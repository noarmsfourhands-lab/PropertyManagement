# Design notes

Why the code is shaped the way it is, and where each requirement is met.

## Layering

Four projects, with every dependency arrow pointing inward.

```
Web  ──▶  Infrastructure  ──▶  Domain
 └──────────────────────────────▶
Tests ────────────────────────▶ Domain
```

The domain project references no framework at all. That is not architecture for its own sake: it is
what allows the business rules to be unit tested as plain function calls, with no database, no HTTP
context and no test doubles. The suite runs in milliseconds and needs no SQL Server.

## Business rules live in one place

Every rule the assessment states in prose has a named home in `PropertyManagement.Domain.Rules`:

| Rule | Type |
| --- | --- |
| Which status transitions exist and who may cause them | `ApplicationWorkflow` |
| Twelve-month lease arithmetic and unit availability | `LeaseTerm` |
| Inactive unit types cannot be selected for other units | `UnitTypeSelection` |
| Comment required for Return and Deny, outcome to status | `ReviewRules` |
| Section order for the single-page wizard | `ApplicationWizard` |

Controllers call these guards before mutating anything, so a forged request is rejected on the
server regardless of what the UI offered. The same predicates decide whether a section renders
editable or read-only, which means the screen and the server can never disagree about permissions.

### Rules take the date, they do not read the clock

`LeaseTerm.HasActiveLease` takes the date to evaluate rather than calling `DateTime.Today`. The web
layer supplies "now" from an injected `TimeProvider`. Tests then assert on real boundaries, such as
the last day of a term, without freezing the system clock or waiting for midnight.

### Rules return a result, they do not throw

A rejected action is an expected outcome, not an exceptional one. `DomainResult` carries a success
flag and a message, so a controller turns a rejection into a validation message on the form. The one
place that does throw is `ReviewRules.ResultingStatus` when handed an outcome outside the enum,
because that is a programming error rather than a user mistake.

## Model decisions

**Applicant information is an owned type.** It maps to columns on the application's own table, so
there is no extra join or nullable foreign key, while staying a distinct object that a section view
model and a section partial can bind to.

**A lease end date is inclusive.** A twelve-month lease starting 2026-01-01 ends 2026-12-31, so
`EndDateFor` is `start.AddMonths(12).AddDays(-1)`. Availability is then the plain reading of the
requirement: a unit whose lease term covers today is not available. A leap-day start clamps the way
`AddMonths` clamps, and there is a test pinning that behaviour so it cannot change silently.

**A second lease is impossible at the database level.** The service checks availability at submit
and again at approval, but two approvals racing each other would both pass a check-then-write. The
unique index on `Leases.RentalApplicationId` is the guard that actually holds, and the approval path
treats a unique-violation as the same rejection the check would have produced. Other open
applications for the unit are deliberately left alone, as the requirements ask.

**Unit types deactivate, they never delete.** A unit already carrying a retired type keeps a valid
reference, and `UnitTypeSelection.SelectableFor` is what builds the dropdown: every active type,
plus the unit's own type when that type has since been retired.

**The audit trail stores the actor's name, not only their id.** A history entry records what was
true when the action happened, so a renamed or removed account does not rewrite the past. The id is
kept alongside it for anything that needs to resolve the current user.

**Per-section concurrency tokens.** SQL Server allows one `rowversion` per table, and a single
token would make any two concurrent saves collide. Applicant information and residence history each
carry their own GUID token marked as a concurrency token. Two applicants saving different sections
do not interfere; a second save of the same stale section is rejected with a message to reload
rather than silently overwriting. This is the bonus-five requirement, and the model supports it
whether or not the multi-applicant UI is finished.

**Indexes follow the queries the requirements name.** The application list filters by status and by
property, and the property is reached through the unit, so there are indexes on `Status` and on
`(UnitId, Status)`, and the applicant join is indexed by user id for "show me my applications".
Availability is answered by "does any lease for this unit cover today", so the lease index leads
with the unit and carries the term. All of this filtering happens in the database.

**Delete behaviour avoids multiple cascade paths.** Units and applications restrict rather than
cascade, because SQL Server rejects a schema where the same row can be reached by two cascade paths.
The collections genuinely owned by an application, its residences, events, notes and applicant
links, do cascade.

## Security posture

Every controller is authenticated by default through a global `AuthorizeFilter`; a public page opts
out explicitly with `[AllowAnonymous]`. That way a new controller is safe on the day it is written
rather than on the day someone remembers to decorate it. Role checks go through named policies
rather than role strings scattered across attributes.

Manager-only notes are a separate entity that no applicant-facing view model or endpoint projects,
so they cannot leak by accident through a shared DTO.

## Seeding

Seeding runs at start-up after migrations are applied. Each step looks for what it is about to
create and skips the work when the rows already exist, so restarting is safe. Bogus runs from a
fixed seed, so a rebuilt database comes back with the same names, rents and addresses, which makes
the app easier to demonstrate and review. Approved applications get a lease starting last month so
their unit reads as unavailable today, which exercises the availability rule on a fresh database.

## Requirements coverage

| Requirement | Where | Status |
| --- | --- | --- |
| ASP.NET Core MVC with Razor, .NET 10 | Solution targets `net10.0` | Done |
| EF Core code-first, SQL Server | `PropertyManagementDbContext`, `Persistence/Migrations` | Done |
| Migrations applied and database created on start | `Program.cs` start-up scope | Done |
| Idempotent seeding with Bogus, every status | `DatabaseSeeder` | Done |
| ASP.NET Identity for users and roles | `DependencyInjection.AddInfrastructure` | Done |
| Unit tests for business logic | `tests/PropertyManagement.Domain.Tests`, 103 tests | Done |
| Status lifecycle, terminal statuses, permissions | `ApplicationWorkflow` | Done |
| Twelve-month lease on approval, availability | `LeaseTerm`, `Leases` unique index | Done |
| Inactive unit type enforced on the server | `UnitTypeSelection` | Done |
| Review outcome and comment requirement | `ReviewRules` | Done |
| Section order for the single-page application | `ApplicationWizard` | Done |
| Sign up, log in, log out with role choice | `AccountController` and its views | To build |
| Properties and units maintained through modals | `PropertiesController`, `UnitsController` | To build |
| Partial views and view components | `Views/Shared`, `ViewComponents` | To build |
| Modal validation re-render in place | Controller actions returning the same partial | To build |
| Single-page application wizard, one form, one action | `RentalApplicationsController` | To build |
| Residence add, edit, remove through a modal | `ResidencesController` | To build |
| Review modal and status history panel | `ReviewController`, history view component | To build |
| Application list filtered in the database | `ApplicationQuery` over `IQueryable` | To build |

### Bonus items

The model already carries what these need, so they are additions to the UI rather than changes to
the schema.

| Bonus | Model support |
| --- | --- |
| 1. Paging, sorting, reusable grid driven by a JSON endpoint | Indexes and `IQueryable` composition are in place; `Microsoft.AspNetCore.OpenApi` is referenced |
| 2. Review queue with claim and release | `ClaimedByUserId`, `ClaimedAtUtc`, `UnderReview` status, `CanClaim` and `CanRelease` |
| 3. Manager-only notes | `PropertyManagerNote` |
| 4. Save a section that fails validation | Section save timestamps are separate from validity |
| 5. Multiple applicants and stale-save rejection | `RentalApplicationApplicant`, per-section concurrency tokens |

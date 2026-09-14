# Design notes

Why the code is shaped the way it is, and where each requirement is met.

## Layering

Four projects, with every dependency arrow pointing inward.

```
Web  ──▶  Infrastructure  ──▶  Domain
 └──────────────────────────────▶
Tests ────────────────────────▶ both
```

The domain project references no framework at all. That is not architecture for its own sake: it is
what allows the business rules to be unit tested as plain function calls, with no database, no HTTP
context and no test doubles.

## Business rules live in one place

Every rule the assessment states in prose has a named home in `PropertyManagement.Domain.Rules`:

| Rule | Type |
| --- | --- |
| Which status transitions exist and who may cause them | `ApplicationWorkflow` |
| Twelve-month lease arithmetic and unit availability | `LeaseTerm` |
| Inactive unit types cannot be selected for other units | `UnitTypeSelection` |
| Comment required for Return and Deny, outcome to status | `ReviewRules` |
| Section order for the single-page wizard | `ApplicationWizard` |
| Residence dates, and how much history is enough | `ResidenceRules` |

Services call these guards before mutating anything, so a request that forges a status, an owner or
a section is rejected whatever the page offered. The same predicates decide whether a section
renders editable or read-only, which means the screen and the server can never disagree.

### Rules take the date, they do not read the clock

`LeaseTerm.HasActiveLease` takes the date to evaluate rather than calling `DateTime.Today`. The web
layer supplies "now" from an injected `TimeProvider`. Tests then assert on real boundaries, such as
the last day of a term, without freezing the system clock or waiting for midnight.

### Rules return a result, they do not throw

A rejected action is an expected outcome, not an exceptional one. `DomainResult` carries a success
flag and a message, so a controller turns a rejection into a validation message on the form. The one
place that does throw is `ReviewRules.ResultingStatus` when handed an outcome outside the enum,
because that is a programming error rather than a user mistake.

## The single-page application

Requirement 4b asks for one page that shows one section at a time, one view model, one form and one
action, with the button pressed deciding what happens. That is exactly what
`RentalApplicationsController.Section` is:

| Button | What happens |
| --- | --- |
| Continue | Validates the current section, saves it only if valid, then moves to the next one or the Summary |
| Back | Redirects to the previous section, which discards the post without saving or validating |
| Submit | Honoured only from the Summary, and only once both sections have been saved |

Three details make it work:

**Only the section on screen is validated.** The view model carries both sections, but a post only
contains the one being edited, so the other's required fields would otherwise fail on values the
user was never shown. `ValidateOnlyCurrentSection` drops the model-state entries that do not belong
to the section being posted. Each section declares its rules once, on its own view model, so an
error always lands on the field it belongs to.

**Everything displayed is rebuilt from storage.** The status, the unit, the residences, whether the
page is editable and whether Submit may appear are all set by `Rehydrate` on every request. None of
it is read back from hidden fields, so a crafted post cannot grant itself an edit by claiming one.

**One partial renders a section twice.** `SectionIsEditable` is `CanEdit && CurrentSection is not
Summary`. The Summary therefore renders the very same section partials as read-only text without
asking for a different mode, which is why the editable and read-only views cannot drift apart.

## The modal contract

Requirement 1b asks for modals populated from partial views, re-rendering in place on a validation
failure and refreshing only the affected region on success. That is one small contract between
`ModalController` and `wwwroot/js/modal-forms.js`:

| Direction | Request | Response |
| --- | --- | --- |
| Opening | `GET` the modal action | the partial view for the modal body |
| Rejected | `POST` the form | `422` and the **same** partial, carrying the messages |
| Accepted | `POST` the form | `200` and `{ refreshUrl, target, message }` |

Returning the identical partial from both the GET and the failed POST is the part that matters:
there is no second copy of the form to keep in step. The success payload names a region rather than
reloading the page, so adding a unit updates the unit table and leaves the rest of the screen
alone. A null target means the page reloads, which is what a status change actually needs.

A page opts an element in with `data-modal-url` and a form with `data-modal-form`. Nothing else
needs to know the mechanism exists, which is what keeps it from becoming a front-end framework.

## Partial views and view components

Both are required, and they are used for different jobs.

A **partial view** renders a model it is handed: the section partials, the residence list, every
modal body. A **view component** has work of its own to do.

- `AvailableUnitsViewComponent` resolves today's date, asks the database which units are free, and
  decides how many to show. It can be dropped onto any page without that page's controller knowing
  anything about units.
- `ApplicationHistoryViewComponent` runs its own query and makes its own access decision: an
  applicant gets nothing at all, not an empty panel. Putting the role check inside the component
  means it cannot be added to a page that forgets to guard it.

## Model decisions

**Applicant information is an owned type.** It maps to columns on the application's own table, so
there is no extra join or nullable foreign key, while staying a distinct object that a section view
model and a section partial can bind to.

**A lease end date is inclusive.** A twelve-month lease starting 2026-01-01 ends 2026-12-31, so
`EndDateFor` is `start.AddMonths(12).AddDays(-1)`. Availability is then the plain reading of the
requirement: a unit whose lease term covers today is not available. A leap-day start clamps the way
`AddMonths` clamps, and there is a test pinning that behaviour so it cannot change silently.

**A second lease is impossible at the database level.** Availability is checked at submit and again
at approval, but two approvals racing each other would both pass a check-then-write. The unique
index on `Leases.RentalApplicationId` is the guard that actually holds, and the approval path
treats a unique violation as the same rejection the check would have produced. Other open
applications for the unit are deliberately left alone, as the requirements ask, and a test asserts
that the losing application is still exactly where it was.

**Unit types deactivate, they never delete.** A unit already carrying a retired type keeps a valid
reference, and `UnitTypeSelection.SelectableFor` builds the dropdown: every active type, plus the
unit's own type when that type has since been retired.

**Timestamps are UTC `DateTime`, not `DateTimeOffset`.** Every instant in this model comes from an
injected `TimeProvider` and is UTC by construction, so an offset column would store a permanently
zero offset. The usual objection is that a `DateTime` loses its kind on the way back out of the
database, so `UtcDateTimeConverter` is applied by convention to every `DateTime` in the model: a
value cannot be written without being normalised or read without its kind restored. A test asserts
the kind survives a round trip.

**The audit trail stores the actor's name, not only their id.** A history entry records what was
true when the action happened, so a renamed or removed account does not rewrite the past. The name
travels in the sign-in cookie via `ApplicationUserClaimsPrincipalFactory`, so writing an entry
costs no extra query.

**Per-section concurrency tokens.** SQL Server allows one `rowversion` per table, and a single
token would make any two concurrent saves collide. Applicant information and residence history each
carry their own GUID token marked as a concurrency token, and a save passes the version the page was
rendered with. Two applicants saving different sections do not interfere; a second save of the same
stale section is rejected with a message to reload rather than silently overwriting. Both halves of
that are tested.

**Indexes follow the queries the requirements name.** The application list filters by status and by
property, and the property is reached through the unit, so there are indexes on `Status` and on
`(UnitId, Status)`, and the applicant join is indexed by user id. Availability is answered by "does
any lease for this unit cover today", so the lease index leads with the unit and carries the term.

**Delete behaviour avoids multiple cascade paths.** Units and applications restrict rather than
cascade, because SQL Server rejects a schema where the same row can be reached by two cascade paths.
The collections genuinely owned by an application do cascade.

## Security posture

Every controller is authenticated by default through a global `AuthorizeFilter`; a public page opts
out explicitly with `[AllowAnonymous]`. A new controller is therefore safe on the day it is written
rather than on the day someone remembers to decorate it. Role checks go through named policies.

Beyond that:

- The role chosen at sign-up is checked against the known roles rather than trusted, so a crafted
  post cannot claim one that does not exist.
- The post-sign-in redirect only ever goes somewhere local, so a crafted link cannot bounce a
  freshly signed-in user elsewhere.
- Sign-in failures say only that the pair did not match, because naming which half was wrong tells
  an attacker which addresses exist.
- A residence is always looked up through its application, so guessing an id reaches nothing.
- Manager-only notes are a separate entity that no applicant-facing view model or endpoint
  projects, so they cannot leak through a shared DTO.

## Seeding

Seeding runs at start-up after migrations are applied. Each step looks for what it is about to
create and skips the work when the rows already exist, so restarting is safe. Bogus runs from a
fixed seed, so a rebuilt database comes back identical, which makes the app easier to demonstrate.
Approved applications get a lease starting last month so their unit reads as unavailable today.

Migrating and seeding are separate methods. They are different concerns, and the split is what lets
the idempotency be tested against a schema created some other way.

## Testing

Two suites, because they answer different questions.

| Suite | What it proves | Count |
| --- | --- | --- |
| `PropertyManagement.Domain.Tests` | The business rules are right, as plain function calls | 114 |
| `PropertyManagement.Infrastructure.Tests` | The model, queries and services work against a real relational database | 39 |

The integration suite runs on SQLite held in memory, which enforces keys, unique indexes and
optimistic concurrency the way a server does while needing nothing installed. It is what proves the
schema builds, the queries translate, the concurrency tokens behave as configured, and the seeder is
idempotent. The application itself runs on SQL Server, as required.

## Requirements coverage

| Requirement | Where | Status |
| --- | --- | --- |
| ASP.NET Core MVC with Razor, .NET 10 | Solution targets `net10.0` | Done |
| EF Core code-first, SQL Server | `PropertyManagementDbContext`, `Persistence/Migrations` | Done |
| Migrations applied and database created on start | `Program.cs` start-up scope | Done |
| Idempotent seeding with Bogus, every status | `DatabaseSeeder` | Done |
| ASP.NET Identity for users and roles | `DependencyInjection.AddInfrastructure` | Done |
| Unit tests for business logic | 153 tests across two suites | Done |
| Sign up, log in, log out with role choice | `AccountController` | Done |
| Properties and units maintained through modals | `PropertiesController` | Done |
| Partial views and view components | Section and modal partials; two view components | Done |
| Modal validation re-renders in place | `ModalController`, `wwwroot/js/modal-forms.js` | Done |
| Inactive unit type enforced on the server | `UnitTypeSelection`, `PropertyService.SaveUnitAsync` | Done |
| Browse available units and start an application | `AvailableUnitsViewComponent`, `Start` | Done |
| Single page, one section at a time, one form, one action | `RentalApplicationsController.Section` | Done |
| Continue validates and saves; Back does not | Same action, `WizardCommand` | Done |
| Sections render editable or read-only server-side | `SectionIsEditable` | Done |
| Residences added, edited, removed through a modal | `ResidenceForm`, `SaveResidence`, `DeleteResidence` | Done |
| Submit blocked until both sections saved | `ApplicationWorkflow.CanSubmit` | Done |
| Active lease blocks submit and approval | `LeaseTerm`, `Leases` unique index | Done |
| Withdraw, and correct and resubmit a returned application | `WithdrawAsync`, `SubmitAsync` | Done |
| Review modal with outcome and required comment | `ReviewController`, `ReviewRules` | Done |
| Twelve-month lease issued on approval | `LeaseTerm.Issue` | Done |
| Status history shown to property managers | `ApplicationHistoryViewComponent` | Done |
| List filtered by status and property in the database | `RentalApplicationService.ListAsync` | Done |
| Applicants see their own, managers see all | Same, via the applicant join | Done |

### Bonus items

| Bonus | State |
| --- | --- |
| 1. Paging and sorting in the database | Paging done; the reusable grid over a documented JSON endpoint is not built |
| 2. Review queue with claim and release | Done |
| 3. Manager-only notes | Modelled, no UI |
| 4. Save a section that fails validation | Not done; Continue saves only a valid section, as the main requirement asks |
| 5. Multiple applicants, stale saves rejected | Ownership is a set, and the stale-save rejection works and is tested; there is no UI for inviting a second applicant |

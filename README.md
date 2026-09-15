# Property Management — Rental Applications

A full-stack ASP.NET Core MVC application for a property management company. Property managers
maintain properties and units and review rental applications; applicants browse available units,
apply, and are issued a twelve-month lease when approved.

Built with .NET 10, ASP.NET Core MVC with Razor, Entity Framework Core code-first against SQL
Server, and ASP.NET Identity. No single-page application framework is used.

All five optional items are implemented as well: paging and sorting in the database behind a
reusable grid component over an OpenAPI-documented JSON endpoint, a review queue where a manager
claims an application before deciding it, manager-only notes, saving a section that is still
incomplete with the summary listing what blocks submission, and more than one applicant on an
application with per-section concurrency. [DESIGN.md](DESIGN.md) maps every requirement to where it
is met and explains the reasoning.

## Running it with Docker

The quickest way to see it working, and the only one that needs nothing installed but Docker:

```bash
docker compose up
```

That brings up SQL Server and the application together, waits for the database to accept
connections, then creates it, migrates it and seeds it. The application is at
<http://localhost:8080>; sign in with any of the [seeded accounts](#seeded-accounts) below.

The database lives in a named volume, so it survives `docker compose down`. Use
`docker compose down -v` to throw it away and start from a fresh seed. SQL Server is published on
1433 as well, if you would rather look at the data with a client.

Everything below describes running it directly instead, against an instance of your own.

## Prerequisites

| Requirement | Version used here |
| --- | --- |
| .NET SDK | 10.0.103 |
| SQL Server | SQL Server Express, LocalDB, or a full instance |
| `dotnet-ef` CLI | 10.0.x |

### SQL Server is not optional

The application refuses to start without a reachable SQL Server instance, and says so plainly in
the log rather than printing a provider stack trace. If you do not already have one:

```bash
winget install --id Microsoft.SQLServer.2025.Express --accept-package-agreements
```

That installs the `SQLEXPRESS` instance the default connection string points at. Confirm it is
running before starting the application:

```bash
powershell -Command "Get-Service 'MSSQL$SQLEXPRESS'"
```

LocalDB works equally well if you already have it, for instance from the Visual Studio *Data
storage and processing* workload. Point the connection string at
`Server=(localdb)\MSSQLLocalDB;...` instead.

Install the EF Core tooling if it is not already present:

```bash
dotnet tool install --global dotnet-ef
```

If you already have it at an older version, update it so it matches the 10.0.12 runtime and the
version warning goes away:

```bash
dotnet tool update --global dotnet-ef
```

## Database setup

The connection string lives under `ConnectionStrings:DefaultConnection` in
`src/PropertyManagement.Web/appsettings.json` and defaults to SQL Server Express:

```
Server=localhost\SQLEXPRESS;Database=PropertyManagement;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True
```

Point it at whichever instance you have. For LocalDB:

```
Server=(localdb)\MSSQLLocalDB;Database=PropertyManagement;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True
```

Rather than editing the checked-in file, prefer a local override that git ignores:

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<your connection string>" --project src/PropertyManagement.Web
```

You do not need to create the database or run migrations by hand. On start-up the application
applies any pending migrations and seeds the database, so a first run against an empty server
produces a working system.

## Running

```bash
dotnet run --project src/PropertyManagement.Web
```

That serves <http://localhost:5204>. Add `--launch-profile https` for <https://localhost:7214>
instead. The first run creates the database, applies the migrations and seeds it, so there is
nothing to do beforehand but have SQL Server reachable.

## Seeded accounts

Seeding is idempotent and runs from a fixed random seed, so restarting never duplicates data and a
rebuilt database comes back identical. Every seeded account uses the same password.

| Role | Sign in as | Password |
| --- | --- | --- |
| Property manager | `manager1@example.com`, `manager2@example.com` | `Passw0rd!` |
| Applicant | `applicant1@example.com` through `applicant6@example.com` | `Passw0rd!` |

The seed also creates unit-type lookups including one deactivated type, three properties with their
units, and applications in every status so each screen has something to show. Volume, the random
seed, and the demo password are all configurable under the `Seeding` section of `appsettings.json`;
`Seeding:Enabled` controls whether any of it is written. It is off in `appsettings.json` and on in
`appsettings.Development.json`, so running locally gets a full database and any other environment
gets an empty one unless someone deliberately asks otherwise.

Migrations are applied at start-up. `Database:MigrateOnStartup` turns that off, for a deployment
that runs migrations as its own step rather than letting several replicas race the same one.

## Tests

```bash
dotnet test
```

269 tests across three suites, none of which needs SQL Server:

| Suite | What it covers |
| --- | --- |
| `PropertyManagement.Domain.Tests` | The business rules, as plain function calls over domain objects |
| `PropertyManagement.Infrastructure.Tests` | The model, the queries and the services, against SQLite held in memory |
| `PropertyManagement.Web.Tests` | Section validation, what the application page offers, and the whole pipeline over HTTP |

The second suite proves the schema actually builds, the queries translate, the per-section
concurrency tokens behave as configured, and the seeder is safe to run twice. SQLite is used there
only because it enforces the same constraints without needing an installation; the application
itself runs on SQL Server.

The third suite ends with a set of tests that boot the real application in process, sign in through
the real login form, and make real requests. A service refusing an operation is not the same as the
route refusing it, and those are the tests that can tell the difference: one applicant asking for
another's application gets 404 rather than 403, because a 403 would confirm the id exists.

## Project structure

| Project | Holds |
| --- | --- |
| `src/PropertyManagement.Domain` | Entities, enums, and the business rules. No framework dependencies. |
| `src/PropertyManagement.Application` | The service contracts and the records they take and return. Depends on the domain and nothing else. |
| `src/PropertyManagement.Infrastructure` | EF Core context and mappings, migrations, Identity, seeding, and the service implementations. |
| `src/PropertyManagement.Web` | Controllers, view models, Razor views, partial views, view components. |
| `tests/PropertyManagement.Domain.Tests` | Unit tests for the business rules. |
| `tests/PropertyManagement.Infrastructure.Tests` | Model, query, service and seeding tests against a real database. |
| `tests/PropertyManagement.Web.Tests` | Validation, presentation decisions, and end-to-end requests through the real pipeline. |

The dependency arrows all point inward: the domain depends on nothing, the application layer on the
domain, and infrastructure on both. Controllers and view models name only the application layer, so
nothing outside infrastructure knows the data is in Entity Framework at all. The web project still
references infrastructure, in one place: `Program.cs`, where the implementations are registered.
Swapping a service for another implementation is a change to that file and to nothing else.

## Trying it out

Signed in as a **property manager** you can maintain properties and their units through modals and
work the review queue from the dashboard. Open a submitted application and the only action offered
is *Claim for review*: claiming is required before a decision, so an application under review has
exactly one manager and two people cannot both reach the outcome form. Once claimed you can record
an outcome, or release it back to the queue. Any manager can release a claim, including someone
else's, so nothing is stuck when a colleague is unavailable; the history records which it was.
Approving issues a twelve-month lease, which immediately takes the unit out of the available list.

Editing a unit that applications already point at asks you to confirm first, and tells you how many
there are and what status they are in, because an application reads its rent and type from the unit
as it stands.

Signed in as an **applicant** the home page lists the units available today. Applying opens the
application: one page, one section at a time, with residences managed through a modal and a summary
that only offers Submit once both sections have been saved. An application that comes back Returned
becomes editable again so it can be corrected and resubmitted.

## Design notes

See [DESIGN.md](DESIGN.md) for the reasoning behind the model, how the single-page application and
the modal contract work, how each requirement is met, and the trade-offs behind the decisions that
could reasonably have gone the other way.

While the application is running in Development the JSON endpoint behind the grid describes itself
at <http://localhost:5204/openapi/v1.json>.

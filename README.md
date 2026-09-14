# Property Management — Rental Applications

A full-stack ASP.NET Core MVC application for a property management company. Property managers
maintain properties and units and review rental applications; applicants browse available units,
apply, and are issued a twelve-month lease when approved.

Built with .NET 10, ASP.NET Core MVC with Razor, Entity Framework Core code-first against SQL
Server, and ASP.NET Identity. No single-page application framework is used.

## Prerequisites

| Requirement | Version used here |
| --- | --- |
| .NET SDK | 10.0.103 |
| SQL Server | SQL Server Express, LocalDB, or a full instance |
| `dotnet-ef` CLI | 10.0.x |

### SQL Server is not optional

The application refuses to start without a reachable SQL Server instance, and says so plainly in
the log rather than printing a provider stack trace. If no instance is installed yet, either will do:

- **SQL Server Express** from the Microsoft download page. During setup, include the **LocalDB**
  feature to get the `(localdb)\MSSQLLocalDB` instance the default connection string points at.
- **LocalDB on its own**, which ships with the SQL Server Express installer and with the Visual
  Studio *Data storage and processing* workload.

Confirm the instance is there before running:

```bash
sqllocaldb info
```

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
`src/PropertyManagement.Web/appsettings.json` and defaults to SQL Server LocalDB:

```
Server=(localdb)\MSSQLLocalDB;Database=PropertyManagement;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True
```

Point it at whichever instance you have. For SQL Server Express:

```
Server=localhost\SQLEXPRESS;Database=PropertyManagement;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True
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
set `Seeding:Enabled` to `false` to apply migrations without writing demo data.

## Tests

```bash
dotnet test
```

153 tests across two suites, neither of which needs SQL Server:

| Suite | What it covers |
| --- | --- |
| `PropertyManagement.Domain.Tests` | The business rules, as plain function calls over domain objects |
| `PropertyManagement.Infrastructure.Tests` | The model, the queries and the services, against SQLite held in memory |

The second suite is what proves the schema actually builds, the queries translate, the per-section
concurrency tokens behave as configured, and the seeder is safe to run twice. SQLite is used there
only because it enforces the same constraints without needing an installation; the application
itself runs on SQL Server.

## Project structure

| Project | Holds |
| --- | --- |
| `src/PropertyManagement.Domain` | Entities, enums, and the business rules. No framework dependencies. |
| `src/PropertyManagement.Infrastructure` | EF Core context and mappings, migrations, Identity, seeding, services. |
| `src/PropertyManagement.Web` | Controllers, view models, Razor views, partial views, view components. |
| `tests/PropertyManagement.Domain.Tests` | Unit tests for the business rules. |
| `tests/PropertyManagement.Infrastructure.Tests` | Model, query, service and seeding tests against a real database. |

The dependency arrows all point inward: the web project depends on infrastructure and domain,
infrastructure depends on domain, and the domain depends on nothing. That is what lets the rules be
tested directly.

## Trying it out

Signed in as a **property manager** you can maintain properties and their units through modals,
open any application, claim it for review, and record an outcome. Approving issues a twelve-month
lease, which immediately takes the unit out of the available list.

Signed in as an **applicant** the home page lists the units available today. Applying opens the
application: one page, one section at a time, with residences managed through a modal and a summary
that only offers Submit once both sections have been saved. An application that comes back Returned
becomes editable again so it can be corrected and resubmitted.

## Design notes

See [DESIGN.md](DESIGN.md) for the reasoning behind the model, how the single-page application and
the modal contract work, how each requirement is met, and which bonus items were and were not
attempted.

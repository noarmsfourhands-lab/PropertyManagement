# Design notes

Why the code is shaped the way it is, and where each requirement is met.

## Layering

Four projects, with every dependency arrow pointing inward.

```
Web  ──▶  Application  ──▶  Domain
 └──▶  Infrastructure  ──────┘
          (registration only)
```

The domain project references no framework at all. That is not architecture for its own sake: it is
what allows the business rules to be unit tested as plain function calls, with no database, no HTTP
context and no test doubles.

The application project holds the service contracts and the records they take and return, and
references the domain and nothing else: no Entity Framework, no ASP.NET Core. That separation exists
for one reason. A controller or a view model that names `IRentalApplicationService` should not
thereby be naming the project that knows the data is in a relational database, because once it does,
replacing that implementation stops being a change of one line and becomes a change everywhere the
type appears.

The web project does still reference infrastructure, and it is worth being exact about where. The
composition root in `Program.cs` registers the implementations against the contracts, which is what
a composition root is for. Two other files name it, both for ASP.NET Identity: the account
controller, which needs `UserManager` and `SignInManager`, and the extension that reads the display
name claim. Identity is infrastructure that the web layer has to touch to sign anybody in, so that
is a deliberate exception rather than a leak. No controller, view model or view outside those three
names it, which is the property that matters: nothing that renders or validates knows the data is
in Entity Framework.

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

### A seventh status

Requirement 5b names six statuses: Draft, Submitted, Returned, Approved, Denied and Withdrawn.
There is a seventh, `UnderReview`, and it exists only because bonus two asks for a review queue in
which "a property manager claims a submitted application (Under Review) before completing it".

It behaves as a sub-state of Submitted rather than a new stage: an application enters it only by
being claimed, returns to Submitted when released, and is reviewable from either. Every transition
the six-status lifecycle allows from Submitted is allowed from Under Review too. Dropping bonus two
would mean deleting the value and the two transitions that reach it, and nothing else.

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

### The wizard owns the page, not the rows inside it

Residences are their own controller. The wizard owns the page and its sections; `ResidencesController`
owns the rows in one of them, each reached by its own id and edited through the modal contract rather
than the page's single form. Splitting them left the wizard controller at about 440 lines instead of
650, and gave the residence routes names that say what they are: `/Residences/Form` rather than
`/RentalApplications/ResidenceForm`.

Both controllers have to answer the same two questions before doing anything, so they ask one shared
`ApplicationContextFactory` rather than each keeping a copy of the rule. The order of those questions
is the security-relevant part and is the same in both: whether the person may view it is settled
first, so somebody who may not gets "not found" rather than a "forbidden" that confirms the id.

That split is exactly the kind of change Razor fails silently: a partial that moved folders or a
`Url.Action` missing its controller name still compiles and still renders, and only breaks when
somebody clicks. `ResidenceRoutingTests` reads the links the wizard actually renders and then walks
them, so that failure is a red test rather than a broken modal in a demo.

## Partial views and view components

Both are required, and they are used for different jobs.

A **partial view** renders a model it is handed: the section partials, the residence list, every
modal body. A **view component** has work of its own to do.

- `AvailableUnitsViewComponent` resolves today's date, asks the database which units are free, and
  decides how many to show. It can be dropped onto any page without that page's controller knowing
  anything about units.
- `ApplicationHistoryViewComponent` and `ApplicationNotesViewComponent` each run their own query
  and make their own access decision: an applicant gets nothing at all, not an empty panel. Putting
  the check inside the component means it cannot be added to a page that forgets to guard it.
- `ApplicationApplicantsViewComponent` decides three ways rather than two, offering the controls
  only to an applicant on an application that can still be edited.
- `DataGridViewComponent` is the reusable one: it is handed columns and an endpoint and knows
  nothing about what it is listing.

## Model decisions

**Applicant information is an owned type.** It maps to columns on the application's own table, so
there is no extra join or nullable foreign key, while staying a distinct object that a section view
model and a section partial can bind to.

**A lease end date is inclusive.** A twelve-month lease starting 2026-01-01 ends 2026-12-31, so
`EndDateFor` is `start.AddMonths(12).AddDays(-1)`. Availability is then the plain reading of the
requirement: a unit whose lease term covers today is not available. A leap-day start clamps the way
`AddMonths` clamps, and there is a test pinning that behaviour so it cannot change silently.

**A second lease for a unit is impossible at the database level.** Availability is checked at
submit and again at approval, but two approvals racing each other would both read the unit's leases
before either wrote, so both would pass. The guard that actually holds is a unique index on
`Leases(UnitId, StartDate)`.

It closes the race exactly because approval dates the lease the day it is granted. Two approvals on
the same day for one unit collide on the index, and the loser is told the same thing the
availability check would have told them. An approval on a later day is stopped by the availability
check instead, because the first lease still covers that day. The two together leave no gap, and
the pairing is worth stating plainly: the index alone would not stop overlapping terms starting on
different days, and nothing in the system can produce those.

The violation is recognised by SQL Server's error number rather than its message, because those
messages are localised. Reading them would work on an English server and quietly stop working on
any other, turning a handled race into an unhandled error.

Other open applications for the unit are deliberately left alone, as the requirements ask, and a
test asserts that the losing application is still exactly where it was.

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

**Per-section concurrency tokens, and what they do not cover.** SQL Server allows one `rowversion`
per table, and a single token would make any two concurrent saves collide. Applicant information
and residence history each carry their own GUID token, and a save passes back the version the page
was rendered with, so a save built on a stale copy of a section changes nothing and says so rather
than overwriting the other person's work. Both halves of that are tested.

Two honest limits. EF puts every concurrency token on an entity into the `WHERE` clause of any
update to it, so the isolation between the two sections holds because each service method re-reads
the row immediately before writing, not because EF has been told to check only one. And the tokens
rotate only when a section is saved: the other writes to an application, such as claiming or
deciding it, leave them untouched, so those paths are last-write-wins. That is tolerable because
each is already guarded by a status check that a second actor fails, but it is a narrower promise
than "the row is protected", and worth saying so rather than implying otherwise.

Residences needed both halves, and getting there took a wrong turn worth recording.
Making a residence row move `ResidenceHistoryVersion` sounds like closing a gap: the rows are part
of the section, so changing one should invalidate a save built before it. In practice the residence
modal is opened from the page that holds that token, so an applicant adding a residence invalidated
their own page, and the very next Continue came back as "someone else saved this section" with
nobody else involved. It also protected nothing: the section save writes a completion marker, and
the rows are written one request at a time, each against current storage, so no row can be lost to
a stale page. `Adding_a_residence_does_not_block_the_next_section_save` walks the sequence a person
actually performs and is there to stop that being re-introduced.

But reverting left a real gap, which the section token was the wrong tool for anyway: two applicants
editing the *same* residence. Each modal posts every field it was opened with, so the second save
wrote the first one's corrections back out with no warning. The row now carries its own token. That
guards the row without touching the page the modal was opened from, which is exactly the combination
the section token could not provide. Two tests pin both halves: the second editor is refused, and
the page's own token is untouched so their next Continue still works.

**A unit is referenced, not copied, so editing one is confirmed.** A rental application stores which
unit it is for and nothing about that unit. Every screen showing an application reads the rent, type
and bedroom count from the unit as it stands now, so editing a unit rewrites what every application
against it appears to say, including ones already submitted, under review or decided. Deleting was
already refused outright when applications or leases exist; editing was not, and had no reason to be
refused, because changing a rent is a legitimate thing to do.

The edit form therefore states what it will affect: how many applications reference the unit, broken
down by status, and whether a lease exists. A manager ticks to say they have read it. The check runs
on the server as well as in the page, because the warning and the checkbox are both rendered from
that count, and a post that simply omitted them would otherwise be the one path with no warning at
all. A lease is called out separately as unaffected: `Lease.MonthlyRent` is copied when the lease is
issued, so it keeps the rent it was issued at.

The alternative was to copy the unit's details onto the application at submission. That is the right
answer for a system that has to reproduce exactly what someone applied for, and it is a larger change
than this one: it adds columns that can drift from the unit, and a decision about which of the two a
manager is looking at on every screen. The confirmation is the smaller, honest version.

**Four rules the database keeps, not just the code.** Each of these was enforced somewhere in a
service and nowhere else, which is fine until a second path appears that forgets.

- **The two user ids that carry authority have real foreign keys.** Whether somebody may open an
  application is decided by whether their id is in its applicant set, and whether they may review one
  by whether they hold its claim. An id left pointing at a deleted account would leave an application
  nobody could open, edit or repair, because the primary applicant cannot be removed either. Restrict
  refuses the deletion instead. The audit columns beside them deliberately have no key: each stores a
  name next to the id, so it keeps reading correctly after the account is gone.
- **The audit trail and manager notes no longer cascade.** They are records *about* an application
  rather than parts of one. Cascading meant a future delete would take the history with it for some
  statuses and fail for others, because an approved application is already held back by its lease.
  Restrict is one answer for every case.
- **A submitted application carries its applicant's details, by check constraint.** The rule lived
  only in the submit path, so anything that set a status another way could produce an approved
  application with nobody's name on it. The constraint covers Submitted, Under review, Approved and
  Denied: the four reached only by passing that rule and the four in which nothing can be edited.
  Draft, Returned and Withdrawn are all legitimately incomplete.
- **Records describe themselves.** An application copies the property name and unit number it was
  started for, and a lease copies them when it is issued, the same way it already copied the rent.
  Lists and past decisions read those rather than the unit, so renaming a property no longer rewrites
  what every historical decision appears to say. The wizard still shows the unit's live rent and type,
  because that is the unit somebody is applying for now, and a manager editing it is told what it
  affects.

**Claiming is required, and any manager can release a claim.** These two go together.

Claiming used to be optional: a manager could complete a review straight from Submitted. That made
the queue advisory rather than real. Two managers could open the same submitted application, both
reach the outcome form, both type a comment, and the second to press the button would have their
decision refused by the status check after the work was done. Requiring the claim makes the queue
mean what it says: an application under review has exactly one manager, named on the record, and the
refusal arrives before anyone types anything.

That alone would have been worse than what it replaced, because claiming is only possible from
Submitted. An application claimed by a manager who then leaves, is locked out, or is simply away
would have been stuck for everyone, with no way back. So release is open to any manager. Reviewing
still requires holding the claim, so taking over someone's work is two deliberate steps rather than
one, and the audit entry distinguishes releasing your own claim from taking an application off a
colleague.

**Check-then-write races answer with a sentence.** Deleting a property or unit checks for
applications and then saves; an application started in between still gets in, and the foreign key
refuses the delete. Adding a unit number checks for a duplicate and then saves; a second manager
adding the same number at the same moment is stopped by the unique index. The check produces the good
message almost every time and the constraint is what actually guarantees the rule, so both catch the
constraint and return what the check would have said. `DatabaseErrors` matches on the error number
rather than the message, because SQL Server localises its messages.

**Indexes follow the queries the requirements name.** The application list filters by status and by
property, and the property is reached through the unit, so there are indexes on `Status` and on
`(UnitId, Status)`, and the applicant join is indexed by user id. Availability is answered by "does
any lease for this unit cover today", so the lease index leads with the unit and carries the term.

**The default list ordering is deliberately unindexed.** The list orders by whichever of the
submission or creation time is set, which is a `COALESCE` that no index can serve directly. At this
size a scan is the right answer, and an index over one of the two columns would be worse than none
because it would look like coverage without providing it. If the table ever grew enough to matter,
the fix is a persisted computed column over the same expression with an index on that.

**Delete behaviour avoids multiple cascade paths.** Units and applications restrict rather than
cascade, because SQL Server rejects a schema where the same row can be reached by two cascade paths.
The collections genuinely owned by an application do cascade, including its audit trail. That is
deliberate rather than an oversight: nothing in the application deletes an application, so the trail
outlives everything that can actually happen to one. A system that did delete them would want the
trail kept and the row soft-deleted instead.

## Theming

The look follows troyweb.com. The palette and the two typefaces were read off that site rather
than guessed at: the warm off-white ground, the deep navy, the two oranges, the peach fills and
the fully rounded buttons are the values it actually computes. General Sans is loaded from
Fontshare, which publishes it, rather than from Troy Web's own server, which would be borrowing
their bandwidth.

It is a token layer over Bootstrap, not a fork. Bootstrap 5.3 exposes its design decisions as
`--bs-*` custom properties, so re-pointing those at the palette re-themes every component that
reads them, and upgrading Bootstrap stays a version bump rather than a merge.

One caveat is worth knowing, because it is the thing that catches people out: Bootstrap scopes
some of its variables to the component rather than to `:root`, with the compiled Sass value as the
default. Re-pointing `--bs-primary` therefore does nothing for those. The step indicator on the
application page was the one place it showed, its active pill staying the stock blue, and the fix
is to set `--bs-nav-pills-link-active-bg` where Bootstrap reads it. Every page was then checked
for any remaining stock blue, and there is none.

Application statuses get their own palette rather than Bootstrap's semantic classes. A status is
not a severity: Submitted is not information and Returned is not a warning, so borrowing those
colours would say something the domain does not mean. Every foreground and background pair on the
rendered pages was measured against WCAG AA and passes.

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
- An application someone may not view answers 404, not 403. A 403 confirms the id is real, which is
  the entire thing somebody walking the numbers is trying to learn. Where the person may view it but
  not perform the action, the answer is 403, because they already know it exists and "not found"
  would only be confusing.
- The grid renderer will not put a value from the endpoint into `href` unless it is a path on this
  site, and draws only classes from this application's own status palette. Neither is a hole today,
  because the endpoint is the one in this repository; both stop a response being able to restyle the
  page or retarget a link if that ever stops being true.

## Seeding

Seeding runs at start-up after migrations are applied. Each step looks for what it is about to
create and skips the work when the rows already exist, so restarting is safe. Bogus runs from a
fixed seed, so a rebuilt database comes back identical, which makes the app easier to demonstrate.
Approved applications get a lease starting last month so their unit reads as unavailable today.

Migrating and seeding are separate methods. They are different concerns, and the split is what lets
the idempotency be tested against a schema created some other way.

## Testing

Three suites, because they answer different questions.

| Suite | What it proves | Count |
| --- | --- | --- |
| `PropertyManagement.Domain.Tests` | The business rules are right, as plain function calls | 129 |
| `PropertyManagement.Infrastructure.Tests` | The model, queries and services work against a real relational database | 112 |
| `PropertyManagement.Web.Tests` | Section validation, what the wizard offers, and what the pipeline returns | 45 |

The integration suite runs on SQLite held in memory, which enforces keys, unique indexes and
optimistic concurrency the way a server does while needing nothing installed. It is what proves the
schema builds, the queries translate, the concurrency tokens behave as configured, and the seeder is
idempotent. The application itself runs on SQL Server, as required, and the first run there was
checked by hand: the database was created, the migrations applied, and a second start applied no
migration and created nothing.

What that suite cannot see is worth naming. SQLite has no `nvarchar(max)` and no decimal precision,
so column sizing is invisible to it; its planner says nothing about SQL Server's, so a missing index
is invisible too; `EnsureCreated` builds from the model rather than from the migrations, so
migration drift would not surface there; and because every test context shares one connection, the
read-then-write races are serialised and cannot be reproduced. Those are the first places to look
if something ever behaves differently in production than in the suite.

The web suite ends with twenty tests that boot the real application in process through
`WebApplicationFactory`, sign in through the real login form with a real antiforgery token, and make
real requests. They exist because a service refusing an operation and a route refusing it are
different facts, and only the second one is what a browser meets. They are what pins the disclosure
policy above: one applicant asking for another's application gets 404, an applicant asking for the
review screen on their own application is sent to access-denied, and a post with no antiforgery
token is rejected.

## The grid and its endpoint

Bonus one asks for the list to be extracted into a reusable grid component driven by a documented
JSON endpoint. `DataGridViewComponent` is that component, and it is reusable because it is
ignorant: a column names a property on the returned row, and anything needing a decision, such as a
status label or the colour of its badge, is decided by the endpoint and arrives as a field. Every
value is written with `textContent`, so a field from the endpoint is never treated as markup.

One piece of application knowledge does sit in `data-grid.js`, and it is there deliberately: the
list of status chip classes a badge column is allowed to draw. A class attribute taken verbatim from
a response lets whoever controls that response restyle the page, and the script has no way to know
the response was not tampered with, so it draws only classes this application defines and falls back
to the neutral chip for anything else. A second list using badge columns would need its own classes
added there. The honest trade is a small, named leak of domain knowledge in exchange for a shared
script that cannot be used to inject styling; the alternative, passing an allowlist in through the
column definition, moves the leak rather than removing it.

The component has one consumer today, the applications list. That is the only list in the app with
paging, sorting and filtering to extract; the property and unit tables are short, unfiltered and
hand-rolled, and wrapping them in a grid would add indirection without removing any code.

Sorting goes through an enum rather than a column name taken from the request. A request cannot
therefore order by a column that was never meant to be exposed, and there is no path from the query
string into the shape of the SQL. Ordering, filtering, counting and paging all happen in the
database, with a tiebreak on the id so a row cannot drift between pages when the sort key ties.

The endpoint is documented at `/openapi/v1.json`, taking its descriptions from XML comments on the
action. A signed-out request to anything under `/api` is answered with 401 rather than a redirect
to the sign-in page, because handing a caller expecting JSON a page of HTML and a success status is
worse than telling it plainly.

## Saving work that is not finished

Bonus four asks that a section can be saved while it is still wrong. That sits awkwardly beside
requirement 4b.i, which says Continue persists a section *only* when it is valid. Rather than
choose between them, Continue keeps the behaviour the requirement describes and draft saving is a
second, separate action:

| Button | Saves | Validates | Moves |
| --- | --- | --- | --- |
| Continue | only when valid | yes | to the next section |
| Save for later | always | reports, does not block | stays put |
| Back | never | no | to the previous section |

Both requirements are then met literally, and the two behaviours are told apart by which button was
pressed, which is the mechanism the page already uses.

The rules themselves are defined once. `SectionValidation` evaluates the section view model's own
data annotations with `Validator`, so the attributes that model binding uses when a section is on
screen are the same ones that answer the Summary's question about a section the reader cannot
currently see. Each problem carries the member name it came from, which is turned back into the
model-state key the input is bound to, so a message raised from the Summary lands on the same field
it would have landed on had the section been posted.

Submission is refused in the service and not merely hidden in the page, because a draft save can
leave a section stored but incomplete.

## Notes and shared applications

**Manager-only notes are guarded three times.** The controller sits behind the property manager
policy; the view component makes its own role check and returns nothing at all rather than an empty
panel, so an applicant does not even learn that notes exist; and no applicant-facing view model or
endpoint projects the type. A note therefore cannot leak because someone forgot a guard on a new
page.

**A second applicant needs no new permission code.** Ownership was a set from the first commit, so
every check already asks whether the user is among the applicants rather than whether they are the
applicant. Adding someone to that set is the whole of granting them access, and a test asserts that
the person added can then save a section. Adding is by email and refuses a property manager with
the same message as an unknown address, so it cannot be used to discover which addresses have
accounts. The applicant who started an application cannot be removed, because withdrawing is the
action for giving the whole thing up.

## Requirements coverage

| Requirement | Where | Status |
| --- | --- | --- |
| ASP.NET Core MVC with Razor, .NET 10 | Solution targets `net10.0` | Done |
| EF Core code-first, SQL Server | `PropertyManagementDbContext`, `Persistence/Migrations` | Done |
| Migrations applied and database created on start | `Program.cs` start-up scope | Done |
| Idempotent seeding with Bogus, every status | `DatabaseSeeder` | Done |
| ASP.NET Identity for users and roles | `DependencyInjection.AddInfrastructure` | Done |
| Unit tests for business logic | 286 tests across three suites | Done |
| Sign up, log in, log out with role choice | `AccountController` | Done |
| Properties and units maintained through modals | `PropertiesController` | Done |
| Partial views and view components | Section and modal partials; two view components | Done |
| Modal validation re-renders in place | `ModalController`, `wwwroot/js/modal-forms.js` | Done |
| Inactive unit type enforced on the server | `UnitTypeSelection`, `PropertyService.SaveUnitAsync` | Done |
| Browse available units and start an application | `AvailableUnitsViewComponent`, `Start` | Done |
| Single page, one section at a time, one form, one action | `RentalApplicationsController.Section` | Done |
| Continue validates and saves; Back does not | Same action, `WizardCommand` | Done |
| Sections render editable or read-only server-side | `SectionIsEditable` | Done |
| Residences added, edited, removed through a modal | `ResidencesController` | Done |
| Submit blocked until both sections saved | `ApplicationWorkflow.CanSubmit` | Done |
| Active lease blocks submit and approval | `LeaseTerm`, `Leases` unique index | Done |
| Withdraw, and correct and resubmit a returned application | `WithdrawAsync`, `SubmitAsync` | Done |
| Review modal with outcome and required comment | `ReviewController`, `ReviewRules` | Done |
| Twelve-month lease issued on approval | `LeaseTerm.Issue` | Done |
| Status history shown to property managers | `ApplicationHistoryViewComponent` | Done |
| List filtered by status and property in the database | `RentalApplicationService.ListAsync` | Done |
| Applicants see their own, managers see all | Same, via the applicant join | Done |

### Bonus items

All five are implemented.

| Bonus | Where |
| --- | --- |
| 1. Paging and sorting in the database, a reusable grid over a documented JSON endpoint | `DataGridViewComponent`, `ApplicationsApiController`, `/openapi/v1.json` |
| 2. Review queue with claim and release, claiming required before a decision | `ReviewService.ClaimAsync` and `ReleaseAsync`, `ApplicationWorkflow.CanReview` |
| 3. Manager-only notes | `NoteService`, `ApplicationNotesViewComponent` |
| 4. Save a section that fails validation, Summary lists what blocks submission | `WizardCommand.SaveDraft`, `SectionValidation` |
| 5. More than one applicant, stale saves rejected | `ApplicationApplicantService`, per-section concurrency tokens |

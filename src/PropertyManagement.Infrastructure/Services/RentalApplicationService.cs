using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Services;
using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Entities;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Domain.Rules;
using PropertyManagement.Infrastructure.Persistence;
using System.Linq.Expressions;

namespace PropertyManagement.Infrastructure.Services;

/// <summary>
/// Everything an applicant does to an application. The guards in
/// <see cref="ApplicationWorkflow"/> run here rather than in the controller, so a request that
/// forges a status, an owner or a section is rejected whatever the page offered.
/// </summary>
public class RentalApplicationService(PropertyManagementDbContext db, TimeProvider timeProvider)
    : IRentalApplicationService
{
    private const string StaleSaveMessage =
        "Someone else saved this section while you were editing. Reload the page and apply your changes again.";

    /// <summary>
    /// Loads an application for display and for permission checks.
    ///
    /// Untracked on purpose. A tracked read consults the change tracker first, so after a save
    /// that was rejected this would return the rejected values still sitting on the entity rather
    /// than what is actually stored, and the page would re-render showing the change it had just
    /// refused to make.
    /// </summary>
    public async Task<RentalApplication?> GetAsync(int id, CancellationToken cancellationToken = default) =>
        await db.RentalApplications
            .AsNoTracking()
            .Include(application => application.Unit)
                .ThenInclude(unit => unit.Property)
            .Include(application => application.Unit)
                .ThenInclude(unit => unit.UnitType)
            .Include(application => application.Residences)
            .Include(application => application.Applicants)
            .Include(application => application.Lease)
            // Two collection includes in one statement return their cross product, repeating the
            // whole application and unit payload for every pairing. Split into one query each.
            .AsSplitQuery()
            .FirstOrDefaultAsync(application => application.Id == id, cancellationToken);

    public async Task<ApplicationAccess?> GetAccessAsync(
        int applicationId,
        string userId,
        CancellationToken cancellationToken = default) =>
        await db.RentalApplications
            .AsNoTracking()
            .Where(application => application.Id == applicationId)
            .Select(application => new ApplicationAccess(
                application.Id,
                application.Status,
                application.Applicants.Any(link => link.ApplicantUserId == userId)))
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Ownership is a set rather than a single column, so an application shared between applicants
    /// answers this for every one of them.
    /// </summary>
    public async Task<bool> IsApplicantOnAsync(
        int applicationId,
        string userId,
        CancellationToken cancellationToken = default) =>
        await db.RentalApplicationApplicants.AnyAsync(
            link => link.RentalApplicationId == applicationId && link.ApplicantUserId == userId,
            cancellationToken);

    /// <summary>
    /// Starts a draft for a unit. An applicant who already has an open application for the unit is
    /// handed that one back instead of collecting duplicates.
    /// </summary>
    public async Task<DomainResult<int>> StartAsync(
        int unitId,
        Actor actor,
        DateOnly asOf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var unit = await db.Units
            .Include(entity => entity.Leases)
            // The property name is copied onto the application, so it has to be loaded to copy.
            .Include(entity => entity.Property)
            .FirstOrDefaultAsync(entity => entity.Id == unitId, cancellationToken);

        if (unit is null)
        {
            return DomainResult<int>.Failure("That unit no longer exists.");
        }

        if (LeaseTerm.HasActiveLease(unit.Leases, asOf))
        {
            return DomainResult<int>.Failure("That unit is currently leased and is not accepting applications.");
        }

        // An array rather than the rule's set, because EF translates Enumerable.Contains to NOT IN.
        var terminalStatuses = ApplicationWorkflow.TerminalStatuses.ToArray();

        var existing = await db.RentalApplications
            .Where(application => application.UnitId == unitId)
            .Where(application => application.Applicants.Any(link => link.ApplicantUserId == actor.UserId))
            .Where(application => !terminalStatuses.Contains(application.Status))
            .Select(application => (int?)application.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            return DomainResult<int>.Success(existing.Value);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        // The account already knows who this person is, so the first section opens filled in rather
        // than asking them to type their own name again. Only what the account actually holds: it
        // has no address, so those fields stay empty for them to complete.
        //
        // It is a starting point, not an answer. ApplicantInformationSavedAtUtc stays null, so the
        // applicant still has to look the section over and save it before the application can be
        // submitted, and a stale phone number on the account cannot ride through unnoticed.
        var account = await db.Users
            .AsNoTracking()
            .Where(user => user.Id == actor.UserId)
            .Select(user => new
            {
                user.FirstName,
                user.LastName,
                user.Email,
                user.PhoneNumber
            })
            .FirstOrDefaultAsync(cancellationToken);

        var application = new RentalApplication
        {
            UnitId = unitId,
            Status = ApplicationStatus.Draft,
            CreatedAtUtc = now,

            // What was applied for, as it was named at the time. Lists and past decisions read
            // these rather than the unit, so renaming a property does not rewrite history.
            PropertyName = unit.Property?.Name ?? string.Empty,
            UnitNumber = unit.UnitNumber,
            ApplicantInformation = new ApplicantInformation
            {
                FirstName = Clean(account?.FirstName),
                LastName = Clean(account?.LastName),
                Email = Clean(account?.Email),
                Phone = Clean(account?.PhoneNumber)
            },
            Applicants =
            [
                new RentalApplicationApplicant
                {
                    ApplicantUserId = actor.UserId,
                    IsPrimary = true,
                    AddedAtUtc = now
                }
            ]
        };

        db.RentalApplications.Add(application);
        await db.SaveChangesAsync(cancellationToken);

        return DomainResult<int>.Success(application.Id);
    }

    public async Task<DomainResult> SaveApplicantInformationAsync(
        ApplicantInformationInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var application = await db.RentalApplications
            .Include(entity => entity.Applicants)
            .FirstOrDefaultAsync(entity => entity.Id == input.ApplicationId, cancellationToken);

        if (application is null)
        {
            return DomainResult.Failure("That application no longer exists.");
        }

        var permitted = ApplicationWorkflow.CanSave(application, IsApplicantOn(application, userId));

        if (permitted.Failed)
        {
            return permitted;
        }

        application.ApplicantInformation.FirstName = Clean(input.FirstName);
        application.ApplicantInformation.LastName = Clean(input.LastName);
        application.ApplicantInformation.Phone = Clean(input.Phone);
        application.ApplicantInformation.Email = Clean(input.Email);
        application.ApplicantInformation.AddressLine1 = Clean(input.AddressLine1);
        application.ApplicantInformation.AddressLine2 = Clean(input.AddressLine2);
        application.ApplicantInformation.City = Clean(input.City);
        application.ApplicantInformation.State = Clean(input.State);
        application.ApplicantInformation.PostalCode = Clean(input.PostalCode);

        application.ApplicantInformationSavedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        return await SaveSectionAsync(
            application,
            entity => entity.ApplicantInformationVersion,
            input.Version,
            cancellationToken);
    }

    /// <summary>
    /// Marks the residence history section saved. The residences themselves are written by the
    /// modal; this records that the applicant has been through the section.
    /// </summary>
    /// <param name="requireComplete">
    /// True when moving on, which needs the section to be complete. False for a deliberate draft
    /// save, which records progress on a section the applicant has not finished. Submission is
    /// blocked either way while anything is still wrong; this only decides whether the save itself
    /// is refused.
    /// </param>
    public async Task<DomainResult> SaveResidenceHistoryAsync(
        int applicationId,
        Guid version,
        string userId,
        bool requireComplete = true,
        CancellationToken cancellationToken = default)
    {
        var application = await db.RentalApplications
            .Include(entity => entity.Applicants)
            .Include(entity => entity.Residences)
            .FirstOrDefaultAsync(entity => entity.Id == applicationId, cancellationToken);

        if (application is null)
        {
            return DomainResult.Failure("That application no longer exists.");
        }

        var permitted = ApplicationWorkflow.CanSave(application, IsApplicantOn(application, userId));

        if (permitted.Failed)
        {
            return permitted;
        }

        if (requireComplete)
        {
            var complete = ResidenceRules.ValidateHistory([.. application.Residences]);

            if (complete.Failed)
            {
                return complete;
            }
        }

        application.ResidenceHistorySavedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        return await SaveSectionAsync(
            application,
            entity => entity.ResidenceHistoryVersion,
            version,
            cancellationToken);
    }

    public async Task<DomainResult> SaveResidenceAsync(
        ResidenceInput input,
        string userId,
        DateOnly asOf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var application = await db.RentalApplications
            .Include(entity => entity.Applicants)
            .Include(entity => entity.Residences)
            .FirstOrDefaultAsync(entity => entity.Id == input.ApplicationId, cancellationToken);

        if (application is null)
        {
            return DomainResult.Failure("That application no longer exists.");
        }

        var permitted = ApplicationWorkflow.CanSave(application, IsApplicantOn(application, userId));

        if (permitted.Failed)
        {
            return permitted;
        }

        var dates = ResidenceRules.ValidateDates(input.MoveInDate, input.MoveOutDate, asOf);

        if (dates.Failed)
        {
            return dates;
        }

        Residence residence;

        if (input.Id == 0)
        {
            residence = new Residence { RentalApplicationId = application.Id };
            application.Residences.Add(residence);
        }
        else
        {
            // Looked up through the application, so a residence id belonging to someone else
            // cannot be edited by guessing its number.
            var existing = application.Residences.FirstOrDefault(entity => entity.Id == input.Id);

            if (existing is null)
            {
                return DomainResult.Failure("That residence no longer exists.");
            }

            // The token the modal was opened with. EF puts it in the UPDATE's WHERE clause, so a
            // save built on a copy somebody else has already changed matches no rows and is
            // reported rather than overwriting their edit.
            db.Entry(existing).Property(entity => entity.Version).OriginalValue = input.Version;
            existing.Version = Guid.NewGuid();

            residence = existing;
        }

        residence.AddressLine1 = input.AddressLine1.Trim();
        residence.AddressLine2 = Clean(input.AddressLine2);
        residence.City = input.City.Trim();
        residence.State = input.State.Trim();
        residence.PostalCode = input.PostalCode.Trim();
        residence.LandlordName = input.LandlordName.Trim();
        residence.LandlordPhone = input.LandlordPhone.Trim();
        residence.MoveInDate = input.MoveInDate;
        residence.MoveOutDate = input.MoveOutDate;

        // Deliberately does not touch ResidenceHistoryVersion. That token guards the section save,
        // which writes a completion marker and nothing else. Moving it from here would invalidate
        // the token held by the page this modal was opened from, so the applicant's own next
        // Continue would be refused as somebody else's edit. The row carries its own token instead,
        // which protects the row without touching the page. See the note on the entity.
        return await SaveOrReportStaleAsync(cancellationToken);
    }

    public async Task<DomainResult> DeleteResidenceAsync(
        int residenceId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var residence = await db.Residences
            .Include(entity => entity.RentalApplication)
                .ThenInclude(application => application.Applicants)
            .FirstOrDefaultAsync(entity => entity.Id == residenceId, cancellationToken);

        if (residence is null)
        {
            return DomainResult.Failure("That residence no longer exists.");
        }

        var application = residence.RentalApplication;
        var permitted = ApplicationWorkflow.CanSave(application, IsApplicantOn(application, userId));

        if (permitted.Failed)
        {
            return permitted;
        }

        db.Residences.Remove(residence);

        // As with saving a row, the section's token is left alone.
        await db.SaveChangesAsync(cancellationToken);

        return DomainResult.Success();
    }

    /// <summary>
    /// Submits, or resubmits a returned application. The unit's availability is checked here and
    /// again at approval, because a lease can be issued to someone else in between.
    /// </summary>
    public async Task<DomainResult> SubmitAsync(
        int applicationId,
        Actor actor,
        DateOnly asOf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var application = await db.RentalApplications
            .Include(entity => entity.Applicants)
            // Residences are loaded because whether the section is complete is part of whether
            // the application may be submitted at all.
            .Include(entity => entity.Residences)
            .Include(entity => entity.Unit)
                .ThenInclude(unit => unit.Leases)
            .FirstOrDefaultAsync(entity => entity.Id == applicationId, cancellationToken);

        if (application is null)
        {
            return DomainResult.Failure("That application no longer exists.");
        }

        var permitted = ApplicationWorkflow.CanSubmit(application, IsApplicantOn(application, actor.UserId));

        if (permitted.Failed)
        {
            return permitted;
        }

        if (LeaseTerm.HasActiveLease(application.Unit.Leases, asOf))
        {
            return DomainResult.Failure(
                "This unit has been leased and is no longer accepting applications.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var from = application.Status;

        application.Status = ApplicationStatus.Submitted;
        application.SubmittedAtUtc = now;

        application.Events.Add(new ApplicationEvent
        {
            FromStatus = from,
            ToStatus = ApplicationStatus.Submitted,
            ActorUserId = actor.UserId,
            ActorName = actor.Name,
            OccurredAtUtc = now
        });

        await db.SaveChangesAsync(cancellationToken);
        return DomainResult.Success();
    }

    public async Task<DomainResult> WithdrawAsync(
        int applicationId,
        Actor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var application = await db.RentalApplications
            .Include(entity => entity.Applicants)
            .FirstOrDefaultAsync(entity => entity.Id == applicationId, cancellationToken);

        if (application is null)
        {
            return DomainResult.Failure("That application no longer exists.");
        }

        var permitted = ApplicationWorkflow.CanWithdraw(application, IsApplicantOn(application, actor.UserId));

        if (permitted.Failed)
        {
            return permitted;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var from = application.Status;

        application.Status = ApplicationStatus.Withdrawn;
        application.DecidedAtUtc = now;
        application.ClaimedByUserId = null;
        application.ClaimedAtUtc = null;

        application.Events.Add(new ApplicationEvent
        {
            FromStatus = from,
            ToStatus = ApplicationStatus.Withdrawn,
            ActorUserId = actor.UserId,
            ActorName = actor.Name,
            Comment = "Withdrawn by the applicant.",
            OccurredAtUtc = now
        });

        await db.SaveChangesAsync(cancellationToken);
        return DomainResult.Success();
    }

    /// <summary>
    /// The application list. Both filters, the ownership restriction, the count and the page are
    /// all expressed on <see cref="IQueryable{T}"/>, so the database does the work and only the
    /// rows on screen are ever materialised.
    /// </summary>
    public async Task<ApplicationListPage> ListAsync(
        ApplicationListFilter filter,
        string? applicantUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = db.RentalApplications.AsNoTracking();

        // A null applicant id means a property manager, who sees every application.
        if (applicantUserId is not null)
        {
            query = query.Where(application =>
                application.Applicants.Any(link => link.ApplicantUserId == applicantUserId));
        }

        if (filter.Status is not null)
        {
            query = query.Where(application => application.Status == filter.Status);
        }

        if (filter.PropertyId is not null)
        {
            query = query.Where(application => application.Unit.PropertyId == filter.PropertyId);
        }

        var total = await query.CountAsync(cancellationToken);

        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        // Clamped at both ends. Flooring at one is not enough: (page - 1) * pageSize overflows for
        // a large page number, wraps negative, and SQL Server refuses a negative OFFSET with a 500.
        // The ceiling is the last page the filtered total can actually reach.
        var lastPage = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        var page = Math.Clamp(filter.Page, 1, lastPage);

        var rows = await OrderBy(query, filter.Sort, filter.Descending)
            // A stable tiebreak, so a row cannot drift between pages when the sort key ties.
            .ThenByDescending(application => application.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            // The names the application recorded, not the unit's current ones. This list includes
            // approved, denied and withdrawn rows, and a record of something that already happened
            // should not change because a property was renamed afterwards.
            .Select(application => new ApplicationListRow(
                application.Id,
                application.PropertyName,
                application.UnitNumber,
                ((application.ApplicantInformation.FirstName ?? string.Empty)
                    + " "
                    + (application.ApplicantInformation.LastName ?? string.Empty)).Trim(),
                application.Status,
                application.CreatedAtUtc,
                application.SubmittedAtUtc))
            .ToListAsync(cancellationToken);

        return new ApplicationListPage(rows, total, page, pageSize);
    }

    public async Task<IReadOnlyList<Property>> GetFilterPropertiesAsync(
        CancellationToken cancellationToken = default) =>
        await db.Properties
            .AsNoTracking()
            .OrderBy(property => property.Name)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Applies the chosen ordering to the query rather than to the results, so the database does
    /// the sorting and paging can be trusted to return the right slice.
    /// </summary>
    private static IOrderedQueryable<RentalApplication> OrderBy(
        IQueryable<RentalApplication> query,
        ApplicationSort sort,
        bool descending) => sort switch
    {
        ApplicationSort.Status => descending
            ? query.OrderByDescending(application => application.Status)
            : query.OrderBy(application => application.Status),

        ApplicationSort.Property => descending
            ? query.OrderByDescending(application => application.Unit.Property.Name)
            : query.OrderBy(application => application.Unit.Property.Name),

        ApplicationSort.Unit => descending
            ? query.OrderByDescending(application => application.Unit.UnitNumber)
            : query.OrderBy(application => application.Unit.UnitNumber),

        ApplicationSort.Applicant => descending
            ? query.OrderByDescending(application => application.ApplicantInformation.LastName)
            : query.OrderBy(application => application.ApplicantInformation.LastName),

        // Submitted, and anything unrecognised, falls back to when the application was last
        // moved on, which is the order a queue is worked in.
        _ => descending
            ? query.OrderByDescending(application => application.SubmittedAtUtc ?? application.CreatedAtUtc)
            : query.OrderBy(application => application.SubmittedAtUtc ?? application.CreatedAtUtc)
    };

    private static bool IsApplicantOn(RentalApplication application, string userId) =>
        application.Applicants.Any(link => link.ApplicantUserId == userId);

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Saves, and turns a lost race into a message rather than a 500.
    ///
    /// Moving a concurrency token means the update can now match no rows, which EF reports by
    /// throwing. Every caller that moves one needs this, and the person on the other end needs to
    /// be told to reload rather than shown a stack trace.
    /// </summary>
    private async Task<DomainResult> SaveOrReportStaleAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return DomainResult.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return DomainResult.Failure(StaleSaveMessage);
        }
    }

    /// <summary>
    /// Saves a section under its own concurrency token. Telling EF the original value puts it in
    /// the update's WHERE clause, so a save built on a stale copy of that section changes nothing
    /// and is reported rather than silently overwriting the other person's work. The other
    /// section has its own token and is unaffected.
    /// </summary>
    private async Task<DomainResult> SaveSectionAsync(
        RentalApplication application,
        Expression<Func<RentalApplication, Guid>> token,
        Guid expectedVersion,
        CancellationToken cancellationToken)
    {
        var property = db.Entry(application).Property(token);
        property.OriginalValue = expectedVersion;
        property.CurrentValue = Guid.NewGuid();

        return await SaveOrReportStaleAsync(cancellationToken);
    }
}

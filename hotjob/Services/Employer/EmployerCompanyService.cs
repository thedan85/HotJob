using System.ComponentModel.DataAnnotations;
using hotjob.Data;
using hotjob.DTOs.Employer;
using hotjob.DTOs.Job;
using hotjob.Models.Job;
using hotjob.Models.Users;
using hotjob.Services.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace hotjob.Services.Employer;

/// <summary>Company profile, team member and member-job approval management backed by EF Core.</summary>
/// <param name="db">Application database context.</param>
/// <param name="logger">Logger for company mutations.</param>
public sealed class EmployerCompanyService(ApplicationDbContext db, ILogger<EmployerCompanyService> logger) : IEmployerCompanyService
{
    private const int MaxRejectionReasonLength = 1000;

    public async Task<ServiceResult<CompanyPageResponse>> GetPageAsync(
        string? userId,
        CancellationToken cancellationToken = default)
    {
        var caller = await RequireActiveAsync(userId, requireApprovedCompany: false, cancellationToken);
        if (!caller.Success)
            return ServiceResult<CompanyPageResponse>.Fail(caller.ErrorType!.Value, caller.Errors);

        var callerData = caller.Data!;
        var company = await db.CompanyProfiles
            .AsNoTracking()
            .Include(c => c.Employers).ThenInclude(e => e.User)
            .SingleOrDefaultAsync(c => c.Id == callerData.CompanyProfileId, cancellationToken);
        if (company is null)
            return ServiceResult<CompanyPageResponse>.Fail(ServiceErrorType.NotFound, "Company not found.");

        var pendingJobCount = callerData.IsOwner
            ? await db.JobPostings.CountAsync(
                j => j.CompanyProfileId == company.Id && j.Status == JobStatus.PendingCompanyApproval,
                cancellationToken)
            : 0;

        // Removed members are history: only the owner (who can restore them) sees them.
        var members = company.Employers
            .Where(e => callerData.IsOwner || e.MembershipStatus != MembershipStatus.Removed);

        return ServiceResult<CompanyPageResponse>.Ok(new CompanyPageResponse
        {
            Company = ToDetail(company),
            Members = members
                .OrderByDescending(e => e.IsOwner)
                .ThenBy(e => e.FullName)
                .Select(ToMember)
                .ToArray(),
            IsOwner = callerData.IsOwner,
            PendingJobCount = pendingJobCount
        });
    }

    public async Task<ServiceResult<CompanyDetailResponse>> UpdateAsync(
        string? userId,
        UpdateCompanyRequest request,
        CancellationToken cancellationToken = default)
    {
        var owner = await RequireOwnerAsync(userId, cancellationToken);
        if (!owner.Success)
            return ServiceResult<CompanyDetailResponse>.Fail(owner.ErrorType!.Value, owner.Errors);

        var errors = Validate(request);
        if (errors.Count > 0)
            return ServiceResult<CompanyDetailResponse>.Fail(ServiceErrorType.Validation, errors);

        var company = await db.CompanyProfiles
            .SingleOrDefaultAsync(c => c.Id == owner.Data!.CompanyProfileId, cancellationToken);
        if (company is null)
            return ServiceResult<CompanyDetailResponse>.Fail(ServiceErrorType.NotFound, "Company not found.");

        if (request.Website is not null) company.Website = NullIfWhiteSpace(request.Website);
        if (request.Industry is not null) company.Industry = NullIfWhiteSpace(request.Industry);
        if (request.Location is not null) company.Location = NullIfWhiteSpace(request.Location);
        if (request.Scale.HasValue) company.Scale = request.Scale;
        if (request.Description is not null) company.Description = NullIfWhiteSpace(request.Description);

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Company owner {UserId} updated company {CompanyId}.", userId, company.Id);
        return ServiceResult<CompanyDetailResponse>.Ok(ToDetail(company));
    }

    public Task<ServiceResult<CompanyMemberDto>> ApproveMemberAsync(
        string? userId,
        int memberId,
        CancellationToken cancellationToken = default) =>
        TransitionMemberAsync(userId, memberId, MembershipStatus.Pending, MembershipStatus.Active, cancellationToken);

    public Task<ServiceResult<CompanyMemberDto>> RejectMemberAsync(
        string? userId,
        int memberId,
        CancellationToken cancellationToken = default) =>
        TransitionMemberAsync(userId, memberId, MembershipStatus.Pending, MembershipStatus.Rejected, cancellationToken);

    public Task<ServiceResult<CompanyMemberDto>> RemoveMemberAsync(
        string? userId,
        int memberId,
        CancellationToken cancellationToken = default) =>
        TransitionMemberAsync(userId, memberId, MembershipStatus.Active, MembershipStatus.Removed, cancellationToken);

    public Task<ServiceResult<CompanyMemberDto>> RestoreMemberAsync(
        string? userId,
        int memberId,
        CancellationToken cancellationToken = default) =>
        TransitionMemberAsync(userId, memberId, MembershipStatus.Removed, MembershipStatus.Active, cancellationToken);

    public async Task<ServiceResult<PagedResponse<CompanyPendingJobDto>>> ListPendingJobsAsync(
        string? userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var owner = await RequireOwnerAsync(userId, cancellationToken);
        if (!owner.Success)
            return ServiceResult<PagedResponse<CompanyPendingJobDto>>.Fail(owner.ErrorType!.Value, owner.Errors);

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var companyId = owner.Data!.CompanyProfileId;

        var query = db.JobPostings
            .AsNoTracking()
            .Where(j => j.CompanyProfileId == companyId && j.Status == JobStatus.PendingCompanyApproval);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            // Oldest submission first: the owner works the queue in arrival order.
            .OrderBy(j => j.SubmittedForApprovalAt ?? j.CreatedAt)
            .ThenBy(j => j.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(j => new CompanyPendingJobDto
            {
                Id = j.Id,
                Title = j.Title,
                CreatedByEmployerId = j.CreatedByEmployerId,
                CreatedByName = j.CreatedBy != null ? j.CreatedBy.FullName : null,
                JobType = j.JobType,
                SalaryMin = j.SalaryMin,
                SalaryMax = j.SalaryMax,
                ExpiryDate = j.ExpiryDate,
                CreatedAt = j.CreatedAt,
                SubmittedAt = j.SubmittedForApprovalAt,
                Status = j.Status
            })
            .ToListAsync(cancellationToken);

        return ServiceResult<PagedResponse<CompanyPendingJobDto>>.Ok(new PagedResponse<CompanyPendingJobDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<ServiceResult<CompanyPendingJobDto>> ApproveJobAsync(
        string? userId,
        int jobId,
        CancellationToken cancellationToken = default)
    {
        var owner = await RequireOwnerAsync(userId, cancellationToken);
        if (!owner.Success)
            return ServiceResult<CompanyPendingJobDto>.Fail(owner.ErrorType!.Value, owner.Errors);

        var job = await LoadPendingJobAsync(owner.Data!, jobId, cancellationToken);
        if (!job.Success)
            return job;

        var posting = await db.JobPostings.SingleAsync(j => j.Id == jobId, cancellationToken);
        var now = DateTime.UtcNow;
        // Step 1 done: hand the job over to the admin queue (step 2).
        posting.Status = JobStatus.PendingApproval;
        posting.CompanyReviewedAt = now;
        posting.CompanyReviewedByEmployerId = owner.Data!.Id;
        posting.SubmittedForApprovalAt = now;
        posting.RejectionReason = null;

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Company owner {UserId} approved job {JobId}; forwarded to admin review.", userId, jobId);

        var dto = job.Data!;
        dto.Status = posting.Status;
        dto.SubmittedAt = posting.SubmittedForApprovalAt;
        return ServiceResult<CompanyPendingJobDto>.Ok(dto);
    }

    public async Task<ServiceResult<CompanyPendingJobDto>> RejectJobAsync(
        string? userId,
        int jobId,
        RejectCompanyJobRequest request,
        CancellationToken cancellationToken = default)
    {
        var owner = await RequireOwnerAsync(userId, cancellationToken);
        if (!owner.Success)
            return ServiceResult<CompanyPendingJobDto>.Fail(owner.ErrorType!.Value, owner.Errors);

        var reason = request?.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
            return ServiceResult<CompanyPendingJobDto>.Fail(ServiceErrorType.Validation, "Rejection reason is required.");
        if (reason.Length > MaxRejectionReasonLength)
            return ServiceResult<CompanyPendingJobDto>.Fail(
                ServiceErrorType.Validation, $"Rejection reason must be at most {MaxRejectionReasonLength} characters.");

        var job = await LoadPendingJobAsync(owner.Data!, jobId, cancellationToken);
        if (!job.Success)
            return job;

        var posting = await db.JobPostings.SingleAsync(j => j.Id == jobId, cancellationToken);
        posting.Status = JobStatus.Rejected;
        posting.CompanyReviewedAt = DateTime.UtcNow;
        posting.CompanyReviewedByEmployerId = owner.Data!.Id;
        posting.RejectionReason = reason;

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Company owner {UserId} rejected job {JobId}.", userId, jobId);

        var dto = job.Data!;
        dto.Status = posting.Status;
        return ServiceResult<CompanyPendingJobDto>.Ok(dto);
    }

    /// <summary>Loads an own-company job and checks it is waiting for the owner (step 1 of two-step approval).</summary>
    private async Task<ServiceResult<CompanyPendingJobDto>> LoadPendingJobAsync(
        global::hotjob.Models.Users.Employer owner,
        int jobId,
        CancellationToken cancellationToken)
    {
        var job = await db.JobPostings
            .AsNoTracking()
            .Where(j => j.Id == jobId && j.CompanyProfileId == owner.CompanyProfileId)
            .Select(j => new CompanyPendingJobDto
            {
                Id = j.Id,
                Title = j.Title,
                CreatedByEmployerId = j.CreatedByEmployerId,
                CreatedByName = j.CreatedBy != null ? j.CreatedBy.FullName : null,
                JobType = j.JobType,
                SalaryMin = j.SalaryMin,
                SalaryMax = j.SalaryMax,
                ExpiryDate = j.ExpiryDate,
                CreatedAt = j.CreatedAt,
                SubmittedAt = j.SubmittedForApprovalAt,
                Status = j.Status
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (job is null)
            return ServiceResult<CompanyPendingJobDto>.Fail(ServiceErrorType.NotFound, "Job not found.");
        if (job.Status != JobStatus.PendingCompanyApproval)
            return ServiceResult<CompanyPendingJobDto>.Fail(
                ServiceErrorType.Conflict, "Only jobs waiting for company approval can be approved/rejected.");

        return ServiceResult<CompanyPendingJobDto>.Ok(job);
    }

    private async Task<ServiceResult<CompanyMemberDto>> TransitionMemberAsync(
        string? userId,
        int memberId,
        MembershipStatus expected,
        MembershipStatus target,
        CancellationToken cancellationToken)
    {
        var owner = await RequireOwnerAsync(userId, cancellationToken);
        if (!owner.Success)
            return ServiceResult<CompanyMemberDto>.Fail(owner.ErrorType!.Value, owner.Errors);

        var ownerData = owner.Data!;
        var member = await db.Employers
            .Include(e => e.User)
            .SingleOrDefaultAsync(
                e => e.Id == memberId && e.CompanyProfileId == ownerData.CompanyProfileId,
                cancellationToken);
        if (member is null)
            return ServiceResult<CompanyMemberDto>.Fail(ServiceErrorType.NotFound, "Member not found.");
        if (member.IsOwner || member.Id == ownerData.Id)
            return ServiceResult<CompanyMemberDto>.Fail(ServiceErrorType.Conflict, "The company owner cannot be changed.");
        if (member.MembershipStatus != expected)
            return ServiceResult<CompanyMemberDto>.Fail(ServiceErrorType.Conflict, TransitionError(expected));

        member.MembershipStatus = target;
        if (target == MembershipStatus.Removed)
        {
            member.RemovedAt = DateTime.UtcNow;
            member.RemovedByEmployerId = ownerData.Id;
        }
        else
        {
            member.RemovedAt = null;
            member.RemovedByEmployerId = null;
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Company owner {UserId} moved member {MemberId} from {From} to {Status}.", userId, memberId, expected, target);
        return ServiceResult<CompanyMemberDto>.Ok(ToMember(member));
    }

    private static string TransitionError(MembershipStatus expected) => expected switch
    {
        MembershipStatus.Active => "Only active members can be removed.",
        MembershipStatus.Removed => "Only removed members can be restored.",
        _ => "Only pending members can be approved/rejected."
    };

    private async Task<ServiceResult<global::hotjob.Models.Users.Employer>> RequireActiveAsync(
        string? userId,
        bool requireApprovedCompany,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return ServiceResult<global::hotjob.Models.Users.Employer>.Fail(ServiceErrorType.Unauthorized, "Authentication required.");

        var employer = await db.Employers
            .AsNoTracking()
            .Include(e => e.Company)
            .SingleOrDefaultAsync(e => e.UserId == userId, cancellationToken);

        // Reading the company page stays allowed for Pending/Rejected companies (owner needs to see the status);
        // every write requires an Approved company.
        return EmployerGuard.Evaluate(employer, requireApprovedCompany);
    }

    private async Task<ServiceResult<global::hotjob.Models.Users.Employer>> RequireOwnerAsync(
        string? userId,
        CancellationToken cancellationToken)
    {
        var active = await RequireActiveAsync(userId, requireApprovedCompany: true, cancellationToken);
        if (!active.Success)
            return active;
        if (!active.Data!.IsOwner)
            return ServiceResult<global::hotjob.Models.Users.Employer>.Fail(ServiceErrorType.Unauthorized, "Only the company owner can do this.");

        return active;
    }

    private static IReadOnlyList<string> Validate(UpdateCompanyRequest? request)
    {
        if (request is null)
            return ["Company data is required."];

        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, true);
        var errors = results
            .SelectMany(r => r.ErrorMessage is null ? [] : new[] { r.ErrorMessage })
            .ToList();
        if (request.Scale.HasValue && !Enum.IsDefined(request.Scale.Value))
            errors.Add("Invalid company scale.");
        return errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string? NullIfWhiteSpace(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static CompanyDetailResponse ToDetail(CompanyProfile company) => new()
    {
        Id = company.Id,
        CompanyName = company.CompanyName,
        Website = company.Website,
        Industry = company.Industry,
        Location = company.Location,
        Scale = company.Scale,
        Description = company.Description,
        Status = company.Status
    };

    private static CompanyMemberDto ToMember(global::hotjob.Models.Users.Employer employer) => new()
    {
        Id = employer.Id,
        FullName = employer.FullName,
        Position = employer.Position,
        Email = employer.User?.Email,
        IsOwner = employer.IsOwner,
        MembershipStatus = employer.MembershipStatus,
        JoinedAt = employer.JoinedAt,
        RemovedAt = employer.RemovedAt
    };
}

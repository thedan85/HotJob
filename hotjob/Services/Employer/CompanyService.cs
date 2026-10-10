using System.ComponentModel.DataAnnotations;
using hotjob.Data;
using hotjob.DTOs.Employer;
using hotjob.Models.Users;
using hotjob.Services.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace hotjob.Services.Employer;

/// <summary>Company profile and team member management backed by EF Core.</summary>
/// <param name="db">Application database context.</param>
/// <param name="logger">Logger for company mutations.</param>
public sealed class CompanyService(ApplicationDbContext db, ILogger<CompanyService> logger) : ICompanyService
{
    public async Task<ServiceResult<CompanyPageResponse>> GetPageAsync(
        string? userId,
        CancellationToken cancellationToken = default)
    {
        var caller = await RequireActiveAsync(userId, cancellationToken);
        if (!caller.Success)
            return ServiceResult<CompanyPageResponse>.Fail(caller.ErrorType!.Value, caller.Errors);

        var company = await db.CompanyProfiles
            .AsNoTracking()
            .Include(c => c.Employers).ThenInclude(e => e.User)
            .SingleOrDefaultAsync(c => c.Id == caller.Data!.CompanyProfileId, cancellationToken);
        if (company is null)
            return ServiceResult<CompanyPageResponse>.Fail(ServiceErrorType.NotFound, "Company not found.");

        var callerData = caller.Data!;
        return ServiceResult<CompanyPageResponse>.Ok(new CompanyPageResponse
        {
            Company = ToDetail(company),
            Members = company.Employers
                .OrderByDescending(e => e.IsOwner)
                .ThenBy(e => e.FullName)
                .Select(ToMember)
                .ToArray(),
            IsOwner = callerData.IsOwner
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

    public async Task<ServiceResult<CompanyMemberDto>> ApproveMemberAsync(
        string? userId,
        int memberId,
        CancellationToken cancellationToken = default) =>
        await TransitionMemberAsync(userId, memberId, MembershipStatus.Active, cancellationToken);

    public async Task<ServiceResult<CompanyMemberDto>> RejectMemberAsync(
        string? userId,
        int memberId,
        CancellationToken cancellationToken = default) =>
        await TransitionMemberAsync(userId, memberId, MembershipStatus.Rejected, cancellationToken);

    private async Task<ServiceResult<CompanyMemberDto>> TransitionMemberAsync(
        string? userId,
        int memberId,
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
            return ServiceResult<CompanyMemberDto>.Fail(
                ServiceErrorType.Conflict, "Only pending members can be approved/rejected.");
        if (member.MembershipStatus != MembershipStatus.Pending)
            return ServiceResult<CompanyMemberDto>.Fail(
                ServiceErrorType.Conflict, "Only pending members can be approved/rejected.");

        member.MembershipStatus = target;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Company owner {UserId} moved member {MemberId} to {Status}.", userId, memberId, target);
        return ServiceResult<CompanyMemberDto>.Ok(ToMember(member));
    }

    private async Task<ServiceResult<global::hotjob.Models.Users.Employer>> RequireActiveAsync(
        string? userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return ServiceResult<global::hotjob.Models.Users.Employer>.Fail(ServiceErrorType.Unauthorized, "Authentication required.");

        var employer = await db.Employers
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.UserId == userId, cancellationToken);
        if (employer is null || employer.MembershipStatus != MembershipStatus.Active)
            return ServiceResult<global::hotjob.Models.Users.Employer>.Fail(ServiceErrorType.Unauthorized, "Employer membership is not active.");

        return ServiceResult<global::hotjob.Models.Users.Employer>.Ok(employer);
    }

    private async Task<ServiceResult<global::hotjob.Models.Users.Employer>> RequireOwnerAsync(
        string? userId,
        CancellationToken cancellationToken)
    {
        var active = await RequireActiveAsync(userId, cancellationToken);
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
        MembershipStatus = employer.MembershipStatus
    };
}

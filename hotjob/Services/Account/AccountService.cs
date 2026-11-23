using System.ComponentModel.DataAnnotations;
using hotjob.Data;
using hotjob.DTOs.Authentication;
using hotjob.Models.Users;
using hotjob.Services.Authentication;
using hotjob.Services.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace hotjob.Services.Account;

public sealed class AccountService(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    ApplicationDbContext dbContext,
    IAuthService authService,
    ILogger<AccountService> logger) : IAccountService
{
    /// <summary>Maximum employers waiting for owner approval in one company before new join requests are refused.</summary>
    public const int MaxPendingJoinRequestsPerCompany = 20;

    public async Task<ServiceResult<RegisteredAccountResponse>> RegisterAsync(
        RegisterAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = ValidateRequest(request);
        if (validationErrors.Count > 0)
        {
            return ServiceResult<RegisteredAccountResponse>.Fail(ServiceErrorType.Validation, validationErrors);
        }

        if (await userManager.FindByEmailAsync(request.Email.Trim()) is not null)
        {
            return ServiceResult<RegisteredAccountResponse>.Fail(
                ServiceErrorType.Conflict,
                "An account with this email already exists.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var user = new ApplicationUser
            {
                UserName = request.Email.Trim(),
                Email = request.Email.Trim(),
                IsActive = true
            };

            var createResult = await userManager.CreateAsync(user, request.Password);
            if (!createResult.Succeeded)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return IdentityFailure(createResult);
            }

            int? companyProfileId = null;
            if (request.AccountType == AccountType.Candidate)
            {
                dbContext.Candidates.Add(new Candidate
                {
                    UserId = user.Id,
                    FullName = request.FullName.Trim()
                });

                var roleResult = await AddRoleAsync(user, AppRoles.Candidate);
                if (!roleResult.Succeeded)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    return IdentityFailure(roleResult);
                }
            }
            else
            {
                var employerResult = await AddEmployerProfileAsync(user, request, cancellationToken);
                if (!employerResult.Success)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    return ServiceResult<RegisteredAccountResponse>.Fail(
                        employerResult.ErrorType!.Value,
                        employerResult.Errors);
                }

                companyProfileId = employerResult.Data;
                var roleResult = await AddRoleAsync(user, AppRoles.Employer);
                if (!roleResult.Succeeded)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    return IdentityFailure(roleResult);
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return ServiceResult<RegisteredAccountResponse>.Ok(
                ToResponse(user, request.AccountType == AccountType.Employer, companyProfileId));
        }
        catch (DbUpdateException exception)
        {
            // Never pass the request token to a rollback: a cancelled request would skip it.
            await transaction.RollbackAsync(CancellationToken.None);
            logger.LogWarning(exception, "Registration database conflict for {Email}.", request.Email);

            // Concurrent registrations can pass the pre-checks above and still hit the unique indexes
            // (email, company name); that race must surface as a 409, not a 500.
            var isUniqueViolation = DbExceptionClassifier.IsUniqueViolation(exception);
            return ServiceResult<RegisteredAccountResponse>.Fail(
                isUniqueViolation ? ServiceErrorType.Conflict : ServiceErrorType.Validation,
                isUniqueViolation
                    ? "An account or company with the supplied details already exists."
                    : "The account could not be created.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            logger.LogError(exception, "Registration failed for {Email}.", request.Email);
            throw;
        }
    }

    public async Task<ServiceResult<ProfileResponse>> GetProfileAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
            return ServiceResult<ProfileResponse>.Fail(ServiceErrorType.NotFound, "User not found.");
        if (!user.IsActive)
            return ServiceResult<ProfileResponse>.Fail(ServiceErrorType.Unauthorized, "Account is disabled.");

        var candidate = await dbContext.Candidates
            .FirstOrDefaultAsync(profile => profile.UserId == userId, cancellationToken);
        var employer = await dbContext.Employers
            .Include(profile => profile.Company)
            .FirstOrDefaultAsync(profile => profile.UserId == userId, cancellationToken);
        if (candidate is null && employer is null)
            return ServiceResult<ProfileResponse>.Fail(ServiceErrorType.NotFound, "Profile not found.");

        return ServiceResult<ProfileResponse>.Ok(ToProfileResponse(user, candidate, employer));
    }

    public async Task<ServiceResult<ProfileResponse>> UpdateProfileAsync(
        string userId,
        UpdateProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = ValidateProfileRequest(request);
        if (validationErrors.Count > 0)
            return ServiceResult<ProfileResponse>.Fail(ServiceErrorType.Validation, validationErrors);

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
            return ServiceResult<ProfileResponse>.Fail(ServiceErrorType.NotFound, "User not found.");
        if (!user.IsActive)
            return ServiceResult<ProfileResponse>.Fail(ServiceErrorType.Unauthorized, "Account is disabled.");

        var candidate = await dbContext.Candidates
            .FirstOrDefaultAsync(profile => profile.UserId == userId, cancellationToken);
        var employer = await dbContext.Employers
            .FirstOrDefaultAsync(profile => profile.UserId == userId, cancellationToken);
        if (candidate is null && employer is null)
            return ServiceResult<ProfileResponse>.Fail(ServiceErrorType.NotFound, "Profile not found.");

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
            if (email is not null && !string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
            {
                var existingUser = await userManager.FindByEmailAsync(email);
                if (existingUser is not null && existingUser.Id != user.Id)
                    return await AbortProfileUpdateAsync(transaction, ServiceResult<ProfileResponse>.Fail(
                        ServiceErrorType.Conflict, "An account with this email already exists."));

                // Each Identity call below persists on its own, so a later failure leaves earlier steps
                // written inside this transaction: every failure path must roll back explicitly.
                var emailResult = await userManager.SetEmailAsync(user, email);
                if (!emailResult.Succeeded)
                    return await AbortProfileUpdateAsync(transaction, ProfileIdentityFailure(emailResult));
                var usernameResult = await userManager.SetUserNameAsync(user, email);
                if (!usernameResult.Succeeded)
                    return await AbortProfileUpdateAsync(transaction, ProfileIdentityFailure(usernameResult));
                user.EmailConfirmed = false;
                var updateResult = await userManager.UpdateAsync(user);
                if (!updateResult.Succeeded)
                    return await AbortProfileUpdateAsync(transaction, ProfileIdentityFailure(updateResult));
            }

            if (candidate is not null)
            {
                if (request.FullName is not null) candidate.FullName = request.FullName.Trim();
                if (request.DateOfBirth.HasValue) candidate.DateOfBirth = request.DateOfBirth;
                if (request.Gender.HasValue) candidate.Gender = request.Gender;
                if (request.Address is not null) candidate.Address = NullIfWhiteSpace(request.Address);
                if (request.AvatarUrl is not null) candidate.AvatarUrl = NullIfWhiteSpace(request.AvatarUrl);
                if (request.GithubUrl is not null) candidate.GithubUrl = NullIfWhiteSpace(request.GithubUrl);
                if (request.LinkedInUrl is not null) candidate.LinkedInUrl = NullIfWhiteSpace(request.LinkedInUrl);
                if (request.Position is not null) candidate.Position = NullIfWhiteSpace(request.Position);
                if (request.Summary is not null) candidate.Summary = NullIfWhiteSpace(request.Summary);
                if (request.JobSeekingStatus is not null) candidate.JobSeekingStatus = NullIfWhiteSpace(request.JobSeekingStatus);
                if (request.PreferredWorkModel is not null) candidate.PreferredWorkModel = NullIfWhiteSpace(request.PreferredWorkModel);
                if (request.PreferredLocation is not null) candidate.PreferredLocation = NullIfWhiteSpace(request.PreferredLocation);
            }
            if (employer is not null)
            {
                if (request.FullName is not null) employer.FullName = request.FullName.Trim();
                if (request.Position is not null) employer.Position = NullIfWhiteSpace(request.Position);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            logger.LogWarning(exception, "Profile update database conflict for {UserId}.", userId);
            return ServiceResult<ProfileResponse>.Fail(
                DbExceptionClassifier.IsUniqueViolation(exception) ? ServiceErrorType.Conflict : ServiceErrorType.Validation,
                "The profile could not be updated.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            logger.LogError(exception, "Profile update failed for {UserId}.", userId);
            throw;
        }

        return await GetProfileAsync(userId, cancellationToken);
    }

    public async Task<ServiceResult> ChangePasswordAsync(
        string userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = ValidatePasswordRequest(request);
        if (validationErrors.Count > 0)
            return ServiceResult.Fail(ServiceErrorType.Validation, validationErrors);

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
            return ServiceResult.Fail(ServiceErrorType.NotFound, "User not found.");
        if (!user.IsActive)
            return ServiceResult.Fail(ServiceErrorType.Unauthorized, "Account is disabled.");

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(error => error.Code is "PasswordMismatch" or "InvalidPassword"))
                return ServiceResult.Fail(ServiceErrorType.Unauthorized, "Current password is incorrect.");
            return ServiceResult.Fail(ServiceErrorType.Validation, result.Errors.Select(error => error.Description));
        }

        await authService.LogoutAsync(null, userId, cancellationToken);
        return ServiceResult.Ok();
    }

    /// <summary>
    /// Rolls the profile-update transaction back and drops the context's tracked state, so the
    /// partly-modified user entity cannot be read back as if the change had been saved.
    /// </summary>
    private async Task<ServiceResult<ProfileResponse>> AbortProfileUpdateAsync(
        IDbContextTransaction transaction,
        ServiceResult<ProfileResponse> failure)
    {
        await transaction.RollbackAsync(CancellationToken.None);
        dbContext.ChangeTracker.Clear();
        return failure;
    }

    private static IReadOnlyList<string> ValidateProfileRequest(UpdateProfileRequest? request)
    {
        if (request is null)
            return ["Profile data is required."];
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, true);
        var errors = results.SelectMany(result => result.ErrorMessage is null ? [] : new[] { result.ErrorMessage }).ToList();
        if (request.FullName is not null && string.IsNullOrWhiteSpace(request.FullName))
            errors.Add("Full name cannot be empty.");
        if (request.DateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow))
            errors.Add("Date of birth cannot be in the future.");
        return errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> ValidatePasswordRequest(ChangePasswordRequest? request)
    {
        if (request is null)
            return ["Password data is required."];
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, true);
        return results.SelectMany(result => result.ErrorMessage is null ? [] : new[] { result.ErrorMessage })
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string? NullIfWhiteSpace(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ServiceResult<ProfileResponse> ProfileIdentityFailure(IdentityResult result)
    {
        var errors = result.Errors.ToArray();
        var errorType = errors.Any(error => error.Code is "DuplicateUserName" or "DuplicateEmail")
            ? ServiceErrorType.Conflict
            : ServiceErrorType.Validation;
        return ServiceResult<ProfileResponse>.Fail(errorType, errors.Select(error => error.Description));
    }

    private static ProfileResponse ToProfileResponse(
        ApplicationUser user, Candidate? candidate, global::hotjob.Models.Users.Employer? employer) =>
        new(
            user.Id,
            user.Email ?? string.Empty,
            user.EmailConfirmed,
            candidate is not null,
            employer is not null,
            candidate is null ? null : new CandidateProfileDto
            {
                FullName = candidate.FullName,
                DateOfBirth = candidate.DateOfBirth,
                Gender = candidate.Gender,
                Address = candidate.Address,
                AvatarUrl = candidate.AvatarUrl,
                GithubUrl = candidate.GithubUrl,
                LinkedInUrl = candidate.LinkedInUrl,
                Position = candidate.Position,
                Summary = candidate.Summary,
                JobSeekingStatus = candidate.JobSeekingStatus,
                PreferredWorkModel = candidate.PreferredWorkModel,
                PreferredLocation = candidate.PreferredLocation
            },
            employer is null ? null : new EmployerProfileDto
            {
                FullName = employer.FullName,
                Position = employer.Position,
                CompanyProfileId = employer.CompanyProfileId,
                CompanyName = employer.Company?.CompanyName,
                IsOwner = employer.IsOwner,
                MembershipStatus = employer.MembershipStatus
            });

    private async Task<IdentityResult> AddRoleAsync(ApplicationUser user, string role)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            var createRoleResult = await roleManager.CreateAsync(new IdentityRole(role));
            if (!createRoleResult.Succeeded)
            {
                return createRoleResult;
            }
        }

        return await userManager.AddToRoleAsync(user, role);
    }

    private async Task<ServiceResult<int>> AddEmployerProfileAsync(
        ApplicationUser user,
        RegisterAccountRequest request,
        CancellationToken cancellationToken)
    {
        CompanyProfile? company = null;
        var isOwner = false;
        var membershipStatus = MembershipStatus.Pending;

        if (request.CompanyProfileId.HasValue)
        {
            company = await dbContext.CompanyProfiles
                .FirstOrDefaultAsync(
                    profile => profile.Id == request.CompanyProfileId.Value,
                    cancellationToken);

            if (company is null)
            {
                return ServiceResult<int>.Fail(ServiceErrorType.NotFound, "Company not found.");
            }

            if (!company.IsActive)
            {
                return ServiceResult<int>.Fail(
                    ServiceErrorType.Conflict,
                    "This company is not accepting new members.");
            }

            if (company.Status == CompanyStatus.Rejected)
            {
                return ServiceResult<int>.Fail(
                    ServiceErrorType.Conflict,
                    "You cannot join a rejected company.");
            }

            // Anonymous registration must not be able to flood an owner's approval queue.
            // Soft cap: the count and the insert are not atomic, so concurrent requests can overshoot slightly.
            var pendingRequests = await dbContext.Employers.CountAsync(
                employer => employer.CompanyProfileId == company.Id
                    && employer.MembershipStatus == MembershipStatus.Pending,
                cancellationToken);
            if (pendingRequests >= MaxPendingJoinRequestsPerCompany)
            {
                return ServiceResult<int>.Fail(
                    ServiceErrorType.Conflict,
                    "This company has too many pending join requests. Try again later.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(request.CompanyName))
        {
            var normalizedCompanyName = request.CompanyName.Trim();
            if (await dbContext.CompanyProfiles.AnyAsync(
                    profile => profile.CompanyName == normalizedCompanyName,
                    cancellationToken))
            {
                return ServiceResult<int>.Fail(
                    ServiceErrorType.Conflict,
                    "A company with this name already exists.");
            }

            company = new CompanyProfile
            {
                CompanyName = normalizedCompanyName,
                Status = CompanyStatus.Pending
            };
            dbContext.CompanyProfiles.Add(company);
            isOwner = true;
            membershipStatus = MembershipStatus.Active;
        }
        else
        {
            return ServiceResult<int>.Fail(
                ServiceErrorType.Validation,
                "Select an existing company or provide a company name.");
        }

        var employer = new global::hotjob.Models.Users.Employer
        {
            UserId = user.Id,
            FullName = request.FullName.Trim(),
            Position = string.IsNullOrWhiteSpace(request.Position) ? null : request.Position.Trim(),
            Company = company,
            IsOwner = isOwner,
            MembershipStatus = membershipStatus
        };
        dbContext.Employers.Add(employer);

        await dbContext.SaveChangesAsync(cancellationToken);
        return ServiceResult<int>.Ok(company.Id);
    }

    private static IReadOnlyList<string> ValidateRequest(RegisterAccountRequest? request)
    {
        if (request is null)
        {
            return ["Registration data is required."];
        }

        var validationResults = new List<ValidationResult>();
        Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            validationResults,
            validateAllProperties: true);

        var errors = validationResults
            .SelectMany(result => result.ErrorMessage is null ? [] : new[] { result.ErrorMessage })
            .ToList();

        if (!Enum.IsDefined(request.AccountType))
        {
            errors.Add("A valid account type is required.");
        }

        if (request.AccountType == AccountType.Employer)
        {
            if (string.IsNullOrWhiteSpace(request.FullName))
            {
                errors.Add("Full name is required.");
            }

            if (!request.CompanyProfileId.HasValue && string.IsNullOrWhiteSpace(request.CompanyName))
            {
                errors.Add("Select an existing company or provide a company name.");
            }

            if (request.CompanyProfileId.HasValue && !string.IsNullOrWhiteSpace(request.CompanyName))
            {
                errors.Add("Provide either an existing company or a new company name, not both.");
            }
        }
        else if (string.IsNullOrWhiteSpace(request.FullName))
        {
            errors.Add("Full name is required.");
        }

        return errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static ServiceResult<RegisteredAccountResponse> IdentityFailure(IdentityResult result)
    {
        var errors = result.Errors.ToArray();
        var errorType = errors.Any(error =>
            error.Code is "DuplicateUserName" or "DuplicateEmail")
            ? ServiceErrorType.Conflict
            : ServiceErrorType.Validation;
        return ServiceResult<RegisteredAccountResponse>.Fail(
            errorType,
            errors.Select(error => error.Description));
    }

    private static RegisteredAccountResponse ToResponse(
        ApplicationUser user,
        bool hasEmployerProfile,
        int? companyProfileId) =>
        new(user.Id, user.Email!, !hasEmployerProfile, hasEmployerProfile, companyProfileId);
}
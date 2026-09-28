using System.ComponentModel.DataAnnotations;
using hotjob.Data;
using hotjob.DTOs.Authentication;
using hotjob.Models.Users;
using hotjob.Services.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace hotjob.Services.Account;

public sealed class AccountService(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    ApplicationDbContext dbContext,
    ILogger<AccountService> logger) : IAccountService
{
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
            await transaction.RollbackAsync(cancellationToken);
            logger.LogWarning(exception, "Registration database conflict for {Email}.", request.Email);
            return ServiceResult<RegisteredAccountResponse>.Fail(
                IsUniqueConstraintViolation(exception) ? ServiceErrorType.Conflict : ServiceErrorType.Validation,
                IsUniqueConstraintViolation(exception)
                    ? "An account or company with the supplied details already exists."
                    : "The account could not be created.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogError(exception, "Registration failed for {Email}.", request.Email);
            throw;
        }
    }

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

            if (company.Status == CompanyStatus.Rejected)
            {
                return ServiceResult<int>.Fail(
                    ServiceErrorType.Conflict,
                    "You cannot join a rejected company.");
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

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException?.Message.Contains("Duplicate", StringComparison.OrdinalIgnoreCase) == true ||
        exception.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true;

    private static RegisteredAccountResponse ToResponse(
        ApplicationUser user,
        bool hasEmployerProfile,
        int? companyProfileId) =>
        new(user.Id, user.Email!, !hasEmployerProfile, hasEmployerProfile, companyProfileId);
}
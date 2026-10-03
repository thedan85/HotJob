# Profile Update (Candidate / Employer) — Backend Design Spec

Date: 2026-10-03
Status: Draft, pending user review
Scope: Backend only. No `.razor`, no CSS, no JS.

## Goal

Add backend read + update for candidate and employer profiles: single `GET /api/account/profile`, single `PUT /api/account/profile` (profile fields + email), separate `POST /api/account/change-password`. Delete unused `IUserRepository` / `UserRepository`.

## Context

- Repo is Blazor Server + API controllers, .NET 10.
- Existing: `hotjob/Controllers/AccountController.cs` (register only), `hotjob/Services/Account/AccountService.cs` + `IAccountService.cs` (register only), DTOs in `hotjob/DTOs/Authentication/AccountDtos.cs` (`RegisterAccountRequest`, `AccountType`, `RegisteredAccountResponse`).
- Models: `hotjob/Models/Users/Candidate.cs` (FullName required 100, DateOfBirth, Gender, Address 500, AvatarUrl/GithubUrl/LinkedInUrl Url 2048), `hotjob/Models/Users/Employer.cs` (FullName required 100, Position 100 nullable, CompanyProfileId, IsOwner, MembershipStatus), `hotjob/Models/Users/ApplicationUser.cs` (IsActive, Email).
- Tests pattern: `hotjob/Tests/hotjob.Tests/RegisterTests/RegisterCandidateTests.cs`, fixture `hotjob/Tests/hotjob.Tests/Support/TestFixture.cs` (`fx.Service`, `fx.UserManager`, `fx.Db`, `SeedUserAsync`).
- `Repositories/User/IUserRepository.cs` + `UserRepository.cs` have zero injections; only DI registration at `hotjob/Program.cs:156`.
- Prereq: `SeedOptions.Demo` / `Admins` build break (see `docs/login-pipeline-fix.md` Errors 1–2) must be fixed first, else nothing compiles.

## Non-goals

- No company edit (name, logo, description stay admin/employer-flow).
- No avatar upload (`AvatarUrl` string only).
- No email confirmation token flow.
- No frontend.

## Decisions (confirmed)

- Extend `AccountService`, not new `ProfileService`. Follows register pattern.
- DTOs go in existing `hotjob/DTOs/Authentication/AccountDtos.cs`. No new DTO file.
- Email change: immediate (Option A). `SetEmailAsync` + `SetUserNameAsync`, set `EmailConfirmed=false`.
- Password: separate endpoint (not in PUT body).
- Auth: JWT Bearer, user id from `ClaimTypes.NameIdentifier` (fallback `"sub"`). No userId in body/route.
- Delete `Repositories/User/` + DI line + `using hotjob.Repositories.User;`.

## DTOs (add to `AccountDtos.cs`)

```csharp
public sealed class CandidateProfileDto {
  public string FullName { get; set; } = "";
  public DateOnly? DateOfBirth { get; set; }
  public Gender? Gender { get; set; }
  public string? Address { get; set; }
  public string? AvatarUrl { get; set; }
  public string? GithubUrl { get; set; }
  public string? LinkedInUrl { get; set; }
}
public sealed class EmployerProfileDto {
  public string FullName { get; set; } = "";
  public string? Position { get; set; }
  public int CompanyProfileId { get; set; }
  public string? CompanyName { get; set; }
  public bool IsOwner { get; set; }
  public MembershipStatus MembershipStatus { get; set; }
}
public sealed record ProfileResponse(
  string UserId, string Email, bool EmailConfirmed,
  bool HasCandidateProfile, bool HasEmployerProfile,
  CandidateProfileDto? Candidate, EmployerProfileDto? Employer);
public sealed class UpdateProfileRequest {
  public string? FullName { get; set; }        // 100 max, applied to whichever profile exists (both if dual)
  public DateOnly? DateOfBirth { get; set; }   // candidate only
  public Gender? Gender { get; set; }          // candidate only
  public string? Address { get; set; }         // 500 max, candidate only; empty string clears
  public string? AvatarUrl { get; set; }       // Url 2048, candidate only; empty clears
  public string? GithubUrl { get; set; }       // Url 2048, candidate only; empty clears
  public string? LinkedInUrl { get; set; }     // Url 2048, candidate only; empty clears
  public string? Position { get; set; }        // 100 max, employer only; empty string clears
  public string? Email { get; set; }           // EmailAddress 256; omit (null) = no change
}
public sealed class ChangePasswordRequest {
  [Required] public string CurrentPassword { get; set; } = "";
  [Required, MinLength(6), MaxLength(100)]
  [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).+$",
    ErrorMessage = "Password must contain an uppercase letter, lowercase letter, number, and special character.")]
  public string NewPassword { get; set; } = "";
}
```

Rules:
- `null` = field omitted, no change. Empty/whitespace string on nullable Address/urls/Position = clear to null. `FullName` whitespace-only = validation error (not clear).
- `DateOfBirth` future date = validation error.
- Employer-only fields sent when caller has candidate profile only (and vice versa) = ignored, not error. Document in endpoint summary.
- `Gender` uses existing `hotjob.Models.Users.Gender` enum. Invalid enum value = validation error.

## Endpoints (`AccountController.cs`)

All `[Authorize]`. Get user id: `User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")`; null/empty → `Unauthorized("Not authenticated.")`.

- `GET /api/account/profile` → `200 ProfileResponse`. `404` if user row or neither profile found. `401` if `!IsActive`.
- `PUT /api/account/profile` (`UpdateProfileRequest`) → `200 ProfileResponse` (post-update state). `400` validation, `404` no profile, `409` email taken, `401` disabled/wrong state.
- `POST /api/account/change-password` (`ChangePasswordRequest`) → `204`. `400` new-password policy fail, `401` wrong current password, `404` user missing.

Error mapping: same switch as register (`Conflict→409`, `NotFound→404`, `Unauthorized→401`, else `400` with `result.Errors` list).

## Service (`IAccountService` + `AccountService`)

Signatures:

```csharp
Task<ServiceResult<ProfileResponse>> GetProfileAsync(string userId, CancellationToken ct = default);
Task<ServiceResult<ProfileResponse>> UpdateProfileAsync(string userId, UpdateProfileRequest request, CancellationToken ct = default);
Task<ServiceResult> ChangePasswordAsync(string userId, ChangePasswordRequest request, CancellationToken ct = default);
```

Behavior:
- `GetProfileAsync`: `UserManager.FindByIdAsync(userId)` → null = NotFound. `!IsActive` = Unauthorized. Load `Candidates` (+ no includes needed) and `Employers.Include(e => e.Company)` by `UserId`. Neither found = NotFound. Return union with `EmailConfirmed` from user row.
- `UpdateProfileAsync`: null request = Validation. Validate via DataAnnotations on request + manual rules (FullName length after trim, Address 500, urls Url+2048, Position 100, Email format, DateOfBirth <= today). Collect all errors, return at once (match `ValidateRequest` style).
  - Email: if non-blank and different (case-insensitive compare after trim) → `FindByEmailAsync` → taken by another id = Conflict `"An account with this email already exists."` Else `SetEmailAsync` + `SetUserNameAsync`; on success set `EmailConfirmed=false` + `UpdateAsync`. Map Identity failures via existing `IdentityFailure`-style helper (Conflict on DuplicateEmail/UserName, else Validation).
  - Profile: update non-null fields only, trim names. Candidate `FullName` required guard: reject empty. Use tracked entities (no `AsNoTracking`). `SaveChangesAsync`. Single save; transaction only if email + profile in same call fail midway — use transaction like register to keep atomic.
- `ChangePasswordAsync`: null/empty = Validation. `FindByIdAsync` → NotFound. `!IsActive` → Unauthorized. `UserManager.ChangePasswordAsync(user, current, new)` → success = Ok + revoke sessions via `IAuthService.LogoutAsync(null, userId, ct)` (inject `IAuthService`; no cycle — `AuthService` does not depend on `AccountService`). Wrong password failure → Unauthorized `"Current password is incorrect."` Policy failures → Validation with Identity descriptions.
- ctor change: `AccountService(UserManager, RoleManager, ApplicationDbContext, IAuthService, ILogger)` — update `TestFixture`? No, `TestFixture.Service` constructs `AccountService` directly; plan must update `TestFixture.cs` ctor call or tests construct with stub `IAuthService`. Prefer inject `IAuthService` and update `TestFixture.Service` property accordingly.

## Delete User Repository

- Delete `hotjob/Repositories/User/IUserRepository.cs`, `hotjob/Repositories/User/UserRepository.cs`.
- Remove `hotjob/Program.cs:156` `AddScoped<IUserRepository, UserRepository>()` + `using hotjob.Repositories.User;`.
- Verify zero references via grep.

## Tests (`hotjob/Tests/hotjob.Tests/ProfileTests/`, wired via default SDK globbing)

Service-level via `TestFixture` (mirror `RegisterCandidateTests`):
1. `GetProfile_Candidate_ReturnsUnion` — seed candidate, assert email + candidate node.
2. `GetProfile_Employer_ReturnsCompanyId` — seed employer w/ company, assert `CompanyProfileId` + flags.
3. `Update_Candidate_AllFields_Trims` — full update, assert trimmed + urls saved.
4. `Update_Email_Duplicate_409` — second user email → Conflict.
5. `Update_Email_SetsUnconfirmed` — new email → `EmailConfirmed=false`, username updated.
6. `Update_Employer_Position_Empty_Clears` — `""` → null.
7. `Update_IgnoresOtherRoleFields` — candidate sends `Position` → ignored, success.
8. `ChangePassword_WrongCurrent_401`.
9. `ChangePassword_Ok_RevokesSessions` — login creates session, change password, assert all `RefreshTokens` revoked.
10. `GetProfile_UnknownUser_404`, `Update_Disabled_401`.

Controller tests (mirror `AccountControllerTests`): `PUT` maps 409/400, `POST change-password` 401→401, 204 on success, unauthenticated (no claim) → 401 without service call.

Commands: `dotnet build hotjob/hotjob.csproj`; `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj`.

## Migration

None. No model changes.

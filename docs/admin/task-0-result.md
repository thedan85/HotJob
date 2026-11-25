# HotJob codebase audit — task 0 result

Date: 2026-11-25 · Repo: `C:\Users\phulo\Desktop\HotJob` · Working tree at audit time: **clean** (`git status --short` empty, HEAD `71823ad`).

Method: read actual source (`.cs`, `.razor`, migrations, csproj, config), inspected commit diffs, and ran `dotnet test` from the repo root. Docs/SQL files were used only as secondary context; where they disagreed with code, code wins (noted explicitly in §9–10).

---

## 1. Tech stack & versions

| Concern | Value / evidence |
|---|---|
| Target framework | **.NET 10.0** (`net10.0`) in both `hotjob/hotjob.csproj:4` and `hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj:4`. Installed SDK: **10.0.401**. |
| Web UI | ASP.NET Core **Blazor Server** (.NET 10 Blazor Web App template: `Components/App.razor`, `Components/Routes.razor`). **Global Interactive Server** render mode only — `Program.cs:48-49` (`AddInteractiveServerComponents`) and `Program.cs:301-302` (`MapRazorComponents<App>().AddInteractiveServerRenderMode()`). No WASM/Auto/StaticServer project, no `.Client` project; 26 components repeat `@rendermode InteractiveServer` redundantly. `<BlazorDisableThrowNavigationException>true</...>` in csproj. |
| API | ASP.NET Core MVC controllers + `ControllerBase`, Swagger UI (Swashbuckle **6.6.2**) enabled only in Development (`Program.cs:295-299`). |
| ORM / DB | **EF Core 9.0.0** (`Microsoft.EntityFrameworkCore`, `...Design`), **Pomelo.EntityFrameworkCore.MySql 9.0.0**, target server **MySQL 8.0.36** (`Program.cs:194`). UTF-8 `utf8mb4`. Tests use **EF Core Sqlite 9.0.0** (in-memory/file SQLite). |
| Auth | **ASP.NET Core Identity** (`Microsoft.AspNetCore.Identity.EntityFrameworkCore 9.0.0`, `IdentityDbContext<ApplicationUser>`), **JWT bearer** (`Microsoft.AspNetCore.Authentication.JwtBearer 10.0.5`, `System.IdentityModel.Tokens.Jwt 8.16.0`) as the default authenticate/challenge scheme, plus optional **Google OAuth** (`Microsoft.AspNetCore.Authentication.Google 10.0.5`). Refresh is **not** an Identity cookie: SHA-256-hashed tokens in `RefreshTokens`, raw token in HttpOnly cookie `hotjob_refresh` (`Path=/api/auth`, `SameSite=Lax`, `Secure` when HTTPS). Roles: `Admin`, `Candidate`, `Employer`. |
| Payments | **VNPay 2.1.0** — hand-rolled implementation, no payment NuGet: `Services/Billing/VnPayService.cs` builds sorted URL-encoded `vnp_*` payload and HMAC-**SHA512** signature with fixed-time comparison; `Controllers/VnPayController.cs` exposes IPN/return. |
| Background work | One `BackgroundService`: `Services/Job/JobExpiryService.cs`, 15-minute timer, own DI scope, sets `JobStatus.Expired`. No Quartz/Hangfire. |
| ML | **Microsoft.ML 5.0.1** + **Microsoft.ML.FastTree 5.0.1**: weighted rule score plus a trained FastTree model stored at `App_Data/models/matchscore.zip`, with synthetic training data helper. |
| Tests | **xUnit 2.9.3**, `xunit.runner.visualstudio 3.1.4`, `Microsoft.NET.Test.Sdk 17.14.1`, `coverlet.collector 6.0.4`. |
| Other notable packages | `Microsoft.AspNetCore.Authentication.Google 10.0.5`, `Swashbuckle.AspNetCore 6.6.2`. No Redis, no message bus, no SignalR hub, no Docker. |

---

## 2. Solution / project structure

`hotjob.slnx` (new XML solution format) contains two projects:

| Project | Path | Purpose |
|---|---|---|
| `hotjob` | `hotjob/hotjob.csproj` | Main ASP.NET Core / Blazor Server web app. |
| `hotjob.Tests` | `hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj` | xUnit test project; explicitly excluded from the main build via `<Compile Remove="Tests/**/*.cs" />`. |

Folder tree (2–3 levels; purpose per folder):

```
C:\Users\phulo\Desktop\HotJob
├── hotjob/                         Main app
│   ├── Components/                 Blazor UI shell + all Razor pages
│   │   ├── Layout/                 MainLayout, NavMenu, Footer, ReconnectModal, RegisterLayout
│   │   └── Pages/                  29 routable pages
│   │       ├── Admin/              /admin/* back-office pages (5)
│   │       ├── Employer/           /employer/* ATS & billing pages (5)
│   │       ├── RegisterPage/       /register page
│   │       └── BecomeEmployerPage/ /become-employer upgrade page
│   ├── Controllers/                12 API controllers + ApiControllerBase + validation filter
│   ├── Data/                       ApplicationDbContext, 3 seeders, design-time migration factory
│   ├── DTOs/                       API/service contracts, grouped by domain
│   │   └── Admin|Authentication|Billing|Candidate|Company|Employer|Job|JobAlerts|ML
│   ├── Middleware/                 ApiExceptionHandler (JSON 500 for /api)
│   ├── Migrations/                 13 EF migrations + ApplicationDbContextModelSnapshot
│   ├── Models/                     Domain entities, grouped by domain
│   │   ├── Application| Billing| Job| ML| Resume| Users
│   │   └── root: Notification.cs, Review.cs, SavedJob.cs, Skill.cs
│   ├── Properties/                 launchSettings.json (http 5168 / https 7023)
│   ├── Repositories/               EF query layer: Company/, Employer/, Job/
│   ├── Services/                   Business logic, grouped by domain
│   │   └── Account|Admin|Authentication|Billing|Candidate|Common|Company|Employer|Job|JobAlerts|ML
│   ├── Swagger/                    RequestExampleSchemaFilter
│   ├── Tests/                      Embedded xUnit project (excluded from app build)
│   ├── wwwroot/                    css/, js/, vendored bootstrap, runtime CV uploads (uploads/cv)
│   ├── appsettings*.json, .env.example, Program.cs
├── docs/                           Specs, plans, per-role docs, audit reports
├── project.sql                     Standalone MySQL 8 DDL reference (not used by app)
├── project.seed.example.sql        Illustrative SQL seed example (not used by app)
└── PROJECT-REVIEW.md               Short Vietnamese project overview
```

---

## 3. Domain model

Context: `hotjob/Data/ApplicationDbContext.cs` — `ApplicationDbContext : IdentityDbContext<ApplicationUser>`. All configuration is in `OnModelCreating` (lines 43–131) and `ConfigureBilling` (135–169). Convention-based table names; Identity uses `AspNet*`. **No soft delete / global query filters.** Concurrency tokens: Identity `ConcurrencyStamp` and `Payment.Status` (`IsConcurrencyToken()`, line 161).

### Entities (key fields + relationships)

| Entity (file) | Key fields | Main relationships |
|---|---|---|
| `ApplicationUser` (`Models/Users/ApplicationUser.cs`) | `Id string`, `CreatedAt`, `IsActive` | 1:1 `Candidate`?, 1:1 `Employer`?; `Notifications`, `RefreshTokens` |
| `Candidate` (`Models/Users/Candidate.cs`) | `Id int`, `UserId`, `FullName`, `DateOfBirth`, `Gender`, `JobSeekingStatus`, `PreferredWorkModel`, `PreferredLocation`, `PremiumPlanId?`, `PremiumUntil?`, `BoostUntil?`, `IsDiscoverable` | → `ApplicationUser`, → `Plan` (Restrict), `Resumes`, `Applications`, `SavedJobs`, `Reviews` |
| `Employer` (`Models/Users/Employer.cs`) | `Id`, `UserId`, `CompanyProfileId`, `FullName`, `Position`, `IsOwner`, `MembershipStatus`, `JoinedAt` | → User, → Company (Restrict), `CreatedJobs` |
| `CompanyProfile` (`Models/Users/CompanyProfile.cs`) | `Id`, `CompanyName` (unique), `Website`, `Location`, `Industry`, `Scale`, `Status`, `RejectionReason`, `VerifiedAt?`, `VerifiedByAdminId?`, `PlanId?`, `PlanExpiresAt?` | `Employers`, `JobPostings`, `Reviews`, → `Plan` (Restrict), → Admin (SetNull) |
| `RefreshToken` (`Models/Users/RefreshToken.cs`) | `Id`, `TokenHash` (unique SHA-256), `UserId`, `CreatedAtUtc`, `ExpiresAtUtc`, `RevokedAtUtc?`, `ReplacedByTokenHash?`, `RevokedReason?`; `IsActive(now)` | → User (Cascade) |
| `Skill` / `ResumeSkill` / `JobSkill` (`Models/Skill.cs`, `Models/Resume/ResumeSkill.cs`, `Models/Job/JobSkill.cs`) | Skill `Id`, `Name` (unique), `Category`; joins carry `YearsOfExperience` and `ImportanceWeight` (0–10) + `MinYears` (0–50) | `Skill`→joins **Restrict** (migration `RestrictSkillDeletes`); joins→Resume/Job Cascade |
| `JobPosting` (`Models/Job/JobPosting.cs`) | `Id`, `CompanyProfileId`, `CreatedByEmployerId?`, `Title`, `Description`, `SalaryMin/Max` (decimal 18,2), `ExperienceRequired`, `JobType`, `ExpiryDate?`, `Status`, `FeaturedUntil?` | → Company (Cascade), → Employer (SetNull), `JobSkills`, `CategoryMappings`, `Applications`, `SavedBy`, `MatchScores`, `Payments` (SetNull) |
| `JobCategory` / `JobCategoryMapping` (`Models/Job/`) | Category `Id`, `Name` (unique); join composite PK `(JobPostingId, JobCategoryId)` | many-to-many, both Cascade |
| `Resume` (`Models/Resume/Resume.cs`) | `Id`, `CandidateId`, `Title`, `Summary?`, `FileUrl?`, `IsPrimary`, `CreatedAt` | → Candidate (Cascade), `Educations`, `WorkExperiences`, `ResumeSkills`, `Applications` (Restrict), `MatchScores` |
| `Education` / `WorkExperience` (`Models/Resume/`) | `Id`, `ResumeId`, school/company, dates, `Gpa?`, description | → Resume (Cascade) |
| `Application` (`Models/Application/Application.cs`) | `Id`, `JobPostingId`, `ResumeId`, `CandidateId`, `CoverLetter?`, `CurrentStatus`, `SnapshotFileUrl?`, `SnapshotSummary?`, **`SnapshotJson?` (longtext)**, `AppliedAt` | → Job (Cascade), → Resume (Restrict), → Candidate (Restrict), `StatusHistory`, `Interviews`; unique `(JobPostingId, CandidateId)` |
| `ApplicationStatusHistory` (`Models/Application/`) | `Id`, `ApplicationId`, `Status`, `ChangedByUserId?`, `Notes?`, `ChangedAt` | → Application (Cascade), → User (SetNull) |
| `Interview` (`Models/Application/Interview.cs`) | `Id`, `ApplicationId`, `InterviewDate`, `LocationOrLink?`, `InterviewType?`, `Notes?`, `Status` | → Application (Cascade) |
| `Plan` (`Models/Billing/Plan.cs`) | `Id`, `Audience`, `Code` (unique), `Name`, `Price` (18,0 VND), `JobQuota`, `DurationDays` (0 = perpetual), `IsActive`, `SortOrder`; constants `FREE`, `STARTER`, `GROWTH`, `PREMIUM_MONTHLY` | referenced by Company/Candidate/Payment (Restrict) |
| `Payment` (`Models/Billing/Payment.cs`) | `Id`, `PayerUserId`, `CompanyProfileId?`, `CandidateId?`, `Purpose`, `PlanId?`, `JobPostingId?`, `Amount`, **`Status` (concurrency token)**, `CreatedAt/PaidAt`, `VnpTxnRef` (unique), `VnpTransactionNo`, `VnpResponseCode`, `VnpPayDate`, `Note` | all FKs Restrict except Job (SetNull) so billing history survives deletions |
| `MatchScore` (`Models/ML/MatchScore.cs`) | composite `(JobPostingId, ResumeId)`, `MatchPercentage` (0–100), `AlgorithmUsed?`, `CalculatedAt` | → Job + Resume (Cascade) |
| `ModelTrainingLog` (`Models/ML/ModelTrainingLog.cs`) | `Id`, `RecordCount`, `AccuracyScore` (0–1), `ModelFilePath`, `TrainedByUserId?`, `TrainedAt`, `IsActive` | → User (SetNull) |
| `Notification` (`Models/Notification.cs`) | `Id`, `UserId`, `Title` (200), `Content?` (2000), `Kind?` (40), `RelatedJobPostingId?`, `IsRead`, `CreatedAt`; `NotificationKinds.JobAlert = "JobAlert"` | → User (Cascade), → Job (SetNull); unique `(UserId, Kind, RelatedJobPostingId)` |
| `Review` (`Models/Review.cs`) | `Id`, `CompanyProfileId`, `CandidateId`, `RatingScore` (1–5), `ReviewText?`, `CreatedAt` | → Company + Candidate (Cascade); unique `(CompanyProfileId, CandidateId)` |
| `SavedJob` (`Models/SavedJob.cs`) | composite `(CandidateId, JobPostingId)`, `SavedAt` | → Candidate + Job (Cascade) |

`Models/Application/ResumeSnapshot.cs` holds four **non-EF records** (`ResumeSnapshot`, `SnapshotEducation`, `SnapshotWorkExperience`, `SnapshotSkill`) serialized into `Application.SnapshotJson` at apply time.

### Enums (all persisted as int)

| Enum | File | Values |
|---|---|---|
| `ApplicationStatus` | `Models/Application/Application.cs` | `Pending, Viewed, Shortlisted, Interviewing, Offered, Rejected, Withdrawn` |
| `InterviewStatus` / `InterviewType` | `Models/Application/Interview.cs` | `Scheduled, Completed, Cancelled` / `Online, Onsite, Phone` |
| `JobStatus` / `JobType` | `Models/Job/JobPosting.cs` | `Draft, Published, Closed, Expired` / `FullTime, PartTime, Contract, Internship, Freelance, Remote` |
| `CompanyStatus` / `CompanyScale` | `Models/Users/CompanyProfile.cs` | `Pending, Approved, Rejected` / `LessThan10, From10To49, From50To199, From200To499, MoreThan500` |
| `MembershipStatus` / `Gender` | `Models/Users/` | `Pending, Active, Rejected` / `Male, Female, Other, PreferNotToSay` |
| `PaymentStatus` / `PaymentPurpose` / `PlanAudience` | `Models/Billing/` | `Pending, Paid, Failed, Expired` / `EmployerPlan, EmployerFeatured, CandidatePremium` / `Employer, Candidate` |
| `ServiceErrorType` / `AccountType` | `Services/Common/`, `DTOs/Authentication/` | `Validation, NotFound, Conflict, Unauthorized` / `Candidate=1, Employer=2` (DTO-only) |

### State machines (enforced in code)

**Application** (`Services/Employer/ApplicantService.cs:140-158`, `IsTransitionAllowed`):
```
Pending → Viewed → Shortlisted → Interviewing → Offered
Rejected reachable from Pending|Viewed|Shortlisted|Interviewing
Offered / Rejected / Withdrawn are terminal; employers cannot set Withdrawn
same-status is a no-op (no history row)
```
- Candidate withdraw (`Services/Candidate/ApplicationService.cs:245-263`): allowed from any non-terminal status, writes `Withdrawn` + history "Withdrawn by candidate".
- Interview scheduling (`InterviewService.cs:54-66`): from `Shortlisted` auto-advances to `Interviewing` + history; blocked in terminal states.
- Job close (`JobService.cs:207-230`): `Draft` is hard-deleted; otherwise status → `Closed` and all open applications forced to `Rejected` with history.
- Every change writes `ApplicationStatusHistory` with actor (`ChangedByUserId`), notes, timestamp.

**Payment** (`BillingService.cs:202-245`): only `Pending` is mutable → `Expired` outside `Billing:PaymentWindowMinutes` (15 min), `Failed` on non-`00` gateway response, `Paid` + `PaidAt` + entitlement grant on success; non-Pending callback returns `RspCode=02` (idempotent); `DbUpdateConcurrencyException` on the `Status` token is treated as already-confirmed.

**Other**: `CompanyStatus` any value allowed, `Rejected` requires reason and stamps verifier (`AdminService.cs:194-218`); `JobStatus.Expired` **is** set by `JobExpiryService.ExpireAsync` (15-min sweep) — note the audit doc's older claim of no code transition is wrong; `InterviewStatus` accepts any defined enum value with no transition guard and no code sets `Completed`.

### Migrations (13, `hotjob/Migrations/`)

`InitialCreate` (baseline), `AddUniqueCompanyName`, `AddRefreshTokens`, `EmployerApplicant` *(empty)*, `AdminSeeding` *(empty)*, `CandidateProfilePrefs`, `AddPaymentCore` *(Plans/Payments, billing fields)*, `AddModelTrainingLogIsActive`, `AddJobAlertNotifications` *(Kind + RelatedJobPostingId)*, `AddCandidateIsDiscoverable`, `RestrictSkillDeletes`, `JobSkillMinYears` *(per-skill match scoring)*, `AddApplicationSnapshotJson` *(SnapshotJson longtext, 2026-11-23)*.

---

## 4. Services & controllers

### Services (`hotjob/Services/`)

| Area | Service | Responsibility |
|---|---|---|
| Account | `AccountService` (`Account/`) | Registration (Candidate/Employer, join or create Pending company), profile get/update, password change (revokes sessions), upgrade to employer. |
| Admin | `AdminService` (`Admin/`) | Dashboard counts, paged user/company/job lists, activation toggle, company approve/reject, job delete, training-log list, skill CRUD (blocks in-use deletes). |
| Auth | `AuthService`, `TokenService`, `HotJobAuthStateProvider`/`AuthSession`, `JwtOptions` (`Authentication/`) | Login/lockout/disabled checks, Google link/create, refresh rotation with reuse detection, session restore; HMAC-SHA256 access JWT + 256-bit refresh tokens with SHA-256 hashes; Blazor auth state. |
| Billing | `BillingService`, `VnPayService`, `EntitlementService`, `CandidatePremiumService` (`Billing/`) | Checkout creation + idempotent callback processing + entitlement grants; VNPay signing/verification; effective plan/quota; Premium gate. |
| Candidate | `CandidateService`, `ResumeService`, `ApplicationService`, `CvFileStorage`, `NotificationFeed` (`Candidate/`) | Own applications/notifications; resume CRUD (max 5) + match invalidation; apply with frozen snapshot + withdraw; singleton file store with magic-byte validation (PDF/DOC/DOCX, 5 MB, path-traversal guard); per-circuit DB gate wrapper. |
| Common | `ServiceResult`, `CircuitDbGate` (`Common/`) | Result/error envelope; `SemaphoreSlim(1,1)` serializing DB use within a Blazor circuit. |
| Company (public) | `CompanyService` (`Company/`) | Approved-company directory, filters, detail + rating, candidate reviews. |
| Employer | `ApplicantService`, `InterviewService`, `NotificationService`, `CompanyService`, `CandidateSearchService`, `EmployerGuard`, `ApplicantMapping` (`Employer/`) | ATS inbox + status transitions; interview create/update/cancel + notifications; manual messages; own-company profile/members; discoverable-candidate search with premium boost; active-employer/approved-company guard; entity→DTO mapping incl. snapshot fallback. |
| Job | `JobService`, `JobExpiryService` (`Job/`) | Job CRUD/quota/ownership, browse/detail, saved jobs, skill matching, job-alert publishing; expiry background sweep. |
| Job alerts | `JobAlertService` (`JobAlerts/`) | On publish, score active candidates and create deduped `JobAlert` notifications above `MinMatchPercent`; read/mark alert feed. |
| ML | `MatchScoreService`, `MatchFeatureExtractor`, `MatchModelStore`, `SyntheticMatchTrainingService` (`ML/`) | Weighted + FastTree scoring, Premium-gated breakdown, model load/save atomic replace, dev synthetic training. |

**Repositories** (`hotjob/Repositories/`): `CompanyRepository` (public directory queries), `JobPostingRepository` (owned/published job queries + browse filters), `ApplicantRepository` (all ATS reads/writes, ownership via `Application.JobPosting.CompanyProfileId`, transactions).

**Middleware/filters**: `Middleware/ApiExceptionHandler.cs` writes JSON `ApiResponse` 500 for `/api` paths; `Controllers/ApiControllerBase.cs` maps `ServiceResult` → HTTP status + envelope; `Controllers/ApiValidationEnvelopeAttribute.cs` (`Order=-2001`) keeps automatic model-validation errors in the envelope.

### API controllers (`hotjob/Controllers/`)

| Controller | Route prefix / auth | Endpoints |
|---|---|---|
| `AuthController` | `api/auth`, `[AllowAnonymous]`, `[IgnoreAntiforgeryToken]` | `POST login`, `GET google`, `GET google/callback`, `POST refresh`, `GET me`, `POST logout` |
| `AccountController` | `api/account` | `GET`/`PUT profile` `[Authorize]`, `POST change-password` `[Authorize]`, `POST register` anonymous, `POST become-employer` `[Authorize]` |
| `AdminSkillsController` | `api/admin/skills`, `[Authorize(Roles="Admin")]` | `GET` (paged), `GET {id}`, `POST`, `PUT {id}`, `DELETE {id}` |
| `CompaniesController` | `api/companies`, `[AllowAnonymous]` | `GET` (filters/paging), `GET filters` |
| `EmployerApplicationsController` (file `EmployerApplicantsController.cs`) | `api/employer/applications`, `[Authorize(Roles="Employer")]` | `GET` inbox, `GET {id}`, `PATCH {id}/status`, `POST {id}/notify`, `POST`/`GET {id}/interviews` |
| `EmployerCompanyController` | `api/employer/company`, Employer | `GET`, `PUT`, `POST members/{id}/approve`, `POST members/{id}/reject` |
| `EmployerInterviewsController` | `api/employer/interviews`, Employer | `PUT {id}`, `DELETE {id}` (soft cancel) |
| `EmployerJobsController` | `api/employer/jobs`, Employer | `POST`, `GET` paged, `GET {id}`, `PUT {id}`, `DELETE {id}`, `PUT {id}/categories`, `PUT {id}/skills` |
| `JobSearchController` | `api/jobs`, `[Authorize]` | `GET matching-resume/{resumeId}` |
| `VnPayController` | `api/vnpay`, `[AllowAnonymous]` | `GET ipn`, `GET return` |
| `DevMatchModelController` | `api/dev/match-model`, Admin | `POST seed-and-train` (404 unless Development **and** `DevTools:SyntheticTrainingEnabled=true`) |

Candidates' resume/application/notification, job-alert, match and billing-checkout operations are **not** separate API endpoints — Blazor Server components call the services directly. Only VNPay IPN/return are externally callable for billing.

**Pipeline / DI** (`Program.cs`): `.env.local` loader → Razor components + controllers + Swagger → options validation (`ValidateOnStart` for Jwt/Billing/MatchScore/JobAlerts) → Identity → JWT (after Identity deliberately, so Bearer is default) → optional Google → CORS `"Frontend"` → MySQL DbContext → scoped repositories/services + singleton `CvFileStorage`/`MatchModelStore` → `AddHostedService<JobExpiryService>`. Startup scope runs `MigrateAsync` + three seeders. Middleware order: env-specific exception handler/HSTS → status-code re-exec (`/not-found`) → HTTPS redirect → static files → `UseExceptionHandler()` → CORS → auth → authorization → antiforgery → controllers → Swagger (dev) → static assets → Razor components.

---

## 5. UI pages / routes (`hotjob/Components/`)

Render mode: global Interactive Server (see §1). `RedirectToLogin`, `NavMenu`, all Admin/Employer pages and most Candidate pages declare `@rendermode InteractiveServer` explicitly. `CompanyDetail.razor` has `@attribute [StreamRendering]` but it is inert under global Interactive Server. **No page uses `[Authorize]`** — gating is in-markup via `<AuthorizeView Roles="...">`; `Routes.razor`'s `<NotAuthorized>` fallback is therefore effectively unreachable from pages.

| Role | File | Route | Purpose |
|---|---|---|---|
| Public | `Pages/Home.razor` | `/` | Landing page + hero search → `/it-jobs` |
| Public | `Pages/Jobs.razor` | `/it-jobs` | Browse/filter jobs (salary login-gated) |
| Public | `Pages/JobDetail.razor` | `/jobs/{id:int}` | Job detail, skills, save/match/apply controls |
| Public | `Pages/Companies.razor` | `/companies` | Company directory + filters |
| Public | `Pages/CompanyDetail.razor` | `/companies/{id:int}` | Company profile + reviews (`[AllowAnonymous]`) |
| Public | `Pages/Error.razor` | `/Error` | Exception handler target |
| Public | `Pages/NotFound.razor` | `/not-found` | 404 page (Router `NotFoundPage`) |
| Public | `Pages/PaymentResult.razor` | `/payment/result` | VNPay return display from query string |
| Auth | `Pages/LoginPage.razor` | `/login` | Email/password + Google button |
| Auth | `Pages/RegisterPage/RegisterPage.razor` | `/register` | Candidate registration |
| Auth | `Pages/BecomeEmployerPage/BecomeEmployerPage.razor` | `/become-employer` | Candidate → employer upgrade |
| Candidate | `Pages/Profile.razor` | `/profile` | Profile/CV skills, experience, avatar, discoverability *(no `AuthorizeView`; code-only auth)* |
| Candidate | `Pages/MyApplications.razor` | `/my-applications` | Application tracker + status timeline |
| Candidate | `Pages/Notifications.razor` | `/notifications` | Notification feed, mark-all-read |
| Candidate | `Pages/RecommendedJobs.razor` | `/recommended-jobs` | Skill-coverage ranked jobs |
| Candidate | `Pages/SavedJobs.razor` | `/saved-jobs` | Bookmarked jobs |
| Candidate | `Pages/JobAlerts.razor` | `/job-alerts` | Alert hit feed |
| Candidate | `Pages/Premium.razor` | `/premium` | Premium purchase via VNPay |
| Candidate | `Pages/WriteReview.razor` | `/companies/{id:int}/review` | Company rating/review form |
| Employer | `Pages/Employer/Jobs.razor` | `/employer/jobs` | Job CRUD + quota |
| Employer | `Pages/Employer/Candidates.razor` | `/employer/candidates` | Discoverable candidate search |
| Employer | `Pages/Employer/Interviews.razor` | `/employer/interviews` | ATS pipeline + interviews (PageTitle "Applicants") |
| Employer | `Pages/Employer/Company.razor` | `/employer/company` | Company profile + members |
| Employer | `Pages/Employer/Billing.razor` | `/employer/billing` | Plans, entitlement, VNPay checkout |
| Admin | `Pages/Admin/AdminDashboard.razor` | `/admin` | Users/companies/jobs metrics |
| Admin | `Pages/Admin/AdminAccounts.razor` | `/admin/accounts` | Search users, disable/reactivate |
| Admin | `Pages/Admin/AdminCompanies.razor` | `/admin/companies` | Approve/reject companies |
| Admin | `Pages/Admin/AdminJobs.razor` | `/admin/jobs` | Review/remove jobs |
| Admin | `Pages/Admin/AdminModel.razor` | `/admin/model` | Training logs + retrain |

Shared/root: `App.razor` (HTML shell, CSS/JS), `Routes.razor` (Router + `AuthorizeRouteView`), `_Imports.razor`, `RedirectToLogin.razor`, `Layout/MainLayout.razor`, `Layout/NavMenu.razor`, `Layout/Footer.razor`, `Layout/ReconnectModal.razor`. `Layout/RegisterLayout.razor` is unreferenced (dead code; only its stylesheet is linked).

---

## 6. Configuration & seeded data

### `hotjob/appsettings.json` keys (secrets **redacted**)

| Section | Keys |
|---|---|
| `ConnectionStrings` | `DefaultConnection` — MySQL, **required** (`Program.cs:192-193` throws if absent) |
| `Jwt` | `SecretKey` (**secret**, ≥32 chars, validated on start), `Issuer=HotJobServer`, `Audience=HotJobClient`, `AccessTokenMinutes=60`, `RefreshTokenDays=7` |
| `Google` | `ClientId`, `ClientSecret` (empty ⇒ Google auth disabled; **secrets when set**) |
| `VnPay` | `TmnCode`, `HashSecret` (**secrets**; empty ⇒ checkout disabled), `BaseUrl` (sandbox), `ReturnUrl`, `IpnUrl`, `Version=2.1.0`, `Locale=vn` |
| `Billing` | `FeaturedPrice=200000`, `FeaturedDays=7`, `PaymentWindowMinutes=15` |
| `MatchScore` | `MinTrainingRecords=50`, `ModelPath=App_Data/models/matchscore.zip` |
| `DevTools` | `SyntheticTrainingEnabled=false` |
| `JobAlerts` | `MinMatchPercent=60` |
| `Seed` | `Admins[0..2]` email/password (**secrets**), `Demo` company/employer/candidate credentials (**secrets**) |
| `Cors` | `AllowedOrigins` = `http://localhost:3000`, `http://localhost:5173` |
| `Logging`, `AllowedHosts` | standard; `AllowedHosts=*` |

`appsettings.Development.json` only sets `DetailedErrors=true` and log levels. `hotjob/.env.example` is the env template; `Program.cs:31-46` loads `.env.local` (content root → current dir → `hotjob/`) with `KEY=VALUE`, `export` and `__`→`:` handling, overriding appsettings. Required secrets to supply for a real deployment: DB password, `Jwt__SecretKey`, `VnPay__TmnCode`/`__HashSecret` (payments), `Google__*` (optional login), seed passwords. `launchSettings.json`: http `http://localhost:5168`, https `https://localhost:7023` (+ http 5168).

### Seeded data (runs automatically at startup, `Program.cs:242-269`)

- **Roles**: `Admin`, `Candidate`, `Employer` (`Data/IdentitySeeder.cs:8-13`).
- **Admin accounts**: 3 from `Seed:Admins` (`admin1..3@example.com`, role `Admin`, `EmailConfirmed=true`); legacy single-admin fallback via `Seed:AdminEmail/Password`.
- **Demo accounts**: `Seed:Demo` employer/candidate (`employer@example.com`, `candidate@example.com`) plus `Seed:CandidateEmail` candidate.
- **Plans** (`DatabaseSeeder.DefaultPlans()`): Free (employer, 0 VND, 1 job, perpetual), Starter (500,000 VND, 5 jobs, 30 d), Growth (1,500,000 VND, 20 jobs, 30 d), Premium Monthly (candidate, 99,000 VND, 30 d). Inserted by `Code`; admin edits are never overwritten.
- **Categories**: 10 (`IT - Software`, `Data & Analytics`, `DevOps & Cloud`, `Mobile`, `QA & Testing`, `Product`, `Design`, `Marketing`, `Cybersecurity`, `IT Support`).
- **Skills**: ~64 across Backend/Frontend/Database/Mobile/DevOps/Cloud/Messaging/Data/QA/Design/Product/Marketing etc. (`DatabaseSeeder.SkillCatalog`).
- **Demo dataset** (`Data/DemoSeeder.cs`, gated on full `Seed:Demo`): approved `Acme Corp` + owner, 4 jobs, demo candidate + resume + applications; plus 50 real-ish Vietnamese tech companies, 26 job templates, 12 catalog candidates, 6 reviewers with reviews. All idempotent.
- Root `project.sql` / `project.seed.example.sql` are documentation-only DDL/seed samples; the app never executes them.

---

## 7. How to run

1. **MySQL 8** reachable at the connection string; DB user must be able to create tables (app calls `Database.MigrateAsync()` on every start). Optional: `mysql -u root -p -e "CREATE DATABASE hotjob CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;"` or import `project.sql`.
2. Copy `hotjob/.env.example` → `hotjob/.env.local` and fill real values (DB password, JWT secret, VNPay/Google if used, seed passwords). Alternatively set env vars with `__` separators or user-secrets.
3. Commands from repo root `C:\Users\phulo\Desktop\HotJob` (PowerShell):
   ```powershell
   dotnet restore
   dotnet build
   dotnet run --project hotjob/hotjob.csproj        # or: cd hotjob; dotnet run
   dotnet test
   ```
   HTTP profile: `http://localhost:5168`; HTTPS profile: `https://localhost:7023`. Swagger UI at `/swagger` in Development only.
4. Migrations: applied automatically at startup; design-time factory `Data/DesignTimeDbContextFactory.cs` supports `dotnet ef migrations add <Name> --project hotjob/hotjob.csproj` (requires `dotnet-ef` 9.x). `Database.MigrateAsync()` replaces the need for manual `dotnet ef database update`.
5. Runtime artifacts (gitignored): CV uploads `hotjob/wwwroot/uploads/cv`, trained model `hotjob/App_Data/models/matchscore.zip`.
6. **No Docker** — no Dockerfile, compose file, or `.dockerignore` anywhere in the repo.

---

## 8. Tests

- Project: `hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj`, xUnit 2.9.3, 14 folders (Admin, Auth, Billing, Candidate, Common, Company, Employer, JobAlert, Job, ML, Profile, Register, Seed, Support).
- **61 `.cs` files**: 56 with test methods + 5 support helpers (`TestFixture`, `TempModelDirectory`, `StubJobAlertService`, `MatchTestData`, `FakeTimeProvider`).
- **391 test methods**: 367 `[Fact]` + 24 `[Theory]`; 84 `[InlineData]` rows; no skipped tests authored.
- Actual `dotnet test` run from repo root on the clean tree:
  ```
  Passed!  - Failed: 0, Passed: 451, Skipped: 0, Total: 451, Duration: 53 s - hotjob.Tests.dll (net10.0)
  ```
  **Result: PASS — 451/451.**
- Build warnings (NuGet audit only): `NU1902` moderate — `MySqlConnector 2.4.0` (transitive via Pomelo); `NU1903` high — `SQLitePCLRaw.lib.e_sqlite3 2.1.10` (test dependency). No build or runtime errors.

---

## 9. What changed recently (since 2026-11-23)

Only 3 commits are dated after 2026-11-23; the substantive feature work landed on 2026-11-23 itself and was documented on 24–25 Nov.

| Commit | Date | Change (verified in diff) |
|---|---|---|
| `c7c37a9` | 2026-11-23 | **Freeze full resume on apply**: adds `Application.SnapshotJson` + 4 snapshot records; `ApplicationService.ApplyAsync` now `Include`s education/work/skills and serializes the full snapshot. +test. |
| `1a5373f` | 2026-11-23 | Employer detail shows apply-time snapshot via `ApplicantDetailResponse.IsSnapshot` and `ApplicantMapping.FromSnapshot`/`DeserializeSnapshot` (falls back to current CV for legacy rows); UI label "snapshot at apply". |
| `a73f5c9` | 2026-11-23 | Status history shows actor: `HistoryDto.ChangedByName`, `ThenInclude(h => h.ChangedBy)`, UI renders email or "System". |
| `c0145c3` | 2026-11-23 | Interview change/cancel notifications: `InterviewService` injects `TimeProvider`, adds "Interview updated" and "Interview cancelled" notification rows. +83 test lines. |
| `62d13fe` | 2026-11-23 | Migration `20261123122108_AddApplicationSnapshotJson` (longtext column) + snapshot update. |
| `eea30ab` | 2026-11-23 | Favicon MIME type fix in `App.razor` after midu merge. |
| `0be7bed` / `7d78e1d` / `71823ad` | 2026-11-23→25 | Created `docs/feature-audit-2026-11-23.md`, add employer-gaps design spec + plan, then mark employer features complete and update test count 443→451. |
| `66f90c9` | 2026-11-24 | Caps interview notification content at 2000 chars via `Cap()` — applied to create and update only (see §10 #1). |
| `44ae791` | 2026-11-23 | Merge bringing in `midu`. |

**Staleness check of the 2026-11-23 audit** (`docs/feature-audit-2026-11-23.md`, itself updated by `71823ad`):
- True: full CV snapshot, history actor display, interview create/reschedule/cancel notifications, 451 tests — all present in code.
- **Stale/overstated**: candidate section (line 104) still says interview notifications are sent only on creation; "notification content limited to 2000 chars" is only true for create/update; `docs/superpowers/plans/2026-11-23-employer-gaps.md` still has 25 unchecked steps despite completed code.
- **Still accurate**: admin back-office gaps, no CV parsing, no realtime notifications (no `IHubContext`/`Hub`/polling found).

---

## 10. Known gaps / TODOs / technical debt

**Positive**: no `TODO`/`FIXME`/`HACK`/`NotImplementedException` in production code (matches are test doubles, vendored Bootstrap, or docs). No empty method bodies. `ponytail:` comments are deliberate design notes.

### High priority

1. **Committed credentials + unconditional production seeding.** `appsettings.json:3` contains a DB password, `:18` a JWT dev secret, `:52-66` admin/demo passwords; `Data/DesignTimeDbContextFactory.cs:16` also hardcodes the connection. `Program.cs:242-269` runs migrations + all three seeders in **every** environment, so a production deploy creates admin accounts with known passwords and a full demo dataset. Fix: externalize secrets, gate demo seeding to Development.
2. **Vulnerable NuGet packages**: `MySqlConnector 2.4.0` (moderate, GHSA-473q-m89c-ghf8) and `SQLitePCLRaw.lib.e_sqlite3 2.1.10` (high, GHSA-2m69-gcr7-jv3q). Upgrade via Pomelo 9.x patch / EF Core Sqlite 9.x patch and verify with a green suite.
3. **Uncapped cancel notification** — `Services/Employer/InterviewService.cs:181` bypasses `Cap()` used at `:72`/`:151`, while `Notification.Content` is `[MaxLength(2000)]` (`Models/Notification.cs:23`). Long job titles/locations risk a DB length error; the 2026-11-24 fix and audit claim are incomplete. No test covers the cancel path.
4. **Interview lifecycle edge cases** — `CancelAsync` (`:162-187`) has no idempotency guard (repeat cancel spams notifications); `UpdateAsync` (`:108-160`) allows editing/reviving a cancelled interview and sends only a generic "Interview updated"; `hasChanges` (`:126-127`) is based on field presence, so identical resubmissions notify. No tests for these cases.
5. **`NotificationService.cs:16-40`** does not cap/trim `Content` to 2000 (DTO `[MaxLength]` does not protect direct Blazor service calls).

### Medium / low priority

6. **Over-broad exception handling**: `CompanyService.cs:187-197` maps any `DbUpdateException` to "You already reviewed this company."; `JobAlertService.cs:117-134` treats any DB failure as a benign duplicate; `ApplicantMapping.cs:83-94` silently falls back to live CV on corrupt snapshot JSON; `MatchModelStore.cs:120-127` silently degrades to weighted rule.
7. **Hardcoded 15-minute VNPay URL expiry** (`VnPayService.cs:32`) duplicates `Billing:PaymentWindowMinutes` (`:209` uses the config) — changing config desynchronizes gateway URL vs server acceptance.
8. **Acknowledged perf debt**: `Components/Pages/Employer/Jobs.razor:465` — 4 round-trips for status counts, "collapse to one GROUP BY query when this gets slow".
9. **UI authorization gaps**: no page uses `[Authorize]`, so `Routes.razor`'s `<NotAuthorized>` block is unreachable; `Profile.razor` has no `<AuthorizeView>` and relies on service-layer checks. `CompanyDetail.razor`'s `[StreamRendering]` is inert under global Interactive Server.
10. **Testability/documentation drift**: `ApplicantService.cs:113,123` still uses `DateTime.UtcNow` while new code uses injected `TimeProvider`; two distinct `CompanyService` classes (`Services/Company/` and `Services/Employer/`) are easy to mis-wire; `Layout/RegisterLayout.razor` is dead code; `docs/phuong's doc/HUONG_DAN_CHAY_DU_AN.md` cites stale ports/tables; docs SQL files are unused by the app.
11. **Standing feature gaps from the 2026-11-23 audit (still true in code)**: admin back-office incomplete (no status filters/user detail/reset password/admin reset for jobs/plans/payments/audit log), no CV content parsing into Education/Skills/Experience on upload, no realtime notifications, no Premium badge on own profile (only in candidate search), basic match score free on every calculation rather than "first free".

### Verification summary
All 10 audit tasks completed against source; `dotnet test` executed on a clean checkout: **451 passed / 0 failed / 0 skipped**. Head commit at audit: `71823ad`.

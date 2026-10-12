# Remove Services/Api Loopback Clients Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete `hotjob/Services/Api/` and rewrite the 5 Blazor pages that use it to inject domain services directly, with zero behavior change.

**Architecture:** Blazor Server runs in the same process as the controllers, so each page calls `IAccountService` / `IJobService` / `IApplicantService` / `IInterviewService` / `INotificationService` / `Services.Employer.ICompanyService` directly with the circuit userId from `AuthenticationStateProvider`. Controllers stay untouched for Swagger/external clients. `Services/Common/ServiceResult<T>` (`Success`/`Data`/`Errors`) replaces `Services/Api/ApiResult<T>` (`Success`/`Data`/`Errors`/`Status`) at each call site.

**Tech Stack:** Blazor Server (.NET 10), `AuthenticationStateProvider` + `ClaimTypes.NameIdentifier ?? "sub"` for userId, xUnit + existing `Tests/hotjob.Tests/Support/TestFixture.cs`.

**Spec:** This plan implements the decision in the 2026-10-12 chat: "Api folder within service is redundant" — `Services/Api/ApiClientBase.cs:96,183` loopback HTTP adds socket + double JSON per call; `Components/Pages/Companies.razor:9`, `Jobs.razor:5`, `JobDetail.razor:6` already prove direct injection works.

## Global Constraints

- .NET 10, Blazor Server InteractiveServer (no WASM/prerender-JS dependency in converted pages).
- userId source in every converted page: `ClaimTypes.NameIdentifier ?? "sub"` from `AuthenticationStateProvider.GetAuthenticationStateAsync()`, null/empty → "Please sign in" error path (same UX as today).
- `ServiceResult<T>` error display: `result.Errors.FirstOrDefault() ?? "<existing fallback string>"` — keep each page's current fallback string verbatim.
- Controllers (`hotjob/Controllers/*`) are NOT modified — external/Swagger surface stays.
- `hotjob/DTOs/ServiceResult.cs` (dead, zero references per grep) is deleted as part of this plan, not kept.
- `dotnet build hotjob/hotjob.csproj` 0 warnings/0 errors + `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj` all-pass after every task.
- One task per commit; commit message `refactor: <task name>`.

## Review Focus

- Anonymous Register page calling a service method that requires a userId — expect Register to pass no userId (service signature has none) and still return 201-shaped data.
- Employer page loaded by a Candidate (wrong role) — expect the existing `<AuthorizeView Roles="Employer">` + `RedirectToLogin` guard to stay, service never called.
- Overlapping rapid Search/Page clicks on Jobs/Interviews/Company — expect the existing `if (isLoading && hasLoadedOnce) return;` guard to stay verbatim; no Task.WhenAll introduced.
- `UpdateJobRequest` downcast `(UpdateJobRequest)jobForm` in Employer Jobs Save — expect identical cast logic, no behavior change.
- 401/unauthorized from a direct service call (e.g. expired membership) — expect the message rendered in the page's existing error slot, not a redirect loop.

---

### Task 1: Fix pre-broken JobDetailResponse test constructors (unblocks test suite)

**Files:**
- Modify: `hotjob/Tests/hotjob.Tests/ApiClientTests/EmployerApiClientTests.cs:41,58,90`
- Test: `hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj` (full suite)

**Interfaces:**
- Consumes: `hotjob/DTOs/Job/JobDtos.cs:65-81` — `JobDetailResponse(..., int ApplicationsCount, DateTime CreatedAt, string? CompanyName, ... string? CompanyLocation)` (positional; `CompanyLocation` added by commit f8ffb14, tests still pass 16 args).
- Produces: green baseline suite every later task builds on.

- [ ] **Step 1: Confirm the failure**

Run: `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --nologo -v q`
Expected: FAIL with `CS7036: There is no argument given ... 'CompanyLocation'` x3.

- [ ] **Step 2: Add the missing positional argument**

In `EmployerApiClientTests.cs`, append `, null` (CompanyLocation) to the three `new JobDetailResponse(...)` constructions at lines 41, 58, 90. No other change.

- [ ] **Step 3: Run the full suite to verify green baseline**

Run: `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --nologo -v q`
Expected: PASS, 0 failed.

- [ ] **Step 4: Commit**

```bash
git add hotjob/Tests/hotjob.Tests/ApiClientTests/EmployerApiClientTests.cs
git commit -m "refactor: fix JobDetailResponse test constructors"
```

---

### Task 2: Convert RegisterPage to IAccountService

**Files:**
- Modify: `hotjob/Components/Pages/RegisterPage/RegisterPage.razor:1-10,120-135`
- Test: existing `hotjob/Tests/hotjob.Tests/RegisterTests/` (service-level, no new test needed; page has no bUnit harness)

**Interfaces:**
- Consumes: `hotjob/Services/Account/IAccountService.cs:8-10` — `Task<ServiceResult<RegisteredAccountResponse>> RegisterAsync(RegisterAccountRequest request, CancellationToken = default)`.
- Produces: Register flow with identical UX (success message + form reset, errors joined).

- [ ] **Step 1: Swap the injection and call**

Replace `@using hotjob.Services.Api` + `@inject AccountApiClient AccountApi` with `@using hotjob.Services.Account` + `@inject IAccountService AccountService`. Replace `await AccountApi.RegisterAsync(form)` with `await AccountService.RegisterAsync(form)`. Keep `result.Success`, `result.Data`, `result.Errors` handling verbatim (`ServiceResult<T>` exposes the same three members).

- [ ] **Step 2: Build the project**

Run: `dotnet build hotjob/hotjob.csproj --nologo -v q --no-incremental`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Run Register + Account controller tests**

Run: `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --nologo -v q --filter "FullyQualifiedName~Register|FullyQualifiedName~Account"`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add hotjob/Components/Pages/RegisterPage/RegisterPage.razor
git commit -m "refactor: RegisterPage uses IAccountService directly"
```

---

### Task 3: Convert Profile to IAccountService

**Files:**
- Modify: `hotjob/Components/Pages/Profile.razor:1-10,133-191`
- Test: existing `hotjob/Tests/hotjob.Tests/ProfileTests/` (service-level)

**Interfaces:**
- Consumes: `IAccountService.GetProfileAsync(string userId, CancellationToken)` and `UpdateProfileAsync(string userId, UpdateProfileRequest, CancellationToken)` (`IAccountService.cs:12-19`); userId via `AuthenticationStateProvider.GetAuthenticationStateAsync()` → `ClaimTypes.NameIdentifier ?? "sub"`.
- Produces: Profile load/save with identical UX (loading/error states, save message, `RefreshIdentity` on save).

- [ ] **Step 1: Swap injection to IAccountService + AuthenticationStateProvider**

Replace `@using hotjob.Services.Api` + `@inject AccountApiClient AccountApi` with `@using hotjob.Services.Account`, `@using System.Security.Claims`, `@inject IAccountService AccountService`, `@inject AuthenticationStateProvider AuthenticationStateProvider` (keep existing `@inject HotJobAuthStateProvider AuthStateProvider` for `RefreshIdentity`). Add a private helper `async Task<string?> GetUserIdAsync()` returning `FindFirstValue(NameIdentifier) ?? FindFirstValue("sub")`.

- [ ] **Step 2: Rewrite OnInitializedAsync load path**

Resolve userId first; null/white-space → `errorMessage = "Please sign in to view your profile."`, `isLoading = false`, return. Else `await AccountService.GetProfileAsync(userId)`. Keep the candidate-mapping block (lines 144-157) and error fallback verbatim.

- [ ] **Step 3: Rewrite SaveProfileAsync**

Resolve userId; null/white-space → return silently (matches pre-ApiClient behavior). Else `await AccountService.UpdateProfileAsync(userId, new UpdateProfileRequest { ... })` with the existing property mapping. Keep `RefreshIdentity`, field re-sync, and saveMessage logic verbatim.

- [ ] **Step 4: Build**

Run: `dotnet build hotjob/hotjob.csproj --nologo -v q --no-incremental`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 5: Run Profile tests**

Run: `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --nologo -v q --filter "FullyQualifiedName~Profile"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add hotjob/Components/Pages/Profile.razor
git commit -m "refactor: Profile uses IAccountService directly"
```

---

### Task 4: Convert Employer/Company to Services.Employer.ICompanyService

**Files:**
- Modify: `hotjob/Components/Pages/Employer/Company.razor:1-10,186-278`
- Test: existing `hotjob/Tests/hotjob.Tests/CompanyTests/EmployerCompanyControllerTests.cs` + `CompanyServiceTests.cs` (service-level)

**Interfaces:**
- Consumes: `hotjob/Services/Employer/ICompanyService.cs:12-41` — `GetPageAsync(string? userId, CancellationToken)`, `UpdateAsync(string? userId, UpdateCompanyRequest, CancellationToken)`, `ApproveMemberAsync(string? userId, int memberId, CancellationToken)`, `RejectMemberAsync(...)`; userId via AuthenticationStateProvider (nullable `string?` accepted by all four).
- Produces: Company page with identical tabs/UX (load error, savedMessage, approve/reject notice, `LoadAsync` after each mutation).

- [ ] **Step 1: Swap injection**

Replace `@using hotjob.Services.Api` + `@inject CompanyApiClient CompanyApi` with `@using hotjob.Services.Employer`, `@using System.Security.Claims`, `@inject ICompanyService CompanyService`, `@inject AuthenticationStateProvider AuthenticationStateProvider`. Alias risk: `Components/_Imports.razor:11` has `@using hotjob.Services.Account`; `ICompanyService` here must resolve to `hotjob.Services.Employer.ICompanyService` — add the using inside this file (file-level using wins for this component). Fully qualify if ambiguous.

- [ ] **Step 2: Rewrite LoadAsync/SaveAsync/ApproveAsync/RejectAsync**

At the top of each method resolve `var userId = (await AuthenticationStateProvider.GetAuthenticationStateAsync()).User.FindFirstValue(ClaimTypes.NameIdentifier) ?? ... "sub"`. Replace `CompanyApi.GetPageAsync()` → `CompanyService.GetPageAsync(userId)`, `UpdateAsync(companyForm)` → `UpdateAsync(userId, companyForm)`, `ApproveMemberAsync(memberId)` → `ApproveMemberAsync(userId, memberId)` (same for Reject). Keep `result.Success`/`result.Data`/`result.Errors.FirstOrDefault() ?? "<fallback>"` handling and all messages verbatim. Keep the `isLoading && hasLoadedOnce` guard verbatim.

- [ ] **Step 3: Build**

Run: `dotnet build hotjob/hotjob.csproj --nologo -v q --no-incremental`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Run Company tests**

Run: `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --nologo -v q --filter "FullyQualifiedName~Company"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add hotjob/Components/Pages/Employer/Company.razor
git commit -m "refactor: Employer Company page uses ICompanyService directly"
```

---

### Task 5: Convert Employer/Jobs to IJobService

**Files:**
- Modify: `hotjob/Components/Pages/Employer/Jobs.razor:1-10,195-368`
- Test: existing `hotjob/Tests/hotjob.Tests/JobTests/` + controller tests exercising `IJobService`

**Interfaces:**
- Consumes: `hotjob/Services/Job/IJobService.cs:9-48` — `ListAsync(userId, status, search, page, pageSize, CancellationToken)`, `GetAsync(userId, id, CancellationToken)`, `CreateAsync(userId, request, CancellationToken)`, `UpdateAsync(userId, id, request, CancellationToken)`, `DeleteOrCloseAsync(userId, id, CancellationToken)`.
- Produces: Jobs page with identical UX including the 5-call counts pattern (1 list + 4 status counts, sequential awaits, `ponytail` comment kept), the `(UpdateJobRequest)jobForm` downcast kept verbatim, and all messages kept verbatim.

- [ ] **Step 1: Swap injection**

Replace `@using hotjob.Services.Api` + `@inject EmployerJobsApiClient JobsApi` with `@using hotjob.Services.Job`, `@using System.Security.Claims`, `@inject IJobService JobService`, `@inject AuthenticationStateProvider AuthenticationStateProvider`.

- [ ] **Step 2: Add userId helper + rewrite LoadAsync**

Add `async Task<string> GetUserIdAsync()` (empty-string fallback matches `EmployerJobsController.CurrentUserId()` semantics for service guard). In `LoadAsync`, resolve once, then replace the 5 calls: `JobsApi.ListAsync(statusFilter, search, page, PageSize)` → `JobService.ListAsync(userId, statusFilter, search, page, PageSize)`; the four count calls likewise with `(JobStatus.X, null, 1, 1)`. Keep sequential awaits, guard, and `ponytail` comment verbatim.

- [ ] **Step 3: Rewrite OpenEditFormAsync/SaveAsync/ViewDetailAsync/DeleteOrCloseAsync**

Resolve userId at the top of each; map `JobsApi.GetAsync(id)` → `JobService.GetAsync(userId, id)`, `CreateAsync(jobForm)` → `CreateAsync(userId, jobForm)`, `UpdateAsync(id, (UpdateJobRequest)jobForm)` → `UpdateAsync(userId, id, (UpdateJobRequest)jobForm)`, `DeleteOrCloseAsync(id)` → `DeleteOrCloseAsync(userId, id)`. Keep the `jobForm`/`categoryText`/`skillText`/`TryParseTaxonomy` logic and all messages verbatim.

- [ ] **Step 4: Build**

Run: `dotnet build hotjob/hotjob.csproj --nologo -v q --no-incremental`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 5: Run Job tests**

Run: `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --nologo -v q --filter "FullyQualifiedName~Job"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add hotjob/Components/Pages/Employer/Jobs.razor
git commit -m "refactor: Employer Jobs page uses IJobService directly"
```

---

### Task 6: Convert Employer/Interviews to IApplicantService + IInterviewService + INotificationService

**Files:**
- Modify: `hotjob/Components/Pages/Employer/Interviews.razor:1-10,210-381`
- Test: existing controller/service tests for applicants/interviews/notifications

**Interfaces:**
- Consumes: `IApplicantService.ListAsync(userId, query, CancellationToken)` / `GetDetailAsync(userId, id, CancellationToken)` / `UpdateStatusAsync(userId, id, request, CancellationToken)` (`IApplicantService.cs:8-22`); `IInterviewService.CreateAsync(userId, appId, req, CancellationToken)` / `ListAsync(userId, appId, CancellationToken)` / `UpdateAsync(userId, id, req, CancellationToken)` / `CancelAsync(userId, id, CancellationToken)` (`IInterviewService.cs:8-28`); `INotificationService.NotifyAsync(userId, appId, req, CancellationToken)` (`INotificationService.cs:8-12`). NOTE the曾 mismatch: `EmployerApplicantsApiClient.NotifyAsync` routes to controller `Notify` which calls `notificationService.NotifyAsync` (not applicantService) — the page must inject `INotificationService` for Notify, not `IApplicantService`.
- Produces: Interviews page with identical UX (list/detail/status/interview/notify flows, all messages verbatim, overlap guard kept).

- [ ] **Step 1: Swap injections**

Replace `@using hotjob.Services.Api` + `@inject EmployerApplicantsApiClient ApplicantsApi` + `@inject EmployerInterviewsApiClient InterviewsApi` with `@using hotjob.Services.Employer`, `@using System.Security.Claims`, `@inject IApplicantService ApplicantService`, `@inject IInterviewService InterviewService`, `@inject INotificationService NotificationService`, `@inject AuthenticationStateProvider AuthenticationStateProvider`.

- [ ] **Step 2: Rewrite LoadAsync/SelectAsync/AdvanceStatusAsync**

Resolve userId per method. Map: `ApplicantsApi.ListAsync(query)` → `ApplicantService.ListAsync(userId, query)`; `GetDetailAsync(id)` → `GetDetailAsync(userId, id)`; `ListInterviewsAsync(id)` → `InterviewService.ListAsync(userId, id)`; `UpdateStatusAsync(id, statusForm)` → `UpdateStatusAsync(userId, id, statusForm)`. Keep guard, `query.Page/PageSize/Status` mutation order, and messages verbatim.

- [ ] **Step 3: Rewrite ScheduleAsync/CompleteAsync/CancelAsync/NotifyAsync**

Map: `CreateInterviewAsync(id, form)` → `InterviewService.CreateAsync(userId, id, form)`; `InterviewsApi.UpdateAsync(id, req)` → `InterviewService.UpdateAsync(userId, id, req)`; `CancelAsync(id)` → `InterviewService.CancelAsync(userId, id)`; `NotifyAsync(id, form)` → `NotificationService.NotifyAsync(userId, id, form)`. Keep `interviews.Add`/`FindIndex` update logic and all messages verbatim.

- [ ] **Step 4: Build**

Run: `dotnet build hotjob/hotjob.csproj --nologo -v q --no-incremental`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 5: Run full test suite**

Run: `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --nologo -v q`
Expected: PASS, 0 failed.

- [ ] **Step 6: Commit**

```bash
git add hotjob/Components/Pages/Employer/Interviews.razor
git commit -m "refactor: Employer Interviews page uses domain services directly"
```

---

### Task 7: Delete Services/Api + dead DTO + DI registrations + ApiClient tests

**Files:**
- Delete: `hotjob/Services/Api/ApiClientBase.cs`, `AccountApiClient.cs`, `CompanyApiClient.cs`, `EmployerJobsApiClient.cs`, `EmployerApplicantsApiClient.cs`, `EmployerInterviewsApiClient.cs`, `ApiResult.cs` (entire `Services/Api/` dir), `hotjob/DTOs/ServiceResult.cs` (dead, zero references).
- Delete: `hotjob/Tests/hotjob.Tests/ApiClientTests/ApiStubs.cs`, `ApiClientBaseTests.cs`, `EmployerApiClientTests.cs`, `AccountCompanyApiClientTests.cs` (entire `ApiClientTests/` dir — they test only the deleted clients).
- Modify: `hotjob/Program.cs:176-181` (remove `AddHttpClient(nameof(ApiClientBase))` + 5 `AddScoped(*ApiClient)` lines).
- Keep: `hotjob/Tests/hotjob.Tests/CommonTests/ApiControllerBaseTests.cs` (tests `Controllers/ApiControllerBase`, which stays).

**Interfaces:**
- Consumes: nothing (deletion task; all callers converted in Tasks 2-6).
- Produces: `grep -rn "Services.Api\|ApiClient\|ApiStubs\|DTOs.ServiceResult" hotjob --include="*.cs" --include="*.razor"` returns zero hits outside `Controllers/ApiControllerBase` + its test.

- [ ] **Step 1: Verify zero remaining references**

Run: `grep -rn "Services\.Api\|AccountApiClient\|EmployerJobsApiClient\|EmployerApplicantsApiClient\|EmployerInterviewsApiClient\|CompanyApiClient\|ApiStubs\|hotjob\.DTOs\.ServiceResult" hotjob --include="*.cs" --include="*.razor"`
Expected: zero hits (after Tasks 2-6).

- [ ] **Step 2: Delete the files and DI lines**

Delete the `Services/Api/` directory, `hotjob/DTOs/ServiceResult.cs`, and the `Tests/.../ApiClientTests/` directory. Remove the 6 DI lines in `Program.cs:176-181`. If Step 1 shows hits, stop and fix the caller first — do not delete.

- [ ] **Step 3: Full build + full test suite**

Run: `dotnet build hotjob/hotjob.csproj --nologo -v q --no-incremental`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.
Run: `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --nologo -v q`
Expected: PASS, 0 failed.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor: delete Services/Api loopback clients and dead DTO"
```

---

## Self-Review

1. **Spec coverage:** Register (Task 2), Profile (Task 3), Employer Company (Task 4), Employer Jobs (Task 5), Employer Interviews incl. Notify routing (Task 6), deletion + DI (Task 7), green-baseline fix (Task 1). Public `Companies.razor`/`Jobs.razor`/`JobDetail.razor` already direct — no task needed. Controllers untouched per constraint.
2. **Step scan:** Each code step names exact file, signature, and mapping; no step carries more than one action. Fixed: Task 6 calls out the Notify→INotificationService routing explicitly so the implementer cannot wire it to IApplicantService.
3. **Type consistency:** `ServiceResult<T>` (`Success`/`Data`/`Errors`) used uniformly; `ApiResult.Status` has no equivalent and no page logic branches on it (verified: pages only read `Success`/`Data`/`Errors`). `ICompanyService` collision (Company vs Employer namespaces) flagged in Task 4 with file-level using guidance.
4. **Review Focus:** Five failure modes listed with owning tasks (Register-anonymous→Task 2; wrong-role→Tasks 4-6 guards kept; overlap→Tasks 5-6; downcast→Task 5; 401-render→Tasks 3-6 error slots).
5. **Proportion:** Plan is interface mappings + verification commands, no method bodies transcribed. Length proportional to 5-page conversion + deletion.

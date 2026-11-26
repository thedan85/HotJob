# Public Job Company Visibility Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **User-approved overrides for this plan:** hy4 is the sole worker — no nested or additional agents are spawned (main session monitors and performs independent review after implementation); **no git commands at any phase** (including `status`/`diff`/`log`/`add`/`commit`) and no worktree tools/scripts; no secrets printed; no app startup; no access to or change of the user's real database. The user waived further approval prompts. There are deliberately **no Commit steps** — verification steps replace them.

**Goal:** Gate every public/candidate job listing and the apply action by `CompanyStatus.Approved` with one opt-in non-global predicate, while leaving employer, admin, application-history and notification-history flows unchanged.

**Architecture:** Add one static expression/extension (`PublicJobVisibility.WhereCompanyApproved`) and opt into it at eight candidate/public query call sites. No global query filter, no schema/row mutation, no new endpoint, no DTO or Razor change: visibility is recomputed from the live company row, so reapproval restores automatically. Publication/expiry checks and error strings stay exactly as they are.

**Tech Stack:** .NET 10, Blazor InteractiveServer, EF Core 9 + MySQL/Pomelo (production), xUnit 2.9.3 + SQLite in-memory (`TestFixture`), PowerShell commands from the repository root.

**Spec:** `docs/superpowers/specs/2026-11-26-public-job-company-visibility-design.md` (approved design; read both documents before starting).

## Global Constraints

- No git commands whatsoever; no worktree tools or scripts. Record this override in `docs/admin/task-8-progress.md`.
- No secrets printed, no application startup, no user-database access or change. Tests use only SQLite in-memory (`hotjob/Tests/hotjob.Tests/Support/TestFixture.cs:89-120`).
- Preserve all unrelated pre-existing edits; recently modified candidate/profile files are user work, not cleanup targets.
- Before any source edit, preserve exact pre-edit copies under `C:\Users\phulo\AppData\Local\Temp\opencode\task-8-baseline\<relative path>` and maintain `MANIFEST.md` there with created-vs-modified status and SHA-256 hashes. For created files, snapshot immediately after creation (pre-mutation copy).
- Never weaken, skip or delete an existing assertion. Narrow fixture changes that add `Status = CompanyStatus.Approved` to a happy-path seed are allowed only where the changed query requires them, and must be listed in `docs/admin/task-8-result.md`.
- Mutation verification evidence is reported separately from real full-suite counts; the mutation must never be left in place.
- Final full-suite command, from the repository root: `dotnet test --configuration Release` (background if needed; wait for automatic completion, never poll).
- No global query filter, no migration, no seeder/schema edit, no change to employer/admin/auth/billing/snapshot/expiry/company-public flows.

## Review Focus

1. **A hidden-company job reached through the API delegate** (recommendation path) must disappear although `JobSearchController` itself is unchanged — pinned by `JobSkillMatchTests.GetJobsMatchingResumeAsync_PendingCompanyExcluded` (existing) plus new Pending/Rejected cases in Task 2, and the existing delegation assertions in `JobSearchControllerTests.MatchResume_Success_ReturnsOkEnvelope:15-38` remain green.
2. **Filtered feeds must not lie about totals, unread counts or pages** — pinned by Task 2 saved-list `TotalCount` assertions and Task 4 feed assertions covering approved/hidden/null-legacy rows across two pages.
3. **Apply must create nothing for a hidden company** — pinned by Task 3 assertions of zero `Applications`, `ApplicationStatusHistories` and `Notifications` rows with `NotFound`, while existing Draft/Closed/Expired `Conflict` tests stay untouched.
4. **An accidental global filter must not hide employer/admin/application-history data** — pinned by Task 5 direct repository/admin queries over a Rejected/Pending company and a later-rejected company's application list/withdraw test.
5. **Reapproval must restore visibility without row mutation** — pinned by Task 5 flip-status test that reuses the same jobs/bookmarks after setting `CompanyStatus.Approved`.

---

## File Structure

- `hotjob/Services/Common/PublicJobVisibility.cs` — **create**; the only shared predicate and extension.
- `hotjob/Repositories/Job/JobPostingRepository.cs` — **modify**; public detail/browse queries opt in.
- `hotjob/Services/Job/JobService.cs` — **modify**; saved list, save action and recommendation opt in; owned/admin methods untouched.
- `hotjob/Services/Candidate/ApplicationService.cs` — **modify**; apply lookup only; `GetApplyContextAsync`/`WithdrawAsync` untouched.
- `hotjob/Services/JobAlerts/JobAlertService.cs` — **modify**; generation lookup and feed query opt in.
- Tests — **modify** `JobTests/PublicJobDetailTests.cs`, `JobTests/PublicJobBrowseFilterTests.cs`, `JobTests/SavedJobTests.cs`, `JobTests/JobSkillMatchTests.cs`, `CandidateTests/ApplicationServiceTests.cs`, `JobAlertTests/JobAlertServiceTests.cs`; **create** `JobTests/CompanyVisibilityRegressionTests.cs`.
- Docs — already created: spec and this plan; ledger `docs/admin/task-8-progress.md`; final report `docs/admin/task-8-result.md` (created at the very end).

---

### Task 0: Exact baseline snapshot and manifest

**Files:**
- Baseline root: `C:\Users\phulo\AppData\Local\Temp\opencode\task-8-baseline\`
- Manifest: `C:\Users\phulo\AppData\Local\Temp\opencode\task-8-baseline\MANIFEST.md`

**Interfaces:**
- Consumes: nothing.
- Produces: byte-identical pre-edit copies of the 10 existing files listed below, plus a manifest that distinguishes created from modified files.

- [ ] **Step 1: Create the baseline directory tree and copy every existing file that will be modified**

Mirror the repository-relative path under the baseline root. The 10 files to copy before any edit:

```text
hotjob/Repositories/Job/JobPostingRepository.cs
hotjob/Services/Job/JobService.cs
hotjob/Services/Candidate/ApplicationService.cs
hotjob/Services/JobAlerts/JobAlertService.cs
hotjob/Tests/hotjob.Tests/JobTests/PublicJobDetailTests.cs
hotjob/Tests/hotjob.Tests/JobTests/PublicJobBrowseFilterTests.cs
hotjob/Tests/hotjob.Tests/JobTests/SavedJobTests.cs
hotjob/Tests/hotjob.Tests/JobTests/JobSkillMatchTests.cs
hotjob/Tests/hotjob.Tests/CandidateTests/ApplicationServiceTests.cs
hotjob/Tests/hotjob.Tests/JobAlertTests/JobAlertServiceTests.cs
```

Use `New-Item -ItemType Directory -Force` for each parent and `Copy-Item -LiteralPath <source> -Destination <baseline mirror>`; never edit a source file in this task.

- [ ] **Step 2: Write `MANIFEST.md`**

Table columns: `Relative path | Status | Source SHA-256 | Baseline SHA-256 | Purpose`. Status is `modified` for the 10 rows above; add rows with status `created` for `hotjob/Services/Common/PublicJobVisibility.cs`, `hotjob/Tests/hotjob.Tests/JobTests/CompanyVisibilityRegressionTests.cs` and the four planning/report docs. For created files, record the post-creation pre-mutation hash when the file first exists (Tasks 1 and 5 update those rows).

- [ ] **Step 3: Verify byte identity**

Run a PowerShell `Get-FileHash -Algorithm SHA256` comparison for all 10 mirrored pairs. Expected: every pair equal; any mismatch stops the work.

---

### Task 1: Shared predicate and public search/detail gating

**Files:**
- Create: `hotjob/Services/Common/PublicJobVisibility.cs`
- Modify: `hotjob/Repositories/Job/JobPostingRepository.cs:25-35,74-157`
- Test: `hotjob/Tests/hotjob.Tests/JobTests/PublicJobDetailTests.cs`, `hotjob/Tests/hotjob.Tests/JobTests/PublicJobBrowseFilterTests.cs`

**Interfaces:**
- Consumes: `CompanyStatus.Approved` (`hotjob/Models/Users/CompanyProfile.cs:6-11`), `JobPosting.Company` (`hotjob/Models/Job/JobPosting.cs:60-61`).
- Produces: `PublicJobVisibility.CompanyApproved` (`Expression<Func<JobPosting, bool>>`) and `IQueryable<JobPosting> WhereCompanyApproved(this IQueryable<JobPosting> query)`, consumed by Tasks 2–4.

- [ ] **Step 1: Write failing detail behavior tests (no production helper exists yet)**

In `PublicJobDetailTests.cs`, add `CompanyStatus companyStatus = CompanyStatus.Approved` to the `SeedJobAsync` helper signature (`:15-47`) and set `Status = companyStatus` (`:22`). Add two `[Fact]`s:
`GetPublishedAsync_ReturnsNotFound_ForPendingCompany` and `..._ForRejectedCompany`: seed `JobStatus.Published` with a future expiry and the given company status; assert `!result.Success`, `ServiceErrorType.NotFound`. Keep all four existing tests and their assertions byte-identical; only the intentional helper default is adjusted.

- [ ] **Step 2: Write failing browse behavior tests**

In `PublicJobBrowseFilterTests.cs`, add the same optional company-status parameter to `SeedJobAsync` (`:32-61`). Add `Browse_ExcludesJobsFromNonApprovedCompanies` (Theory with `CompanyStatus.Pending`, `CompanyStatus.Rejected`): seed one Approved "Open role" plus one hidden "Hidden role"; call `BrowsePublishedAsync(null,null,null,null,null,null,null,null,1,20)`; assert only "Open role" is returned and `TotalCount == 1`. Add `Browse_CompanyIdFilter_ExcludesNonApprovedCompany`: seed a hidden company job and call with `companyId` set to that company; assert empty items and `TotalCount == 0`. Keep all existing tests and assertions byte-identical; only the intentional helper default is adjusted.

- [ ] **Step 3: Run the targeted tests RED before any production edit**

Run from the repository root:
`dotnet test --configuration Release --filter "FullyQualifiedName~PublicJobDetailTests|FullyQualifiedName~PublicJobBrowseFilterTests"`
Expected: FAIL only on the newly added hidden-company cases (hidden-company job still returned); all pre-existing cases pass. These tests compile and run against the existing public APIs — no helper is needed to write or compile them.

- [ ] **Step 4: Create `PublicJobVisibility.cs` with the exact signature and body**

`public static class PublicJobVisibility` in namespace `hotjob.Services.Common`, exposing `public static readonly Expression<Func<JobPosting, bool>> CompanyApproved = job => job.Company.Status == CompanyStatus.Approved;` and `public static IQueryable<JobPosting> WhereCompanyApproved(this IQueryable<JobPosting> query) => query.Where(CompanyApproved);`. Do not call the extension inside another lambda expression. Update the created-file baseline hash in `MANIFEST.md`.

- [ ] **Step 5: Opt the repository queries into the predicate**

In `GetPublishedByIdAsync` (`:30-34`) and the base query of `BrowsePublishedAsync` (`:91-93`), append `.WhereCompanyApproved()` before `FirstOrDefaultAsync`/filters; add `using hotjob.Services.Common;`. Do not change `GetByIdOwnedAsync` (`:9-23`) or `ListOwnedAsync` (`:37-72`) — they are employer-owned.

- [ ] **Step 6: Run the targeted tests GREEN**

Run the Step 3 command. Expected: PASS, including all pre-existing cases.

- [ ] **Step 7: Verify no global filter was introduced**

Search for `HasQueryFilter` in `hotjob/`: expected zero hits. No commit step (git is forbidden).

---

### Task 2: Saved list, save action and recommendation gating

**Files:**
- Modify: `hotjob/Services/Job/JobService.cs:415-462,483-549,906-911`
- Test: `hotjob/Tests/hotjob.Tests/JobTests/SavedJobTests.cs`, `hotjob/Tests/hotjob.Tests/JobTests/JobSkillMatchTests.cs`

**Interfaces:**
- Consumes: `PublicJobVisibility.WhereCompanyApproved` from Task 1.
- Produces: unchanged public method signatures in `IJobService` (`:81-95`, `:67-75`); `Controllers/JobSearchController.cs:24-56` continues to delegate to `GetJobsMatchingResumeAsync` with no change.

- [ ] **Step 1: Write failing saved-list tests**

In `SavedJobTests.cs`, add `CompanyStatus companyStatus = CompanyStatus.Approved` to `SeedJobAsync` (`:27-48`) and set it on the company (`:32`). Add `GetSavedJobs_ExcludesJobsFromNonApprovedCompanies` (Theory Pending/Rejected): seed a saved Approved job and a saved hidden job for the same candidate; assert one item (the Approved one), `TotalCount == 1`. Add `SaveJobAsync_ForNonApprovedCompany_ReturnsNotFound` (Theory Pending/Rejected): assert `NotFound` and `CountAsync<SavedJob>() == 0`. Keep existing tests byte-identical.

- [ ] **Step 2: Write failing recommendation tests**

In `JobSkillMatchTests.cs` (helper already has `companyStatus` at `:46`), change the existing `GetJobsMatchingResumeAsync_PendingCompanyExcluded:199-211` into a `[Theory]` with `InlineData(CompanyStatus.Pending)` and `InlineData(CompanyStatus.Rejected)`, keeping its name and assertions; add a case with an Approved job to prove the filter is not over-broad (already covered by `GetJobsMatchingResumeAsync_ReturnsMatchingJobsWithSkills:94-111`). No fixture change elsewhere.

- [ ] **Step 3: Run targeted tests RED**

Run: `dotnet test --configuration Release --filter "FullyQualifiedName~SavedJobTests|FullyQualifiedName~JobSkillMatchTests|FullyQualifiedName~JobSearchControllerTests"`
Expected: FAIL **only on the new saved-list cases**. The save-action and recommendation status cases must already PASS because both code paths already check `CompanyStatus.Approved` (`JobService.cs:501,910`) — they are standardization regression guards, not new behavior. `JobSearchControllerTests` (stubbed delegation) remain PASS. Do not break existing code to manufacture a RED.

- [ ] **Step 4: Implement the three service changes**

In `GetSavedJobsAsync`, define `IQueryable<int> visibleJobIds = dbContext.JobPostings.AsNoTracking().WhereCompanyApproved().Select(job => job.Id);` and add `visibleJobIds.Contains(saved.JobPostingId)` to `baseQuery` (`:431-433`) before both `CountAsync` and the projected page query, so totals and pages agree. In `SaveJobAsync`, replace the inline `job.Company.Status == CompanyStatus.Approved` at `:501` with `.WhereCompanyApproved()` on the existing chained query. In `GetJobsMatchingResumeAsync`, replace the inline company clause at `:910` with `.WhereCompanyApproved()` on the existing `query` chain. Preserve Published/expiry clauses and all error strings.

- [ ] **Step 5: Run targeted tests GREEN**

Run the Step 3 command. Expected: PASS including stubbed API delegation tests.

---

### Task 3: Apply gate with zero side effects

**Files:**
- Modify: `hotjob/Services/Candidate/ApplicationService.cs:76-101`
- Test: `hotjob/Tests/hotjob.Tests/CandidateTests/ApplicationServiceTests.cs`

**Interfaces:**
- Consumes: `PublicJobVisibility.WhereCompanyApproved` from Task 1.
- Produces: unchanged `IApplicationService.ApplyAsync(string?, int, int, string?, CancellationToken)`; error type `NotFound` with existing text “Job not found.”

- [ ] **Step 1: Write failing apply tests**

In `ApplicationServiceTests.cs`, add `CompanyStatus companyStatus = CompanyStatus.Approved` to `SeedJobAsync` (`:35-102`) and set it on both company seeds (`:45`, `:77`) so all existing happy paths stay Approved. Add `ApplyAsync_NonApprovedCompany_ReturnsNotFoundWithoutSideEffects` (Theory Pending/Rejected): seed the candidate, resume and hidden job; call `ApplyAsync`; assert `!Success`, `ServiceErrorType.NotFound`, `Contains("Job not found.")`, and zero rows in `Applications`, `ApplicationStatusHistories`, `Notifications`. Keep all existing assertions and the four unpublished-job `Conflict` cases byte-identical.

- [ ] **Step 2: Run targeted tests RED**

Run: `dotnet test --configuration Release --filter "FullyQualifiedName~ApplicationServiceTests"`
Expected: FAIL only on the new hidden-company cases.

- [ ] **Step 3: Add the predicate to the apply job lookup**

Append `.WhereCompanyApproved()` to the chained `dbContext.JobPostings.AsNoTracking().Where(row => row.Id == jobId)` query at `:76-87`; the existing `if (job is null) return NotFound` at `:88-89` then handles hidden companies. **Preserve the existing ordering**: cover-letter normalization/length validation at `:69-74` runs *before* the job lookup, so the new gate sits after that validation and before resume loading, snapshot build, duplicate check, transaction and notifications. Do not touch `GetApplyContextAsync`, `WithdrawAsync`, or the status/expiry `Conflict` branches.

- [ ] **Step 4: Run targeted tests GREEN**

Run the Step 2 command. Expected: PASS including all snapshot/duplicate/withdraw regressions.

---

### Task 4: Alert generation and alert feed filtering

**Files:**
- Modify: `hotjob/Services/JobAlerts/JobAlertService.cs:33-55,164-187`
- Test: `hotjob/Tests/hotjob.Tests/JobAlertTests/JobAlertServiceTests.cs`

**Interfaces:**
- Consumes: `PublicJobVisibility.WhereCompanyApproved` from Task 1.
- Produces: unchanged `IJobAlertService` signatures; feed filtering keeps `JobAlertDto.RelatedJobPostingId` null for legacy rows.

- [ ] **Step 1: Write failing generation tests**

In `JobAlertServiceTests.cs`, add `NonApprovedCompanyJob_DoesNotNotify` (Theory Pending/Rejected): seed a candidate with a matching skill via `MatchTestData.CreateCandidateAsync`, create a company with the given status (seed with `MatchTestData.CreateCompanyAsync`, then set `company.Status` and save), create a Published matching job via `MatchTestData.CreateJobAsync`; assert `NotifyForPublishedJobAsync(job.Id) == 0` and zero `Notification` rows. Keep existing tests byte-identical.

- [ ] **Step 2: Write failing feed/pagination tests**

Add `GetAlerts_ExcludesNonApprovedCompanyJobs_KeepsLegacyNullJobAlerts`: seed the caller candidate; insert four `Notification` rows for that user with `Kind = NotificationKinds.JobAlert` — one referencing an Approved job, one referencing a Pending-company job, one referencing a Rejected-company job, one with `RelatedJobPostingId == null` ("Legacy alert"); assert `TotalCount == 2`, `UnreadCount == 2`, items contain the Approved and Legacy alerts only, and a page-2 request with `pageSize: 1` returns the remaining item without overlap.

- [ ] **Step 3: Run targeted tests RED**

Run: `dotnet test --configuration Release --filter "FullyQualifiedName~JobAlertServiceTests"`
Expected: FAIL only on the new cases.

- [ ] **Step 4: Implement generation and feed changes**

In `NotifyForPublishedJobAsync`, append `.WhereCompanyApproved()` to the job lookup at `:33-44`; the existing `:52-55` Published/expiry gate and the “job no longer exists” warning remain. In `GetAlertsAsync`, compute `IQueryable<int> visibleJobIds = dbContext.JobPostings.AsNoTracking().WhereCompanyApproved().Select(job => job.Id);` before the notification query and add `notification.RelatedJobPostingId == null || (notification.RelatedJobPostingId.HasValue && visibleJobIds.Contains(notification.RelatedJobPostingId.Value))` to the `Where` at `:164-167`. The `total` (`:169`), `unread` (`:170`) and page slice (`:172-187`) all use that same filtered query. Do not change `MarkAllReadAsync` or `JobAlerts.razor` (legacy null-id alerts already have no link).

- [ ] **Step 5: Run targeted tests GREEN**

Run the Step 3 command. Expected: PASS including all existing alert tests.

---

### Task 5: Regression guards for untouched flows and reapproval

**Files:**
- Create: `hotjob/Tests/hotjob.Tests/JobTests/CompanyVisibilityRegressionTests.cs`

**Interfaces:**
- Consumes: `JobPostingRepository`, `JobService`, `AdminService`, `CandidateService`, `ApplicationService`, `PublicJobVisibility` (all from earlier tasks or existing code).
- Produces: no production change; test-only guard suite.

- [ ] **Step 1: Write regression tests**

Create `hotjob/Tests/hotjob.Tests/JobTests/CompanyVisibilityRegressionTests.cs` using `TestFixture`. Keep each flow in its own test; use `[Theory] [InlineData(CompanyStatus.Pending)] [InlineData(CompanyStatus.Rejected)]` where the same assertion applies to both hidden states.

1. `OwnedQueries_StillReturnDraftAndPublishedJobs_ForNonApprovedCompanies` (Theory Pending/Rejected) — seed one Draft and one Published job for the company; call `new JobPostingRepository(fx.Db).ListOwnedAsync(companyId, null, null, 1, 20)` and `GetByIdOwnedAsync` for each id (with `includeDetails: false` and `true`); assert both jobs are returned by both methods (employer-owned data is not globally filtered).
2. `AdminJobList_StillReturnsJobsOfNonApprovedCompany` (Theory Pending/Rejected) — seed a Published job; call `new AdminService(fx.Db, fx.UserManager, fx.RoleManager).GetJobsAsync(1, 20)` (constructor `hotjob/Services/Admin/AdminService.cs:15-18`; fixture members `Tests/hotjob.Tests/Support/TestFixture.cs:26-27`); assert the job appears.
3. `EmployerGuard_StillRevokesNonApprovedCompany` (Theory Pending/Rejected) — seed an **Active** employer of the hidden company and one application; call `new ApplicantService(new ApplicantRepository(fx.Db), NullLogger<ApplicantService>.Instance).GetDetailAsync(userId, applicationId)`; assert `Unauthorized` (mirrors `EmployerTests/EmployerGuardTests.cs:79-92`). Report separately that the employer UI itself already rejects a revoked company through this guard; no UI test change is needed here.
4. `ExistingApplication_FromLaterRejectedCompany_RemainsVisibleAndWithdrawable` — seed an Approved company, job, candidate, resume and application with all snapshot fields populated (`SnapshotJson`, `SnapshotSummary`, `SnapshotFileUrl`); set `company.Status = Rejected` and save; assert **both** `CandidateService.GetMyApplicationsAsync(userId)` and `GetMyApplicationsPageAsync(userId, new MyApplicationsQuery())` return the job title, company name and current status; snapshot the three snapshot fields; call `ApplicationService.WithdrawAsync`; assert status `Withdrawn`, exactly one new history row, and all three snapshot fields unchanged. The unchanged `Components/Pages/MyApplications.razor:158-159` renders these as plain titles and has no `/jobs/` link (verified by source inspection plus existing `CandidateTests/MyApplicationsPageTests.cs` render tests) — do not modify that page.
5. Reapproval flow tests (each `[Theory]` Pending/Rejected → Approved, separate jobs/candidates where needed to avoid duplicate applications/bookmarks):
   - `Reapproval_RestoresDetailBrowseAndSavedJobs` — direct-insert the bookmark (`fx.Db.SavedJobs.Add(...)`; never call `SaveJobAsync` while hidden); assert detail `NotFound`, browse empty, saved list empty; flip status; assert all three return the job and the job/bookmark row counts and ids are unchanged.
   - `Reapproval_RestoresRecommendations` — seed a candidate/resume with a matching skill (`MatchTestData` helpers) and a Published job with that `JobSkill`; assert `JobService.GetJobsMatchingResumeAsync` is empty; flip status; assert the job appears; job/resume/skill row counts unchanged.
   - `Reapproval_AllowsAlertGeneration` — use a stub `IMatchScoreService` whose `GetAsync` returns an available 100% `MatchScoreDto`, with `JobAlertService` options `MinMatchPercent = 60`; assert 0 notifications while hidden, then a new alert row with the same `RelatedJobPostingId` after approval; existing rows untouched.
   - `Reapproval_AllowsSaveAndApply` — assert `SaveJobAsync` and `ApplyAsync` both return `NotFound` with zero bookmark/application/history/notification rows while hidden; after approval assert both succeed and create exactly one bookmark and one application with its history row; the original job row id and all pre-existing rows are unchanged.

- [ ] **Step 2: Run the new suite GREEN**

Run: `dotnet test --configuration Release --filter "FullyQualifiedName~CompanyVisibilityRegressionTests"`
Expected: PASS. If a test fails, fix only the test setup; do not change production visibility rules to accommodate a bad fixture.

- [ ] **Step 3: Run all JobTests plus candidate/alert suites together**

Run: `dotnet test --configuration Release --filter "FullyQualifiedName~JobTests|FullyQualifiedName~ApplicationServiceTests|FullyQualifiedName~JobAlertServiceTests|FullyQualifiedName~CandidateServiceTests"`
Expected: PASS, proving unrelated candidate-history tests were not broken by fixture changes.

---

### Task 6: Mutation verification, full suite and final report

**Files:**
- Read/verify: `hotjob/Services/Common/PublicJobVisibility.cs`
- Write: `docs/admin/task-8-result.md`

**Interfaces:**
- Consumes: exact pre-mutation copy of `PublicJobVisibility.cs` from `C:\Users\phulo\AppData\Local\Temp\opencode\task-8-baseline\created\hotjob\Services\Common\PublicJobVisibility.cs`.
- Produces: separate mutation evidence and real full-suite counts in the final report.

- [ ] **Step 1: Confirm no nested/additional agent is editing source (hy4 is the sole worker), then invert the central predicate**

Change only `CompanyApproved` so it compares `!= CompanyStatus.Approved`. Leave every other line and every test untouched.

- [ ] **Step 2: Prove the regression suite catches the mutation**

Run: `dotnet test --configuration Release --filter "FullyQualifiedName~JobTests|FullyQualifiedName~ApplicationServiceTests|FullyQualifiedName~JobAlertServiceTests"`
Expected: FAIL on Approved/Pending/Rejected visibility cases. Capture the command, failing test names and counts into a temporary mutation-evidence note (not into full-suite counts).

- [ ] **Step 3: Restore from the exact copy and verify hash**

Copy the baseline `created\hotjob\Services\Common\PublicJobVisibility.cs` back over the source file; compare `Get-FileHash -Algorithm SHA256` of source and baseline copy. Expected: identical; no mutation remains.

- [ ] **Step 4: Rerun targeted tests normally**

Run the Step 2 command. Expected: PASS with the mutation reverted.

- [ ] **Step 5: Run the full suite from the repository root**

Run: `dotnet test --configuration Release` (use the background option if it exceeds the foreground timeout; wait for automatic completion and never poll). Record the exact pass/fail/skipped counts from this run only.

- [ ] **Step 6: Write `docs/admin/task-8-result.md`**

Include: complete query inventory with paths/lines; exact file list (created vs modified); narrow fixture changes with justification; full-suite counts and command; mutation evidence in a clearly separate section; manual browser steps (guest and Candidate on `/it-jobs`, `/jobs/{id}` of Approved/Pending/Rejected companies, `/saved-jobs`, `/recommended-jobs`, `/job-alerts`, `/my-applications` withdraw, admin reapproval then refresh); limitations (no real MySQL verification, no app startup by the agent, no git-based diff).

- [ ] **Step 7: Update the progress ledger**

Mark only steps actually completed in `docs/admin/task-8-progress.md`; leave unfinished checkboxes untouched. No commit step (git is forbidden).

# Task 8 result — public job company visibility

## 1. Summary

Public/candidate job listings and apply are now gated by `CompanyStatus.Approved` through **one opt-in, non-global predicate**. Employer-owned, admin and application/notification-history flows are unchanged. Reapproval restores visibility automatically because the predicate is evaluated at query time; no job/bookmark/application/notification row is mutated to hide or restore anything.

- Baseline full suite (before any code change): **624 passed, 0 failed, 0 skipped**, 55 s.
- hy4's post-wave full suite (`dotnet test --configuration Release`, repository root): **657 passed, 0 failed, 0 skipped**, **52 s** (+33 tests) — worker evidence, kept separate from main's run below.
- Main's independent final run on FINAL code (`dotnet test --configuration Release`, repository root): exit 0, **657 passed, 0 failed, 0 skipped**, **56 s**. Main reported **FINAL REVIEW COMPLETE — approved** (correctness, spec coverage, quality); no production or test changes required.
- No git commands, no worktree tools, no secrets printed, no app startup, no real database writes. hy4 was the sole worker; main performed the independent review and final verification. Recorded in the ledger.

### Root cause (verified in the read-only audit)

Public/candidate job queries were gated incompletely or not at all by company approval:

- `Repositories/Job/JobPostingRepository.cs:GetPublishedByIdAsync` (detail) and `BrowsePublishedAsync` (search, including the `companyId` filter used by the company page) filtered only `JobStatus.Published` + unexpired — **no company-status check**.
- `Services/Job/JobService.cs:GetSavedJobsAsync` had no company/visibility filter, and `Services/JobAlerts/JobAlertService.cs:NotifyForPublishedJobAsync/GetAlertsAsync` had none either.
- `Services/Candidate/ApplicationService.cs:ApplyAsync` checked only Published + expiry — **no company-status check**.
- Admin rejection (`Services/Admin/AdminService.cs:UpdateCompanyStatusAsync`) writes **only `CompanyProfile.Status`** (plus reason/verifier); published job rows stay unchanged, so those ungated entrypoints continued to expose and accept applications for a rejected company.
- The existing inline `CompanyStatus.Approved` checks in `SaveJobAsync` (`JobService.cs:501`) and `GetJobsMatchingResumeAsync` (`:910`) protected **only those two entrypoints**; they did not protect search/detail/saved/alerts/apply, and duplicating them further would drift.

The central opt-in predicate closes that drift: every candidate/public entrypoint now asks the same live `job.Company.Status == CompanyStatus.Approved` question, with no global filter and no row mutation.

### Design decisions (explicit)

- Hidden saved rows are **excluded from the saved list, not deleted**, so reapproval restores them automatically with the same bookmark row.
- Hidden-job alerts are **excluded from the feed, not deleted**; reapproval restores the same notification id. Legacy alerts with `RelatedJobPostingId == null` remain visible and unclickable (unchanged `JobAlerts.razor:123-126`).
- The helper covers **company approval only**. Publication/expiry semantics stay with each caller, so Draft/Closed/expired jobs keep their existing status labels in saved jobs and their existing `Conflict` behavior on apply, and the alert feed's job-link rule is unchanged.

## 2. Exact files

### Created

| Path | Purpose |
|---|---|
| `hotjob/Services/Common/PublicJobVisibility.cs` | The single `CompanyApproved` expression + `WhereCompanyApproved()` extension (company approval only; publication/expiry stay with callers). |
| `hotjob/Tests/hotjob.Tests/JobTests/CompanyVisibilityRegressionTests.cs` | 17 guard tests: owned/admin/guard flows, application history, per-flow reapproval including the existing **alert feed** (added in the main-requested wave). |
| `docs/superpowers/specs/2026-11-26-public-job-company-visibility-design.md` | Approved design (created phase 2, preflight corrections applied). |
| `docs/superpowers/plans/2026-11-26-public-job-company-visibility.md` | Implementation plan (created phase 2, preflight corrections applied). |
| `docs/admin/task-8-progress.md` | Progress ledger with preflight table. |
| `docs/admin/task-8-result.md` | This report. |

### Modified — production

`hotjob/Services/Common/PublicJobVisibility.cs` is **new** (listed above), not a modified file.

| Path | Methods / lines after edit | Change |
|---|---|---|
| `hotjob/Repositories/Job/JobPostingRepository.cs` | `GetPublishedByIdAsync:35`, `BrowsePublishedAsync:96` | `.WhereCompanyApproved()` appended to the existing Published + unexpired base queries. |
| `hotjob/Services/Job/JobService.cs` | `GetSavedJobsAsync:431-438`, `SaveJobAsync:502`, `GetJobsMatchingResumeAsync:916` | Saved list filtered through `visibleJobIds`; two pre-existing inline Approved checks standardized onto the shared predicate. |
| `hotjob/Services/Candidate/ApplicationService.cs` | `ApplyAsync:79` | Predicate added to the job lookup after existing cover-letter validation and before snapshot/history/application/notification. |
| `hotjob/Services/JobAlerts/JobAlertService.cs` | `NotifyForPublishedJobAsync:36`, `GetAlertsAsync:165-176` | Generation gated; feed filters referenced non-approved jobs, keeps null-job legacy alerts; totals/unread/pages use one filtered query. |

### Modified — tests

- `hotjob/Tests/hotjob.Tests/JobTests/PublicJobDetailTests.cs`
- `hotjob/Tests/hotjob.Tests/JobTests/PublicJobBrowseFilterTests.cs`
- `hotjob/Tests/hotjob.Tests/JobTests/SavedJobTests.cs`
- `hotjob/Tests/hotjob.Tests/JobTests/JobSkillMatchTests.cs`
- `hotjob/Tests/hotjob.Tests/CandidateTests/ApplicationServiceTests.cs`
- `hotjob/Tests/hotjob.Tests/JobAlertTests/JobAlertServiceTests.cs`

No fixture/support file was changed (`Support/TestFixture.cs`, `Support/MatchTestData.cs`, `Support/StubJobAlertService.cs` are untouched).

### Baseline evidence

Exact pre-edit copies of all 10 modified files are under `C:\Users\phulo\AppData\Local\Temp\opencode\task-8-baseline\` with a created-vs-modified manifest (`MANIFEST.md`) and SHA-256 hashes; created files were snapshotted immediately after creation (the helper snapshot is the mutation-restore source). All 10 source/baseline hash pairs matched at snapshot time.

## 3. Complete query inventory (post-change)

| Flow | File · method | Gating |
|---|---|---|
| Public detail | `Repositories/Job/JobPostingRepository.cs:GetPublishedByIdAsync:25-36` ← `Services/Job/JobService.cs:GetPublishedAsync:396-413` ← `Components/Pages/JobDetail.razor:654` | Published + unexpired + **predicate**; hidden → existing `NotFound` |
| Public browse / `companyId` filter | `JobPostingRepository.cs:BrowsePublishedAsync:74-158` ← `JobService.cs:BrowsePublishedAsync` ← `Components/Pages/Jobs.razor:286`, `CompanyDetail.razor:197` | Published + unexpired + **predicate**; totals consistent |
| Recommended / API delegate | `JobService.cs:GetJobsMatchingResumeAsync:847-1026` ← `RecommendedJobs.razor:263`, `Controllers/JobSearchController.cs:24-56` | Published + unexpired + **predicate** (previously inline check); controller unchanged |
| Save / saved list | `JobService.cs:SaveJobAsync:483-554`, `GetSavedJobsAsync:415-467`, `IsSavedAsync`, `UnsaveJobAsync` ← `JobDetail.razor:542,562-564`, `SavedJobs.razor:179,221` | Save **predicate** → existing `NotFound`; saved list filters referenced jobs; unsave/is-saved unchanged |
| Apply | `Services/Candidate/ApplicationService.cs:ApplyAsync:58-233` ← `JobDetail.razor:618` | **predicate** in lookup; hidden → `NotFound` “Job not found.” with zero side effects; original status/expiry `Conflict` behavior preserved |
| Alert generation | `Services/JobAlerts/JobAlertService.cs:NotifyForPublishedJobAsync:28-139` ← `JobService.cs:604-617` | **predicate**; hidden → 0 alerts |
| Alert feed | `JobAlertService.cs:GetAlertsAsync:140-198` ← `JobAlerts.razor:209` | **predicate** on referenced job id; `RelatedJobPostingId == null` legacy alerts retained without job link (unchanged markup at `JobAlerts.razor:123-126`) |

### Deliberately untouched (file · method · reason)

| File · method | Reason |
|---|---|
| `hotjob/Services/Job/JobService.cs:CreateAsync/UpdateAsync/DeleteOrCloseAsync/AssignCategoriesAsync/AssignSkillsAsync/ListAsync/GetAsync/GetDetailAsync/GetAuthorizedEmployerAsync` | Employer-owned; authorization already requires Active membership + Approved company (`:619-626`). Applying the public predicate would hide the employer's own Draft/Closed/Rejected-company data. |
| `hotjob/Repositories/Job/JobPostingRepository.cs:GetByIdOwnedAsync:9-23`, `ListOwnedAsync:37-72` | Owned by `CompanyProfileId`; regression test proves Draft + Published rows of hidden companies still return. |
| `hotjob/Services/Employer/EmployerGuard.cs:RequireActiveAsync:9-27`, `hotjob/Services/Billing/EntitlementService.cs:GetForUserAsync:16-32`, `BillingService.cs:300` | Existing Approved-company gate for employer actions; changing it would alter authorization logic outside scope. |
| `hotjob/Controllers/EmployerJobsController.cs:Create/List/Get/Update/DeleteOrClose/AssignCategories/AssignSkills`, `hotjob/Components/Pages/Employer/Jobs.razor:455,537,587,604,642`, `Employer/Interviews.razor:579` | Employer UI/API surface; already gated by role + guard. |
| `hotjob/Services/Employer/ApplicantService.cs`, `ApplicantRepository.cs:32-164`, `InterviewService.cs`, `NotificationService.cs`, `hotjob/Controllers/EmployerApplicantsController.cs`, `EmployerInterviewsController.cs` | Employer ATS over applications; access scoped by `Application.JobPosting.CompanyProfileId` and `EmployerGuard`. Existing applications must remain visible/actionable to the employer. |
| `hotjob/Services/Admin/AdminService.cs:GetJobsAsync:220-254`, `DeleteJobAsync`, `hotjob/Components/Pages/Admin/AdminJobs.razor:110,137` | Admin must see all postings regardless of company or job status. |
| `hotjob/Services/Candidate/CandidateService.cs:GetMyApplicationsAsync:19-38`, `GetMyApplicationsPageAsync:40-96`, `OwnedApplications:201-204`; `Services/Candidate/ApplicationService.cs:WithdrawAsync:230-269`; `hotjob/Components/Pages/MyApplications.razor:158-159` | Application history is a historical record: plain job/company titles, no `/jobs/` link, withdraw still allowed (verified for a later-rejected company with unchanged snapshots). |
| `hotjob/Services/Company/CompanyService.cs:ListAsync/GetDetailAsync/GetReviewsAsync/CreateReviewAsync`, `Repositories/Company/CompanyRepository.cs:ListApprovedAsync/GetApprovedByIdAsync/ListFilterOptionsAsync`, `Controllers/CompaniesController.cs`, `Components/Pages/Companies.razor`, `CompanyDetail.razor` | Public company pages already gate `CompanyStatus.Approved` (`:27,54,60,73` in repo; `:155-163` in service); company "Show jobs" link uses the now-gated companyId browse. |
| `hotjob/Services/ML/MatchScoreService.cs:GetAsync`, `GetStoredForJobsAsync`, `GetAdvancedAsync`, `ScoreAsync`, `TrainAsync`, `LoadTrainingRowsAsync`; `hotjob/Services/ML/SyntheticMatchTrainingService.cs:SeedAndTrainAsync` | These return match scores or ML training data by job/resume id; they do **not** return public job cards or create a candidate visibility path. Match-score badges are used on search (`Components/Pages/Jobs.razor:154-157,317-341`) as well as job detail, but the surrounding job lists are already gated by the repository/service predicates, so no hidden job is discoverable through scores. |
| `hotjob/Services/Job/JobExpiryService.cs:ExpireAsync:55-65` | Background status transition (Published → Expired), not a candidate listing. |
| `hotjob/Services/Candidate/CandidateService.cs:GetMyNotificationsAsync:98-139`, `GetUnreadNotificationCountAsync`, `MarkNotificationReadAsync`, `MarkAllNotificationsReadAsync`; `Services/Candidate/NotificationFeed.cs` | Employer-message feed explicitly excludes `NotificationKinds.JobAlert` (`:110-115`); no job query, no dead job links. |
| `hotjob/Services/Employer/CandidateSearchService.cs:SearchAsync/SetDiscoverableAsync`, `ICandidateSearchService.cs` | Employer candidate discovery over opt-in candidates (`IsDiscoverable`); not a job query. |
| `hotjob/Controllers/JobSearchController.cs:MatchResume:24-56` | API delegation only: passes `userId` to `IJobService.GetJobsMatchingResumeAsync`; no job query in the controller. Stub-delegation tests remain green. |
| Apply API entry point | **No `POST /applications` controller exists**; apply is invoked from `Components/Pages/JobDetail.razor:618` through `IApplicationService`, already gated. |
| `hotjob/Data/ApplicationDbContext.cs`, `Data/DemoSeeder.cs`, `Migrations/*`, `project.sql`, `Program.cs` | Schema/seed/wiring; no global query filter added (verified zero `HasQueryFilter` hits), no migration or seed change needed for a query-time predicate. |

"Similar jobs" does not exist in the codebase (no `Similar` symbol in application code).

## 4. Test evidence (real counts)

All commands run from `C:\Users\phulo\Desktop\HotJob` with `--configuration Release`.

| Phase | Command / filter | Result |
|---|---|---|
| Baseline (pre-code) | `dotnet test --configuration Release` | 624 passed, 0 failed, 0 skipped |
| Task 1 RED | `--filter "FullyQualifiedName~PublicJobDetailTests\|FullyQualifiedName~PublicJobBrowseFilterTests"` | **6 failed**, 8 passed, 14 total (only new hidden-company cases) |
| Task 1 GREEN | same | 14 passed, 0 failed |
| Task 2 RED | `--filter "FullyQualifiedName~SavedJobTests\|FullyQualifiedName~JobSkillMatchTests\|FullyQualifiedName~JobSearchControllerTests"` | **2 failed** (saved list only), 22 passed, 24 total; save/recommendation status cases already passed as designed |
| Task 2 GREEN | same | 24 passed, 0 failed |
| Task 3 RED | `--filter "FullyQualifiedName~ApplicationServiceTests"` | **2 failed**, 23 passed, 25 total |
| Task 3 GREEN | same | 25 passed, 0 failed |
| Task 4 RED | `--filter "FullyQualifiedName~JobAlertServiceTests"` | **3 failed**, 13 passed, 16 total |
| Task 4 GREEN | same | 16 passed, 0 failed |
| Task 5 guards | `--filter "FullyQualifiedName~CompanyVisibilityRegressionTests"` | 15 passed, 0 failed (guards for already-correct untouched flows, so no RED expected) |
| Combined targeted | `--filter "FullyQualifiedName~JobTests\|FullyQualifiedName~ApplicationServiceTests\|FullyQualifiedName~JobAlertServiceTests\|FullyQualifiedName~CandidateServiceTests"` | 117 passed, 0 failed |
| Main-requested coverage wave (new alert-feed reapproval theory + existing null-legacy alert tests) | `--filter "FullyQualifiedName~CompanyVisibilityRegressionTests\|FullyQualifiedName~JobAlertServiceTests"` | **33 passed, 0 failed, 0 skipped** (17 regression + 16 alert tests). New cases passed on first run because they close a coverage gap on already-correct behavior; no production change was made. |
| Final full suite | `dotnet test --configuration Release` | **657 passed, 0 failed, 0 skipped** (52 s) |

No test was skipped, disabled, deleted or weakened. Existing happy paths retained their original assertions; only seed-helper signatures gained an optional `CompanyStatus ... = CompanyStatus.Approved` parameter so all original cases still use Approved. The existing `GetJobsMatchingResumeAsync_PendingCompanyExcluded` fact became a Pending/Rejected `[Theory]` with its original assertions intact.

## 5. Mutation verification — HISTORICAL evidence, reported separately from full-suite counts

Run before the main-requested alert-feed coverage wave. The 48/99 figures are the exact observed counts **at that time**; the two new alert-feed reapproval cases were not included in that run and the mutation was deliberately not rerun for them. The final production code is identical to the mutated-then-restored version.

1. Central predicate temporarily inverted to `job.Company.Status != CompanyStatus.Approved` (`PublicJobVisibility.cs:14`).
2. `dotnet test --configuration Release --filter "FullyQualifiedName~JobTests|FullyQualifiedName~ApplicationServiceTests|FullyQualifiedName~JobAlertServiceTests"` → **Failed: 48, Passed: 51, Skipped: 0, Total: 99**. Failures spanned detail, browse, recommendations, saved list, save, apply, alert generation/feed and every reapproval flow — confirming the tests pin the shared predicate.
3. Restored from the exact baseline copy. Source SHA-256 `9923CB2478B30962366A45D9E7C48B731234DBD990E7DCD9BA40266EA11E67EE` matched the baseline byte-for-byte; `Select-String` confirmed `== CompanyStatus.Approved`.
4. **Build nuance:** the first post-restore test invocation still reported the mutated 48 failures because MSBuild reused the stale Release DLL (restored file timestamp was older than the build output). A forced `dotnet build --configuration Release --no-incremental` rebuilt cleanly (12 pre-existing warnings, 0 errors).
5. After the forced rebuild, `dotnet test --configuration Release --no-build --filter ...` → **99 passed, 0 failed**. The then-current normal full suite (`dotnet test --configuration Release`) → 655 passed. No mutation remains in the workspace; final helper hash still matches its baseline copy.

These mutation numbers are historical and are deliberately kept out of the real-suite counts in section 4.

## 6. Fixture changes (narrow, justified)

- `PublicJobDetailTests.SeedJobAsync`: added `companyStatus` optional parameter; all four original tests still seed Approved.
- `PublicJobBrowseFilterTests.SeedJobAsync`: same; all original filter/salary/category tests still seed Approved.
- `SavedJobTests.SeedJobAsync`: same; original round-trip/draft/unauthorized tests unchanged.
- `ApplicationServiceTests.SeedJobAsync`: same on both company seeds; all original snapshot/duplicate/Conflict/withdraw tests unchanged.
- `JobSkillMatchTests`: existing pending-company fact converted to a two-case theory (no assertion changes).
- `JobAlertServiceTests`, `CompanyVisibilityRegressionTests`: new tests only.

Legacy saved bookmarks and alerts are inserted directly in tests where the corresponding public action correctly rejects a hidden company — no production rule was relaxed to make a test pass.

## 7. Precise manual script for main (NOT executed by hy4)

hy4 did not start, stop or mutate the running app or any real database; the following is a review script only. Use a **disposable** test company and records; note ids, never secrets.

**Setup (while company is Approved)**
1. Create a disposable company `CompanyStatus.Approved` and one or more `JobStatus.Published`, unexpired jobs; note `companyId` and `jobId`.
2. As test **Candidate A**: save the job (`/jobs/{jobId}` → Save job), ensure a matching resume exists so `/recommended-jobs` lists the job, publish it so a job alert is created (`/job-alerts` shows the alert), and submit an application that is still in an in-progress status so it is withdrawable. Note the alert id and application id; `/my-applications` shows job title, company name and status.
2b. As test **Candidate B** (a second disposable candidate with their own resume, who has **not** applied to `jobId`): open `/jobs/{jobId}` while the company is Approved so an apply form is available. Candidate B is required for the stale-open apply probe below because Candidate A has already applied and therefore may not show an Apply button at all.

**Revocation — reject THIS previously Approved disposable company via `/admin/companies` (do not use unrelated companies)**
3. Guest and Candidate: `/it-jobs` must not list `jobId`; `/it-jobs?companyId={companyId}` must be empty with consistent counts.
4. Candidate and guest: `/companies/{companyId}` must keep its existing not-found behavior (company pages already gate Approved); the "Show jobs" company-filter URL must be empty.
5. Candidate: `/recommended-jobs` and `/job-alerts` must not reference `jobId`; a legacy alert with no job id (if present) stays visible without a "View job" link.
6. Candidate B: direct `/jobs/{jobId}` must show "Job not found". Using the apply form left open from setup step 2b (Candidate B had **not** applied), attempt to submit: the apply path must return the existing `NotFound` “Job not found.” and create **no** new application, status-history row, bookmark or notification for Candidate B. Verify this by reloading `/my-applications` as Candidate B and checking the database view only if your test-data policy permits. Candidate A's earlier application is checked separately in step 7; do not use Candidate A for this probe because their Apply button may be absent once already applied.
7. Candidate: `/my-applications` still shows the original job title, company name and status for `applicationId`; withdraw remains allowed for an in-progress status, and the CV snapshot fields are unchanged.
8. Admin: `/admin/jobs` still lists `jobId` regardless of the company's rejected status.

**Expected employer restriction (explicit):** `EmployerGuard.RequireActiveAsync` (`hotjob/Services/Employer/EmployerGuard.cs:9-27`) and `JobService.GetAuthorizedEmployerAsync:619-626` already revoke employer actions while the company is non-approved, so do **not** expect the employer UI/ATS to list or act on revoked-company jobs during revocation. Making that UI list them would require changing authorization logic, which is outside this scope. Owned data itself remains intact (proved by `CompanyVisibilityRegressionTests.OwnedQueries_StillReturnDraftAndPublishedJobs_ForNonApprovedCompanies`); after reapproval the employer UI/ATS sees its original jobs/applications again.

**Cleanup (try/finally-style, perform even if any check above fails)**
9. Restore the disposable company to `CompanyStatus.Approved` in `/admin/companies`; refresh `/it-jobs`, `/it-jobs?companyId={companyId}`, `/jobs/{jobId}`, `/recommended-jobs`, `/job-alerts` and `/saved-jobs` and confirm the same job/alert/bookmark reappear with the same ids and no backfill. Confirm employer UI/ATS access returns too. Delete or retire the disposable records per your normal test-data policy.

## 8. Limitations and assumptions

- EF translation was verified on SQLite in-memory (`TestFixture`); no real MySQL/Pomelo database was contacted. The predicate expression is the same shape already used in translated queries elsewhere in the codebase.
- No browser runtime verification was performed. This is **not** because the application is absent — an app instance already existed — but because this worker was not asked to start/stop it or mutate real data. hy4 therefore did not execute any step of section 7.
- No git-based diff was produced; comparison is via the exact baseline copies and `MANIFEST.md`.
- Pre-existing advisories remain untouched (out of scope): `NU1902` MySqlConnector 2.4.0 (moderate), `NU1903` SQLitePCLRaw.lib.e_sqlite3 2.1.10 (high). Pre-existing compiler warnings remain in unrelated files (`JobDetail.razor` duplicate using, `AdminService.cs` unread params/nullability, `RecommendedJobs.razor` and `PublicJobBrowseFilterTests.cs:167` nullability).
- The incremental-build timestamp issue after the mutation restore is documented in section 5; the final source and DLL are both the correct, non-mutated version.
- Assumption resolved within the approved design: hidden-company apply returns the existing `NotFound` (not a new error), and alert history filters by referenced company rather than suppressing the whole notification.

## 9. Main review evidence and coverage-gap closure

- Main independently compared all 10 modified files against the exact baseline copies with Python `difflib`, reviewed the new helper and regression tests and the public page callers, and reported **no unintended production changes**; production code approved in principle.
- Main independently ran `dotnet test --configuration Release` from the repo root: exit 0, **655 passed / 0 failed / 0 skipped, 50 s** (the suite state before this final wave).
- Main identified one coverage gap: reapproval of an **existing alert feed** entry had not been exercised (only generation had). Closed with `CompanyVisibilityRegressionTests.Reapproval_RestoresExistingAlertFeedWithSameAlert` (`[Theory]` Pending/Rejected): Approved → alert visible → revoked → feed empty with total/unread zero → reapproved → the **same alert id/job id** visible with totals/unread restored, read state and notification/job counts unchanged. Null-job legacy behavior remains covered in `JobAlertServiceTests`. No production code changed in this wave.
- hy4's post-wave targeted run: 33 passed / 0 failed. hy4's post-wave full suite: **657 passed / 0 failed / 0 skipped, 52 s**, command `dotnet test --configuration Release` from the repo root.
- Main's independent final verification on FINAL code: same command, exit 0, **657 passed / 0 failed / 0 skipped, 56 s**. Main reported **FINAL REVIEW COMPLETE — approved**; no production or test changes required. This is a separate run from the worker's 52 s evidence, not a replacement count.
- **Main final review: COMPLETED / APPROVED** (recorded in `docs/admin/task-8-progress.md`). This final docs-only bookkeeping wave added the root-cause section, corrected the ML inventory and manual apply probe, and recorded the approval; no tests were rerun for these documentation edits.

## 10. Governance record

No git/worktree commands; original workspace retained. hy4 is the sole worker; no nested or additional agents were spawned. Main session performs the independent review after implementation; the user waived further approval prompts. Spec, plan and ledger with preflight table were approved before code was written. Existing compiler/NuGet warnings are known and out of scope.

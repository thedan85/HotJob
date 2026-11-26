# Public job company-visibility design

Date: 2026-11-26
Status: **approved** — the user approved the proposed design and explicitly waived further approval gates (“just proceed dont need to ask me anything”). hy4 is the sole worker; no nested or additional agents are spawned. Main session monitors and performs the independent review after implementation.

## Intent

Every public/candidate job listing and the apply action must be gated by `CompanyProfile.Status == CompanyStatus.Approved`, in addition to the publication/expiry checks that already exist. The actual gaps are public search/detail, saved bookmarks, apply and job alerts. Recommendations (`JobService.cs:910`) and the save action (`JobService.cs:501`) **already check `CompanyStatus.Approved` inline**; their inline checks are standardized onto the shared predicate, not introduced as new fixes. Unchecked listings produce public dead links once detail is correctly hidden, and let candidates apply to a company that administration has not approved.

The gate must be **one opt-in, non-global query predicate**, never a model-level global query filter: employer-owned, admin, notification-history and application-history flows must keep seeing all of their own data. Visibility is recomputed from the live company row on every query, so administrative re-approval restores listings automatically with no backfill or row mutation.

## Verified baseline (from the read-only audit)

- `CompanyStatus { Pending, Approved, Rejected }`, default `Pending`: `hotjob/Models/Users/CompanyProfile.cs:6-11,48`.
- `JobStatus { Draft, Published, Closed, Expired }`, default `Draft`: `hotjob/Models/Job/JobPosting.cs:6-12,53`.
- `JobPosting.Company` navigation makes `job.Company.Status` EF-translatable: `hotjob/Models/Job/JobPosting.cs:60-61`.
- No `HasQueryFilter` exists anywhere (`ApplicationDbContext.cs`); nothing is globally hidden today.
- Employer authorization already rejects non-approved companies: `Services/Employer/EmployerGuard.cs:9-27` (gate `:23-24`), `Services/Job/JobService.cs:619-626` (gate `:625`), `Services/Billing/EntitlementService.cs:16-32` (gate `:26`), `Services/Billing/BillingService.cs:300`.
- Admin approval only writes `CompanyProfile.Status`/reason/verifier: `Services/Admin/AdminService.cs:194-218` — therefore re-approval is automatic for a query-time predicate.
- Existing inline company gates (to be replaced by the shared predicate where candidate/public): `JobService.cs:501` (save), `:910` (recommend), `CompanyService.cs:158` (review), `CompanyRepository.cs:27,54,60,73`, `EmployerGuard.cs:23`, `EntitlementService.cs:26`, `BillingService.cs:300`.

Root cause of the leak: `Repositories/Job/JobPostingRepository.cs:25-35` (`GetPublishedByIdAsync`) and `:74-93` (`BrowsePublishedAsync`) filter only Published + expiry; `Services/Candidate/ApplicationService.cs:76-101` (`ApplyAsync`) filters only Published + expiry; `JobService.cs:431-452` (`GetSavedJobsAsync`) and `Services/JobAlerts/JobAlertService.cs:33-55,164-187` have no company gate. Public cards link to `/jobs/{id}` (`Components/Pages/Jobs.razor:130`, `SavedJobs.razor:67,86`, `RecommendedJobs.razor:89`, `JobAlerts.razor:125`), so hidden jobs become dead links unless every one of those queries is gated.

## Goals

1. One shared predicate file exposing `Expression<Func<JobPosting, bool>>` for `CompanyStatus.Approved` and an opt-in `IQueryable<JobPosting>.WhereCompanyApproved()` extension. No global filter, no schema change, no row mutation.
2. Apply it to every public/candidate job read: browse/search (including `companyId` filter), public detail, recommended/skill-match (including its API delegate), save action, saved list, alert generation and alert feed.
3. Apply it to `ApplicationService.ApplyAsync` before any snapshot, application row, history row or notification is created.
4. Detail of a non-approved company's job is the existing `ServiceErrorType.NotFound` / “Job not found.”; apply returns the same existing `NotFound` before side effects. Existing “not accepting applications” `Conflict` behavior for Draft/Closed/Expired/expired jobs is retained.
5. Saved list and alert feed silently exclude jobs whose company is not Approved, with consistent totals/unread/pagination. Legacy alerts with `RelatedJobPostingId == null` remain visible and already render no job link (`Components/Pages/JobAlerts.razor:123-126`).
6. Reapproval restores all of the above automatically; no stored flag, no backfill.

## Non-goals

- No change to employer-owned job CRUD/list/detail (`Services/Job/JobService.cs:32-342` owned methods, `Controllers/EmployerJobsController.cs`, `Components/Pages/Employer/*`) or `EmployerGuard` semantics — they already reject non-approved companies and must continue to use the guard, not the public predicate.
- No change to admin job list/delete (`Services/Admin/AdminService.cs:220-254`, `Components/Pages/Admin/AdminJobs.razor`) or approval workflow.
- No change to application history/withdraw (`Services/Candidate/CandidateService.cs:19-96`, `Services/Candidate/ApplicationService.cs:230-269`, `Components/Pages/MyApplications.razor`): those pages already render plain job/company titles and contain no `/jobs/` link, so existing applications from a later-rejected company stay visible and withdrawable with snapshots untouched.
- No change to match-score computation (`Services/ML/MatchScoreService.cs`), job expiry sweep (`Services/Job/JobExpiryService.cs`), public company pages (`Services/Company/*`, `Repositories/Company/*`), auth/billing, CSS, seeders, migrations, `project.sql` or `Program.cs`.
- No "similar jobs" feature exists in the codebase; employer candidate-discovery (`Services/Employer/CandidateSearchService.cs`) is not a job query and is out of scope.
- No global query filter, no new controller/API endpoint, no DTO field, no Razor markup change (except none required).

## The single predicate

New file `hotjob/Services/Common/PublicJobVisibility.cs`, namespace `hotjob.Services.Common`:

- `public static readonly Expression<Func<JobPosting, bool>> CompanyApproved` whose body is exactly `job => job.Company.Status == CompanyStatus.Approved`.
- `public static IQueryable<JobPosting> WhereCompanyApproved(this IQueryable<JobPosting> query) => query.Where(CompanyApproved);`

Implementation rule: call the extension **only on a query root/fluent chain outside a lambda**. To filter `SavedJobs` or `Notifications` by referenced job, first create `IQueryable<int> visibleJobIds = dbContext.JobPostings.AsNoTracking().WhereCompanyApproved().Select(job => job.Id)` and use `visibleJobIds.Contains(...)` in the lambda. Calling `.WhereCompanyApproved()` inside an expression tree is not EF-translatable.

## Decision table

| Query (file:method) | Change | Pending/Rejected company result |
|---|---|---|
| `Repositories/Job/JobPostingRepository.cs:25-35 GetPublishedByIdAsync` (← `JobService.cs:396-413 GetPublishedAsync`, `Components/Pages/JobDetail.razor:654`) | append `.WhereCompanyApproved()` to base query | `NotFound` via the existing null check — no new error path |
| `Repositories/Job/JobPostingRepository.cs:74-157 BrowsePublishedAsync` (← `JobService.cs:344-381`, `Components/Pages/Jobs.razor:286`; also `CompanyDetail.razor:197` → `?companyId=`) | append `.WhereCompanyApproved()` to base query before filters | job absent; `TotalCount` excludes it |
| `JobService.cs:847-1021 GetJobsMatchingResumeAsync` (query `:906-911`; API `Controllers/JobSearchController.cs:24-56` delegates here) | replace inline company check with `.WhereCompanyApproved()` | job absent; controller untouched |
| `JobService.cs:483-549 SaveJobAsync` (isOpen `:495-503`) | replace inline company check with the predicate | existing `NotFound` |
| `JobService.cs:415-462 GetSavedJobsAsync` | join/filter saved rows through `visibleJobIds` | bookmark omitted; `TotalCount` consistent |
| `Services/Candidate/ApplicationService.cs:58-228 ApplyAsync` (lookup `:76-101`) | append `.WhereCompanyApproved()` to job lookup; keep existing status/expiry `Conflict` checks at `:90-101` | `NotFound` “Job not found.” before snapshot/history/application/notification |
| `Services/JobAlerts/JobAlertService.cs:28-138 NotifyForPublishedJobAsync` (lookup `:33-44`) | append `.WhereCompanyApproved()` to job lookup | 0 alerts created |
| `Services/JobAlerts/JobAlertService.cs:140-191 GetAlertsAsync` | filter by `RelatedJobPostingId == null || visibleJobIds.Contains(id)` | referenced alert omitted; unread/total/pagination computed from the filtered set; null-id legacy alerts kept |

Publication/expiry checks, ordering, paging, DTO shapes, status labels and all existing error strings stay exactly as they are.

## Reapproval

Visibility is computed from `job.Company.Status` at query time. Once an administrator sets the company back to `Approved` (`AdminService.UpdateCompanyStatusAsync:194-218`), the next browse/detail/save/saved/recommend/apply/alert query sees the job again. No job/application/notification row is edited, deleted or backfilled.

## Constraints

- Do not run any git command (including `status`, `diff`, `log`, `add`, `commit`) and do not use worktree tools or scripts. Record overrides in the progress ledger.
- Do not print secrets, start the application, or access/change the user's real database. Tests use only SQLite in-memory via `Tests/hotjob.Tests/Support/TestFixture.cs:89-120`.
- Preserve all unrelated pre-existing edits; treat recently modified candidate/profile files as user work.
- Before any source edit: copy exact pre-edit bytes of every existing file to be modified into `C:\Users\phulo\AppData\Local\Temp\opencode\task-8-baseline\<relative path>` and write a created-vs-modified manifest there. For new files, snapshot the file immediately after creation for mutation-restore purposes.
- No subagents; main session reviews. No extra human approval gates.
- Verify translation on SQLite; the expression is already used in translated EF queries elsewhere, so MySQL/Pomelo parity is expected.
- Never weaken or delete an existing assertion. Narrow fixture changes (adding `Status = CompanyStatus.Approved` where a happy-path seed currently relies on the default `Pending`) are permitted only when the changed query requires it, and must be listed in the final report.
- Mutation verification evidence must be reported separately from real full-suite counts; never leave the mutation in place.

## Verification

- Targeted RED/GREEN via `dotnet test --configuration Release --filter "FullyQualifiedName~..."` from the repository root.
- Final full suite from the repository root: `dotnet test --configuration Release` (background if needed; wait for automatic completion, never poll).
- Mutation check at test stage: invert the central predicate (`CompanyApproved` → non-approved), confirm the targeted visibility regressions fail, restore from the exact baseline copy, rerun targeted and full suites normally.
- Final report: `docs/admin/task-8-result.md` with complete inventory, exact files, full test evidence counts, mutation evidence, manual browser steps and limitations.

## Files in scope

Production (modify): `hotjob/Repositories/Job/JobPostingRepository.cs`, `hotjob/Services/Job/JobService.cs`, `hotjob/Services/Candidate/ApplicationService.cs`, `hotjob/Services/JobAlerts/JobAlertService.cs`.
Production (create): `hotjob/Services/Common/PublicJobVisibility.cs`.
Tests (modify): `JobTests/PublicJobDetailTests.cs`, `JobTests/PublicJobBrowseFilterTests.cs`, `JobTests/SavedJobTests.cs`, `JobTests/JobSkillMatchTests.cs`, `CandidateTests/ApplicationServiceTests.cs`, `JobAlertTests/JobAlertServiceTests.cs`, plus a new regression file for untouched flows if needed.
Docs: this spec, `docs/superpowers/plans/2026-11-26-public-job-company-visibility.md`, `docs/admin/task-8-progress.md`, `docs/admin/task-8-result.md`.

Out of scope: see Non-goals.

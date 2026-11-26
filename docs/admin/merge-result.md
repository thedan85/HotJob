# Merge resolution report — 2026-11-26

## 1. Summary and scope

The four conflict regions in `JobPostingRepository.cs` and `JobService.cs` have been resolved in file contents. The teammate's public-list projection queries, approval workflow, and company-deactivation rule are retained. Task 8's company-approval gate remains opt-in, and saved/history-style queries do not acquire publication/expiry filters.

**Verification verdict:** solution builds; full suite is **798 passed, 1 failed, 0 skipped, 799 total**. The sole failure is the rejected-company apply response disagreement in section 8. This is not an all-green/ready-to-merge claim.

Company activity alignment required only **three additional query filters**: saved-list lookup, alert generation, and alert-feed lookup. Apply already checks activity; it was not edited.

No Git commands were run, so the Git index was not updated or marked resolved. No app was started, no migrations were run, and no real application database was used. Verification uses the existing SQLite in-memory test fixtures.

### Checklist

- [x] Read both sides of all four conflict regions and both visibility helpers.
- [x] Establish the exact meaning of company activity.
- [x] Resolve the four regions without removing list projections.
- [x] Preserve task 8's approval helper and per-flow publication/expiry behavior.
- [x] Add activity-alignment regression coverage and fix the merge-related test compilation issue.
- [x] Build the complete solution in Release.
- [x] Finish the full root-level test run and record counts, including the unresolved semantic disagreement.
- [x] Search all repository files, including hidden and ignored files, for remaining conflict markers.
- [x] Review source edits against pre-edit copies without Git.

## 2. Exact meaning of “company active”

`hotjob/Models/Job/JobVisibility.cs` defines:

```csharp
public static Expression<Func<JobPosting, bool>> OpenToCandidates(DateTime nowUtc) =>
    job => job.Status == JobStatus.Published
        && (job.ExpiryDate == null || job.ExpiryDate > nowUtc)
        && job.Company.Status == CompanyStatus.Approved
        && job.Company.IsActive;
```

The additional rule is exactly **`job.Company.IsActive == true`**. This is `CompanyProfile.IsActive`, a separate boolean soft-deactivation flag, defaulting to `true` (`Models/Users/CompanyProfile.cs`). It is **not** employer membership, employer activity, user-account activity, subscription validity, or recent activity.

`CompanyAdminService.DeactivateAsync` sets it to `false` and records the deactivating admin/time. `ReactivateAsync` sets it back to `true` and clears that metadata. The predicate checks the boolean, not those metadata fields. Approval and activity are independent: an Approved-but-inactive company fails the teammate's rule.

This is strictly stronger than `PublicJobVisibility.CompanyApproved`, which checks **only** `Company.Status == Approved`. That helper and its contract remain unchanged.

## 3. Each original conflict and its resolution

The snippets below reproduce the code on each side of the four regions, without reintroducing conflict delimiters into the repository. Line numbers refer to the original conflicted files.

### Region 1 — repository `GetPublishedByIdAsync` (original lines 33–41)

**Our side**, following `return DetailsQuery()`:

```csharp
    .Where(job => job.Id == id
        && job.Status == JobStatus.Published
        && (job.ExpiryDate == null || job.ExpiryDate > now))
    .WhereCompanyApproved()
```

**Teammate's side**:

```csharp
    .Where(JobVisibility.OpenToCandidates(now))
    .Where(job => job.Id == id)
```

**Resolution:** retained the teammate's side. The existing `DetailsQuery()` includes and `FirstOrDefaultAsync(cancellationToken)` remain. The helper subsumes Published, unexpired, and task 8's Approved checks, adding the company's activity flag. Hidden detail still returns the existing `NotFound`.

### Region 2 — repository `BrowsePublishedAsync` (original lines 122–131)

**Our side**:

```csharp
var query = DetailsQuery()
    .Where(job => job.Status == JobStatus.Published
        && (job.ExpiryDate == null || job.ExpiryDate > now))
    .WhereCompanyApproved();
```

**Teammate's side**:

```csharp
var query = dbContext.JobPostings
    .AsNoTracking()
    .Where(JobVisibility.OpenToCandidates(now));
```

**Resolution:** retained the teammate's no-tracking base query. All keyword/city/skill/company/type/category/salary filters, featured/date/id ordering, count, pagination, and `PublicJobListItemResponse` projection are unchanged. The list does not revert to loading full detail graphs. `ListOwnedAsync` and its separate projection are also unchanged.

### Region 3 — service `SaveJobAsync` (original lines 545–555)

Both sides follow `dbContext.JobPostings.AsNoTracking()`.

**Our side**:

```csharp
    .WhereCompanyApproved()
    .AnyAsync(
        job => job.Id == jobId
            && job.Status == JobStatus.Published
            && (job.ExpiryDate == null || job.ExpiryDate > now),
        cancellationToken)
```

**Teammate's side**:

```csharp
    .Where(JobVisibility.OpenToCandidates(now))
    .AnyAsync(job => job.Id == jobId, cancellationToken)
```

**Resolution:** retained the teammate's side. Creating a new bookmark already required an open job; the central open-job predicate preserves that rule and task 8's approval check while adding activity. The existing `NotFound`, duplicate-save handling, timestamps, and write/concurrency logic remain.

This is **not** `GetSavedJobsAsync`: listing existing bookmarks must retain Draft/Closed/expired status labels, so that query deliberately does not use `OpenToCandidates`.

### Region 4 — service `GetJobsMatchingResumeAsync` (original lines 938–946)

Both sides follow `dbContext.JobPostings.AsNoTracking()`.

**Our side**:

```csharp
    .Where(job => job.Status == JobStatus.Published
        && (job.ExpiryDate == null || job.ExpiryDate > now)
        && job.JobSkills.Any(jobSkill => resumeSkillIds.Contains(jobSkill.SkillId)))
    .WhereCompanyApproved();
```

**Teammate's side**:

```csharp
    .Where(JobVisibility.OpenToCandidates(now))
    .Where(job => job.JobSkills.Any(jobSkill => resumeSkillIds.Contains(jobSkill.SkillId)));
```

**Resolution:** retained the teammate's side. Resume ownership, matching-skill filtering, city/type/minimum-match filters, SQL projection, counts, paging, ordering, score lookup, and matched-skill loading are unchanged. Approved-but-deactivated companies are excluded; reactivation restores their eligible jobs.

## 4. Tiny activity alignment and preserved behavior

| Flow | Final rule and reason |
|---|---|
| Public detail, browse, recommendations, new saves | `JobVisibility.OpenToCandidates(now)`; these paths already require Published + unexpired. |
| Existing saved-job list | Keep `WhereCompanyApproved()` and add `.Where(job => job.Company.IsActive)` to `visibleJobIds`. Do not filter job status or expiry; existing labels survive. |
| Apply | **Unchanged.** Keep `WhereCompanyApproved()` in the lookup. Existing projected `CompanyOpen = Approved && IsActive` rejects deactivated Approved companies with `Conflict`. Existing Draft/Closed/non-Published and expired-job `Conflict` paths remain. Pending/Rejected companies are filtered out first and return task 8's `NotFound`, without application/history/notification side effects. |
| Alert generation | Keep `WhereCompanyApproved()` and add `.Where(row => row.Company.IsActive)`. Existing publication/expiry checks and deduplication remain. |
| Alert feed | Keep `WhereCompanyApproved()` and add activity filtering to `visibleJobIds`; no publication/expiry filter. Null-job legacy alerts remain visible. Total/unread counts and pagination still use the same filtered feed query. |
| Reactivation/reapproval | Query-time visibility only. Bookmarks and notifications are hidden, not deleted; the same rows reappear when company visibility is restored. |
| Employer-owned/admin queries and application history | No production changes. They are not given a global/public job filter. |

## 5. Exact files edited

1. `hotjob/Repositories/Job/JobPostingRepository.cs`
   - Resolve two regions; remove the now-unused `Services.Common` import.
2. `hotjob/Services/Job/JobService.cs`
   - Resolve two regions; add one company-activity filter to saved-list lookup.
3. `hotjob/Services/JobAlerts/JobAlertService.cs`
   - Add one company-activity filter each to generation and feed lookup.
4. `hotjob/Tests/hotjob.Tests/JobTests/CompanyVisibilityRegressionTests.cs`
   - Migrate the admin-list regression test setup to `fx.CreateJobAdminService().ListAsync(1, 20)`. The teammate split job administration out of `AdminService` and made audit-service injection mandatory; the old three-argument constructor and `GetJobsAsync` call no longer compile. All existing assertions remain unchanged.
   - Add seven cases: five saved-list/feed state variants, one alert-generation/reactivation/deduplication test, and one save/recommendation/reactivation test.
5. `docs/admin/merge-result.md`
   - This report and execution checklist.

Intentionally unchanged: `PublicJobVisibility.cs`, `JobVisibility.cs`, `ApplicationService.cs`, `JobVisibilityTests.cs`, production admin code, DTOs/interfaces, migrations, schema, fixtures, app configuration, and UI.

Pre-edit source copies and a hash manifest are stored outside the repository at `C:\Users\phulo\AppData\Local\Temp\opencode\merge-resolution-baseline`. Comparison confirmed only the four source/test files listed above changed; the report is the fifth repository edit. No tests were removed, disabled, skipped, or weakened.

## 6. Build and test evidence

Commands run from `C:\Users\phulo\Desktop\HotJob`. Release was used throughout to avoid interfering with a potentially running Debug app.

### Baseline and regression checks

| Step | Command/filter | Result |
|---|---|---|
| Before resolving source | `dotnet build --configuration Release --no-incremental` | Failed with 12 `CS8300` marker errors across the four regions. |
| After resolving regions | Focused job-visibility/projection tests | Test compilation exposed the stale admin-list test setup (`CS7036`, then removed API `CS1061`). Fixed by using the current job-admin fixture/service; assertions untouched. |
| Saved/feed RED | `dotnet test --configuration Release --filter "FullyQualifiedName~CompanyActivity_HidesAndRestoresSavedJobsAndAlertFeed_WithoutFilteringJobState"` | 5 failed, 0 passed: inactive-company bookmarks were visible. |
| Saved-only fix | Same filter | 5 failed, 0 passed: bookmark filtering now worked, but inactive-company alert-feed entries remained. |
| Saved/feed GREEN | Same filter | 5 passed, 0 failed, 0 skipped. |
| Generation RED | `dotnet test --configuration Release --filter "FullyQualifiedName~CompanyActivity_Blocks"` | 1 failed, 1 passed: inactive-company alert generation still created a notification; save/recommendations already respected activity after conflict resolution. |
| Combined relevant tests | `dotnet test --configuration Release --filter "FullyQualifiedName~JobTests\|FullyQualifiedName~ApplicationServiceTests\|FullyQualifiedName~JobAlertTests"` | 135 passed, 1 failed, 0 skipped, 136 total, 15 s. Sole failure is the semantic disagreement documented below. |
| Mutation check | Temporarily negate the newly added alert-generation activity filter, then run `--filter "FullyQualifiedName~CompanyActivity_BlocksAlertGeneration_UntilReactivated"` | 1 failed as expected (created 1 alert instead of 0). Restored the correct positive filter immediately; final build forces recompilation. No mutation remains. |

### Final solution build

`dotnet build --configuration Release --no-incremental`

**Succeeded: 0 errors, 13 warnings**, 8.25 s. Build output: `C:\Users\phulo\AppData\Local\Temp\opencode\merge-resolution-build.log`.

Warnings left unchanged and outside this merge fix:

- `NU1902`: MySqlConnector 2.4.0 moderate advisory.
- `NU1903`: SQLitePCLRaw.lib.e_sqlite3 2.1.10 high advisory.
- Existing duplicate-using, unread-parameter, and nullable warnings in `JobDetail.razor`, `RecommendedJobs.razor`, `AdminService.cs`, `CandidateReviewTests.cs`, and `PublicJobBrowseFilterTests.cs`.

### Full test run

```powershell
dotnet test --configuration Release --logger "trx;LogFileName=merge-full.trx" --results-directory "C:\Users\phulo\AppData\Local\Temp\opencode\merge-resolution-test-results"
```

No filter, exclusions, or `--no-build` were used. Console output is also captured at `C:\Users\phulo\AppData\Local\Temp\opencode\merge-resolution-test.log`.

**Exit code 1. Complete result:**

```text
Failed!  - Failed: 1, Passed: 798, Skipped: 0, Total: 799, Duration: 1 m 7 s - hotjob.Tests.dll (net10.0)
```

The TRX counters independently confirm **799 executed, 798 passed, 1 failed, 0 not executed, 0 errors, 0 timeouts, 0 aborted**. TRX: `C:\Users\phulo\AppData\Local\Temp\opencode\merge-resolution-test-results\merge-full.trx`.

All seven added regression cases passed on the final restored production code, including the previously mutation-sensitive alert-generation test. The only failed test is listed in section 8; no other full-suite failures remain.

## 7. Conflict-marker scan

Scanned **all 1,658 files** under the repository root, including hidden files, ignored files, `.git` metadata, `bin`/`obj`, and binary files, with **no directory exclusions**. Result: **0 files containing conflict-delimiter lines, 0 read/scan errors**.

The scan checks standard start/separator/end delimiters and diff3 ancestor delimiters at line boundaries, including BOM-encoded UTF-16/UTF-32 files. It prints only paths/counts, not file contents. Evidence: `C:\Users\phulo\AppData\Local\Temp\opencode\merge-resolution-marker-scan.json`.

### Final scoped review

- Correctness: all four chosen branches match the requested intent; preserved Draft/Closed/expiry and hidden-company apply semantics.
- Architecture/readability: existing helpers and fixture factories reused; no new abstraction, package, API, or global query filter.
- Security: activity/approval gates are server-side; candidate/resume ownership and notification-user scoping remain unchanged. No secrets were added or printed.
- Performance: list DTO projections, no-tracking queries, counts, ordering, and pagination retained; no new per-row query loop.
- Scope: source comparison confirms only the four listed source/test files changed. `ApplicationService.cs`, the approval-only helper, and the disagreeing teammate test are byte-for-byte unchanged from the pre-edit baseline.
- Verification: solution build passes; full suite remains blocked only by the explicitly reported test-contract disagreement, not by a compiler error or a suppressed test.

## 8. Open question: rejected-company apply response

**Confirmed semantic disagreement, deliberately not hidden by editing a test or weakening production behavior:**

- Test: `hotjob.Tests.JobTests.JobVisibilityTests.JobsOfAnInactiveCompany_AreHiddenAndClosedToApplications(companyState: "rejected")`.
- Location: `hotjob/Tests/hotjob.Tests/JobTests/JobVisibilityTests.cs:51`.
- Teammate's test expects `ServiceErrorType.Conflict`.
- Task 8 explicitly requires `ServiceErrorType.NotFound` for non-approved companies. Existing task 8 tests also assert `NotFound`.
- Actual result after preserving task 8: **`NotFound`**.
- For an **Approved but deactivated** company, apply still returns **`Conflict`**, matching the teammate's activity behavior.

The recommended contract under the user's instructions is to retain task 8's `NotFound` for Pending/Rejected companies and the existing `Conflict` for Approved-but-deactivated or non-open jobs. A follow-up decision is needed before updating the teammate's rejected-case expectation. Neither that test nor `ApplicationService.cs` was modified.

No real-database or browser-runtime validation was performed, per the no-app-startup/no-database constraint. The tests verify relational query translation using SQLite, not a production MySQL connection.

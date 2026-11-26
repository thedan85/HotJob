# Task 7 — Server-side paging for the candidate “My applications” page

## Work checklist

- [x] Verify the real read path and the existing paging conventions before changing code.
- [x] Add a database-side paged query with stable ordering, `AsNoTracking` and a separate total count.
- [x] Keep the existing unpaged method working and migrate the page consistently.
- [x] Wire the existing pager pattern with loading, empty and out-of-range behaviour; keep withdraw working.
- [x] Keep the existing status filter and newest-first ordering, applied in the database.
- [x] Add xUnit service tests and page-level UI tests.
- [x] Run the FULL `dotnet test` from the repository root and record the counts.
- [ ] Manual browser verification — not executed (see the steps below).
- [x] Write this report.

## Summary and root cause

`MyApplications.razor` previously called `CandidateService.GetMyApplicationsAsync`, which loaded every application owned by the candidate, including the job, company, resume, status history and interviews, and then filtered and sorted the whole list in memory in the component. The cost therefore grew with the candidate’s total application history, and the page had no real page boundaries.

The task description named `Services/Candidate/ApplicationService.cs` as the data source, but that service only owns apply/withdraw rules. The read path is `Services/Candidate/CandidateService.cs`; the paging was added there. `ApplicationService` was not modified.

A new `GetMyApplicationsPageAsync(userId, MyApplicationsQuery)` query now applies ownership, search and status filters in SQL, counts the filtered rows, and fetches only `Skip((page - 1) * pageSize).Take(pageSize)` rows ordered by `AppliedAt DESC, Id DESC`. The page renders that slice and the existing `workspace-pagination` markup. The original `GetMyApplicationsAsync` method still exists and behaves exactly as before for its remaining callers.

## Files changed

1. `hotjob/DTOs/Candidate/ApplicationDtos.cs` — added `MyApplicationsQuery` (search, status group, page, page size, default/max constants) and `MyApplicationsPageDto` (page result plus header counts).
2. `hotjob/DTOs/Employer/ApplicantDtos.cs` — removed `sealed` from `PagedResult<T>` so the candidate page result can extend the existing paging shape instead of duplicating it. No behaviour or member changes.
3. `hotjob/Services/Candidate/ICandidateService.cs` — added `GetMyApplicationsPageAsync`; kept `GetMyApplicationsAsync`.
4. `hotjob/Services/Candidate/CandidateService.cs` — added the paged query, shared ownership/filter/projection helpers, and reused them in the original unpaged method.
5. `hotjob/Components/Pages/MyApplications.razor` — server-side paging UI, database-backed filters, page-change/withdraw refresh, out-of-range correction, loading/empty states.
6. `hotjob/Tests/hotjob.Tests/CandidateTests/CandidateServiceTests.cs` — added 9 service tests plus a batch seeding helper.
7. `hotjob/Tests/hotjob.Tests/CandidateTests/MyApplicationsPageTests.cs` — added 4 real-page render/interaction tests.
8. `docs/admin/task-7-result.md` — this report.

No migrations, schema changes or indexes were added.

## Design decisions

**Page size.** `MyApplicationsQuery.DefaultPageSize = 10` and `MaxPageSize = 50`. This mirrors the closest existing convention (`CandidateSearchService`: default 10, max 50). `Page < 1` becomes 1; `PageSize <= 0` becomes the default; anything above the maximum is clamped to 50. This is the same `Math.Max`/`Math.Clamp` shape already used by notifications, applicants and candidate search.

**Ordering.** `ORDER BY AppliedAt DESC, Id DESC`, matching the previous in-memory order and the employer applicant queue. The `Id` tiebreaker makes the order deterministic when two applications share a timestamp, so rows cannot repeat or disappear between pages.

**Filtering.** Search uses `EF.Functions.Like` on job title and company name, matching the employer applicant repository’s search style. The status group (`active`, `offers`, `closed`, empty = all) maps to explicit status predicates. Filters are applied before both `CountAsync` and `Skip/Take`, so the pager’s `TotalCount`/`TotalPages` reflect the filtered set. The search box keeps its live `oninput` behaviour with a 300 ms debounce so typing does not issue a query per keystroke; the status select applies immediately and resets to page 1.

**Out-of-range pages.** The service follows the existing `JobAlertService` convention: a page past the end returns an empty `Items` list with the correct `TotalCount`/`TotalPages` rather than clamping silently. The component detects `requestedPage > TotalPages` after a reload and asks for the last page. This is what makes “withdraw the only item on the last page” return to the previous page automatically.

**Header counts.** The metrics at the top of the page are not page-scoped: `TotalApplications`, `InProgressCount` and `OfferCount` are computed over all applications owned by the caller with separate count queries, while the pager uses the filtered `TotalCount`. Without this, the stats would silently change meaning after paging.

**Callers.** `GetMyApplicationsAsync` is retained with its original signature and behaviour (all rows, same ordering, same DTO). It now shares the ownership query and DTO projection with the paged method. The only production caller, `MyApplications.razor`, was migrated to `GetMyApplicationsPageAsync`. Existing tests that call the unpaged method were left unchanged and still pass.

**DTO reuse.** `MyApplicationsPageDto : PagedResult<MyApplicationDto>` reuses the app’s existing `PagedResult<T>` contract (`Items`, `Page`, `PageSize`, `TotalCount`, `TotalPages`) and adds the three header counts. `PagedResult<T>` had no derived types before, so unsealing it is a source-compatible change.

**Preserved behaviour.** The `AuthorizeView Roles="Candidate"` guard, `CircuitDbGate` wrapping around candidate reads and the withdraw call, the withdraw rules in `ApplicationService`, snapshots, ATS, billing and authentication were not changed. Notifications still load through `NotificationFeed` exactly as before.

## Tests added

Service tests in `CandidateServiceTests`:

- `GetMyApplicationsPage_SlicesRequestedPageAndReportsTotal`
- `GetMyApplicationsPage_OrderIsStableAcrossPages`
- `GetMyApplicationsPage_BeyondLastPage_ReturnsEmptyPageAndKeepsTotal`
- `GetMyApplicationsPage_ClampsPageSizeToHardMaximum`
- `GetMyApplicationsPage_UsesDefaultPageSizeWhenNotSet`
- `GetMyApplicationsPage_ReturnsOnlyCallersApplications`
- `GetMyApplicationsPage_FilterAndPagingApplyTogether`
- `GetMyApplicationsPage_Anonymous_Unauthorized`
- `GetMyApplicationsPage_NoCandidateProfile_NotFound`

Page tests in `MyApplicationsPageTests` (real `MyApplications` component rendered with `StaticHtmlRenderer` against SQLite; private handlers invoked the same way the existing Premium/Profile page tests do, because the repository has no bUnit):

- `Render_ShowsFirstPageWithPagerAndTotals`
- `ChangePage_ShowsRequestedSlice`
- `WithdrawOnlyItemOnLastPage_ReturnsToPreviousPage`
- `Render_NoApplications_ShowsEmptyState`

The first focused run exposed two real failures, both fixed before the final run: EF Core could not translate a captured `ApplicationStatus[]` `.Contains` (it bound to the span overload), and the page tests consequently saw the error state instead of the pager. Replacing the array check with explicit status comparisons and using unique company names in the seed helper made the focused suite green (22/22).

## Full test result

Command, run from the repository root with no test filter:

```text
dotnet test -c Release
```

Result:

```text
Passed!  - Failed:     0, Passed:   624, Skipped:     0, Total:   624, Duration: 53 s - hotjob.Tests.dll (net10.0)
```

Baseline before this task was 611 passing tests; the 13 new tests account for the increase to 624. No tests were skipped or disabled.

## Manual browser test steps

Not executed here: starting the app would run the configured migration/seed path against a real database, which the task explicitly prohibited. Automated SQLite/service and component tests above passed.

Use a disposable development database and a candidate account.

1. Seed or apply to at least 25 jobs so the default page size produces three pages. Sign in and open `/my-applications`.
   - Expect exactly 10 cards, “Showing 1–10 of 25”, “Page 1 of 3”, and the Applications stat showing 25.
   - Expect the newest application first; on page 2, confirm no card repeats from page 1.
2. Use “Next →” and “← Previous”. Confirm the range text, page label and disabled state at the first and last page. Confirm the URL/back button are not required for paging.
3. Set Status to “In progress”. Confirm the list, “Showing …”, total and total pages all change to the filtered set and the page resets to 1.
4. Type part of a job title and part of a company name. After roughly 300 ms the list narrows; combine the search with a status filter and confirm both are applied together and the totals match.
5. With an “In progress” filter and 21 matching applications, go to the last page (one card), withdraw that application and confirm.
   - Expect the page to move back automatically to page 2 of 2, “Showing 11–20 of 20”, and the withdrawn card to disappear from the active list.
   - Expect the “Application withdrawn.” notice and updated In-progress/Applications stats.
6. Withdraw a card on a middle page and confirm the page stays on the same page and the card is refreshed (status becomes Withdrawn when “All applications” is selected).
7. Clear filters and confirm the page returns to page 1 of the unfiltered list.
8. Check the empty states: a candidate with no applications sees “No applications yet”; a filter with no matches sees “Nothing matches those filters” and the Clear filters button.
9. Keyboard/screen-reader check: Previous/Next are disabled at the boundaries, the list section reports `aria-busy` while a page is loading, and the pager labels are announced.

## Open issues, assumptions and recommendations

- **Index (recommendation only, not added):** `Applications` currently has a foreign-key index on `CandidateId`, plus the unique `(JobPostingId, CandidateId)` index. A composite index on `(CandidateId, AppliedAt)` (optionally including `Id` for the tiebreaker) would let the database satisfy the ownership filter and the newest-first page without a sort. It was not added because no migration was requested and the current volume was not measured.
- **Extra count queries:** each page load issues three small count queries for the header stats in addition to the filtered count and the page query. If profiling shows this matters, they can be folded into one grouped query.
- **Case sensitivity:** search relies on the database collation, like the existing applicant search. MySQL’s default collation is case-insensitive; SQLite `LIKE` is case-insensitive for ASCII, which is what the tests exercise.
- **UI test level:** the page tests render the real component and invoke its real handlers, but they are not a browser end-to-end test. Actual circuit reconnection, visual layout and keyboard behaviour remain part of the manual steps.
- **Assumption:** the candidate page is the only production caller of the unpaged method. A repository-wide search confirmed that only tests still call it.

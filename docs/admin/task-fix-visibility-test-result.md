# Visibility-test contract fix

Date: 2026-11-27

## Root cause and ownership of the expectations

This was a merged test-contract disagreement, not a Task 10 regression.

- `PublicJobVisibility.CompanyApproved` (`hotjob/Services/Common/PublicJobVisibility.cs:14`)
  requires `job.Company.Status == CompanyStatus.Approved`.
- `ApplicationService.ApplyAsync` applies `.WhereCompanyApproved()` to its lookup
  at lines 76–90. Pending/Rejected companies therefore produce no matching job;
  lines 91–92 return `NotFound`, `"Job not found."`, before application writes.
- This is Task 8's intended contract, documented in `docs/admin/task-8-result.md`
  sections 3 and 8, and asserted by
  `ApplicationServiceTests.ApplyAsync_NonApprovedCompany_ReturnsNotFoundWithoutSideEffects`.
- The teammate's merged
  `JobVisibilityTests.JobsOfAnInactiveCompany_AreHiddenAndClosedToApplications`
  previously expected `Conflict` for both of its parameter cases at line 51.
  `docs/admin/merge-result.md:228-239` explicitly attributes that expectation
  to the teammate's test. No individual author name is established by these
  files; no git/blame commands were run.

## Parameter cases and why they differ

| Case | Before fix | Intended apply result | Reason |
| --- | --- | --- | --- |
| `rejected` in the merged theory | Failed | `NotFound` | Approval-only lookup filters the job out before later checks. |
| `deactivated` in the same theory | Passed | `Conflict` | Company remains Approved, so lookup succeeds; `CompanyOpen = Approved && IsActive` is false and the later conflict branch runs (`ApplicationService.cs:87-96`). |
| Pending company, separate Task 8 theory | Passed | `NotFound` | Same approval-only filter; existing assertions also require no application/history/notification side effects. |

The merged theory has **only** `deactivated` and `rejected` parameters, not
Pending or Suspended. `CompanyStatus` contains Pending/Approved/Rejected;
inactivity/deactivation is the separate `IsActive=false` flag, not a Suspended
enum value. Public browse/detail still hide approved-but-inactive jobs through
`JobVisibility.OpenToCandidates`, while apply correctly remains blocked.

## Exact change

Only the apply-error assertion in
`hotjob/Tests/hotjob.Tests/JobTests/JobVisibilityTests.cs` changed, with an
explanatory comment:

```csharp
Assert.Equal(companyState == "rejected" ? ServiceErrorType.NotFound : ServiceErrorType.Conflict, apply.ErrorType);
```

Both parameter cases, setup, calls, browse visibility assertion, detail
`NotFound` assertion, and all other assertions in the file remain intact.
The deactivated apply case still requires `Conflict`. No production code,
other tests, application behavior, or dependencies changed. No tests were
skipped, deleted, or disabled.

## Verification

- [x] Reproduced before editing: the two-case theory had **1 passed, 1 failed,
  0 skipped**; only `rejected` failed.
- [x] Focused verification after editing: that theory plus the separate
  Pending/Rejected apply theory had **4 passed, 0 failed, 0 skipped**.
- [x] Full repository-root suite, Release to avoid the running app's Debug files:

```text
dotnet test -c Release --logger "trx;LogFileName=visibility-full.trx" --results-directory TestResults/visibility-fix-full
```

**920 passed, 0 failed, 0 skipped; total 920. Exit code 0.**
Terminal results and `TestResults/visibility-fix-full/visibility-full.trx`
Counters agree. Existing unrelated NuGet/compiler warnings remain unchanged.
No git commands, app startup, real database changes, or external AI calls.

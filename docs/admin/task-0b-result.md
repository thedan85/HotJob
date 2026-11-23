# Admin audit log — Phase 0b result

## Status: not ready to ship

The changes are incomplete: four existing concurrency tests fail, and rollback
behavior has not been established for every operation or for an existing outer
transaction. In particular, when a caller already owns a transaction, the
current implementation reuses it but does not roll it back on audit failure;
the caller could still commit the business change. EF-tracked changes after
failure also require care before reusing the context.

The affected methods now start a transaction when none exists, commit after
the audit write, and capture job and company names before deletion. Missing
admin ID or email throws. Detail is trimmed and limited to 500 characters; the
caller policy prohibits supplying credentials, tokens, connection strings,
or complete request bodies.

## Files changed

- `hotjob/Services/Admin/AdminService.cs`
- `hotjob/Services/Admin/AdminAuditService.cs`
- `hotjob/Services/Admin/IAdminAuditService.cs`
- `hotjob/Tests/hotjob.Tests/AdminTests/AdminAuditHooksTests.cs`
- `hotjob/Tests/hotjob.Tests/AdminTests/AdminAuditServiceTests.cs`
- `hotjob/Tests/hotjob.Tests/AdminTests/AdminSkillServiceTests.cs`
- `hotjob/Tests/hotjob.Tests/AdminTests/SkillApiValidationTests.cs`
- Added `docs/admin/task-0b-result.md`.

Migration: none; no schema changes.

## Tests

From `hotjob/`:

- `dotnet test ..\hotjob.slnx --nologo -v q --filter "FullyQualifiedName~LogAsync_AnonymousAdmin_ThrowsAndWritesNothing|FullyQualifiedName~ToggleUserActive_When"`:
  3 passed, 0 failed (before the subsequent test-fixture changes).
- `dotnet test ..\hotjob.slnx --nologo -v q`: 456 passed, 4 failed,
  0 skipped, 460 total. The failing cases are the two
  `SkillWrite_RacingDuplicate_ReturnsConflict` and two
  `DeleteSkillAsync_ReferenceInsertedAfterCounts_ReturnsConflict` cases.
  Their injected writes use the same SQLite connection and now participate in
  the transaction, so the rollback also removes those simulated concurrent
  writes. They need a faithful independent-transaction test setup, not weaker
  assertions.

## Remaining risks

- Free-form detail is intentionally not secret-scanned; administrators can
  accidentally enter sensitive information in a reason or name. The policy
  relies on callers and users excluding such information.
- The complete rollback matrix, outer-transaction behavior, and context reuse
  after rollback require fixes and tests before release.
- Restore reports existing vulnerability warnings for MySqlConnector 2.4.0
  and SQLitePCLRaw.lib.e_sqlite3 2.1.10.
- MySQL is configured without `EnableRetryOnFailure`; manual transactions
  would need an execution-strategy wrapper if retrying is enabled later.

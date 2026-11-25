# Admin audit log — Phase 0c result

## Status

The requested transaction and test changes are implemented and the full test
suite passes. **Not yet confirmed ready for production**: rollback testing
uses SQLite, not the production MySQL provider, and free-form audit details
can still contain secrets entered by an administrator. Production-provider
verification and acceptance of that data-handling risk are needed.

## Files changed

- `hotjob/Services/Admin/AdminService.cs`: reject existing transactions at
  entry; roll back failed owned transactions and clear the change tracker.
- `hotjob/Tests/hotjob.Tests/AdminTests/AdminSkillServiceTests.cs`: repair
  the four race simulations and assert that no audit row is left behind.
- `hotjob/Tests/hotjob.Tests/AdminTests/AdminAuditHooksTests.cs`: test
  transaction ownership and database-backed failure, rollback, same-context
  retry, and successful audit writes for all eight requested cases.
- Added `docs/admin/task-0c-result.md`.

No migration was needed.

## Previously failing tests

The original competing data and all original conflict and data assertions
remain intact. A transaction-start interceptor now writes the competing data
after the service's preliminary checks but before its transaction begins.
This makes the competing write committed independently instead of being
rolled back with the service transaction.

- Duplicate create and duplicate update: insert the competing `Rust` skill
  before the transaction and retain the original duplicate and unchanged
  target assertions.
- Resume-reference delete and job-reference delete: insert the appropriate
  competing reference before the transaction and retain the original skill
  and reference assertions.

Each case now additionally checks that no audit row exists.

## Verification

Commands run from `hotjob/`:

- `dotnet test ..\hotjob.slnx --nologo -v q --filter "FullyQualifiedName~AdminTests"`:
  72 passed, 0 failed, 0 skipped (before the final test additions).
- `dotnet test ..\hotjob.slnx --nologo -v q`: **474 passed, 0 failed,
  0 skipped**.

The transaction/caller search found no existing admin write callers or tests
that explicitly wrap the calls in a transaction. Existing transaction usages
found elsewhere belong to unrelated services.

## Remaining risks

- Audit reasons and names are free text and are not secret-scanned by design.
- Clearing the change tracker after rollback detaches all tracked entities,
  including any unrelated unsaved changes on the same context.
- MySQL retry strategies are not enabled in the current configuration; if
  enabled later, the transaction operations need execution-strategy handling.
- Restore reports existing vulnerability warnings for MySqlConnector 2.4.0
  and SQLitePCLRaw.lib.e_sqlite3 2.1.10.

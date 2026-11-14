# Debugging Guide

Investigate first. Edit only after the root cause is understood and explained.

## 1. Understand
- Capture the exact error message, stack trace, HTTP status, log lines, and repro steps.
- Identify the module and the first frame in the user's own code (skip framework frames).
- State expected vs actual behavior. If either is unknown, ask.

## 2. Trace the flow
Backend: Request → Middleware → Routing/Filters → Controller → Service → Repository → DbContext/SQL → Response.
Full-stack: Frontend → API request (URL, method, headers, body) → Backend → DB → API response → Frontend parsing/rendering.
At each hop ask: what goes in, what comes out, where does it diverge from expected?

## 3. Investigate (cheapest evidence first)
- Build output / compiler errors: read the first error, not the cascade.
- Source: Grep for the throwing method, the endpoint route, the entity.
- Config: appsettings (+ environment variants), connection strings, env vars, launchSettings, CORS, auth schemes.
- Dependencies: package versions, target framework, version conflicts, missing restore.
- Database: schema vs model (pending/missing migrations), FK/nullability, seed data, constraint violations.
- Logs and `git log`/`git diff` for recent changes near the failure.
- Reproduce with the smallest case (a test, a curl call). Add temporary logging only if needed and remove it afterward.

Common .NET causes: NullReferenceException on unloaded navigation property; DI lifetime/registration missing (`Unable to resolve service`); migration/model mismatch (`Invalid column name`); model-binding or validation failure (400); antiforgery/CORS/401/403 config; circular JSON serialization; async deadlock (`.Result`); DbContext used after disposal or across threads; build artifacts/locked files (bin/obj conflicts).

## 4. Fix strategy: say this before editing
1. What caused the error?
2. Which files are affected?
3. Why did it occur (the underlying reason, not just the line)?
4. What is the safest fix (smallest change addressing the root cause)?
5. Could it break another feature? (check callers via Grep)
Form hypotheses, test the likeliest, and say when something is still a hypothesis. If several fixes exist, recommend one. If the fix needs a business decision, ask.

## 5. Implement
Minimal change only. No drive-by refactors. Match existing style. Don't suppress exceptions or loosen security to make an error disappear.

## 6. Verify (required)
- Build the affected project/solution; report warnings/errors.
- Run relevant tests; add a regression test if a test project exists and the bug is testable.
- Re-run the original repro and confirm the error is gone.
- Check neighboring features for regressions.
- If something cannot be verified (no DB, needs UI, no tests), say exactly what was not verified.

## Output
Problem Summary → Root Cause → Affected Files → Proposed Fix → Changes Made → Build/Test Results → Remaining Risks.

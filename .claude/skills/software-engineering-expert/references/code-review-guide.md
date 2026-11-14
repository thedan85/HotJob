# Code Review Guide

Read-only. Do not edit, create implementation files, or run migrations.

## Process
1. Build a project map (projects, layers, DbContext, Program.cs/Startup, DI registrations, auth config).
2. Scope: whole project, a module, or a diff (`git diff`, `git log`). Review what was asked.
3. Per area below, Grep for risk patterns, then read only the hits and their callers.
4. Verify each suspected issue by reading the surrounding code (callers, guards, validation, filters) before reporting.
5. Write the report.

## Architecture
- Layer boundaries respected (controllers don't touch DbContext if a service/repository layer exists; domain doesn't reference infrastructure).
- Circular or inverted dependencies between projects/namespaces; unnecessary coupling.
- DI: lifetimes correct (no scoped inside singleton), no `new` of services, no service locator, constructor not over-injected (>5-6 suggests SRP violation).
- SOLID/DRY: god classes, fat controllers, duplicated logic, leaky abstractions.
- Consistent patterns (error handling, DTO/ViewModel use, naming).

## Code quality
- Duplicated or dead code (unused methods, unreachable branches, unused usings only as INFO).
- Logic errors: off-by-one, wrong comparison, inverted condition, swallowed exceptions, wrong status codes.
- Null handling: nullable reference warnings, `.First()` vs `FirstOrDefault`, unchecked `Find`/`FirstOrDefault` results.
- Async: `.Result`/`.Wait()`, `async void`, missing `await`, missing CancellationToken in long calls, fire-and-forget.
- Resources: undisposed `IDisposable`/streams, `HttpClient` per request, unbounded collections.
- Over-complexity: deep nesting, long methods, magic numbers/strings.

## Security
Use `security-checklist.md` (auth, authz, injection, XSS/CSRF, IDOR, validation, data exposure, uploads, RBAC).

## Database
- Entities/relationships: missing FKs, cascade delete risks, nullable mismatch, missing unique constraints.
- N+1 (loops that query, lazy loading, navigation access in loops), missing `Include`/projection.
- Loading whole tables then filtering in memory (`ToList()` before `Where`), `Count()>0` vs `Any()`.
- Missing `AsNoTracking` on read-only queries, missing pagination.
- Transactions around multi-step writes; concurrency tokens where needed.
- Indexes on filtered/joined/sorted columns; migrations consistent with model snapshot.

## Severity scale
- **CRITICAL**: exploitable security hole, data loss/corruption, or crash on a main path.
- **HIGH**: likely bug or vulnerability with significant impact; auth gaps; serious perf failure.
- **MEDIUM**: bug in edge cases, maintainability issues likely to cause defects, notable inefficiency.
- **LOW**: minor quality, naming, small inefficiency.
- **INFO**: observation or suggestion, no defect.

## Confidence labels
- **Confirmed**: the code demonstrably does the wrong thing; cite lines.
- **Potential risk**: depends on config, data, or code not inspected; say what would confirm it.
Do not inflate severity of potential risks. Do not report style nitpicks as problems.

## Finding template
```
[SEVERITY][Confirmed|Potential] Short title
Location: path/File.cs, ClassName.MethodName, line N
Problem: what the code does
Why it is a problem: reasoning
Impact: what can go wrong
Recommendation: concrete fix (short snippet only if useful)
```

## Report structure
1. Executive Summary (scope reviewed, what was not reviewed, counts per severity, overall verdict)
2. Critical Findings
3. Security Findings
4. Architecture Findings
5. Code Quality Findings
6. Recommended Improvements (prioritized)
Merge duplicates (one finding with multiple locations). Include positives briefly if relevant.

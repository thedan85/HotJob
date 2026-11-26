# Testing & Verification Guide

## Discover first
- Find test projects (`*Tests*.csproj`, xUnit/NUnit/MSTest references) and frameworks; follow their conventions.
- If no tests exist, say so; verify via build plus a manual/scripted repro and state the limits of that verification.

## Commands (.NET)
```
dotnet build <solution-or-project>              # compile; read the FIRST error
dotnet test <project> --no-build                # after a successful build
dotnet test --filter "FullyQualifiedName~Name"  # targeted run
dotnet list package --vulnerable                # dependency vulnerabilities
dotnet ef migrations list                       # inspect only; don't update a shared DB unprompted
```
Run the narrowest build/test that covers the change first, then the broader set. Other stacks: use the project's own build/test commands (package.json scripts, Makefile, etc.).

## What to test
- New/changed business logic: happy path, edge cases (null, empty, boundaries), failure paths.
- Authorization: allowed role, forbidden role, anonymous, other user's resource (IDOR).
- Validation: invalid and missing input produce the right status/message.
- Data access: queries return the right data; use in-memory SQLite or a test database rather than EF InMemory when relational behavior matters.
- Regression: a failing test that reproduces the bug, then passes after the fix.

## Writing tests
- Arrange-Act-Assert; one behavior per test; names like `Method_Scenario_ExpectedResult`.
- Unit-test services with mocked dependencies; integration-test endpoints with `WebApplicationFactory`.
- Deterministic: no real time, network, or shared mutable state; isolate DB per test.
- Don't add test packages without need; reuse what the solution already references.

## Reporting results
- State commands run, pass/fail counts, and any failing test names with the message.
- Distinguish pre-existing failures from ones you introduced (compare with `git stash` baseline only if safe, or note uncertainty).
- Say explicitly what was not verified (UI behavior, external services, production data).
- Never claim success from a build alone when behavior was changed and tests exist.

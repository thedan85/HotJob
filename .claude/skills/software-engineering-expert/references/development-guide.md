# Development Guide

## Before development
1. Inspect architecture: solution/projects, layers, DI registration, naming, error-handling and validation patterns.
2. Read the relevant entities, DTOs/ViewModels, services, repositories, controllers, and existing tests.
3. Understand existing business rules; don't infer new ones. Ask when requirements are unclear or business logic is ambiguous.
4. Identify reusable components (base classes, helpers, extensions, existing endpoints).
5. List affected files and whether a DB change is required.
6. Present the plan: requirements as understood, files to create/modify, DB changes, risks, open questions. Wait for approval on non-trivial or ambiguous work.

## During development
- Preserve architecture and conventions; reuse before creating.
- Implement in small increments; keep the build green between steps.
- Follow SOLID; use constructor DI; right lifetimes.
- Validate input (DTO validation + business rules) at the boundary; enforce authorization on the server (policies/roles/resource ownership), never rely on UI hiding.
- Consistent exception handling and response shapes (follow what the project already does).
- async/await end to end; pass CancellationToken where the project does.
- Maintain backward compatibility (public API contracts, routes, DB data).
- Protect sensitive data (no secrets in code/logs, no over-posting, no entity exposure in API responses).
- No new NuGet packages unless clearly justified; mention any you add.
- No unrelated refactoring.

## Database changes
- Inspect existing entities, DbContext configuration, migrations, model snapshot, and seed data first.
- Check for existing columns/relationships to avoid duplicates; preserve existing data (defaults for new non-null columns, data-safe renames).
- Create a migration only when the model changes; review the generated migration (no unintended drops).
- Do not apply migrations to a shared/production database or run destructive SQL without explicit confirmation.

## After development
- Build the solution; fix warnings you introduced.
- Run relevant tests; add tests for new logic (see `testing-guide.md`).
- Review the diff (`git diff`) for leftovers, debug code, accidental changes.
- Check integration with neighboring modules and callers.
- Report what was completed, what was not, and known limitations.

## Output
Requirements → Existing Architecture Analysis → Implementation Plan → Files Created → Files Modified → Implementation Summary → Test Results → Remaining Tasks.

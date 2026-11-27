# CV Parsing Workflow Checklist

This is workflow tracking, not an approved specification or implementation plan.
No git commands, real DeepSeek requests, application startup, or secret-value reads are permitted.

## Architectural design

- [x] Classify the task as a new parsing subsystem.
- [x] Explore storage validation, resume DTOs/entities, save validation, upload flow, configuration loading, DI, controller structure, and SQLite test fixtures.
- [x] Confirm that `.gitignore` contains `.env.local`; do not read that file.
- [x] Resolve the education degree mapping: merge degree and field of study into Major; retain separate parse-only metadata.
- [x] Present the recommended architecture; document alternatives in the written specification.
- [x] Write the proposed specification to `docs/superpowers/specs/2026-11-26-cv-parsing-design.md` as explicitly requested, without committing.
- [x] Self-review the written specification for placeholders, consistency, scope, and ambiguity.
- [x] User reviews design sections in the written specification, including safety limits and cancellation behavior.
- [x] Obtain user approval of the written specification.
- [x] Write and self-review the implementation plan using writing-plans: `docs/superpowers/plans/2026-11-26-cv-parsing.md`.
- [x] Obtain user review of the plan and an execution-method selection: Native.

## Implementation and verification

- [x] Implement core backend with tests first; review fixes verified. Three minor deviations remain explicitly documented in the result report.
- [x] Verify exact package versions, release age, and vulnerability advisories before adding dependencies.
- [x] Run the full repository-root `dotnet test` suite and record exact counts: final 919 passed / 1 failed / 0 skipped (920 total); same failure as baseline 806/1/0.
- [x] Write `docs/admin/task-10-result.md` with implementation, privacy, integration, and final verification details.
- [x] Complete fresh review and verify Important fixes; reconcile final report/counts. Full suite is not green and three Minor findings remain.

## Resolved planning clarification

- [x] Resolve the NuGet package identity with the user: the official PdfPig
  project publishes package `PdfPig` (namespace `UglyToad.PdfPig`); the separate
  package named `UglyToad.PdfPig` currently lists old custom prereleases.
  NuGet lists official `PdfPig` 0.1.16 (2026-08-22) and OpenXML 3.5.1
  (2026-03-18). Both packages are now installed; audited restore and native
  vulnerability listing found no advisories in the added dependency graph.
  The user approved official `PdfPig` 0.1.16; the specification now records this
  clarification and the planned OpenXML 3.5.1 version.

# SDD ledger — plan: docs/superpowers/plans/2026-11-26-cv-parsing.md

Execution: Native, approved by user. Spec and plan read. No git commands.
Ruling: Work directly in the supplied workspace and retain this ledger; do not run git-based workspace/commit scripts — explicit user prohibition — cost: no git isolation/history supplied by the agent.
Baseline: dotnet test hotjob.slnx -c Release, TRX TestResults/task-10-baseline: 806 passed, 1 failed, 0 skipped (807 total). Existing JobVisibilityTests rejected-company case expects Conflict, receives NotFound. No product edits existed at baseline.
Ruling: Preserve unrelated visibility behavior and existing dependency versions — scope explicitly excludes those systems — cost: final suite may retain the pre-existing failure and advisories.
Threat boundaries: uploaded bytes/filename (size/signature/format budgets); caller identity (principal + stored role/profile); external AI (redaction, quota, strict JSON, catalog validation); logs/persistence (no document logging or writes).
Pre-flight shared interfaces: 1→2/3 validated owned bytes, consistent; 2→3 runner/text record, consistent; 4→5/8/9 DTO/catalog normalization, consistent; 6/7→8 privacy/quotas, consistent; 8→9 draft results normalized after catalog query, consistent; 9→10 public service and scoped EF, consistent; all→11 fake-only integration and report, consistent.
Task 1: shared validation RED missing type → GREEN 22/22 validation/storage tests. Remaining detailed additions continue in integration; do not claim all plan cases yet.
Dependencies: official NuGet registration metadata checked 2026-11-26: PdfPig 0.1.16 (2026-08-22), OpenXML/Framework 3.5.1 (2026-03-18), System.IO.Packaging 10.0.2 (2026-01-13), no listed vulnerabilities. Restore audit follows.
Tasks 2–11 pending. Task 2 in progress.
Task 2 initial RED missing extraction namespace → GREEN 3/3 PDF/runner tests; additional protection/image/cap coverage pending Task 11.
Task 3 in progress: archive tests first.
Task 3 initial RED missing dispatcher → GREEN combined extraction 13/13.
Task 4 initial RED missing DTO/matcher/normalizer → GREEN 5/5.
Task 5 in progress: bilingual/date tests first.
Task 5 initial RED missing rules/dates → GREEN 9/9.
Task 6/7 initial RED missing privacy/quota types; long email-adjacent run exposed quadratic backtracking. Replaced contact regex engine with NonBacktracking; GREEN 5/5 privacy/quota.
Ruling: Pure helper tests grouped by feature in fewer classes than the plan lists — same behavior boundaries, less fixture duplication — cost: class names differ from planned filters; actual filter/counts recorded.
Task 8 in progress: fake-only transport tests first.
Task 8 initial RED missing client → GREEN 15/15 HTTP/schema/timeout cases.
Task 9 initial RED missing coordinator → initial failures traced to test HttpContextAccessor AsyncLocal set inside async factory, not authorization. Circuit state is now explicit fixture default; HTTP tests set their principal at call site. GREEN 11/11 service/auth/fallback/no-row-change tests.
Task 10 in progress: registration tests first.
Task 10 RED missing extension → GREEN entire feature 70/70.
Task 11 edge regressions RED invalid ISO dates threw, degree-only normalization invented field metadata, unknown truncation warning ordering. Minimal fixes → GREEN 13/13 edge tests.
Task 11 generated image/password PDFs validated independently with PdfPig; archive expansion/cap/external-link regression tests GREEN 5/5.
Task 11 no-write interceptor/tripwire storage and actual DI HTTP logging privacy/injection/cancellation regressions GREEN 6/6 (existing behaviors, no fictitious red phase).
Ruling: Password fixture is generated with BCL revision-2 PDF encoding because PdfPig has no encryption writer; normal and image fixtures use PdfDocumentBuilder — no additional package/binary — cost: test-only legacy crypto helper maintenance.
Full suite first implementation run: 900 passed, 1 failed, 0 skipped, total 901; same rejected-company visibility failure as baseline. Advisory check: only existing MySqlConnector moderate in app; existing SQLite high in test project.
Final review pending. Report/checklists pending. No task checkbox claims broader test coverage than actually implemented.
Final review received: six Important findings, five Minor, no Critical. One fix pass started.
Final: Ruling: Regrade invalid supplied AI IDs and lost summary truncation warning as Important, plus incidental-year range association — untrusted output consistency and silent information loss affect ordinary review — cost if wrong: conservative omissions/warnings rather than malformed suggestions.
Final fix pass RED: 13 failing regression cases reproduce phone/date privacy, Unicode boundary, unsupported digits, multi-organization blocks, invalid IDs, summary warnings and incidental years. Two more RED cases prove cancellation-ignoring header/body reads block caller waiting.
Final fixes: date protection precedes numeric redaction; surrogate-safe extraction; ASCII/nonthrowing dates with explicit-range priority; separate credible organizations; preserve summary warnings and invalid-ID presence; independent WaitAsync bounds read waits while holding late destinations. Guard tests now directly isolate preflight and include a safe control, missing document actually omitted.
Focused feature suite after fixes: 111 passed, 0 failed, 0 skipped. Exact A/B principal and full JSON schema guard coverage added after this run.
Final: minor (deferred): mixed known/unknown skill fragments can lose diacritics, slash fragments stay combined, and early matcher caps can omit shortening warnings.
Final: minor (deferred): empty+unsupported input has different error precedence from upload (empty vs unsupported); individual rules remain enforced.
Final: minor (deferred): body traversal may include Word text-box text; no external/embedded execution or fetch.
Final: Ruling: Known visibility failure/dependency advisories remain outside scope — confirmed before edits — cost if wrong: suite/dependency remediation still required separately.
Final: Ruling: Report/checklist absence was temporary and is now resolved — report exists and is being reconciled — cost if wrong: inaccurate documentation, checked against final output.
Final: Ruling: No hard document-worker kill/memory sandbox — approved in-process architecture — cost if wrong: hostile synchronous library work may occupy two workers or internal memory.
Final: Ruling: Restart-reset/single-process quotas retained — explicitly accepted — cost if wrong: multi-replica/restart deployments can exceed intended total allowance.
Final: Ruling: DOC/OCR/perfect-layout support remains excluded — approved scope with manual-entry fallback — cost if wrong: unsupported or complex documents need manual work.
Final: Ruling: Redaction is not complete anonymization — documented optional external sharing — cost if wrong: names and undetected obfuscated contact data may remain.
Final: Ruling: Do not merge local collections automatically into a successful partial AI response — approved parser contract — cost if wrong: AI suggestions can omit information rules had found.
Final: Ruling: Real-provider accuracy/model compatibility, UI consent/rendering and deployment compliance remain unverified here — fake-only backend task — cost if wrong: deployment/UI follow-up must verify those boundaries.
Final: Ruling: No historical git/provenance verification — user prohibits git — cost if wrong: unrelated/historical tracking changes cannot be certified.
Final verification: dotnet test hotjob.slnx -c Release --logger "trx;LogFileName=task-10-full.trx" --results-directory TestResults/task-10 → 919 passed, 1 failed, 0 skipped, total 920. TRX Counters independently read. All 113 parsing cases passed; sole failure identical to baseline, no tests skipped/suppressed/deleted.
Final native advisory check: only pre-existing MySqlConnector 2.4.0 moderate in app. Added PdfPig/OpenXML graph has no listed advisory. Existing test SQLite advisory persists.
Final fixes verified: phone/date, surrogate truncation, ASCII/nonthrowing dates, read-wait bounds, bilingual organization separation, guard-test isolation, explicit invalid AI IDs, retained summary warning, incidental-year association — RED→GREEN and final full-suite no new failures. No second reviewer.
Tasks 1–10 core deliverables verified; Task 11 report/evidence complete. Original illustrative-step checkboxes stay conservative; three Minor deviations are explicitly deferred, not claimed complete.
Finishing-a-development-branch gate: stopped at the known failed full-suite result; no git/integration action, no workspace deletion. User commits manually. Ledger retained as authorized.

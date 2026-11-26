# Task 4: Remove only a resume attachment

Date: 2026-11-26

## Summary and root cause

Candidates can now edit a resume at `/profile#cv-management` and remove its uploaded attachment without deleting the resume. Removal requires confirmation, shows a disabled/loading state, displays service errors, and updates both the editor and resume card. Unsaved content edits are preserved.

Previously, `IResumeService` offered upload/replacement and whole-resume deletion, but no attachment-only operation. Whole-resume deletion correctly blocked resumes used in applications. Upload replacement protected old files through `Application.SnapshotFileUrl` only; an old file referenced solely by `SnapshotJson` could be deleted.

`Resume` has only one attachment field, `FileUrl`. It has no stored original filename, content type, or size fields. No schema changes are needed.

## Exact files changed by this task

Paths are relative to the repository root; this lists this task's edits, not a git-derived working-tree inventory.

1. `hotjob/Services/Candidate/IResumeService.cs` — declare attachment removal.
2. `hotjob/Services/Candidate/ResumeService.cs` — implement owner-scoped removal and share conservative file cleanup with upload replacement and whole-resume deletion.
3. `hotjob/Services/Candidate/CvFileStorage.cs` — make deletion-failure warnings omit file URLs, filesystem paths, and exception details.
4. `hotjob/Components/Pages/Profile.razor` — add the current attachment, removal confirmation/loading/error/success states in the editor; gate related resume operations.
5. `hotjob/Tests/hotjob.Tests/CandidateTests/ResumeServiceTests.cs` — make the existing test class partial to reuse its helpers.
6. `hotjob/Tests/hotjob.Tests/CandidateTests/ResumeServiceFileTests.cs` — attachment lifecycle, ownership, validation, snapshot retention, replacement and cleanup-failure tests.
7. `hotjob/Tests/hotjob.Tests/ProfileTests/ProfilePremiumBadgeTests.cs` — make the existing test class partial to reuse its static-rendering support.
8. `hotjob/Tests/hotjob.Tests/ProfileTests/ProfileResumeFileTests.cs` — real Profile/service rendering and event-handler tests without adding a UI test package.
9. `docs/admin/task-4-result.md` — this report and work checklist.

## Design decisions

### Service contract and ownership

`RemoveResumeFileAsync(string? userId, int resumeId, CancellationToken cancellationToken = default)` returns `ServiceResult<ResumeDetailDto>`.

- Resolve the candidate through the existing service lookup, then filter the resume by both ID and candidate ID.
- Anonymous callers receive Unauthorized; missing or another candidate's resumes receive NotFound.
- With no attachment, return a successful current DTO without an error, even for a legacy resume without skills. Repeated calls are harmless.
- With an attachment, validate saved skills, set `FileUrl` to null, and save before attempting disk cleanup.
- The resume row, ownership, title, summary, primary flag, creation date, education, work experience, skills, and application snapshots are not changed.

### Shared-file rule

Cleanup is private service logic, shared by removal, replacement, and successful whole-resume deletion:

1. Ignore an absent/blank old URL.
2. Keep the file if any remaining resume still uses the URL.
3. Keep the file if any application has that `SnapshotFileUrl`.
4. Read non-null `SnapshotJson` values across **all** applications, not just applications using the edited resume.
5. Parse JSON with `System.Text.Json`; examine root `FileUrl` properties case-insensitively. Decoding handles escaped slashes/Unicode, and URL comparison conservatively ignores case.
6. Keep the file when JSON references the URL, is malformed/blank, has a non-object root, lacks a recognized `FileUrl`, or has a non-string/non-null `FileUrl`. Duplicate recognized properties are all examined.
7. Only after all checks establish no reference, call the existing storage deletion method, which confines recognized local URLs to its uploads root.

Null `SnapshotJson` is treated as legacy data, whose file reference is in `SnapshotFileUrl`. Known JSON with null or a different `FileUrl` does not itself prevent cleanup.

This is intentionally a simple application-side JSON scan, not database-specific JSON SQL or a migration. Uncertain snapshots may retain unrelated old files; retaining extra files is preferable to breaking an employer's access.

### Cleanup failure after a successful database update

Reference queries and physical cleanup are best-effort after the saved change. If they throw, including cancellation during cleanup, the helper logs a warning containing only the resume ID and a fixed message and returns normally. It does not roll back the saved change or convert removal/replacement into a failure.

The real storage implementation already catches disk I/O/access failures. Its warning is now generic, without the URL or raw exception. Tests also inject a storage boundary failure and verify successful persisted removal, a retained file, and a warning without sensitive details.

### Empty-resume rule

Existing create/update validation requires **at least one skill**, including the “Upload CV” flow. Removal follows the approved same rule: removing an existing attachment requires at least one **saved** `ResumeSkill`.

Error: `Add and save at least one skill before removing the attached file.`

This blocks file-only legacy resumes from becoming empty. Education, experience, or a summary alone do not waive the saved-skill requirement. Unsaved editor skills do not count: save them first. The requirement is not applied to an already-fileless, no-op removal.

### UI and scope

- In CV management, select **Edit**. The editor shows **Current attachment**, **View CV**, and **Remove file** when an attachment exists.
- Removal uses the existing `profile-btn`, `cv-actions`, `cv-confirm`, and danger classes, with an inline, labelled confirmation group rather than a new modal/JavaScript dependency.
- Confirmation explains that the resume stays and past applications retain their original file.
- While removing, controls show `Removing...`, disable confirmation/cancel and conflicting edit/upload/delete/save actions, and expose `aria-busy`.
- The new service call uses `CircuitDbGate`. Related edit/save/upload/primary/delete/list calls in this flow also use the gate.
- Success updates attachment state from the returned DTO without replacing the unsaved editor form. Service errors appear in the existing alert.
- The optional API was skipped: there is no existing resume controller pattern to extend trivially.
- No apply, snapshot creation/mapping, ATS, billing, or authentication logic was changed.
- No git commands or commits were run. No packages or migrations were added; the app was not started to run its existing startup migrations/seeding.

## Existing upload, replacement and deletion findings

- `CvFileStorage` accepts PDF/DOC/DOCX with signature checks and a 5 MB maximum. It writes generated names under the configured CV root, with public URLs `/uploads/cv/{candidateId}/{guid}.{extension}`.
- Upload saves the new physical file, updates the resume, then retires the old file. If the database save fails, it attempts to delete the newly uploaded file.
- Replacement previously checked only `SnapshotFileUrl`. Five regression cases (JSON-only reference, escaped JSON, camelCase, malformed JSON, and unknown JSON shape) all failed before the cleanup fix because the original file was deleted.
- Replacement now uses the shared guard, retaining referenced/uncertain files and deleting known-unreferenced old files. Failure to clean up an old file does not invalidate a saved replacement.
- Whole-resume deletion still refuses a resume used to apply. Its successful post-save disk cleanup now also uses the shared guard, protecting files referenced by another resume/application. A regression test covers another application's JSON reference.
- Existing storage deletion ignores missing files and unknown/foreign URLs and catches expected I/O/access failures.

## Automated verification

Executed from `C:\Users\phulo\Desktop\HotJob`, using `hotjob.slnx`, with no test filter:

```powershell
dotnet test --configuration Release --verbosity minimal --logger "trx;LogFileName=task-4-full.trx" --results-directory "C:\Users\phulo\AppData\Local\Temp\opencode\hotjob-task4-results"
```

Final output:

```text
Passed!  - Failed:     0, Passed:   593, Skipped:     0, Total:   593, Duration: 54 s - hotjob.Tests.dll (net10.0)
```

Exit code: **0**. Both application and test projects built in Release.

TRX: `C:\Users\phulo\AppData\Local\Temp\opencode\hotjob-task4-results\task-4-full.trx`.

Coverage includes:

- Owner removal and persisted survival of the resume and all its content.
- Non-owner denial with unchanged URL and disk file.
- Anonymous and missing-resume cases.
- No-file/repeated removal, including legacy records without skills.
- Applied-resume removal while whole-resume deletion remains blocked.
- Retention for `SnapshotFileUrl`, JSON-only/escaped/camelCase references, and references on another application's resume.
- Retention on malformed, blank, unfamiliar, missing-property, and wrong-property-type JSON.
- Deletion of unreferenced files, including when a valid snapshot references a different URL.
- Retention when another resume uses the file.
- Cleanup failure/cancellation after save still returning success and safe warnings.
- Replacement retention, unreferenced replacement cleanup, and replacement cleanup failure.
- UI visibility, confirmation/cancel, loading/disabled state while waiting for the circuit gate, service validation messages, view updates, and preservation of unsaved edits.

The UI tests use the existing static renderer with the real component and service and invoke handlers through `EventCallback`. They are not real browser click or CSS/layout tests.

A deliberate review mutation inverted the JSON-retention condition: **9 of 10** snapshot-retention cases failed. The correct condition was restored before the final full test run.

### Warnings, not test failures

The build/restore reports existing dependency advisories:

- `MySqlConnector` 2.4.0: NU1902, moderate — https://github.com/advisories/GHSA-473q-m89c-ghf8
- `SQLitePCLRaw.lib.e_sqlite3` 2.1.10: NU1903, high — https://github.com/advisories/GHSA-2m69-gcr7-jv3q

Existing compiler warnings concern a duplicate using in `JobDetail.razor`, nullable arguments in `RecommendedJobs.razor`/`PublicJobBrowseFilterTests.cs`, and unused parameters/nullability in `AdminService.cs`. No package upgrades, warning suppressions, or unrelated fixes were made.

## Manual browser test steps — not executed

A connected browser was available, but navigating to the configured `http://localhost:5168/profile` failed with `net::ERR_CONNECTION_REFUSED`. No running app was available there. Starting the app would execute `Database.MigrateAsync` and seeding in `Program.cs`; it was not started automatically. Therefore browser interaction, screenshots, keyboard checks, responsive layout, and employer HTTP download verification remain unverified.

Use a disposable local test database and non-sensitive CVs. Start the app through your normal approved workflow.

### A. Resume never used in an application

1. Sign in as a candidate; open `/profile#cv-management`.
2. Create/upload a PDF, DOC, or DOCX CV with at least one saved skill. Do not apply with this resume.
3. Record the **View CV** URL and confirm the file opens. Optionally locate that generated file under the configured CV storage root.
4. Select **Edit**. Make a harmless unsaved title change.
5. Select **Remove file**, then **Cancel**. Verify the file still opens and nothing was removed.
6. Select **Remove file** again, then **Confirm remove file**. Verify the loading/disabled state where observable.
7. Verify `Attached file removed.`, `No file attached`, no attachment link/removal button, the resume card remains, and the unsaved title change is still in the editor.
8. Reload the page: the original saved title/content/skills/primary status remain, and no file is attached.
9. With no shared references or uncertain snapshots elsewhere, verify the old physical file is gone and its URL no longer serves the file. If uncertain snapshots exist, retention is intentional.

### B. Resume previously used in an application

1. Create/upload a different CV with at least one saved skill.
2. Apply to a disposable test job with that resume, using the unchanged existing application flow.
3. In an employer session for that job, open the submitted application and its CV attachment. Record the original URL and verify its content.
4. Return to the candidate session; edit that resume and confirm **Remove file**.
5. Verify the candidate's resume still exists, its attachment is gone, and all saved content/skills remain.
6. Refresh the employer's application details and open/download the old attachment again. It must still serve the original file and original content.
7. Verify whole-resume deletion is still refused because the resume was used to apply.

### C. Replacement and empty-resume checks

1. On another applied resume, upload a replacement file through its resume card. Verify the candidate sees the new file while the employer can still open the original application's file.
2. Replace a never-applied, unshared attachment. Verify the new file opens and the unreferenced old file is removed, subject to the conservative uncertain-snapshot rule.
3. With a disposable legacy attachment-only resume lacking saved skills, attempt removal. Verify the exact saved-skill validation message and that the file is still attached/on disk. Do not weaken create validation just to manufacture this legacy record.
4. Add and save a skill, then retry removal; it should succeed.
5. Use keyboard navigation for Edit → Remove file → Cancel/Confirm, and check narrow/mobile and desktop widths, error announcements and the confirmation's accessible name.

## Open issues and assumptions

- The scan is deliberately conservative and linear in non-null application snapshots. Appropriate for this academic project; no reference index, garbage collector, or background cleanup was added.
- One malformed/unfamiliar snapshot can retain otherwise unreferenced files globally. Cleanup failures can also leave orphan files. No automatic retry is implemented.
- The check observes persisted references at cleanup time. It does not introduce distributed locking or make application submission and filesystem cleanup atomic across independent requests/processes. A concurrently in-flight submission using an already-loaded old URL is not covered by these tests; application submission logic was intentionally left unchanged.
- Generated local CV URLs and the existing root `ResumeSnapshot.FileUrl` format are assumed. Arbitrary historical URL aliases/absolute URLs are not canonicalized; unknown JSON structures retain files.
- Null JSON relies on the existing legacy `SnapshotFileUrl` field. No snapshot data is repaired or rewritten.
- The full suite uses the repository's SQLite test fixtures; a live MySQL/browser/employer-download exercise remains manual.
- Dependency advisories above remain open and outside this task's no-package-change scope.

## Completion checklist

- [x] Inspect existing service, storage, snapshots, validation and candidate UI.
- [x] Obtain approval for the bounded design and saved-skill requirement.
- [x] Implement and test conservative shared-file cleanup and upload replacement.
- [x] Implement and test owner-scoped attachment removal and no-file behavior.
- [x] Verify resume survival and the saved-skill/empty-resume rule.
- [x] Implement and test UI confirmation, loading, errors, gate use and attachment updates.
- [x] Review the optional API and document why it was skipped.
- [x] Run the full root-level test suite: 593 passed, 0 failed, 0 skipped.
- [x] Write this report and manual browser test instructions.
- [x] Keep git, packages, migrations and unrelated business logic unchanged.
- [ ] Execute the live candidate/employer browser scenarios above.

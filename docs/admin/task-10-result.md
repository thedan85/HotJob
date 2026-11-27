# Task 10 — CV parsing backend

Date: 2026-11-27  
Status: backend implemented; fresh review's Important findings fixed and regression-tested. Three deferred minor findings and one pre-existing full-suite failure remain, documented below. No integration/commit/push performed.

## Summary

Added candidate-authorized, memory-only resume suggestion parsing before any
resume/file is saved. Rules mode is local and is the default. AI requires both
configuration opt-in and an explicit `Ai` call; failures return the cached local
suggestions with a warning. No review UI, endpoint, migration, or automatic save
was added.

Existing upload validation is shared without changing storage's signature,
streaming save, generated paths, accepted signatures, or overflow cleanup.
Existing resume writes and their required-title/at-least-one-skill rules remain
unchanged.

## Exact files changed

### Modified existing product files

- `hotjob/hotjob.csproj`
- `hotjob/Program.cs`
- `hotjob/.env.example`
- `hotjob/Services/Candidate/CvFileStorage.cs`

### Added product files

- `hotjob/Properties/AssemblyInfo.cs`
- `hotjob/DTOs/Candidate/ParsedResumeDtos.cs`
- `hotjob/Services/Candidate/IResumeParserService.cs`
- `hotjob/Services/Candidate/CvFileValidation.cs`
- `hotjob/Configuration/ResumeParsingRegistration.cs`
- `hotjob/Services/Candidate/ResumeParsing/DeepSeekOptions.cs`
- `hotjob/Services/Candidate/ResumeParsing/DeepSeekResumeClient.cs`
- `hotjob/Services/Candidate/ResumeParsing/DeepSeekResumeJson.cs`
- `hotjob/Services/Candidate/ResumeParsing/DocxArchiveGuard.cs`
- `hotjob/Services/Candidate/ResumeParsing/DocxResumeTextExtractor.cs`
- `hotjob/Services/Candidate/ResumeParsing/ExtractedResumeText.cs`
- `hotjob/Services/Candidate/ResumeParsing/ParsedResumeNormalizer.cs`
- `hotjob/Services/Candidate/ResumeParsing/PdfResumeTextExtractor.cs`
- `hotjob/Services/Candidate/ResumeParsing/ResumeAiUsageLimiter.cs`
- `hotjob/Services/Candidate/ResumeParsing/ResumeContactRedactor.cs`
- `hotjob/Services/Candidate/ResumeParsing/ResumeDateParser.cs`
- `hotjob/Services/Candidate/ResumeParsing/ResumeExtractionRunner.cs`
- `hotjob/Services/Candidate/ResumeParsing/ResumeParserService.cs`
- `hotjob/Services/Candidate/ResumeParsing/ResumeParsingOptions.cs`
- `hotjob/Services/Candidate/ResumeParsing/ResumeSkillMatcher.cs`
- `hotjob/Services/Candidate/ResumeParsing/ResumeTextExtractor.cs`
- `hotjob/Services/Candidate/ResumeParsing/ResumeTextNormalization.cs`
- `hotjob/Services/Candidate/ResumeParsing/RuleBasedResumeParser.cs`

### Added tests

Under `hotjob/Tests/hotjob.Tests/CandidateTests/ResumeParsing/`:

- `CvFileValidationTests.cs`
- `DeepSeekResumeClientTests.cs`
- `DocxResumeTextExtractorTests.cs`
- `ParsedResumeNormalizerTests.cs`
- `PdfResumeTextExtractorTests.cs`
- `ResumeAiUsageLimiterTests.cs`
- `ResumeContactRedactorTests.cs`
- `ResumeDocumentSafetyTests.cs`
- `ResumeParserServiceTests.cs`
- `ResumeParsingEdgeTests.cs`
- `ResumeParsingRegistrationTests.cs`
- `ResumeParsingSafetyTests.cs`
- `ResumeReadDeadlineTests.cs`
- `ResumeReviewRegressionTests.cs`
- `RuleBasedResumeParserTests.cs`

Under `hotjob/Tests/hotjob.Tests/Support/ResumeParsing/`:

- `ManualTimeProvider.cs`
- `ParseWriteGuardInterceptor.cs`
- `RecordingHttpHandler.cs`
- `RecordingLoggerProvider.cs`
- `ResumeDocumentFactory.cs`
- `ResumeParserTestContext.cs`
- `SpecialPdfFactory.cs`

### Added workflow/report documents

- `docs/superpowers/specs/2026-11-26-cv-parsing-design.md`
- `docs/superpowers/plans/2026-11-26-cv-parsing.md`
- `docs/superpowers/plans/2026-11-26-cv-parsing-checklist.md`
- `docs/admin/task-10-result.md`
- Local execution ledger: `.superpowers/sdd/2026-11-26-cv-parsing/progress.md`

This manifest tracks this task's edits without git commands. It makes no claim
about unrelated user changes in the working directory. Generated `bin`, `obj`,
and ignored `TestResults` are build/test outputs, not source deliverables.

## Dependencies and security audit

| Direct package | Exact version | NuGet publication date |
| --- | --- | --- |
| `PdfPig` | `0.1.16` | 2026-08-22 |
| `DocumentFormat.OpenXml` | `3.5.1` | 2026-03-18 |

The user approved official package `PdfPig`, whose namespace is
`UglyToad.PdfPig`, rather than the separate package literally named
`UglyToad.PdfPig` listing old custom prereleases. Both selected releases are
stable and older than two weeks.

New OpenXML transitives resolve to `DocumentFormat.OpenXml.Framework 3.5.1`
and `System.IO.Packaging 10.0.2`. PdfPig's selected modern target adds no
transitive dependencies. NuGet registration/advisory metadata was checked
before installation; audited restore and the native vulnerability listing
reported no advisories in this newly added graph.

Commands:

```text
dotnet restore hotjob.slnx -p:NuGetAudit=true -p:NuGetAuditMode=all
dotnet list hotjob/hotjob.csproj package --vulnerable --include-transitive
dotnet list hotjob/hotjob.csproj package --include-transitive
```

Pre-existing advisories remain, deliberately not upgraded in this scoped task:

- App/test transitive `MySqlConnector 2.4.0`: moderate,
  <https://github.com/advisories/GHSA-473q-m89c-ghf8>.
- Test transitive `SQLitePCLRaw.lib.e_sqlite3 2.1.10`: high,
  <https://github.com/advisories/GHSA-2m69-gcr7-jv3q>.

Known-advisory checks are not a guarantee of package safety. No AI SDK,
additional crypto library, OCR library, or legacy DOC dependency was installed.

## Configuration

| Environment-style key | Default |
| --- | --- |
| `ResumeParsing__UseAi` | `false` |
| `ResumeParsing__AiDailyLimit` | `10` |
| `ResumeParsing__AiPerUserDailyLimit` | `3` |
| `DeepSeek__ApiKey` | empty |
| `DeepSeek__BaseUrl` | `https://api.deepseek.com` |
| `DeepSeek__Model` | `deepseek-chat` |

Only commit-safe defaults/placeholders were appended to `.env.example`.
The existing loader already limits `.env.local` to Development and maps `__`
to `:`. `.gitignore` line 10 excludes `.env.local`; that file was never read.
Without git inspection this does not prove that it was never historically
tracked. No credentials were printed or added to source.

AI option parsing fails closed: malformed enable flags disable AI; malformed or
nonpositive quotas permit zero calls. Missing settings use the defaults above.
An invalid/non-HTTPS base URL, URL credentials/query/fragment, missing key, or
empty model makes `IsAiAvailable=false`. The property describes configured
capability, not remaining quota/provider uptime. Model selection is always taken
from options; the requested default is not silently replaced with a newer name.
Current provider documentation advertises newer models, so deployments may need
to configure an accepted model; an unsupported model safely falls back.

## Final DTO shape

Namespace: `hotjob.DTOs.Candidate`.

```text
ResumeParseMode: Rules | Ai
ResumeParseConfidence: High | Medium | Low

ParsedResumeDto
  Summary: string?
  SummaryConfidence: ResumeParseConfidence?
  Educations: IReadOnlyList<ParsedEducationDto>
  WorkExperiences: IReadOnlyList<ParsedWorkExperienceDto>
  Skills: IReadOnlyList<ParsedSkillDto>
  ModeUsed: ResumeParseMode
  Warnings: IReadOnlyList<string>
  UnrecognizedSkills: IReadOnlyList<string>

ParsedEducationDto
  Value: ResumeEducationInput
    SchoolName, Major, StartDate, EndDate, Gpa, Description
  Degree: string?
  FieldOfStudy: string?
  Confidence: ResumeParseConfidence

ParsedWorkExperienceDto
  Value: ResumeWorkExperienceInput
    CompanyName, JobTitle, StartDate, EndDate, Description
  IsCurrent: bool
  Confidence: ResumeParseConfidence

ParsedSkillDto
  Value: ResumeSkillInput
    SkillId, YearsOfExperience
  SkillName: string
  Confidence: ResumeParseConfidence
```

No row ID, title guess, file URL, contact/profile fields, or hidden save action.
Child input values fit existing write contracts; partial results still require
a candidate-supplied title and at least one selected catalog skill before save.

Degree/field metadata stays parse-only. Existing `Major` receives, for example,
`Bachelor of Science - Computer Science`, with whitespace normalization,
deduplication, and a 200-character cap plus warning. Separate Degree/FieldOfStudy
metadata each have a 200-character cap. The next UI must recompute/review Major
when editing those components.

## Local parsing rules and accuracy limitations

- Compare headings case/diacritic-insensitively using Unicode decomposition,
  combining-mark removal, and explicit `đ` folding; return NFC original text.
- Recognize all specified English/Vietnamese education, experience, skills,
  summary/objective headings; support colons, inline heading content, and repeated
  sections. Common project/contact/language headings stop relevant sections.
- Organization extraction prefers labeled school/company/degree/major/job-title
  lines, then cautious keyword cues. Blank lines, organization labels, credible
  successive organizations/bullets, and subsequent date blocks delimit entries.
  Required organizations are never fabricated.
- Match live catalog names/IDs, not assumed seed IDs. Punctuation-aware,
  longest-span matching supports C#, C++, .NET, Node.js, ASP.NET Core, Spring Boot
  and the explicit aliases in the spec. Canonical aliases are inactive if that
  catalog row does not exist. Java does not match JavaScript; SQL does not match
  MySQL; .NET is not manufactured from an ASP.NET span.
- Recognized skills are deduplicated by ID; unknown skill-list fragments remain
  plain strings. No Skill row is created. Years default to 0, not inferred tenure.
- Dates support year ranges, month/year ranges, English month names, Vietnamese
  month expressions, ISO dates, and Present/Hiện tại. Incomplete starts use the
  first day and incomplete ends the last day, with a precision warning.
  Ongoing work has null EndDate and IsCurrent=true. Explicit ranges take
  precedence over incidental institution/company years. Invalid/reversed dates
  and unsupported non-ASCII decimal digit forms are omitted with warnings,
  rather than throwing, guessing, or swapping.
- Summary is literal section text, capped at 2,000 characters, or null. This
  remains true in AI mode: the model cannot replace it with invented text.
- Exact local catalog/summary matches have High confidence; structured/alias
  suggestions generally Medium; ambiguous organization heuristics Low.
  AI-derived items are Medium.

This is heuristic assistance, not accurate interpretation of arbitrary layouts.
Multi-column reading order, narrative/unlabeled organizations, ambiguous dates,
unusual headings, and formatting can cause omissions or incorrect associations.
The candidate must review every suggestion. Unknown terms are not an exhaustive
technical-skills dictionary.

Unsupported:

- Legacy `.doc`: friendly manual-entry error, no risky library.
- Scanned/image-only/blank documents: friendly no-readable-text/manual-entry
  error; OCR is out of scope.
- Password-protected/corrupt documents: sanitized friendly errors.
- DOCX layout reconstruction, headers/footers, embedded objects, and linked
  content are not required sources; only body text is used.

## Safety limits and cancellation

- Shared actual-byte input cap: 5 MB, including non-seekable/chunked streams.
  No source stream is rewound or disposed by parsing. Read deadline: 10 seconds.
  Waiting is bounded independently of whether the stream honors cancellation;
  outstanding read destinations remain owned/reachable until the read finishes.
- PDF: first 15 pages, 100,000 extracted characters, truncation warnings.
  PDF and DOCX extraction truncate at valid UTF-16 boundaries, never half an emoji.
- DOCX: 256 ZIP entries, ≤10 MB per expanded entry, ≤25 MB total expanded,
  ≤200:1 compression ratio, checked actual decompressed byte counts,
  `MaxCharactersInPart=2,000,000`, application XML depth 64, prohibited DTD/XXE.
  No ZIP extraction to disk or external relationship fetching.
- CPU extraction is offloaded behind two process-wide admission slots, with a
  10-second linked deadline. Busy requests fail promptly. A timed-out worker
  retains its slot/resources until it really exits; late faults are observed.
- **Not a sandbox:** synchronous PdfPig/OpenXML calls cannot be forcibly stopped
  by Task.Run/cancellation. Deadlines bound caller waiting, not internal memory
  allocations or absolute CPU termination. Hard isolation needs a killable,
  memory-limited external worker, outside this task.
- Before usable local results exist, cancellation yields a friendly failure.
  During AI, cancellation/timeout returns the cached rules result without new
  database queries.
- Output caps: 20 education, 20 work, 100 selected skills, 100 unknown strings;
  existing 200/2,000-character field limits; at most 50 warnings of 300 characters.
  Truncation/dropped-invalid-value warnings are fixed text without PII.

## Data sent to DeepSeek and redaction

Only explicit AI requests after candidate authorization and successful local
extraction may send an HTTPS request. Rules mode never calls the provider.

Outbound body contains **only**:

1. Configured model and fixed generation parameters.
2. Fixed extraction/system prompt with strict JSON schema/example.
3. One user message containing contact-redacted extracted CV text, capped at
   30,000 characters **after redaction**.

No raw file bytes, original filename, user/candidate IDs, claims, database rows/
skill catalog, file URLs, application history, or additional profile data.
The API key is only the Bearer header, never body/query/results/logs.

Redaction removes obvious email/mailto, telephone formats, URLs/www/bare domains/
social links, labeled social handles, and English/Vietnamese street-address
lines. Fixed markers contain no matched source text. Ordinary date ranges are
preserved. Recognized date ranges are protected before numeric-contact matching,
including adjacent date/phone text and dates followed by duration notes. Runs
containing adjacent phones are redacted rather than bypassed for having too many
digits. This conservative numeric handling can also remove long non-contact
numeric runs. Technical names Node.js/ASP.NET are not treated as bare contact URLs.
Redaction runs over the complete bounded extracted text before AI truncation.
Patterns are bounded, with 100 ms regex deadlines and a one-second overall
budget; failure/no meaningful remaining text skips AI without quota charge.

**Not anonymization:** personal names, institution/company names, dates, and
career descriptions in the CV may remain. Obfuscated contact information can
escape heuristic detection; legitimate information can be over-redacted.
Provider/server transport metadata is not removed by text redaction. Future UI
must disclose the external processing before its explicit AI action.

Transport: one POST to `{BaseUrl}/chat/completions`, no retry/redirect, configured
model, temperature 0, JSON-object response format, max_tokens 4096, stream false,
30-second send/body budget, 256 KiB actual response limit, depth-16 JSON.

CV instructions never select code paths/tools. Model output is untrusted:
fixed schema, strict structural types, no trailing/commented/duplicate-key JSON,
unknown fields ignored, successful completion required, then catalog and field
normalization. Unknown/conflicting skills cannot bypass validation. Text is
never executed or rendered as HTML by this service. Schema compliance cannot
prove the factual truth of an AI suggestion.

The parser does not log document content, filenames, personal data, provider
bodies/errors, or options. Named HttpClient logging is removed and Authorization
is explicitly redacted. Tests capture Trace-level logging and unique sentinels.
Application instrumentation added later must preserve these restrictions.

## Usage limits

A singleton, lock-protected counter uses injected TimeProvider and UTC date.
Admission atomically reserves global and verified-user slots; defaults are
10/day and 3/user/day. No per-user data is added for rejected new identities.

Redaction/serialization/configuration/cancellation checks precede dispatch.
An unsent reservation is released; once SendAsync is invoked, the attempt
counts even if it fails, times out, or is canceled. Actual provider receipt
cannot reliably be inferred from a transport failure. No retries means one
admission makes at most one call. Date-stamped reservation disposal cannot
decrement the next day's totals.

Restart resets counts. “Global” means one application process; replicas would
each have their own allowance. Distributed quotas are not implemented.

## Verification

Baseline before product edits:

```text
dotnet test hotjob.slnx -c Release --logger "trx;LogFileName=task-10-baseline.trx" --results-directory TestResults/task-10-baseline
Passed: 806, Failed: 1, Skipped: 0, Total: 807
```

Final full implementation run after review fixes (terminal output and TRX Counters verified):

```text
dotnet test hotjob.slnx -c Release --logger "trx;LogFileName=task-10-full.trx" --results-directory TestResults/task-10
Passed: 919, Failed: 1, Skipped: 0, Total: 920
```

All **113 new parsing test cases pass**. The first implementation run was
900 passed / 1 failed / 0 skipped (901 total); the final run adds the review
regressions. The full suite is **not green**:
the same pre-existing failure remains in
`JobVisibilityTests.JobsOfAnInactiveCompany_AreHiddenAndClosedToApplications("rejected")`,
line 51: expected `Conflict`, actual `NotFound`.
`ApplicationService` filters unapproved companies before loading a job, so its
existing NotFound branch precedes the conflict check. Apply/visibility code and
the test were not altered or weakened.

Tests generate fake documents in memory, including a real image-only PDF and a
password-protected PDF independently opened with its test password. No binary
fixtures. HTTP uses fake handlers; SQLite uses the existing in-memory fixture.
No application startup, user's database, browser save/upload action, real AI
request, or secret-file loading was performed.

Coverage includes bilingual sections/normalization/dates, catalog boundaries,
partial results, byte/signature limits, PDF/ZIP/XML guards, cooperative timeout
ownership, authorization and principal precedence, fake AI failures/schema/
privacy/cancellation, quota races/UTC rollover, shared DI lifetimes, no-write EF
interception, existing-row preservation, and storage tripwires.

### Fresh review and fix verification

One fresh-context read-only reviewer reported no Critical findings, six
Important findings, and five Minor findings. The six Important issues were
addressed in one fix pass:

1. Protect recognized date ranges before redacting adjacent numeric phone runs.
2. Truncate extracted PDF/DOCX text without splitting surrogate pairs; test the
   full service with an emoji at the 100,000-character boundary.
3. Restrict date tokens to supported ASCII digits and use nonthrowing conversion.
4. Independently bound waits on cancellation-ignoring header/body reads while
   retaining their buffers and leaving the caller's stream open.
5. Split ordinary successive unlabeled organization/date blocks in both languages.
6. Exercise ZIP guards directly, add a safe control, and actually omit the
   missing-main-document fixture instead of relying on OpenXML package rejection.

Also fixed the review's incidental-year/date-range association recommendation.
Regraded invalid supplied AI IDs and lost source-summary shortening warnings as
Important: explicit zero/negative IDs now remain invalid through catalog
validation, and AI success retains the literal summary's truncation warning.

The behavior regressions were observed failing first: 13 cases in the first
review-regression run plus two cancellation-ignoring read cases. Added coverage
also verifies exact HTTP candidate A/circuit candidate B precedence and the
complete AI work/education schema, depth, tool-call, and truncation guards.
Guard-test strengthening correctly passed against existing guard code; no
fictional red phase is claimed for that coverage improvement.

After fixes, the focused feature run passed 111/111; the final full run passed
all 113 feature cases after two additional coverage cases. No second independent
review was requested; the fix pass was verified by tests, not a claim of reviewer
re-approval.

## Next task: review UI integration

Inject `hotjob.Services.Candidate.IResumeParserService`.

```csharp
// Use the trusted signed-in ID and the existing CircuitDbGate for DB-backed calls.
await using var stream = browserFile.OpenReadStream(CvFileStorage.MaxFileSizeBytes);
var result = await Parser.ParseAsync(
    userId, stream, browserFile.Name, ResumeParseMode.Rules, cancellationToken);
```

Show a separate **Parse with AI** button only when `Parser.IsAiAvailable`.
Disclose that redacted CV text goes externally; only that explicit action calls
the same method with `ResumeParseMode.Ai`. No key is needed for standard parsing.
Display actual ModeUsed, warnings, confidence, unknown terms, and editable degree/
field metadata. Treat every string as encoded text, not MarkupString/HTML.

Keep suggestions in the editor and allow correction/removal. Recompute Major
from edited Degree/FieldOfStudy with the existing 200-character limit. Require
a title and at least one selected existing skill. Only explicit Save maps to:

```csharp
var input = new ResumeInput
{
    Title = reviewedTitle,
    Summary = reviewed.Summary,
    Educations = reviewed.Educations.Select(x => x.Value).ToList(),
    WorkExperiences = reviewed.WorkExperiences.Select(x => x.Value).ToList(),
    Skills = reviewed.Skills.Select(x => x.Value).ToList()
};
// Only now call existing ResumeService.CreateAsync/UpdateAsync.
// Reopen the browser file separately for existing UploadFileAsync, if requested.
```

Do not call CreateAsync or UploadFileAsync merely to parse. The parser has no
saved-file token or durable upload; source selection must remain available in
the UI if the candidate later wants to attach the file.

The optional API was omitted: there is no existing resume controller, and the
next Blazor UI can call this service directly. Any later multipart endpoint
must add candidate authorization/request limits and reuse this service.

## Open issues and workflow decisions

- The existing full-suite failure and dependency advisories above remain.
- No real AI/model availability/accuracy was tested; deliberate fake-only policy.
- Heuristic parsing/redaction and in-process extraction have the limitations
  above; human review is required.
- Native execution used the supplied workspace without git/worktree operations.
  The user commits/pushes manually; package references travel in the csproj and
  are restored by subsequent builds. Keys do not travel with source.
- Some tests are grouped in fewer classes than the plan's initial filenames;
  no claim is made that every illustrative test sketch is a separate method.
- Password encryption is test-only BCL fixture generation because PdfPig's
  writer has no encryption API; normal/image fixtures use PdfDocumentBuilder.

### Deferred minor findings

These were not silently fixed or treated as completed requirements:

1. Mixed recognized/unknown skill fragments can lose the unknown fragment's
   original diacritics; slash-separated unknowns can stay combined. Early matcher
   truncation can also omit some shortening/omission warnings. Catalog selection
   and final string/item limits remain enforced.
2. Combined-invalid input can have different message precedence: an empty
   stream named `cv.exe` reports unsupported type in parsing, whereas upload
   with reported length zero reports empty file. Both reject it; no size,
   signature, or extension rule is bypassed.
3. Main-body traversal can include text inside a Word text box, although text
   boxes were outside the intended extraction scope. No linked content is
   fetched or embedded content executed.

### Rulings made during Native execution

Recorded in execution order; costs describe the remaining trade-off:

1. Work in the supplied workspace and retain the ledger instead of git-based
   isolation/commit scripts: explicit user restriction; no agent-provided git
   isolation/history.
2. Preserve unrelated visibility behavior and dependency versions: explicit
   scope; the known suite failure/advisories remain.
3. Group helper tests in fewer classes: avoids redundant fixtures; some planned
   class/filter names differ, so actual commands/counts are recorded.
4. Generate the protected PDF with test-only BCL revision-2 encoding: PdfPig
   cannot write encryption; extra legacy fixture-code maintenance.
5. Regrade invalid AI IDs, lost summary warnings, and incidental-year association
   as Important: avoids inconsistent untrusted suggestions/silent information
   loss; conservative omissions/warnings may reduce suggestions.
6. Keep the pre-existing visibility failure/advisories out of the final fix
   pass: reproduced before edits; separate remediation remains necessary.
7. Treat temporary report/checklist absence as resolved documentation work:
   reconciled with final evidence; documentation could otherwise misstate status.
8. Retain the approved in-process extraction design: no hard kill/memory sandbox;
   hostile library work can occupy two workers or internal memory.
9. Retain restart-reset, single-process quotas: accepted design; restarts or
   replicas can exceed a deployment-wide allowance.
10. Exclude DOC/OCR/perfect layout recovery: approved scope; unsupported/complex
    documents need manual correction.
11. Do not claim complete anonymization: optional redacted-text sharing; names
    and obfuscated contacts can remain.
12. Do not automatically merge local collections into successful partial AI
    output: approved contract; AI may omit information rules found.
13. Leave real-provider accuracy/model compatibility, UI consent/rendering, and
    deployment compliance unverified here: fake-only backend scope; the follow-up
    UI/deployment must verify those boundaries.
14. Do not certify historical git tracking/provenance: git prohibited; unrelated
    or historical changes cannot be independently certified.

No branch-finishing/integration menu was offered: the full suite retains a
known failure and the user explicitly handles commits/pushes. No git command,
secret read, real DeepSeek call, app startup, or broad cleanup was performed.

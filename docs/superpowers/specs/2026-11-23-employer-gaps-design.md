# Employer gaps design

Date: 2026-11-23
Status: approved (approach (a) for all three features)

## Intent

Close the three partial employer features from `HotJob/docs/feature-audit-2026-11-23.md` section III:

1. Application detail must show the CV as it was at apply time, not the candidate's live CV.
2. Status history must identify the actor by something human-readable.
3. Rescheduling or cancelling an interview must notify the candidate.

## Goals

- Freeze full CV (education, experience, skills, summary, file URL) on apply.
- Show actor name/email in employer status history.
- Create one candidate notification per successful interview update or cancellation.
- Keep legacy applications readable (no backfill required).

## Non-goals

- No snapshot backfill for existing applications.
- No audit log, realtime notification, or candidate-side changes.
- No new controllers or API endpoints; existing Blazor UI and API responses gain fields.

## Approaches considered

### Snapshot

- **(a) JSON text column on `Application` (chosen)** — one generated migration, full structure frozen, read-only display, no duplicate tables.
- (b) Normalized snapshot tables — queryable, but more entities, mappings, migrations, and code for data no query uses.

### History actor

- **(a) Load `ChangedBy` navigation, map email/name into `HistoryDto` (chosen)** — no migration, one include + DTO field + UI line.
- (b) Show raw `ChangedByUserId` — no useful identity for a human reviewer.

### Interview notifications

- **(a) Add `repo.AddNotification` inside existing `UpdateAsync`/`CancelAsync` (chosen)** — reuses transaction-free save and existing candidate include.
- (b) New event/messaging layer — unnecessary for one producer.

## Design

### 1. Full CV snapshot

- Add nullable `Application.SnapshotJson` string column (unbounded text; generated migration, never hand-edited).
- Add `ResumeSnapshot` model in `Models/Application` with title, summary, file URL, and lists of education, work experience, skill snapshots (no entity IDs).
- `ApplicationService.ApplyAsync` loads the resume's children (`Educations`, `WorkExperiences`, `ResumeSkills.Skill`), builds `ResumeSnapshot`, serializes with `System.Text.Json`, and stores it in `SnapshotJson` alongside existing summary/file fields.
- `ApplicantMapping.ToDetail` uses `SnapshotJson` when present; otherwise falls back to the current live resume for legacy rows.
- UI keeps existing resume layout; add a short label indicating the data is the apply-time snapshot. Existing `SnapshotSummary`/`SnapshotFileUrl` remain populated.

### 2. Status history actor

- `ApplicantRepository.GetOwnedDetailAsync` includes `StatusHistory.ChangedBy`.
- `HistoryDto` gains optional `ChangedByName`.
- `ApplicantMapping.ToDetail` fills it from `ChangedBy.Email`, falling back to `ChangedByUserName` then the raw user ID; null means system.
- `Interviews.razor` renders actor next to the timestamp.

### 3. Interview notifications

- Inject `TimeProvider` into `InterviewService`; use it for new timestamps and date validation. Update the one manual test construction.
- `UpdateAsync`: when at least one field is supplied, add a candidate notification summarising the new date/time and location, including job title.
- `CancelAsync`: add a candidate notification naming the job and the cancelled interview time.
- Notifications persist in the same `SaveChangesAsync` as the interview change; no extra transaction.

## Constraints

- EF queries must still translate on SQLite and MySQL/Pomelo; snapshot data is read as a whole column, never queried.
- Migration generated with `dotnet ef migrations add`, not hand-edited.
- Keep existing response shapes compatible; only additive DTO fields.
- Use `TimeProvider` for new "now" values.

## Verification

- Unit tests: apply writes snapshot JSON; detail reads frozen values after live resume changes; legacy null snapshot falls back; update/cancel create notifications; update with no fields creates none; actor mapping.
- `dotnet build HotJob/hotjob/hotjob.csproj -nologo -v q`
- `dotnet test HotJob/hotjob/Tests/hotjob.Tests -nologo -v q`

## Left to builder

- Exact JSON property names and `ResumeSnapshot` record layout.
- Notification wording.
- UI microcopy and styling class for the snapshot label/actor line.

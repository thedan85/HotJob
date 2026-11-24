# Employer Gaps Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the three partial employer features: full CV snapshot at apply, actor name in status history, and candidate notifications on interview update/cancel.

**Architecture:** Freeze the full resume into a JSON column on `Application` at apply time; read it back for employer detail with a live-resume fallback for legacy rows. Load the existing `ChangedBy` navigation for history display. Reuse `ApplicantRepository.AddNotification` inside interview update/cancel.

**Tech Stack:** ASP.NET Core 10, EF Core 9, Blazor Interactive Server, xUnit on SQLite in-memory, System.Text.Json.

**Spec:** `HotJob/docs/superpowers/specs/2026-11-23-employer-gaps-design.md`

## Global Constraints

- Migrations are generated with `dotnet ef migrations add`; never hand-edit migration files or `ApplicationDbContextModelSnapshot.cs`.
- EF queries must translate on SQLite and MySQL/Pomelo.
- Use the registered `TimeProvider` for new "now" values.
- Keep existing JSON/API response shapes additive; no removed fields.
- Business validation returns `ServiceResult` failures, never throws.
- Tests use one fresh SQLite fixture per test, plain `Assert`, and `NullLogger<T>.Instance`.

## Review Focus

- Legacy application with `SnapshotJson == null` must show the live resume, not an empty CV.
- A live resume edited after apply must not change the employer's snapshot view.
- Interview update with no supplied fields must create no notification.
- History actor null (system/candidate deleted) must render as system, not throw.
- Snapshot with empty education/experience/skills lists must round-trip without null collections.

---

### Task 1: Freeze full resume snapshot on apply

**Files:**
- Create: `HotJob/hotjob/Models/Application/ResumeSnapshot.cs`
- Modify: `HotJob/hotjob/Models/Application/Application.cs`
- Modify: `HotJob/hotjob/Services/Candidate/ApplicationService.cs`
- Test: `HotJob/hotjob/Tests/hotjob.Tests/CandidateTests/ApplicationServiceTests.cs`

**Interfaces:**
- Produces:
  - `Application.SnapshotJson` (`string?`)
  - `ResumeSnapshot(string Title, string? Summary, string? FileUrl, IReadOnlyList<SnapshotEducation> Educations, IReadOnlyList<SnapshotWorkExperience> WorkExperiences, IReadOnlyList<SnapshotSkill> Skills)`
  - `SnapshotEducation(string SchoolName, string? Major, DateOnly? StartDate, DateOnly? EndDate, double? Gpa, string? Description)`
  - `SnapshotWorkExperience(string CompanyName, string? JobTitle, DateOnly? StartDate, DateOnly? EndDate, string? Description)`
  - `SnapshotSkill(int SkillId, string SkillName, int YearsOfExperience)`

- [ ] **Step 1: Write the failing test**

In `ApplicationServiceTests`, extend the `ApplyAsync_Success_FreezesSnapshot...` setup by seeding one education, one work experience, and one resume skill on the resume before applying. Add assertions:

```csharp
Assert.False(string.IsNullOrWhiteSpace(application.SnapshotJson));
var snapshot = JsonSerializer.Deserialize<ResumeSnapshot>(application.SnapshotJson!);
Assert.NotNull(snapshot);
Assert.Equal("Backend CV", snapshot!.Title);
Assert.Equal("HUST", Assert.Single(snapshot.Educations).SchoolName);
Assert.Equal("Acme", Assert.Single(snapshot.WorkExperiences).CompanyName);
Assert.Equal("C#", Assert.Single(snapshot.Skills).SkillName);
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test HotJob/hotjob/Tests/hotjob.Tests -nologo -v q --filter "FullyQualifiedName~ApplyAsync_Success_FreezesSnapshot"`
Expected: FAIL — `SnapshotJson`/`ResumeSnapshot` do not exist.

- [ ] **Step 3: Implement snapshot model and apply write**

Create `ResumeSnapshot.cs` with the records above. Add an unbounded `public string? SnapshotJson { get; set; }` to `Application` (no `MaxLength`, so Pomelo renders `longtext`) with a comment that it stores a frozen JSON snapshot.

In `ApplicationService.ApplyAsync`, load the resume with `Educations`, `WorkExperiences`, and `ResumeSkills.Skill` instead of the current scalar projection. Build `ResumeSnapshot` from the entity and set `SnapshotJson = JsonSerializer.Serialize(snapshot)` next to `SnapshotSummary`/`SnapshotFileUrl`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test HotJob/hotjob/Tests/hotjob.Tests -nologo -v q --filter "FullyQualifiedName~ApplyAsync_Success_FreezesSnapshot"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add HotJob/hotjob/Models/Application/ResumeSnapshot.cs HotJob/hotjob/Models/Application/Application.cs HotJob/hotjob/Services/Candidate/ApplicationService.cs HotJob/hotjob/Tests/hotjob.Tests/CandidateTests/ApplicationServiceTests.cs
git commit -m "feat: freeze full resume snapshot on apply"
```

---

### Task 2: Read frozen snapshot in employer detail

**Files:**
- Modify: `HotJob/hotjob/DTOs/Employer/ApplicantDtos.cs`
- Modify: `HotJob/hotjob/Services/Employer/ApplicantMapping.cs`
- Modify: `HotJob/hotjob/Components/Pages/Employer/Interviews.razor`
- Test: `HotJob/hotjob/Tests/hotjob.Tests/EmployerTests/EmployerAtsTests.cs`

**Interfaces:**
- Consumes: `ResumeSnapshot`, `Application.SnapshotJson` from Task 1.
- Produces: `ApplicantDetailResponse.IsSnapshot` (`bool`); `ApplicantMapping.ToDetail` uses snapshot when present.

- [ ] **Step 1: Write the failing tests**

Add two tests to `EmployerAtsTests`:

```csharp
[Fact]
public async Task GetDetail_UsesFrozenSnapshot_AfterLiveResumeChanges()
```

Seed an application, serialize a `ResumeSnapshot` with `SchoolName = "Frozen U"` into `application.SnapshotJson`, save, then change the live resume's education to `"Live U"` and add a new skill. After `Detach(fx)`, assert `detail.IsSnapshot`, `detail.Resume.Educations.Single().SchoolName == "Frozen U"`, and the newly added live skill is absent.

```csharp
[Fact]
public async Task GetDetail_LegacyNullSnapshot_FallsBackToLiveResume()
```

Seed an application with `SnapshotJson = null` and a live resume skill; assert `!detail.IsSnapshot` and the live skill is returned.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test HotJob/hotjob/Tests/hotjob.Tests -nologo -v q --filter "FullyQualifiedName~GetDetail_UsesFrozenSnapshot|FullyQualifiedName~GetDetail_LegacyNullSnapshot"`
Expected: FAIL — `IsSnapshot` missing and snapshot ignored.

- [ ] **Step 3: Implement snapshot mapping**

Add `public bool IsSnapshot { get; set; }` to `ApplicantDetailResponse`.

In `ApplicantMapping.ToDetail`, add a private `ResumeDetailDto FromSnapshot(ResumeSnapshot s)` that maps all fields. If `a.SnapshotJson` deserializes, set `IsSnapshot = true` and use it; otherwise keep the existing live-resume mapping. Wrap deserialization in `try/catch (JsonException)` and fall back to live data so corrupt legacy JSON cannot break the page.

In `Interviews.razor`, change the resume subtitle to include `@(selected.IsSnapshot ? " · snapshot at apply" : " · current CV")`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test HotJob/hotjob/Tests/hotjob.Tests -nologo -v q --filter "FullyQualifiedName~GetDetail_"`
Expected: PASS for all detail tests.

- [ ] **Step 5: Commit**

```bash
git add HotJob/hotjob/DTOs/Employer/ApplicantDtos.cs HotJob/hotjob/Services/Employer/ApplicantMapping.cs HotJob/hotjob/Components/Pages/Employer/Interviews.razor HotJob/hotjob/Tests/hotjob.Tests/EmployerTests/EmployerAtsTests.cs
git commit -m "feat: show apply-time CV snapshot in employer detail"
```

---

### Task 3: Show status history actor

**Files:**
- Modify: `HotJob/hotjob/Repositories/Employer/ApplicantRepository.cs`
- Modify: `HotJob/hotjob/DTOs/Employer/ApplicantDtos.cs`
- Modify: `HotJob/hotjob/Services/Employer/ApplicantMapping.cs`
- Modify: `HotJob/hotjob/Components/Pages/Employer/Interviews.razor`
- Test: `HotJob/hotjob/Tests/hotjob.Tests/EmployerTests/EmployerAtsTests.cs`

**Interfaces:**
- Produces: `HistoryDto(... , string? ChangedByName = null)`.
- Consumes: existing `ApplicationStatusHistory.ChangedBy`.

- [ ] **Step 1: Write the failing test**

Add `GetDetail_History_IncludesActorEmail`: seed an employer/application, add an `ApplicationStatusHistory` row with `ChangedByUserId = owner`, save, detach, then assert `detail.StatusHistory.Single().ChangedByName == "hr@example.com"`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test HotJob/hotjob/Tests/hotjob.Tests -nologo -v q --filter "FullyQualifiedName~GetDetail_History_IncludesActorEmail"`
Expected: FAIL — `ChangedByName` does not exist.

- [ ] **Step 3: Implement actor display**

Add `.Include(a => a.StatusHistory).ThenInclude(h => h.ChangedBy)` beside the existing history include in `GetOwnedDetailAsync`.

Add `string? ChangedByName = null` as the last parameter of `HistoryDto`. In `ToDetail`, pass `h.ChangedBy?.Email ?? h.ChangedBy?.UserName ?? h.ChangedByUserId`.

In the history timeline item, add a line under the note:

```razor
<span class="ei-timeline-note">@(h.ChangedByName ?? "System")</span>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test HotJob/hotjob/Tests/hotjob.Tests -nologo -v q --filter "FullyQualifiedName~GetDetail_History_IncludesActorEmail"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add HotJob/hotjob/Repositories/Employer/ApplicantRepository.cs HotJob/hotjob/DTOs/Employer/ApplicantDtos.cs HotJob/hotjob/Services/Employer/ApplicantMapping.cs HotJob/hotjob/Components/Pages/Employer/Interviews.razor HotJob/hotjob/Tests/hotjob.Tests/EmployerTests/EmployerAtsTests.cs
git commit -m "feat: show actor in application status history"
```

---

### Task 4: Notify candidate on interview update/cancel

**Files:**
- Modify: `HotJob/hotjob/Services/Employer/InterviewService.cs`
- Modify: `HotJob/hotjob/Tests/hotjob.Tests/EmployerTests/EmployerAtsTests.cs`
- Modify: `HotJob/hotjob/Tests/hotjob.Tests/EmployerTests/EmployerGuardTests.cs`

**Interfaces:**
- Produces: `InterviewService(IApplicantRepository repo, TimeProvider timeProvider, ILogger<InterviewService> logger)`.
- Consumes: `repo.AddNotification` and `interview.Application.Candidate.UserId`.

- [ ] **Step 1: Write the failing tests**

In `EmployerAtsTests`, update the `Interviews` helper to pass `TimeProvider.System`, and add:

```csharp
[Fact]
public async Task UpdateInterview_Reschedules_NotifiesCandidate()
```

Create an interview, clear `fx.Db.Notifications`, call `UpdateAsync` with a new date and location, then assert one notification for the candidate with title `"Interview updated"` and content containing the job title.

```csharp
[Fact]
public async Task UpdateInterview_NoFields_DoesNotNotify()
```

Create an interview, clear notifications, call `UpdateAsync` with an empty `UpdateInterviewRequest`, then assert `fx.Db.Notifications` is empty.

```csharp
[Fact]
public async Task CancelInterview_NotifiesCandidate()
```

Create an interview, clear notifications, call `CancelAsync`, then assert one notification with title `"Interview cancelled"` and content containing the job title.

In `EmployerGuardTests`, change the manual construction to `new InterviewService(new ApplicantRepository(fx.Db), TimeProvider.System, NullLogger<InterviewService>.Instance)`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test HotJob/hotjob/Tests/hotjob.Tests -nologo -v q --filter "FullyQualifiedName~UpdateInterview_Reschedules_NotifiesCandidate|FullyQualifiedName~UpdateInterview_NoFields_DoesNotNotify|FullyQualifiedName~CancelInterview_NotifiesCandidate"`
Expected: FAIL — no notifications.

- [ ] **Step 3: Implement notifications**

Add `TimeProvider timeProvider` to the primary constructor. Replace new `DateTime.UtcNow` uses in this service with `timeProvider.GetUtcNow().UtcDateTime`.

In `UpdateAsync`, track `var hasChanges = request.InterviewDate.HasValue || request.LocationOrLink is not null || request.InterviewType.HasValue || request.Notes is not null || request.Status.HasValue;` before applying fields. If true, call `repo.AddNotification(new Notification { UserId = interview.Application.Candidate.UserId, Title = "Interview updated", Content = $"The interview for \"{interview.Application.JobPosting.Title}\" was updated to {interview.InterviewDate:u}." + (location ? $" Location: {location}." : ""), IsRead = false, CreatedAt = now })`.

In `CancelAsync`, before saving, add `"Interview cancelled"` notification with content `$"The interview for \"{interview.Application.JobPosting.Title}\" scheduled at {interview.InterviewDate:u} was cancelled."`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test HotJob/hotjob/Tests/hotjob.Tests -nologo -v q --filter "FullyQualifiedName~UpdateInterview|FullyQualifiedName~CancelInterview|FullyQualifiedName~CreateInterview"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add HotJob/hotjob/Services/Employer/InterviewService.cs HotJob/hotjob/Tests/hotjob.Tests/EmployerTests/EmployerAtsTests.cs HotJob/hotjob/Tests/hotjob.Tests/EmployerTests/EmployerGuardTests.cs
git commit -m "feat: notify candidates when interviews change or cancel"
```

---

### Task 5: Migration and full verification

**Files:**
- Create: `HotJob/hotjob/Migrations/<timestamp>_AddApplicationSnapshotJson.cs`
- Modify: `HotJob/hotjob/Migrations/ApplicationDbContextModelSnapshot.cs` (generated only)

- [ ] **Step 1: Generate the migration**

Run from `HotJob`:

```bash
dotnet ef migrations add AddApplicationSnapshotJson --project hotjob/hotjob.csproj
```

Do not edit the generated files. Confirm the `Up` adds `SnapshotJson` to `Applications`.

- [ ] **Step 2: Build**

Run: `dotnet build HotJob/hotjob/hotjob.csproj -nologo -v q`
Expected: 0 errors.

- [ ] **Step 3: Run the full suite**

Run: `dotnet test HotJob/hotjob/Tests/hotjob.Tests -nologo -v q`
Expected: all tests pass.

- [ ] **Step 4: Commit**

```bash
git add HotJob/hotjob/Migrations
git commit -m "chore: add application snapshot migration"
```

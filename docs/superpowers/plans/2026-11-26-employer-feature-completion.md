# Employer Feature Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Close the remaining employer/realtime/ML gaps recorded in `docs/feature-audit-2026-11-23.md` against `docs/features-needed.md`.

**Architecture:** Keep scoring in `MatchScoreService`: use the persisted FastTree probability when a model is active, with weighted-rule fallback and versioned cache markers. Push notification changes to active Blazor Server circuits through an in-process subscriber service (existing SignalR transport, no new package). Extend interviews with explicit round numbers and make saved CV upload URLs renderable under a strict local-path policy.

**Tech Stack:** ASP.NET Core 10, Blazor Interactive Server, EF Core 9 + SQLite tests, xUnit, ML.NET 5 FastTree.

**Spec:** `docs/features-needed.md`, `docs/feature-audit-2026-11-23.md`

## Global Constraints

- Run tests from `/home/dan/Projects/dotnet`; tests are SQLite-only and must not hit MariaDB.
- Use `TimeProvider` in business logic; no `DateTime.UtcNow` for "now".
- EF queries must translate on SQLite and MySQL/Pomelo.
- Blazor pages call services directly through DI; no HTTP loopback.
- Do not hand-edit generated migrations or `ApplicationDbContextModelSnapshot.cs`; use `dotnet ef`.
- Keep secrets out of source control.
- No notification broadcast may replace DB persistence; push is best-effort after commit/save.

## Review Focus

- Model unavailable or corrupt: `MatchScoreService` falls back to `weighted-rule`; test with no model file.
- New promoted model: cached score changes algorithm and is not served from stale row; test versioned marker.
- Untrusted snapshot URL: `/uploads/cv/...` accepted, absolute/script-ish/traversal URLs rejected; test policy.
- Interview round: create/update/list round survives service and mapping; test service round.
- Job close: every open application gets a notification and broadcast; test JobService close.
- Realtime fan-out: only subscribers of the notified user are called; test subscriber service.

---

### Task 1: Use FastTree probability for displayed match score

**Files:**
- Modify: `hotjob/Services/ML/MatchModelStore.cs`
- Modify: `hotjob/Services/ML/MatchScoreService.cs`
- Modify: `hotjob/Tests/hotjob.Tests/MLTests/MatchScoreServiceTests.cs`
- Modify: `hotjob/Tests/hotjob.Tests/MLTests/MatchTrainingTests.cs`
- Modify: `hotjob/Tests/hotjob.Tests/MLTests/SyntheticMatchTrainingTests.cs`

**Interfaces:**
- Consumes: `IMatchModelStore.TryPredict(MatchFeatures features, out double probability)`.
- Produces: `IMatchModelStore.ModelVersion` (nullable stable string); `MatchScoreService.ModelAlgorithmFor(string version)`; score `AlgorithmUsed` is `weighted-rule` or `FastTreeBinary:<version>`.

- [x] **Step 1: Write failing tests** for fixed model probability, no-model fallback, and version change forcing recompute.
- [x] **Step 2: Run `dotnet test ... --filter "FullyQualifiedName~MLTests"` and verify failures.**
- [x] **Step 3: Add versioned model selection in `MatchScoreService` and version tracking in `MatchModelStore`.**
- [x] **Step 4: Run ML tests and verify pass.**
- [x] **Step 5: Commit.**

### Task 2: Allow local CV snapshot URLs

**Files:**
- Create: `hotjob/Services/Common/UploadUrlPolicy.cs`
- Create: `hotjob/Tests/hotjob.Tests/CommonTests/UploadUrlPolicyTests.cs`
- Modify: `hotjob/Components/Pages/Employer/Interviews.razor`

**Interfaces:**
- Produces: `UploadUrlPolicy.IsSafeSnapshotUrl(string? url)`.

- [x] **Step 1: Write failing tests for local `/uploads/cv/...`, HTTPS, and hostile URLs.**
- [x] **Step 2: Run test filter and verify failure.**
- [x] **Step 3: Add policy and replace private `SafeUrl` in Interviews page.**
- [x] **Step 4: Run CommonTests and verify pass.**
- [x] **Step 5: Commit.**

### Task 3: Add interview rounds

**Files:**
- Modify: `hotjob/Models/Application/Interview.cs`
- Modify: `hotjob/DTOs/Employer/ApplicantDtos.cs`
- Modify: `hotjob/Services/Employer/InterviewService.cs`
- Modify: `hotjob/Services/Employer/ApplicantMapping.cs`
- Modify: `hotjob/Components/Pages/Employer/Interviews.razor`
- Create migration via `dotnet ef migrations add InterviewRounds --project hotjob/hotjob.csproj`
- Modify: `hotjob/Tests/hotjob.Tests/EmployerTests/EmployerAtsTests.cs`

**Interfaces:**
- Produces: `Round` on `Interview`, `CreateInterviewRequest`, `UpdateInterviewRequest`, `InterviewResponse`; default 1.

- [x] **Step 1: Write failing service test that creates round 2 and reads it back.**
- [x] **Step 2: Run employer test filter and verify failure.**
- [x] **Step 3: Add model/DTO/service/mapping/UI fields and migration.**
- [x] **Step 4: Run employer tests and verify pass.**
- [x] **Step 5: Commit.**

### Task 4: Notify candidates on job close

**Files:**
- Modify: `hotjob/Services/Job/JobService.cs`
- Modify: `hotjob/Tests/hotjob.Tests/JobTests/JobCloseTests.cs` (or nearest existing job-close test file)

**Interfaces:**
- Produces: one `Notification` per open application rejected by `DeleteOrCloseAsync`.

- [x] **Step 1: Write failing test asserting notification rows for mass-rejected applications.**
- [x] **Step 2: Run job test filter and verify failure.**
- [x] **Step 3: Add notifications in `DeleteOrCloseAsync` after status changes.**
- [x] **Step 4: Run job tests and verify pass.**
- [x] **Step 5: Commit.**

### Task 5: Realtime notification fan-out over Blazor Server circuits

**Files:**
- Create: `hotjob/Services/Notifications/INotificationRealtime.cs`
- Create: `hotjob/Services/Notifications/INotificationBroadcaster.cs`
- Create: `hotjob/Tests/hotjob.Tests/NotificationTests/NotificationRealtimeTests.cs`
- Modify: notification-producing services (`JobService`, `ApplicantService`, `InterviewService`, `NotificationService`, `ApplicationService`, `JobAlertService`)
- Modify: `hotjob/Program.cs`
- Modify: `hotjob/Components/Layout/NavMenu.razor`

**Interfaces:**
- Consumes: existing `Notification` rows and `AuthSession.Tokens`.
- Produces: `INotificationRealtime.Subscribe(userId, Func<Task>)`, `INotificationBroadcaster.NotifyAsync(userId, title, cancellationToken)`.

- [x] **Step 1: Write failing fan-out test (only matching user callbacks).**
- [x] **Step 2: Run notification test filter and verify failure.**
- [x] **Step 3: Implement singleton subscription registry and broadcaster; wire services and NavMenu subscription.**
- [x] **Step 4: Run notification + employer + job tests and verify pass.**
- [x] **Step 5: Commit.**

### Task 6: Align audit and verify whole branch

**Files:**
- Modify: `docs/feature-audit-2026-11-23.md`

- [x] **Step 1: Update ML, snapshot, interview, job-close and realtime rows/notes.**
- [x] **Step 2: Run full `dotnet build` and full test suite.**
- [x] **Step 3: Run `git diff --check`, commit, and report evidence.**

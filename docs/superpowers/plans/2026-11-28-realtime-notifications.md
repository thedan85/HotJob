# Realtime User Notifications Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete durable, user-owned realtime notifications for recruitment, company/member decisions, and role-targeted administrator broadcasts.

**Architecture:** Reuse Notification persistence and the existing single-process Blazor SignalR invalidation registry. Add a shared inbox, serialized circuit reconciliation, transactional event producers, and retry-safe synchronous broadcasts. Keep existing Candidate DTOs/service methods as compatibility delegates.

**Tech Stack:** .NET 10, Blazor Interactive Server, EF Core 9.0.0, Pomelo 9.0.0/MySQL, Identity, xUnit 2.9.3, SQLite 9.0.0 tests.

**Spec:** `docs/superpowers/specs/2026-11-28-realtime-notifications-design.md`, approved by the user on 2026-11-28.

## Global Constraints

- The first release runs as **one application process**.
- Do not add packages.
- Keep Notification.Title at 200 characters and Content at 2,000 characters.
- **Only JobAlert populates RelatedJobPostingId.**
- Existing Kind == null rows remain visible as legacy messages. Do not infer their kind from title text.
- Do not backfill old events or reset existing IsRead/CreatedAt values.
- Use `hotjob.DTOs.Job.PagedResponse<T>` for compatibility with NotificationFeed, not the similarly named Common type.
- Personal inbox access requires an active signed-in ApplicationUser, not an active Employer membership or an approved company.
- Resolve audiences server-side and deduplicate by ordinal UserId, not by role/profile rows.
- Broadcast audiences are exactly `All`, `Candidate`, or `Employer`; maximum 10,000 recipients, inserts in batches of 500.
- Reconcile connected authenticated circuits every 30 seconds using PeriodicTimer and TimeProvider.
- Keep existing INotificationRealtime and INotificationBroadcaster contracts; signals are invalidations, not counters.
- Keep domain changes and their notifications atomic, publish only after commit, and use TimeProvider in touched notification paths.
- Preserve current ATS/interview/company/membership permissions and state-machine rules.
- Keep JobAlert excluded from the unified badge, inbox, and read actions; its dedicated page/service stays unchanged.
- No additional public SignalR hub, email/SMS/browser push, broker, Redis, scheduled broadcasts, or user/company-specific admin targeting.
- Never weaken/delete existing assertions, clear unrelated user work, log message payloads/secrets, or hide historical notifications with a global filter.
- Generate only the approved additive migrations; applying them requires an explicitly approved disposable database. Never start Program.cs against a real/shared database during tests.
- Approval of this plan and selection of an execution method are still required. No product work has started.

## Review Focus

1. A mark-all request races with a newly inserted notification: the later row remains unread. Pin in Task 1.
2. A fake clock changes UTC time but never fires timers: reconciliation tests must exercise CreateTimer, not sleep or GetUtcNow alone. Pin in Task 3.
3. Maximum-length or Unicode job/interview fields exceed notification limits: preserve essential appointment details and a safe, visibly shortened preview. Pin in Task 5.
4. A recipient disappears between broadcast selection and insertion: all submission/notification/audit writes roll back; no partial send. Pin in Task 12.
5. Commit succeeds but its acknowledgement is lost: retry resolves the same RequestId without duplicate recipients or audit. Pin in Task 12.

---

## Execution map and shared test rules

**Plan/checklist location:** This file is the single task ledger, following the repository's existing `docs/superpowers/plans/` convention. Do not create duplicate `tasks/plan.md` or `tasks/todo.md`.

**Dependency order:** `1 -> 2 -> 3 -> 4`; `5 -> 6 -> 7 -> 8 -> 11 -> 12 -> 13 -> 14`; `5 -> 9 -> 10`. Task 14 also consumes Task 4; Task 15 follows all chains. Execute in numerical order by default because shared Program.cs, fixtures, and DbContext changes make parallel file ownership costly.

**Existing reusable test tools:**

- `hotjob/Tests/hotjob.Tests/Support/TestFixture.cs`: SQLite/Identity, SeedUserAsync, HTTP/circuit admin identity, and audit-service factories.
- `hotjob/Tests/hotjob.Tests/Support/RecordingBroadcaster.cs`: post-commit recipient recording.
- `hotjob/Tests/hotjob.Tests/Support/ResumeParsing/ManualTimeProvider.cs`: existing timer-capable clock. The simpler Support/FakeTimeProvider only overrides GetUtcNow and is insufficient for PeriodicTimer tests.
- `hotjob/Tests/hotjob.Tests/Support/ResumeParsing/RecordingLoggerProvider.cs`: capture and reject sensitive logs.
- `hotjob/Tests/hotjob.Tests/Support/AuthUiTestContext.cs`: real static Blazor renderer and event-handler invocation; extend its registration hooks, not a new rendering dependency.
- `hotjob/Tests/hotjob.Tests/AdminTests/SkillApiValidationTests.cs:124-177`: real ephemeral Kestrel host and test authentication pattern. Do not start the production entry point for API tests.

Each test step below names tests and executable assertions, not an entire test body. Seed arrangements and action names are specified alongside those assertions. Use the existing fixtures and actual services; add local helpers only to the owning test file unless a later task needs them.

For concurrent tests, use distinct DbContexts and connections to a named shared-cache SQLite database with an anchor connection, not concurrent transactions on TestFixture.Connection. Test stale expected values deterministically; verify real concurrent behavior on disposable MySQL in Task 15. Coordinate with TaskCompletionSource/barriers, never sleeps.

Before changing a constructor/interface, search every construction/implementation, including target-typed `new`. Update callers/fixtures in the same task; do not keep an old constructor that silently disables notifications.

Each task has RED, implementation, GREEN, and commit steps. Missing-type/member compilation failure is an acceptable initial RED for a new interface. For behavior changes, require the specific assertion to fail. Stage only that task's listed paths and narrowly required caller-fixture edits, inspect `git diff --cached`, then run its specified commit command. Do not commit unrelated work.

## File ownership

| Task | Owned deliverable |
| --- | --- |
| 1–2 | `DTOs/Notifications/NotificationDtos.cs`, `Services/Notifications/INotificationInboxService.cs`, `NotificationInboxService.cs`, personal controller, Candidate compatibility delegates, and registrations. |
| 3–4 | `Services/Notifications/NotificationCircuitState.cs`, `NotificationCircuitHandler.cs`, navbar/inbox/MyApplications consumers, and renderer registrations. |
| 5–8 | Kind constants, `NotificationText.cs`, existing recruiting services/repository, Interview.Revision, and its migration. |
| 9–10 | Existing company and owner/admin membership decision producers. |
| 11–14 | NotificationBroadcast model/mapping/migration, broadcast DTOs/service, admin controller/page, and audit constants. |
| 15 | Provider/browser verification evidence and rollout instructions, not extra product features. |

### Task 1: Shared, active-account-owned inbox

**Dependencies:** None.

**Files:**
- Create `hotjob/DTOs/Notifications/NotificationDtos.cs`.
- Create `hotjob/Services/Notifications/INotificationInboxService.cs` and `NotificationInboxService.cs`.
- Create `hotjob/Tests/hotjob.Tests/NotificationTests/NotificationInboxTests.cs`.

**Interfaces:**
- Consumes `ApplicationDbContext` and `INotificationBroadcaster.NotifyAsync(string userId, string title, CancellationToken cancellationToken = default)`.
- Produces `NotificationDto(int Id, string Title, string? Content, bool IsRead, DateTime CreatedAt, string? Kind)`.
- Produces `INotificationInboxService` with the four exact signatures in spec section 6: ListAsync, UnreadCountAsync, MarkReadAsync, MarkAllReadAsync. Implementation constructor takes `(ApplicationDbContext db, INotificationBroadcaster broadcaster)`.

- [ ] **Step 1: Write failing ownership, paging, and read-action tests.**

| Test | Arrange/action and assertions |
| --- | --- |
| `List_UsesUserOwnershipNotProfiles` | Seed an active Employer-only user, two same-timestamp own rows, one foreign row, one JobAlert, and one legacy null Kind. `Assert.Equal(expectedIdsDescending, result.Data!.Items.Select(x => x.Id)); Assert.Equal(3, result.Data.TotalCount);` Run the same access check for Candidate, dual-role, Admin, and rejected/inactive Employer profiles. |
| `List_ClampsWithoutOverflow` | Exercise page -1, size 0/101, page int.MaxValue, and an empty inbox. `Assert.Equal(1, first.Data!.Page); Assert.Equal(1, first.Data.PageSize); Assert.Empty(huge.Data!.Items);` |
| `Read_IsOwnedIdempotentAndExcludesJobAlert` | Read the same own row twice; try foreign/missing/JobAlert IDs. `Assert.True(second.Data!.IsRead); Assert.Equal(ServiceErrorType.NotFound, foreign.ErrorType); Assert.False(alert.IsRead);` Assert only the first mutation invalidates. |
| `AllOperations_RejectDisabledOrMissingUser` | For each operation, disable/delete the user after initial access. `Assert.Equal(ServiceErrorType.Unauthorized, result.ErrorType); Assert.Empty(push.UserIds);` |
| `MarkAll_LaterInsertRemainsUnread` | Mark all, then insert an own non-alert row after that UPDATE has completed. `Assert.Equal(1, unread.Data); Assert.False(later.IsRead); Assert.False(jobAlert.IsRead);` A repeat with no newly unread rows returns 0. |

- [ ] **Step 2: Run RED.** `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --configuration Release --filter "FullyQualifiedName~NotificationInboxTests"`; expect missing shared-inbox types.
- [ ] **Step 3: Implement the shared service.** Validate active user on all operations, apply the same non-JobAlert predicate everywhere, use stable descending order and long offsets, and use a conditional unread UPDATE for read actions. Publish after persistence only when affected rows > 0. Re-read mark-read's owned result without returning foreign data. Keep side-effect signaling outside database failure/rollback handling.
- [ ] **Step 4: Run GREEN.** Repeat Step 2; expect all new tests to pass. Also run `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --configuration Release --filter "FullyQualifiedName~NotificationRealtimeTests"`; expect the existing five registry tests to pass.
- [ ] **Step 5: Commit.** `git commit -m "feat: add a shared user-owned notification inbox"`

### Task 2: Personal API and legacy compatibility

**Dependencies:** Task 1.

**Files:**
- Create `hotjob/Controllers/NotificationsController.cs`.
- Modify `hotjob/Services/Candidate/CandidateService.cs:98-199`, `NotificationFeed.cs:11-27`, and `hotjob/Program.cs`.
- Create `hotjob/Tests/hotjob.Tests/NotificationTests/NotificationApiTests.cs`.
- Modify `hotjob/Tests/hotjob.Tests/CandidateTests/CandidateServiceTests.cs`, `MyApplicationsPageTests.cs`, `hotjob/Tests/hotjob.Tests/JobTests/CompanyVisibilityRegressionTests.cs`, and `hotjob/Tests/hotjob.Tests/Support/AuthUiTestContext.cs` only for constructor/DI changes.

**Interfaces:**
- Consumes Task 1's inbox interface and DTO.
- Produces the four exact `/api/notifications` routes and ApiResponse shapes in spec section 6.
- `CandidateService(ApplicationDbContext db, INotificationInboxService notifications)` retains every ICandidateService method.
- `NotificationFeed(INotificationInboxService inner, CircuitDbGate gate)` retains its existing ListAsync/UnreadCountAsync/MarkReadAsync/MarkAllReadAsync signatures returning MyNotificationDto where applicable.

- [ ] **Step 1: Add route-level and delegate tests.** Host real controllers on `http://127.0.0.1:0` with the existing Bearer test-scheme pattern and per-test role/ID controls. `PersonalRoutes_RequireBearerButNotCandidateRole` asserts unauthenticated 401 and Employer-only 200 for both GETs and both PUTs. `PersonalRoutes_ReturnOwnedEnvelopes` asserts `success == true`, the intended item/count payloads, and 404 for foreign/JobAlert items. `CandidateDelegates_PreserveFiveFieldDtos` asserts ID/title/content/read/time unchanged and applications still load. `DelegateMarkAll_DoesNotMarkAlerts` asserts JobAlert.IsRead remains false.
- [ ] **Step 2: Run RED.** `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --configuration Release --filter "FullyQualifiedName~NotificationApiTests|FullyQualifiedName~CandidateServiceTests"`; new URLs must return 404 before implementation.
- [ ] **Step 3: Implement routes/delegates/DI.** Apply explicit ApiController, route, and JWT Authorize attributes; extract UserId from the validated principal only. Return through ApiControllerBase. Move no application-query logic. Map shared DTOs to the existing five-field DTO in the compatibility layer; remove duplicate inbox database logic. Register the shared inbox scoped and fix every listed constructor caller.
- [ ] **Step 4: Run GREEN.** Repeat Step 2 and run `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --configuration Release --filter "FullyQualifiedName~MyApplicationsPageTests|FullyQualifiedName~CompanyVisibilityRegressionTests|FullyQualifiedName~AuthTests"`; expect no delegate/DI regressions.
- [ ] **Step 5: Commit.** `git commit -m "feat: expose notifications to every authenticated user"`

### Task 3: Circuit reconciliation and lifecycle

**Dependencies:** Tasks 1–2.

**Files:**
- Create `hotjob/Services/Notifications/NotificationCircuitState.cs` and `NotificationCircuitHandler.cs`.
- Modify `hotjob/Services/Notifications/NotificationBroadcaster.cs` and `hotjob/Program.cs`.
- Create `hotjob/Tests/hotjob.Tests/NotificationTests/NotificationCircuitTests.cs`.

**Interfaces:**
- Consumes existing realtime registry, NotificationFeed, AuthenticationStateProvider/HotJobAuthStateProvider, and timer-capable TimeProvider.
- Produces scoped `NotificationCircuitState : IAsyncDisposable` with `string? UserId`, `long Generation`, `int UnreadCount`, `bool IsStale`, `event Action? Changed`, `void RequestRefresh()`, `Task RefreshAsync(CancellationToken cancellationToken = default)`, and `void SetConnected(bool connected)`.
- `RequestRefresh` schedules/coalesces observed work without blocking a writer; `RefreshAsync` lets UI/tests await the same drain, never a separate concurrent read.
- Produces `NotificationCircuitHandler(NotificationCircuitState state)` mapping OnConnectionUpAsync/OnConnectionDownAsync/OnCircuitClosedAsync to connected state/refresh/disposal. Existing account-status handler remains registered too.

- [ ] **Step 1: Write deterministic failure-mode tests.** `Signals_RequeryCountWithoutIncrementing` seeds unread=2, sends two hints, asserts `Assert.Equal(2, state.UnreadCount);`. `DroppedHint_ReconcilesAtThirtySeconds` advances ManualTimeProvider by 29 then 1 seconds, asserting unchanged count before the tick and correct DB count afterward. `Disconnect_PausesUntilReconnect` asserts no timer read while disconnected and immediate read on reconnect. `SignalDuringRead_DrainsAgain` uses a barrier and asserts the final count, not the first read, wins. `GateHeldByWriter_DoesNotDeadlock` asserts publish completes before releasing CircuitDbGate and the refresh completes after release. `IdentitySwitch_DiscardsOldRead`, `DisabledAccount_ClearsAndSignsOut`, `TransientFailure_PreservesStaleCount`, and `Dispose_UnsubscribesAndStopsTimer` assert their named states and zero post-disposal callbacks. `Broadcaster_DoesNotLogPayloads` captures logs and asserts `Assert.DoesNotContain(secretTitle, string.Join("\\n", logs.Messages));`. `Broadcaster_ContainsCancelledRealtime` injects an INotificationRealtime whose PublishAsync throws OperationCanceledException and asserts `Assert.Null(await Record.ExceptionAsync(() => broadcaster.NotifyAsync(userId, secretTitle)));`.
- [ ] **Step 2: Run RED.** `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --configuration Release --filter "FullyQualifiedName~NotificationCircuitTests"`; expect missing circuit types and the existing title log to fail its assertion.
- [ ] **Step 3: Implement the scoped state/handler.** Subscribe before reading, handle auth-generation changes, gate all reads, maintain a dirty drain flag, and observe callback/timer errors. Pause/cancel the 30-second PeriodicTimer with circuit lifecycle; do not start it in prerender. Treat Unauthorized as clear/sign-out and transient errors as stale. Remove the broadcaster title log and explicitly contain OperationCanceledException from realtime publishing as a best-effort transport cancellation, not a failed committed write. Keep the registry's existing callback-isolation/nonwaiting behavior.
- [ ] **Step 4: Run GREEN.** Repeat Step 2 plus `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --configuration Release --filter "FullyQualifiedName~NotificationRealtimeTests|FullyQualifiedName~NotificationInboxTests"`; expect counts, lifecycle, privacy, and old registry tests to pass.
- [ ] **Step 5: Commit.** `git commit -m "feat: reconcile notification state across circuit lifecycles"`

### Task 4: Live shared inbox and navbar

**Dependencies:** Task 3.

**Files:**
- Modify `hotjob/Components/Layout/NavMenu.razor`, `hotjob/Components/Pages/Notifications.razor`, and `MyApplications.razor`.
- Modify `hotjob/Tests/hotjob.Tests/Support/AuthUiTestContext.cs` and `hotjob/Tests/hotjob.Tests/CandidateTests/MyApplicationsPageTests.cs`.
- Create `hotjob/Tests/hotjob.Tests/NotificationTests/NotificationUiTests.cs`.

**Interfaces:**
- Consumes NotificationCircuitState properties/event and the unchanged NotificationFeed method contracts.
- Produces a single authenticated-user navbar inbox entry, role-neutral `/notifications`, and live current-page list/messages refresh. Components unsubscribe/discard pending results on disposal or identity generation change.

- [ ] **Step 1: Add real-renderer tests.** `EmployerAndAdmin_SeeOneInboxLink` includes Candidate-only, Employer-only, dual-role, pending/rejected employer, and Admin principals; assert one `/notifications` link per signed-in user and none anonymously. `Inbox_RendersReturnedOrderAndUtc` asserts item positions match service order even when older rows are unread. Seed CreatedAt as `2026-11-28T09:30:00Z`; assert the visible timestamp is `28 Nov 2026, 09:30 UTC` and its time element has `datetime="2026-11-28T09:30:00.0000000Z"` (or the equivalent explicit `+00:00` value) on both notification surfaces. `MyApplications_InterviewTimeIsUtc` seeds `2026-12-01T10:30:00Z` and asserts that interview's own date label contains `10:30 UTC`, not merely that “UTC” appears somewhere on the page. `Hint_RefreshesMountedPageWithoutMarkingRead` asserts current page preserved and rows remain unread. `ReadAction_RefreshesBadgeAndOtherCircuit` asserts both same-user badges converge and another user's badge stays unchanged. `BackgroundRefresh_PreservesFocusAndCurrentPage` is also a required browser assertion in Task 15. `SignOut_ClearsMountedMessages` asserts no prior title remains in rendered HTML.
- [ ] **Step 2: Run RED.** `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --configuration Release --filter "FullyQualifiedName~NotificationUiTests|FullyQualifiedName~MyApplicationsPageTests"`; Candidate-only access and client reordering should fail.
- [ ] **Step 3: Wire consumers.** Replace navbar-owned subscription/local increments with shared state. Listen with renderer InvokeAsync and serialize list refresh through NotificationFeed. Reload only the current notification page/panel; clamp an out-of-range final page. Clear local rows immediately on auth loss, retain prior rows with a retry/stale message only on transient failures, and reject late-generation results. Render returned order, encoded text, and explicit UTC times including full MyApplications interview dates. Do not reload the entire ATS page for every reconciliation or mark rows read on refresh.
- [ ] **Step 4: Run GREEN.** Repeat Step 2 and `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --configuration Release --filter "FullyQualifiedName~AuthTests"`; expect UI and existing auth behavior to pass.
- [ ] **Step 5: Commit.** `git commit -m "feat: refresh the shared notification inbox live"`

### Checkpoint A: User-owned live inbox

- [ ] Run `dotnet build hotjob/hotjob.csproj --configuration Release`; expect exit 0.
- [ ] Run `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --configuration Release`; expect no failed or newly skipped tests.
- [ ] Verify Tasks 1–4 meet spec acceptance groups 1, 7, and 8; record any environment-only browser checks for Task 15.

### Task 5: Typed events and bounded generated text

**Dependencies:** None; keep numerical order to avoid shared-file conflicts.

**Files:**
- Modify `hotjob/Models/Notification.cs`.
- Create `hotjob/Services/Notifications/NotificationText.cs`.
- Create `hotjob/DTOs/Common/TrimmedMaxLengthAttribute.cs`.
- Create `hotjob/Tests/hotjob.Tests/NotificationTests/NotificationTextTests.cs`.

**Interfaces:**
- Produces every exact NotificationKinds constant in spec section 4.
- Produces internal static `string NotificationText.Title(string text)`, `string NotificationText.Content(string text)`, and `string NotificationText.InterviewContent(string kind, string jobTitle, Interview interview)`.
- Title/Content bound **generated** text only; manual/admin input is validated rather than truncated. InterviewContent budgets essential fields first and uses the spec's exact full-details sentence for an oversized location.
- Produces `TrimmedMaxLengthAttribute(int length) : MaxLengthAttribute` with `public override bool IsValid(object? value)`: pass a string's trimmed value to the base validator without mutating the DTO. Null retains standard MaxLength behavior; Required handles mandatory fields. This makes automatic MVC and Blazor validation agree with service-level trimmed limits.

- [ ] **Step 1: Add formatter tests.** `Interview_ContainsRequiredDetailsInUtc` asserts round, type, status, job, UTC appointment, location, and notes where they fit. `Interview_MaximumFieldsHaveBoundedPreview` passes a 2,048-character location, 2,000-character notes, and maximum job label; assert `Assert.InRange(content.Length, 1, 2000); Assert.Contains("Full interview details are available in Applications.", content);` and the original full fields remain unchanged. `GeneratedText_DoesNotSplitSurrogatePairs` puts an emoji across a truncation boundary and asserts no isolated surrogate remains. `TitlesAndLegacyKind_AreBoundedAndStable` asserts generated title <=200 and `Assert.Equal("JobAlert", NotificationKinds.JobAlert);`. `TrimmedMaxLength_ValidatesNormalizedLength` asserts `Assert.True(new TrimmedMaxLengthAttribute(200).IsValid(new string('a', 200) + " ")); Assert.False(new TrimmedMaxLengthAttribute(200).IsValid(" " + new string('a', 201) + " "));` and equivalent 2,000/2,001-content cases.
- [ ] **Step 2: Run RED.** `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --configuration Release --filter "FullyQualifiedName~NotificationTextTests"`; expect missing formatter/constants.
- [ ] **Step 3: Implement small formatters and the shared validation attribute.** Use invariant, explicitly UTC date text. Omit/shorten optional notes and labels before truncating a location preview, reserving room for its exact full-details sentence. Keep generated content as plain text, not HTML or a new URL-link scheme. TrimmedMaxLength must validate without silently truncating or changing raw input; keep the normal model's storage MaxLength attributes unchanged.
- [ ] **Step 4: Run GREEN.** Repeat Step 2; expect all length, content, and Unicode cases to pass.
- [ ] **Step 5: Commit.** `git commit -m "feat: define typed notification events and bounded content"`

### Task 6: Application submission and manual messages

**Dependencies:** Tasks 1, 5.

**Files:**
- Modify `hotjob/Services/Candidate/ApplicationService.cs:176-202`, `hotjob/Services/Employer/NotificationService.cs`, and `hotjob/Controllers/EmployerApplicantsController.cs:70-86`.
- Modify only SendNotificationRequest's length annotations in `hotjob/DTOs/Employer/ApplicantDtos.cs` to use Task 5's TrimmedMaxLength.
- Modify `hotjob/Components/Pages/Employer/Interviews.razor:491-503,780-815`.
- Modify `hotjob/Tests/hotjob.Tests/EmployerTests/EmployerAtsTests.cs` for its Notifier constructor.
- Create `hotjob/Tests/hotjob.Tests/NotificationTests/RecruitmentNotificationTests.cs`; extend `hotjob/Tests/hotjob.Tests/CandidateTests/ApplicationServiceTests.cs` for recipient exclusions.
- Extend `hotjob/Tests/hotjob.Tests/NotificationTests/NotificationApiTests.cs` for the manual-message padded-input route test, reusing that file's existing real Kestrel host.

**Interfaces:**
- Consumes Task 5 constants/text helpers and existing ApplyAsync/NotifyAsync contracts.
- `NotificationService(IApplicantRepository repo, TimeProvider timeProvider, INotificationBroadcaster? broadcaster = null)` replaces its current constructor.
- Add `Task<bool> IApplicantRepository.IsUserActiveAsync(string userId, CancellationToken cancellationToken = default)` in `hotjob/Repositories/Employer/IApplicantRepository.cs` and `ApplicantRepository.cs`; query active ApplicationUser without loading private profile data. Tasks 7–8 reuse it.
- Produces ApplicationSubmitted and EmployerMessage rows with null RelatedJobPostingId and no new public send endpoint.

- [ ] **Step 1: Add producer tests.** `Apply_NotifiesDistinctEligibleCompanyEmployers` includes owner/member, inactive employer, pending/rejected member, disabled user, and other-company HR; assert the exact recipient set and Kind == ApplicationSubmitted. `DuplicateApply_DoesNotCreateMoreNotifications` asserts original counts unchanged. `ManualMessage_ValidatesTrimmedLimitsAndOwnership` asserts 200/2,000 pass, 201/2,001 and blank title fail, blank optional content becomes null, foreign application is NotFound, and a disabled candidate gives Conflict with no message/push. `ManualMessage_PaddedLimitsAgreeAcrossRouteAndService` posts a 200-character title/2,000-character content each surrounded by spaces to the real notify URL and invokes the service with the same input: assert both succeed and persist trimmed values; normalized 201/2,001 fail in both paths. `ManualMessage_UsesClockAndDoesNotExposeCv` asserts exact fake timestamp and no CV URL/snapshot in generated application messages. `CommittedApplyAndManualMessage_CancelledRealtimeStillSucceed` uses the real NotificationBroadcaster over an INotificationRealtime that throws OperationCanceledException and asserts `Assert.True(result.Success);` plus the original committed row count. Preserve existing transaction-rollback tests and assert no push on rollback.
- [ ] **Step 2: Run RED.** `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --configuration Release --filter "FullyQualifiedName~RecruitmentNotificationTests|FullyQualifiedName~ApplicationServiceTests|FullyQualifiedName~NotificationApiTests"`; expect missing kinds/eligibility checks and the raw-length route/service mismatch.
- [ ] **Step 3: Finish these existing producers.** Retain the application/history transaction. Add user/employer activity filters and typed bounded content. Validate manual title/content inside the service and replace raw DTO MaxLength validators with TrimmedMaxLength so automatic REST validation accepts the same normalized limits. Keep Required and the existing global request-size protections; never disable ApiController validation globally. If the owned application's candidate user is unavailable, return Conflict with “Candidate account is unavailable.” rather than a successful response for an unsent message. Use TimeProvider and send only after persistence. Move post-commit signaling out of rollback catch scopes. Add the Swagger timeout/non-idempotent warning and honest unknown-result send-form feedback; no automatic resend or candidate-inbox access.
- [ ] **Step 4: Run GREEN.** Repeat Step 2 and `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --configuration Release --filter "FullyQualifiedName~EmployerAtsTests"`; expect existing ATS/manual behavior unchanged except specified notification fixes.
- [ ] **Step 5: Commit.** `git commit -m "feat: complete application and employer message notifications"`

### Task 7: Race-safe ATS status and job-close events

**Dependencies:** Tasks 5–6.

**Files:**
- Modify `hotjob/Repositories/Employer/IApplicantRepository.cs`, `ApplicantRepository.cs`, `hotjob/Services/Employer/ApplicantService.cs`, and `hotjob/Services/Job/JobService.cs:214-275`.
- Create `hotjob/Tests/hotjob.Tests/NotificationTests/ApplicationStatusNotificationTests.cs`.
- Update constructor callers in `hotjob/Tests/hotjob.Tests/EmployerTests/EmployerAtsTests.cs`, `EmployerGuardTests.cs`, `hotjob/Tests/hotjob.Tests/AdminTests/AdminModerationTests.cs`, and `hotjob/Tests/hotjob.Tests/JobTests/CompanyVisibilityRegressionTests.cs`.

**Interfaces:**
- Add `Task<int> IApplicantRepository.TryUpdateApplicationStatusAsync(int companyProfileId, int applicationId, ApplicationStatus expectedStatus, ApplicationStatus targetStatus, CancellationToken cancellationToken = default)`.
- The method conditionally updates only the owned row with the expected status and returns affected count; synchronize/detach only that target's tracked entry so later SaveChanges cannot overwrite it.
- `ApplicantService(IApplicantRepository repo, ILogger<ApplicantService> logger, TimeProvider timeProvider, INotificationBroadcaster? broadcaster = null)`; existing IApplicantService methods remain unchanged.

- [ ] **Step 1: Add status/race tests.** `EveryAllowedStatusTransition_NotifiesOnce` covers the existing forward/reject matrix and fake-clock history; assert Kind == ApplicationStatusChanged and the intended candidate user. `SameOrInvalidStatus_DoesNotNotify` asserts unchanged counts. `StaleExpectedStatus_ReturnsConflictWithoutHistoryOrPush` executes the losing predicate against a changed row; assert affected ==0, Conflict, no extra rows. `CloseJob_RejectsOnlyOpenApplicationsAtomically` covers Pending/Viewed/Shortlisted/Interviewing versus Offered/Rejected/Withdrawn. `CloseRacingOffer_RollsBackEntireClose` forces an application status change after selection and before CAS; assert the offer survives and job/history/notifications retain pre-close values. `DisabledCandidate_StatusStillChangesWithoutNotification` asserts domain operation succeeds with no recipient row. `CommittedStatusAndClose_CancelledRealtimeStillSucceed` repeats Task 6's real-broadcaster/cancelled-realtime arrangement and asserts committed results remain successful.
- [ ] **Step 2: Run RED.** `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --configuration Release --filter "FullyQualifiedName~ApplicationStatusNotificationTests"`; existing unconditional writes must fail race assertions.
- [ ] **Step 3: Implement CAS-backed transitions.** Preserve validation/state-machine errors, perform status/history/notification work within one transaction, and use Task 6 active-user check. Wrap job close plus per-open-application guarded rejections in one transaction, matching job's original Status and each application's original CurrentStatus. On zero affected rows, roll back before returning Conflict. Do not change job update validation: it already requires closing via DELETE. Publish distinct recipients only after confirmed commit; remove rollback-after-push failure paths.
- [ ] **Step 4: Run GREEN.** Repeat Step 2 and `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --configuration Release --filter "FullyQualifiedName~EmployerAtsTests|FullyQualifiedName~EmployerJobCrudTests|FullyQualifiedName~EmployerGuardTests|FullyQualifiedName~AdminModerationTests"`; expect transition/close behavior and guards to remain valid.
- [ ] **Step 5: Commit.** `git commit -m "fix: make application status notifications atomic under races"`

### Task 8: Full interview events with revision guards

**Dependencies:** Tasks 5–7.

**Files:**
- Modify `hotjob/Models/Application/Interview.cs`, `hotjob/Data/ApplicationDbContext.cs`, `hotjob/Repositories/Employer/IApplicantRepository.cs`, `ApplicantRepository.cs`, and `hotjob/Services/Employer/InterviewService.cs`.
- Generate `AddInterviewNotificationRevision` migration and designer in `hotjob/Migrations/`; modify `ApplicationDbContextModelSnapshot.cs`. EF owns the timestamp prefix.
- Create `hotjob/Tests/hotjob.Tests/NotificationTests/InterviewNotificationTests.cs`; retain/extend `hotjob/Tests/hotjob.Tests/DataTests/InterviewRoundModelTests.cs`.

**Interfaces:**
- Produces internal stored `Interview.Revision : long`, default 0; no DTO requirement changes.
- Add `Task<int> IApplicantRepository.TryUpdateInterviewAsync(int companyProfileId, int interviewId, long expectedRevision, Interview replacement, CancellationToken cancellationToken = default)`. Update only Round/InterviewDate/LocationOrLink/InterviewType/Notes/Status and Revision == expectedRevision+1; never trust replacement's keys/ownership/revision.
- Consumes Task 7 application CAS for the existing Shortlisted-to-Interviewing auto-advance and Task 6 recipient activity check.

- [ ] **Step 1: Add interview tests.** `Schedule_ContainsDetailsAndAutoAdvancesWithOneNotification` asserts exact round/type/time/location/notes and one InterviewScheduled row. `Schedule_PendingAndViewedRetainStatus` preserves current rules; terminal applications fail. `IdenticalNormalizedUpdate_IsSilent` asserts Revision unchanged. `RealUpdate_AdvancesRevisionAndNotifiesOnce` asserts Revision +1 and resulting details. `CancelViaEitherEndpoint_UsesCancelledKindOnce` tests both methods and repeats. `ConcurrentSameRevision_HasOneWinner` asserts one affected row/event and one Conflict across identical updates, update-versus-cancel, and double cancel. `MaximumInterviewFields_AreStoredFullyWithBoundedNotification` reuses Task 5 boundary values. `NotificationSaveFailure_RollsBackInterviewAndHistory` asserts Revision/status/history unchanged and no push.
- [ ] **Step 2: Run RED.** `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --configuration Release --filter "FullyQualifiedName~InterviewNotificationTests"`; expect missing Revision/CAS and old duplicate-update behavior.
- [ ] **Step 3: Implement the revision-backed path and migration.** Generate with `dotnet ef migrations add AddInterviewNotificationRevision --project hotjob/hotjob.csproj --startup-project hotjob/hotjob.csproj`; inspect only the default-0 column/model change. Normalize and compare candidate-visible values before deciding an event; use a detached replacement snapshot. Place guarded update/history/notification persistence in one transaction, emit only InterviewCancelled for cancellation through UpdateAsync, and use the formatter. Preserve full stored fields and existing permission/eligibility errors. Publish after commit outside rollback scopes.
- [ ] **Step 4: Run GREEN.** Repeat Step 2 and `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --configuration Release --filter "FullyQualifiedName~EmployerAtsTests|FullyQualifiedName~InterviewRoundModelTests"`; expect all new and existing interview cases to pass. Generate SQL with `dotnet ef migrations script 20261126154350_RestoreLegacyCompanyAndEmployerActivity AddInterviewNotificationRevision --project hotjob/hotjob.csproj --startup-project hotjob/hotjob.csproj`; inspect additive default-0 SQL without applying it.
- [ ] **Step 5: Commit.** `git commit -m "feat: deliver complete race-safe interview notifications"`

### Checkpoint B: Recruiting lifecycle

- [ ] Run `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --configuration Release`; expect no failed/newly skipped tests.
- [ ] Confirm spec event rows ApplicationSubmitted, ApplicationStatusChanged, InterviewScheduled/Updated/Cancelled, and EmployerMessage all have producing tests.
- [ ] Confirm no new non-JobAlert producer assigns RelatedJobPostingId; repeat events for the same job must not violate the old unique index.

### Task 9: Company decision notifications

**Dependencies:** Task 5.

**Files:**
- Modify `hotjob/Services/Admin/CompanyAdminService.cs` and `hotjob/Tests/hotjob.Tests/Support/TestFixture.cs`.
- Create `hotjob/Tests/hotjob.Tests/NotificationTests/CompanyDecisionNotificationTests.cs`.

**Interfaces:**
- Keep ICompanyAdminService methods unchanged.
- Constructor becomes `(ApplicationDbContext dbContext, IAdminAuditService auditService, ILogger<CompanyAdminService> logger, TimeProvider timeProvider, INotificationBroadcaster? broadcaster = null)`.
- Existing fixture factory passes TimeProvider.System; new tests instantiate with their fixed clock/RecordingBroadcaster.

- [ ] **Step 1: Add company-decision tests.** `Decisions_NotifyOnlyOwnerAndActiveMembers` exercises approve, reject/revoke, suspend, and reactivate, excluding pending/rejected/inactive employers, disabled users, and another company. Assert exact kind/company/reason, distinct recipient IDs, timestamp, and no non-alert job FK. `Suspension_RejectionDoNotFilterOutTheirOwnAudience` asserts delivery despite resulting company ineligibility. `ReactivateRejectedCompany_DoesNotClaimApproval` asserts correct wording and unchanged rejection status. `SameStateAndStaleExpectedState_DoNotNotify` asserts Conflict with unchanged row/audit counts. `AuditFailure_RollsBackCompanyAndMessages` asserts no push and original company state. `CompanyReason_IsAbsentFromOrdinaryLogs` captures the service logger and asserts `Assert.DoesNotContain(privateReason, string.Join("\\n", logs.Messages));` while the authorized audit/message still contains the reason.
- [ ] **Step 2: Run RED.** `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --configuration Release --filter "FullyQualifiedName~CompanyDecisionNotificationTests"`; existing methods create no notification.
- [ ] **Step 3: Add rows to existing audited transactions.** Query distinct eligible UserIds, conditionally update expected Status/IsActive with ExecuteUpdateAsync, preserve all verifier/deactivation fields, and add generated bounded messages plus existing audit in the same AdminWriteTransaction. Inside its callback, throw DbUpdateConcurrencyException for a zero-row guarded write; let the helper roll back, then map that exception to Conflict outside the helper. Keep an untouched target snapshot for content; prevent stale tracking overwrites. Publish only after RunAsync confirms commit. Do not create a separate CompanyReactivated event when approval merely also sets IsActive true. Remove rejection-reason text from this service's ordinary logger output; preserve the reason in the authorized audit record and recipient message.
- [ ] **Step 4: Run GREEN.** Repeat Step 2 and `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --configuration Release --filter "FullyQualifiedName~AdminModerationTests|FullyQualifiedName~AdminAuditHooksTests"`; expect all existing admin/audit cases to remain valid.
- [ ] **Step 5: Commit.** `git commit -m "feat: notify company members about moderation decisions"`

### Task 10: Owner and admin membership decisions

**Dependencies:** Tasks 5, 9.

**Files:**
- Modify `hotjob/Services/Employer/CompanyService.cs`, `hotjob/Services/Admin/EmployerAdminService.cs`, and `hotjob/Tests/hotjob.Tests/Support/TestFixture.cs`.
- Modify employer-service constructor calls in `hotjob/Tests/hotjob.Tests/CompanyTests/CompanyServiceTests.cs:393-530`.
- Create `hotjob/Tests/hotjob.Tests/NotificationTests/MembershipNotificationTests.cs`.

**Interfaces:**
- Existing owner/admin public membership methods stay unchanged.
- Employer CompanyService constructor appends `(TimeProvider timeProvider, INotificationBroadcaster? broadcaster = null)` after existing db/logger.
- EmployerAdminService constructor appends the same dependencies after existing db/audit/logger.

- [ ] **Step 1: Add both producer-path tests.** `OwnerAndAdminDecisions_NotifyTheAffectedUser` covers approve/reject through each path; assert one MembershipApproved/Rejected row with company and supplied reason only where supported. `AdminRejectedMember_CanStillReadRejection` asserts Employer.IsActive false, ApplicationUser.IsActive true, and Task 1 inbox returns the message. `OwnerCannotDecideForeignSelfOrOwnerMember` preserves existing refusal categories and zero side effects. `ConcurrentOwnerAdminDecision_HasOneWinner` asserts one final state/notification, losing Conflict, and admin audit only if the admin wins. `AuditFailure_RollsBackMemberAndNotification` asserts full admin rollback. `MembershipReason_IsAbsentFromOrdinaryLogs` asserts private rejection text stays out of logger output. Disabled ApplicationUsers receive no notification while permitted domain decisions still persist.
- [ ] **Step 2: Run RED.** `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --configuration Release --filter "FullyQualifiedName~MembershipNotificationTests"`; both current paths lack notification rows.
- [ ] **Step 3: Add conditional writes and recipient rows atomically.** Owner path gains a local transaction; admin path stays inside its existing audited transaction. Match original MembershipStatus and IsActive in ExecuteUpdateAsync, preserve existing admin rejection/deactivation effects, and leave owner reject's request shape unchanged. The admin callback follows Task 9's DbUpdateConcurrencyException rollback/mapping rule; owner path explicitly rolls back before Conflict. Use the affected UserId despite resulting inactive Employer profile, gated only by active ApplicationUser. Return correct updated member data without stale tracked writes. Publish after commit, not inside audit/rollback handling. Keep rejection reasons in the authorized audit/message, not ordinary service logs.
- [ ] **Step 4: Run GREEN.** Repeat Step 2 and `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --configuration Release --filter "FullyQualifiedName~CompanyServiceTests|FullyQualifiedName~EmployerCompanyControllerTests|FullyQualifiedName~AdminModerationTests|FullyQualifiedName~AdminAuditHooksTests"`; expect owner/admin authorization and audit regressions absent.
- [ ] **Step 5: Commit.** `git commit -m "feat: notify users about membership decisions"`

### Checkpoint C: Decision coverage

- [ ] Run `dotnet build hotjob/hotjob.csproj --configuration Release` and `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --configuration Release`; require exit 0.
- [ ] Verify all Company/Membership kinds in the spec event matrix have positive, refused-recipient, and rollback tests.
- [ ] Confirm no changes to admin employer deactivation/reactivation behavior or new notifications for excluded events.

### Task 11: Broadcast persistence and unique intent claims

**Dependencies:** Task 8 for migration order.

**Files:**
- Create `hotjob/Models/NotificationBroadcast.cs`.
- Modify `hotjob/Models/Notification.cs` and `hotjob/Data/ApplicationDbContext.cs`.
- Generate `AddNotificationBroadcasts` migration/designer in `hotjob/Migrations/`; update `ApplicationDbContextModelSnapshot.cs`.
- Create `hotjob/Tests/hotjob.Tests/NotificationTests/NotificationBroadcastModelTests.cs`.

**Interfaces:**
- Produces NotificationBroadcast's exact Id/AdminUserId/RequestId/Audience/Title/Content/RecipientCount/CreatedAt fields and limits from spec section 5.
- Produces `Notification.BroadcastId : int?` and navigation `NotificationBroadcast? Broadcast`.
- Produces `DbSet<NotificationBroadcast> NotificationBroadcasts`; unique `(AdminUserId, RequestId)`, unique `(UserId, BroadcastId)`, restrictive broadcast FK, and `(UserId, CreatedAt, Id)`.

- [ ] **Step 1: Add relational model tests.** `BroadcastIntent_IsUniquePerAdmin` asserts a same-admin/key second insert throws DbUpdateException but a different admin/key combination succeeds. `Recipient_IsUniquePerBroadcastButNullKeysRepeat` asserts duplicate copies fail, null-BroadcastId ordinary rows coexist, and same-user/same-job non-alert rows do not collide. `LegacyAlertUniquenessAndUserCascade_ArePreserved` asserts JobAlert protection remains, deleting a recipient cascades only their copies, and deleting a referenced submission is restricted. Model metadata assertions pin field lengths and Interview.Revision's unchanged default.
- [ ] **Step 2: Run RED.** `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --configuration Release --filter "FullyQualifiedName~NotificationBroadcastModelTests"`; expect missing model fields.
- [ ] **Step 3: Implement additive mappings and migration.** Run `dotnet ef migrations add AddNotificationBroadcasts --project hotjob/hotjob.csproj --startup-project hotjob/hotjob.csproj`. Do not add a cascading actor FK or alter the existing JobAlert index. Inspect that the migration changes only the broadcast table, nullable FK, and described indexes.
- [ ] **Step 4: Run GREEN.** Repeat Step 2 and `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --configuration Release --filter "FullyQualifiedName~JobAlertTests|FullyQualifiedName~InterviewRoundModelTests"`. Run `dotnet ef migrations script AddInterviewNotificationRevision AddNotificationBroadcasts --project hotjob/hotjob.csproj --startup-project hotjob/hotjob.csproj`; inspect additive SQL without applying.
- [ ] **Step 5: Commit.** `git commit -m "feat: persist broadcast submissions and recipient uniqueness"`

### Task 12: Authorized, retry-safe broadcast transaction

**Dependencies:** Tasks 5, 11.

**Files:**
- Create `hotjob/DTOs/Notifications/BroadcastDtos.cs`.
- Create `hotjob/Services/Admin/IAdminBroadcastService.cs` and `AdminBroadcastService.cs`.
- Modify `hotjob/Models/Admin/AdminAuditLog.cs` for action/target constants and filter lists only.
- Create `hotjob/Tests/hotjob.Tests/NotificationTests/AdminBroadcastTests.cs` and `BroadcastTransactionTests.cs`.
- Modify `hotjob/Tests/hotjob.Tests/Support/TestFixture.cs` to support a named shared-cache database with a separate anchor connection.

**Interfaces:**
- Produces mutable `CreateBroadcastRequest` with Guid RequestId and string Audience/Title/Content, defaults Guid.Empty/`All`/empty/empty; add Required and Task 5's TrimmedMaxLength annotations plus the same service-level validation.
- Produces `BroadcastResultDto(int BroadcastId, int RecipientCount, DateTime CreatedAt)`.
- Produces `Task<ServiceResult<BroadcastResultDto>> IAdminBroadcastService.SendAsync(CreateBroadcastRequest request, CancellationToken cancellationToken = default)`.
- AdminBroadcastService constructor takes `(IServiceScopeFactory scopes, IAdminAuditService auditService, TimeProvider timeProvider, INotificationBroadcaster broadcaster, ILogger<AdminBroadcastService> logger)`.
- Capture actor from the caller's audit identity once, then create an isolated async DI scope for DbContext and the transaction. It permits safe failed-claim tracker clearing without discarding unrelated circuit-tracked changes. No user-supplied AdminUserId and no new IAdminAuditService API.
- Extend TestFixture's existing factory to `Task<TestFixture> CreateAsync(Action<IServiceCollection>? configure = null, string? sqliteConnectionString = null)`. When the optional string is supplied, hold an open anchor connection but register DbContext using UseSqlite(the connection string), not UseSqlite(the anchor object). Existing callers keep the old isolated in-memory behavior.
- All multi-scope broadcast tests create one `$"Data Source=notification-broadcast-{Guid.NewGuid():N};Mode=Memory;Cache=Shared"` string per test and pass it to that factory; every DbContext opens its own connection to that same named database. An isolated DI scope alone isolates tracking, not the existing fixture's connection.

- [ ] **Step 1: Add input/audience/security tests.** `Send_ValidatesExactAudienceAndTrimmedLimits` tests Guid.Empty, `candidate`/unknown audiences, whitespace, 200/201 title, 2,000/2,001 content; assert Validation and no rows. `Send_RequiresActiveDatabaseAdmin` tests anonymous, non-admin, removed DB role, disabled actor, and missing email; assert Unauthorized. `Audiences_AreDatabaseRoleSnapshots` covers all three audiences, role-only users, dual-role users, pending/rejected memberships, disabled accounts, and an empty role group; assert exact UserIds and one copy each. `RecipientLimit_IsAtomic` proves 10,000 succeeds, 10,001 conflicts, and 500-row batches stay inside one transaction.
- [ ] **Step 2: Add retry/failure tests.** `SameKey_ReplaysOriginalResultWithoutReselecting` changes current audience membership before retry and asserts original result/count, one audit, and no extra push. `SameKeyChangedPayload_IsConflict` and `SeparateAdmins_HaveIndependentKeys` pin key scoping. `ConcurrentSameKey_ClaimsOnce` coordinates separate connections and asserts one committed fan-out, with an original result or safe in-flight Conflict for the other caller. `RecipientForeignKeyFailure_RollsBackEverything` injects one absent UserId into a pending notification through a test SaveChangesInterceptor, simulating a recipient lost after selection; assert no submission/recipient/audit/push. SQLite serializes writers, so do not attempt a second-connection delete while its broadcast write transaction holds the lock; exercise the real deletion race on MySQL in Task 15. `AuditInsertFailure_RollsBackEverything` asserts the same rollback. `IdentitySwitchAfterCapture_KeepsOriginalActor` asserts both submission and audit use the captured UserId/email. `CommitAcknowledgementLost_ResolvesBySameKey` injects a commit interceptor that throws after the DB commit and asserts retry resolves one submission/audit/copy set. `PostCommitPushFailure_DoesNotRollback` includes OperationCanceledException and asserts successful persisted result. `FailedClaim_DoesNotClearCallerTrackedEntity` asserts a modified unrelated entity in the circuit scope remains modified.
- [ ] **Step 3: Run RED.** `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --configuration Release --filter "FullyQualifiedName~AdminBroadcastTests|FullyQualifiedName~BroadcastTransactionTests"`; expect missing service/types.
- [ ] **Step 4: Implement SendAsync and the named-connection test-fixture mode.** Follow spec section 7's exact transaction/replay order. Claim by a unique INSERT, never check-then-insert; catch only the claim's actual unique-constraint violation for replay, not an arbitrary DbUpdateException. Snapshot at most 10,001 distinct active recipient IDs, persist batches of 500 in that same transaction, and add the captured-actor AdminAuditLog directly. Replay from a clean isolated context after rolling back a failed claim; compare normalized strings with ordinal equality. A recipient FK failure is a rollback, not a dedup/replay success. Treat lost commit acknowledgement as unknown; never substitute a new key or return a false “nothing sent” result. Signal after confirmed commit outside rollback logic, retaining the original result despite push failure. Do not log payloads or call mutable-identity LogAsync. The optional fixture mode must register the connection string, hold the anchor until disposal, and leave all existing fixture callers unchanged.
- [ ] **Step 5: Run GREEN.** Repeat Step 3 plus `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --configuration Release --filter "FullyQualifiedName~NotificationBroadcastModelTests|FullyQualifiedName~AdminAuditHooksTests"`; expect atomicity, actor, replay, and old audit tests to pass.
- [ ] **Step 6: Commit.** `git commit -m "feat: send transactional retry-safe admin broadcasts"`

### Task 13: Admin broadcast API

**Dependencies:** Task 12.

**Files:**
- Create `hotjob/Controllers/Admin/AdminNotificationsController.cs`.
- Modify `hotjob/Program.cs` for scoped IAdminBroadcastService registration.
- Extend `hotjob/Tests/hotjob.Tests/NotificationTests/NotificationApiTests.cs`.

**Interfaces:**
- Consumes SendAsync and Task 12 DTOs.
- Produces `POST /api/admin/notifications/broadcasts`, raw BroadcastResultDto on 200, existing admin error-array convention, and inherited Admin/Bearer protection.
- Declares explicit `[Route("api/admin/notifications")]` and action `[HttpPost("broadcasts")]`; never rely on AdminControllerBase's controller-token route.

- [ ] **Step 1: Add routed API tests.** `BroadcastRoute_RequiresAdminAndReturnsRawResult` asserts 401 anonymous, 403 authenticated non-admin, and 200 Admin with `broadcastId`, `recipientCount`, `createdAt` directly on the JSON root. `BroadcastRoute_UsesServerActorAndReplayContract` asserts same key/payload returns identical result, changed payload gives 409, and forged body fields cannot change actor. `BroadcastRoute_ValidatesBody` asserts 400 for required/length/audience/key errors with an admin error array. `BroadcastRoute_PaddedLimitsAgreeWithDirectService` asserts raw padded-to-limit 200/2,000 input succeeds and persists normalized values through both paths, while trimmed 201/2,001 fails; retrying the same key with equivalent whitespace returns the original result. Use the same real Kestrel route host as Task 2, not direct action invocation only.
- [ ] **Step 2: Run RED.** `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --configuration Release --filter "FullyQualifiedName~NotificationApiTests"`; admin URL should be 404 before controller addition.
- [ ] **Step 3: Implement the thin controller/DI.** Forward only CreateBroadcastRequest and cancellation to the service; return OkOrFailure. Use TrimmedMaxLength instead of raw DTO MaxLength so pre-action validation agrees with SendAsync; keep Required. Preserve the existing global validation factory's admin error arrays without changing unrelated API behavior. Do not accept recipient/actor IDs.
- [ ] **Step 4: Run GREEN.** Repeat Step 2 and `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --configuration Release --filter "FullyQualifiedName~SkillApiValidationTests|FullyQualifiedName~ApiControllerBaseTests"`; expect admin routing and existing envelope tests to pass.
- [ ] **Step 5: Commit.** `git commit -m "feat: expose the authorized admin broadcast endpoint"`

### Task 14: Admin broadcast form with honest retry state

**Dependencies:** Tasks 4, 12–13.

**Files:**
- Create `hotjob/Components/Pages/Admin/AdminNotifications.razor`.
- Modify `hotjob/Components/Layout/NavMenu.razor` for the Admin navigation link.
- Extend `hotjob/Tests/hotjob.Tests/Support/AuthUiTestContext.cs` registration hooks if needed.
- Create `hotjob/Tests/hotjob.Tests/NotificationTests/AdminBroadcastUiTests.cs`.

**Interfaces:**
- Consumes IAdminBroadcastService.SendAsync directly through DI and CircuitDbGate; no HTTP loopback.
- Produces `/admin/notifications` and an Admin-only navbar link; personal inbox link remains distinct and single.
- Form state: editable draft -> confirmation -> pending locked payload/key -> committed result or unknown-result retry. Retry uses the same normalized payload/RequestId; deliberate new send creates a new key with the spec warning.

- [ ] **Step 1: Add real-renderer behavior tests.** `BroadcastForm_IsAdminOnlyAndValidatesLengths` asserts role access, required labels/fields, and correct limit errors. `ConfirmationAndPending_PreventDuplicateSend` asserts audience confirmation precedes SendAsync and the busy form cannot send twice. `UnknownResult_RetryRetainsPayloadAndRequestId` captures two service calls; assert matching key and values, editable inputs remain locked until retry or deliberate new intent. `Success_ShowsCommittedCountAndNewIntentUsesNewKey` asserts count, no auto-resend, and a new key only on a deliberate new send. `Payload_IsEncoded` asserts literal `<script>`/HTML is escaped in confirmation and results, not rendered as markup.
- [ ] **Step 2: Run RED.** `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --configuration Release --filter "FullyQualifiedName~AdminBroadcastUiTests"`; expect missing page.
- [ ] **Step 3: Build the form using existing styles.** Add labels, required inputs, length feedback, audience selector, explicit confirmation, disabled/busy states, and a polite success/error result. Generate one RequestId at confirmation; freeze the submitted payload across an unknown result. Offer “Retry same broadcast” or a warned deliberate new send, never automatic retry with a fresh key. Subscribe to no extra per-user realtime channel. Add only the Admin navigation entry.
- [ ] **Step 4: Run GREEN.** Repeat Step 2 and `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --configuration Release --filter "FullyQualifiedName~NotificationUiTests|FullyQualifiedName~AuthTests"`; expect no personal navbar/auth regression.
- [ ] **Step 5: Commit.** `git commit -m "feat: add the admin broadcast composer"`

### Checkpoint D: End-to-end feature assembled

- [ ] Run `dotnet build hotjob/hotjob.csproj --configuration Release`.
- [ ] Run `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --configuration Release`; expect no failed or newly skipped tests.
- [ ] Check every spec event kind, active-account policy, route, and broadcast retry condition against its task test. Missing tests block completion, not silently become manual checks.

### Task 15: Provider/browser proof and release handoff

**Dependencies:** All preceding tasks.

**Files:**
- Create `docs/testing/realtime-notifications.md` with reproducible checks and recorded outcomes.
- Modify this plan's checkboxes and the spec's implementation verification status only to reflect actual evidence.
- No new product source, dependency, test bypass, or production infrastructure.

**Interfaces:**
- Consumes both generated migrations, complete event producers, circuit state, and all personal/admin surfaces.
- Produces a verification record with build/test counts, MySQL migration/concurrency results, browser observations, unresolved environment checks, and single-process deployment limits.

- [ ] **Step 1: Run the complete local verification commands.**

```powershell
dotnet build hotjob/hotjob.csproj --configuration Release
dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --configuration Release
dotnet ef migrations script 20261126154350_RestoreLegacyCompanyAndEmployerActivity AddNotificationBroadcasts --project hotjob/hotjob.csproj --startup-project hotjob/hotjob.csproj
git diff --check
```

Require exit 0 and record actual test counts. Triage new warnings separately from existing MySqlConnector/SQLitePCLRaw advisories; this feature does not silently fix or waive dependency risks.

- [ ] **Step 2: Obtain an explicitly approved disposable MySQL database and verify migration preservation.** If unavailable, record “not run: disposable MySQL unavailable” and do not claim release readiness. Never use the default real database. Before the first migration command, require a new empty database; never downgrade a populated database. Before both commands, verify that HOTJOB_NOTIFICATION_TEST_CONNECTION is nonempty and still identifies that approved disposable database. Supply its connection via that process environment variable without printing it:

```powershell
dotnet ef database update 20261126154350_RestoreLegacyCompanyAndEmployerActivity --project hotjob/hotjob.csproj --startup-project hotjob/hotjob.csproj --connection "$env:HOTJOB_NOTIFICATION_TEST_CONNECTION"
```

On that old schema, run the following SQL in the approved database using an existing MySQL client. It supplies the Identity/profile/job/application foreign-key prerequisites directly; the SQLite TestFixture cannot seed MySQL. Keep this SQL session open for its variables, stop on any error, and roll back instead of continuing after a failed insert. Copy this fixture and its before/after outputs into `docs/testing/realtime-notifications.md` during implementation:

```sql
START TRANSACTION;
SET @verify_user = CONCAT('notification-verify-', REPLACE(UUID(), '-', ''));
SET @verify_stamp = '2026-11-28 09:30:00.000000';
INSERT INTO AspNetUsers
    (Id, CreatedAt, IsActive, UserName, NormalizedUserName,
     EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount)
VALUES (@verify_user, @verify_stamp, TRUE, @verify_user, UPPER(@verify_user),
        FALSE, FALSE, FALSE, FALSE, 0);
INSERT INTO Candidates (UserId, FullName, IsDiscoverable)
VALUES (@verify_user, 'Notification verification fixture', FALSE);
SET @verify_candidate = LAST_INSERT_ID();
INSERT INTO Resumes (CandidateId, Title, IsPrimary, CreatedAt)
VALUES (@verify_candidate, 'Verification resume', TRUE, @verify_stamp);
SET @verify_resume = LAST_INSERT_ID();
INSERT INTO CompanyProfiles (CompanyName, Status, IsActive)
VALUES (CONCAT('Verification company ', @verify_user), 1, TRUE);
SET @verify_company = LAST_INSERT_ID();
INSERT INTO JobPostings
    (CompanyProfileId, Title, Description, ExperienceRequired, JobType, Status, CreatedAt)
VALUES (@verify_company, 'Verification job', 'Fixture only', 0, 0, 1, @verify_stamp);
SET @verify_job = LAST_INSERT_ID();
INSERT INTO Applications
    (JobPostingId, ResumeId, CandidateId, CurrentStatus, AppliedAt)
VALUES (@verify_job, @verify_resume, @verify_candidate, 3, @verify_stamp);
SET @verify_application = LAST_INSERT_ID();
INSERT INTO Interviews
    (ApplicationId, Round, InterviewDate, LocationOrLink, InterviewType, Notes, Status)
VALUES (@verify_application, 2, '2026-12-01 10:30:00.000000',
        'Verification room', 0, 'Fixture only', 0);
SET @verify_interview = LAST_INSERT_ID();
INSERT INTO Notifications
    (UserId, Title, Content, Kind, RelatedJobPostingId, IsRead, CreatedAt)
VALUES
    (@verify_user, 'Legacy unread', 'Fixture only', NULL, NULL, FALSE, @verify_stamp),
    (@verify_user, 'Legacy read', 'Fixture only', NULL, NULL, TRUE, @verify_stamp),
    (@verify_user, 'Legacy alert', 'Fixture only', 'JobAlert', @verify_job, FALSE, @verify_stamp);
COMMIT;
SELECT Id, Title, Content, Kind, RelatedJobPostingId, IsRead, CreatedAt
FROM Notifications WHERE UserId = @verify_user ORDER BY Id;
SELECT Id, ApplicationId, Round, InterviewDate, LocationOrLink, InterviewType, Notes, Status
FROM Interviews WHERE Id = @verify_interview;
```

Record those non-personal fixture outputs, then run the next migration command in the terminal:

```powershell
dotnet ef database update AddNotificationBroadcasts --project hotjob/hotjob.csproj --startup-project hotjob/hotjob.csproj --connection "$env:HOTJOB_NOTIFICATION_TEST_CONNECTION"
```

Re-run the two SELECTs in the same SQL session and compare every recorded value, then run `SELECT Revision FROM Interviews WHERE Id = @verify_interview;` and require 0. Assert legacy IDs/values/read flags survive, nullable broadcast keys coexist, duplicate non-null broadcast copies fail, JobAlert uniqueness remains, and user-delete cascade/restrictive broadcast FK behave as specified. Constraint/destructive assertions use additional fixture rows in this disposable database, not deletion of the preservation fixture or real data.

- [ ] **Step 3: Verify real MySQL races with separate connections.** For each case, coordinate two sessions to read the same initial status/revision before releasing writes: ATS status versus job close, owner versus admin membership decision, identical interview changes, and the same broadcast key. Also delete a selected broadcast recipient on a second connection before notification insertion and verify complete rollback, not partial delivery. Assert one guarded winner, no duplicate event/audit, no overwritten offer, and safe retry of an in-flight broadcast. A deadlock/lock-timeout may abort an attempt; it must not commit partial notifications or be mislabeled a successful replay. Record actual database error/HTTP outcomes and retry using the original broadcast key when applicable.
- [ ] **Step 4: Start only the approved disposable app and verify browser behavior.** Preserve existing environment values for restoration afterward. Set both DOTNET_ENVIRONMENT and ASPNETCORE_ENVIRONMENT to `NotificationVerification` and ConnectionStrings__DefaultConnection to the approved test connection. This deliberately avoids DevelopmentEnvironmentFile.Load, which adds `.env.local` after other sources and could override the safe connection. Supply non-production Jwt options explicitly because user secrets are not automatically loaded in this non-Development environment. Use no launch profile:

```powershell
dotnet run --no-launch-profile --project hotjob/hotjob.csproj --configuration Release -- --urls http://127.0.0.1:5099
```

Do not start unless the effective connection is known to be the approved disposable database. Startup applies migrations and seeds. Use separate candidate/employer/admin sessions and two tabs for one user; verify every spec event row, each broadcast audience, pending/rejected-member access, no duplicate dual-role message, correct live count/list, explicit reads across tabs, dropped-hint recovery within the next successful 30-second reconciliation, offline/reconnect recovery, UTC dates/full details, no read-on-render, HTML escaping, keyboard focus, busy/empty/error states, and role-neutral personal inbox. Real browser checks are required; static renderer tests do not prove SignalR reconnect or focus behavior.
- [ ] **Step 5: Record evidence and review the final branch.** Cross-check all ten acceptance groups in spec section 11 against named test output or actual provider/browser observations. Mark unavailable checks honestly. Read the complete diff with review skills; fix supported defects and rerun affected/full checks before stating completion. Keep the server single-process; an unapproved multi-process deployment is not supported.
- [ ] **Step 6: Commit evidence only after checks.** `git commit -m "docs: record realtime notification verification and rollout limits"`

## Self-review and handoff

- [x] Map every spec acceptance group to owning tasks: 1 -> 1–2/4; 2 -> 6–7; 3 -> 5–6/8; 4 -> 9–10; 5–6 -> 11–14; 7–8 -> 3–4; 9 -> 8/11/15; 10 -> 2–6/13–15.
- [x] Specify source/fixture reach, provider limitations, interface names/types, and exact constants without duplicating implementation bodies.
- [x] Pin all five Review Focus conditions to named tests.
- [x] Complete Hy4 Flash `high` plan review using review skills, independently verify findings, and fix supported issues.
- [ ] Obtain user review of this plan and an explicit execution-method choice before product work.

**Execution recommendation:** Native execution is the lower-cost default: tasks share fixtures, DbContext mappings, Program.cs, and recruiting repository interfaces, so sequential ownership avoids coordination conflicts. Subagent-driven execution provides fresh per-task review at additional context cost. The user's prior Hy4 reviewer request does not select an implementation method.

### Review disposition

Hy4 Flash `opencode/hy4-flash-free#high` reviewed this plan using review skills. Main-session checks confirmed raw DTO validation happened before trimmed service validation, the broadcaster excluded OperationCanceledException from its catch, and TestFixture registered one connection object across scopes.

The plan now specifies a shared trim-aware validation attribute and route/service parity tests, explicit cancellation containment with committed-producer integration assertions, and an optional named-database fixture mode that opens distinct connections. It also strengthens per-timestamp UTC assertions, supplies executable old-schema MySQL fixture SQL with all foreign-key prerequisites, and fixes the migration-chain dependency map.

Independent checking additionally accounted for SQLite's single-writer limitation: its lost-recipient test injects the actual FK failure, while the real deletion race remains an explicit MySQL check. No product source, test implementation, migration, database, or browser session was changed during plan authoring.

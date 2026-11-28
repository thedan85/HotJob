# Realtime user notifications design

Date: 2026-11-28

Status: Draft for user approval. This document authorizes no implementation.

## 1. Confirmed intent and scope

Complete persistent, realtime in-app notifications for candidates and employers. Administrators can send broadcasts to everyone or to a Candidate or Employer role group. A user with both roles receives one copy per broadcast.

The first release runs as **one application process**. Reuse the existing Notification table, business services, and Blazor Interactive Server SignalR connection. Deliver this specification first, then an implementation plan after the user approves the specification.

Included:

- Candidate application status changes, including rejection when a job closes.
- Candidate interview scheduling, updates, and cancellations, plus employer messages.
- New applications for eligible employers in the job's company.
- Company approval, rejection/revocation, suspension, and reactivation decisions.
- Membership approval/rejection by either company owners or administrators.
- Admin broadcasts, a shared personal inbox, unread counts, read actions, live refresh, and recovery after reconnect.

Excluded:

- Product implementation during this documentation task.
- Email, SMS, operating-system/browser push, interview reminders, and notification preferences.
- Multiple application processes, Redis, a broker, a distributed outbox, and an additional public SignalR hub.
- Admin broadcast targeting by specific user or company; scheduled broadcasts and a broadcast history-management UI.
- New join-request or company-review-queue alerts, candidate-withdrawal alerts, billing alerts, and job-moderation alerts.
- Changes to ATS transition rules, interview eligibility, company approval rules, or membership permissions.
- Redesigning the existing JobAlert feature, which remains separate.

### Proposed policy details for approval

The following details make the confirmed scope implementable; they are design choices, not additional user-confirmed requirements:

1. Company decisions reach the owner and active members, not pending or rejected join applicants.
2. A membership rejection remains readable even when the Employer profile becomes inactive.
3. Broadcast audiences use current Identity roles, not employer membership or company approval status.
4. A 30-second reconciliation interval repairs missed live signals without adding durable push infrastructure.
5. Broadcast creation is synchronous and transactional, with at most 10,000 recipients per submission.

## 2. Repository baseline

Read against commit `5bc6a04`; these are source observations, not claims of successful runtime testing.

| Existing code | Current behavior / gap |
| --- | --- |
| `hotjob/Services/Notifications/NotificationRealtime.cs` | Singleton, per-user in-process subscriptions; isolates failing callbacks and never awaits slow callbacks from a writer. |
| `hotjob/Services/Notifications/NotificationBroadcaster.cs` | Sends a best-effort refresh signal after persistence. It does not deliver notification content to browsers directly. |
| `hotjob/Services/Candidate/ApplicationService.cs`, `ApplyAsync` | Stores application, history, and employer notifications in one transaction; signals employers after commit. |
| `hotjob/Services/Employer/ApplicantService.cs` | Stores candidate status notifications and signals after commit. |
| `hotjob/Services/Employer/InterviewService.cs` | Stores and signals scheduled/updated/cancelled messages; currently treats supplied update fields as changes and repeats cancellation notifications. |
| `hotjob/Services/Employer/NotificationService.cs` | Sends a manual message to the candidate of an employer-owned application. |
| `hotjob/Services/Job/JobService.cs`, `DeleteOrCloseAsync` | Rejects open applications and stores candidate notifications when closing a job. |
| `hotjob/Services/Candidate/CandidateService.cs` | Inbox operations already filter by UserId, not Candidate profile; excludes JobAlert from list/count/mark-all. Single-item mark-read currently does not exclude JobAlert. |
| `hotjob/Services/Candidate/NotificationFeed.cs` | Serializes notification access through the existing per-circuit CircuitDbGate. |
| `hotjob/Components/Layout/NavMenu.razor` | Notification link/subscription is Candidate-only; increments its badge locally for every signal instead of reading the authoritative count. |
| `hotjob/Components/Pages/Notifications.razor` | Candidate-only inbox; no realtime subscription; reloads on initial authentication and explicit actions only. |
| `hotjob/Components/Pages/MyApplications.razor` | Uses the existing NotificationFeed for its messages panel. |
| `hotjob/Services/Admin/CompanyAdminService.cs` | Approve/reject/deactivate/reactivate are audited transactions, but create no notifications. |
| `hotjob/Services/Employer/CompanyService.cs`, `TransitionMemberAsync` | Owner membership decisions create no notifications. |
| `hotjob/Services/Admin/EmployerAdminService.cs`, `ApproveAsync` / `RejectAsync` | Admin membership decisions create no notifications; rejection also sets Employer.IsActive to false. |
| `hotjob/Data/ApplicationDbContext.cs` | Notification indexes include unique `(UserId, Kind, RelatedJobPostingId)`, originally for JobAlert deduplication. |

## 3. Architecture decision

Three approaches were considered:

1. **Extend existing circuit notifications — selected.** Smallest change for the approved single-process Blazor deployment. Database rows are durable; SignalR carries UI refreshes through the existing circuit.
2. Add a dedicated authenticated notification hub and browser client. Appropriate for independent web/mobile clients, but duplicates the transport and authentication integration needed here.
3. Add an outbox worker plus distributed fan-out. Appropriate for durable delivery attempts across processes, but unnecessary for a persistent inbox with best-effort live hints.

The feature is one notification subsystem. Business domains remain its producers, not separate new subsystems.

```text
Validated business action
  -> business mutation + notification rows (+ existing audit) in one DB transaction
  -> commit
  -> best-effort per-user invalidation
  -> circuit queues a serialized refresh
  -> inbox/count read from DB
  -> Blazor renders changes over its existing SignalR connection
```

No hub method lets a browser choose another user's subscription or send a notification. No JWT query-string token plumbing is required.

### Responsibilities

- `INotificationInboxService` / `NotificationInboxService`, under `Services/Notifications`: user-owned list/count/read operations and active-account checks.
- Existing `Services/Candidate/NotificationFeed`: retain as the circuit-gated facade; redirect its existing methods to the shared inbox service without breaking current callers.
- `NotificationCircuitState`, under `Services/Notifications`: one scoped invalidation subscription and reconciliation loop per authenticated circuit. Exposes change notifications to the navbar and any mounted inbox/messages components.
- `NotificationCircuitHandler`: connection up/down/closed hooks; request refresh on connection up, pause reconciliation while disconnected, and cancel work on closure.
- Existing realtime registry/broadcaster: retain its interfaces and best-effort, nonblocking fan-out. A signal means **data changed**, not **increment unread**. Remove the broadcaster's current title-bearing debug log; log only the target UserId and a static invalidation message there. Producers may log kind/ID when available, never the title/content.
- Domain services: construct their event's Notification rows inside their existing unit of work. Do not introduce a second database transaction through a generic notification sender.
- `IAdminBroadcastService` / `AdminBroadcastService`: authenticated admin validation, audience selection, retry-safe transactional fan-out, and auditing.

Blazor calls services directly through DI. REST controllers expose the same business operations without HTTP loopback from components.

## 4. Event and recipient contract

Add the following string constants to `NotificationKinds`; keep the existing `"JobAlert"` value unchanged.

| Kind | Trigger | Recipients and content |
| --- | --- | --- |
| `ApplicationSubmitted` | A new application commits | Distinct eligible employers in the job's company. Include candidate name and job title, but no CV file URL or CV snapshot. |
| `ApplicationStatusChanged` | A real employer status transition, or rejection from job closure | The application's candidate user. Include job title, new status, and the candidate-facing note/reason already intended for this flow. |
| `InterviewScheduled` | An interview round is created | Candidate user. Include job title, round, date/time, interview type, location/link when present, and candidate-visible notes. |
| `InterviewUpdated` | A candidate-visible interview value actually changes, other than cancellation | Candidate user. Show the resulting round, date/time, type, location/link, candidate-visible notes, and status, not merely a generic “updated” message. |
| `InterviewCancelled` | An interview changes to Cancelled through either update or cancel | Candidate user. Include job title, round, and the cancelled appointment time. |
| `EmployerMessage` | Employer sends a valid manual message for an owned application | Candidate user. Preserve the supplied title/content after validation. |
| `CompanyApproved` | Company changes to Approved | Owner plus active company members; identify company and approval. |
| `CompanyRejected` | Company changes to Rejected, including revoking approval | Same audience; identify company and supplied rejection reason. |
| `CompanySuspended` | Company IsActive changes to false | Same audience; identify company and supplied reason when present. |
| `CompanyReactivated` | Company IsActive changes to true | Same audience; identify company and reactivation. Do not claim approval if Status is not Approved. |
| `MembershipApproved` | Pending membership changes to Active through owner or admin path | The affected member's user; identify company and approval. |
| `MembershipRejected` | Pending membership changes to Rejected through owner or admin path | The affected member's user; include company and reason if the existing operation supplies one. Owner rejection does not acquire a new mandatory reason field. |
| `AdminBroadcast` | Admin submits a valid broadcast | Audience snapshot defined in section 7. Preserve the admin's title and content. |

### Eligibility and duplicate behavior

- All recipients must have an existing ApplicationUser with IsActive == true. Do not create new messages for disabled/deleted accounts.
- New-application recipients must also have Employer.IsActive == true and MembershipStatus == Active in the application's company.
- Company-decision recipients must have Employer.IsActive == true and be the owner or an Active member. Do not require the company to remain Approved or active: rejection/suspension is precisely what the message announces.
- Membership decisions target the affected active ApplicationUser regardless of the resulting Employer.IsActive or MembershipStatus.
- Personal inbox access requires an active signed-in ApplicationUser, not an active Employer membership or an approved company. Historical messages remain owned by that user after role/membership changes.
- Resolve audiences server-side and deduplicate by ordinal UserId, not by role/profile rows.
- A failed validation, authorization failure, transaction rollback, unchanged value, or losing concurrent state transition produces zero new notifications.
- Preserve the current distinction between idempotent no-ops and Conflict errors; either outcome creates no extra notification.
- Concurrent owner/admin membership decisions, application/company transitions, and interview updates/cancellations use conditional compare-and-swap updates inside their transaction. Only the winning state change can insert its notification. Return the existing Conflict category for a losing state change.
- Use EF Core ExecuteUpdateAsync with the previously read state in its predicate: Application.CurrentStatus for application changes, JobPosting.Status for closing a job, CompanyProfile.Status and IsActive for company decisions, and Employer.MembershipStatus and IsActive for membership decisions. For interviews, compare the numeric Revision defined in section 5 and increment it in that same update. A zero-row result is a Conflict, with no history/notification/audit/push. Keep the changed entity synchronized with the DbContext after the conditional update; do not later save a stale tracked instance over the winner.
- Wrap the conditional write, related history/audit, and notification inserts in one transaction, including InterviewService.UpdateAsync and CancelAsync, which currently rely on a single SaveChangesAsync. Job-close rejection must guard each application's original status too: if a concurrent ATS action wins, roll back the whole close operation and return Conflict rather than rejecting a now-terminal application.
- Compare normalized interview values with stored values. An identical resubmission is silent. Cancelling an already cancelled interview is silent; cancellation through UpdateAsync emits only InterviewCancelled.
- Creating an interview for a Shortlisted application still auto-advances it to Interviewing, but emits one InterviewScheduled notification, not a second redundant status notification.
- Existing scheduling for Pending/Viewed applications remains allowed. “Accepted for interview” means a real scheduled interview, not the terminal Offered status.
- A distinct valid manual message submission is a new notification. Its existing endpoint is not made retry-idempotent in this release. Document this in the XML summary for EmployerApplicantsController.Notify so Swagger warns that automatic retries can duplicate messages. Employer send-form feedback must say the delivery result is unknown after a timeout; do not automatically retry. The employer cannot inspect the candidate's private inbox, so an uncertain resend requires a deliberate user decision.

### Content rules

- Keep Notification.Title at 200 characters and Content at 2,000 characters.
- Validate manual/admin input after trimming: title is required; admin content is required; manual content remains optional. Reject overlength input rather than silently truncating user-authored text.
- Generated titles/content must stay within storage limits even when every source field reaches its own maximum. Preserve event kind, round, status, UTC appointment time, and interview type first; shorten job/company labels and optional notes with an ellipsis when needed.
- Interview LocationOrLink allows 2,048 characters, exceeding the 2,000-character notification content limit before any other details. If the complete location/link cannot fit after omitting notes and shortening labels, include a clearly shortened text preview plus “Full interview details are available in Applications.” Do not reject an otherwise valid interview to fit its notification. The existing MyApplications interview section remains the source of full stored details; the shortened preview is not a clickable URL.
- Store dates in UTC using the registered TimeProvider. Date text must explicitly identify UTC; do not render a timezone-free interview time.
- Render content as encoded text. An interview URL is text in this release, not raw HTML.
- Do not copy internal audit details, access tokens, CV snapshots, or unrelated candidate data into messages.
- Per-record deep links are not required. Keep existing workspace navigation; do not invent routes or query parameters that current pages do not support.

## 5. Persistence and compatibility

### Existing Notification entity

Retain Id, UserId, Title, Content, Kind, RelatedJobPostingId, IsRead, and CreatedAt.

Add nullable `int? BroadcastId` linked to the broadcast submission described below. Existing rows keep null.

- Add unique `(UserId, BroadcastId)` to enforce at most one row per broadcast recipient. SQLite and MySQL permit multiple nulls, so other notifications are unaffected.
- Add `(UserId, CreatedAt, Id)` for stable personal inbox ordering.
- Preserve unique `(UserId, Kind, RelatedJobPostingId)` and existing JobAlert foreign-key behavior.
- **Only JobAlert populates RelatedJobPostingId.** All kinds in section 4 leave it null. Otherwise repeated applications/statuses/interviews for the same job would collide with the existing unique index.
- Existing Kind == null rows remain visible as legacy messages. Do not infer their kind from title text.
- Do not backfill old events or reset existing IsRead/CreatedAt values.
- Keep existing cascade deletion from ApplicationUser to that user's Notification rows.

### NotificationBroadcast entity

Add `hotjob/Models/NotificationBroadcast.cs` and a DbSet in ApplicationDbContext:

| Field | Contract |
| --- | --- |
| Id | Integer primary key. |
| AdminUserId | Required string, maximum 255; server-resolved actor identifier, retained like audit metadata. No cascading user FK. |
| RequestId | Required Guid, supplied once for one send intent. |
| Audience | Required string, maximum 20; exactly `All`, `Candidate`, or `Employer`. |
| Title / Content | Required; maximum 200 / 2,000, normalized once. |
| RecipientCount | Integer count of distinct persisted recipients. |
| CreatedAt | UTC timestamp from TimeProvider. |

Unique `(AdminUserId, RequestId)` claims a broadcast intent atomically. Notification.BroadcastId uses a restrictive FK, not cascading deletion. No public delete/update operation exists for a broadcast submission.

### Interview concurrency metadata

Add `long Revision` to `hotjob/Models/Application/Interview.cs`, with database default 0 for existing rows. A newly created interview starts at 0. Every real update or cancellation through either endpoint conditionally matches the read Revision and atomically increments it with the changed values. No-op requests do not increment it. Revision is internal persistence metadata, not a new required request/response field.

This avoids comparing long, nullable text fields under different MySQL/SQLite collations and prevents concurrent identical updates/cancellations from both creating notifications. Do not use SQL Server rowversion syntax.

The migration is additive: one new table, nullable Notification.BroadcastId/FK, non-null Interview.Revision with default 0, and indexes. Preserve all legacy notifications, interview values, and job alerts. Keep MySQL/Pomelo and SQLite model behavior compatible. Include the model snapshot.

Notifications follow the existing durable inbox lifecycle: no automatic TTL purge in this release. Account deletion removes personal notification copies. Broadcast text is shared admin-authored content and must not include private candidate information. A production-wide retention/backup policy remains an operational responsibility, not a claim this feature provides legal-compliance automation.

## 6. Shared inbox and REST contracts

Use `hotjob.DTOs.Job.PagedResponse<T>` for compatibility with NotificationFeed, not the similarly named Common type.

Define `NotificationDto(int Id, string Title, string? Content, bool IsRead, DateTime CreatedAt, string? Kind)` under `DTOs/Notifications`.

The shared inbox service exposes:

```csharp
Task<ServiceResult<PagedResponse<NotificationDto>>> ListAsync(
    string? userId, int page, int pageSize, CancellationToken cancellationToken = default);
Task<ServiceResult<int>> UnreadCountAsync(
    string? userId, CancellationToken cancellationToken = default);
Task<ServiceResult<NotificationDto>> MarkReadAsync(
    string? userId, int notificationId, CancellationToken cancellationToken = default);
Task<ServiceResult<int>> MarkAllReadAsync(
    string? userId, CancellationToken cancellationToken = default);
```

Internal userId arguments come only from the authenticated API principal or Blazor authentication state, never a form, query parameter, or request-body recipient field. The service checks that the user still exists and is active on every operation.

- Filter all four operations by caller UserId and `(Kind == null || Kind != JobAlert)`.
- Sort list by CreatedAt descending, then Id descending. Clamp page to at least 1 and pageSize to 1..100; defaults are 1 and 20. Use long offset arithmetic before Skip to avoid overflow.
- Missing/disabled caller: Unauthorized. Missing, foreign-owned, or JobAlert item on mark-read: NotFound, without revealing ownership.
- Mark-read is idempotent and returns the item. Mark-all returns affected count and is idempotent for already-read rows.
- Mark-all updates unread rows visible to its single database statement. A notification inserted after that statement remains unread.
- After a read action actually changes rows, publish an invalidation after persistence so other tabs refresh their counts. A no-op need not publish.
- Existing CandidateService notification methods and NotificationFeed signatures remain compatibility delegates, mapping NotificationDto to the unchanged five-field MyNotificationDto. Application reads remain in CandidateService.

Add `NotificationsController : ApiControllerBase` with explicit `[ApiController]`, `[Route("api/notifications")]`, and `[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]`. ApiControllerBase does not supply these attributes. Apply the HTTP method/template shown below to each action:

| Endpoint | Success |
| --- | --- |
| `GET /api/notifications?page=1&pageSize=20` | 200, `ApiResponse<PagedResponse<NotificationDto>>`. |
| `GET /api/notifications/unread-count` | 200, `ApiResponse<int>`. |
| `PUT /api/notifications/{id:int}/read` | 200, `ApiResponse<NotificationDto>`. |
| `PUT /api/notifications/read-all` | 200, `ApiResponse<int>`. |

Personal endpoints require authentication, not a Candidate-only role. Preserve ApiControllerBase's error envelope/status mapping; do not refactor unrelated APIs.

## 7. Admin broadcasts

### Input, audiences, and authorization

`CreateBroadcastRequest` contains `Guid RequestId`, `string Audience`, `string Title`, and `string Content`. Reject Guid.Empty, unsupported/case-mismatched audiences, empty normalized text, or overlength text as Validation.

- `All`: all active ApplicationUsers, including admins and users without Candidate/Employer profiles.
- `Candidate`: active users currently assigned the Identity Candidate role.
- `Employer`: active users currently assigned the Identity Employer role, including pending/rejected memberships. A broadcast is not ATS access.
- Role audience selection uses database Identity role membership, not profile presence or client claims. Store one copy per distinct UserId.
- Resolve the acting administrator once through IAdminAuditService.GetRequiredAdminIdAsync, then verify the corresponding active user, nonempty email, and current database Admin role before selecting recipients. Capture that validated UserId/email as the immutable actor for this send. Missing identity/role returns Unauthorized without side effects; do not leak the helper's missing-admin exception to the caller.
- `AdminNotificationsController : AdminControllerBase` explicitly declares `[Route("api/admin/notifications")]` to replace the inherited `api/admin/[controller]` route. Its action declares `[HttpPost("broadcasts")]`. Return 200 with the raw BroadcastResultDto, matching existing admin controller convention. Preserve admin error arrays and existing 400/401/403/409 behavior.
- `BroadcastResultDto(int BroadcastId, int RecipientCount, DateTime CreatedAt)` is returned for both first submission and replay.
- Blazor uses the same service and authorization checks directly; hiding an admin page is not sufficient authorization.

### Transaction and retries

1. Authenticate/authorize and capture the actor, then normalize/validate input before claiming anything. An identity switch after this authorization point cannot reattribute an in-flight operation; later operations require fresh authorization.
2. Within one database transaction, insert NotificationBroadcast to claim unique `(AdminUserId, RequestId)`.
3. Snapshot distinct eligible recipient IDs once, with a bounded query taking at most 10,001 IDs. More than 10,000 is a Conflict: roll back with no submission, recipients, audit, or push. An empty audience is a Validation failure, also with no side effects.
4. Insert one unread Notification per snapshotted recipient with Kind == AdminBroadcast and BroadcastId set. Process tracked inserts in batches of 500, retaining the same transaction. Do not detach unrelated tracked caller data.
5. Persist RecipientCount and append one AdminAuditLog row with action `Notification.Broadcast`, target type `NotificationBroadcast`, and the submission ID. Use the captured actor UserId/email and TimeProvider timestamp. Construct this row inside AdminBroadcastService using the same DbContext/transaction; do not call LogAsync, which re-resolves mutable circuit identity. Keep the existing audit entity limits, including 500-character detail. Detail contains only audience/count/submission ID, not message title/content. Do not change IAdminAuditService or unrelated audit writers for this feature.
6. Commit all rows and audit together. Only then signal recipients, once each. Slow/failing circuit callbacks never hold the database transaction open.

There is no asynchronous job acceptance or partial success. The limit is explicit protection for a small synchronous release, not a claim of large-scale broadcast throughput. Upgrade to durable batched background fan-out before raising it.

On unique-claim collision, roll back/clear the failed unit of work before querying the existing submission:

- Same actor, RequestId, Audience, normalized Title, and normalized Content: replay its original result without reselecting recipients, rewriting audit, or notifying again.
- Same actor/key with different normalized payload: Conflict.
- If a concurrent claim has not become readable within the database/request timeout: Conflict; never run another fan-out because the first attempt appears slow.
- RequestIds from different administrators represent independent intents.
- Keep submission records for the lifetime of their notification copies. No expiry silently permits replaying an old request as a new broadcast.

Transport failures after commit do not make the broadcast a database failure. A client retry with the same RequestId can recover the stored result.

A failure before commit rolls back the unit of work. A lost commit acknowledgement is an **unknown outcome**, not proof of rollback: do not claim nothing was sent, and do not retry with a new RequestId. A same-key retry resolves a committed submission or safely makes a new attempt if the transaction truly rolled back. Publish only after a confirmed commit. After that point, a failed/cancelled best-effort push must not run the business rollback handler or change the persisted result.

### Admin page

Add `/admin/notifications` with audience selector, required title/content, length feedback, and an explicit “Send broadcast” action. Show a confirmation containing audience and the fact that recipients receive an in-app message. Disable double submission while pending and display the committed recipient count afterward.

Generate RequestId once per confirmed send intent. After a network/unknown-result failure, retain the payload and RequestId for retry; do not silently generate a new one. A deliberate new send after success receives a new RequestId. Editing a failed pending payload requires a deliberate new-send action and warns that the prior intent may already have committed.

Reuse existing admin layout/classes and accessible form patterns. No new UI library, visual redesign, or broadcast deletion/history console.

## 8. Realtime, reconciliation, and circuit safety

- Signals remain user-scoped invalidations with no notification body. Keep the existing INotificationRealtime and INotificationBroadcaster contracts.
- Subscribe once for the current active authenticated user per circuit; replace the subscription on identity change and dispose it on sign-out or circuit closure.
- Subscribe before the initial database read. Refresh on sign-in, inbox entry, connection-up/reconnect, and every live invalidation.
- While connected/authenticated, reconcile at **30-second intervals** using PeriodicTimer and TimeProvider. This repairs lost hints, including a crash after commit but before push. Pause on disconnect; reconnect requests an immediate refresh.
- Navbar, `/notifications`, and the mounted MyApplications messages panel react to the shared circuit change state. A mounted inbox reloads its current page; if that page is now out of range, load the final valid page. Do not force users back to page 1 for every event.
- Never use `unreadCount++` or `--` as the authoritative update. Fetch the filtered DB count after any invalidation/read action; duplicate or out-of-order hints must not drift the badge.
- Route all notification database work sharing the circuit DbContext through CircuitDbGate. Circuit callbacks queue refresh work; writers never await that refresh while holding the gate.
- Coalesce overlapping invalidations, but preserve a dirty flag if a new signal arrives during a read. Drain another refresh after the current one, rather than dropping the last event. No overlapping reads on one DbContext.
- Capture an identity generation before each asynchronous refresh. Ignore late results after sign-out/user switch, clear displayed data immediately, and prevent a disposed component from rendering.
- Read actions refresh locally and invalidate other circuits for the same user. Browser tabs for another user remain unchanged.
- A transient database refresh failure keeps previously loaded data visibly stale, offers retry on the inbox, and retries at the next normal trigger. Do not replace the count with zero on a database error or expose exception messages. Unauthorized is different: clear personal data and the subscription and return to the signed-out state; never retain disabled-account data as a “stale” successful inbox.
- Realtime success means an active, healthy circuit refreshes without navigation/manual reload. It is not an exactly-once transport guarantee.
- Recovery bound is the next successful explicit refresh, reconnect, or 30-second reconciliation plus query/render time. No delivery-time SLA is claimed while disconnected or the database is unavailable.

## 9. UI behavior and boundaries

- Make `/notifications` and its navbar entry available to every authenticated active user. This includes pure Employer accounts, pending/rejected members, and Admin recipients of All broadcasts.
- Show exactly one navbar inbox entry for dual-role users. Change Candidate-only heading/copy to role-neutral wording.
- Keep list pagination, loading/empty/error states, mark-read, mark-all-read, and unread indication.
- Render the shared inbox and MyApplications messages panel in the service's returned order: CreatedAt descending, then Id descending. Remove their existing unread-first client-side sorting; read actions must not reorder an otherwise unchanged page.
- Display notification CreatedAt timestamps with an explicit UTC label in both notification surfaces, for example `28 Nov 2026, 09:30 UTC`, and use a UTC-qualified machine-readable time value. Interpret database DateTime values as stored UTC rather than applying the server's local timezone. Generated appointment text also identifies UTC.
- Label the MyApplications full interview date/time as UTC as well, so opening full details after a shortened notification does not change the apparent timezone.
- Automatically refreshing the page never marks items read. Read state changes only through explicit read actions.
- Keep JobAlert excluded from the unified badge, inbox, and read actions; its dedicated page/service stays unchanged.
- Render text safely, provide labels and disabled/busy states for controls, and announce action results without repeatedly announcing the entire list on each reconciliation.
- Preserve keyboard focus and current pagination during background refresh. Do not add forced popups, audio, or intrusive toasts.

### Trust boundaries and abuse cases

Assets are private recruiting messages, interview locations/links, account identity, and the administrator's ability to fan out content.

- A caller cannot read/mark someone else's row by guessing its ID.
- A client cannot supply another UserId, impersonate an admin, choose another user's realtime channel, or bypass company ownership for manual messages.
- A rejected/inactive Employer profile does not prevent its active ApplicationUser from reading its own rejection.
- Changed roles do not silently grant access to another user's history. New fan-out eligibility uses current database state.
- Disabling a user blocks inbox service reads/actions on the next operation even before the existing account-status circuit revalidation signs them out.
- Treat both titles and bodies as potentially sensitive user-authored message content. Do not log either, interview links, CV data, or tokens. Log event kind, notification/submission ID, counts, and errors without sensitive payloads. The broadcaster's legacy title parameter remains for compatibility but must not appear in logs.
- Preserve existing authentication, HTTPS, CORS, and audit behavior. No new auth scheme, dependency, broker, or external service.

## 10. Files and project conventions

Expected production areas, to be converted into exact task-level edits in the implementation plan:

| Area | Responsibility |
| --- | --- |
| `hotjob/Models/Notification.cs`, new `NotificationBroadcast.cs` | Kind constants and broadcast persistence. |
| `hotjob/Models/Application/Interview.cs`, `Repositories/Employer/IApplicantRepository.cs`, `ApplicantRepository.cs` | Interview Revision and conditional recruiting state updates used by existing services. |
| `hotjob/Data/ApplicationDbContext.cs`, `hotjob/Migrations/` | Additive schema/index mapping and model snapshot. |
| `hotjob/DTOs/Notifications/` | Personal inbox, broadcast request, and result contracts. |
| `hotjob/Services/Notifications/` | Shared inbox service, per-circuit invalidation state/handler, existing broadcaster. |
| `hotjob/Services/Candidate/CandidateService.cs`, `NotificationFeed.cs`, `ApplicationService.cs` | Compatibility delegates and employer event production. |
| `hotjob/Services/Employer/ApplicantService.cs`, `InterviewService.cs`, `NotificationService.cs`, `CompanyService.cs` | Complete and normalize recruiting/membership events. |
| `hotjob/Services/Job/JobService.cs` | Preserve typed candidate rejection events from closing jobs. |
| `hotjob/Services/Admin/CompanyAdminService.cs`, `EmployerAdminService.cs`, new broadcast service/interface | Decision events and transactional broadcast orchestration. |
| `hotjob/Models/Admin/AdminAuditLog.cs` | Add broadcast action/target constants and filter-menu lists. |
| New `hotjob/Controllers/NotificationsController.cs`, `hotjob/Controllers/Admin/AdminNotificationsController.cs` | Shared personal API and admin broadcast API. |
| `hotjob/Controllers/EmployerApplicantsController.cs` | Swagger warning for manual-message retry ambiguity; no endpoint contract change. |
| `hotjob/Components/Layout/NavMenu.razor`, `Components/Pages/Notifications.razor`, `MyApplications.razor`, new `Pages/Admin/AdminNotifications.razor` | Role-neutral inbox, live refresh, and admin send form. |
| `hotjob/Components/Pages/Employer/Interviews.razor` | Honest unknown-result feedback for manual-message send failures; no automatic retry or candidate-inbox access. |
| `hotjob/Program.cs` | Scoped services/circuit handler registrations; retain singleton realtime registry/broadcaster. |
| `hotjob/Tests/hotjob.Tests/` | Notification tests and affected existing domain/admin fixtures. |

Tech stack: .NET 10, Blazor Interactive Server, EF Core 9.0.0, Pomelo 9.0.0/MySQL, Identity, xUnit 2.9.3, and SQLite 9.0.0 tests. Do not add packages.

Follow existing primary constructors, file-scoped namespaces, async CancellationToken parameters, nullable annotations, and ServiceResult validation rather than thrown business errors. Example of the existing code style:

```csharp
public sealed class NotificationFeed(ICandidateService inner, CircuitDbGate gate)
{
    public Task<ServiceResult<int>> UnreadCountAsync(
        string? userId, CancellationToken cancellationToken = default) =>
        gate.RunAsync(() => inner.GetUnreadNotificationCountAsync(userId, cancellationToken));
}
```

This is a style example from the current facade, not the target constructor after delegation changes.

### Commands

Run from repository root:

```powershell
dotnet build hotjob/hotjob.csproj --configuration Release
dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --configuration Release
dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --configuration Release --filter "FullyQualifiedName~NotificationTests"
dotnet ef migrations add CompleteRealtimeNotifications --project hotjob/hotjob.csproj --startup-project hotjob/hotjob.csproj
dotnet run --project hotjob/hotjob.csproj
```

Migration generation requires the existing EF tool and local configuration. Do not install tools without agreement. Running the application applies migrations/seeds at startup; only run it against an explicitly approved disposable development database. Unit/integration tests must use disposable test databases, never the user's real database. No standalone lint command is established for this feature.

### Always / ask first / never

- Always: keep domain changes and their notifications atomic, publish only after commit, preserve current error/authorization semantics, use TimeProvider in touched notification paths, and test both permitted and refused recipients.
- Ask first: adding packages/infrastructure, changing scope or state-machine rules, raising the broadcast recipient limit, or applying migrations to a real/shared database. User approval of this spec permits generating the described additive migration during later implementation, not applying it remotely.
- Never: weaken/delete existing assertions, clear unrelated user work, log message payloads/secrets, add a global query filter hiding historical messages, or claim SignalR guarantees durable/exactly-once delivery.

## 11. Acceptance and verification

The implementation plan must attach named executable tests to these acceptance groups. No arbitrary new coverage percentage replaces the existing suite.

1. **Inbox ownership/compatibility:** active Candidate-only, Employer-only, dual-role, Admin, pending/rejected members, disabled/missing caller, foreign ID, legacy null Kind, JobAlert single-item rejection, repeated mark-read/all, empty list, stable ties, and extreme pagination values.
2. **Recruitment producers:** application commit creates one row per eligible same-company employer; exclude other companies, pending/rejected/inactive employers, and disabled users. A duplicate application creates no extra rows. All real ATS transitions and job-close rejections notify the right candidate; no-op/invalid transitions and rollback notify nobody.
3. **Interview/manual messages:** verify every specified detail, UTC labeling, type/status, real updates versus identical values, concurrent identical updates, concurrent update/cancel and double cancellation, cancellation through both endpoints, repeated cancellation, terminal-application refusal, ownership, optional notes, overlength input, maximum 2,048-character locations plus 2,000-character notes, and exactly one message on auto-advancement. A race winner stores one event and advances Revision once; the loser returns Conflict. Maximum-length interviews remain valid, store a bounded preview, and retain full application detail.
4. **Company/membership producers:** company approve/reject/revoke/suspend/reactivate reach the specified distinct recipients; pending/rejected applicants receive no company-wide decision. Owner and admin membership decisions notify the affected user, including admin rejection setting Employer.IsActive false. Refusal, same-state requests, concurrent losers, and audit failure leave no extra messages or push.
5. **Broadcast security/audiences:** unauthenticated/non-admin direct service calls and endpoints have no side effects; All includes admin-only users; role groups use DB roles; dual-role users get one copy; pending/rejected role holders remain eligible; disabled users are excluded; zero recipients and 10,001 recipients fail atomically; 10,000 succeeds with 500-row insert batches.
6. **Broadcast idempotency/atomicity:** same-key replay, changed-payload conflict, concurrent same-key sends, separate admins, and response loss after commit; exactly one submission, recipient set, audit entry, and original result. Switch/sign out the circuit's identity after actor capture and assert the submission/audit keep the same original validated UserId/email. Inject recipient-insert/audit and definite pre-commit failures: rolled-back attempts produce no push or partial success. Separately simulate lost commit acknowledgement: a same-key retry resolves the original intent without duplicates. A cancelled/failed push after confirmed commit cannot trigger rollback or change the successful stored result.
7. **Circuit safety/recovery:** keep existing registry tests and add duplicate/out-of-order signals, stale badge correction, open inbox/messages refresh, concurrent invalidation dirty-flag drain, sign-out/user-switch during a pending read, account disable causing Unauthorized/clear rather than stale private content, reconnection, paused disconnected timer, disposal, transient query failure, and a same-circuit writer holding CircuitDbGate. No deadlock or overlapping DbContext work.
8. **Cross-tab behavior:** two circuits for one user converge after creation and read actions; another user's circuit is unaffected. With a deliberately dropped post-commit signal, the next 30-second fake-time reconciliation recovers the correct count and mounted list.
9. **Persistence/providers:** migrate a disposable MySQL database with legacy messages, interviews, and JobAlert rows; prove values survive, existing interviews acquire Revision 0, and new keys/FKs and conditional-update races behave as specified. SQLite relational tests cover nullable unique keys, same-job repeated non-alert events, rollback, ordering, revision checks, and broadcast deduplication. SQLite success does not substitute for the MySQL migration/concurrency check.
10. **Browser/API/privacy acceptance:** with separate candidate/employer/admin sessions, submit an application, move status, schedule/reschedule/cancel interview, decide company/membership requests, send each broadcast audience, mark read across tabs, disconnect/reconnect, and verify text encoding, focus, loading/error states, UTC timestamps, stable returned ordering, and no manual reload. Route-level integration tests exercise the exact personal and admin API URLs and authentication attributes, rather than only calling controller methods. Logger-capture tests assert manual/admin title/body and interview locations never appear in notification fan-out logs. Use existing styles and controls; browser checks are not replaced by source-text assertions.

Run the full existing suite after implementation, not just notification tests. Record command outputs and any unavailable MySQL/browser checks honestly.

## 12. Review workflow and approval gate

Documentation checklist:

- [x] Confirm intent, event scope, audience grouping, and single-process deployment with the user.
- [x] Inspect current producers, consumers, persistence constraints, authorization, and tests.
- [x] Draft the specification and distinguish proposed policies from confirmed intent.
- [x] Have `opencode/hy4-flash-free#high` review this specification using review skills.
- [x] Independently verify reviewer findings against source, fix supported issues, and check document consistency.
- [ ] Obtain user approval of the reviewed written specification.
- [ ] Write the implementation plan with `writing-plans`; request plan review before any product implementation.

The user requested Hy4 flash at its highest effort as the reviewer. The available model is Hy4 flash Free, with `high` as its highest listed effort.

### Review disposition and evidence

Hy4 Flash reviewed the draft using the requested review skills. Main-session source checks confirmed the relevant route inheritance, title logging, unread-first UI sort, missing interview concurrency protection, timezone-free rendered dates, and mutable audit-identity resolution.

The reviewed revision explicitly fixes controller attributes/routes, payload-free broadcaster logging, returned UI ordering/UTC timestamps, interview compare-and-swap with Revision, and captured-actor broadcast auditing without expanding the shared audit API. Manual-message retry documentation now names its Swagger location and does not suggest employers can inspect a candidate's private inbox.

The main session additionally found the 2,048-character interview-location versus 2,000-character notification-body mismatch and specified a bounded, visibly shortened preview with full details retained in Applications.

Existing notification-registry baseline: `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --configuration Release --filter "FullyQualifiedName~NotificationTests" --nologo --verbosity minimal` passed 5 tests, failed 0, skipped 0. This does not verify any of the proposed feature behavior. Restore/build reported existing MySqlConnector moderate-severity and SQLitePCLRaw.lib.e_sqlite3 high-severity vulnerability advisories, plus compiler warnings; dependency remediation is outside this documentation change.

No blocking product-intent question remains. The policies explicitly listed in section 1 and the rest of this draft are subject to written-spec approval. New feature runtime, browser behavior, MySQL migration/concurrency, and the full regression suite have not been verified during this documentation task.

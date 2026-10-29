# SDD ledger — plan: docs/premium-payment-plan.md

Contract: `.superpowers/sdd/premium-payment/contract.md` (binding decisions D1–D16).
Baseline before any change (2026-10-26): `dotnet build` 0 warnings / 0 errors; `dotnet test` 204 passed, 0 failed.
Environment: git available but **no commits are made by the build** (user's tree has unrelated changes); verification uses scratch MariaDB databases `hotjob_verify_*`, never `hotjob`.

Phase order: P1 (GĐ1 Payment core) → P2 (GĐ2 Employer billing) → P3 (GĐ3 CV + Apply) → P4 (GĐ4 MatchScore ML.NET) → P5 (GĐ5 Candidate Premium) → P8 (GĐ8 Admin revenue) → P6 (GĐ6 Job alerts) → P7 (GĐ7 Candidate search + boost) → final integration review.

## Phase results

## Phase P1 — Payment core (done 2026-10-27)
- `Services/Billing/`: `VnPayOptions`, `BillingOptions`, `IVnPayService`/`VnPayService`, `IBillingService`/`BillingService`, `IEntitlementService`/`EntitlementService`.
- `Controllers/VnPayController` (`GET api/vnpay/ipn`, `GET api/vnpay/return`), `Components/Pages/PaymentResult.razor`.
- VNPay HMAC-SHA512 URL + callback verification, amount check, 15-minute expiry, atomic Pending→Paid claim, idempotent replay, plan/featured/premium grants.
- DI + `appsettings.json` (empty secrets) + `.env.example`.
- Tests: `VnPayServiceTests`, `BillingServiceTests`.
- Fixed a pre-existing test fixture leak in `PaymentDataModelTests.DeletingCompanyWithPayments_IsBlocked_AndHistorySurvives` (tracker held a severed required Employer).

## Phase P2 — Employer billing (done 2026-10-27)
- Effective plan resolution (paid while unexpired, else FREE), active-job usage = Draft + Published.
- Quota enforced in `JobService.CreateAsync` and on re-open in `JobService.UpdateAsync`; exact contract message; Conflict error type.
- Featured jobs sort first on the public board; `FeaturedUntil` exposed on job DTOs.
- `Components/Pages/Employer/Billing.razor` + `EmployerBilling.css`; nav link; jobs page plan banner, featured badge, “Feature” purchase, upgrade link.
- Tests: `EntitlementQuotaTests` (free quota, drafts, expired plan, paid quota, re-open, usage, ordering).
- Verification: `dotnet build` 0 warnings / 0 errors; full suite 246 passed.

## Phase P3 — CV + Apply + Withdraw (done 2026-10-27)
- `Services/Candidate/`: `IResumeService`/`ResumeService`, `ICvFileStorage`/`CvFileStorage`, `IApplicationService`/`ApplicationService`; DTOs in `DTOs/Candidate/`.
- Resume CRUD scoped to `Candidate.UserId`, max 5, first resume primary, single-primary invariant, delete-primary promotion, skill/date/duplicate validation, `Application` restrict-delete guard.
- CV upload: PDF/DOC/DOCX ≤ 5 MB, generated safe names under `wwwroot/uploads/cv/{candidateId}/`, traversal rejection, replace/delete cleanup; `app.UseStaticFiles()` so runtime uploads are downloadable; snapshot-referenced files are retained.
- Apply: Published + unexpired (TimeProvider) + owned resume + duplicate pre-check and unique-index conflict; freezes SnapshotSummary/SnapshotFileUrl, writes Pending history, notifies active employers of the company.
- Withdraw: candidate-only, blocked for Offered/Rejected/Withdrawn, writes "Withdrawn by candidate" history.
- UI: `Profile.razor` real CV list/editor/upload/primary/delete with loading/empty/error states; `JobDetail.razor` candidate-only apply panel (primary resume preselected, cover letter, already-applied/no-resume states); `MyApplications.razor` withdraw confirmation + refresh.
- Tests: `ResumeServiceTests`, `CvFileStorageTests`, `ApplicationServiceTests`.
- Verification: `dotnet build` 0 warnings / 0 errors; full suite 301 passed (0 failed).

## Phase P6 — Job alerts (done 2026-10-27)
- `Notification` gains `Kind` (varchar 40, null) and `RelatedJobPostingId` (FK to `JobPosting`, `SetNull`), plus `(UserId, Kind, CreatedAt)` and unique `(UserId, Kind, RelatedJobPostingId)` indexes. Migration `20261027034226_AddJobAlertNotifications`; applied cleanly on scratch MariaDB `hotjob_verify_p6`, then dropped. `hotjob` was never touched.
- `Services/JobAlerts/`: `IJobAlertService`/`JobAlertService`, `JobAlertsOptions` (`JobAlerts:MinMatchPercent`, default 60), DTOs in `DTOs/JobAlerts/`.
- On publish, Premium + active candidates are scored per resume through `IMatchScoreService`; the best available score ≥ threshold creates one in-app alert. Dedupe is checked before insert and backstopped by the unique index (concurrent duplicate logged, other candidates continue). Non-Published/expired jobs are ignored.
- `JobService` hook fires only on create-Published and the transition into Published (Draft/re-open). Alert failures are caught and logged with the job id; the publish result and stored status are unaffected. Free/FREE-plan and existing quota behaviour unchanged.
- `/job-alerts` page (`AuthorizeView Roles="Candidate"`, InteractiveServer, auth-race, loading/empty/error), Premium-only feed with read state and “Mark all as read”, free-candidate upsell to `/premium`, nav link. Candidate messages feed now excludes `Kind = JobAlert` so alerts do not double as employer messages.
- Tests: `JobAlertTests/JobAlertServiceTests` + `JobAlertTests/JobAlertPublishTests` (18 tests: threshold boundary, free/inactive exclusion, best resume, expiry/draft, dedupe, isolation/read state, no-skill job, publish hook matrix, failure logging).
- Verification: `dotnet build hotjob/hotjob.csproj` 0 warnings / 0 errors; full suite 339 passed (0 failed).

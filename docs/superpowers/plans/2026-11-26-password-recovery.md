# Password Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver secure local-account recovery through configurable Gmail SMTP and a Development-only email-log fallback.

**Architecture:** Anonymous requests enter a bounded process-local queue before any account lookup. A scoped worker generates Identity reset tokens and sends email; a scoped recovery service resets passwords and transactionally revokes refresh sessions. Shared throttling and a server-owned circuit caller context protect both pages and APIs.

**Tech Stack:** .NET 10, ASP.NET Core Identity/Data Protection, EF Core, Blazor Interactive Server, MailKit, xUnit/SQLite/HtmlRenderer.

**Spec:** `docs/superpowers/specs/2026-11-26-password-recovery-design.md`

## Global Constraints

- No git commands, real secret output, real email delivery, or database migrations.
- Use existing English UI, `ServiceResult`, and `CircuitDbGate`.
- Queue capacity 100; one email admission per 60 seconds; 10 attempts per IP per 10-minute fixed window; 10,000 entries per limiter category.
- Inject `TimeProvider`; token lifetime defaults to `01:00:00` through `DataProtectionTokenProviderOptions`.
- SMTP uses STARTTLS, default port 587, 15-second client timeout, and 30-second linked delivery deadline.
- Only Development may load `.env.local` or log reset email contents.
- Run `dotnet test` at repository root; report exact results to `docs/admin/task-2-result.md`.
- Execution: native, with focused TDD and review; user explicitly waived another plan approval gate.

## Review Focus

1. IPv4-mapped IPv6 addresses must share an IP budget with equivalent IPv4 addresses.
2. Invalid configured base URLs must not silently fall back to request-controlled origins.
3. A failed password-policy attempt must not consume a valid reset token.
4. A scoped UserManager must not retain a rolled-back password/stamp and poison later circuit operations.
5. Navigation to another reset query must clear old form state rather than accidentally submit an old token.

---

## File structure and interfaces

- `hotjob/Configuration/DevelopmentEnvironmentFile.cs`: existing dotenv behavior extracted and Development-gated.
- `hotjob/Services/Email/{IEmailSender,EmailOptions,SmtpEmailSender,DevelopmentEmailSender,NotConfiguredEmailSender,EmailRegistration}.cs`: transport contract, safe implementations, and selection.
- `hotjob/Services/Account/PasswordRecovery/{PasswordResetOptions,PasswordResetRateLimiter,PasswordResetQueue,PasswordResetCallerContext,PasswordResetCircuitHandler,PasswordResetLink,IPasswordResetService,PasswordResetService,PasswordResetService.Reset,PasswordResetDelivery,PasswordResetWorker,PasswordResetRegistration}.cs`: focused recovery units.
- `hotjob/DTOs/Authentication/{ForgotPasswordRequest,ResetPasswordRequest}.cs`: API requests.
- `hotjob/Components/Account/{ForgotPasswordForm,ResetPasswordForm}.cs`, `PasswordResetNotice.razor`, `{ForgotPasswordSection,ResetPasswordSection,PasswordRecoveryCard}.razor`: UI validation, fixed notice, testable forms, shared card.
- `hotjob/Components/Pages/{ForgotPasswordPage,ResetPasswordPage}.razor`: accessible forms using existing login CSS.
- Modify `Program.cs`, `LoginPage.razor`, `AccountController.cs`, `.env.example`, and targeted test fixture infrastructure.
- Tests in `hotjob/Tests/hotjob.Tests/PasswordRecoveryTests/`.

Public recovery API:

```csharp
Task<ServiceResult<string>> RequestPasswordResetAsync(
    string? email, string baseUrl, CancellationToken cancellationToken = default);
Task<ServiceResult> ResetPasswordAsync(
    string? email, string? token, string newPassword,
    CancellationToken cancellationToken = default);
```

Queue contract: `bool TryEnqueue(PasswordResetJob job)`, with
`PasswordResetJob(string Email, string BaseUrl)` and cancellable async reading.
Delivery contract: `Task DeliverAsync(PasswordResetJob job, CancellationToken ct)`.
Caller context exposes a retained, normalized, server-observed IP key; it does
not accept client body/query/header overrides.

### Task 1: Configuration and safe email transports

**Files:** Configuration/Email files above; `Program.cs`, `.env.example`;
`PasswordRecoveryTests/EmailTests.cs` (including environment-file tests).

**Interfaces:** Produce `IEmailSender.SendAsync(string to, string subject,
string htmlBody, CancellationToken ct = default)` and
`AddRecoveryEmail(IServiceCollection, IConfiguration, IHostEnvironment)`.

- [x] Write tests for dotenv `__` conversion, missing-file behavior, Development-only loading, and all sender-selection combinations.
- [x] Write tests asserting non-Development fallback never logs body/recipient/token; SMTP exceptions containing sentinel secrets log no exception payload. Use an injected fake SMTP client, not a network connection.
- [x] Run filtered tests; confirm failure before implementing.
- [x] Extract the loader; preserve lookup locations and dotenv quoting. Add options and guarded senders, selecting SMTP iff host/username/password are all nonblank.
- [x] Check NuGet for current stable MailKit and audit its dependency graph; add a version with no published advisory found.
- [x] Extend `.env.example` with dummy/empty requested keys and token lifetime; never read `.env.local` values.
- [x] Run filtered tests; require all passing.

### Task 2: Bounded queue, limiter, safe links, and caller identity

**Files:** Recovery options/queue/limiter/link/caller/circuit-handler above;
`PasswordRecoveryTests/RequestInfrastructureTests.cs`.

**Interfaces:** Produce queue contract above,
`bool PasswordResetRateLimiter.TryAcquire(string email, string ip)`,
`string? PasswordResetLink.ResolveBaseUrl(string fallback)`, and link composition.

- [x] Write tests for queue capacity/nonblocking failure, normalized email cooldown, IP fixed-window budget charged on email denial, expiry, synchronized admission, limiter capacity and cleanup.
- [x] Write tests for IPv4-mapped IPv6 equivalence, retained caller IP after HttpContext disappears, shared unknown-IP bucket, and ignoring forwarded headers.
- [x] Write tests for configured URL precedence, invalid configured URL rejection, email/token URL encoding, and query/fragment/userinfo rejection.
- [x] Run filtered tests to confirm red.
- [x] Implement synchronized hashed-key dictionaries with TimeProvider, bounded Channel queue, HTTP(S) link validation, and server-owned caller context captured on circuit connection.
- [x] Run filtered tests to confirm green.

### Task 3: Worker delivery and enumeration-safe request service

**Files:** Recovery service/interface/delivery/worker/registration above;
`PasswordRecoveryTests/PasswordResetRequestTests.cs`; test support helpers.

**Interfaces:** Produce request API above and `PasswordResetDelivery.DeliverAsync`.
Consume Task 1 sender and Task 2 queue/limiter/link/caller contracts.

- [x] Write real-Identity tests: unknown/disabled/passwordless/valid requests all return identical success; only active password users receive generated tokens.
- [x] Write a blocked-delivery test proving request completion without waiting for lookup/SMTP; verify a fresh worker scope per job and safe continuation after job failure.
- [x] Test configured lifetime/HTML encoding and safe delivery failures with sentinel exception text.
- [x] Run tests to confirm red.
- [x] Implement request admission without database work and scoped hosted-worker consumption; validate eligibility inside delivery; HTML-encode link and lifetime; use fixed safe logs.
- [x] Wire recovery registrations without starting the real application.
- [x] Run tests to confirm green.

### Task 4: Transactional reset and refresh-session revocation

**Files:** `PasswordResetService.cs`;
`PasswordRecoveryTests/PasswordResetTests.cs`, test fixture support.

**Interfaces:** Produce reset API above; consume real UserManager/DbContext and existing `IAuthService.LogoutAsync`.

- [x] Write real-Identity tests for password/stamp change, all-user-session revocation, other-user preservation, old refresh rejection, malformed/invalid/expired/reused tokens, and independent-context concurrency.
- [x] Write tests for policy errors plus successful retry, disabled-after-issuance rejection, and login rejection after disablement.
- [x] Write revocation-failure rollback test, including a subsequent read/reset in the same scope after rollback.
- [x] Run tests to confirm red.
- [x] Implement bounded decoding, generic invalid-link results, Identity policy-error mapping, and a shared transaction around password reset and LogoutAsync. Clear tracked rollback state; log only fixed diagnostics/types for unexpected errors.
- [x] Run tests to confirm green.

### Task 5: Blazor forms, login flag, and API endpoints

**Files:** DTO/forms/pages/notice above; `LoginPage.razor`, `AccountController.cs`;
`PasswordRecoveryTests/RecoveryUiTests.cs`, `RecoveryControllerTests.cs`.

**Interfaces:** Consume shared recovery API. API forgot-password returns HTTP 200 fixed message; reset returns 204 or existing 400 error-array pattern.

- [x] Write HtmlRenderer tests for fixed notice flags, forgot/reset validation, policy errors, invalid-link retry destination, redirect, password clearing, gate serialization, duplicate-submit protection, and cookie-cleanup failure.
- [x] Write reset-query-change test asserting old field/error state is cleared.
- [x] Write controller tests for anonymous metadata, fixed responses, and safe server-computed base URL.
- [x] Run tests to confirm red.
- [x] Implement English forms with login CSS, direct services within CircuitDbGate, accessible field/error/status markup, no-referrer reset metadata, and force-load success redirect. Do not duplicate Identity policy.
- [x] Wire forgot link and reset notice without changing login logic. Add endpoints using the same service.
- [x] Run focused and existing login/profile/controller tests; require green.

### Task 6: Review, full verification, and final report

**Files:** `docs/admin/task-2-result.md`; this plan and spec status.

- [x] Review changes against the spec: secrets/logging, scope lifetimes, worker cancellation, rollback state, rendering mode, disabled accounts, and abuse-limit behavior.
- [x] Run NuGet vulnerability audit including transitive dependencies; distinguish new and pre-existing findings.
- [x] Run `dotnet test` from repo root and record exact passed/failed/skipped counts.
- [x] If real-browser testing is unavailable or would require real local secrets, report it honestly; do not start real-email delivery.
- [x] Write the final report with actual files/version/results, Development dotenv behavior, design decisions, Gmail and console-fallback manual steps (including inbox/spam), and operational limitations.
- [x] Mark completed checklist steps; leave all files uncommitted.

## Self-review

All spec sections map to Tasks 1–6. Review-focus cases have explicit tests in
Tasks 2, 4, and 5. Queue/delivery/service signatures agree across tasks. Setup and
configuration are grouped with the deliverable they enable. No git or real-email
verification steps are present. The user authorized proceeding without further
line-by-line plan review.

# Task 2 — Forgot/reset password

Date: 2026-11-26

## Summary

Implemented local-account password recovery, real SMTP delivery through MailKit,
and a Development-only email-log fallback. Added `/forgot-password`,
`/reset-password`, the login link and fixed success notice, and matching anonymous
account APIs. Reset uses real Identity/Data Protection tokens and transactionally
revokes all refresh sessions through the existing logout mechanism.

No migrations, git commands, real SMTP sends, or changes to login authentication
logic were made. Local-secret values were not read or displayed. All automated
email tests use fakes, including the SMTP client failure test.

## Files

### Added

- `hotjob/Configuration/DevelopmentEnvironmentFile.cs`
- `hotjob/Services/Email/`:
  `IEmailSender.cs`, `EmailOptions.cs`, `EmailRegistration.cs`,
  `SmtpEmailSender.cs`, `DevelopmentEmailSender.cs`,
  `NotConfiguredEmailSender.cs`
- `hotjob/Services/Account/PasswordRecovery/`:
  `IPasswordResetService.cs`, `PasswordResetOptions.cs`,
  `PasswordResetRegistration.cs`, `PasswordResetCallerContext.cs`,
  `PasswordResetCircuitHandler.cs`, `PasswordResetRateLimiter.cs`,
  `PasswordResetQueue.cs`, `PasswordResetLink.cs`,
  `PasswordResetService.cs`, `PasswordResetService.Reset.cs`,
  `PasswordResetDelivery.cs`, `PasswordResetWorker.cs`
- `hotjob/DTOs/Authentication/ForgotPasswordRequest.cs`
- `hotjob/DTOs/Authentication/ResetPasswordRequest.cs`
- `hotjob/Components/Account/`:
  `ForgotPasswordForm.cs`, `ResetPasswordForm.cs`,
  `PasswordRecoveryCard.razor`, `ForgotPasswordSection.razor`,
  `ResetPasswordSection.razor`, `PasswordResetNotice.razor`
- `hotjob/Components/Pages/ForgotPasswordPage.razor`
- `hotjob/Components/Pages/ResetPasswordPage.razor`
- `hotjob/Tests/hotjob.Tests/PasswordRecoveryTests/`:
  `EmailTests.cs`, `RequestInfrastructureTests.cs`,
  `PasswordResetRequestTests.cs`, `PasswordResetTests.cs`,
  `RecoveryControllerTests.cs`, `RecoveryUiTests.cs`, `RecoveryFixture.cs`
- `docs/superpowers/specs/2026-11-26-password-recovery-design.md`
- `docs/superpowers/plans/2026-11-26-password-recovery.md`
- `docs/admin/task-2-result.md`

### Modified

- `hotjob/Program.cs`: email/recovery registrations and gated dotenv loader.
- `hotjob/hotjob.csproj`: MailKit package.
- `hotjob/.env.example`: added empty/dummy email/base-URL/lifetime settings;
  preserved existing entries.
- `hotjob/Components/Pages/LoginPage.razor`: forgot link and fixed reset notice.
- `hotjob/Controllers/AccountController.cs`: anonymous recovery endpoints using
  the same recovery service.
- `hotjob/Tests/hotjob.Tests/Support/TestFixture.cs`: optional DI configuration
  hook and scoped test-service accessor; existing tests remain unchanged.

`.gitignore` already ignores `.env.local`; no modification was needed.
`AccountService`, `AuthService`, existing CSS, and database schema were not changed.

## `.env.local` loading and configuration

The original `Program.cs` already loaded dotenv entries into `IConfiguration`.
No DotNetEnv package or launch-profile variable expansion was involved. It
translated `__` to `:`, accepted `export` prefixes and quoted values, and searched:

1. Content root's `.env.local` (normally `hotjob/.env.local`).
2. Current working directory's `.env.local`.
3. Current working directory's `hotjob/.env.local`.

That loader originally ran in every environment. It is now extracted into
`DevelopmentEnvironmentFile` and **only runs in Development**, preserving those
locations and parsing conventions. Repeated keys use the last entry. As before,
dotenv is an added in-memory configuration source and overrides earlier sources.
Restart the app after changing it.

The existing `http`/`https` launch profiles set Development. Production/Staging
must supply settings through normal host configuration; they no longer consume
`.env.local`. No real settings were added to appsettings files.

Required names in the commit-safe example:

```dotenv
Email__Host=
Email__Port=587
Email__Username=
Email__Password=
Email__From=
App__BaseUrl=http://localhost:5168
PasswordReset__TokenLifetime=01:00:00
```

The URL above is a dummy development address, not a secret.

## Dependency and audit

Added **MailKit 5.0.0**, the latest stable version returned by NuGet during this
task. Its MIME dependency resolves to **MimeKit.Core 5.0.0**.

Executed:

```powershell
dotnet list hotjob.slnx package --vulnerable --include-transitive --format json
```

NuGet reported **no advisories for MailKit or its new dependency graph**. An
advisory audit is not a guarantee against undisclosed vulnerabilities.

Existing findings were present before this task and were not silently upgraded:

| Package | Severity / scope | Advisory |
| --- | --- | --- |
| MySqlConnector 2.4.0 | Moderate; existing runtime MySQL dependency | https://github.com/advisories/GHSA-473q-m89c-ghf8 |
| SQLitePCLRaw.lib.e_sqlite3 2.1.10 | High; existing test-only SQLite dependency | https://github.com/advisories/GHSA-2m69-gcr7-jv3q |

These remain open dependency-maintenance issues. Review/update separately before
production release; test-only scope does not mean an advisory should be ignored.

## Design decisions

### Token lifetime, eligibility, and reset

- `PasswordReset:TokenLifetime` defaults to **one hour** and must be positive.
  It configures `DataProtectionTokenProviderOptions.TokenLifespan`; consequently
  it also affects other tokens using that default Data Protection provider.
- Only active users with a local password receive a token. Google-only accounts
  are ineligible; a Google-linked account with a local password is eligible.
- Tokens use `GeneratePasswordResetTokenAsync`, UTF-8 and Base64Url encoding.
  Links URL-encode email/token; HTML email values are HTML-encoded.
- `App:BaseUrl` takes precedence. Otherwise the Blazor navigation base or API
  request base is used. Only absolute HTTP(S) origins/base paths without
  credentials, query, or fragment are accepted. Invalid configured URLs fail
  safely rather than falling back to a potentially untrusted request Host.
- Reset checks eligibility again, decodes bounded input, and calls
  `ResetPasswordAsync`. Invalid/expired/reused/ineligible links share:
  `This reset link is invalid or has expired`
- Identity owns password policy. Only required/matching confirmation validation
  is in the UI; real policy descriptions come from Identity.
- Password/stamp update and `LogoutAsync(null, userId)` run in one transaction.
  Failed revocation rolls back both. Tracked rollback state is cleared so a
  later operation in the same circuit cannot see a noncommitted password/stamp.
- The transaction explicitly uses **Serializable** isolation and reloads the
  user from storage before eligibility checks. This prevents an already-tracked
  active user from undoing an independent administrator disable. In MySQL
  InnoDB, Serializable read locks coordinate that eligibility read with competing
  writes through commit; no admin-service/schema changes were necessary.
- Real Identity tests verify stamp rotation, one-time use, and optimistic
  concurrency with two independently loaded users. Policy failure does not
  consume the token.
- Reset never enables a disabled account. Existing disabled-account login,
  refresh, and session checks remain intact.

### Timing, queue, and abuse limits

- The request returns the same fixed success message before account lookup,
  token generation, or delivery. A hosted worker uses a fresh DI scope per job.
  No fire-and-forget work captures a circuit's DbContext or UserManager.
- Nonblocking queue capacity: **100**. Full/closed queue, unknown account,
  disabled/passwordless account, rate limits, and send failure do not change the
  anonymous response.
- Per normalized email: **one admission per 60 seconds**.
- Per server-observed IP: **10 attempts per 10-minute fixed window**; email
  cooldown denials still charge the IP budget.
- Both limiter categories hold at most **10,000** hashed keys, clean up expired
  entries, and fail closed at capacity. `TimeProvider` makes boundaries testable.
- IPv4-mapped IPv6 is normalized. A scoped caller context captures the connection
  IP and retains it for circuit events. Missing context shares an `unknown`
  bucket instead of bypassing limits. Arbitrary forwarded headers are not read.
- This avoids account-dependent response-critical work; it is not a promise of
  constant-time behavior under host load.

### Email sender by environment

| Condition | Sender |
| --- | --- |
| Host, username, password all nonblank; any environment | SMTP |
| Any missing; Development | Development log sender |
| Any missing; all other environments | Not-configured warning sender |

- SMTP uses configured From, default port 587, required STARTTLS, normal
  certificate validation, a **15-second client timeout**, and a **30-second
  linked operation deadline**. Client/message resources are disposed.
- SMTP failure is caught and logged using only a fixed diagnostic and exception
  type, never an exception payload, password, token, recipient, or message body.
- SMTP failure does **not** switch to logging a reset link.
- Development logging includes the email/reset link and independently checks
  that the actual environment is Development.
- Not-configured logging is a fixed warning without recipient/token/link and
  does not throw to the user.

### UI and API

- Pages use existing login classes, English copy, labelled/autocomplete inputs,
  live validation/status regions, disabled busy controls, and duplicate-submit
  guards. Direct service calls use `CircuitDbGate`.
- Route/query wrappers delegate to small testable form sections. A new reset
  query clears old passwords and errors; stale queued submissions are ignored.
- Success discards password/EditContext state and signs out locally. Cookie
  cleanup is best effort, followed by a force-load redirect to
  `/login?passwordReset=1`. Only exact flag `1` renders hardcoded notice text.
- Reset includes no-referrer metadata. Operators must still prevent reset URLs
  from entering infrastructure/access logs and enforce appropriate response
  security headers.
- API endpoints fit the existing controller pattern:
  `POST api/account/forgot-password` returns 200 and the fixed message;
  `POST api/account/reset-password` returns 204 or 400 with the error array.
  Both are `[AllowAnonymous]` and call the same service as Blazor. A malformed
  JSON/body may still receive the framework's normal validation response.
- The repo currently sets Interactive Server per page rather than globally on
  Routes. New pages match that pattern and require no render-mode restructuring.

## Verification

Baseline: **479 passed, 0 failed, 0 skipped**.

After implementation, executed from the repository root:

```powershell
dotnet test --nologo --verbosity quiet --logger "trx;LogFileName=password-recovery-result.trx"
```

Final result: **551 passed, 0 failed, 0 skipped** (72 additional recovery test cases).
TRX: `hotjob/Tests/hotjob.Tests/TestResults/password-recovery-result.trx`.

Coverage includes sender selection/environment guards/safe failure logs, dotenv
loading, queue bounds, limiter concurrency/capacity/expiry, safe links, actual
Identity eligibility/token expiry/stamp/reuse/concurrency/policy, all-session
revocation and real SQLite rollback, disabled-account login, form/gate/query
lifecycle, fixed login flag, cleanup failure, and controller results.

UI verification uses `HtmlRenderer` and component events, not a real browser or
live Gmail. The real app was not started against local credentials or its
startup migrations. Manual browser checks below remain necessary.

Independent read-only code review found no Critical or Minor issues and one
Important stale-user disablement race. A two-scope regression reproduced it
(failed before the fix); the explicit Serializable transaction and storage reload
fixed it. The final full-suite run above includes that passing regression.
The reviewer did not rerun after the fix; the fix was verified by the regression
and the full suite rather than a second review.

## Manual browser test: real Gmail delivery (operator only)

The agent did not perform these sends. Use a test mailbox/account you control.

1. Ensure the normal database/app prerequisites work and you have an **active
   local-password account** whose email you can receive. Do not use a Google-only
   account for the positive test.
2. Privately edit ignored `hotjob/.env.local`: set host to `smtp.gmail.com`, port
   to `587`, username to the sending Gmail account, password to its Gmail
   **app password**, and From to an authorized sending address. Supply the
   reachable `App__BaseUrl` and optionally the lifetime. Never paste credentials
   into source, reports, screenshots, chat, or shared terminal logs.
3. Start in Development, for example from the repo root:
   `dotnet run --project hotjob/hotjob.csproj --launch-profile http`.
   Confirm the browser's scheme/host/port match `App__BaseUrl`.
4. Open `/login`; choose **Forgot password?**. Enter the local account email.
   Submit once. Expect the fixed success notice promptly, not an SMTP result.
5. Check the recipient's **Inbox**, **Spam/Junk**, and Gmail tabs such as
   Promotions. Verify the sender, HotJob subject, English instructions, one-hour
   default/configured lifetime, and correct application URL.
6. Follow the link. Enter mismatching confirmation and verify field validation.
   Try matching weak passwords; verify Identity policy errors appear without
   consuming the link. Then submit a matching policy-compliant password.
7. Expect `/login?passwordReset=1` and the hardcoded success notice. Sign in with
   the new password; the old password must fail.
8. Open the original reset link again and submit. Expect only the fixed
   invalid/expired error with a **Request a new reset link** destination.
9. If the account had multiple browser sessions, test their old refresh/session
   restoration: all refresh sessions must be invalid. Existing JWTs may retain
   their normal short access-token lifetime.
10. Request an unknown address, a disabled account, and a Google-only account.
    Expect the same visible notice, but no reset emails for those addresses.
    Repeated requests within 60 seconds must not create additional email.
11. For expiry, temporarily configure a short positive lifetime, restart, issue
    a fresh link, wait beyond that lifetime, and submit it. Restore the intended
    lifetime afterward. Test disabling an account after issuance: it must not
    reset or subsequently log in.
12. For delivery failure, privately use intentionally invalid dummy SMTP
    authentication settings, restart, and use a fresh eligible request after
    cooldown. Expect generic browser success and a safe failure-type warning,
    not a token/password or message payload. Restore correct settings privately.

If Gmail rejects authentication, verify app-password availability/account
settings and sender authorization yourself. Never disable TLS/certificate
validation to get delivery working.

## Manual browser test: Development-console fallback

1. Stop the app. Privately clear at least one of host/username/password in
   `.env.local` and ensure another configuration source does not fill it.
   Leave the application base URL correct.
2. Start using the Development launch profile above. Never use this fallback
   with shared/public Development logs.
3. Request recovery for an active local-password account. Expect the same
   generic notice and a **Development email** entry in the server console.
4. Copy the link from the HTML anchor. If needed, turn HTML `&amp;` into `&`
   before opening it. Do not share, screenshot, or commit that link.
5. Complete reset and repeat the policy/reuse/session checks from the Gmail
   procedure. No inbox email is expected on this path.
6. Test unknown/disabled/Google-only inputs and immediate repeated requests:
   same notice, no additional reset message for ineligible/cooling-down input.
7. Test a non-Development environment with missing email settings **only after**
   supplying the normal database/JWT settings through host configuration (dotenv
   is intentionally ignored there). Expect a fixed not-configured warning,
   never a reset link. Restore your original local configuration privately.

## Known limitations, open issues, and assumptions

- Process-local queue/limiter state is lost on restart and is not shared across
  app instances. Use a durable queue/shared limiter for multi-instance guarantees.
- Queue saturation, shutdown, or SMTP failure can leave no email despite generic
  success. There is no retry/outbox/delivery-status UI. Retry after cooldown.
- Configure `App__BaseUrl` and trusted proxy/Host handling in deployment.
  Without trusted proxy configuration, users behind a proxy may share an IP
  budget; a missing circuit context uses the deliberately conservative shared
  fallback bucket.
- Persist/share Data Protection keys when deploying/restarting multiple hosts.
  Key-store infrastructure was not added.
- Already-issued access JWTs keep their expiry, matching existing
  change-password behavior. Reset does not unlock Identity lockout.
- Serializable transactions can briefly block competing writes or produce a
  deadlock/serialization failure under contention; reset reports a fixed
  temporary-failure message, and the caller can retry. Tests use SQLite, not a
  live MySQL concurrency test; MySQL behavior is grounded in its InnoDB
  transaction-isolation documentation.
- Infrastructure request logs and early external resource loads require their
  own URL-redaction/referrer/header controls; page metadata alone is not a
  deployment-wide privacy guarantee.
- Gmail delivery/spam placement and real-browser interaction remain manual
  verification items. No SMTP connectivity or credential validity was tested.
- Existing NuGet advisories and compiler warnings remain open; unrelated
  dependency/auth/CSS refactoring was intentionally excluded.
- Native execution stayed in the existing workspace because the user forbade
  git operations; no agent-created branch/worktree/commits exist.

## Implementation rulings and review boundaries

- Used the existing workspace and a progress ledger instead of git/worktree
  scripts because all git commands were forbidden. Trade-off: no agent-created
  branch isolation or commit save points.
- Split form sections from thin route/query pages, following the existing
  change-password pattern, and added a small shared recovery card. Trade-off:
  three extra focused component files; less duplicated markup and directly
  testable form behavior.
- Kept the approved origin fallback, process-local state, Development link logs,
  and existing JWT expiry. Trade-off: operators must configure public origin,
  proxy/Host controls, log privacy, and distributed infrastructure as needed.
- Did not launch the real app, exercise SMTP, or run a real browser against
  local settings/startup migrations. Trade-off: Gmail delivery, live browser
  circuit behavior, and live MySQL locking remain manual/integration checks.
- Pre-existing dependency findings were reported, not upgraded outside scope.
  No review minors were deferred.

## Official references

- Identity token verification, password validation, and stamp update:
  https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Identity/Extensions.Core/src/UserManager.cs
- Blazor server context/circuit service patterns:
  https://learn.microsoft.com/en-us/aspnet/core/blazor/security/additional-scenarios?view=aspnetcore-10.0
- MailKit explicit TLS/cancellation connection API:
  https://mimekit.net/docs/html/M_MailKit_Net_Smtp_SmtpClient_ConnectAsync_2.htm
- EF Core transaction and isolation guidance:
  https://learn.microsoft.com/en-us/ef/core/saving/concurrency#using-isolation-levels-for-concurrency-control
- MySQL InnoDB Serializable read locking (Oracle-hosted official manual):
  https://docs.oracle.com/cd/E17952_01/mysql-8.0-en/innodb-transaction-isolation-levels.html#isolevel_serializable
- Stable package versions:
  https://api.nuget.org/v3-flatcontainer/mailkit/index.json

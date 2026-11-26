# Local-account password recovery

Date: 2026-11-26
Status: written specification approved; implemented and verified (see task-2 report).

## 1. Intent and scope

Let people with an active local email/password account recover access through an
emailed, expiring, single-use reset link. Deliver real email through Gmail SMTP
when configured, with a deliberately Development-only logging fallback.

Success means that eligible users can reset their password, all their refresh
sessions are revoked, and anonymous callers cannot distinguish account existence
or eligibility through the response or waiting for SMTP. Password policy remains
owned by ASP.NET Core Identity.

Keep existing login behavior, UI conventions, `CircuitDbGate`, and `ServiceResult`.
Do not create migrations, run git commands, print secret values, or send real
email from the agent environment. The user commits manually.

## 2. Verified repository context

- `hotjob/Program.cs` already reads `.env.local` into `IConfiguration`, translates
  double underscores to colons, and currently does so in every environment.
- `.gitignore` already ignores `.env.local`. `hotjob/.env.example` already exists;
  extend it without replacing unrelated entries.
- Identity registers default token providers and enforces password policy.
- `AccountService.ChangePasswordAsync` calls
  `IAuthService.LogoutAsync(null, userId, cancellationToken)` after changing the
  password. That mechanism revokes all unrevoked refresh-token rows for the user.
- `AuthService.LoginAsync`, refresh, and session restoration check `IsActive`.
- Login's forgot-password link currently points to `/`.
- `PasswordChangedNotice` renders a hardcoded message only for the string flag
  `"1"`. Recovery will use the same convention with its own notice.
- Account APIs map service results to HTTP responses. Anonymous registration
  provides an existing pattern for anonymous recovery endpoints.
- The UI is English. Components use Interactive Server per page; `Routes` does
  not currently declare a global render mode. Recovery must work in Interactive
  Server without changing unrelated rendering configuration.
- Tests use xUnit, SQLite in memory, real Identity managers, and `HtmlRenderer`.

These statements come from file inspection, not git history. Local-secret values
were not read or displayed.

## 3. Architecture and rationale

### Selected approach: bounded in-memory queue

The request path validates bounded input, applies abuse limits, and attempts to
enqueue an email address plus a validated application base URL. It returns the
same `ServiceResult<string>` success message in every account-related outcome:

> If an active account with a local password matches that email, we will send a password reset link.

The request path does not query the account, generate a token, await SMTP, or
report whether the queue accepted the request. A hosted worker creates an
independent dependency-injection scope for each job and performs eligibility
lookup, token generation, email composition, and delivery.

This removes account lookup and delivery latency from the response-critical
path. It is not a claim of mathematical constant-time behavior: input validation,
rate-limit lookup, queue admission, and shared host load can still affect timing,
but they do not branch on account existence.

The queue has capacity 100, uses nonblocking admission, and has a single reader.
Queue-full and shutdown cases still return generic success. Queued work contains
no generated token. The worker must not capture a page, circuit, request-scoped
DbContext, or scoped UserManager from the requesting caller.

Catch failures per job, record only a fixed diagnostic and exception type, and
continue processing subsequent jobs. Host shutdown cancels outstanding delivery
through the worker's cancellation token.

Alternatives considered:

1. Synchronous SMTP with response padding: simpler but sensitive to timeout and
   network variance and therefore a weaker enumeration defense.
2. Durable external queue: survives restarts and scales better, but introduces
   infrastructure outside this task's scope.

### Service boundaries

- `IEmailSender.SendAsync(to, subject, htmlBody, ct)` abstracts transport.
- Account-recovery services expose `RequestPasswordResetAsync(email, baseUrl)`
  and `ResetPasswordAsync(email, token, newPassword)`, with optional cancellation
  parameters following repository conventions.
- Keep recovery orchestration in focused account-service files rather than
  growing the existing large `AccountService` with SMTP and queue internals.
- Share the same recovery service between Blazor pages and API controllers.
- A singleton rate limiter and queue are safe for concurrent callers. Identity,
  EF Core, and reset operations remain scoped.

## 4. Configuration and email delivery

Read settings through `IConfiguration`/`IOptions`, never directly from dotenv
inside individual services:

| Environment-style key | Meaning |
| --- | --- |
| `Email__Host` | SMTP host |
| `Email__Port` | SMTP port; default 587 |
| `Email__Username` | SMTP authentication username |
| `Email__Password` | SMTP authentication password/app password |
| `Email__From` | Sender mailbox |
| `App__BaseUrl` | Preferred absolute public application base URL |
| `PasswordReset__TokenLifetime` | TimeSpan value; default `01:00:00` |

Bind the positive token lifetime to Identity's
`DataProtectionTokenProviderOptions.TokenLifespan`. State in the final report
that this option also applies to other tokens using that provider. Use the same
configured lifetime when composing the email.

### Dotenv behavior

Retain the existing loader's supported locations and key conversion, but only
execute it in Development. Production configuration must come from the host's
configuration providers, not a local secrets file. Do not add real values to
appsettings or overwrite unrelated dotenv-example entries. Preserve the existing
ignore rule. Document the loader change and its deployment implication.

### Sender selection

| Configuration/environment | Sender |
| --- | --- |
| Nonblank host, username, and password, in any environment | `SmtpEmailSender` |
| Any of those missing, Development | `DevelopmentEmailSender` |
| Any of those missing, any other environment | `NotConfiguredEmailSender` |

Use a recent stable MailKit version and check package advisories, including its
transitive dependencies, during implementation. Record the actual version and
audit evidence in the final report; do not claim an audit before running it.

SMTP requirements:

- Connect with `SecureSocketOptions.StartTls`, default port 587.
- Authenticate using the configured username/password; use `Email:From`.
- Keep normal certificate validation enabled.
- Use a 15-second client timeout and a linked cancellation deadline of 30 seconds
  for the delivery operation. Dispose the client and disconnect where possible.
- Do not enable protocol logging or log bodies, links, credentials, or tokens.
- Failures are caught at the delivery/worker boundary. Log a fixed diagnostic
  category and exception type, not an exception object, message, or stack trace
  that might embed SMTP content or credentials. Never propagate delivery errors
  to the anonymous requester.

The Development sender logs the message, including its reset link, only after
checking the actual environment is Development. It must not log message contents
when directly instantiated in another environment. The not-configured sender
only logs a fixed warning, never the recipient or message contents, and returns
without throwing.

SMTP failure does not fall back to logging the reset link, even in Development.
Fallback is chosen from configuration, not from delivery success.

## 5. Request flow and abuse protection

Normalize email consistently with Identity for case-insensitive throttling;
trim input and impose a 254-character bound. Malformed, missing, oversized,
unknown, disabled, and Google-only addresses all produce the same service-level
success message. The browser may show ordinary required/email-format validation
before submission, which does not depend on account existence.

Before admission:

- One accepted attempt per normalized email per 60 seconds.
- At most 10 attempts per server-observed IP per 10-minute fixed window.
- Charge the IP budget even if an address is already on cooldown, so an IP
  cannot make unlimited repeated attempts against one address.
- Use injected `TimeProvider`, synchronized admission, expired-entry cleanup,
  hashed limiter keys, and at most 10,000 entries per key category. When live
  storage is full, deny admission generically instead of evicting live limits.
- Use the same limiter for API and Blazor entry points.

Capture the connection's server-observed remote IP in a server-owned scoped
context before request context becomes unavailable during circuit events.
Do not accept a body/query IP, and do not trust arbitrary forwarded headers.
Preserve the captured address through the circuit. If the host supplies no
address, use a shared unknown-address bucket rather than skipping the limit.
Test circuit-context retention and the missing-address behavior.

The worker looks up the account and generates a token only when both
`user.IsActive` and `UserManager.HasPasswordAsync(user)` are true. A linked Google
account that also has a local password remains eligible; a Google-only account
does not.

Generate with `GeneratePasswordResetTokenAsync`, UTF-8 encode, then
`WebEncoders.Base64UrlEncode`.

Prefer `App:BaseUrl`; otherwise use NavigationManager's base URI for Blazor and
the server request's base URI for the API. Accept only absolute HTTP(S) URLs with
no userinfo, query, or fragment. A nonblank but invalid configured URL is a safe
delivery failure, not a reason to trust a caller-supplied alternative. Operators
should configure `App:BaseUrl` in deployment to avoid relying on request Host.

Compose `/reset-password?email=...&token=...` with URL-encoded values. The English
HTML email has a clear reset action, a copyable link, the configured lifetime,
and an instruction to ignore unsolicited requests. HTML-encode inserted values.

## 6. Reset flow and security

`ResetPasswordAsync`:

1. Validate email length (at most 254 characters) and encoded token length (at
   most 4,096 characters), decode Base64Url, and handle malformed encoding without
   exposing exception details.
2. Find the user and require an active local-password account. Missing,
   ineligible, malformed, invalid, expired, and already-used links all return:
   `This reset link is invalid or has expired`
3. Call `UserManager.ResetPasswordAsync`. Map invalid-token errors to that same
   message; surface Identity password-policy descriptions for a valid token.
   Do not duplicate Identity's complexity or length rules in UI models.
4. On success, call `LogoutAsync(null, user.Id, cancellationToken)` and require
   successful refresh-session revocation before reporting completion.
5. Keep the password change and refresh-session revocation in a shared database
   transaction so revocation failure cannot leave a reported successful reset
   with surviving refresh sessions. No schema change is needed.

Unexpected reset/storage failures return a fixed temporary-failure message,
not exception details. Only committed success triggers the login redirect.

Use the real Identity implementation and test that successful reset changes the
security stamp, invalidates the same token on reuse, and prevents two independent
store contexts from successfully consuming the same token concurrently through
Identity's optimistic concurrency checks. Do not create a custom token store.

Reset never sets `IsActive`. Reject a link if an account was disabled after it
was issued. Also test the existing login service after a successful reset
followed by account disablement.

Revoking refresh sessions does not invalidate already-issued JWT access tokens
before their expiration. This matches the existing change-password behavior.

## 7. Pages and API

### `/forgot-password`

- English email form, normal email autocomplete and accessible labels.
- Uses the recovery service directly and wraps scoped service calls with
  `CircuitDbGate`.
- Disables controls during submission and ignores duplicate submissions.
- Always displays the hardcoded generic success response after a valid submit.
- Provides a link back to `/login`.

### `/reset-password`

- Reads `email` and `token` from query parameters; neither is editable input.
- New-password and confirmation fields use `autocomplete="new-password"`.
- UI validation only requires the fields and matching confirmation; Identity
  owns all password-policy validation.
- Missing or rejected link shows the fixed invalid/expired message and a link
  to `/forgot-password`. Password-policy errors preserve the form for retry.
- Scoped service work runs through `CircuitDbGate`; no background work uses the
  circuit's DbContext.
- On success, discard password values and the EditContext, clear the local
  authentication state, and perform best-effort refresh-cookie cleanup like the
  existing change-password UI. Redirect with force load to
  `/login?passwordReset=1`, even when cookie cleanup is unavailable.
- Add no-referrer page metadata to reduce reset-link leakage through navigation.
  Application logs introduced here must never log reset URLs. Infrastructure
  access-log configuration remains an operator responsibility.

### Login

Only change the forgot-password destination, add the query flag, and render a
small notice component matching `PasswordChangedNotice`. Only the exact flag
`"1"` shows a hardcoded success message; never render query-provided text.
Do not change login submission or authentication behavior.

### API

Add `[AllowAnonymous]` actions:

- `POST /api/account/forgot-password`: email request; HTTP 200 with the fixed
  generic message for account-related outcomes, rate limiting, and queue
  admission failure.
- `POST /api/account/reset-password`: email, token, new password; HTTP 204 on
  success; HTTP 400 with service errors for invalid links or policy failure.

Use the same services as the pages and existing controller response conventions.
Malformed JSON or an unbindable request may still receive the framework's normal
HTTP 400; that behavior does not depend on account existence. Do not accept a
public baseUrl override in the API payload.

## 8. Verification and acceptance criteria

Use fake senders only in automated tests. Never instantiate a configured
real-delivery path to send mail or start the application against local secrets.

Add xUnit coverage for:

- Identical request responses for unknown, disabled, Google-only, malformed,
  eligible, rate-limited, queue-full, and failed-delivery cases.
- Request completion without waiting for lookup/delivery, and worker isolation
  from the caller's scoped resources.
- Token generation only for active local-password users.
- Base URL precedence, URL/HTML encoding, and configured lifetime in email.
- Successful reset changes the password, updates the stamp, revokes all refresh
  sessions but not another user's sessions, and invalidates old refresh tokens.
- Invalid, malformed, expired, reused, and concurrently submitted tokens.
  Exercise actual Identity/Data Protection behavior, not only mocked outcomes.
- Disabled-account reset rejection and existing login rejection after disablement.
- Identity password-policy errors, and successful retry with the same unconsumed
  token after a policy error.
- Atomic rollback if session revocation fails.
- Per-email and per-IP limits, normalization, time-window expiry, concurrent
  admission, cleanup, capacity behavior, and shared unknown-IP bucket.
- Sender selection across configuration and environment combinations.
- Development sender's environment guard; safe not-configured and SMTP failure
  logs, including exceptions deliberately containing sentinel secrets/tokens.
- Dotenv environment gating and `__` key conversion using temporary dummy files.
- Fixed login notice flags; form validation and success/error behavior, password
  clearing, cookie-cleanup failure, and circuit-gate handling.

Check the MailKit dependency graph for published vulnerability advisories.
Run `dotnet test` from the repository root and report actual passed, failed, and
skipped counts. Do not suppress, delete, or skip existing tests to get green.

## 9. Deliverables

Expected change areas (exact focused file names may follow existing conventions):

- Email options, interface, three senders, and DI selection.
- Recovery options, queue/hosted worker, limiter, caller context, services, DTOs,
  and form models.
- Program registration/configuration and Development-only dotenv loading.
- Two recovery pages, a reset notice, and minimal login changes.
- Account-controller endpoints and targeted updates to test fixtures.
- Existing `.env.example` extended with commit-safe values.
- New xUnit tests and the final report:
  `docs/admin/task-2-result.md`.

The report must list added/modified files; explain dotenv behavior and changes;
name the installed MailKit version; record design decisions and exact test
results; provide manual Gmail and Development-fallback browser instructions,
including inbox/spam checks; and identify limitations and assumptions. It must
contain no secret values.

## 10. Limitations and operational requirements

- Queue and limiter state are process-local and lost on restart. Multiple
  instances need shared abuse limits and durable queueing for equivalent global
  guarantees.
- Queue saturation, shutdown, or delivery failure can mean no email arrives
  despite generic success. Users can retry after cooldown.
- Gmail typically requires an app password and suitable account security
  settings; the operator supplies these privately.
- `Email:From` must be valid for the SMTP account. Missing/invalid From does not
  change sender selection, but delivery will fail safely.
- Persist/share Data Protection keys for predictable links across restarts and
  multiple hosts. Do not add key-store infrastructure in this task.
- Configure the public base URL and trusted proxy infrastructure in deployments;
  untrusted forwarded headers are not a source of client identity.
- Development logs deliberately contain reset links and must not be published.
- Existing access tokens retain their normal expiry; reset is not immediate JWT
  revocation or automatic unlocking of Identity lockout.

## 11. Review and execution gates

The conversational design and written specification were approved. The user
authorized native execution immediately after writing the plan and explicitly
waived another plan review. No specification or implementation commit was made.

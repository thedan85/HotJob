# Security Checklist

Verify each item against the actual code before reporting. Mark unverified items as **Potential risk**.

## Authentication
- Passwords hashed with a strong adaptive hash (ASP.NET Identity/PBKDF2, bcrypt, Argon2); never plaintext/MD5/SHA1.
- Lockout/rate limiting on login and password reset; generic error messages (no user enumeration).
- JWT: validate issuer, audience, lifetime, signing key; strong secret not in source; sensible expiry; refresh-token rotation/revocation.
- Cookies: `HttpOnly`, `Secure`, `SameSite`; sliding expiration reasonable.
- Password reset/email confirm tokens are single-use, expiring.

## Authorization / RBAC / IDOR
- Every non-public endpoint has `[Authorize]` (or a global fallback policy); `[AllowAnonymous]` is deliberate.
- Roles/policies enforced server-side; client hiding is not security.
- **IDOR**: for any `id` from the client, verify the resource belongs to / is permitted for the current user (compare owner ID from claims, not from request body). Look for `GetById(id)` without ownership checks.
- No privilege escalation via mass assignment (role/IsAdmin/OwnerId bindable from request).
- Admin-only actions protected on API as well as UI.

## Injection
- SQL: no string concatenation/interpolation into `FromSqlRaw`, `ExecuteSqlRaw`, ADO.NET commands; use parameters.
- Dynamic LINQ/ORDER BY from user input is whitelisted.
- Command/path injection: no user input into process args or file paths unsanitized; guard against path traversal (`..`).

## XSS / CSRF
- Razor encodes by default; flag `Html.Raw`, `@Html.Raw`, `innerHTML`, `dangerouslySetInnerHTML`, unsanitized rich text.
- State-changing MVC actions use antiforgery tokens; cookie-auth APIs need CSRF protection; SameSite configured.
- Content-Security-Policy and security headers where feasible.

## Input validation / over-posting
- Server-side validation on all inputs (length, range, format, enums); DTOs with validation attributes.
- Bind to DTOs/ViewModels, not entities (over-posting); `[Bind]` whitelist if binding entities.
- Validate pagination bounds, sort fields, and filters.

## Sensitive data exposure
- No secrets (connection strings, keys, SMTP, API tokens) committed in appsettings/source; check git-tracked files.
- API responses don't leak password hashes, tokens, internal IDs, or other users' PII; no entities returned directly.
- Errors don't expose stack traces/SQL in production (`UseDeveloperExceptionPage` only in Development).
- Logs exclude passwords/tokens/PII. HTTPS enforced; HSTS in production.

## File upload
- Validate extension and content type and magic bytes; enforce size limits.
- Generate server-side file names; never trust client file name; prevent path traversal.
- Store outside web root or serve via controlled endpoint; block executable types; scan if feasible.
- Authorize downloads (no public enumerable URLs for private files).

## Other
- CORS not `AllowAnyOrigin` with credentials; limited origins/methods/headers.
- Rate limiting on expensive/public endpoints.
- Dependencies: check for known-vulnerable packages (`dotnet list package --vulnerable`).
- Open redirects: validate `returnUrl` (`Url.IsLocalUrl`).
- SSRF: user-supplied URLs fetched server-side are restricted.

## Severity hints
Unauthenticated data access/IDOR on sensitive data, SQL injection, committed production secrets → CRITICAL/HIGH. Missing antiforgery on a state-changing form, missing rate limiting → MEDIUM. Missing security headers → LOW/INFO.

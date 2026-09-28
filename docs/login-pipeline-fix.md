# Login Pipeline + Navbar Auth — Errors, Descriptions, Suggested Fixes

> Scope: A full fix. HttpOnly cookie via API controller. UI stays `.razor` Blazor Server (not MVC `.cshtml`). No code fixed yet — this file is the todo for later.
> Date: 2026-09-28

## Context: Razor vs Blazor

Repo uses Blazor Server Razor Components (`.razor`, `@page`, `@rendermode`, `EditForm`, `AuthorizeView`), not MVC Razor Pages (`.cshtml`).
Relevant files:
- `hotjob/Components/App.razor:27-29`
- `hotjob/Components/Routes.razor:1-6`
- `hotjob/Components/Layout/NavMenu.razor:1-56`
- `hotjob/Components/Layout/MainLayout.razor:1-19`
- `hotjob/Components/Pages/LoginPage.razor:1-113`
- `hotjob/Components/Pages/Home.razor:1-64`
- `hotjob/Controllers/AuthController.cs:1-98`
- `hotjob/Services/Authentication/AuthService.cs:1-220`
- `hotjob/Services/Authentication/HotJobAuthStateProvider.cs:1-53`
- `hotjob/Program.cs:166-169,186-199`

Keep `.razor`. HttpOnly cookie flow works from Blazor via `HttpClient` -> API controller. Do not read `document.cookie` in JS — HttpOnly is unreadable by design.

---

## Error 1 — Build broken: `SeedOptions.Demo` missing

**Error:**
`hotjob/Data/DemoSeeder.cs(29,25): error CS1061: 'SeedOptions' does not contain a definition for 'Demo'`

**Description:**
- `hotjob/Data/IdentitySeeder.cs:16-29` defines only `AdminEmail`, `AdminPassword`, `CandidateEmail`, `CandidatePassword`.
- `hotjob/Data/DemoSeeder.cs:29` reads `seed.Demo`, with `demo.CompanyName`, `demo.EmployerEmail`, `demo.EmployerPassword`, `demo.CandidateEmail`, `demo.CandidatePassword`, `demo.EmployerName`, `demo.CandidateName`.
- `hotjob/Tests/SeedTests/DemoSeederTests.cs:13-25` expects `SeedOptions { Demo = new SeedDemoOptions { ... } }`.
- `hotjob/Tests/SeedTests/IdentitySeederTests.cs:10-18` expects `SeedOptions { Admins = [ new SeedAdminAccount { ... } x3 ] }`.
- `hotjob/appsettings.json:24-39` configures `Seed:Admins[]` + `Seed:Demo{}` — nothing binds to current model.
- Nothing runs (`dotnet build`, login check, navbar check) until this compiles.

**Suggested fix:**
1. In `hotjob/Data/IdentitySeeder.cs`, restore:
   ```csharp
   public sealed class SeedAdminAccount { public string Email { get; set; } = ""; public string Password { get; set; } = ""; }
   public sealed class SeedDemoOptions {
     public string CompanyName { get; set; } = ""; public string EmployerEmail { get; set; } = "";
     public string EmployerPassword { get; set; } = ""; public string EmployerName { get; set; } = "";
     public string CandidateEmail { get; set; } = ""; public string CandidatePassword { get; set; } = "";
     public string CandidateName { get; set; } = "";
   }
   public sealed class SeedOptions {
     public const string SectionName = "Seed";
     public List<SeedAdminAccount> Admins { get; set; } = new();
     public string AdminEmail { get; set; } = "";      // legacy fallback, keep
     public string AdminPassword { get; set; } = "";   // legacy fallback, keep
     public string CandidateEmail { get; set; } = "";
     public string CandidatePassword { get; set; } = "";
     public SeedDemoOptions? Demo { get; set; }
   }
   ```
2. Update `IdentitySeeder.SeedAsync` to seed `seed.Admins` list + legacy `AdminEmail` fallback, keep signature `(RoleManager, UserManager, ApplicationDbContext dbContext, SeedOptions, ILogger, CancellationToken)` to match `hotjob/Program.cs:186-192`.
3. Keep null-guard in `DemoSeeder.cs:29-38`: if `seed.Demo is null` or fields empty → skip with log.
4. Update `hotjob/.env.example` to document `Seed__Admins__0__Email`, `Seed__Demo__CompanyName`, etc.
5. Verify: `dotnet build hotjob/hotjob.csproj` passes.

## Error 2 — `IdentitySeederTests` signature mismatch

**Error:**
Future compile error once Error 1 is fixed. Tests call 4-arg overload, impl needs 6 args.

**Description:**
- `hotjob/Tests/SeedTests/IdentitySeederTests.cs:25,40,41,56,68`: `IdentitySeeder.SeedAsync(fx.RoleManager, fx.UserManager, ThreeAdmins(), NullLogger.Instance)`.
- Impl `hotjob/Data/IdentitySeeder.cs:40-46`: `(roleManager, userManager, dbContext, seed, logger, cancellationToken)`.
- `hotjob/Tests/hotjob.Tests/Support/TestFixture.cs:87-117` already exposes `fx.Db`, so fix is trivial.

**Suggested fix:**
Update all 4 call sites to `IdentitySeeder.SeedAsync(fx.RoleManager, fx.UserManager, fx.Db, seed, NullLogger.Instance)`. Or add 4-arg overload forwarding with `db` from scope — prefer updating tests, single source of truth in impl.

## Error 3 — Login bypasses `AuthController` cookie

**Error:**
No `Set-Cookie: hotjob_refresh` ever reaches browser on Blazor login path.

**Description:**
- `hotjob/Controllers/AuthController.cs:22-34` `POST /api/auth/login` calls `AuthService.LoginAsync` then `SetRefreshCookie(rawToken, expires)` (`HttpOnly, SameSite=Lax, Path=/api/auth, Secure=Request.IsHttps`).
- `hotjob/Components/Pages/LoginPage.razor:88` calls `IAuthService.LoginAsync` directly server-side. Controller never runs. Raw refresh token lives only in scoped `AuthSession`, discarded on circuit end.
- Same for refresh/logout: `AuthController.cs:40-73` reads `Request.Cookies[hotjob_refresh]`, but Blazor `NavMenu.razor:52` calls `AuthService.LogoutAsync` directly with in-memory token.

**Suggested fix (HttpOnly via API, per request):**
1. Stop calling `IAuthService` from `.razor`. Inject `HttpClient` (or `IHttpClientFactory`) in `LoginPage` and `NavMenu`.
2. `LoginPage.SubmitAsync`: `POST /api/auth/login` with `LoginRequest { Email, Password }`, `AllowCredentials`. Let controller set cookie. Parse `AuthResponse` body (access token + userId + email + roles), build client-side `AuthTokens`, call `AuthStateProvider.SignIn(tokens)`, then `NavigateTo("/", forceLoad:false)`.
3. Register in `hotjob/Program.cs`: `builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) })` or named client `AuthApi` with `BaseAddress = NavigationManager.BaseUri`. For Blazor Server, use `IHttpContextAccessor` or relative URI via `NavigationManager`.
4. API antiforgery: `/api/auth/*` is `ApiController` + `UseAntiforgery`. Browser `fetch` from Blazor Server needs antiforgery token header or `[IgnoreAntiforgeryToken]` on `AuthController`. Pick one and document. Easiest: add `[IgnoreAntiforgeryToken(Order = 1001)]` on login/refresh/logout if no CSRF token flow, rely on `SameSite=Lax` + short access-token lifetime. If strict, implement `GET /api/antiforgery/token` and send `RequestVerificationToken` header.
5. Do not store raw refresh token in localStorage. Access token in memory only; refresh via cookie (`POST /api/auth/refresh` with empty body, cookie auto-sent).

## Error 4 — Auth state lost after `NavigateTo("/")`, navbar reverts to Sign in

**Error:**
Login succeeds, then navbar shows Sign in again.

**Description:**
- `hotjob/Services/Authentication/HotJobAuthStateProvider.cs:6-15` `AuthSession.Tokens` is in-memory, `Scoped` (`hotjob/Program.cs:166-169`). No persistence: no `ProtectedLocalStorage`, no `PersistentComponentState`, no cookie read.
- `hotjob/Components/Pages/Home.razor:1` has no `@rendermode` → static SSR. `MainLayout`, `NavMenu` also static. Post-login `NavigationManager.NavigateTo("/")` in `LoginPage.razor:92` creates new render scope/circuit → fresh `AuthSession` → `Anonymous` → `NavMenu.razor:39-41` `<NotAuthorized>` renders Sign in.
- `App.razor:27` `<CascadingAuthenticationState>` prerenders as anonymous for same reason.

**Suggested fix:**
1. Add restore path. Minimal HttpOnly-only design:
   - Add `GET /api/auth/me` in `AuthController` (authorize via access-token `Authorization: Bearer` header OR via refresh cookie → return `{ UserId, Email, DisplayName, Roles }`). Blazor calls it on startup to rehydrate `AuthSession`.
   - In `NavMenu.razor` / `App.razor` `OnAfterRenderAsync(firstRender)`: `GET /api/auth/me` (with credentials), if 200 → `AuthStateProvider.SignIn(tokensFromMe + cached access token)`, `StateHasChanged`.
2. Alternative accepted: store access token in `ProtectedLocalStorage`, rehydrate in provider. Still keep refresh in HttpOnly cookie only. Never store refresh in localStorage.
3. Add `builder.Services.AddCascadingAuthenticationState()` in `Program.cs` (.NET 8+ pattern) alongside existing `AddAuthorizationCore`.
4. Verify manually: login → `/` shows avatar + name + Sign out, reload page still shows it, new browser tab still shows it (via cookie refresh).

## Error 5 — `NavMenu` not interactive, Sign out dead, `AuthorizeView` stale

**Error:**
`@onclick="SignOutAsync"` never fires; profile icon/name/logout never update without full reload.

**Description:**
- `hotjob/Components/Layout/NavMenu.razor:1-55` has no `@rendermode`. Inherits static from `MainLayout`. `@onclick` requires `InteractiveServer`.
- `SignOutAsync` (`NavMenu.razor:48-54`) reads `AuthSession.Tokens`, calls `AuthService.LogoutAsync`, calls `AuthStateProvider.SignOut()`, but never calls API to clear `hotjob_refresh` cookie, never navigates, never `StateHasChanged`.
- `AuthorizeView` (`NavMenu.razor:26-42`) already has correct markup: `.nav-avatar` SVG + `@authState.User.Identity?.Name` + `Sign out` button vs `Sign in` link. Markup is fine; wiring is not.

**Suggested fix:**
1. Add `@rendermode InteractiveServer` to `NavMenu.razor` (or to `MainLayout.razor` if whole shell should be interactive).
2. Inject `HttpClient` + `NavigationManager` in `NavMenu`. `SignOutAsync`: `POST /api/auth/logout` (cookie auto-sent, server does `Response.Cookies.Delete` per `AuthController.cs:75`), then `AuthStateProvider.SignOut()`, then `NavigationManager.NavigateTo("/", forceLoad:true)` to force re-render as anonymous.
3. Keep existing CSS classes (`nav-user`, `nav-avatar`, `nav-sign-out` in `hotjob/wwwroot/css/NavMenu.css:21-25`) — no UI change needed.
4. Verify: click Sign out → cookie cleared (DevTools Application → Cookies), navbar flips to Sign in, `GET /api/auth/me` returns 401.

## Error 6 — `Routes` has no auth guard, no redirect to `/login`

**Error:**
Anonymous users see no 401 handling; authorized pages never force login.

**Description:**
- `hotjob/Components/Routes.razor:1-6` uses `<RouteView>` not `<AuthorizeRouteView>`. `App.razor` wraps in `<CascadingAuthenticationState>` but no `NotAuthorized` redirect.
- `Program.cs:92-93` registers both `AddAuthorization()` and `AddAuthorizationCore()` — redundant, harmless but confusing.

**Suggested fix:**
1. In `Routes.razor`, switch to:
   ```razor
   <AuthorizeRouteView RouteData="routeData" DefaultLayout="typeof(Layout.MainLayout)">
     <NotAuthorized><RedirectToLogin /></NotAuthorized>
   </AuthorizeRouteView>
   ```
   with small `RedirectToLogin.razor` calling `NavigationManager.NavigateTo("/login", true)`.
2. Keep `AddAuthorizationCore()` for Blazor, `AddAuthorization()` for controllers. Add `AddCascadingAuthenticationState()`.

## Error 7 — `DisplayName` missing for Employer/Admin, `AuthResponse` drops it

**Error:**
Navbar `title="Signed in as @authState.User.Identity?.Name"` shows email for non-candidates.

**Description:**
- `hotjob/Services/Authentication/AuthService.cs:200-204` `GetDisplayNameAsync` queries only `Candidates`. Employers/Admins → null → `HotJobAuthStateProvider.cs:46` falls back to `tokens.Email`.
- `hotjob/DTOs/Authentication/AuthDtos.cs:22-27` `AuthResponse(AccessToken, AccessTokenExpiresAtUtc, UserId, Email, Roles)` has no `DisplayName`, so API login path cannot return name even after fix.

**Suggested fix:**
1. Extend `GetDisplayNameAsync` to query `Employers.FullName` then `Candidates.FullName`, fallback null.
2. Add `DisplayName` to `AuthResponse` (nullable string) and to `GET /api/auth/me` response. Update `AuthControllerTests.cs` expectations.
3. `HotJobAuthStateProvider.CreatePrincipal` already prefers `DisplayName ?? Email` — no change needed there.

## Error 8 — Duplicate/unused wiring, missing `HttpClient`

**Description:**
- No `HttpClient` registration for Blazor → API calls. Needed for Error 3/4/5 fix.
- `Program.cs` JWT wiring (`AddAuthentication().AddJwtBearer()`) protects API controllers, but Blazor `AuthenticationStateProvider` is custom `HotJobAuthStateProvider`, unrelated to JWT middleware — correct by design, but confusing. Document: JWT middleware validates `Authorization: Bearer` on `/api/*`; Blazor UI uses `HotJobAuthStateProvider` + HttpOnly cookie.

**Suggested fix:**
Add named `HttpClient` with `BaseAddress = new Uri("https://localhost:xxxx/")` or relative via `NavigationManager`, `DefaultRequestHeaders` accept JSON. Enable `AllowCredentials` equivalent (cookies auto-sent server-side in Blazor Server via `HttpContext`, but `HttpClient` from server needs `CookieContainer` or `IHttpContextAccessor` forwarding — pick `IHttpContextAccessor` + `Cookie` header forward, or call `AuthService` server-side only for refresh while controller sets cookie via `HttpContext.Response`. Decide in implementation, note tradeoff in PR.)

---

## Suggested implementation order

1. Error 1 + 2: restore `SeedOptions`, fix tests, `dotnet build` green.
2. Error 7: add `DisplayName` to `AuthResponse` + service (small, testable).
3. Error 3: route Blazor login/logout through `AuthController` (`HttpClient`, cookie set/clear). Manual check DevTools → `hotjob_refresh` HttpOnly present.
4. Error 4 + 5: `NavMenu` interactive, `GET /api/auth/me` restore, sign-out clears cookie + reload.
5. Error 6 + 8: `AuthorizeRouteView`, `AddCascadingAuthenticationState`, `HttpClient` registration.
6. Final verify: `dotnet build`, `dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj`, manual: register → login → `/` shows avatar + name + Sign out → reload persists → Sign out flips to Sign in.

## Manual verification checklist (for later)

- [ ] `dotnet build hotjob/hotjob.csproj` passes, 0 errors.
- [ ] Seed `candidate@example.com / Candidate123!` exists (via `Seed__CandidateEmail` or `Seed:Demo`).
- [ ] Login with correct creds → 200, `Set-Cookie: hotjob_refresh=...; HttpOnly; Path=/api/auth`.
- [ ] After login `/` navbar shows profile SVG icon + name + Sign out, not Sign in.
- [ ] Reload `/` still shows profile (cookie restore works).
- [ ] Sign out → cookie cleared, navbar shows Sign in, `/api/auth/me` → 401.
- [ ] Wrong password → 401 generic `Invalid email or password`, no enumeration.

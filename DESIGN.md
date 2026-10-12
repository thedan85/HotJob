# HotJob — DESIGN.md (as-built)

Scope: reflects `hotjob/Components/**/*.razor` + `hotjob/wwwroot/css/*.css` as of 2026-10-09.
Blazor components (`.razor`) only. No Razor Pages (`.cshtml`) in HotJob — zero `.cshtml` files; the `MyApp/` `.cshtml` sample is a separate project and out of scope.
Old vision (Top Employers, `/it-jobs`, ATS boards, admin) is moved to §10 gaps — nothing below describes unbuilt pages as done.

Stack: Blazor Server (.NET 10), `InteractiveServer` render mode on interactive pages, EF Core Identity + JWT Bearer, MySQL. Auth JS bridge `wwwroot/js/auth.js` (`hotjobAuth.login/me/logout`). Font Lexend (Google Fonts, App.razor).

## 1. Shell

`Components/App.razor`: Bootstrap + `app.css` + one CSS file per page/layout (`MainLayout/NavMenu/RegisterLayout/ReconnectModal/Home/RegisterPage/LoginPage/Profile`), Lexend, `CascadingAuthenticationState > Routes > ReconnectModal`, `js/auth.js` before `blazor.web.js`.

`Components/Routes.razor`: `Router` + `AuthorizeRouteView DefaultLayout=MainLayout`. Not authorized → `RedirectToLogin` when anonymous, else "You do not have access". `FocusOnNavigate h1`.

Two layouts, no shared UI components (`JobCard`/`SkillChip`/etc. do not exist):
- `Layout/MainLayout.razor`: `.page > NavMenu + main > .content-shell > article.content`. CSS `MainLayout.css`: shell max 1340px, content pad 48/72 (32/48 mobile).
- `Layout/RegisterLayout.razor`: `.register-shell > header.register-header + main.register-content + footer.register-footer`. Header gradient `269.85deg #1b262c→#0f4c75`, footer `177.12deg #0f4c75→#1b262c`. Header links (`/`, `/`, `/`, `/`) and footer links are `/` placeholders.

## 2. Routes (actual)

| Route | File | Layout | Mode | State |
|---|---|---|---|---|
| `/` | `Pages/Home.razor` | Main | static | Built, no DB query |
| `/register` | `Pages/RegisterPage/RegisterPage.razor` | Register | InteractiveServer | Built, wired to `IAccountService.RegisterAsync` |
| `/login` | `Pages/LoginPage.razor` | Register | InteractiveServer | Built, wired via JS `hotjobAuth.login` + `HotJobAuthStateProvider.SignIn` |
| `/profile` | `Pages/Profile.razor` | Main | InteractiveServer | Built, profile fields wired; CV list is hardcoded mock |
| `/counter`, `/weather` | template defaults | Main | mixed | Placeholders — linked from nav, replace when features land |
| `/not-found`, `/Error` | `NotFound`/`Error` | Main/none | static | Built |

## 3. NavMenu (`Layout/NavMenu.razor`, `NavMenu.css`)

Sticky `.top-row`, gradient `110deg #1b262c→#0f4c75`. Brand `hot` (#BBE1FA) + `job` (white).
Links: Home `""`, Explore jobs → `/counter`, Companies → `/weather`, For employers → `/register` (all three are placeholders, see §10).
Right side: static `EN / VI` (no-op), `AuthorizeView`: authorized → avatar + `User.Identity.Name` linking `/profile` + `Sign out` (`hotjobAuth.logout` → `SignOut`, goto `/`); anonymous → `Sign in` (`/login`).
Per-circuit session restore in `OnAfterRenderAsync(firstRender)`: `hotjobAuth.me` → `SignIn`, cookie-gone → `SignOut`. Mobile: checkbox hamburger, dropdown panel at ≤720px.

## 4. Home (`Pages/Home.razor`, `Home.css`)

Hero `.home-hero` (gradient `#eaf4fb→#f9fcfd→#d8edf8`, radius 10): eyebrow, `h1` "Find work that fits.", lede, `form.job-search` GET to `/it-jobs` (fields `keyword`, `city`), trending pills → `/it-jobs?keyword=Java|ReactJS|.NET|Tester`. Note: duplicate `<PageTitle>` (second wins); `/it-jobs` route does not exist yet so search 404s (§10).
Second section: `.shortcut-grid` 3 cards → Get matched `/register`, Meet great teams `/companies` (missing route), Know your value `/register` (dark variant). No campaign banner, no Top Employers, no articles, no DB query.

## 5. Register (`/register`)

Split `.register-card` 1fr/1fr (stacks ≤960px). Form panel: eyebrow "Candidate Account Creation", h1, intro; `EditForm RegisterAccountRequest` (`FullName`, `Email`, `Password`, `AccountType=Candidate` hardcoded — no employer toggle). Password Show/Hide toggle, hint (6+ chars, upper/lower/digit/special), consent checkbox gates submit, Terms/Privacy links → `/` placeholder. Submit → `AccountService.RegisterAsync`, success message + form reset, errors joined. Login prompt → `/login`.
Benefits panel (dark gradient `#1b262c→#0f4c75`): "70,000+ Talents", 3 items (AI matching / confidential search / salary reports), 5-star quote block.

## 6. Login (`/login`)

Split `.login-card` 1fr/1fr (stacks ≤860px). Form: "Welcome back" / "Sign in to HotJob"; `EditForm LoginRequest` (`Email`, `Password`), Forgot password? → `/` placeholder, Show/Hide toggle. Submit → browser fetch `hotjobAuth.login(email, password)` (HttpOnly refresh cookie lands on browser, never via server HttpClient) → `AuthStateProvider.SignIn(tokens)` → goto `/`. Failures show `.form-message.error`. Prompt → `/register`. Benefits panel: "Make work fit your life." + 3 numbered points. Styling note: `.submit-button/.eyebrow/.password-wrap/.form-message` reused from `RegisterPage.css` (all CSS is global via App.razor).

## 7. Profile (`/profile`, `Profile.css`)

Header: eyebrow, h1 "Candidate Profile & CV Management", "Preview public profile" (`#personal-info` anchor), Save button → `SaveProfileAsync` (disabled while saving).
Layout: sidebar 290px + content (`1300px` max, stacks on mobile): avatar initials (from FullName), Position/"Candidate", JobSeekingStatus, anchor nav (personal / CVs(count) / preferences).
Boxes: (a) Personal details — `FullName/Email/Address/Location/Position/LinkedInUrl/Summary`; (b) CV management — badge `n/5`, static upload dropzone (`Browse files` no-op), list from hardcoded `List<CvItem>` (3 `Alex_Nguyen_*` entries, Default badge; Set-default/Download/Delete no-ops); (c) Career preferences — `JobSeekingStatus/PreferredWorkModel/PreferredLocation`. Loading + error states (`Please sign in…`).
Code: userId from `NameIdentifier ?? "sub"`; `GetProfileAsync` on init, `UpdateProfileAsync` + `RefreshIdentity` on save; `Initials` = first letters of 2 name parts.

## 8. Profile ↔ backend map (`DTOs/Authentication/AccountDtos.cs`, `Models/Users/Candidate.cs`)

Rendered: `FullName, Email, Address, LinkedInUrl, Position, Summary, JobSeekingStatus, PreferredWorkModel, PreferredLocation`. Not rendered (supported by DTO, add inputs when needed): `DateOfBirth, Gender, AvatarUrl, GithubUrl`. `UpdateProfileRequest`: null = no change, empty = clear (nullable); email change → `SetEmail/SetUserName`, `EmailConfirmed=false`, 409 on duplicate. `POST change-password` exists backend-only, no UI. CV section maps to nothing — wire to `Resume` model/service when built (`ponytail:` upload + default-flag endpoints, then delete `CvItem` mock).

## 9. UI tokens (as-built, `app.css`)

```css
--hj-ink:#1B262C; --hj-primary:#0F4C75; --hj-primary-light:#BBE1FA; --hj-accent:#3282B8;
--hj-page-bg:#F5F8FA; --hj-surface:#FFF; --hj-border:#DCE5EA; --hj-muted:#5B6C75;
--hj-radius-sm:6px; --hj-radius-md:10px;
--hj-shadow-sm:0 2px 10px rgba(27,38,44,.07); --hj-shadow-lg:0 16px 40px rgba(27,38,44,.16);
```

Buttons are page-scoped, not shared: `.search-button` (Home, 54px, primary→ink hover), `.submit-button` (Register 50px, primary→ink hover), `.profile-btn-primary/secondary/small` (Profile). Focus rings `rgba(50,130,184,.24–.35)`. Old doc's `--i-*` tokens and `.ibtn/.itag/.icontainer/HjButton` do not exist — do not reference them.

## 10. Known gaps (fix when features land)

1. `/it-jobs`, `/companies`, `/companies/best` routes missing — Home form/trending/cards link into 404. 2. Nav Explore-jobs→`/counter`, Companies→`/weather` placeholders. 3. Register is candidate-only; `/become-employer` flow not built. 4. Forgot-password, Terms/Privacy, EN/VI, avatar edit, CV upload/actions are no-ops. 5. Candidate `Position` MaxLength drift: model 200 vs `UpdateProfileRequest` 100 — align to 200 when touching DTOs. 6. Home duplicate `<PageTitle>`.

## 11. Rules for new pages

One Blazor component (`.razor`, `@page` + `@rendermode` — never `.cshtml`) + one `css/<Name>.css` linked in `App.razor`; `InteractiveServer` only when calling services/JS; `EditForm + DataAnnotationsValidator` for forms; userId from `NameIdentifier ?? "sub"`; server-side paging (`AsNoTracking`, 10–20/page, no `<Virtualize>`); reuse `--hj-*` tokens, no new palette.

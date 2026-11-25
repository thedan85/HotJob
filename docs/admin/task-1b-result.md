# Task 1b — profile section navigation

Date: 2026-11-25

Status: implemented; full test suite and signed-in browser navigation checks passed.

## Work checklist

- [x] Inspect profile anchors, target IDs, application base URL, routing, CSS, and application JavaScript.
- [x] Reproduce navigation away from `/profile` in the signed-in browser.
- [x] Search other components for the same fragment-only anchor pattern.
- [x] Approve the short in-chat design.
- [x] Add focused regression coverage and implement the approved fix.
- [x] Verify mouse/keyboard navigation in the browser.
- [x] Run root-level `dotnet test` and record counts.
- [x] Complete this report with changed files, manual steps, and other affected locations.

## Confirmed root cause

`hotjob/Components/App.razor` sets `<base href="/">`. The five fragment-only links in the profile sidebar resolved against that base, not against `/profile`: for example, `href="#change-password"` resolved to `http://localhost:5168/#change-password`.

The live DOM confirmed this URL resolution for all four section links and the avatar shortcut. Activating **Manage CVs** with Enter reproduced navigation to `http://localhost:5168/#cv-management` and rendered the home page. All five target IDs exist on the profile page.

Inspected `App.razor`, `Routes.razor`, the anchors themselves, and the application scripts `auth.js` and `motion.js`. No application anchor click/hash navigation handler, custom enhanced-navigation handler, or per-link enhanced-navigation override was responsible. Blazor routing followed the incorrectly resolved home-page URL; this was not a password-form issue. The application base URL must remain unchanged for routing/assets.

## Fix summary

Changed only the five `href` values in `Profile.razor`:

| Shortcut | Correct destination | Existing target |
|---|---|---|
| Personal details | `/profile#personal-info` | Profile section |
| Manage CVs | `/profile#cv-management` | Profile section |
| Career preferences | `/profile#preferences` | Profile section |
| Change password | `/profile#change-password` | Section in `ChangePasswordSection.razor` |
| Set avatar URL | `/profile#avatar-url` | Avatar URL input |

These are still ordinary anchor elements: mouse activation, keyboard activation, and native link behavior remain available. The route stays `/profile`; only its fragment changes.

No additional JavaScript, click handlers, router changes, or CSS changes were needed. `wwwroot/app.css` already supplies `html { scroll-behavior: smooth; }` and reduced-motion overrides. `wwwroot/css/Profile.css` already supplies `.profile-box { scroll-margin-top: 92px; }`. All five targets were verified to exist exactly once in the live DOM.

No change-password logic, services, authentication behavior, or unrelated profile functionality was changed. No git commands were run.

## Files changed for task 1b

| Status | File | Change |
|---|---|---|
| Modified | `hotjob/Components/Pages/Profile.razor` | Five link destinations only. |
| Added | `hotjob/Tests/hotjob.Tests/ProfileTests/ProfileAnchorTests.cs` | Five regression cases checking actual Razor anchor destinations against the application's base URL and checking matching target IDs. |
| Added | `docs/admin/task-1b-result.md` | This report. |

## Test results

Before the fix:

```text
dotnet test --filter "FullyQualifiedName~ProfileAnchorTests"
Failed: 5, Passed: 0, Skipped: 0, Total: 5
```

All five cases failed with the same meaningful assertion: expected destination path `/profile`, actual destination path `/`.

After the fix, executed from `C:\Users\phulo\Desktop\HotJob`:

```text
dotnet test
Passed! - Failed: 0, Passed: 479, Skipped: 0, Total: 479
Duration: 51 s
Exit code: 0
```

The first post-fix build attempt was blocked by the running Windows `hotjob.exe` locking the executable. With user approval, stopped that specific dev-server process, ran the successful root-level test command, and restarted the server on `http://localhost:5168`.

The regression cases deliberately check the source markup rather than refactor the large page just to make it statically renderable. They require a source checkout containing `hotjob.slnx`. Real activation and scrolling were checked separately in Chrome.

## Browser verification

Used the existing signed-in browser session; did not save profile changes or submit a password change.

- Reproduced the original issue with keyboard activation before editing.
- After rebuilding, activated **all four section links and the avatar shortcut using both mouse clicks and Enter**.
- Every activation kept the path `/profile` with the expected fragment and scrolled to the matching target.
- The four section cards settled approximately 92px below the viewport top, matching the existing sticky-header offset.
- Confirmed computed document scroll behavior was `smooth` with reduced motion disabled, and confirmed the existing reduced-motion CSS remains unchanged.
- Inspected the resulting password section visually with a screenshot; its form remained intact and empty.
- Console capture included transient WebSocket/reconnection messages during the dev-server restart/runner timeout, and Back-Forward Cache disconnect messages. After restarting/reloading, a final Change password navigation produced no additional captured console errors. No console-wide clean-state claim is made.

## Manual browser test steps

1. With the rebuilt app running, sign in and open `http://localhost:5168/profile`. Wait for the profile to finish loading.
2. Click each sidebar item: **Personal details**, **Manage CVs**, **Career preferences**, and **Change password**. Confirm the address remains `/profile#...`, the matching section scrolls into view, and the home page never appears.
3. Activate the **+ / Set avatar URL** shortcut. Confirm `/profile#avatar-url` and the matching input.
4. Use Tab/Shift+Tab to focus each shortcut and press Enter. Confirm the same destination/scroll behavior and visible keyboard focus.
5. Click links in reverse order to verify scrolling up as well as down. At desktop width, section headings should stop below the sticky header.
6. Optionally enter an unsaved, non-sensitive profile field value, navigate between sections, and confirm it remains before reloading. Do not select **Save changes** unless you intend to keep it.
7. Enable the OS/browser reduced-motion preference and repeat navigation: scrolling should remain functional without forced animation.
8. Repeat at a narrow/mobile viewport to check link wrapping and target visibility.

## Other matching locations

- **Shared layout:** `hotjob/Components/Layout/MainLayout.razor`, `href="#main-content"` targeting the existing `id="main-content"`. On non-home routes the same base-URL resolution problem applies. It needs a current-route-aware destination rather than a hardcoded `/profile` replacement. Affected wherever `MainLayout` is used. Reported, not modified, as approved.
- **Employer applicants/interviews:** `hotjob/Components/Pages/Employer/Interviews.razor`, `href="#advance-status"` targeting the existing `id="advance-status"`, on `/employer/interviews`. The page also consumes a `jobId` query parameter, so an appropriate follow-up should preserve the current route/query and selection rather than blindly copy the profile-specific href. Reported, not modified, as approved; no employer-session browser check was performed.
- **Not affected by this pattern:** `hotjob/Components/Pages/JobDetail.razor` and `hotjob/Components/Pages/RecommendedJobs.razor` already use `/profile#cv-management`.

The component search found no other fragment-only `href` values after fixing the profile links.

## Remaining issues and assumptions

- The shared layout skip link and employer review link remain separate known issues, listed above.
- The fix follows the application's existing root-hosted `/profile` URL convention and the current `<base href="/">`.
- Existing NuGet advisories remain: NU1902 for `MySqlConnector` 2.4.0 and NU1903 for the test dependency `SQLitePCLRaw.lib.e_sqlite3` 2.1.10. Unrelated compiler warnings also remain; no dependencies, warning suppressions, or thresholds were changed.
- Mobile, assistive-technology, and reduced-motion emulation checks remain manual; the completed live checks were desktop mouse/keyboard navigation.

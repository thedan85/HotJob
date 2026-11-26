# Tasks 5–6: Premium plan display and Profile authorization

## Work checklist

- [x] Inspect Premium markup, Plan, candidate plan seed, billing selection and entitlement duration.
- [x] Inspect Profile, other candidate page guards, the custom authentication provider and redirect component.
- [x] Obtain approval for the short design.
- [x] Write and self-review the explicitly requested implementation plan.
- [x] Obtain approval for the implementation plan and execution method.
- [x] Fix A: add failing tests, implement database-backed plan display and unavailable-plan handling, and verify.
- [x] Fix B: add failing tests, apply the existing candidate guard and verify anonymous/candidate behavior.
- [x] Run the FULL `dotnet test` from the repository root and record passed/failed/skipped counts.
- [x] Complete manual browser verification, or explicitly record any blockers and unexecuted steps.
- [x] Complete the report with separate exact file lists, root causes and related-page findings.

## Fix A — Premium uses the database offer

**Summary:** Premium now loads `GetPurchasablePlanAsync` through the existing candidate Premium service and displays its name, price and duration in the plan card, price/term summary, explanatory copy and purchase label. Money uses `N0` with invariant culture and `VND`, matching `PaymentResult.razor`. Unavailable offers show “Premium is temporarily unavailable. Please try again later.” and disable subscribe/renew. Loading/error states never offer a hardcoded fallback. Database reads and checkout calls use `CircuitDbGate`; navigation occurs after releasing the gate.

**Root cause:** `Premium.razor` repeated the seed name `Premium Monthly`, price `99.000đ` and term `30 days`, without loading the catalogue. The existing service read only the candidate's subscription, not the purchasable plan.

**Source of truth:** `Plan.PremiumMonthlyCode` (`PREMIUM_MONTHLY`), candidate audience, active and positive-priced, matching checkout availability. No substitute plan or default price/duration is used. The seed remains 99000/30 days, but edited catalogue values are displayed. Existing checkout still charges `Plan.Price`; confirmed payment still extends by `Plan.DurationDays`. Billing, VNPay, seeds, schema and entitlement logic were not changed.

**Exact Fix A files (independent of B):**

1. Modified `hotjob/Services/Billing/CandidatePremiumService.cs`
2. Modified `hotjob/Components/Pages/Premium.razor`
3. Added `hotjob/Tests/hotjob.Tests/BillingTests/CandidatePremiumPlanTests.cs`
4. Added `hotjob/Tests/hotjob.Tests/BillingTests/PremiumPageTests.cs`

## Fix B — Profile candidate authorization

**Summary:** Profile now wraps its existing markup in `AuthorizeView Roles="Candidate"` with `Authorized Context="auth"` and the existing `RedirectToLogin` in `NotAuthorized`. This matches Notifications, SavedJobs, MyApplications and JobAlerts, rather than introducing an authorization attribute or new mechanism.

**Root cause:** Profile had no page-level authorization wrapper. Its initialization displayed an anonymous error message and relied on service checks instead of participating in the existing session-restoring guard. It could also retain visible markup after sign-out.

**Custom provider compatibility:** Automated tests render the real Profile with `HotJobAuthStateProvider`. They prove that anonymous markup contains no profile/forms; the actual `RedirectToLogin` lifecycle navigates an unsuccessful session check to `/login`; a successful candidate browser-session response restores access without navigation; already-signed-in candidates see their profile/password section; wrong-role identities see access denied; sign-out removes profile markup. Static HTML rendering does not trigger after-render callbacks, so tests explicitly invoke the real redirect component's `IHandleAfterRender` lifecycle. This is component-level runtime verification, not a browser end-to-end claim.

Profile's preexisting content is unchanged outside the wrapper, verified against the starting file snapshot. Change-password components, Premium badge logic, sidebar anchors, scroll behavior, CSS, custom provider, redirect component and service ownership checks were not changed.

**Exact Fix B files (independent of A):**

1. Modified `hotjob/Components/Pages/Profile.razor` — guard wrapper only.
2. Added `hotjob/Tests/hotjob.Tests/ProfileTests/ProfileAuthorizationTests.cs`
3. Modified `hotjob/Tests/hotjob.Tests/ProfileTests/ProfilePremiumBadgeTests.cs` — only two authorization-service registrations; existing badge tests unchanged.
4. Modified `hotjob/Tests/hotjob.Tests/ProfileTests/ProfileResumeFileTests.cs` — only the same two registrations; existing resume-file work and assertions preserved.

The two registration calls are `AddAuthorizationCore()` and `AddCascadingAuthenticationState()`. They allow existing standalone test renderers to exercise the newly guarded page. This task is not the Premium badge task or the resume attachment task.

## Verification results

Full command, run from `C:\Users\phulo\Desktop\HotJob` without a filter:

```text
dotnet test
Passed! - Failed: 0, Passed: 611, Skipped: 0, Total: 611
```

- Fix A service tests: **7 passed**. Read tests cover updated name/price/duration, no tracking, empty catalogue, inactive/wrong-audience/unrelated plans and nonpositive prices.
- Fix A regression selection: **57 passed, 0 failed, 0 skipped**, including the six new real-page tests and existing billing/VNPay/entitlement/Premium-gate tests.
- Fix B regression selection: **62 passed, 0 failed, 0 skipped**, including five new real-page authorization/session tests and existing password, badge, anchor and resume tests.
- TDD evidence: the missing read API failed compilation before implementation; all six Premium render tests failed before the page change; four authorization tests failed before the wrapper, while the normal-candidate regression already passed.
- Mutation check: temporarily inverting the new active-plan condition caused two selection tests to fail, confirming the tests catch that regression. The correct condition was restored.
- Final full root-level `dotnet test` after restoration also passed: **611 passed, 0 failed, 0 skipped**, duration 54 seconds.
- Source comparisons and protected-file hashes verify the narrow Profile changes and unchanged billing, VNPay, entitlement, password, Profile CSS, auth-provider and redirect implementations.
- One independent final reviewer inspected implementation, tests, snapshots and final suite output: **no Critical, Important or Minor findings**. Actual browser/cookie/visual behavior, live database/VNPay behavior and repository-history provenance were not independently verified.
- Existing NuGet warnings remain: MySqlConnector 2.4.0 (moderate, GHSA-473q-m89c-ghf8) and SQLitePCLRaw.lib.e_sqlite3 2.1.10 (high, GHSA-2m69-gcr7-jv3q). Dependencies were not changed or suppressed in this task.

## Browser status and manual test steps

**Not executed in a real browser.** The browser relay was connected, but no app listened on the configured local ports 5168/7023. `Program.cs` unconditionally migrates and seeds its configured MySQL database at startup. The agent did not start it against a potentially shared database or edit real catalogue/entitlement data to manufacture test cases. Automated component/SQLite tests above passed; the following browser checks remain manual.

Use a disposable development database and a normal candidate account. Do not change production/shared catalogue rows. If the development host redirects HTTP to HTTPS, use its configured HTTPS origin (`https://localhost:7023`).

### Profile

1. Sign out, then directly open `/profile` (not only via navigation). Expect the existing brief “Checking your sign-in...” state followed by `/login`. No profile information or editable forms should appear.
2. Sign in as a normal Candidate and open `/profile`. Expect personal details, CV management, preferences and the unchanged change-password section.
3. Hard-reload `/profile` while that candidate browser session is valid. Expect restoration and profile content, not an erroneous redirect to login.
4. Sign out while viewing Profile. Expect private markup to disappear and the normal guard to return to login.
5. Sign in with an Employer-only account and directly open `/profile`. Expect access denied, no candidate forms and no loop through login.
6. As a candidate, confirm the existing profile sidebar shortcuts still target the correct sections, including change-password. No CSS/scroll code was edited.

### Premium with an available plan

1. In the disposable database, locate the active candidate plan with code `PREMIUM_MONTHLY`; note its name, price and `DurationDays`.
2. Sign in as a candidate and open `/premium`. Check the plan heading/card, term/price summary, copy and subscribe label against that row. With the existing seed, expect `Premium Monthly`, `99,000 VND`, `30 days`.
3. Change only that disposable plan's name/price/duration to `Premium Flex`, `149000`, `45`; reload. Expect `Premium Flex`, `149,000 VND`, `45 days` everywhere offer data appears; no old 99000/30-day offer remains.
4. Confirm subscribe (or renew for an existing subscriber) is enabled once loading finishes. Do not submit a real payment for this display-only verification. Existing automated billing tests cover charging/granting behavior.

### Premium without an available plan

1. Set only that disposable plan's `IsActive` to false (prefer this over deleting a referenced plan); reload `/premium`.
2. Expect “Premium is temporarily unavailable. Please try again later.” and a disabled subscribe/renew button, with no old/default price or duration and no crash. Repeat for both unsubscribed and active-Premium candidates.
3. Re-enable the disposable plan and restore the original name/price/duration; reload and confirm the offer returns.

## Related-page audit

| Candidate-only page | Existing protection / finding |
| --- | --- |
| `Notifications.razor` | Candidate `AuthorizeView` + `RedirectToLogin`; no change needed. |
| `SavedJobs.razor` | Same guard; no change needed. |
| `MyApplications.razor` | Same guard; no change needed. |
| `JobAlerts.razor` | Same guard, named authorized context; no change needed. |
| `RecommendedJobs.razor` | Same guard; no change needed. |
| `WriteReview.razor` | Candidate guard + redirect; no change needed. |
| `Premium.razor` | Already Candidate-guarded; its guest sign-in message is intentional and preserved. Its hardcoded offer display is fixed by A. |
| `Profile.razor` | Missing guard fixed by B. |

No additional candidate-only pages missing this guard were found, and no additional candidate Premium price/duration literals remain in Razor components. Public job/company pages are intentionally public with candidate-only actions guarded within them; the shared payment-result page is not candidate-only. No extra pages were changed.

## Separate commit guidance and shared documentation

Fix A and Fix B have no shared changed product/test files or new cross-fix dependency. Use the exact file lists above for separate manual commits. The two existing Profile test files contain other preexisting work; this task adds only their authorization registrations.

Shared documentation changed:

1. Added `docs/superpowers/plans/2026-11-26-candidate-premium-profile-fixes.md`
2. Added `docs/admin/task-5-6-result.md`

Execution evidence (snapshots, ledger and logs) is retained at `C:\Users\phulo\AppData\Local\Temp\opencode\hotjob-candidate-premium-profile-fixes-2026-11-26`, outside the repository; it is not part of either fix's commit list. No git commands were executed during implementation and no secrets were printed.

### Subsequent commit request

After implementation, the user explicitly authorized a combined commit with summary `fix: read premium price and duration from plan & add authorize guard to profile page`. Git operations for that request supersede the original no-git instruction.

The attachment task was committed separately while this commit was being prepared. This commit includes only the remaining Premium changes, Profile guard, tests, and documentation. In the two existing Profile test files, it includes only the two authorization registrations; the attachment functionality and badge test's `partial` modifier belong to earlier commits.

### Execution decisions and limits

- Work stayed in the supplied directory, using file snapshots instead of git/worktree/commit operations because all git commands were prohibited. Cost: manual commit selection and no independent verification of repository history; separate file lists and before/after checks mitigate this.
- Browser/cookie/visual checks remain manual rather than risking the configured database's automatic migration/seeding. Cost: browser-only integration issues are not ruled out by the passing component tests.
- No production database or live VNPay/payment verification was attempted. Billing/entitlement code is unchanged and existing regression tests pass, but that is not a live payment certification.
- There are no deferred Minor review findings.

## Approved bounded design

The user approved this design before implementation:

- Fix A adds one small read method to the existing candidate Premium service for the active candidate `PREMIUM_MONTHLY` plan. Premium reads its name, price and duration rather than repeating the seed values. Read-only queries use `AsNoTracking`; page database operations use `CircuitDbGate`. Format amounts with `N0` and `VND`, following the payment-result page. A missing, inactive, wrong-audience or nonpositive-price plan is unavailable for purchase, consistent with checkout validation: display a friendly message and disable the purchase button. Do not substitute another plan or invent default prices/durations.
- Keep the existing checkout code, billing selection, VNPay and entitlement-grant logic unchanged. The read method selects by `Plan.PremiumMonthlyCode`, `PlanAudience.Candidate`, `IsActive` and positive price; billing remains authoritative when checkout starts.
- Fix B wraps Profile's existing loading/error/content markup in `AuthorizeView Roles="Candidate"` and uses the existing `RedirectToLogin` in `NotAuthorized`. Do not introduce authorization attributes or a new redirect mechanism. Do not change password components, Premium badge behavior, profile contents, CSS or sidebar anchors.
- Test the real page rendering with `HotJobAuthStateProvider`: anonymous users cannot see profile content and are redirected after the existing session check; signed-in candidates still see their profile; wrong-role users cannot see it; restored candidate sessions are accepted.
- Keep A and B independently verifiable with separate exact file lists. Run the full root-level `dotnet test`, record actual counts, and provide manual browser steps including logged-out `/profile` and Premium with/without an available plan. Do not run any git commands or print secrets.
- Write the final report only to `docs/admin/task-5-6-result.md`, not `task-3-result.md`. Tick checklist items only after completion. Record browser checks separately from automated component tests and disclose blockers.

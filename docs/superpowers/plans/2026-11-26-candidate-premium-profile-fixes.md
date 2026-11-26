# Candidate Premium Display and Profile Guard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make candidate Premium display the purchasable database plan and protect Profile with the existing candidate authorization pattern.

**Architecture:** Two independent bounded fixes, not a new subsystem. A read-only query in `CandidatePremiumService` supplies Premium's catalogue data; existing billing remains untouched. Profile uses the same role-based `AuthorizeView` and session-restoring `RedirectToLogin` as other candidate pages.

**Tech Stack:** .NET 10, Blazor InteractiveServer, EF Core, xUnit, SQLite in-memory, existing component-rendering test patterns.

**Spec:** `docs/admin/task-5-6-result.md`, section “Approved bounded design,” records the approved in-chat design; no separate architectural spec is needed.

## Global Constraints

- Do not run any git commands or print secrets.
- Keep A and B independently verifiable with separate exact file lists.
- Keep the existing checkout code, billing selection, VNPay and entitlement-grant logic unchanged.
- Do not introduce authorization attributes or a new redirect mechanism.
- Do not change password components, Premium badge behavior, profile contents, CSS or sidebar anchors.
- Write the final report only to `docs/admin/task-5-6-result.md`, not `task-3-result.md`.
- Tick checklist items only after completion.
- Run the full root-level `dotnet test`, record actual counts, and provide manual browser steps including logged-out `/profile` and Premium with/without an available plan.

## Review Focus

1. An inactive/wrong-audience/unrelated/nonpositive-price plan must never become a substitute purchasable candidate plan (Task A service and render tests).
2. Edited catalogue values must replace every repeated price, term and plan name; subscription history must not supply the offer (Task A render tests).
3. Loading, read failure and no-plan states must not advertise stale/default amounts or allow purchase; active subscribers also cannot renew unavailable plans (Task A render tests).
4. A fresh circuit with a valid browser session must restore candidate access rather than redirect prematurely; an actual guest must reach `/login` (Task B redirect-lifecycle tests and browser checks).
5. Wrong-role users and candidates who sign out must not retain profile markup; ordinary candidates must retain the password section and existing profile behavior (Task B render tests and existing regressions).

---

## File Structure and Independent Change Groups

### Fix A only

- Modify `hotjob/Services/Billing/CandidatePremiumService.cs`: interface and implementation of the catalogue read; keep subscription methods unchanged.
- Modify `hotjob/Components/Pages/Premium.razor`: load/render the plan and unavailable state; gate page database calls.
- Create `hotjob/Tests/hotjob.Tests/BillingTests/CandidatePremiumPlanTests.cs`: selection and read-only behavior.
- Create `hotjob/Tests/hotjob.Tests/BillingTests/PremiumPageTests.cs`: actual Premium rendering with test-local harness/stubs.

### Fix B only

- Modify `hotjob/Components/Pages/Profile.razor`: authorization wrapper only, without reformatting its existing contents.
- Create `hotjob/Tests/hotjob.Tests/ProfileTests/ProfileAuthorizationTests.cs`: real provider/page/redirect behavior with a test-local harness.
- Modify `hotjob/Tests/hotjob.Tests/ProfileTests/ProfilePremiumBadgeTests.cs`: only register authorization/cascading-auth services needed by the newly guarded page; retain all badge assertions.
- Modify `hotjob/Tests/hotjob.Tests/ProfileTests/ProfileResumeFileTests.cs`: the same two authorization registrations only; preserve this existing work and all resume-file assertions.

### Shared documentation only

- `docs/admin/task-5-6-result.md`: completion checklist, separate A/B file lists, evidence and browser instructions.
- This implementation plan: execution tracking, not a dependency of either product fix.

No application dependency, schema, seed, billing, auth-provider, redirect-component, password or stylesheet changes are planned. Keep test helpers local to their respective fix; A must not depend on B's tests or vice versa.

### Task A: Database-backed Premium offer and unavailable state

**Interfaces:**
- Consumes existing `Plan`, `Plan.PremiumMonthlyCode`, `PlanAudience.Candidate`, `CircuitDbGate.RunAsync`, `GetStatusAsync(string?, CancellationToken)` and the unchanged `IBillingService.CreateCandidatePremiumCheckoutAsync`.
- Produces `Task<Plan?> ICandidatePremiumService.GetPurchasablePlanAsync(CancellationToken cancellationToken = default)` implemented by `CandidatePremiumService`.
- Read predicate: matching Premium code, candidate audience, active flag and `Price > 0`. Return an `AsNoTracking` entity or null; preserve the stored `Name`, `Price` and `DurationDays` exactly.

- [x] **Step A1: Write failing service tests in `CandidatePremiumPlanTests.cs`.**

Use the existing `TestFixture` and `CandidatePremiumService`. Seed a plan named `Premium Flex`, price `149_000m`, duration `45`. Assert returned code/name/price/duration match that row and the returned entity is detached. Update the row and save; a fresh read must return the changed values. Test an empty catalogue, inactive Premium, Premium with employer audience, zero/negative price, and an unrelated candidate code: each must return null rather than choose a substitute.

Name the tests `GetPurchasablePlan_ReturnsCurrentDetachedCatalogueValues`, `GetPurchasablePlan_ReturnsNullForEmptyCatalogue` and parameterized `GetPurchasablePlan_RejectsUnavailableOrUnrelatedRows`. Core assertions:

```csharp
var actual = await service.GetPurchasablePlanAsync();
Assert.NotNull(actual);
Assert.Equal(Plan.PremiumMonthlyCode, actual.Code);
Assert.Equal("Premium Flex", actual.Name);
Assert.Equal(149_000m, actual.Price);
Assert.Equal(45, actual.DurationDays);
Assert.Equal(EntityState.Detached, fx.Db.Entry(actual).State);
// After changing the seeded row's price to 179000 and saving:
Assert.Equal(179_000m, (await service.GetPurchasablePlanAsync())!.Price);
// In each unavailable/empty-catalogue test:
Assert.Null(await service.GetPurchasablePlanAsync());
```

- [x] **Step A2: Verify those tests fail before implementing the read.**

Run from the repository root:
`dotnet test --filter "FullyQualifiedName~CandidatePremiumPlanTests"`

Expected: the new API is missing. Once the interface exists, behavioral failures must reflect selection/data mismatches, not unrelated fixture errors.

- [x] **Step A3: Implement the one read method in `CandidatePremiumService.cs`.**

Add the interface signature above and an EF Core `AsNoTracking().FirstOrDefaultAsync(...)` query with the exact predicate. Do not change the constructor or existing subscription-status methods.

- [x] **Step A4: Verify service tests pass.**

Run: `dotnet test --filter "FullyQualifiedName~CandidatePremiumPlanTests"`
Expected: all selected tests pass.

- [x] **Step A5: Write failing real-page tests in `PremiumPageTests.cs`.**

Follow `ProfilePremiumBadgeTests`' render-mode-aware `StaticHtmlRenderer` pattern. Register `AddAuthorizationCore`, `AddCascadingAuthenticationState`, the real custom auth provider with Candidate tokens, navigation, a circuit gate, and test-local premium/billing implementations. No new test packages.

Pin these assertions:

- For `Premium Flex`/149000/45, the authorized hero, term/price summary, plan heading/card and subscribe button show the database values; HTML contains `149,000 VND`, `45 days` and `Premium Flex`, but no `99.000`, `30 days` or `Premium Monthly`.
- Subscription status with a different historical plan name does not replace the current offer.
- Null offer shows `Premium is temporarily unavailable. Please try again later.`, no default price/term, and a disabled purchase button for both subscribed and unsubscribed candidates.
- Pending/failed plan reads do not render an enabled purchase button or fallback amounts. A failed read shows the page's error state.
- Hold the real circuit gate while initializing the page: service calls cannot begin until release. Release it in `finally` so failing tests cannot hang.

Use task-completion sources, not timing-based sleeps. If a test double implements the extended service interface, implement both existing methods as well.

Name the tests `Render_UsesCatalogueOfferInsteadOfHistoricalSubscription`, `Render_NoPlan_DisablesSubscribeAndRenew` (parameterized status), `Render_LoadingOrFailure_DoesNotOfferPurchase` and `Load_WaitsForCircuitGate`. Core HTML assertions for the successful render:

```csharp
Assert.Contains("Premium Flex", html);
Assert.Contains("149,000 VND", html);
Assert.Contains("45 days", html);
Assert.DoesNotContain("99.000", html);
Assert.DoesNotContain("30 days", html);
Assert.DoesNotContain("Premium Monthly", html);
```

For null-offer cases assert the exact unavailable copy above, locate the subscribe/renew button in rendered HTML and assert its `disabled` attribute, and assert no fallback amounts. For the gate test assert zero service calls while held, then one plan read after release.

- [x] **Step A6: Verify page tests fail on hardcoded/unavailable-state assertions.**

Run: `dotnet test --filter "FullyQualifiedName~PremiumPageTests"`
Expected: tests fail because the page still shows hardcoded values, lacks unavailable handling or does not wait for the gate.

- [x] **Step A7: Implement Premium-only display changes.**

Add nullable `Plan` state and inject `CircuitDbGate`; load status and plan sequentially through that gate. Clear stale offer state before a reload. Replace every authorized hardcoded name/price/term with the loaded plan, including accessible plan labels and purchase copy. Format money as `Price.ToString("N0", CultureInfo.InvariantCulture) + " VND"`, matching PaymentResult. Show no default offer while loading or on failure.

Use the exact unavailable message from A5. Disable subscribe/renew when the offer is null, loading, buying or in a load-error state, and return early from `BuyAsync` in those states. Keep the current checkout method, Premium code, client IP and gateway navigation unchanged; serialize the checkout call with the same circuit gate without holding it during navigation. Do not change guest sign-in behavior or entitlement/status logic.

- [x] **Step A8: Verify A and billing regressions.**

Run: `dotnet test --filter "FullyQualifiedName~BillingTests|FullyQualifiedName~CandidatePremiumGateTests"`
Expected: all selected tests pass, including unchanged billing/VNPay/entitlement tests. Record A's exact changed files; leave manual commits to the user.

### Task B: Existing candidate guard on Profile

**Interfaces:**
- Consumes existing `HotJobAuthStateProvider`, `AuthSession`, `AuthorizeView Roles="Candidate"` and `RedirectToLogin`.
- Produces guarded Profile rendering; no new public API and no dependency on Task A's read method.
- Existing `RedirectToLogin` calls `hotjobAuth.me`, restores successful sessions and redirects only unsuccessful anonymous sessions. Authenticated wrong-role users remain denied.

- [x] **Step B1: Write failing profile authorization tests.**

Create `ProfileAuthorizationTests.cs` using the real Profile and custom provider, in-memory fixture/services and a render-mode-aware static renderer modeled on the existing badge test. Register authorization and cascading auth services. Capture child components with an `IComponentActivator`; use test navigation and a JS stub that only accepts `hotjobAuth.me`.

Assert these cases:

- Anonymous render contains `Checking your sign-in...`, contains no `class="profile-summary"` or profile form, and instantiates `RedirectToLogin`.
- For an unsuccessful anonymous session check, invoking the captured redirect's `IHandleAfterRender.OnAfterRenderAsync()` on the renderer dispatcher records navigation exactly to `/login`.
- For a successful browser-session result for a real candidate, the same lifecycle restores `AuthSession`, the page shows `class="profile-summary"` and `Match Candidate`, and navigation remains unset.
- Already-authenticated Candidate render shows its profile and `id="change-password"`, with no redirect.
- An Employer-only identity cannot see profile markup; the existing redirect lifecycle shows access denied and does not navigate to login.
- After a rendered candidate signs out through the real provider, profile markup disappears.

Static HTML rendering does not automatically run `OnAfterRender`; explicitly exercising the captured existing component lifecycle is required for redirect assertions. Keep this test-only adaptation separate from real browser verification.

Name the tests `AnonymousRender_HidesProfileAndRedirectsToLogin`, `BrowserSession_RestoresCandidateWithoutRedirect`, `CandidateRender_PreservesProfileAndPasswordSection`, `EmployerRender_DeniesProfileWithoutLoginRedirect` and `SignOut_RemovesProfileMarkup`. Core anonymous assertions:

```csharp
Assert.Contains("Checking your sign-in...", html);
Assert.DoesNotContain("class=\"profile-summary\"", html);
Assert.DoesNotContain("<form", html);
await renderer.Dispatcher.InvokeAsync(
    () => ((IHandleAfterRender)redirect).OnAfterRenderAsync());
Assert.Equal("/login", navigation.Destination);
```

For candidate/restoration cases assert `class="profile-summary"`, `Match Candidate`, `id="change-password"` and `Assert.Null(navigation.Destination)`. After sign-out and for Employer-only tokens assert absence of profile markup; after the Employer redirect lifecycle assert access-denied copy and no navigation.

- [x] **Step B2: Verify the anonymous/role tests fail before editing Profile.**

Run: `dotnet test --filter "FullyQualifiedName~ProfileAuthorizationTests"`
Expected: anonymous render lacks the existing redirect component, and role/sign-out assertions expose the missing wrapper.

- [x] **Step B3: Add the existing guard without modifying Profile contents.**

Wrap existing loading/error/content markup (between PageTitle and `@code`) in `<AuthorizeView Roles="Candidate"><Authorized>...</Authorized><NotAuthorized><RedirectToLogin /></NotAuthorized></AuthorizeView>`.

Do not reindent the whole file, alter data-loading methods, move password markup, edit sidebar anchors or modify CSS. In `ProfilePremiumBadgeTests.cs` and `ProfileResumeFileTests.cs`, add only `AddAuthorizationCore()` and `AddCascadingAuthenticationState()` to the test service setup so existing assertions still exercise authorized content.

- [x] **Step B4: Verify B and profile/auth regressions.**

Run: `dotnet test --filter "FullyQualifiedName~Profile|FullyQualifiedName~ChangePassword|FullyQualifiedName~HotJobAuthStateProviderTests"`

Expected: all selected tests pass. Record B's exact files separately from A; no commits or other git commands.

## Final Verification and Report

- [x] Reinspect the changed files for accidental billing/password/sidebar edits, added defaults and shared dependencies between A and B. Do not inspect changes using git.
- [x] Run **`dotnet test`** without a filter from `C:\Users\phulo\Desktop\HotJob`; retain its actual summary. Record passed, failed, skipped and total counts. If anything fails, investigate without weakening checks and rerun the full command after fixes.
- [ ] Use a connected browser and safely available local app to verify logged-out `/profile` redirects to `/login`, then normal candidate access and wrong-role denial. Never print credentials, cookies or tokens.
- [ ] Verify candidate `/premium` with an available plan, then with no available plan in a disposable local/test database. Check displayed values against that database, the unavailable message and disabled subscribe/renew. Do not alter a potentially shared configured database to manufacture this condition; if a safe environment is unavailable, record that browser case as unexecuted and provide exact manual steps.
- [x] Confirm no local browser-test setup, catalogue or entitlement data was changed, and no payment was initiated; no restoration is needed.
- [x] Finalize `docs/admin/task-5-6-result.md`: short summary/root cause per fix; exact separate A/B file lists and shared documentation list; full-suite counts; automated-vs-browser evidence; manual steps for guests, candidates, plan edits and unavailable plans; related-page audit findings. Existing candidate-only pages inspected have guards; Premium's guest message is intentional, and public job/company/payment-result pages are not missing candidate guards.
- [x] Mark only completed checklist items. Explicitly report failed or blocked verification rather than claiming completion.

Browser checks above remain unexecuted: no app is listening locally, and startup unconditionally migrates/seeds the configured MySQL DB. No potentially shared database was touched. See the final report for manual steps. The final restored-source full suite passed: 611 passed, 0 failed, 0 skipped.

Independent final review: no Critical, Important or Minor findings. No final fix pass was needed. Execution artifacts are retained outside the repository under the approved OpenCode temporary directory; git operations and commits remain with the user.

## Execution Handoff

Plan self-review: approved requirements map to A, B and final verification; selection, stale display, session restoration, wrong roles and sign-out have named tests. The tasks share no new product interfaces. No git/commit step is authorized.

**Recommendation:** Native execution. These are two small changes with independent files and existing test patterns; per-task implementation/review agents would add disproportionate overhead. Wait for plan approval and the user's execution-method choice before implementation.

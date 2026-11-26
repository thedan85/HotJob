# Task 3 — Premium badge on the candidate profile

Date: 2026-11-26

## Scope

Only the Premium badge on `/profile`. Premium plan display (Task 5) and the
Profile authorization guard (Task 6) are deferred. No git commands or commits.

## Summary and root cause

The profile summary did not read Premium entitlement state or render a Premium
badge, even though `CandidatePremiumService` already provided the expiry-aware
check used elsewhere.

`Profile.razor` now calls the existing
`ICandidatePremiumService.IsPremiumAsync(userId)` through `CircuitDbGate` during
profile loading. A true result displays **Premium** beside the candidate's name,
using the existing `profile-badge profile-badge-green` classes. The service remains
the source of truth: `PremiumUntil` must be strictly later than the service's UTC
clock. Missing and expired subscriptions do not display the badge.

No new fields, migrations, dependencies, subscription rules, or styles were added.
Billing, VNPay, entitlement services, authentication, change-password functionality,
and sidebar navigation/scrolling were not modified.

## Exact files changed

- Modified: `hotjob/Components/Pages/Profile.razor` — service injection, entitlement
  read through the circuit gate, and conditional badge in the existing summary.
- Added: `hotjob/Tests/hotjob.Tests/ProfileTests/ProfilePremiumBadgeTests.cs` —
  real profile-render tests with SQLite, the real entitlement service, the custom
  auth-state provider, and a fixed clock.
- Added: `docs/admin/task-3-result.md` — this report.

## Automated coverage

The focused tests render the actual profile, not a separate badge implementation:

| Candidate state | Expected badge |
| --- | --- |
| Premium expires in 60 seconds | Visible |
| Premium expired 60 seconds ago, plan still assigned | Absent |
| Premium expires exactly at the current time | Absent |
| No Premium plan or expiry | Absent |

Each case also confirms the profile summary, candidate name, and change-password
section still render. The tests use a static renderer that accepts the page's
InteractiveServer render-mode declaration; they do not test a live Blazor circuit.

Before implementation, the active-Premium test failed because the badge was
missing (the three negative cases passed). After implementation, all four passed.
As a regression-strength check, temporarily inverting the badge condition made
all four cases fail. The correct condition was restored before final verification.

## Test results

Executed from the repository root:

```powershell
dotnet test --configuration Release -p:UseSharedCompilation=false
```

**Passed: 555 · Failed: 0 · Skipped: 0 · Total: 555.**
Reported test duration: 1 minute 11 seconds. Build and test command succeeded.
This includes all four new profile badge cases and the existing profile,
password, billing, and authentication tests.

Focused verification before the full run:

```powershell
dotnet test --configuration Release --filter FullyQualifiedName~ProfilePremiumBadgeTests --no-restore
```

**Passed: 4 · Failed: 0 · Skipped: 0.**

## Verification environment

The initial Debug test command was blocked before running tests because the
already-running app locked `hotjob.exe`. Tests use Release output instead;
the running app was not stopped or restarted.

Existing NuGet warnings report MySqlConnector 2.4.0 (NU1902) and
SQLitePCLRaw.lib.e_sqlite3 2.1.10 (NU1903). Existing compiler warnings in unrelated
files were left untouched. No dependencies were changed for this task.

## Manual browser checks

Not executed: the browser tool reported `paired-offline`/reconnecting with no
connected browser. Automated render coverage is not a substitute for visual or
interactive browser checks.

After restarting the app with the updated code, use disposable development test
accounts/data:

1. Sign in as a candidate with an active, future-dated Premium entitlement and
   open `/profile`. Confirm **Premium** appears beside the name, with the existing
   green pill styling.
2. Open `/profile` as a candidate whose Premium expiry is in the past (even with
   the old plan still assigned). Confirm no Premium badge appears.
3. Open `/profile` as a candidate who has never subscribed. Confirm no Premium
   badge appears.
4. Check the summary at desktop and narrow/mobile widths. Confirm the name and
   badge remain readable and the sidebar links and change-password section still
   work.
5. Sign out and open `/profile`. Confirm no Premium badge appears. The existing
   sign-in behavior remains unchanged; adding a login redirect is deferred to
   Task 6, not implemented here.

## Deferred tasks

- Task 5 / Fix A: database-backed Premium plan name, price, duration, and
  unavailable-plan handling on `/premium`.
- Task 6 / Fix B: Profile authorization guard using the existing candidate-page
  pattern.

No other candidate pages were modified.

## Checklist

- [x] Inspect the existing entitlement service and profile badge styles.
- [x] Obtain approval for the badge-only design.
- [x] Add focused render tests and confirm the active-Premium case fails.
- [x] Add the badge using the entitlement service and existing circuit gate.
- [x] Run focused tests and the full test suite.
- [x] Review the changes and record results and manual browser steps.

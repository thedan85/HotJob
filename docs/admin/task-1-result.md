# Task 1 — candidate change-password form

Date: 2026-11-25

## Summary

Added a **Change password** section and sidebar link to the candidate profile. The existing profile page is already large, so the form lives in a focused child component.

- Three masked fields: current password, new password, and confirmation, with appropriate password-manager autocomplete hints and accessible labels/errors.
- Blazor form validation requires all fields, matching confirmation, and a new password different from the current password. Password strength/length rules remain solely in the existing account service/Identity pipeline; the UI displays all returned service errors.
- Calls `IAccountService.ChangePasswordAsync` directly through `CircuitDbGate`, using the authenticated principal's user ID, never an editable user ID.
- Disables all fields and the submit button while processing, ignores duplicate submissions, and resets the form and its validation context after success.
- Signs out and redirects to `/login?passwordChanged=1`. Only that exact flag displays a hardcoded success notice; query-string text is never rendered as a message.

No changes to backend services, controllers, authentication mechanisms, CSS, dependencies, migrations, or unrelated features. No git commands were run.

## Files changed

| Status | File | Purpose |
|---|---|---|
| Added | `hotjob/Components/Account/ChangePasswordForm.cs` | UI-only required-field and cross-field validation; no duplicated password policy. |
| Added | `hotjob/Components/Account/ChangePasswordSection.razor` | Form, service call, loading/error states, and post-success sign-out. |
| Added | `hotjob/Components/Account/PasswordChangedNotice.razor` | Fixed-flag, hardcoded login notice. |
| Modified | `hotjob/Components/Pages/Profile.razor` | Sidebar anchor and child component. |
| Modified | `hotjob/Components/Pages/LoginPage.razor` | Query flag binding and notice component. |
| Added | `hotjob/Tests/hotjob.Tests/ProfileTests/ChangePasswordFormTests.cs` | Required fields, confirmation, different password, and leaving strength validation to the service. |
| Added | `hotjob/Tests/hotjob.Tests/ProfileTests/ChangePasswordSectionTests.cs` | Component rendering/callback tests using real Identity/SQLite and test doubles for navigation/JS. |
| Added | `hotjob/Tests/hotjob.Tests/AuthTests/PasswordChangedNoticeTests.cs` | Exact flag handling and hardcoded rendered notice. |
| Modified | `hotjob/Tests/hotjob.Tests/ProfileTests/ProfileServiceTests.cs` | Old refresh-token rejection and policy-rejection/session-preservation tests. |
| Modified | `hotjob/Tests/hotjob.Tests/AuthTests/HotJobAuthStateProviderTests.cs` | Sign-out clears tokens and notifies an anonymous authentication state. |
| Added | `docs/admin/task-1-result.md` | This report. |

## Session side effect

Verified the existing flow:

1. `AccountService.ChangePasswordAsync` validates the old password and new password, changes the password through Identity, and calls `AuthService.LogoutAsync(null, userId)` to revoke all of that user's refresh sessions.
2. Revocation alone does **not** clear the active circuit's in-memory `AuthSession`. The UI therefore resets the password form, then calls the existing browser helper `hotjobAuth.logout` to clear the HttpOnly refresh cookie using the existing logout endpoint.
3. Regardless of a non-success logout response, JavaScript failure, or timeout, the UI calls `HotJobAuthStateProvider.SignOut()` and force-loads `/login?passwordChanged=1`, ending the current page/circuit and displaying: **“Password changed successfully. Please sign in with your new password.”**
4. Cookie cleanup is best-effort: even if a revoked cookie remains in the browser, it cannot restore or refresh the session. A subsequent successful login replaces it.

Tests explicitly verify that **two refresh tokens issued before the password change both fail `GetSessionAsync` and `RefreshAsync` afterward**, with unauthorized results. They also verify that the old password no longer logs in and the new password does.

## Verification

Command executed from repository root, `C:\Users\phulo\Desktop\HotJob`:

```text
dotnet test

Passed! - Failed: 0, Passed: 474, Skipped: 0, Total: 474
Duration: 52 s
Exit code: 0
```

The command restored/built both .NET 10 projects and ran the full xUnit suite. Added 23 test cases without new test dependencies.

Focused checks additionally covered:

- Loading/disabled rendering while waiting for `CircuitDbGate`, and duplicate submission protection.
- Service errors leaving the current session intact.
- Submitting after the session has ended not changing the password.
- Password fields and the replacement form model being empty before cookie cleanup.
- Successful sign-out for normal, unauthorized, failed, thrown, and timed-out cookie cleanup.
- The login notice rejecting arbitrary flag values. Temporarily inverting its flag condition caused all four notice tests to fail; the correct condition was restored before the full passing run.

**Live browser verification was not performed:** the browser tool reported `paired-offline` and that the paired browser was reconnecting. Static component rendering/callback tests are not a substitute for visual, keyboard, or screen-reader checks.

## Manual browser test steps

Use a disposable local candidate account with a known password. These tests change that account's real password and revoke its refresh sessions.

1. Start the app with your normal local configuration, for example:
   ```powershell
   dotnet run --project hotjob/hotjob.csproj --launch-profile http
   ```
   Sign in at `http://localhost:5168/login`, open `/profile`, and select **Change password** in the sidebar. Confirm the section follows the existing profile styling.
2. Submit all fields empty. Confirm required-field errors. Fill mismatched new/confirmation values and submit; confirm a matching error. Use the current password in both new fields; confirm the different-password error.
3. Enter an incorrect current password and a matching, valid new password. Confirm **“Current password is incorrect.”**, no redirect, and the account remains signed in.
4. Enter the correct current password and a matching weak new password such as `weak`. Confirm the existing service's validation errors are displayed and the old password/session still works.
5. Enter the correct current password and a different valid new password, with matching confirmation. Submit. On a sufficiently slow operation, confirm **“Changing password...”** and disabled inputs/button; repeated clicks must not cause multiple changes.
6. Confirm navigation to `/login?passwordChanged=1`, the hardcoded success notice, and a signed-out navigation menu. Go back to `/profile` or reload it: the old session must not be restored, and password fields must not retain the previous values.
7. Confirm the old password fails login and the new password succeeds.
8. For cross-session behavior, before changing the password sign into the same test account in a second browser/profile. After the change, reload that second session; it must no longer restore authentication from its old refresh cookie. Already-open circuits are subject to the limitation below.
9. Check `/login?passwordChanged=0` and `/login?passwordChanged=arbitrary-text`: neither should display the password-change success notice or the supplied text. `/login?passwordChanged=1` should display only the fixed notice.
10. Optional cleanup-failure check: use DevTools request blocking for `/api/auth/logout` only, leaving the Blazor connection working, then change the password again. Confirm the UI still signs out and redirects successfully.
11. Tab through the three fields and submit button; check visible focus and error announcements. Check the profile layout at narrow/mobile and desktop widths. Check the browser console for new errors.

## Open issues and assumptions

- Requires an existing candidate profile and a current local password. This is not a password-reset or first-password setup flow for Google-only accounts.
- The login flag is a cosmetic notice, not proof that a password was changed; anyone can visit that URL. It cannot supply arbitrary message content.
- Existing auth behavior revokes refresh sessions, not already-issued access JWTs or other currently open circuits' in-memory identities immediately. This change explicitly signs out the current circuit; it does not introduce cross-circuit logout broadcasting or JWT revocation.
- Password update/session-revocation atomicity remains the existing service's responsibility; that backend implementation was not changed.
- Live visual, keyboard, responsive, and assistive-technology verification remains manual, as noted above.
- The full test run reported existing NuGet advisories: **NU1902**, `MySqlConnector` 2.4.0 ([GHSA-473q-m89c-ghf8](https://github.com/advisories/GHSA-473q-m89c-ghf8)), and **NU1903**, test dependency `SQLitePCLRaw.lib.e_sqlite3` 2.1.10 ([GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q)). Existing compiler warnings also remain in unrelated files. No packages were added/upgraded or warnings suppressed; dependency remediation is outside this task.

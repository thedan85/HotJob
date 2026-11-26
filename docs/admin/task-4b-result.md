# Task 4b: Exclude runtime uploads from dotnet watch

Date: 2026-11-26

## Summary and confirmed cause

The project's default CV upload directory is inside the web project's `wwwroot`. Before this fix, an existing uploaded PDF was included in both MSBuild's default `Content` items and `dotnet watch --list`. Runtime uploads were therefore being treated as watched web assets rather than being excluded from development build/Hot Reload inputs.

The reported `Unexpected true` assertion in `HotReloadMSBuildWorkspace.cs` / `Microsoft.DotNet.Watch.ManagedCodeWorkspace.UpdateFileContentAsync` is in the SDK watcher, not `CvFileStorage`. The configuration defect above is confirmed by before/after item and watch-list evaluations. The exact SDK assertion during a live upload was **not reproduced here**: the app was not started, as requested. The manual check below must confirm that excluding this trigger prevents the reported crash.

Installed SDK used for verification: **10.0.401**.

## Physical storage and configuration findings

`hotjob/Program.cs` registers `ICvFileStorage` with:

```csharp
Path.Combine(
    builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot"),
    "uploads",
    "cv")
```

With normal project hosting, this resolves to:

```text
C:\Users\phulo\Desktop\HotJob\hotjob\wwwroot\uploads\cv
```

`CvFileStorage` resolves that root with `Path.GetFullPath`, creates a candidate-ID subdirectory, and stores a generated GUID filename with the validated extension:

```text
{root}\{candidateId}\{guid}.pdf
{root}\{candidateId}\{guid}.doc
{root}\{candidateId}\{guid}.docx
```

Public URLs remain `/uploads/cv/{candidateId}/{guid}.{extension}`.

There is **no CV-specific storage-root configuration key** read by this registration, and no matching CV/upload/storage-root setting in the two checked-in `appsettings` files. The constructor accepts a root path, but the app supplies the web-root-derived value directly; setting an invented `CvStorage:RootPath` or similar key would do nothing.

The web root itself is a hosting setting, not an independent CV setting. Relocating the whole web root would affect other static assets; it is not needed for this fix. No hosting configuration or storage root was changed.

`Program.cs` already calls `app.UseStaticFiles()` to serve runtime uploads. Keeping existing files in place preserves that serving behavior and their database URLs.

## Exact files changed by Task 4b

1. `hotjob/hotjob.csproj` — exclude runtime upload folders from default items and the watch list.
2. `docs/admin/task-4b-result.md` — this report.

These are this task's edits, not a git-derived inventory of earlier/user changes.

## Minimal fix

Added to the existing property group:

```xml
<!-- Runtime uploads are not build inputs or Hot Reload assets. -->
<DefaultItemExcludes>$(DefaultItemExcludes);wwwroot/uploads/**;uploads/**</DefaultItemExcludes>
```

Added a focused item group:

```xml
<ItemGroup>
  <Watch Remove="wwwroot/uploads/**;uploads/**" />
</ItemGroup>
```

`DefaultItemExcludes` appends to the SDK exclusions rather than replacing them, and excludes these folders from default compile/content/resource/None globs. In .NET 10 it also excludes matching paths from watch discovery. `Watch Remove` explicitly removes matching Watch items. Both the actual `wwwroot/uploads` tree and a project-root `uploads` tree are covered; other source and web assets remain included.

No upload/storage implementation, validation, naming, cleanup behavior, public URL, database data, or app startup logic was changed. No files were moved or deleted. Runtime uploads are no longer packaged as default build/publish content; existing runtime files remain on disk and are still served by the unchanged static-file middleware.

Official reference: [dotnet watch — files watched by default and folder exclusions](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-watch#ignore-specified-files-and-folders).

## Verification

### Before the fix: reproduce the inclusion without starting the app

Ran from the repository root:

```powershell
dotnet watch --project hotjob/hotjob.csproj --list --no-restore
dotnet msbuild hotjob/hotjob.csproj -nologo -getItem:Compile,Content,None,EmbeddedResource -getProperty:DefaultItemExcludes
```

The watch list contained an existing PDF under `hotjob\wwwroot\uploads\cv\2\`. Evaluated item counts under upload paths:

| Item type | Before | After |
| --- | ---: | ---: |
| Compile | 0 | 0 |
| Content | 1 | 0 |
| None | 0 | 0 |
| EmbeddedResource | 0 | 0 |

Before the fix, `DefaultItemExcludes` contained no upload exclusion.

### After the fix: watch and item checks

Repeated those evaluations with PowerShell assertions:

- No evaluated Compile, Content, None, or EmbeddedResource item has an uploads path.
- The evaluated default exclusions contain the added patterns.
- No uploads path appears in the watch list.
- `Program.cs`, `Components\Pages\Profile.razor`, and `wwwroot\css\Profile.css` are **still watched**.

Both assertion commands exited **0**. `dotnet watch --list` lists files and exits; it does not start the watcher or app. No new xUnit test/package was needed for this project-file-only change; the evaluated MSBuild and watcher checks directly exercise the configuration.

### Build

```powershell
dotnet build hotjob/hotjob.csproj --configuration Release --verbosity minimal
```

Result: **Build succeeded; 0 errors, 7 warnings**, exit code **0**.

### Full test suite

Ran from `C:\Users\phulo\Desktop\HotJob`, using the root `hotjob.slnx`, without a filter:

```powershell
dotnet test --configuration Release --verbosity minimal --logger "trx;LogFileName=task-4b-full.trx" --results-directory "C:\Users\phulo\AppData\Local\Temp\opencode\hotjob-task4b-results"
```

Final result:

```text
Passed!  - Failed:     0, Passed:   611, Skipped:     0, Total:   611, Duration: 56 s - hotjob.Tests.dll (net10.0)
```

Exit code: **0**.

TRX: `C:\Users\phulo\AppData\Local\Temp\opencode\hotjob-task4b-results\task-4b-full.trx`.

Existing warnings remain: `MySqlConnector` 2.4.0 moderate advisory (NU1902), test dependency `SQLitePCLRaw.lib.e_sqlite3` 2.1.10 high advisory (NU1903), and duplicate-using/unread-parameter/nullability compiler warnings in unrelated files. No packages or unrelated source were changed to silence them.

## Manual verification steps for the user

1. Stop any previous `dotnet watch` process so the changed project settings are picked up.
2. Optionally verify existing uploads are absent from the watch list, from the repository root:

   ```powershell
   dotnet watch --project hotjob/hotjob.csproj --list --no-restore |
       Select-String -Pattern '[\\/]uploads[\\/]'
   ```

   Expected: no upload paths. Normal source and CSS should still appear in the unfiltered list.
3. Start the app using your normal database/development configuration:

   ```powershell
   dotnet watch --project hotjob/hotjob.csproj
   ```

4. Sign in as a candidate and open `/profile#cv-management`. Create a CV or choose an existing resume and upload a non-sensitive, valid PDF of at most 5 MB. If creating a resume, add at least one skill as usual.
5. Expected: upload succeeds; the watcher/app remain running, with no `Unexpected true`, `HotReloadMSBuildWorkspace`, or `UpdateFileContentAsync` crash and no upload-induced Hot Reload/restart.
6. Open **View CV** and confirm the uploaded PDF still opens at `/uploads/cv/...`. Refresh the profile and confirm the attachment is still present.
7. Upload a second PDF to replace the attachment; verify the watcher still stays alive and the replacement opens. Optionally remove an unshared attachment and verify disk cleanup also does not trigger a watcher restart.
8. Run the watch-list check in another terminal after the upload. The newly uploaded file should also be absent.
9. Optionally make a harmless change to ordinary CSS/source to confirm normal watching still works, then undo that manual test edit yourself.

These live upload steps remain **unverified here**, because no app process was launched against your database. If the same assertion persists, capture the SDK version, the full watcher stack trace, and the specific changed path reported by `dotnet watch --verbose`; that would need further SDK-level investigation rather than an upload/storage rewrite.

## Completion checklist

- [x] Trace storage registration, default physical path and configuration.
- [x] Confirm an uploaded PDF is included in default Content and the watch list before the fix.
- [x] Add focused project-file exclusions without changing runtime storage.
- [x] Verify uploads are absent from evaluated items and the watch list, with ordinary source/static files retained.
- [x] Build successfully and run the full suite: 611 passed, 0 failed, 0 skipped.
- [x] Complete the report and manual verification instructions.
- [x] Do not start the app/access the application database; run no git commands.
- [ ] Manually upload under a running `dotnet watch` session (user verification).

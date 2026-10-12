# Job Detail Page (`/jobs/{id}`) — Design Spec

Date: 2026-10-09
Status: Draft, pending user review
Scope: Read-only public job detail page (Approach A). No Apply/Save writes, no MatchScore.

## Goal

Fix the dead link from `/it-jobs`: `hotjob/Components/Pages/Jobs.razor:79` links each card to `/jobs/{id}`, which 404s today. Add a public read-only detail page showing the published posting, reusing the existing `JobDetailResponse` DTO and list-page styles.

## Context

- Stack: Blazor Server (.NET 10), EF Core + MySQL. `hotjob/Program.cs` registers `IJobPostingRepository`, `IJobService`.
- List path (working): `Jobs.razor` → `IJobService.BrowsePublishedAsync` → `JobPostingRepository.BrowsePublishedAsync` (filters `Status == Published && (ExpiryDate == null || > UtcNow)` via `DetailsQuery()` with `AsNoTracking()` + Company + JobSkills/Skill + CategoryMappings/Category).
- Detail gap: `IJobService.GetAsync` (`hotjob/Services/Job/IJobService.cs:45`) and `JobPostingRepository.GetByIdOwnedAsync` (`hotjob/Repositories/Job/IJobPostingRepository.cs:7`) are employer-owned only (require `companyId` + active employer of approved company). No public by-id method exists.
- DTO ready: `JobDetailResponse` (`hotjob/DTOs/Job/JobDtos.cs:65`) already carries Id, CompanyProfileId, Title, Description, SalaryMin/Max, ExperienceRequired, JobType, ExpiryDate, Status, CompanyName, Skills (`JobSkillDto` with ImportanceWeight), Categories, ApplicationsCount, CreatedAt. No new DTO needed.
- `JobService.ToDetail` (`hotjob/Services/Job/JobService.cs:473`) maps a `JobPosting` to this DTO; reuse it.
- Styles: `hotjob/wwwroot/css/Jobs.css` (`.job-card`, `.job-tag`, `.job-salary`); CSS files are linked per-page in `hotjob/Components/App.razor:10-19`.
- Not-found page exists: `hotjob/Components/Pages/NotFound.razor` route `/not-found`.
- Tests live in `hotjob/Tests/hotjob.Tests/` but there are currently no JobService tests (only Auth/EmployerGuard/Profile/Register/Seed suites).

## Non-goals

- No working Apply / Save buttons (render as "Sign in to apply/save" links to `/login`, `/register`).
- No MatchScore / Match% (model `hotjob/Models/ML/MatchScore.cs` exists but no computing service; explicitly out of v1 per user decision).
- No employer/admin views, no draft/closed/expired visibility.
- No new API controller (Blazor calls `IJobService` directly, same as `Jobs.razor`).
- No company-reviews section, no related-jobs section.

## Decisions (confirmed)

- Approach A (read-only detail). Chosen over full-functional because Apply/Save/Match backends do not exist yet.
- Public visibility rule = same as list: `Status == Published && (ExpiryDate == null || ExpiryDate > UtcNow)`. Draft/Closed/Expired/missing → not-found state.
- Reuse `JobDetailResponse`; `ApplicationsCount` included but not displayed in v1 (could confuse; count is employer-facing).
- Blazor `[StreamRendering]`, same as `Jobs.razor`.
- New CSS file `hotjob/wwwroot/css/JobDetail.css` + one `<link>` line in `App.razor` (follows existing per-page pattern).

## Backend changes

### 1. `IJobPostingRepository` + `JobPostingRepository`

```csharp
Task<JobPosting?> GetPublishedByIdAsync(int id, CancellationToken cancellationToken = default);
```

Implementation: reuse private `DetailsQuery()` (AsNoTracking + includes), add:

```csharp
.Where(job => job.Id == id
    && job.Status == JobStatus.Published
    && (job.ExpiryDate == null || job.ExpiryDate > DateTime.UtcNow))
.FirstOrDefaultAsync(cancellationToken)
```

### 2. `IJobService` + `JobService`

```csharp
Task<ServiceResult<JobDetailResponse>> GetPublishedAsync(int id, CancellationToken cancellationToken = default);
```

Implementation: call `jobRepository.GetPublishedByIdAsync`; null → `Fail(NotFound, "Job not found.")`; else count applications (reuse line 312 pattern) and return `ToDetail(job, count)`. No auth check (public). Note: existing private `GetDetailAsync(companyId, jobId)` stays untouched (employer path).

## Frontend: `hotjob/Components/Pages/JobDetail.razor`

Route: `@page "/jobs/{id:int}"`, `@attribute [StreamRendering]`, `@inject IJobService JobService`.

Parameters/state:

```csharp
[Parameter] public int Id { get; set; }
private JobDetailResponse? job;
private bool isLoading = true;
private string? loadError; // null = ok; "not-found" or message
protected override async Task OnParametersSetAsync() {
  // guard Id <= 0 → not-found; call JobService.GetPublishedAsync(Id)
  // Success → job = Data; NotFound → loadError = "not-found"; else loadError = message
}
```

Render states:
1. Loading: `<p role="status">Loading job…</p>`.
2. Not found: title "Job not found", copy "This role is no longer available or never existed.", back link to `/it-jobs`.
3. Error: `role="alert"` message + retry link.
4. Detail:
   - Breadcrumb: `/it-jobs` back link.
   - Header: logo initial (same `CompanyInitial` helper as list), `h1` Title, company name + `·` location (location not in `JobDetailResponse` — see open point below), meta row: JobType, Posted date, Expires date or "No deadline", ExperienceRequired (`"X years"` or `"No experience required"` when 0).
   - Salary block: same `FormatSalary` helper as `Jobs.razor:233`.
   - Skills: chips from `job.Skills` showing `SkillName`; weight shown as muted suffix only when `ImportanceWeight > 0` (e.g. `C# · 8/10`) — satisfies DESIGN.md skill+weight requirement without clutter.
   - Description: `<div class="job-detail-desc">` with `white-space: pre-wrap` to preserve line breaks (Blazor auto-encodes; no raw HTML).
   - Categories: chips or "Uncategorized" fallback.
   - Sidebar/aside: company card (name + location), plus auth prompts: `<a href="/login">Sign in to apply</a>`, `<a href="/register">Create account to save</a>`. No company-detail link — `/companies/{id}` route does not exist yet (only `/companies` link target on Home, no page).

Helpers copied from `Jobs.razor`: `CompanyInitial`, `FormatSalary` (duplicate 10-line private statics; no shared component in v1 to keep diff small).

### Location gap (resolved in-spec)

`JobDetailResponse` has `CompanyName` but no `CompanyLocation`; list DTO has it. Options: (a) extend DTO with `CompanyLocation` — chosen. Add `string? CompanyLocation` field (populated from `job.Company?.Location` in `ToDetail`) and update employer `ToDetail` call sites (single method, so all employer detail responses gain the field too — additive, non-breaking for positional records only if appended last; append as last positional arg or convert to optional). This avoids a second query from the page.

## CSS: `hotjob/wwwroot/css/JobDetail.css` + `App.razor` link

New file, ~120 lines, reusing `Jobs.css` tokens (`--hj-*`): `.job-detail-layout` (2-col grid, 1fr 320px, collapse <900px), `.job-detail-header`, `.job-detail-tags` (reuse `.job-tag` class names so chips match list), `.job-detail-desc`, `.job-detail-side`, `.job-detail-auth` prompt box, not-found/empty states mirroring `.jobs-empty`. One line added to `App.razor` head: `<link rel="stylesheet" href="css/JobDetail.css" />`.

## Testing

- New `hotjob/Tests/hotjob.Tests/JobTests/PublicJobDetailTests.cs` using existing `Support/TestFixture.cs` pattern: (1) published job returns detail with skills/categories; (2) draft id → NotFound; (3) expired published → NotFound; (4) missing id → NotFound.
- `dotnet test` must pass; manual: `/it-jobs` → click card → `/jobs/{id}` renders; direct nav to draft id shows not-found; back link works.

## Risks

- Positional record change to `JobDetailResponse` (new trailing `CompanyLocation`): compiler catches all constructions (only `ToDetail` constructs it). Low risk.
- `ExpiryDate` uses `DateTime.UtcNow` comparison, consistent with `BrowsePublishedAsync`.

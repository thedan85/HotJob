# Job Detail Page (`/jobs/{id}`) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a read-only public `/jobs/{id:int}` page that fixes the dead link from `/it-jobs` job cards.

**Architecture:** Add one public read method down the existing repository → service chain (`GetPublishedByIdAsync` → `GetPublishedAsync`), reusing `DetailsQuery()` and `ToDetail()`, then a new `JobDetail.razor` page plus `JobDetail.css`. No auth, no new DTO, no controller.

**Tech Stack:** Blazor Server (.NET 10), EF Core, xUnit + SQLite in-memory tests.

**Spec:** `docs/superpowers/specs/2026-10-09-job-detail-design.md`

## Global Constraints

- Public visibility rule is exactly `Status == Published && (ExpiryDate == null || ExpiryDate > DateTime.UtcNow)` — same as `BrowsePublishedAsync`.
- Reuse `JobDetailResponse` DTO; additive change only (append trailing `CompanyLocation`).
- Follow `Jobs.razor` patterns: `[StreamRendering]`, `@inject IJobService`, `ServiceResult` success/error handling.
- CSS tokens `--hj-*`; new styles in `hotjob/wwwroot/css/JobDetail.css`, linked from `hotjob/Components/App.razor`.
- TDD: failing test first for backend tasks; `dotnet test` green before commit.

## Review Focus

- Direct nav to a Draft/Closed/Expired/missing id must show not-found, not an error or empty shell.
- `/jobs/0` and `/jobs/-3` must show not-found without hitting the database.
- Description text must render as plain text with line breaks (no raw HTML injection).
- Skill weight `0` must not show a `· 0/10` suffix; weights 0–10 only.
- List page `/it-jobs` links must keep working unchanged after the DTO gains a field.

---

### Task 1: Public published-by-id read (repository + service + DTO location)

**Files:**
- Modify: `hotjob/DTOs/Job/JobDtos.cs` (append `string? CompanyLocation` to `JobDetailResponse`)
- Modify: `hotjob/Repositories/Job/IJobPostingRepository.cs` (add signature)
- Modify: `hotjob/Repositories/Job/JobPostingRepository.cs` (add method)
- Modify: `hotjob/Services/Job/IJobService.cs` (add signature)
- Modify: `hotjob/Services/Job/JobService.cs` (add method + `ToDetail` location mapping)
- Test: `hotjob/Tests/hotjob.Tests/JobTests/PublicJobDetailTests.cs` (create dir `JobTests/`)

**Interfaces:**
- Consumes: `DetailsQuery()` (private, existing), `ToDetail(JobPosting, int)` (private, existing), `ServiceResult<T>.Ok/Fail`, `ServiceErrorType.NotFound`.
- Produces: `Task<JobPosting?> IJobPostingRepository.GetPublishedByIdAsync(int id, CancellationToken cancellationToken = default)`; `Task<ServiceResult<JobDetailResponse>> IJobService.GetPublishedAsync(int id, CancellationToken cancellationToken = default)`; `JobDetailResponse` gains trailing `string? CompanyLocation`.

- [ ] **Step 1: Write the failing tests** in `hotjob/Tests/hotjob.Tests/JobTests/PublicJobDetailTests.cs`:

```csharp
[Fact] public async Task GetPublishedAsync_ReturnsDetail_ForPublishedJob();
[Fact] public async Task GetPublishedAsync_ReturnsNotFound_ForDraftJob();
[Fact] public async Task GetPublishedAsync_ReturnsNotFound_ForExpiredJob();
[Fact] public async Task GetPublishedAsync_ReturnsNotFound_ForMissingId();
```

Seeding per test (SQLite via `TestFixture.CreateAsync()`, dispose with `await using`): company `new CompanyProfile { CompanyName = "Co", Location = "Ho Chi Minh", Status = CompanyStatus.Approved }`; skill `new Skill { Name = "C#" }`; category `new JobCategory { Name = "Backend" }`; job `new JobPosting { CompanyProfileId, Title = "Dev", Description = "Line1\nLine2", Status = JobStatus.Published, JobSkills = new List<JobSkill> { new() { SkillId, ImportanceWeight = 8 } }, CategoryMappings = new List<JobCategoryMapping> { new() { JobCategoryId } } }`. Draft test uses `Status = JobStatus.Draft`; expired test uses `Status = Published, ExpiryDate = DateTime.UtcNow.AddDays(-1)`; missing test uses id ` int.MaxValue`. Service under test: `new JobService(fx.Db, new JobPostingRepository(fx.Db), NullLogger<JobService>.Instance)`. Assertions: success case `Assert.True(r.Success)`, `Assert.Equal("Dev", r.Data!.Title)`, `Assert.Equal("Ho Chi Minh", r.Data.CompanyLocation)`, single skill `Assert.Equal("C#", r.Data.Skills[0].SkillName)`, `Assert.Equal(8, r.Data.Skills[0].ImportanceWeight)`; not-found cases `Assert.False(r.Success)`, `Assert.Equal(ServiceErrorType.NotFound, r.ErrorType)`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test hotjob/Tests/hotjob.Tests --filter PublicJobDetailTests`
Expected: FAIL — build error, `GetPublishedAsync` does not exist.

- [ ] **Step 3: Append `CompanyLocation` to `JobDetailResponse`** in `hotjob/DTOs/Job/JobDtos.cs` as the last positional parameter (`string? CompanyLocation`), and map it in `JobService.ToDetail` from `job.Company?.Location`.

- [ ] **Step 4: Add `GetPublishedByIdAsync`** to `IJobPostingRepository` and implement in `JobPostingRepository` using `DetailsQuery().Where(job => job.Id == id && job.Status == JobStatus.Published && (job.ExpiryDate == null || job.ExpiryDate > DateTime.UtcNow)).FirstOrDefaultAsync(cancellationToken)`.

- [ ] **Step 5: Add `GetPublishedAsync`** to `IJobService` and implement in `JobService` (no auth check): fetch via repository, null → `Fail(NotFound, "Job not found.")`, else count `dbContext.Applications` for the id and return `ToDetail(job, count)`.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test hotjob/Tests/hotjob.Tests --filter PublicJobDetailTests`
Expected: PASS (4/4).

- [ ] **Step 7: Run the full suite** (`dotnet test hotjob/Tests/hotjob.Tests`) — employer `ToDetail` responses gain the new field too; PASS means no regression.

- [ ] **Step 8: Commit**

```bash
git add hotjob/DTOs/Job/JobDtos.cs hotjob/Repositories/Job hotjob/Services/Job hotjob/Tests/hotjob.Tests/JobTests
git commit -m "feat: add public published job detail read"
```

### Task 2: `JobDetail.razor` page (read-only UI)

**Files:**
- Create: `hotjob/Components/Pages/JobDetail.razor`
- Test: manual render check (no bUnit in repo; verify via `dotnet run`).

**Interfaces:**
- Consumes: `IJobService.GetPublishedAsync(int, CancellationToken)` (from Task 1), `JobDetailResponse` fields.
- Produces: route `/jobs/{id:int}` rendering loading / not-found / error / detail states.

- [ ] **Step 1: Create `JobDetail.razor`** with `@page "/jobs/{id:int}"`, `@attribute [StreamRendering]`, `@inject IJobService JobService`, `@using hotjob.DTOs.Job`. Code block: `[Parameter] public int Id { get; set; }`, `JobDetailResponse? job`, `bool isLoading = true`, `string? loadError`. `OnParametersSetAsync`: `Id <= 0` → `loadError = "not-found"` without service call; else call `GetPublishedAsync(Id)`, success → `job`, `ErrorType == NotFound` → `"not-found"`, else first error message. `finally isLoading = false`.

- [ ] **Step 2: Render the four states**: loading `<p class="job-detail-status" role="status">Loading job…</p>`; not-found block (h1 "Job not found", copy "This role is no longer available or never existed.", `<a href="/it-jobs">← Back to jobs</a>`); error block `role="alert"` + retry link to same `/jobs/{Id}`; detail block per spec (breadcrumb, header with `CompanyInitial` helper, title, company + location, salary via `FormatSalary` copied from `Jobs.razor:233-251`, meta JobType/posted/expiry/experience, skill chips reusing `job-tag` class with weight suffix only when `> 0`, `white-space: pre-wrap` description div, category chips, aside company card + `/login` + `/register` prompt links — no company-detail link).

- [ ] **Step 3: Verify** — `dotnet build`, then `dotnet run` and open `/it-jobs`, click a card, confirm `/jobs/{id}` renders; direct-nav a draft id and `/jobs/0` for not-found.

- [ ] **Step 4: Commit**

```bash
git add hotjob/Components/Pages/JobDetail.razor
git commit -m "feat: add read-only job detail page"
```

### Task 3: `JobDetail.css` styling + `App.razor` link

**Files:**
- Create: `hotjob/wwwroot/css/JobDetail.css`
- Modify: `hotjob/Components/App.razor` (one `<link>` line after the `Jobs.css` line)

**Interfaces:**
- Consumes: `Jobs.css` class names/tokens (`.job-tag`, `--hj-*`).
- Produces: `.job-detail-*` styles used by Task 2's markup.

- [ ] **Step 1: Create `JobDetail.css`** (~120 lines): `.job-detail-layout` 2-col grid (`1fr 320px`, collapse to 1 col under 900px), header/logo/title/meta, tags row, `white-space: pre-wrap` description, aside card + auth prompt box, status/empty states mirroring `.jobs-empty`. Reuse `.job-tag` class name for chips so they match the list.

- [ ] **Step 2: Add the `<link>`** in `App.razor` head: `<link rel="stylesheet" href="css/JobDetail.css" />` directly after the `Jobs.css` line.

- [ ] **Step 3: Verify** — rebuild, reload `/jobs/{id}` at desktop + mobile widths; confirm no unstyled flash and grid collapses.

- [ ] **Step 4: Commit**

```bash
git add hotjob/wwwroot/css/JobDetail.css hotjob/Components/App.razor
git commit -m "style: add job detail page styles"
```

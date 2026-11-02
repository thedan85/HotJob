# Skill Management API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add API-only admin skill CRUD and candidate skill-based job search.

**Architecture:** Extend the existing service layer and add two controllers that use `ApiControllerBase`. Admin writes validate and persist `Skill` rows. Candidate search projects published jobs, matched skill counts, and stored match scores in SQL, then loads matched skill names for the current page only.

**Tech Stack:** .NET 10, ASP.NET Core controllers, EF Core 9 with MySQL, xUnit, SQLite in-memory tests.

**Spec:** `docs/superpowers/specs/2026-11-02-skill-management-design.md`

## Execution Record

All seven tasks are complete. Initial task test counts below record the original RED/GREEN gates, not the larger final suite.

- [x] Task 1: Admin skill read path (`66ddbcb`, 4 tests at the task gate).
- [x] Task 2: Admin skill create and update (`ec69ef0`, 12 cumulative service tests).
- [x] Task 3: Restricted deletion and migration (`e837482`, 14 cumulative service tests).
- [x] Task 4: Admin controller (`406c3a0`, 6 controller tests).
- [x] Task 5: Candidate matching query (`cd06b19`, 11 query tests).
- [x] Task 6: Candidate search controller (`2a49366`, 3 controller tests).
- [x] Task 7: Full tests, build, and offline migration checks.

One fresh-context review found three Important bugs: raw MVC validation errors, overflowing page offsets, and overbroad database-error catches. A fourth finding, raw DTO length validation rejecting valid padded input, was promoted from Minor to Important. Each fix passed a reproducing RED/GREEN test and the full suite. Examples below include these corrections.

Final tests: 417 passed, 0 failed, 0 skipped, including 60 feature test cases. Real loopback MVC tests cover malformed input, normalized boundary lengths, and unchanged legacy validation responses. SQLite interceptor tests cover duplicate-write races, both post-count FK races, and unrelated database/concurrency errors.

Final fix commit: `291d3da`. `dotnet build hotjob.slnx` succeeded with 0 errors. Offline migration discovery lists `20261102085548_RestrictSkillDeletes`, whose `Up` method recreates exactly two FKs with `ReferentialAction.Restrict`. A deliberate inversion of the validation filter guard failed 11 HTTP tests; restoring it returned the full suite to 417/417.

Decisions: work stayed on existing `dev` rather than a separate worktree; existing empty-catalog startup reseeding remains unchanged. Deleting every skill can therefore be undone by the next startup. Additional direct-FK mutation tests and adversarial ranking/tie/pagination fixtures are deferred. No live MySQL migration test or production JWT smoke test was run. Existing dependency advisories remain outside this change.

## Global Constraints

- API-only. Do not add Blazor pages. Do not change `Program.cs` authorization.
- No new columns or tables. One migration changes the two skill foreign-key delete behaviors from `Cascade` to `Restrict`.
- Action success and error bodies use `ApiResponse<T>` through `ApiControllerBase`. Framework authentication challenges (401/403 from `[Authorize]`) keep the existing empty-body behavior.
- Both controllers use `[ApiValidationEnvelope]` to envelope automatic model-validation errors without changing `Program.cs` or unrelated endpoints.
- Keep the app's existing JSON defaults. The project has no `JsonStringEnumConverter`; `JobType` serializes numerically. Do not add a converter for this feature.
- Admin endpoints use `[Authorize(Roles = "Admin", AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]`.
- Candidate search returns `404 NotFound` for a missing or foreign resume. Never `403`.
- `AdminSkillDto` has no `CreatedAt` field.
- `matchPercentage` is the stored `MatchScore.MatchPercentage`, or `null` when no score exists.
- Skill name: required, trimmed, 1-100 characters, case-insensitive unique. Category: optional, trimmed, maximum 100 characters, empty becomes `null`.
- DTOs keep `[Required]` but no raw `[MaxLength]`; the service validates lengths after trimming. Only expected unique-name/restricted-FK failures map to `409`.
- Admin `pageSize` default 50, maximum 200. Candidate `pageSize` default 20, maximum 100. `minSkillMatches` is clamped to 1-50. `page` is clamped to at least 1.
- Compute page offsets as `long`; return an empty page when the offset is at least the count before casting to `int` for `Skip`.
- Existing public job browse behavior stays unchanged.
- Service methods use `.ConfigureAwait(false)`, matching `AdminService` and `JobService`.

## Review Focus

- Whitespace-only or overlong skill names must return validation errors, not persist bad data. Covered by Task 2.
- Case-only duplicates such as `c#` and `C#` must conflict. Covered by Task 2.
- Deleting a referenced skill returns `409` and leaves resume and job links intact. The `Restrict` foreign keys are the race safety net behind the count check. Covered by Task 3.
- A resume with no skills returns an empty page. Another user's resume returns `404`. Covered by Task 5.
- Jobs with no `MatchScore` return `null` percentage and still sort deterministically. Covered by Task 5.

## File Structure

- `hotjob/DTOs/Admin/SkillDtos.cs`: admin request and response DTOs.
- `hotjob/Services/Admin/IAdminService.cs`: admin skill method contracts.
- `hotjob/Services/Admin/AdminService.cs`: validation, duplicate checks, list/detail queries, create/update/delete.
- `hotjob/Controllers/AdminSkillsController.cs`: HTTP mapping for admin skill CRUD.
- `hotjob/Controllers/ApiValidationEnvelopeAttribute.cs`: feature-scoped automatic validation envelopes.
- `hotjob/DTOs/Job/SkillMatchDtos.cs`: candidate match response DTOs.
- `hotjob/Services/Job/IJobService.cs`: candidate search method contract.
- `hotjob/Services/Job/JobService.cs`: candidate match query.
- `hotjob/Controllers/JobSearchController.cs`: HTTP mapping for candidate search.
- `hotjob/Data/ApplicationDbContext.cs`: skill FK delete behavior.
- Tests under `hotjob/Tests/hotjob.Tests/AdminTests/` and `JobTests/`.
- `hotjob/Tests/hotjob.Tests/AdminTests/SkillApiValidationTests.cs`: real MVC pipeline and normalized-input checks.

---

### Task 1: Admin skill read path

**Files:**
- Create: `hotjob/DTOs/Admin/SkillDtos.cs`
- Modify: `hotjob/Services/Admin/IAdminService.cs`
- Modify: `hotjob/Services/Admin/AdminService.cs`
- Test: `hotjob/Tests/hotjob.Tests/AdminTests/AdminSkillServiceTests.cs`

**Interfaces:**
- Consumes: `ApplicationDbContext`, `TestFixture`.
- Produces:
  - `public sealed record AdminSkillDto(int Id, string Name, string? Category, int ResumeCount, int JobCount);`
  - `Task<ServiceResult<PagedResponse<AdminSkillDto>>> GetSkillsAsync(string? search, string? category, int page, int pageSize, CancellationToken cancellationToken = default);`
  - `Task<ServiceResult<AdminSkillDto>> GetSkillAsync(int skillId, CancellationToken cancellationToken = default);`

- [x] **Step 1: Write the failing tests**

Create `hotjob/Tests/hotjob.Tests/AdminTests/AdminSkillServiceTests.cs`:

```csharp
using hotjob.Data;
using hotjob.DTOs.Admin;
using hotjob.Models;
using hotjob.Models.Job;
using hotjob.Models.Resume;
using hotjob.Models.Users;
using hotjob.Services.Admin;
using hotjob.Services.Common;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Tests.AdminTests;

public sealed class AdminSkillServiceTests
{
    private static AdminService CreateService(TestFixture fx) =>
        new(fx.Db, fx.UserManager, fx.RoleManager);

    private static async Task<Skill> SeedSkillAsync(
        TestFixture fx,
        string name,
        string? category = null)
    {
        var skill = new Skill { Name = name, Category = category };
        fx.Db.Skills.Add(skill);
        await fx.Db.SaveChangesAsync();
        return skill;
    }

    private static async Task AddUsageAsync(TestFixture fx, int skillId)
    {
        var user = await fx.SeedUserAsync(
            email: $"{Guid.NewGuid():N}@example.com",
            role: AppRoles.Candidate);
        var candidate = new Candidate { UserId = user.Id, FullName = "Test Candidate" };
        fx.Db.Candidates.Add(candidate);
        await fx.Db.SaveChangesAsync();

        var resume = new Resume { CandidateId = candidate.Id, Title = "CV" };
        var company = new CompanyProfile
        {
            CompanyName = $"Company {Guid.NewGuid():N}",
            Status = CompanyStatus.Approved
        };
        fx.Db.Resumes.Add(resume);
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();

        fx.Db.ResumeSkills.Add(new ResumeSkill
        {
            ResumeId = resume.Id,
            SkillId = skillId
        });

        var job = new JobPosting
        {
            CompanyProfileId = company.Id,
            Title = "Developer",
            Description = "Build",
            Status = JobStatus.Published
        };
        fx.Db.JobPostings.Add(job);
        await fx.Db.SaveChangesAsync();

        fx.Db.JobSkills.Add(new JobSkill
        {
            JobPostingId = job.Id,
            SkillId = skillId
        });
        await fx.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetSkillsAsync_FiltersAndOrdersByName()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedSkillAsync(fx, "Zeta", "Backend");
        await SeedSkillAsync(fx, "Alpha", "Backend");
        await SeedSkillAsync(fx, "Beta", "Frontend");

        var result = await CreateService(fx).GetSkillsAsync("a", "Backend", 1, 50);

        Assert.True(result.Success);
        Assert.Equal(new[] { "Alpha", "Zeta" }, result.Data!.Items.Select(skill => skill.Name));
        Assert.Equal(2, result.Data.TotalCount);
    }

    [Fact]
    public async Task GetSkillsAsync_ClampsPageAndPageSize()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedSkillAsync(fx, "C#");

        var result = await CreateService(fx).GetSkillsAsync(null, null, 0, 1000);

        Assert.True(result.Success);
        Assert.Equal(1, result.Data!.Page);
        Assert.Equal(200, result.Data.PageSize);
    }

    [Fact]
    public async Task GetSkillAsync_ReturnsUsageCounts()
    {
        await using var fx = await TestFixture.CreateAsync();
        var skill = await SeedSkillAsync(fx, "C#");
        await AddUsageAsync(fx, skill.Id);

        var result = await CreateService(fx).GetSkillAsync(skill.Id);

        Assert.True(result.Success);
        Assert.Equal(1, result.Data!.ResumeCount);
        Assert.Equal(1, result.Data.JobCount);
    }

    [Fact]
    public async Task GetSkillAsync_Missing_ReturnsNotFound()
    {
        await using var fx = await TestFixture.CreateAsync();

        var result = await CreateService(fx).GetSkillAsync(9999);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --filter "FullyQualifiedName~AdminSkillServiceTests"
```

Expected: FAIL to compile because `AdminSkillDto`, `GetSkillsAsync`, and `GetSkillAsync` do not exist.

- [x] **Step 3: Implement the DTO and read methods**

Create `hotjob/DTOs/Admin/SkillDtos.cs`:

```csharp
namespace hotjob.DTOs.Admin;

public sealed record AdminSkillDto(
    int Id,
    string Name,
    string? Category,
    int ResumeCount,
    int JobCount);
```

Add these methods to `IAdminService`:

```csharp
Task<ServiceResult<PagedResponse<AdminSkillDto>>> GetSkillsAsync(
    string? search,
    string? category,
    int page,
    int pageSize,
    CancellationToken cancellationToken = default);

Task<ServiceResult<AdminSkillDto>> GetSkillAsync(
    int skillId,
    CancellationToken cancellationToken = default);
```

Implement in `AdminService`. Use the existing `dbContext` field. List query:

```csharp
page = Math.Max(page, 1);
pageSize = Math.Clamp(pageSize, 1, 200);

var query = dbContext.Skills.AsNoTracking();

if (!string.IsNullOrWhiteSpace(search))
{
    var term = search.Trim();
    query = query.Where(skill => EF.Functions.Like(skill.Name, $"%{term}%"));
}

if (!string.IsNullOrWhiteSpace(category))
{
    var term = category.Trim();
    query = query.Where(skill => skill.Category == term);
}

var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
var offset = (long)(page - 1) * pageSize;
if (offset >= total)
{
    return ServiceResult<PagedResponse<AdminSkillDto>>.Ok(
        new PagedResponse<AdminSkillDto> { TotalCount = total, Page = page, PageSize = pageSize });
}

var items = await query
    .OrderBy(skill => skill.Name)
    .ThenBy(skill => skill.Id)
    .Skip((int)offset)
    .Take(pageSize)
    .Select(skill => new AdminSkillDto(
        skill.Id,
        skill.Name,
        skill.Category,
        skill.ResumeSkills.Count,
        skill.JobSkills.Count))
    .ToListAsync(cancellationToken)
    .ConfigureAwait(false);

return ServiceResult<PagedResponse<AdminSkillDto>>.Ok(
    new PagedResponse<AdminSkillDto>
    {
        Items = items,
        TotalCount = total,
        Page = page,
        PageSize = pageSize
    });
```

Detail query:

```csharp
var dto = await dbContext.Skills
    .AsNoTracking()
    .Where(skill => skill.Id == skillId)
    .Select(skill => new AdminSkillDto(
        skill.Id,
        skill.Name,
        skill.Category,
        skill.ResumeSkills.Count,
        skill.JobSkills.Count))
    .FirstOrDefaultAsync(cancellationToken)
    .ConfigureAwait(false);

return dto is null
    ? ServiceResult<AdminSkillDto>.Fail(ServiceErrorType.NotFound, "Skill not found.")
    : ServiceResult<AdminSkillDto>.Ok(dto);
```

Add `using hotjob.DTOs.Admin;`, `using hotjob.Models;`, and `using Microsoft.EntityFrameworkCore;` to `AdminService.cs` if missing. `Skill` lives in `hotjob.Models`, not `hotjob.Models.Users` or `hotjob.Models.Job`. Add `using hotjob.DTOs.Job;` to `IAdminService.cs` and `AdminService.cs` for `PagedResponse<T>`.

- [x] **Step 4: Run tests to verify they pass**

Run:

```powershell
dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --filter "FullyQualifiedName~AdminSkillServiceTests"
```

Expected: PASS, 4 tests.

- [x] **Step 5: Commit**

```powershell
git add hotjob/DTOs/Admin/SkillDtos.cs hotjob/Services/Admin/IAdminService.cs hotjob/Services/Admin/AdminService.cs hotjob/Tests/hotjob.Tests/AdminTests/AdminSkillServiceTests.cs
git commit -m "feat: add admin skill read API"
```

---

### Task 2: Admin skill create and update

**Files:**
- Modify: `hotjob/DTOs/Admin/SkillDtos.cs`
- Modify: `hotjob/Services/Admin/IAdminService.cs`
- Modify: `hotjob/Services/Admin/AdminService.cs`
- Test: `hotjob/Tests/hotjob.Tests/AdminTests/AdminSkillServiceTests.cs`

**Interfaces:**
- Consumes: `AdminSkillDto`, `GetSkillsAsync`, `GetSkillAsync` from Task 1.
- Produces:
  - `public sealed class CreateSkillRequest`
  - `public sealed class UpdateSkillRequest`
  - `Task<ServiceResult<AdminSkillDto>> CreateSkillAsync(CreateSkillRequest request, CancellationToken cancellationToken = default);`
  - `Task<ServiceResult<AdminSkillDto>> UpdateSkillAsync(int skillId, UpdateSkillRequest request, CancellationToken cancellationToken = default);`

- [x] **Step 1: Write the failing tests**

Add these tests to `AdminSkillServiceTests`:

```csharp
[Fact]
public async Task CreateSkillAsync_ValidRequest_TrimsAndReturnsSkill()
{
    await using var fx = await TestFixture.CreateAsync();

    var result = await CreateService(fx).CreateSkillAsync(
        new CreateSkillRequest { Name = "  Rust  ", Category = "  Backend  " });

    Assert.True(result.Success);
    Assert.Equal("Rust", result.Data!.Name);
    Assert.Equal("Backend", result.Data.Category);
    Assert.Equal(0, result.Data.ResumeCount);
    Assert.Equal(0, result.Data.JobCount);
}

[Fact]
public async Task CreateSkillAsync_DuplicateName_ReturnsConflict()
{
    await using var fx = await TestFixture.CreateAsync();
    await SeedSkillAsync(fx, "C#");

    var result = await CreateService(fx).CreateSkillAsync(
        new CreateSkillRequest { Name = "C#" });

    Assert.False(result.Success);
    Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
}

[Fact]
public async Task CreateSkillAsync_CaseInsensitiveDuplicate_ReturnsConflict()
{
    await using var fx = await TestFixture.CreateAsync();
    await SeedSkillAsync(fx, "C#");

    var result = await CreateService(fx).CreateSkillAsync(
        new CreateSkillRequest { Name = "c#" });

    Assert.False(result.Success);
    Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
}

[Fact]
public async Task CreateSkillAsync_WhitespaceName_ReturnsValidation()
{
    await using var fx = await TestFixture.CreateAsync();

    var result = await CreateService(fx).CreateSkillAsync(
        new CreateSkillRequest { Name = "   " });

    Assert.False(result.Success);
    Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
}

[Fact]
public async Task CreateSkillAsync_NameTooLong_ReturnsValidation()
{
    await using var fx = await TestFixture.CreateAsync();

    var result = await CreateService(fx).CreateSkillAsync(
        new CreateSkillRequest { Name = new string('a', 101) });

    Assert.False(result.Success);
    Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
}

[Fact]
public async Task UpdateSkillAsync_Rename_KeepsResumeAndJobLinks()
{
    await using var fx = await TestFixture.CreateAsync();
    var skill = await SeedSkillAsync(fx, "C#");
    await AddUsageAsync(fx, skill.Id);

    var result = await CreateService(fx).UpdateSkillAsync(
        skill.Id,
        new UpdateSkillRequest { Name = "C# / .NET", Category = "Backend" });

    Assert.True(result.Success);
    Assert.Equal("C# / .NET", result.Data!.Name);
    Assert.Equal(1, result.Data.ResumeCount);
    Assert.Equal(1, result.Data.JobCount);
    Assert.Equal(1, await fx.Db.ResumeSkills.CountAsync());
    Assert.Equal(1, await fx.Db.JobSkills.CountAsync());
}

[Fact]
public async Task UpdateSkillAsync_DuplicateName_ReturnsConflict()
{
    await using var fx = await TestFixture.CreateAsync();
    var first = await SeedSkillAsync(fx, "C#");
    await SeedSkillAsync(fx, "SQL");

    var result = await CreateService(fx).UpdateSkillAsync(
        first.Id,
        new UpdateSkillRequest { Name = "SQL" });

    Assert.False(result.Success);
    Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
}

[Fact]
public async Task UpdateSkillAsync_SameNameDifferentCase_AllowsUpdate()
{
    await using var fx = await TestFixture.CreateAsync();
    var skill = await SeedSkillAsync(fx, "C#");

    var result = await CreateService(fx).UpdateSkillAsync(
        skill.Id,
        new UpdateSkillRequest { Name = "c#" });

    Assert.True(result.Success);
    Assert.Equal("c#", result.Data!.Name);
}
```

`Microsoft.EntityFrameworkCore` is already imported in the test file from Task 1 for `CountAsync`.

- [x] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --filter "FullyQualifiedName~AdminSkillServiceTests"
```

Expected: FAIL to compile because `CreateSkillRequest`, `UpdateSkillRequest`, `CreateSkillAsync`, and `UpdateSkillAsync` do not exist.

- [x] **Step 3: Implement create and update**

Add `using System.ComponentModel.DataAnnotations;` above the `namespace hotjob.DTOs.Admin;` line in `SkillDtos.cs`. Then add the request classes inside the namespace:

```csharp
public sealed class CreateSkillRequest
{
    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Category { get; set; }
}

public sealed class UpdateSkillRequest
{
    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Category { get; set; }
}
```

Add the methods to `IAdminService`.

Implement in `AdminService`:

```csharp
private static (string Name, string? Category, string[] Errors) NormalizeSkillInput(
    string? name,
    string? category)
{
    var errors = new List<string>();
    var normalizedName = name?.Trim() ?? string.Empty;
    var normalizedCategory = string.IsNullOrWhiteSpace(category) ? null : category.Trim();

    if (normalizedName.Length == 0)
        errors.Add("Skill name is required.");

    if (normalizedName.Length > 100)
        errors.Add("Skill name must be 100 characters or fewer.");

    if (normalizedCategory is { Length: > 100 })
        errors.Add("Skill category must be 100 characters or fewer.");

    return (normalizedName, normalizedCategory, errors.ToArray());
}
```

`CreateSkillAsync`:

```csharp
if (request is null)
    return ServiceResult<AdminSkillDto>.Fail(ServiceErrorType.Validation, "Skill data is required.");

var (name, category, errors) = NormalizeSkillInput(request.Name, request.Category);
if (errors.Length > 0)
    return ServiceResult<AdminSkillDto>.Fail(ServiceErrorType.Validation, errors);

var normalizedName = name.ToLower();
var exists = await dbContext.Skills
    .AnyAsync(skill => skill.Name.ToLower() == normalizedName, cancellationToken)
    .ConfigureAwait(false);

if (exists)
    return ServiceResult<AdminSkillDto>.Fail(ServiceErrorType.Conflict, "Skill name already exists.");

var entity = new Skill { Name = name, Category = category };
dbContext.Skills.Add(entity);

try
{
    await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
}
catch (DbUpdateException exception) when (IsSkillNameConflict(exception))
{
    return ServiceResult<AdminSkillDto>.Fail(ServiceErrorType.Conflict, "Skill name already exists.");
}

return ServiceResult<AdminSkillDto>.Ok(
    new AdminSkillDto(entity.Id, entity.Name, entity.Category, 0, 0));
```

`UpdateSkillAsync`:

```csharp
var entity = await dbContext.Skills
    .FirstOrDefaultAsync(skill => skill.Id == skillId, cancellationToken)
    .ConfigureAwait(false);

if (entity is null)
    return ServiceResult<AdminSkillDto>.Fail(ServiceErrorType.NotFound, "Skill not found.");

if (request is null)
    return ServiceResult<AdminSkillDto>.Fail(ServiceErrorType.Validation, "Skill data is required.");

var (name, category, errors) = NormalizeSkillInput(request.Name, request.Category);
if (errors.Length > 0)
    return ServiceResult<AdminSkillDto>.Fail(ServiceErrorType.Validation, errors);

var normalizedName = name.ToLower();
var exists = await dbContext.Skills
    .AnyAsync(
        skill => skill.Id != skillId && skill.Name.ToLower() == normalizedName,
        cancellationToken)
    .ConfigureAwait(false);

if (exists)
    return ServiceResult<AdminSkillDto>.Fail(ServiceErrorType.Conflict, "Skill name already exists.");

entity.Name = name;
entity.Category = category;

try
{
    await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
}
catch (DbUpdateException exception) when (IsSkillNameConflict(exception))
{
    return ServiceResult<AdminSkillDto>.Fail(ServiceErrorType.Conflict, "Skill name already exists.");
}

var dto = await dbContext.Skills
    .AsNoTracking()
    .Where(skill => skill.Id == skillId)
    .Select(skill => new AdminSkillDto(
        skill.Id,
        skill.Name,
        skill.Category,
        skill.ResumeSkills.Count,
        skill.JobSkills.Count))
    .FirstAsync(cancellationToken)
    .ConfigureAwait(false);

return ServiceResult<AdminSkillDto>.Ok(dto);
```

Do not update `ResumeSkill` or `JobSkill` rows. They reference `Skill.Id`.

Add `using System.Data.Common;` and `using MySqlConnector;` to `AdminService.cs`. The catch filter uses this helper so unrelated database/concurrency failures reach the existing server-error handler:

```csharp
private static bool IsSkillNameConflict(DbUpdateException exception) =>
    exception.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry } ||
    exception.InnerException is DbException databaseException &&
    databaseException.Message.Contains("UNIQUE constraint failed: Skills.Name", StringComparison.OrdinalIgnoreCase);
```

- [x] **Step 4: Run tests to verify they pass**

Run:

```powershell
dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --filter "FullyQualifiedName~AdminSkillServiceTests"
```

Expected: PASS, 12 tests.

- [x] **Step 5: Commit**

```powershell
git add hotjob/DTOs/Admin/SkillDtos.cs hotjob/Services/Admin/IAdminService.cs hotjob/Services/Admin/AdminService.cs hotjob/Tests/hotjob.Tests/AdminTests/AdminSkillServiceTests.cs
git commit -m "feat: add admin skill create and update"
```

---

### Task 3: Admin skill delete with restricted foreign keys

**Files:**
- Modify: `hotjob/Data/ApplicationDbContext.cs`
- Modify: `hotjob/Services/Admin/IAdminService.cs`
- Modify: `hotjob/Services/Admin/AdminService.cs`
- Create: EF Core migration under `hotjob/Migrations/`
- Test: `hotjob/Tests/hotjob.Tests/AdminTests/AdminSkillServiceTests.cs`

**Interfaces:**
- Consumes: `AdminSkillDto`, `CreateSkillAsync`, `UpdateSkillAsync` from Tasks 1-2.
- Produces:
  - `Task<ServiceResult> DeleteSkillAsync(int skillId, CancellationToken cancellationToken = default);`

- [x] **Step 1: Write the failing tests**

Add these tests:

```csharp
[Fact]
public async Task DeleteSkillAsync_UnusedSkill_Deletes()
{
    await using var fx = await TestFixture.CreateAsync();
    var skill = await SeedSkillAsync(fx, "Rust");

    var result = await CreateService(fx).DeleteSkillAsync(skill.Id);

    Assert.True(result.Success);
    Assert.Null(await fx.Db.Skills.FindAsync(skill.Id));
}

[Fact]
public async Task DeleteSkillAsync_SkillInUse_ReturnsConflict()
{
    await using var fx = await TestFixture.CreateAsync();
    var skill = await SeedSkillAsync(fx, "C#");
    await AddUsageAsync(fx, skill.Id);

    var result = await CreateService(fx).DeleteSkillAsync(skill.Id);

    Assert.False(result.Success);
    Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
    Assert.NotNull(await fx.Db.Skills.FindAsync(skill.Id));
    Assert.Equal(1, await fx.Db.ResumeSkills.CountAsync());
    Assert.Equal(1, await fx.Db.JobSkills.CountAsync());
}
```

- [x] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --filter "FullyQualifiedName~AdminSkillServiceTests"
```

Expected: FAIL to compile because `DeleteSkillAsync` does not exist. The in-use test will pass through the service count check; the foreign-key change protects the race between that check and the delete.

- [x] **Step 3: Implement the restricted FK and delete method**

In `ApplicationDbContext.OnModelCreating`, change these two lines:

```csharp
modelBuilder.Entity<ResumeSkill>().HasOne(x => x.Skill).WithMany(x => x.ResumeSkills).HasForeignKey(x => x.SkillId).OnDelete(DeleteBehavior.Restrict);
modelBuilder.Entity<JobSkill>().HasOne(x => x.Skill).WithMany(x => x.JobSkills).HasForeignKey(x => x.SkillId).OnDelete(DeleteBehavior.Restrict);
```

Add `DeleteSkillAsync` to `IAdminService`.

Implement in `AdminService`:

```csharp
var entity = await dbContext.Skills
    .FirstOrDefaultAsync(skill => skill.Id == skillId, cancellationToken)
    .ConfigureAwait(false);

if (entity is null)
    return ServiceResult.Fail(ServiceErrorType.NotFound, "Skill not found.");

var resumeCount = await dbContext.ResumeSkills
    .CountAsync(link => link.SkillId == skillId, cancellationToken)
    .ConfigureAwait(false);

var jobCount = await dbContext.JobSkills
    .CountAsync(link => link.SkillId == skillId, cancellationToken)
    .ConfigureAwait(false);

if (resumeCount > 0 || jobCount > 0)
{
    return ServiceResult.Fail(
        ServiceErrorType.Conflict,
        $"Cannot delete skill '{entity.Name}': used in {resumeCount} resumes and {jobCount} job postings.");
}

dbContext.Skills.Remove(entity);

try
{
    await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
}
catch (DbUpdateException exception) when (IsSkillReferenceConflict(exception))
{
    return ServiceResult.Fail(ServiceErrorType.Conflict, "Cannot delete skill: it is in use.");
}

return ServiceResult.Ok();
```

Add the corresponding restricted-delete helper:

```csharp
private static bool IsSkillReferenceConflict(DbUpdateException exception) =>
    exception.InnerException is MySqlException { ErrorCode: MySqlErrorCode.RowIsReferenced2 } ||
    exception.InnerException is DbException databaseException &&
    databaseException.Message.Contains("FOREIGN KEY constraint failed", StringComparison.OrdinalIgnoreCase);
```

Generate the migration:

```powershell
dotnet ef migrations add RestrictSkillDeletes --project hotjob/hotjob.csproj --startup-project hotjob/hotjob.csproj
```

Expected: one new migration file under `hotjob/Migrations/` that drops and recreates the two skill foreign keys with `OnDelete: ReferentialAction.Restrict`.

- [x] **Step 4: Run tests to verify they pass**

Run:

```powershell
dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --filter "FullyQualifiedName~AdminSkillServiceTests"
```

Expected: PASS, 14 tests.

- [x] **Step 5: Commit**

```powershell
git add hotjob/Data/ApplicationDbContext.cs hotjob/Services/Admin/IAdminService.cs hotjob/Services/Admin/AdminService.cs hotjob/Migrations hotjob/Tests/hotjob.Tests/AdminTests/AdminSkillServiceTests.cs
git commit -m "feat: add admin skill delete with restricted references"
```

---

### Task 4: Admin skills controller

**Files:**
- Create: `hotjob/Controllers/AdminSkillsController.cs`
- Test: `hotjob/Tests/hotjob.Tests/AdminTests/AdminSkillsControllerTests.cs`

**Interfaces:**
- Consumes: all five `IAdminService` skill methods from Tasks 1-3.
- Produces: HTTP endpoints:
  - `GET /api/admin/skills`
  - `GET /api/admin/skills/{id:int}`
  - `POST /api/admin/skills`
  - `PUT /api/admin/skills/{id:int}`
  - `DELETE /api/admin/skills/{id:int}`

- [x] **Step 1: Write the failing controller tests**

Create `AdminSkillsControllerTests.cs` with a stub `IAdminService`. Use the existing controller-test style from `hotjob/Tests/hotjob.Tests/CompanyTests/EmployerCompanyControllerTests.cs`.

File usings:

```csharp
using System.Reflection;
using HotJob.Dtos;
using hotjob.Controllers;
using hotjob.DTOs.Admin;
using hotjob.DTOs.Job;
using hotjob.Models.Users;
using hotjob.Services.Admin;
using hotjob.Services.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
```

Required tests:

```csharp
[Fact]
public async Task List_Success_ReturnsOkEnvelope()
{
    var service = new StubAdminService
    {
        PageResult = ServiceResult<PagedResponse<AdminSkillDto>>.Ok(
            new PagedResponse<AdminSkillDto>
            {
                Items = new[] { new AdminSkillDto(1, "C#", "Backend", 2, 3) },
                TotalCount = 1,
                Page = 1,
                PageSize = 50
            })
    };
    var controller = new AdminSkillsController(service);

    var result = await controller.List(null, null, 1, 50, CancellationToken.None);

    var ok = Assert.IsType<OkObjectResult>(result);
    var body = Assert.IsType<ApiResponse<PagedResponse<AdminSkillDto>>>(ok.Value);
    Assert.True(body.Success);
    Assert.Single(body.Data!.Items);
}

[Fact]
public async Task Create_Success_ReturnsCreated()
{
    var service = new StubAdminService
    {
        CreateResult = ServiceResult<AdminSkillDto>.Ok(
            new AdminSkillDto(4, "Rust", "Backend", 0, 0))
    };
    var controller = new AdminSkillsController(service);

    var result = await controller.Create(
        new CreateSkillRequest { Name = "Rust", Category = "Backend" },
        CancellationToken.None);

    Assert.Equal(StatusCodes.Status201Created, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
}

[Fact]
public async Task Delete_Conflict_Returns409()
{
    var service = new StubAdminService
    {
        DeleteResult = ServiceResult.Fail(ServiceErrorType.Conflict, "in use")
    };
    var controller = new AdminSkillsController(service);

    var result = await controller.Delete(1, CancellationToken.None);

    Assert.Equal(StatusCodes.Status409Conflict, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
}

[Fact]
public async Task Delete_Success_Returns204()
{
    var controller = new AdminSkillsController(new StubAdminService
    {
        DeleteResult = ServiceResult.Ok()
    });

    var result = await controller.Delete(1, CancellationToken.None);

    Assert.IsType<NoContentResult>(result);
}

[Fact]
public async Task Get_Missing_Returns404()
{
    var service = new StubAdminService
    {
        DetailResult = ServiceResult<AdminSkillDto>.Fail(ServiceErrorType.NotFound, "missing")
    };
    var controller = new AdminSkillsController(service);

    var result = await controller.Get(999, CancellationToken.None);

    Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
}

[Fact]
public void Controller_RequiresAdminJwt()
{
    var authorize = typeof(AdminSkillsController).GetCustomAttribute<AuthorizeAttribute>();

    Assert.NotNull(authorize);
    Assert.Equal("Admin", authorize!.Roles);
    Assert.Equal(JwtBearerDefaults.AuthenticationScheme, authorize.AuthenticationSchemes);
}
```

The stub implements every `IAdminService` member. The seven existing members can return empty or not-found results. The five skill members return `PageResult`, `DetailResult`, `CreateResult`, `UpdateResult`, and `DeleteResult`.

- [x] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --filter "FullyQualifiedName~AdminSkillsControllerTests"
```

Expected: FAIL to compile because `AdminSkillsController` does not exist.

- [x] **Step 3: Implement the controller**

Create `hotjob/Controllers/AdminSkillsController.cs`. Match the `EmployerCompanyController` house style: one XML `<summary>` per action, `<response>` lines for each status the spec documents, and `[ProducesResponseType(typeof(ApiResponse<T>), StatusCodes.StatusNNN)]` for 200/201/400/401/403/404/409. Add `using HotJob.Dtos;` for `ApiResponse<T>`. Keep the signatures below unchanged.

```csharp
using hotjob.DTOs.Admin;
using hotjob.Services.Admin;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace hotjob.Controllers;

[ApiController]
[ApiValidationEnvelope]
[Route("api/admin/skills")]
[Authorize(Roles = "Admin", AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class AdminSkillsController(IAdminService adminService) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] string? category,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        FromResult(await adminService.GetSkillsAsync(
            search, category, page, pageSize, cancellationToken));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(
        int id,
        CancellationToken cancellationToken = default) =>
        FromResult(await adminService.GetSkillAsync(id, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateSkillRequest request,
        CancellationToken cancellationToken = default) =>
        FromResultCreated(await adminService.CreateSkillAsync(request, cancellationToken));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpdateSkillRequest request,
        CancellationToken cancellationToken = default) =>
        FromResult(await adminService.UpdateSkillAsync(id, request, cancellationToken));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(
        int id,
        CancellationToken cancellationToken = default)
    {
        var result = await adminService.DeleteSkillAsync(id, cancellationToken);
        return result.Success ? NoContent() : FromError(result);
    }
}
```

- [x] **Step 4: Run tests to verify they pass**

Run:

```powershell
dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --filter "FullyQualifiedName~AdminSkillsControllerTests"
```

Expected: PASS, 6 tests.

- [x] **Step 5: Commit**

```powershell
git add hotjob/Controllers/AdminSkillsController.cs hotjob/Tests/hotjob.Tests/AdminTests/AdminSkillsControllerTests.cs
git commit -m "feat: add admin skills controller"
```

---

### Task 5: Candidate match query

**Files:**
- Create: `hotjob/DTOs/Job/SkillMatchDtos.cs`
- Modify: `hotjob/Services/Job/IJobService.cs`
- Modify: `hotjob/Services/Job/JobService.cs`
- Test: `hotjob/Tests/hotjob.Tests/JobTests/JobSkillMatchTests.cs`

**Interfaces:**
- Consumes: `JobService` constructor, `ApplicationDbContext`, `JobPostingRepository`.
- Produces:
  - `public sealed record MatchedSkillDto(int SkillId, string SkillName);`
  - `public sealed record SkillMatchJobListItemDto(...)` exactly as the spec.
  - `Task<ServiceResult<PagedResponse<SkillMatchJobListItemDto>>> GetJobsMatchingResumeAsync(string userId, int resumeId, string? city, JobType? jobType, int minSkillMatches, int page, int pageSize, CancellationToken cancellationToken = default);`

- [x] **Step 1: Write the failing tests**

Create `JobSkillMatchTests.cs`. File usings:

```csharp
using hotjob.Models;
using hotjob.Models.Job;
using hotjob.Models.ML;
using hotjob.Models.Resume;
using hotjob.Models.Users;
using hotjob.Repositories.Job;
using hotjob.Services.Billing;
using hotjob.Services.Common;
using hotjob.Services.Job;
using hotjob.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
```

Reuse the service construction pattern from `PublicJobDetailTests.cs`:

```csharp
private static JobService CreateService(TestFixture fx) =>
    new(
        fx.Db,
        new JobPostingRepository(fx.Db),
        new EntitlementService(fx.Db, TimeProvider.System),
        new StubJobAlertService(),
        NullLogger<JobService>.Instance);
```

Add these exact helper signatures:

```csharp
private static async Task<(string UserId, int ResumeId)> SeedCandidateWithSkillsAsync(
    TestFixture fx,
    params string[] skillNames)
{
    var (candidate, resume) = await MatchTestData.CreateCandidateAsync(
        fx,
        $"{Guid.NewGuid():N}@example.com",
        skillNames.Select(name => (name, 1)).ToArray());

    return (candidate.UserId, resume.Id);
}

private static async Task<int> SeedJobAsync(
    TestFixture fx,
    string title,
    string[] skillNames,
    int? resumeId = null,
    double? score = null,
    string city = "Hanoi",
    JobType jobType = JobType.FullTime,
    DateTime? expiryDate = null,
    CompanyStatus companyStatus = CompanyStatus.Approved)
{
    // Create CompanyProfile (CompanyName unique, Location = city, Status = companyStatus),
    // JobPosting (Description required, Status = Published, CreatedAt = DateTime.UtcNow),
    // and JobSkill rows. Get each Skill through MatchTestData.GetOrCreateSkillAsync so
    // repeated names reuse one row. Add a MatchScore row only when both resumeId and score are non-null.
}
```

Required tests and assertions:

```csharp
[Fact]
public async Task GetJobsMatchingResumeAsync_ReturnsMatchingJobsWithSkills()
{
    await using var fx = await TestFixture.CreateAsync();
    var (userId, resumeId) = await SeedCandidateWithSkillsAsync(fx, "C#", "SQL");
    var jobId = await SeedJobAsync(
        fx, "Senior .NET", ["C#", "SQL"], resumeId: resumeId, score: 80);

    var result = await CreateService(fx).GetJobsMatchingResumeAsync(
        userId, resumeId, null, null, 1, 1, 20);

    Assert.True(result.Success);
    var item = Assert.Single(result.Data!.Items);
    Assert.Equal(jobId, item.Id);
    Assert.Equal(2, item.MatchedSkillCount);
    Assert.Equal(new[] { "C#", "SQL" }, item.MatchedSkills.Select(skill => skill.SkillName));
    Assert.Equal(80d, item.MatchPercentage!.Value);
}
```

Additional tests:

- `GetJobsMatchingResumeAsync_ResumeWithNoSkills_ReturnsEmpty`: seed candidate with no `ResumeSkill` rows; assert success, `TotalCount == 0`, `Items` empty.
- `GetJobsMatchingResumeAsync_MinSkillMatchesFilters`: job with one matching skill; call `minSkillMatches = 2`; assert empty.
- `GetJobsMatchingResumeAsync_SortsByMatchedCountThenScore`: seed job A with 2 matches and score 50, job B with 2 matches and score 90, job C with 1 match; pass the same `resumeId` to `SeedJobAsync` for scores A and B; assert B, A, C.
- `GetJobsMatchingResumeAsync_OtherUserResume_ReturnsNotFound`: call with `"other-user"`; assert `ServiceErrorType.NotFound`.
- `GetJobsMatchingResumeAsync_MissingResume_ReturnsNotFound`: call with `9999`; assert `NotFound`.
- `GetJobsMatchingResumeAsync_ExpiredJobExcluded`: seed `ExpiryDate = DateTime.UtcNow.AddDays(-1)`; assert empty.
- `GetJobsMatchingResumeAsync_PendingCompanyExcluded`: seed company `CompanyStatus.Pending`; assert empty.
- `GetJobsMatchingResumeAsync_NoMatchScore_ReturnsNullPercentage`: no `MatchScore` row; assert `item.MatchPercentage is null` and success.
- `GetJobsMatchingResumeAsync_CityAndJobTypeFilters`: one job in `Hanoi`/`FullTime`, one in `Da Nang`/`PartTime`; filter `city: "Ha"` and `jobType: JobType.FullTime`; assert only first.
- `GetJobsMatchingResumeAsync_ClampsPageAndMinMatches`: call `page: 0`, `pageSize: 500`, `minSkillMatches: 0`; assert `Page == 1`, `PageSize == 100`; job with one match is returned.

- [x] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --filter "FullyQualifiedName~JobSkillMatchTests"
```

Expected: FAIL to compile because `SkillMatchJobListItemDto` and `GetJobsMatchingResumeAsync` do not exist.

- [x] **Step 3: Implement the DTOs and query**

Create `hotjob/DTOs/Job/SkillMatchDtos.cs` with `using hotjob.Models.Job;` above `namespace hotjob.DTOs.Job;` and the two records from the spec.

Add the method to `IJobService`.

Implement in `JobService` using the query shape from the spec:

1. Clamp `page`, `pageSize`, and `minSkillMatches`.
2. Load the resume and verify ownership. Return `NotFound` for missing or foreign resume.
3. Load resume skill IDs. Return an empty `PagedResponse` when none exist.
4. Build the published-job query with expiry, approved company, and overlapping-skill filters.
5. Apply optional city and job type filters.
6. Project scalar job fields, `MatchedSkillCount`, and `MatchPercentage` from `job.MatchScores`.
7. Filter by `minSkillMatches`, count, order, and page.
8. Load matched skill names for the page in one query.
9. Group and map to `SkillMatchJobListItemDto`.

After counting, calculate `var offset = (long)(page - 1) * pageSize;`. If `offset >= total`, return an empty `PagedResponse` preserving total/page/pageSize. Otherwise use `.Skip((int)offset)`.

Use these exact ordering keys:

```csharp
.OrderByDescending(row => row.MatchedSkillCount)
.ThenByDescending(row => row.MatchPercentage ?? 0)
.ThenByDescending(row => row.CreatedAt)
.ThenByDescending(row => row.Id)
```

Do not use `Include` with a projection. Do not load `MatchScores` into a dictionary before ordering. Apply `.ConfigureAwait(false)` to every `await`, matching the rest of `JobService`.

- [x] **Step 4: Run tests to verify they pass**

Run:

```powershell
dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --filter "FullyQualifiedName~JobSkillMatchTests"
```

Expected: PASS, 11 tests.

- [x] **Step 5: Commit**

```powershell
git add hotjob/DTOs/Job/SkillMatchDtos.cs hotjob/Services/Job/IJobService.cs hotjob/Services/Job/JobService.cs hotjob/Tests/hotjob.Tests/JobTests/JobSkillMatchTests.cs
git commit -m "feat: add candidate skill match query"
```

---

### Task 6: Candidate job search controller

**Files:**
- Create: `hotjob/Controllers/JobSearchController.cs`
- Test: `hotjob/Tests/hotjob.Tests/JobTests/JobSearchControllerTests.cs`

**Interfaces:**
- Consumes: `GetJobsMatchingResumeAsync` from Task 5.
- Produces: `GET /api/jobs/matching-resume/{resumeId:int}`.

- [x] **Step 1: Write the failing controller tests**

Create `JobSearchControllerTests.cs` with a stub `IJobService`.

File usings:

```csharp
using System.Security.Claims;
using HotJob.Dtos;
using hotjob.Controllers;
using hotjob.DTOs.Job;
using hotjob.Models.Job;
using hotjob.Services.Common;
using hotjob.Services.Job;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
```

Required tests:

```csharp
[Fact]
public async Task MatchResume_Success_ReturnsOkEnvelope()
{
    var service = new StubJobService
    {
        MatchResult = ServiceResult<PagedResponse<SkillMatchJobListItemDto>>.Ok(
            new PagedResponse<SkillMatchJobListItemDto>
            {
                Items = Array.Empty<SkillMatchJobListItemDto>(),
                TotalCount = 0,
                Page = 1,
                PageSize = 20
            })
    };
    var controller = new JobSearchController(service)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "user-1")],
                    "test"))
            }
        }
    };

    var result = await controller.MatchResume(1, null, null, 1, 1, 20, CancellationToken.None);

    var ok = Assert.IsType<OkObjectResult>(result);
    var body = Assert.IsType<ApiResponse<PagedResponse<SkillMatchJobListItemDto>>>(ok.Value);
    Assert.True(body.Success);
}

[Fact]
public async Task MatchResume_MissingUser_Returns401()
{
    var controller = new JobSearchController(new StubJobService())
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        }
    };

    var result = await controller.MatchResume(1, null, null, 1, 1, 20, CancellationToken.None);

    Assert.Equal(
        StatusCodes.Status401Unauthorized,
        Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
}

[Fact]
public async Task MatchResume_NotFound_Returns404()
{
    var service = new StubJobService
    {
        MatchResult = ServiceResult<PagedResponse<SkillMatchJobListItemDto>>.Fail(
            ServiceErrorType.NotFound,
            "Resume not found.")
    };
    var controller = new JobSearchController(service)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "user-1")],
                    "test"))
            }
        }
    };

    var result = await controller.MatchResume(99, null, null, 1, 1, 20, CancellationToken.None);

    Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
}
```

The stub implements all `IJobService` methods; only `GetJobsMatchingResumeAsync` returns `MatchResult`. Other methods can return `ServiceResult<T>.Fail(ServiceErrorType.NotFound)`.

- [x] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --filter "FullyQualifiedName~JobSearchControllerTests"
```

Expected: FAIL to compile because `JobSearchController` does not exist.

- [x] **Step 3: Implement the controller**

Create `hotjob/Controllers/JobSearchController.cs`. Add an XML `<summary>` and `<response>` lines for 200/400/401/404, plus `[ProducesResponseType(typeof(ApiResponse<T>), StatusCodes.StatusNNN)]`, matching `EmployerCompanyController`. Add `using HotJob.Dtos;`. Keep the signature below unchanged.

```csharp
using System.Security.Claims;
using hotjob.DTOs.Job;
using hotjob.Models.Job;
using hotjob.Services.Common;
using hotjob.Services.Job;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace hotjob.Controllers;

[ApiController]
[ApiValidationEnvelope]
[Route("api/jobs")]
[Authorize]
public sealed class JobSearchController(IJobService jobService) : ApiControllerBase
{
    [HttpGet("matching-resume/{resumeId:int}")]
    public async Task<IActionResult> MatchResume(
        int resumeId,
        [FromQuery] string? city,
        [FromQuery] JobType? jobType,
        [FromQuery] int minSkillMatches = 1,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(userId))
        {
            return FromError(ServiceResult.Fail(
                ServiceErrorType.Unauthorized, "Not authenticated."));
        }

        return FromResult(await jobService.GetJobsMatchingResumeAsync(
            userId,
            resumeId,
            city,
            jobType,
            minSkillMatches,
            page,
            pageSize,
            cancellationToken));
    }
}
```

- [x] **Step 4: Run tests to verify they pass**

Run:

```powershell
dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj --filter "FullyQualifiedName~JobSearchControllerTests"
```

Expected: PASS, 3 tests.

- [x] **Step 5: Commit**

```powershell
git add hotjob/Controllers/JobSearchController.cs hotjob/Tests/hotjob.Tests/JobTests/JobSearchControllerTests.cs
git commit -m "feat: add candidate job search controller"
```

---

### Task 7: Full verification

**Files:** none.

- [x] **Step 1: Run the full test suite**

```powershell
dotnet test hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj
```

Expected: all tests pass, 0 failures.

- [x] **Step 2: Build the solution**

```powershell
dotnet build hotjob.slnx
```

Expected: build succeeds with 0 errors.

- [x] **Step 3: Check the migration**

```powershell
dotnet ef migrations list --no-connect --project hotjob/hotjob.csproj --startup-project hotjob/hotjob.csproj
```

Expected: `RestrictSkillDeletes` appears after the existing migrations.

Then confirm the generated migration changes both skill foreign keys:

```powershell
Get-ChildItem hotjob/Migrations/*RestrictSkillDeletes.cs | Select-String -Pattern "ReferentialAction.Restrict"
```

Expected: two matches.

- [x] **Step 4: Commit any final changes**

If verification required no changes, skip this step. Otherwise:

```powershell
git add -- hotjob/Controllers/AdminSkillsController.cs hotjob/Controllers/JobSearchController.cs hotjob/Controllers/ApiValidationEnvelopeAttribute.cs hotjob/DTOs/Admin/SkillDtos.cs hotjob/Services/Admin/AdminService.cs hotjob/Services/Job/JobService.cs hotjob/Tests/hotjob.Tests/AdminTests/AdminSkillServiceTests.cs hotjob/Tests/hotjob.Tests/AdminTests/SkillApiValidationTests.cs hotjob/Tests/hotjob.Tests/JobTests/JobSkillMatchTests.cs
git commit -m "test: verify skill management API"
```

---

## Self-Review

- **Spec coverage:** Admin list/detail/create/update/delete, validation, duplicate handling, FK restriction, candidate search, filters, sorting, paging, DTOs, controllers, tests, and migration each map to a task.
- **Step scan:** Every test step has a failing command and expected failure. Every implementation step names the file and exact signature. Verification steps have commands and expected results.
- **Type consistency:** `AdminSkillDto`, `CreateSkillRequest`, `UpdateSkillRequest`, `SkillMatchJobListItemDto`, and `MatchedSkillDto` are spelled the same in every task. The five `IAdminService` methods and the one `IJobService` method match the spec.
- **Review Focus:** Whitespace and overlong names are covered in Task 2. Case-only duplicates are covered in Task 2. Referenced-skill deletion is covered in Task 3. Zero-skill and foreign resumes are covered in Task 5. Missing scores and tied counts are covered in Task 5.
- **Proportion:** The plan uses signatures, test names, and only the query shape that the spec pins. It does not reproduce full application code.

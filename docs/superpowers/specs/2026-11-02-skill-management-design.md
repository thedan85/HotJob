# Skill Management Feature Specification

## Overview

Add two API capabilities:

1. **Admin Skills CRUD** - Admin endpoints to create, update, delete, and list skills.
2. **Candidate Skill-Based Job Search** - Find published jobs matching a resume's skills.

This phase is API-only. Existing Blazor pages stay unchanged. The endpoints serve Swagger and external API clients. No new admin or candidate UI is in scope.

## Goals

- Admin can manage the Skills catalog through the API (create, rename, delete, list, search).
- Candidates can search published jobs that match their resume skills.
- Maintain data integrity: unique skill names, no deletion while referenced, no orphaned references.
- Follow existing codebase patterns: REST controllers, service layer, `ServiceResult`, `ApiResponse`, DTOs, pagination.
- Keep the change small: no new columns, no new tables, no new authorization policy.

## Non-Goals

- Blazor admin or candidate pages for this feature.
- Skill recommendation engine.
- Skill taxonomy or hierarchy.
- Bulk import/export.
- Skill versioning.
- Skill synonyms or aliases.
- Skill proficiency levels or endorsements.

## Technical Approach

- New `AdminSkillsController` at `/api/admin/skills`, deriving `ApiControllerBase`.
- New `JobSearchController` at `/api/jobs`, deriving `ApiControllerBase`.
- Extend `IAdminService` and `IJobService`.
- Reuse the existing `Skill` entity and the existing `ResumeSkill` / `JobSkill` join rows.
- Reuse `PagedResponse<T>`, `ApiResponse<T>`, and `ServiceResult`.
- Opt both new controllers into `ApiValidationEnvelopeAttribute` so automatic body/query validation also returns the envelope, without changing unrelated controllers.
- Admin endpoints use the existing role attribute pattern:
  `[Authorize(Roles = "Admin", AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]`.
- Candidate search uses `[Authorize]` and verifies resume ownership in the service.
- Do not add a custom authorization policy and do not change `Program.cs`.

## Admin Skills CRUD

### Endpoints

Action responses use the existing envelope, except successful deletes return an empty `204` response:

```json
{ "success": true, "data": { }, "errors": [] }
```

Action errors and automatic model-validation errors use:

```json
{ "success": false, "data": null, "errors": ["..."] }
```

Framework authentication/authorization challenges keep the existing empty-body `401`/`403` behavior.

#### 1. List All Skills

**GET** `/api/admin/skills`

**Auth:** Admin role required.

**Query params:**
- `search` (optional): case-insensitive Name filter.
- `category` (optional): exact Category filter.
- `page` (default: 1, minimum: 1).
- `pageSize` (default: 50, minimum: 1, maximum: 200).

**Ordering:** `Name` ascending, then `Id` ascending. This makes pagination stable.

**Response 200:**

```json
{
  "success": true,
  "data": {
    "items": [
      {
        "id": 1,
        "name": "C#",
        "category": "Backend",
        "resumeCount": 45,
        "jobCount": 23
      }
    ],
    "totalCount": 120,
    "page": 1,
    "pageSize": 50,
    "totalPages": 3
  },
  "errors": []
}
```

**Response 401:** Not authenticated.
**Response 403:** Not admin.

#### 2. Get Single Skill

**GET** `/api/admin/skills/{id}`

**Auth:** Admin role required.

**Response 200:**

```json
{
  "success": true,
  "data": {
    "id": 1,
    "name": "C#",
    "category": "Backend``",
    "resumeCount": 45,
    "jobCount": 23
  },
  "errors": []
}
```

**Response 401:** Not authenticated.
**Response 403:** Not admin.
**Response 404:** Skill not found.

#### 3. Create Skill

**POST** `/api/admin/skills`

**Auth:** Admin role required.

**Request body:**

```json
{
  "name": "Rust",
  "category": "Backend"
}
```

**Validation:**
- `name`: required, trimmed, 1-100 characters.
- `category`: optional, trimmed, maximum 100 characters. Empty becomes `null`.
- Duplicate check is case-insensitive.
- Validation lives in `AdminService`, not only in DTO annotations.

**Response 201:**

```json
{
  "success": true,
  "data": {
    "id": 125,
    "name": "Rust",
    "category": "Backend",
    "resumeCount": 0,
    "jobCount": 0
  },
  "errors": []
}
```

**Response 400:** Validation errors.
**Response 401:** Not authenticated.
**Response 403:** Not admin.
**Response 409:** Skill name already exists (case-insensitive).

#### 4. Update Skill

**PUT** `/api/admin/skills/{id}`

**Auth:** Admin role required.

**Request body:**

```json
{
  "name": "C# / .NET",
  "category": "Backend"
}
```

**Validation:**
- Same as Create.
- If the name changes, duplicate check excludes the current skill.
- No `ResumeSkill` or `JobSkill` rows need updating. Both join rows reference `Skill.Id`; renaming the skill keeps every reference valid.

**Response 200:**

```json
{
  "success": true,
  "data": {
    "id": 1,
    "name": "C# / .NET",
    "category": "Backend",
    "resumeCount": 45,
    "jobCount": 23
  },
  "errors": []
}
```

**Response 400:** Validation errors.
**Response 401:** Not authenticated.
**Response 403:** Not admin.
**Response 404:** Skill not found.
**Response 409:** New name conflicts with an existing skill.

#### 5. Delete Skill

**DELETE** `/api/admin/skills/{id}`

**Auth:** Admin role required.

**Behavior:**
- Check `ResumeSkills` and `JobSkills` counts.
- If either count is greater than zero, return `409 Conflict` with both counts.
- If both are zero, delete the skill.
- The database FK is `ON DELETE RESTRICT` for both skill join tables. A concurrent insert after the count check cannot cause a cascade delete. A foreign-key violation is caught and returned as `409 Conflict`.

**Response 204:** Deleted successfully.

**Response 401:** Not authenticated.
**Response 403:** Not admin.
**Response 404:** Skill not found.
**Response 409:** Skill is in use.

```json
{
  "success": false,
  "data": null,
  "errors": ["Cannot delete skill 'C#': used in 45 resumes and 23 job postings."]
}
```

### DTOs

Only one skill response DTO is needed. List and detail fields are identical.

```csharp
public sealed record AdminSkillDto(
    int Id,
    string Name,
    string? Category,
    int ResumeCount,
    int JobCount);
```

```csharp
public sealed class CreateSkillRequest
{
    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Category { get; set; }
}
```

```csharp
public sealed class UpdateSkillRequest
{
    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Category { get; set; }
}
```

There is no `CreatedAt` field. The `Skills` table has no such column, and this feature adds no columns.

Validate lengths only after service normalization. Raw DTO length annotations would reject a padded 100-character name or a whitespace-only category before trimming.

### Service Interface

Extend `IAdminService`:

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

Task<ServiceResult<AdminSkillDto>> CreateSkillAsync(
    CreateSkillRequest request,
    CancellationToken cancellationToken = default);

Task<ServiceResult<AdminSkillDto>> UpdateSkillAsync(
    int skillId,
    UpdateSkillRequest request,
    CancellationToken cancellationToken = default);

Task<ServiceResult> DeleteSkillAsync(
    int skillId,
    CancellationToken cancellationToken = default);
```

### Implementation Notes

**Normalization and validation:**

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

**List query:** project counts so the query does not load every join row.

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

var total = await query.CountAsync(cancellationToken);
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
    .ToListAsync(cancellationToken);
```

**Duplicate check:**

```csharp
var normalizedName = request.Name.Trim();
var exists = await dbContext.Skills
    .AnyAsync(
        skill => skill.Id != excludedSkillId
            && skill.Name.ToLower() == normalizedName.ToLower(),
        cancellationToken);
```

For create, use `excludedSkillId = 0`. For update, exclude the current skill.

**Create:**

- Normalize and validate.
- Run duplicate check.
- Add the entity.
- Save changes.
- Catch `DbUpdateException` from the unique `Name` index and return `Conflict`.
- Return `AdminSkillDto` with zero counts.

**Update:**

- Load the skill; missing returns `NotFound`.
- Normalize and validate.
- Run duplicate check excluding `skillId`.
- Assign `Name` and `Category`.
- Save changes.
- Catch `DbUpdateException` from the unique index and return `Conflict`.
- Return the DTO with current counts.

**Delete:**

- Load the skill; missing returns `NotFound`.
- Count `ResumeSkills` and `JobSkills` with projection or `CountAsync`.
- Non-zero counts return `Conflict` with the spec message.
- Remove and save.
- Catch `DbUpdateException` from the restricted FK and return `Conflict` with a generic in-use message.

Use catch filters for expected constraint violations only. MySQL `DuplicateKeyEntry` maps to a name conflict and `RowIsReferenced2` maps to a restricted-delete conflict. Recognize the equivalent SQLite constraint failures for the existing test provider. Unrelated storage and concurrency exceptions propagate to the existing server-error handler.

## Candidate Skill-Based Job Search

### Endpoint

**GET** `/api/jobs/matching-resume/{resumeId}`

**Auth:** Authenticated user; must own the resume.

**Path params:**
- `resumeId`: int, required.

**Query params:**
- `city` (optional): case-insensitive filter on `Company.Location`.
- `jobType` (optional): one of `FullTime`, `PartTime`, `Contract`, `Internship`, `Freelance`, `Remote`.
- `minSkillMatches` (optional, default: 1, clamped to 1-50).
- `page` (default: 1, clamped to minimum 1).
- `pageSize` (default: 20, clamped to 1-100).

**Response 200:**

```json
{
  "success": true,
  "data": {
    "items": [
      {
        "id": 42,
        "title": "Senior .NET Developer",
        "companyName": "Acme Corp",
        "location": "Hanoi",
        "salaryMin": 2000,
        "salaryMax": 3500,
        "experienceRequired": 5,
        "jobType": 0,
        "expiryDate": "2026-12-31T00:00:00Z",
        "matchedSkillCount": 4,
        "matchedSkills": [
          { "skillId": 1, "skillName": "C#" },
          { "skillId": 2, "skillName": "SQL" },
          { "skillId": 5, "skillName": "Azure" },
          { "skillId": 8, "skillName": "React" }
        ],
        "matchPercentage": 87.5,
        "createdAt": "2026-10-15T10:00:00Z",
        "featuredUntil": null
      }
    ],
    "totalCount": 18,
    "page": 1,
    "pageSize": 20,
    "totalPages": 1
  },
  "errors": []
}
```

`matchPercentage` is the stored `MatchScore.MatchPercentage` for `(resumeId, jobId)`, or `null` when no score exists. It is not computed from `matchedSkillCount`.

`jobType` uses the existing numeric JSON serialization (`FullTime` is `0`). Do not add an enum converter.

**Response 401:** Not authenticated.
**Response 404:** Resume not found or not owned by the current user. A non-owned resume returns `404`, not `403`, to avoid revealing that the resume exists. This matches the existing resume service behavior.

### Matching Logic

1. Load the resume with its candidate.
2. Return `404` if the resume does not exist or `resume.Candidate.UserId != userId`.
3. Load the resume's `ResumeSkills` skill IDs.
4. If there are no skill IDs, return an empty page.
5. Query `JobPostings` where:
   - `Status == Published`
   - `ExpiryDate is null OR ExpiryDate > UtcNow`
   - `Company.Status == Approved`
   - `JobSkills` contains at least one resume skill ID
6. Apply `city` and `jobType` filters.
7. Project `MatchedSkillCount` and the stored `MatchPercentage`.
8. Filter `MatchedSkillCount >= minSkillMatches`.
9. Count the filtered rows before paging.
10. Sort by:
    - `MatchedSkillCount` descending
    - `MatchPercentage` descending, null treated as 0
    - `CreatedAt` descending
    - `Id` descending
11. Page the rows.
12. Load matched skills for the page in one query.
13. Group matched skills by job and order each group by skill name.
14. Map to `SkillMatchJobListItemDto`.

### DTOs

```csharp
public sealed record SkillMatchJobListItemDto(
    int Id,
    string Title,
    string CompanyName,
    string? Location,
    decimal? SalaryMin,
    decimal? SalaryMax,
    int ExperienceRequired,
    JobType JobType,
    DateTime? ExpiryDate,
    int MatchedSkillCount,
    IReadOnlyList<MatchedSkillDto> MatchedSkills,
    double? MatchPercentage,
    DateTime CreatedAt,
    DateTime? FeaturedUntil);

public sealed record MatchedSkillDto(int SkillId, string SkillName);
```

### Service Interface

Extend `IJobService`:

```csharp
Task<ServiceResult<PagedResponse<SkillMatchJobListItemDto>>> GetJobsMatchingResumeAsync(
    string userId,
    int resumeId,
    string? city,
    JobType? jobType,
    int minSkillMatches,
    int page,
    int pageSize,
    CancellationToken cancellationToken = default);
```

### Implementation Notes

**Ownership check:**

```csharp
var resume = await dbContext.Resumes
    .AsNoTracking()
    .FirstOrDefaultAsync(r => r.Id == resumeId, cancellationToken);

if (resume is null || resume.CandidateId == 0)
    return ServiceResult<PagedResponse<SkillMatchJobListItemDto>>.Fail(
        ServiceErrorType.NotFound,
        "Resume not found.");

var ownerUserId = await dbContext.Candidates
    .Where(candidate => candidate.Id == resume.CandidateId)
    .Select(candidate => candidate.UserId)
    .FirstOrDefaultAsync(cancellationToken);

if (ownerUserId != userId)
    return ServiceResult<PagedResponse<SkillMatchJobListItemDto>>.Fail(
        ServiceErrorType.NotFound,
        "Resume not found.");
```

**Bounds:**

```csharp
page = Math.Max(page, 1);
pageSize = Math.Clamp(pageSize, 1, 100);
minSkillMatches = Math.Clamp(minSkillMatches, 1, 50);
```

**Base query and projection:** use explicit scalar fields. Do not project an entity or use `Include` with a projection.

```csharp
var now = DateTime.UtcNow;
var resumeSkillIds = await dbContext.ResumeSkills
    .AsNoTracking()
    .Where(resumeSkill => resumeSkill.ResumeId == resumeId)
    .Select(resumeSkill => resumeSkill.SkillId)
    .ToListAsync(cancellationToken);

if (resumeSkillIds.Count == 0)
{
    return ServiceResult<PagedResponse<SkillMatchJobListItemDto>>.Ok(
        new PagedResponse<SkillMatchJobListItemDto>
        {
            Items = Array.Empty<SkillMatchJobListItemDto>(),
            TotalCount = 0,
            Page = page,
            PageSize = pageSize
        });
}

var query = dbContext.JobPostings
    .AsNoTracking()
    .Where(job => job.Status == JobStatus.Published
        && (job.ExpiryDate == null || job.ExpiryDate > now)
        && job.Company.Status == CompanyStatus.Approved
        && job.JobSkills.Any(jobSkill => resumeSkillIds.Contains(jobSkill.SkillId)));

if (!string.IsNullOrWhiteSpace(city))
{
    var term = city.Trim();
    query = query.Where(job => job.Company.Location != null
        && EF.Functions.Like(job.Company.Location, $"%{term}%"));
}

if (jobType.HasValue)
{
    query = query.Where(job => job.JobType == jobType.Value);
}

var projected = query
    .Select(job => new
    {
        job.Id,
        job.Title,
        CompanyName = job.Company.CompanyName,
        Location = job.Company.Location,
        job.SalaryMin,
        job.SalaryMax,
        job.ExperienceRequired,
        job.JobType,
        job.ExpiryDate,
        MatchedSkillCount = job.JobSkills.Count(
            jobSkill => resumeSkillIds.Contains(jobSkill.SkillId)),
        MatchPercentage = job.MatchScores
            .Where(score => score.ResumeId == resumeId)
            .Select(score => (double?)score.MatchPercentage)
            .FirstOrDefault(),
        job.CreatedAt,
        job.FeaturedUntil
    })
    .Where(row => row.MatchedSkillCount >= minSkillMatches);

var total = await projected.CountAsync(cancellationToken);
var offset = (long)(page - 1) * pageSize;
if (offset >= total)
{
    return ServiceResult<PagedResponse<SkillMatchJobListItemDto>>.Ok(
        new PagedResponse<SkillMatchJobListItemDto> { TotalCount = total, Page = page, PageSize = pageSize });
}

var rows = await projected
    .OrderByDescending(row => row.MatchedSkillCount)
    .ThenByDescending(row => row.MatchPercentage ?? 0)
    .ThenByDescending(row => row.CreatedAt)
    .ThenByDescending(row => row.Id)
    .Skip((int)offset)
    .Take(pageSize)
    .ToListAsync(cancellationToken);
```

**Matched skills for the page:** one query, then group in memory.

```csharp
var jobIds = rows.Select(row => row.Id).ToList();

var matchedSkillRows = await dbContext.JobSkills
    .AsNoTracking()
    .Where(jobSkill => jobIds.Contains(jobSkill.JobPostingId)
        && resumeSkillIds.Contains(jobSkill.SkillId))
    .Select(jobSkill => new
    {
        jobSkill.JobPostingId,
        jobSkill.SkillId,
        SkillName = jobSkill.Skill.Name
    })
    .ToListAsync(cancellationToken);

var skillsByJob = matchedSkillRows
    .GroupBy(row => row.JobPostingId)
    .ToDictionary(
        group => group.Key,
        group => (IReadOnlyList<MatchedSkillDto>)group
            .OrderBy(row => row.SkillName)
            .Select(row => new MatchedSkillDto(row.SkillId, row.SkillName))
            .ToList());

var items = rows
    .Select(row => new SkillMatchJobListItemDto(
        row.Id,
        row.Title,
        row.CompanyName,
        row.Location,
        row.SalaryMin,
        row.SalaryMax,
        row.ExperienceRequired,
        row.JobType,
        row.ExpiryDate,
        row.MatchedSkillCount,
        skillsByJob.GetValueOrDefault(row.Id) ?? Array.Empty<MatchedSkillDto>(),
        row.MatchPercentage,
        row.CreatedAt,
        row.FeaturedUntil))
    .ToList();
```

Do not load `MatchScore` rows into a dictionary before sorting. The score must be part of the SQL query so ordering and pagination are correct.

## Authorization

### Admin Role Check

Use the existing controller pattern:

```csharp
[ApiController]
[ApiValidationEnvelope]
[Route("api/admin/skills")]
[Authorize(Roles = "Admin", AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class AdminSkillsController(IAdminService adminService) : ApiControllerBase
```

No custom policy is needed. JWT access tokens already include role claims.

### Candidate Resume Ownership

Return `404 NotFound` for a missing resume and for a resume owned by another user. Do not return `403`. This matches `ResumeService` and avoids leaking resume existence.

## Validation Rules

### Skill Name
- Required.
- Trimmed before validation and save.
- 1-100 characters after trimming.
- Case-insensitive uniqueness.
- No leading or trailing whitespace preserved.

### Skill Category
- Optional.
- Trimmed before save.
- Empty or whitespace becomes `null`.
- Maximum 100 characters.
- Freeform text; no enum.

### Pagination
- Admin: `page >= 1`; `pageSize` clamped to 1-200.
- Candidate search: `page >= 1`; `pageSize` clamped to 1-100.
- `minSkillMatches` clamped to 1-50.
- Calculate offsets as `long`; return an empty page when the offset is outside the counted results before casting to `int` for `Skip`.

Validation runs in the service layer. DTO annotations also apply through the API pipeline, but service validation is the source of truth.

## Error Handling

### Conflict (409)
- Skill name already exists.
- Skill is in use and cannot be deleted.
- Unique-index or restricted-FK race.

### Not Found (404)
- Skill ID does not exist.
- Resume ID does not exist.
- Resume is owned by another user.

### Unauthorized (401)
- Not authenticated.

### Forbidden (403)
- Not admin, for admin endpoints.

### Validation (400)
- Empty or whitespace-only name.
- Name longer than 100 characters.
- Category longer than 100 characters.
- Invalid request body or query binding.

## Database Changes

No new columns and no new tables.

One migration changes the delete behavior of these foreign keys from `Cascade` to `Restrict`:

- `ResumeSkills.SkillId -> Skills.Id`
- `JobSkills.SkillId -> Skills.Id`

Reason: an admin delete must never silently remove skill links from resumes or jobs because of a race between the count check and the delete. With `Restrict`, the database rejects the delete, and the service maps the failure to `409 Conflict`.

Existing schema details:

- `Skills.Id` is the primary key.
- `Skills.Name` is `varchar(100)` with a unique index.
- `Skills.Category` is `varchar(100)`, nullable.
- `ResumeSkills` and `JobSkills` already reference `Skill.Id`.
- `MatchScores` has a composite key `(JobPostingId, ResumeId)`.

## Testing Strategy

Tests use the existing SQLite `TestFixture`. Controller action mappings use stub services. `SkillApiValidationTests` hosts a small real MVC/Kestrel pipeline on a dynamic loopback port to verify automatic validation and normalized input without adding dependencies or running application seeding.

### AdminService Tests

File: `hotjob/Tests/hotjob.Tests/AdminTests/AdminSkillServiceTests.cs`

- `CreateSkillAsync_ValidRequest_TrimsAndReturnsSkill`
- `CreateSkillAsync_DuplicateName_ReturnsConflict`
- `CreateSkillAsync_CaseInsensitiveDuplicate_ReturnsConflict`
- `CreateSkillAsync_WhitespaceName_ReturnsValidation`
- `CreateSkillAsync_NameTooLong_ReturnsValidation`
- `UpdateSkillAsync_Rename_KeepsResumeAndJobLinks`
- `UpdateSkillAsync_DuplicateName_ReturnsConflict`
- `UpdateSkillAsync_SameNameDifferentCase_AllowsUpdate`
- `DeleteSkillAsync_UnusedSkill_Deletes`
- `DeleteSkillAsync_SkillInUse_ReturnsConflict`
- `GetSkillsAsync_FiltersAndOrdersByName`
- `GetSkillsAsync_ClampsPageAndPageSize`
- `GetSkillAsync_Missing_ReturnsNotFound`
- `GetSkillsAsync_LargePage_ReturnsEmptyWithoutOverflow`
- `SkillWrite_UnrelatedSaveFailure_Propagates`
- `SkillWrite_RacingDuplicate_ReturnsConflict`
- `DeleteSkillAsync_ReferenceInsertedAfterCounts_ReturnsConflict`

### JobService Tests

File: `hotjob/Tests/hotjob.Tests/JobTests/JobSkillMatchTests.cs`

- `GetJobsMatchingResumeAsync_ReturnsMatchingJobsWithSkills`
- `GetJobsMatchingResumeAsync_ResumeWithNoSkills_ReturnsEmpty`
- `GetJobsMatchingResumeAsync_MinSkillMatchesFilters`
- `GetJobsMatchingResumeAsync_SortsByMatchedCountThenScore`
- `GetJobsMatchingResumeAsync_OtherUserResume_ReturnsNotFound`
- `GetJobsMatchingResumeAsync_MissingResume_ReturnsNotFound`
- `GetJobsMatchingResumeAsync_ExpiredJobExcluded`
- `GetJobsMatchingResumeAsync_PendingCompanyExcluded`
- `GetJobsMatchingResumeAsync_NoMatchScore_ReturnsNullPercentage`
- `GetJobsMatchingResumeAsync_CityAndJobTypeFilters`
- `GetJobsMatchingResumeAsync_ClampsPageAndMinMatches`
- `GetJobsMatchingResumeAsync_LargePage_ReturnsEmptyWithoutOverflow`

### Controller Tests

Files:
- `hotjob/Tests/hotjob.Tests/AdminTests/AdminSkillsControllerTests.cs`
- `hotjob/Tests/hotjob.Tests/JobTests/JobSearchControllerTests.cs`
- `hotjob/Tests/hotjob.Tests/AdminTests/SkillApiValidationTests.cs`

Controller tests cover:
- List success returns 200 with the `ApiResponse` envelope.
- Create success returns 201.
- Delete success returns 204.
- Delete conflict returns 409.
- Missing skill or resume returns 404.
- Missing authenticated user returns 401.
- Automatic validation returns enveloped 400 responses for malformed JSON, missing/whitespace names, and malformed query values.
- Both POST and PUT accept normalized boundary-length input and reject genuinely overlong names/categories.
- Unrelated controllers retain their existing automatic-validation response shape.

## File Changes Summary

**New files:**
- `hotjob/Controllers/AdminSkillsController.cs`
- `hotjob/Controllers/JobSearchController.cs`
- `hotjob/Controllers/ApiValidationEnvelopeAttribute.cs`
- `hotjob/DTOs/Admin/SkillDtos.cs`
- `hotjob/DTOs/Job/SkillMatchDtos.cs`
- `hotjob/Tests/hotjob.Tests/AdminTests/AdminSkillServiceTests.cs`
- `hotjob/Tests/hotjob.Tests/AdminTests/AdminSkillsControllerTests.cs`
- `hotjob/Tests/hotjob.Tests/AdminTests/SkillApiValidationTests.cs`
- `hotjob/Tests/hotjob.Tests/JobTests/JobSkillMatchTests.cs`
- `hotjob/Tests/hotjob.Tests/JobTests/JobSearchControllerTests.cs`
- One EF Core migration for the FK delete behavior change.

**Modified files:**
- `hotjob/Data/ApplicationDbContext.cs` (two `OnDelete` calls: `Cascade` to `Restrict`)
- `hotjob/Services/Admin/IAdminService.cs` (add 5 skill methods)
- `hotjob/Services/Admin/AdminService.cs` (implement skill CRUD)
- `hotjob/Services/Job/IJobService.cs` (add `GetJobsMatchingResumeAsync`)
- `hotjob/Services/Job/JobService.cs` (implement candidate search)

**Not modified:**
- `hotjob/Program.cs`
- Existing Blazor pages
- Existing public job browse behavior

## Security Considerations

1. **Admin authorization:** Enforce on the controller with the existing JWT role attribute.
2. **Resume ownership:** Verify the resume belongs to the current user before returning any data. Use `404` for non-owned resumes.
3. **SQL injection:** Use EF Core parameterization and `EF.Functions.Like`.
4. **Delete safety:** Use `ON DELETE RESTRICT` plus a service conflict response. Never rely on a count check alone.
5. **Input sanitization:** Trim and validate all string inputs in the service.
6. **Rate limiting:** Out of scope; no rate limiter exists in the project.

## Performance Considerations

1. **Skill list:** project `ResumeSkills.Count` and `JobSkills.Count`; do not `Include` join rows.
2. **Skill list pagination:** default 50, maximum 200; order by `Name`, then `Id`.
3. **Candidate search:** filter published jobs, expiry, approved companies, and overlapping skills in SQL.
4. **Matched skill count:** computed in SQL; do not load all job skills for every candidate job.
5. **Match score:** correlated subquery on the composite key; no N+1 query.
6. **Matched skills:** one additional query for the current page only.
7. **Indexes:** `Skills.Name` is unique; `JobSkills` and `ResumeSkills` have indexes on `SkillId`; `MatchScores` has `(JobPostingId, ResumeId)` as its key.

## Future Enhancements (Out of Scope)

- Blazor admin and candidate pages.
- Skill synonyms/aliases.
- Skill proficiency levels.
- Skill endorsements.
- Auto-suggest skills from resume file parsing.
- Skill trending/popularity stats.
- Batch skill import (CSV/JSON).
- Skill hierarchy.

## Success Metrics

- Admins can manage the skill catalog through the API without database access.
- Candidates can find matching published jobs through the API.
- Duplicate skill names are rejected case-insensitively.
- Deleting a referenced skill returns `409`; no resume or job skill links are removed by the delete.
- Renaming a skill keeps every resume and job link valid because links reference `Skill.Id`.

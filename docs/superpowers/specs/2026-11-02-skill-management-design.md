# Skill Management Feature Specification

## Overview

Add two capabilities:
1. **Admin Skills CRUD** - Admin endpoints to create, update, delete, and list skills
2. **Candidate Skill-Based Job Search** - Find jobs matching a resume's skills

## Goals

- Admin can manage the Skills catalog (create, rename, delete, list)
- Candidates can search jobs that match their resume skills
- Maintain data integrity (prevent orphaned references, duplicate skills)
- Follow existing codebase patterns (REST API, service layer, DTOs)

## Non-Goals

- Skill recommendation engine
- Skill taxonomy/hierarchy
- Bulk import/export
- Skill versioning

## Technical Approach

- New `AdminController` at `/api/admin/skills/*` for Skills CRUD
- New endpoint on existing job search: `/api/jobs/matching-resume/{resumeId}`
- Extend `IAdminService` and `IJobService` interfaces
- Reuse existing `Skill` entity, add DTOs for requests/responses
- Authorization: admin endpoints require admin role; candidate search requires authentication

## Admin Skills CRUD

### Endpoints

#### 1. List All Skills

**GET** `/api/admin/skills`

**Auth:** Admin role required

**Query params:**
- `search` (optional): filter by Name (case-insensitive LIKE)
- `category` (optional): filter by exact Category
- `page` (default: 1)
- `pageSize` (default: 50, max: 200)

**Response 200:**
```json
{
  "items": [
    {
      "id": 1,
      "name": "C#",
      "category": "Backend",
      "resumeCount": 45,
      "jobCount": 23,
      "createdAt": "2026-01-15T10:00:00Z"
    }
  ],
  "totalCount": 120,
  "page": 1,
  "pageSize": 50
}
```

**Response 401:** Not authenticated  
**Response 403:** Not admin

#### 2. Get Single Skill

**GET** `/api/admin/skills/{id}`

**Auth:** Admin role required

**Response 200:**
```json
{
  "id": 1,
  "name": "C#",
  "category": "Backend",
  "resumeCount": 45,
  "jobCount": 23,
  "createdAt": "2026-01-15T10:00:00Z"
}
```

**Response 401:** Not authenticated  
**Response 403:** Not admin  
**Response 404:** Skill not found

#### 3. Create Skill

**POST** `/api/admin/skills`

**Auth:** Admin role required

**Request body:**
```json
{
  "name": "Rust",
  "category": "Backend"
}
```

**Validation:**
- `name`: required, 1-100 chars, trimmed
- `category`: optional, max 100 chars, trimmed
- Case-insensitive duplicate check: reject if `Name.ToLower()` exists

**Response 201:**
```json
{
  "id": 125,
  "name": "Rust",
  "category": "Backend",
  "resumeCount": 0,
  "jobCount": 0,
  "createdAt": "2026-11-02T07:30:00Z"
}
```

**Response 400:** Validation errors  
**Response 401:** Not authenticated  
**Response 403:** Not admin  
**Response 409:** Skill name already exists (case-insensitive)

#### 4. Update Skill

**PUT** `/api/admin/skills/{id}`

**Auth:** Admin role required

**Request body:**
```json
{
  "name": "C# / .NET",
  "category": "Backend"
}
```

**Validation:**
- Same as Create
- If name changed: case-insensitive duplicate check excluding current skill
- Name change updates all `ResumeSkill` and `JobSkill` references (via navigation property)

**Response 200:**
```json
{
  "id": 1,
  "name": "C# / .NET",
  "category": "Backend",
  "resumeCount": 45,
  "jobCount": 23,
  "createdAt": "2026-01-15T10:00:00Z"
}
```

**Response 400:** Validation errors  
**Response 401:** Not authenticated  
**Response 403:** Not admin  
**Response 404:** Skill not found  
**Response 409:** New name conflicts with existing skill

#### 5. Delete Skill

**DELETE** `/api/admin/skills/{id}`

**Auth:** Admin role required

**Behavior:**
- Check if skill is referenced in `ResumeSkills` or `JobSkills`
- If referenced: return 409 Conflict with usage counts
- If not referenced: hard delete

**Response 204:** Deleted successfully

**Response 401:** Not authenticated  
**Response 403:** Not admin  
**Response 404:** Skill not found  
**Response 409:** Skill is in use
```json
{
  "errors": ["Cannot delete skill 'C#': used in 45 resumes and 23 job postings."]
}
```

### DTOs

**AdminSkillListItemDto:**
```csharp
public sealed record AdminSkillListItemDto(
    int Id,
    string Name,
    string? Category,
    int ResumeCount,
    int JobCount,
    DateTime CreatedAt);
```

**AdminSkillDetailDto:**
```csharp
public sealed record AdminSkillDetailDto(
    int Id,
    string Name,
    string? Category,
    int ResumeCount,
    int JobCount,
    DateTime CreatedAt);
```

**CreateSkillRequest:**
```csharp
public sealed class CreateSkillRequest
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Category { get; set; }
}
```

**UpdateSkillRequest:**
```csharp
public sealed class UpdateSkillRequest
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Category { get; set; }
}
```

### Service Interface

Extend `IAdminService`:

```csharp
Task<ServiceResult<PagedResponse<AdminSkillListItemDto>>> GetSkillsAsync(
    string? search,
    string? category,
    int page,
    int pageSize,
    CancellationToken cancellationToken = default);

Task<ServiceResult<AdminSkillDetailDto>> GetSkillAsync(
    int skillId,
    CancellationToken cancellationToken = default);

Task<ServiceResult<AdminSkillDetailDto>> CreateSkillAsync(
    CreateSkillRequest request,
    CancellationToken cancellationToken = default);

Task<ServiceResult<AdminSkillDetailDto>> UpdateSkillAsync(
    int skillId,
    UpdateSkillRequest request,
    CancellationToken cancellationToken = default);

Task<ServiceResult> DeleteSkillAsync(
    int skillId,
    CancellationToken cancellationToken = default);
```

### Implementation Notes

**Duplicate check:**
```csharp
var normalized = request.Name.Trim().ToLowerInvariant();
var exists = await dbContext.Skills
    .AnyAsync(s => s.Name.ToLower() == normalized, cancellationToken);
```

**Usage counts:**
```csharp
var skill = await dbContext.Skills
    .Include(s => s.ResumeSkills)
    .Include(s => s.JobSkills)
    .FirstOrDefaultAsync(s => s.Id == skillId, cancellationToken);

var resumeCount = skill.ResumeSkills.Count;
var jobCount = skill.JobSkills.Count;
```

**Delete validation:**
```csharp
if (resumeCount > 0 || jobCount > 0)
{
    return ServiceResult.Fail(
        ServiceErrorType.Conflict,
        $"Cannot delete skill '{skill.Name}': used in {resumeCount} resumes and {jobCount} job postings.");
}
```

## Candidate Skill-Based Job Search

### Endpoint

**GET** `/api/jobs/matching-resume/{resumeId}`

**Auth:** Authenticated candidate, must own the resume

**Path params:**
- `resumeId`: int (required)

**Query params:**
- `city` (optional): filter by company location
- `jobType` (optional): FullTime, PartTime, Contract, Internship
- `minSkillMatches` (optional, default: 1): minimum overlapping skills
- `page` (default: 1)
- `pageSize` (default: 20, max: 100)

**Response 200:**
```json
{
  "items": [
    {
      "id": 42,
      "title": "Senior .NET Developer",
      "companyName": "Acme Corp",
      "location": "Hanoi",
      "salaryMin": 2000,
      "salaryMax": 3500,
      "experienceRequired": 5,
      "jobType": "FullTime",
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
  "pageSize": 20
}
```

**Response 401:** Not authenticated  
**Response 403:** Resume does not belong to current user  
**Response 404:** Resume not found

### Matching Logic

1. Load resume's `ResumeSkills` (skill IDs)
2. Query `JobPostings` where:
   - Status = Published
   - ExpiryDate is null OR > UtcNow
   - Company.Status = Approved
   - JobSkills has ANY skill ID in resume's skill IDs
3. For each job, count matched skills: `job.JobSkills.Count(js => resumeSkillIds.Contains(js.SkillId))`
4. Filter: `matchedSkillCount >= minSkillMatches`
5. Load existing `MatchScore` for (resumeId, jobId) pairs if available
6. Sort by:
   - matchedSkillCount DESC
   - matchPercentage DESC (if MatchScore exists)
   - createdAt DESC
7. Paginate

### DTOs

**SkillMatchJobListItemDto:**
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

**Authorization check:**
```csharp
var resume = await dbContext.Resumes
    .Include(r => r.Candidate)
    .FirstOrDefaultAsync(r => r.Id == resumeId, cancellationToken);

if (resume is null)
    return ServiceResult<...>.Fail(ServiceErrorType.NotFound, "Resume not found.");

if (resume.Candidate.UserId != userId)
    return ServiceResult<...>.Fail(ServiceErrorType.Unauthorized, "Resume does not belong to you.");
```

**Load resume skill IDs:**
```csharp
var resumeSkillIds = await dbContext.ResumeSkills
    .Where(rs => rs.ResumeId == resumeId)
    .Select(rs => rs.SkillId)
    .ToListAsync(cancellationToken);

if (resumeSkillIds.Count == 0)
    return ServiceResult<...>.Success(new PagedResponse<...>(Array.Empty<...>(), 0, page, pageSize));
```

**Query matching jobs:**
```csharp
var now = DateTime.UtcNow;
var query = dbContext.JobPostings
    .AsNoTracking()
    .Include(j => j.Company)
    .Include(j => j.JobSkills).ThenInclude(js => js.Skill)
    .Where(j => j.Status == JobStatus.Published
        && (j.ExpiryDate == null || j.ExpiryDate > now)
        && j.Company.Status == CompanyStatus.Approved
        && j.JobSkills.Any(js => resumeSkillIds.Contains(js.SkillId)));

if (!string.IsNullOrWhiteSpace(city))
{
    var term = city.Trim();
    query = query.Where(j => j.Company.Location != null 
        && EF.Functions.Like(j.Company.Location, $"%{term}%"));
}

if (jobType.HasValue)
    query = query.Where(j => j.JobType == jobType.Value);
```

**Project with matched skill count:**
```csharp
var projected = query.Select(j => new
{
    Job = j,
    MatchedSkillCount = j.JobSkills.Count(js => resumeSkillIds.Contains(js.SkillId)),
    MatchedSkills = j.JobSkills
        .Where(js => resumeSkillIds.Contains(js.SkillId))
        .Select(js => new { js.SkillId, js.Skill.Name })
        .ToList()
})
.Where(x => x.MatchedSkillCount >= minSkillMatches);
```

**Load MatchScores:**
```csharp
var jobIds = items.Select(x => x.Job.Id).ToList();
var matchScores = await dbContext.MatchScores
    .Where(ms => ms.ResumeId == resumeId && jobIds.Contains(ms.JobPostingId))
    .ToDictionaryAsync(ms => ms.JobPostingId, ms => ms.MatchPercentage, cancellationToken);
```

**Sort and paginate:**
```csharp
var sorted = projected
    .OrderByDescending(x => x.MatchedSkillCount)
    .ThenByDescending(x => matchScores.ContainsKey(x.Job.Id) ? matchScores[x.Job.Id] : 0)
    .ThenByDescending(x => x.Job.CreatedAt);

var total = await sorted.CountAsync(cancellationToken);
var items = await sorted
    .Skip((page - 1) * pageSize)
    .Take(pageSize)
    .ToListAsync(cancellationToken);
```

## Authorization

### Admin Role Check

Create `[Authorize(Roles = "Admin")]` attribute or custom policy.

Implementation options:
1. **Role claim:** Check `User.IsInRole("Admin")`
2. **Custom claim:** Check `User.HasClaim("IsAdmin", "true")`
3. **Policy:** Define policy in `Program.cs`:

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole("Admin"));
});
```

Apply to controller:
```csharp
[Authorize(Policy = "AdminOnly")]
[ApiController]
[Route("api/admin/skills")]
public class AdminController : ControllerBase
```

### Candidate Resume Ownership

Check in service layer:
```csharp
var resume = await dbContext.Resumes
    .Include(r => r.Candidate)
    .FirstOrDefaultAsync(r => r.Id == resumeId, cancellationToken);

if (resume?.Candidate.UserId != userId)
    return Fail(Unauthorized, "Resume not found or access denied.");
```

## Validation Rules

### Skill Name
- Required
- 1-100 characters
- Trimmed before save
- Case-insensitive uniqueness (compare `ToLower()`)
- No leading/trailing whitespace preserved

### Skill Category
- Optional
- Max 100 characters
- Trimmed before save
- Freeform text (no enum/validation)

### minSkillMatches
- Default: 1
- Range: 1-50
- If resume has fewer skills than minSkillMatches, return empty result

## Error Handling

### Conflict (409)
- Skill name already exists
- Skill in use (cannot delete)

### Not Found (404)
- Skill ID doesn't exist
- Resume ID doesn't exist

### Unauthorized (401)
- Not authenticated

### Forbidden (403)
- Not admin (for admin endpoints)
- Resume doesn't belong to user (for candidate search)

### Validation (400)
- Empty/invalid name
- Name too long
- Category too long
- Invalid minSkillMatches range

## Database Changes

**None required.** `Skills` table already has:
- `Id` (PK, identity)
- `Name` (nvarchar(100), unique index)
- `Category` (nvarchar(100), nullable)

Navigation properties exist:
- `Skill.ResumeSkills`
- `Skill.JobSkills`

Schema migration NOT needed.

## Testing Strategy

### Unit Tests

**AdminService Tests (`AdminServiceTests.cs`):**
- `CreateSkillAsync_ValidRequest_ReturnsSkill`
- `CreateSkillAsync_DuplicateName_ReturnsConflict`
- `CreateSkillAsync_CaseInsensitiveDuplicate_ReturnsConflict`
- `UpdateSkillAsync_ValidRequest_ReturnsUpdatedSkill`
- `UpdateSkillAsync_DuplicateName_ReturnsConflict`
- `DeleteSkillAsync_UnusedSkill_ReturnsSuccess`
- `DeleteSkillAsync_SkillInUse_ReturnsConflict`
- `GetSkillsAsync_WithFilters_ReturnsPaginated`

**JobService Tests (extend `JobServiceTests.cs` or new file):**
- `GetJobsMatchingResumeAsync_ResumeWithSkills_ReturnsMatchingJobs`
- `GetJobsMatchingResumeAsync_NoMatchingJobs_ReturnsEmpty`
- `GetJobsMatchingResumeAsync_MinSkillMatchesFilter_FiltersCorrectly`
- `GetJobsMatchingResumeAsync_SortsByMatchCount_Correct`
- `GetJobsMatchingResumeAsync_OtherUserResume_ReturnsUnauthorized`
- `GetJobsMatchingResumeAsync_CityFilter_FiltersCorrectly`

### Integration Tests

- Admin creates skill → appears in list
- Admin updates skill → ResumeSkills/JobSkills still linked
- Admin deletes unused skill → removed from DB
- Admin tries delete used skill → 409
- Candidate searches with resume → returns jobs with overlapping skills
- Candidate searches other's resume → 403

## File Changes Summary

**New files:**
- `hotjob/Controllers/AdminController.cs` (admin skills CRUD)
- `hotjob/DTOs/Admin/SkillDtos.cs` (skill request/response DTOs)
- `hotjob/DTOs/Job/SkillMatchDtos.cs` (candidate search DTOs)
- `hotjob/Tests/hotjob.Tests/AdminTests/AdminSkillServiceTests.cs`
- `hotjob/Tests/hotjob.Tests/JobTests/JobSkillMatchTests.cs`

**Modified files:**
- `hotjob/Services/Admin/IAdminService.cs` (add 5 skill methods)
- `hotjob/Services/Admin/AdminService.cs` (implement skill CRUD)
- `hotjob/Services/Job/IJobService.cs` (add GetJobsMatchingResumeAsync)
- `hotjob/Services/Job/JobService.cs` (implement candidate search)
- `hotjob/Controllers/EmployerJobsController.cs` OR new `hotjob/Controllers/JobSearchController.cs` (add candidate search endpoint)
- `hotjob/Program.cs` (register admin policy if needed)

## Security Considerations

1. **Admin authorization:** Enforce on every admin endpoint, fail closed
2. **Resume ownership:** Always verify userId owns resumeId before returning data
3. **SQL injection:** Use parameterized queries (EF Core handles this)
4. **Rate limiting:** Consider rate limit on candidate search (prevent abuse)
5. **Input sanitization:** Trim and validate all string inputs

## Performance Considerations

1. **Skill list pagination:** Default pageSize=50, max=200
2. **Candidate search:** Index on `JobSkills.SkillId` exists (FK), query efficient
3. **Usage counts:** Use `Include()` + `.Count` on collections (single query)
4. **MatchScore lookup:** Dictionary lookup after batch load (avoid N+1)
5. **Resume skill IDs:** Single query, in-memory list for `Contains()` filter

## Future Enhancements (Out of Scope)

- Skill synonyms/aliases (e.g., "JavaScript" = "JS")
- Skill proficiency levels
- Skill endorsements
- Auto-suggest skills from resume file parsing
- Skill trending/popularity stats
- Batch skill import (CSV/JSON)
- Skill hierarchy (parent/child relationships)

## Success Metrics

- Admins can manage skills without database access
- Candidates find relevant jobs faster via skill-based search
- Skill deduplication prevents catalog bloat
- Zero orphaned skill references after renames

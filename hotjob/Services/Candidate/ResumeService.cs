using hotjob.Data;
using hotjob.DTOs.Candidate;
using hotjob.Models.Resume;
using hotjob.Services.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace hotjob.Services.Candidate;

/// <summary>
/// Candidate-scoped resume writes. Every lookup filters by the signed-in
/// candidate's profile id, so passing another candidate's resume id is a
/// not-found rather than an edit.
/// </summary>
public sealed class ResumeService(
    ApplicationDbContext dbContext,
    ICvFileStorage fileStorage,
    TimeProvider timeProvider,
    ILogger<ResumeService> logger) : IResumeService
{
    /// <summary>Maximum number of resumes one candidate may keep.</summary>
    public const int MaxResumes = 5;

    public async Task<ServiceResult<IReadOnlyList<ResumeListItemDto>>> ListAsync(
        string? userId,
        CancellationToken cancellationToken = default)
    {
        var candidate = await ResolveCandidateAsync(userId, cancellationToken);
        if (!candidate.Success)
        {
            return ServiceResult<IReadOnlyList<ResumeListItemDto>>.Fail(
                candidate.ErrorType!.Value, candidate.Errors);
        }

        var resumes = await dbContext.Resumes
            .AsNoTracking()
            .Where(resume => resume.CandidateId == candidate.Data)
            .OrderByDescending(resume => resume.IsPrimary)
            .ThenByDescending(resume => resume.CreatedAt)
            .ThenByDescending(resume => resume.Id)
            .Select(resume => new ResumeListItemDto(
                resume.Id,
                resume.Title,
                resume.Summary,
                resume.FileUrl,
                resume.IsPrimary,
                resume.CreatedAt,
                resume.ResumeSkills.Count,
                resume.Educations.Count,
                resume.WorkExperiences.Count))
            .ToListAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<ResumeListItemDto>>.Ok(resumes);
    }

    public async Task<ServiceResult<ResumeDetailDto>> GetAsync(
        string? userId,
        int resumeId,
        CancellationToken cancellationToken = default)
    {
        var candidate = await ResolveCandidateAsync(userId, cancellationToken);
        if (!candidate.Success)
            return ServiceResult<ResumeDetailDto>.Fail(candidate.ErrorType!.Value, candidate.Errors);

        var detail = await LoadDetailAsync(candidate.Data, resumeId, cancellationToken);
        return detail is null
            ? ServiceResult<ResumeDetailDto>.Fail(ServiceErrorType.NotFound, "Resume not found.")
            : ServiceResult<ResumeDetailDto>.Ok(detail);
    }

    public async Task<ServiceResult<ResumeDetailDto>> CreateAsync(
        string? userId,
        ResumeInput input,
        CancellationToken cancellationToken = default)
    {
        var candidate = await ResolveCandidateAsync(userId, cancellationToken);
        if (!candidate.Success)
            return ServiceResult<ResumeDetailDto>.Fail(candidate.ErrorType!.Value, candidate.Errors);

        var errors = await ValidateInputAsync(input, cancellationToken);
        if (errors.Count > 0)
            return ServiceResult<ResumeDetailDto>.Fail(ServiceErrorType.Validation, errors);

        var count = await dbContext.Resumes.CountAsync(
            resume => resume.CandidateId == candidate.Data, cancellationToken);
        if (count >= MaxResumes)
        {
            return ServiceResult<ResumeDetailDto>.Fail(
                ServiceErrorType.Conflict,
                $"You can keep up to {MaxResumes} resumes. Delete one before adding another.");
        }

        var hasPrimary = await dbContext.Resumes
            .AnyAsync(resume => resume.CandidateId == candidate.Data && resume.IsPrimary, cancellationToken);

        var resume = new Resume
        {
            CandidateId = candidate.Data,
            Title = input.Title.Trim(),
            Summary = Normalize(input.Summary),
            IsPrimary = !hasPrimary,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime
        };
        ApplyChildren(resume, input);

        dbContext.Resumes.Add(resume);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Candidate {CandidateId} created resume {ResumeId}.", candidate.Data, resume.Id);

        var detail = await LoadDetailAsync(candidate.Data, resume.Id, cancellationToken);
        return ServiceResult<ResumeDetailDto>.Ok(detail!);
    }

    public async Task<ServiceResult<ResumeDetailDto>> UpdateAsync(
        string? userId,
        int resumeId,
        ResumeInput input,
        CancellationToken cancellationToken = default)
    {
        var candidate = await ResolveCandidateAsync(userId, cancellationToken);
        if (!candidate.Success)
            return ServiceResult<ResumeDetailDto>.Fail(candidate.ErrorType!.Value, candidate.Errors);

        var errors = await ValidateInputAsync(input, cancellationToken);
        if (errors.Count > 0)
            return ServiceResult<ResumeDetailDto>.Fail(ServiceErrorType.Validation, errors);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var resume = await dbContext.Resumes
                .Include(row => row.Educations)
                .Include(row => row.WorkExperiences)
                .Include(row => row.ResumeSkills)
                .FirstOrDefaultAsync(
                    row => row.Id == resumeId && row.CandidateId == candidate.Data,
                    cancellationToken);
            if (resume is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return ServiceResult<ResumeDetailDto>.Fail(ServiceErrorType.NotFound, "Resume not found.");
            }

            // ponytail: clear children first so re-adding a skill with the same composite key stays trackable.
            var skillsChanged = SkillsChanged(resume.ResumeSkills, input.Skills);
            dbContext.Educations.RemoveRange(resume.Educations);
            dbContext.WorkExperiences.RemoveRange(resume.WorkExperiences);
            dbContext.ResumeSkills.RemoveRange(resume.ResumeSkills);
            await dbContext.SaveChangesAsync(cancellationToken);

            resume.Title = input.Title.Trim();
            resume.Summary = Normalize(input.Summary);
            ApplyChildren(resume, input);
            if (skillsChanged)
            {
                // ponytail: old scores are invalid; the next job view recomputes and upserts them.
                await dbContext.MatchScores
                    .Where(score => score.ResumeId == resumeId)
                    .ExecuteDeleteAsync(cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogError(exception, "Update resume {ResumeId} failed.", resumeId);
            throw;
        }

        var detail = await LoadDetailAsync(candidate.Data, resumeId, cancellationToken);
        return ServiceResult<ResumeDetailDto>.Ok(detail!);
    }

    public async Task<ServiceResult> DeleteAsync(
        string? userId,
        int resumeId,
        CancellationToken cancellationToken = default)
    {
        var candidate = await ResolveCandidateAsync(userId, cancellationToken);
        if (!candidate.Success)
            return ServiceResult.Fail(candidate.ErrorType!.Value, candidate.Errors);

        string? storedFileUrl;
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                var resume = await dbContext.Resumes.FirstOrDefaultAsync(
                    row => row.Id == resumeId && row.CandidateId == candidate.Data,
                    cancellationToken);
                if (resume is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return ServiceResult.Fail(ServiceErrorType.NotFound, "Resume not found.");
                }

                var usedToApply = await dbContext.Applications
                    .AnyAsync(application => application.ResumeId == resumeId, cancellationToken);
                if (usedToApply)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return ServiceResult.Fail(
                        ServiceErrorType.Conflict,
                        "This resume was used to apply for a job, so it can no longer be deleted.");
                }

                storedFileUrl = resume.FileUrl;
                var wasPrimary = resume.IsPrimary;
                dbContext.Resumes.Remove(resume);
                await dbContext.SaveChangesAsync(cancellationToken);

                if (wasPrimary)
                {
                    var replacement = await dbContext.Resumes
                        .Where(row => row.CandidateId == candidate.Data)
                        .OrderByDescending(row => row.CreatedAt)
                        .ThenByDescending(row => row.Id)
                        .FirstOrDefaultAsync(cancellationToken);
                    if (replacement is not null)
                    {
                        replacement.IsPrimary = true;
                        await dbContext.SaveChangesAsync(cancellationToken);
                    }
                }

                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await transaction.RollbackAsync(cancellationToken);
                logger.LogError(exception, "Delete resume {ResumeId} failed.", resumeId);
                throw;
            }
        }

        await fileStorage.DeleteAsync(storedFileUrl, cancellationToken);
        logger.LogInformation("Candidate {CandidateId} deleted resume {ResumeId}.", candidate.Data, resumeId);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<ResumeDetailDto>> SetPrimaryAsync(
        string? userId,
        int resumeId,
        CancellationToken cancellationToken = default)
    {
        var candidate = await ResolveCandidateAsync(userId, cancellationToken);
        if (!candidate.Success)
            return ServiceResult<ResumeDetailDto>.Fail(candidate.ErrorType!.Value, candidate.Errors);

        var resumes = await dbContext.Resumes
            .Where(row => row.CandidateId == candidate.Data)
            .ToListAsync(cancellationToken);
        if (resumes.All(row => row.Id != resumeId))
            return ServiceResult<ResumeDetailDto>.Fail(ServiceErrorType.NotFound, "Resume not found.");

        foreach (var resume in resumes)
        {
            resume.IsPrimary = resume.Id == resumeId;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        var detail = await LoadDetailAsync(candidate.Data, resumeId, cancellationToken);
        return ServiceResult<ResumeDetailDto>.Ok(detail!);
    }

    public async Task<ServiceResult<ResumeDetailDto>> UploadFileAsync(
        string? userId,
        int resumeId,
        string? fileName,
        long length,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var candidate = await ResolveCandidateAsync(userId, cancellationToken);
        if (!candidate.Success)
            return ServiceResult<ResumeDetailDto>.Fail(candidate.ErrorType!.Value, candidate.Errors);

        var resume = await dbContext.Resumes.FirstOrDefaultAsync(
            row => row.Id == resumeId && row.CandidateId == candidate.Data,
            cancellationToken);
        if (resume is null)
            return ServiceResult<ResumeDetailDto>.Fail(ServiceErrorType.NotFound, "Resume not found.");

        var stored = await fileStorage.SaveAsync(candidate.Data, fileName, length, content, cancellationToken);
        if (!stored.Success)
            return ServiceResult<ResumeDetailDto>.Fail(stored.ErrorType!.Value, stored.Errors);

        var previousUrl = resume.FileUrl;
        resume.FileUrl = stored.Data;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await fileStorage.DeleteAsync(stored.Data, cancellationToken);
            throw;
        }

        if (!string.Equals(previousUrl, stored.Data, StringComparison.Ordinal))
        {
            // ponytail: an application snapshot may still point at the old file; keep it until no snapshot uses it.
            var referencedBySnapshot = previousUrl is not null
                && await dbContext.Applications.AnyAsync(
                    application => application.SnapshotFileUrl == previousUrl, cancellationToken);
            if (!referencedBySnapshot)
                await fileStorage.DeleteAsync(previousUrl, cancellationToken);
        }

        var detail = await LoadDetailAsync(candidate.Data, resumeId, cancellationToken);
        return ServiceResult<ResumeDetailDto>.Ok(detail!);
    }

    public async Task<ServiceResult<IReadOnlyList<SkillOptionDto>>> ListSkillsAsync(
        CancellationToken cancellationToken = default)
    {
        var skills = await dbContext.Skills
            .AsNoTracking()
            .OrderBy(skill => skill.Name)
            .Select(skill => new SkillOptionDto(skill.Id, skill.Name, skill.Category))
            .ToListAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<SkillOptionDto>>.Ok(skills);
    }

    private async Task<ServiceResult<int>> ResolveCandidateAsync(
        string? userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return ServiceResult<int>.Fail(ServiceErrorType.Unauthorized, "Not signed in.");

        var candidateId = await dbContext.Candidates
            .Where(candidate => candidate.UserId == userId)
            .Select(candidate => (int?)candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return candidateId is null
            ? ServiceResult<int>.Fail(ServiceErrorType.NotFound, "Candidate profile not found.")
            : ServiceResult<int>.Ok(candidateId.Value);
    }

    private async Task<ResumeDetailDto?> LoadDetailAsync(
        int candidateId,
        int resumeId,
        CancellationToken cancellationToken)
    {
        var resume = await dbContext.Resumes
            .AsNoTracking()
            .Include(row => row.Educations)
            .Include(row => row.WorkExperiences)
            .Include(row => row.ResumeSkills).ThenInclude(skill => skill.Skill)
            .FirstOrDefaultAsync(
                row => row.Id == resumeId && row.CandidateId == candidateId,
                cancellationToken);
        if (resume is null)
            return null;

        return new ResumeDetailDto
        {
            Id = resume.Id,
            Title = resume.Title,
            Summary = resume.Summary,
            FileUrl = resume.FileUrl,
            IsPrimary = resume.IsPrimary,
            CreatedAt = resume.CreatedAt,
            Educations = resume.Educations
                .OrderBy(entry => entry.StartDate)
                .ThenBy(entry => entry.Id)
                .Select(entry => new ResumeEducationItemDto(
                    entry.Id,
                    entry.SchoolName,
                    entry.Major,
                    entry.StartDate,
                    entry.EndDate,
                    entry.Gpa,
                    entry.Description))
                .ToArray(),
            WorkExperiences = resume.WorkExperiences
                .OrderByDescending(entry => entry.StartDate)
                .ThenByDescending(entry => entry.Id)
                .Select(entry => new ResumeWorkExperienceItemDto(
                    entry.Id,
                    entry.CompanyName,
                    entry.JobTitle,
                    entry.StartDate,
                    entry.EndDate,
                    entry.Description))
                .ToArray(),
            Skills = resume.ResumeSkills
                .OrderBy(skill => skill.Skill.Name)
                .Select(skill => new ResumeSkillItemDto(
                    skill.SkillId,
                    skill.Skill.Name,
                    skill.YearsOfExperience))
                .ToArray()
        };
    }

    private static bool SkillsChanged(
        IEnumerable<ResumeSkill> existing,
        IReadOnlyCollection<ResumeSkillInput> incoming)
    {
        var before = existing
            .OrderBy(skill => skill.SkillId)
            .Select(skill => (skill.SkillId, skill.YearsOfExperience))
            .ToArray();
        var after = incoming
            .OrderBy(skill => skill.SkillId)
            .Select(skill => (skill.SkillId, skill.YearsOfExperience))
            .ToArray();
        return !before.SequenceEqual(after);
    }

    private static void ApplyChildren(Resume resume, ResumeInput input)
    {
        resume.Educations = input.Educations
            .Select(entry => new Education
            {
                SchoolName = entry.SchoolName.Trim(),
                Major = Normalize(entry.Major),
                StartDate = entry.StartDate,
                EndDate = entry.EndDate,
                Gpa = entry.Gpa,
                Description = Normalize(entry.Description)
            })
            .ToList();
        resume.WorkExperiences = input.WorkExperiences
            .Select(entry => new WorkExperience
            {
                CompanyName = entry.CompanyName.Trim(),
                JobTitle = Normalize(entry.JobTitle),
                StartDate = entry.StartDate,
                EndDate = entry.EndDate,
                Description = Normalize(entry.Description)
            })
            .ToList();
        resume.ResumeSkills = input.Skills
            .Select(skill => new ResumeSkill
            {
                SkillId = skill.SkillId,
                YearsOfExperience = skill.YearsOfExperience
            })
            .ToList();
    }

    private async Task<List<string>> ValidateInputAsync(ResumeInput? input, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        if (input is null)
        {
            errors.Add("Resume data is required.");
            return errors;
        }

        var title = input.Title?.Trim() ?? string.Empty;
        if (title.Length == 0)
            errors.Add("Resume title is required.");
        else if (title.Length > 200)
            errors.Add("Resume title must be at most 200 characters.");

        if (Normalize(input.Summary)?.Length > 2000)
            errors.Add("Resume summary must be at most 2000 characters.");

        if (input.Educations.Count > 20)
            errors.Add("A resume can have at most 20 education entries.");
        for (var index = 0; index < input.Educations.Count; index++)
        {
            var entry = input.Educations[index];
            var label = $"Education entry {index + 1}";
            if (string.IsNullOrWhiteSpace(entry.SchoolName))
                errors.Add($"{label}: school name is required.");
            else if (entry.SchoolName.Trim().Length > 200)
                errors.Add($"{label}: school name must be at most 200 characters.");
            if (Normalize(entry.Major)?.Length > 200)
                errors.Add($"{label}: major must be at most 200 characters.");
            if (Normalize(entry.Description)?.Length > 2000)
                errors.Add($"{label}: description must be at most 2000 characters.");
            if (entry.Gpa is < 0 or > 4)
                errors.Add($"{label}: GPA must be between 0 and 4.");
            if (entry.StartDate.HasValue && entry.EndDate.HasValue && entry.EndDate < entry.StartDate)
                errors.Add($"{label}: end date cannot be before the start date.");
        }

        if (input.WorkExperiences.Count > 20)
            errors.Add("A resume can have at most 20 work experience entries.");
        for (var index = 0; index < input.WorkExperiences.Count; index++)
        {
            var entry = input.WorkExperiences[index];
            var label = $"Work experience entry {index + 1}";
            if (string.IsNullOrWhiteSpace(entry.CompanyName))
                errors.Add($"{label}: company name is required.");
            else if (entry.CompanyName.Trim().Length > 200)
                errors.Add($"{label}: company name must be at most 200 characters.");
            if (Normalize(entry.JobTitle)?.Length > 200)
                errors.Add($"{label}: job title must be at most 200 characters.");
            if (Normalize(entry.Description)?.Length > 2000)
                errors.Add($"{label}: description must be at most 2000 characters.");
            if (entry.StartDate.HasValue && entry.EndDate.HasValue && entry.EndDate < entry.StartDate)
                errors.Add($"{label}: end date cannot be before the start date.");
        }

        var duplicateSkillIds = input.Skills
            .GroupBy(skill => skill.SkillId)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateSkillIds.Length > 0)
            errors.Add("Each skill can only be added once per resume.");

        foreach (var skill in input.Skills)
        {
            if (skill.SkillId <= 0)
                errors.Add("Choose a valid skill.");
            if (skill.YearsOfExperience is < 0 or > 50)
                errors.Add("Skill experience must be between 0 and 50 years.");
        }

        var skillIds = input.Skills
            .Select(skill => skill.SkillId)
            .Where(id => id > 0)
            .Distinct()
            .ToList();
        if (skillIds.Count > 0)
        {
            var existing = await dbContext.Skills
                .Where(skill => skillIds.Contains(skill.Id))
                .Select(skill => skill.Id)
                .ToListAsync(cancellationToken);
            if (existing.Count != skillIds.Count)
                errors.Add("One or more selected skills no longer exist.");
        }

        return errors;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

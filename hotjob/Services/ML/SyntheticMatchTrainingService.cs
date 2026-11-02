using hotjob.Data;
using hotjob.DTOs.ML;
using hotjob.Models.Application;
using hotjob.Models.Job;
using hotjob.Models.Resume;
using hotjob.Models.Users;
using hotjob.Services.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Services.ML;

/// <summary>Seeds deterministic synthetic applications and trains the match model. Dev-only.</summary>
public interface ISyntheticMatchTrainingService
{
    /// <summary>Creates or reuses the synthetic candidates/applications, then trains the model.</summary>
    Task<ServiceResult<SyntheticMatchTrainingResultDto>> SeedAndTrainAsync(
        string? trainedByUserId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Demo-only data source for the match model. It pairs synthetic candidates with
/// existing published jobs so the FastTree pipeline can be exercised without
/// waiting for real hiring outcomes. Never exposed outside Development.
/// </summary>
public sealed class SyntheticMatchTrainingService(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    IMatchScoreService matchScoreService,
    TimeProvider timeProvider,
    ILogger<SyntheticMatchTrainingService> logger) : ISyntheticMatchTrainingService
{
    private const int CandidateCount = 14;
    private const int StrongCount = 6;
    private const int WeakCount = 4;
    private const int RequiredJobs = 4;
    private const string EmailPrefix = "ml-demo-candidate-";
    private const string EmailDomain = "@example.invalid";

    public async Task<ServiceResult<SyntheticMatchTrainingResultDto>> SeedAndTrainAsync(
        string? trainedByUserId,
        CancellationToken cancellationToken = default)
    {
        var jobs = await dbContext.JobPostings
            .AsNoTracking()
            .Where(job => job.Status != JobStatus.Closed
                && job.Status != JobStatus.Expired
                && job.JobSkills.Any())
            .OrderBy(job => job.Status == JobStatus.Published ? 0 : 1)
            .ThenBy(job => job.Id)
            .Take(RequiredJobs)
            .Select(job => new
            {
                job.Id,
                job.ExperienceRequired,
                SkillIds = job.JobSkills.OrderBy(skill => skill.SkillId)
                    .Select(skill => skill.SkillId)
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        if (jobs.Count < RequiredJobs)
        {
            return ServiceResult<SyntheticMatchTrainingResultDto>.Fail(
                ServiceErrorType.Validation,
                $"At least {RequiredJobs} jobs with skills are required; found {jobs.Count}.");
        }

        var allSkillIds = jobs.SelectMany(job => job.SkillIds).Distinct().OrderBy(id => id).ToArray();

        var emails = Enumerable.Range(1, CandidateCount)
            .Select(index => $"{EmailPrefix}{index:00}{EmailDomain}")
            .ToArray();
        var existingUsers = await dbContext.Users
            .Where(user => user.Email != null && user.Email.StartsWith(EmailPrefix))
            .ToListAsync(cancellationToken);
        var usersByEmail = existingUsers
            .Where(user => emails.Contains(user.Email!, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(user => user.Email!, StringComparer.OrdinalIgnoreCase);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var candidateCreated = 0;

        foreach (var email in emails)
        {
            if (usersByEmail.ContainsKey(email))
            {
                continue;
            }

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                IsActive = true,
                CreatedAt = now
            };
            var created = await userManager.CreateAsync(user);
            if (!created.Succeeded)
            {
                return ServiceResult<SyntheticMatchTrainingResultDto>.Fail(
                    ServiceErrorType.Validation,
                    created.Errors.Select(error => error.Description));
            }

            usersByEmail[email] = user;
            candidateCreated++;
        }

        var userIds = usersByEmail.Values.Select(user => user.Id).ToList();
        var candidates = await dbContext.Candidates
            .Where(candidate => userIds.Contains(candidate.UserId))
            .ToListAsync(cancellationToken);
        var candidatesByUserId = candidates.ToDictionary(candidate => candidate.UserId);

        for (var index = 0; index < emails.Length; index++)
        {
            var user = usersByEmail[emails[index]];
            if (candidatesByUserId.ContainsKey(user.Id))
            {
                continue;
            }

            var candidate = new hotjob.Models.Users.Candidate
            {
                UserId = user.Id,
                FullName = $"ML Demo Candidate {index + 1:00}",
                Position = "Synthetic training profile",
                Summary = "Created by the dev-only ML seed. Not a real person.",
                JobSeekingStatus = "Open to work"
            };
            dbContext.Candidates.Add(candidate);
            candidatesByUserId[user.Id] = candidate;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var candidateIds = candidatesByUserId.Values.Select(candidate => candidate.Id).ToList();
        var resumes = await dbContext.Resumes
            .Include(resume => resume.ResumeSkills)
            .Where(resume => candidateIds.Contains(resume.CandidateId))
            .ToListAsync(cancellationToken);
        var resumesByCandidate = resumes
            .GroupBy(resume => resume.CandidateId)
            .ToDictionary(group => group.Key, group => group.OrderBy(resume => resume.Id).First());

        for (var index = 0; index < emails.Length; index++)
        {
            var candidate = candidatesByUserId[usersByEmail[emails[index]].Id];
            if (resumesByCandidate.ContainsKey(candidate.Id))
            {
                continue;
            }

            var skillIds = SkillIdsFor(index, allSkillIds);
            var resume = new Resume
            {
                CandidateId = candidate.Id,
                Title = "ML Demo CV",
                Summary = "Synthetic resume used only to verify the match-model pipeline.",
                IsPrimary = true,
                CreatedAt = now,
                ResumeSkills = skillIds
                    .Select(skillId => new ResumeSkill
                    {
                        SkillId = skillId,
                        YearsOfExperience = YearsFor(index)
                    })
                    .ToList()
            };
            dbContext.Resumes.Add(resume);
            resumesByCandidate[candidate.Id] = resume;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var existingApplications = await dbContext.Applications
            .Where(application => candidateIds.Contains(application.CandidateId))
            .Select(application => new { application.CandidateId, application.JobPostingId })
            .ToListAsync(cancellationToken);
        var applicationKeys = existingApplications
            .Select(application => (application.CandidateId, application.JobPostingId))
            .ToHashSet();
        var applicationCreated = 0;

        for (var index = 0; index < emails.Length; index++)
        {
            var candidate = candidatesByUserId[usersByEmail[emails[index]].Id];
            var resume = resumesByCandidate[candidate.Id];
            var status = index < StrongCount
                ? ApplicationStatus.Offered
                : index < StrongCount + WeakCount
                    ? ApplicationStatus.Rejected
                    : ApplicationStatus.Withdrawn;

            foreach (var job in jobs)
            {
                if (!applicationKeys.Add((candidate.Id, job.Id)))
                {
                    continue;
                }

                dbContext.Applications.Add(new Application
                {
                    JobPostingId = job.Id,
                    ResumeId = resume.Id,
                    CandidateId = candidate.Id,
                    CurrentStatus = status,
                    AppliedAt = now,
                    SnapshotSummary = "Synthetic application created by the dev-only ML seed."
                });
                applicationCreated++;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var applications = await dbContext.Applications
            .AsNoTracking()
            .Where(application => candidateIds.Contains(application.CandidateId))
            .Select(application => application.CurrentStatus)
            .ToListAsync(cancellationToken);

        var training = await matchScoreService.TrainAsync(trainedByUserId, cancellationToken);
        if (!training.Success)
        {
            return ServiceResult<SyntheticMatchTrainingResultDto>.Fail(
                training.ErrorType ?? ServiceErrorType.Validation,
                training.Errors);
        }

        logger.LogInformation(
            "Synthetic match data ready: {Candidates} candidates, {Applications} applications, {Offered} offered.",
            candidateIds.Count, applications.Count, applications.Count(status => status == ApplicationStatus.Offered));

        return ServiceResult<SyntheticMatchTrainingResultDto>.Ok(new SyntheticMatchTrainingResultDto(
            CandidateCount: candidateIds.Count,
            CandidateCreated: candidateCreated,
            ApplicationCount: applications.Count,
            ApplicationCreated: applicationCreated,
            OfferedCount: applications.Count(status => status == ApplicationStatus.Offered),
            RejectedCount: applications.Count(status => status == ApplicationStatus.Rejected),
            WithdrawnCount: applications.Count(status => status == ApplicationStatus.Withdrawn),
            Training: training.Data!));
    }

    private static int[] SkillIdsFor(int candidateIndex, int[] allSkillIds) =>
        candidateIndex < StrongCount + WeakCount
            ? allSkillIds
            : allSkillIds.Where((_, index) => index % 2 == 0).ToArray();

    private static int YearsFor(int candidateIndex) =>
        candidateIndex < StrongCount ? 8 : candidateIndex < StrongCount + WeakCount ? 0 : 3;
}

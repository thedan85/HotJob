using hotjob.Data;
using hotjob.DTOs.Candidate;
using hotjob.Models;
using hotjob.Models.Application;
using hotjob.Models.Job;
using hotjob.Models.Resume;
using hotjob.Models.Users;
using hotjob.Services.Candidate;
using hotjob.Services.Common;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace hotjob.Tests.CandidateTests;

/// <summary>
/// Resume ownership, the five-resume cap, single-primary invariant, delete
/// promotion, child validation and real file lifecycle. Every assertion checks
/// persisted state, not just the returned DTO.
/// </summary>
public sealed class ResumeServiceTests
{
    private static ResumeService NewService(TestFixture fx, string? root = null) =>
        new(
            fx.Db,
            new CvFileStorage(
                root ?? Path.Combine(Path.GetTempPath(), "hotjob-resume-tests", Guid.NewGuid().ToString("N")),
                NullLogger<CvFileStorage>.Instance),
            TimeProvider.System,
            NullLogger<ResumeService>.Instance);

    private static async Task<(string UserId, int CandidateId)> SeedCandidateAsync(
        TestFixture fx, string email)
    {
        var user = await fx.SeedUserAsync(email, "Str0ng!Pass", AppRoles.Candidate);
        var candidate = new Candidate { UserId = user.Id, FullName = $"Name {email}" };
        fx.Db.Candidates.Add(candidate);
        await fx.Db.SaveChangesAsync();
        return (user.Id, candidate.Id);
    }

    private static async Task<Skill> SeedSkillAsync(TestFixture fx, string name)
    {
        var skill = new Skill { Name = name };
        fx.Db.Skills.Add(skill);
        await fx.Db.SaveChangesAsync();
        return skill;
    }

    private static async Task<int> SeedJobAsync(TestFixture fx)
    {
        var company = new CompanyProfile
        {
            CompanyName = $"Co {Guid.NewGuid():N}",
            Status = CompanyStatus.Approved
        };
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();

        var job = new JobPosting
        {
            CompanyProfileId = company.Id,
            Title = "Backend Engineer",
            Description = "d",
            Status = JobStatus.Published
        };
        fx.Db.JobPostings.Add(job);
        await fx.Db.SaveChangesAsync();
        return job.Id;
    }

    private static ResumeInput FullInput(int skillId, string title = "Backend CV") => new()
    {
        Title = title,
        Summary = "Five years building APIs.",
        Educations =
        [
            new ResumeEducationInput
            {
                SchoolName = "University of Technology",
                Major = "Computer Science",
                StartDate = new DateOnly(2018, 9, 1),
                EndDate = new DateOnly(2022, 6, 30),
                Gpa = 3.5,
                Description = "Graduated with honours."
            }
        ],
        WorkExperiences =
        [
            new ResumeWorkExperienceInput
            {
                CompanyName = "Acme",
                JobTitle = "Software Engineer",
                StartDate = new DateOnly(2022, 7, 1),
                EndDate = new DateOnly(2025, 12, 31),
                Description = "Built payment APIs."
            }
        ],
        Skills = [new ResumeSkillInput { SkillId = skillId, YearsOfExperience = 3 }]
    };

    private static async Task<ResumeInput> SimpleInputAsync(TestFixture fx, string title)
    {
        var skill = await SeedSkillAsync(fx, $"Skill {Guid.NewGuid():N}");
        return new ResumeInput
        {
            Title = title,
            Skills = [new ResumeSkillInput { SkillId = skill.Id }]
        };
    }

    [Fact]
    public async Task CreateAsync_FirstResumeBecomesPrimaryAndPersistsChildren()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, candidateId) = await SeedCandidateAsync(fx, "me@example.com");
        var skill = await SeedSkillAsync(fx, "C#");

        var result = await NewService(fx).CreateAsync(userId, FullInput(skill.Id));

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var detail = result.Data!;
        Assert.True(detail.IsPrimary);
        Assert.Equal("Backend CV", detail.Title);
        Assert.Equal("Software Engineer", Assert.Single(detail.WorkExperiences).JobTitle);
        Assert.Equal("University of Technology", Assert.Single(detail.Educations).SchoolName);
        Assert.Equal("C#", Assert.Single(detail.Skills).SkillName);

        var stored = await fx.Db.Resumes.AsNoTracking().SingleAsync(resume => resume.Id == detail.Id);
        Assert.Equal(candidateId, stored.CandidateId);
        Assert.True(stored.IsPrimary);
        Assert.Equal(1, await fx.Db.Educations.CountAsync(entry => entry.ResumeId == stored.Id));
        Assert.Equal(1, await fx.Db.WorkExperiences.CountAsync(entry => entry.ResumeId == stored.Id));
        Assert.Equal(1, await fx.Db.ResumeSkills.CountAsync(entry => entry.ResumeId == stored.Id));
    }

    [Fact]
    public async Task CreateAsync_SixthResume_ReturnsConflictAndKeepsFive()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, candidateId) = await SeedCandidateAsync(fx, "me@example.com");
        var service = NewService(fx);
        for (var index = 1; index <= 5; index++)
        {
            var created = await service.CreateAsync(userId, await SimpleInputAsync(fx, $"CV {index}"));
            Assert.True(created.Success, string.Join("; ", created.Errors));
        }

        var result = await service.CreateAsync(userId, await SimpleInputAsync(fx, "CV 6"));

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
        Assert.Contains("up to 5 resumes", Assert.Single(result.Errors));
        Assert.Equal(5, await fx.Db.Resumes.CountAsync(resume => resume.CandidateId == candidateId));
    }

    [Fact]
    public async Task CreateAsync_OnlyOneResumeIsPrimary()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, _) = await SeedCandidateAsync(fx, "me@example.com");
        var service = NewService(fx);

        var first = await service.CreateAsync(userId, await SimpleInputAsync(fx, "First"));
        var second = await service.CreateAsync(userId, await SimpleInputAsync(fx, "Second"));

        Assert.True(first.Data!.IsPrimary);
        Assert.False(second.Data!.IsPrimary);
        var primaryCount = await fx.Db.Resumes.CountAsync(resume => resume.IsPrimary);
        Assert.Equal(1, primaryCount);
    }

    [Fact]
    public async Task SetPrimaryAsync_ClearsThePreviousPrimary()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, candidateId) = await SeedCandidateAsync(fx, "me@example.com");
        var service = NewService(fx);
        var first = await service.CreateAsync(userId, await SimpleInputAsync(fx, "First"));
        var second = await service.CreateAsync(userId, await SimpleInputAsync(fx, "Second"));

        var result = await service.SetPrimaryAsync(userId, second.Data!.Id);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.True(result.Data!.IsPrimary);
        var rows = await fx.Db.Resumes.AsNoTracking()
            .Where(resume => resume.CandidateId == candidateId)
            .OrderBy(resume => resume.Id)
            .ToListAsync();
        Assert.False(rows[0].IsPrimary);
        Assert.True(rows[1].IsPrimary);
        Assert.Single(rows, resume => resume.IsPrimary);
        Assert.Equal(first.Data!.Id, rows[0].Id);
    }

    [Fact]
    public async Task DeletePrimary_NewestRemainingResumeIsPromoted()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, candidateId) = await SeedCandidateAsync(fx, "me@example.com");
        var service = NewService(fx);
        var first = await service.CreateAsync(userId, await SimpleInputAsync(fx, "First"));
        var second = await service.CreateAsync(userId, await SimpleInputAsync(fx, "Second"));
        var third = await service.CreateAsync(userId, await SimpleInputAsync(fx, "Third"));

        var result = await service.DeleteAsync(userId, first.Data!.Id);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var rows = await fx.Db.Resumes.AsNoTracking()
            .Where(resume => resume.CandidateId == candidateId)
            .OrderBy(resume => resume.Id)
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.DoesNotContain(rows, resume => resume.Id == first.Data.Id);
        Assert.False(rows.Single(resume => resume.Id == second.Data!.Id).IsPrimary);
        Assert.True(rows.Single(resume => resume.Id == third.Data!.Id).IsPrimary);
    }

    [Fact]
    public async Task DeletePrimary_WithoutOtherResumes_LeavesNone()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, candidateId) = await SeedCandidateAsync(fx, "me@example.com");
        var service = NewService(fx);
        var only = await service.CreateAsync(userId, await SimpleInputAsync(fx, "Only"));

        var result = await service.DeleteAsync(userId, only.Data!.Id);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(0, await fx.Db.Resumes.CountAsync(resume => resume.CandidateId == candidateId));
    }

    [Fact]
    public async Task DeleteAsync_ResumeUsedByApplication_ReturnsConflictAndKeepsRow()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, candidateId) = await SeedCandidateAsync(fx, "me@example.com");
        var service = NewService(fx);
        var resume = await service.CreateAsync(userId, await SimpleInputAsync(fx, "Applied CV"));
        var jobId = await SeedJobAsync(fx);
        fx.Db.Applications.Add(new Application
        {
            JobPostingId = jobId,
            ResumeId = resume.Data!.Id,
            CandidateId = candidateId,
            CurrentStatus = ApplicationStatus.Pending
        });
        await fx.Db.SaveChangesAsync();

        var result = await service.DeleteAsync(userId, resume.Data.Id);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
        Assert.Contains("used to apply", Assert.Single(result.Errors));
        Assert.True(await fx.Db.Resumes.AnyAsync(row => row.Id == resume.Data.Id));
    }

    [Fact]
    public async Task UpdateAsync_AnotherCandidatesResume_IsNotFoundAndUnchanged()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (ownerUserId, _) = await SeedCandidateAsync(fx, "owner@example.com");
        var (otherUserId, _) = await SeedCandidateAsync(fx, "other@example.com");
        var service = NewService(fx);
        var resume = await service.CreateAsync(ownerUserId, await SimpleInputAsync(fx, "Owner CV"));

        var result = await service.UpdateAsync(
            otherUserId, resume.Data!.Id, await SimpleInputAsync(fx, "Hijacked"));

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
        var stored = await fx.Db.Resumes.AsNoTracking().SingleAsync(row => row.Id == resume.Data.Id);
        Assert.Equal("Owner CV", stored.Title);
    }

    [Fact]
    public async Task GetAsync_AnotherCandidatesResume_IsNotFound()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (ownerUserId, _) = await SeedCandidateAsync(fx, "owner@example.com");
        var (otherUserId, _) = await SeedCandidateAsync(fx, "other@example.com");
        var service = NewService(fx);
        var resume = await service.CreateAsync(ownerUserId, await SimpleInputAsync(fx, "Private CV"));

        var result = await service.GetAsync(otherUserId, resume.Data!.Id);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
    }

    [Fact]
    public async Task UpdateAsync_ReplacesChildren()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, _) = await SeedCandidateAsync(fx, "me@example.com");
        var firstSkill = await SeedSkillAsync(fx, "C#");
        var secondSkill = await SeedSkillAsync(fx, "SQL");
        var service = NewService(fx);
        var created = await service.CreateAsync(userId, FullInput(firstSkill.Id));

        var result = await service.UpdateAsync(userId, created.Data!.Id, new ResumeInput
        {
            Title = "Updated CV",
            Summary = "Updated summary.",
            Educations = [new ResumeEducationInput { SchoolName = "New School" }],
            Skills = [new ResumeSkillInput { SkillId = secondSkill.Id, YearsOfExperience = 5 }]
        });

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal("Updated CV", result.Data!.Title);
        Assert.Equal("New School", Assert.Single(result.Data.Educations).SchoolName);
        Assert.Empty(result.Data.WorkExperiences);
        var skill = Assert.Single(result.Data.Skills);
        Assert.Equal(secondSkill.Id, skill.SkillId);
        Assert.Equal(5, skill.YearsOfExperience);
        Assert.Equal(1, await fx.Db.Educations.CountAsync(entry => entry.ResumeId == created.Data.Id));
        Assert.Equal(0, await fx.Db.WorkExperiences.CountAsync(entry => entry.ResumeId == created.Data.Id));
        Assert.Equal(1, await fx.Db.ResumeSkills.CountAsync(entry => entry.ResumeId == created.Data.Id));
    }

    [Fact]
    public async Task CreateAsync_DuplicateSkillIds_ReturnsValidationError()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, _) = await SeedCandidateAsync(fx, "me@example.com");
        var skill = await SeedSkillAsync(fx, "C#");
        var input = new ResumeInput
        {
            Title = "CV",
            Skills =
            [
                new ResumeSkillInput { SkillId = skill.Id, YearsOfExperience = 1 },
                new ResumeSkillInput { SkillId = skill.Id, YearsOfExperience = 2 }
            ]
        };

        var result = await NewService(fx).CreateAsync(userId, input);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
        Assert.Contains("only be added once", string.Join(" ", result.Errors));
        Assert.Equal(0, await fx.Db.Resumes.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_UnknownSkillId_ReturnsValidationError()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, _) = await SeedCandidateAsync(fx, "me@example.com");
        var input = new ResumeInput
        {
            Title = "CV",
            Skills = [new ResumeSkillInput { SkillId = 9999, YearsOfExperience = 1 }]
        };

        var result = await NewService(fx).CreateAsync(userId, input);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
        Assert.Contains("skills no longer exist", string.Join(" ", result.Errors));
    }

    [Fact]
    public async Task CreateAsync_EducationEndBeforeStart_ReturnsValidationError()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, _) = await SeedCandidateAsync(fx, "me@example.com");
        var input = new ResumeInput
        {
            Title = "CV",
            Educations =
            [
                new ResumeEducationInput
                {
                    SchoolName = "School",
                    StartDate = new DateOnly(2024, 1, 1),
                    EndDate = new DateOnly(2023, 1, 1)
                }
            ]
        };

        var result = await NewService(fx).CreateAsync(userId, input);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
        Assert.Contains("end date cannot be before", string.Join(" ", result.Errors));
    }

    [Fact]
    public async Task CreateAsync_WorkExperienceEndBeforeStart_ReturnsValidationError()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, _) = await SeedCandidateAsync(fx, "me@example.com");
        var input = new ResumeInput
        {
            Title = "CV",
            WorkExperiences =
            [
                new ResumeWorkExperienceInput
                {
                    CompanyName = "Acme",
                    StartDate = new DateOnly(2024, 1, 1),
                    EndDate = new DateOnly(2023, 1, 1)
                }
            ]
        };

        var result = await NewService(fx).CreateAsync(userId, input);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
        Assert.Contains("end date cannot be before", string.Join(" ", result.Errors));
    }

    [Fact]
    public async Task CreateAsync_NoSkills_ReturnsValidationError()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, _) = await SeedCandidateAsync(fx, "me@example.com");

        var result = await NewService(fx).CreateAsync(userId, new ResumeInput { Title = "CV" });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
        Assert.Contains("At least one skill is required.", string.Join(" ", result.Errors));
        Assert.Equal(0, await fx.Db.Resumes.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_MissingTitle_ReturnsValidationError()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, _) = await SeedCandidateAsync(fx, "me@example.com");

        var result = await NewService(fx).CreateAsync(userId, new ResumeInput { Title = "   " });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
        Assert.Contains("title is required", string.Join(" ", result.Errors));
    }

    [Fact]
    public async Task CreateAsync_AnonymousUser_IsUnauthorized()
    {
        await using var fx = await TestFixture.CreateAsync();

        var result = await NewService(fx).CreateAsync(null, new ResumeInput { Title = "CV" });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Unauthorized, result.ErrorType);
    }

    [Fact]
    public async Task UploadFileAsync_StoresFileAndDeleteResumeRemovesIt()
    {
        await using var fx = await TestFixture.CreateAsync();
        var root = Path.Combine(Path.GetTempPath(), "hotjob-resume-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var (userId, candidateId) = await SeedCandidateAsync(fx, "me@example.com");
            var service = NewService(fx, root);
            var created = await service.CreateAsync(userId, await SimpleInputAsync(fx, "CV"));
            await using var content = new MemoryStream("%PDF-1.4 uploaded"u8.ToArray());

            var upload = await service.UploadFileAsync(
                userId, created.Data!.Id, "My CV.pdf", content.Length, content);

            Assert.True(upload.Success, string.Join("; ", upload.Errors));
            var fileUrl = upload.Data!.FileUrl!;
            Assert.StartsWith($"/uploads/cv/{candidateId}/", fileUrl);
            var storedPath = Path.Combine(root, fileUrl["/uploads/cv/".Length..]);
            Assert.True(File.Exists(storedPath));

            var deleted = await service.DeleteAsync(userId, created.Data.Id);

            Assert.True(deleted.Success, string.Join("; ", deleted.Errors));
            Assert.False(File.Exists(storedPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task UploadFileAsync_KeepsOldFileWhenAnApplicationSnapshotReferencesIt()
    {
        await using var fx = await TestFixture.CreateAsync();
        var root = Path.Combine(Path.GetTempPath(), "hotjob-resume-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var (userId, candidateId) = await SeedCandidateAsync(fx, "me@example.com");
            var service = NewService(fx, root);
            var created = await service.CreateAsync(userId, await SimpleInputAsync(fx, "CV"));

            await using var first = new MemoryStream("%PDF-1.4 first"u8.ToArray());
            var firstUpload = await service.UploadFileAsync(userId, created.Data!.Id, "first.pdf", first.Length, first);
            var firstUrl = firstUpload.Data!.FileUrl!;
            var firstPath = Path.Combine(root, firstUrl["/uploads/cv/".Length..]);

            var jobId = await SeedJobAsync(fx);
            fx.Db.Applications.Add(new Application
            {
                JobPostingId = jobId,
                ResumeId = created.Data.Id,
                CandidateId = candidateId,
                SnapshotFileUrl = firstUrl,
                CurrentStatus = ApplicationStatus.Pending
            });
            await fx.Db.SaveChangesAsync();

            await using var second = new MemoryStream("%PDF-1.4 second"u8.ToArray());
            var secondUpload = await service.UploadFileAsync(userId, created.Data.Id, "second.pdf", second.Length, second);
            var secondPath = Path.Combine(root, secondUpload.Data!.FileUrl!["/uploads/cv/".Length..]);

            Assert.True(File.Exists(firstPath));
            Assert.True(File.Exists(secondPath));
            var snapshot = await fx.Db.Applications.AsNoTracking().SingleAsync();
            Assert.Equal(firstUrl, snapshot.SnapshotFileUrl);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task UploadFileAsync_ReplacesThePreviousStoredFile()
    {
        await using var fx = await TestFixture.CreateAsync();
        var root = Path.Combine(Path.GetTempPath(), "hotjob-resume-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var (userId, _) = await SeedCandidateAsync(fx, "me@example.com");
            var service = NewService(fx, root);
            var created = await service.CreateAsync(userId, await SimpleInputAsync(fx, "CV"));

            await using var first = new MemoryStream("%PDF-1.4 first"u8.ToArray());
            var firstUpload = await service.UploadFileAsync(userId, created.Data!.Id, "first.pdf", first.Length, first);
            var firstPath = Path.Combine(root, firstUpload.Data!.FileUrl!["/uploads/cv/".Length..]);

            await using var second = new MemoryStream("%PDF-1.4 second"u8.ToArray());
            var secondUpload = await service.UploadFileAsync(userId, created.Data.Id, "second.pdf", second.Length, second);
            var secondPath = Path.Combine(root, secondUpload.Data!.FileUrl!["/uploads/cv/".Length..]);

            Assert.NotEqual(firstUpload.Data.FileUrl, secondUpload.Data.FileUrl);
            Assert.False(File.Exists(firstPath));
            Assert.True(File.Exists(secondPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}

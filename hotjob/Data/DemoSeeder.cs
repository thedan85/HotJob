using hotjob.Models.Application;
using hotjob.Models.Job;
using hotjob.Models.Resume;
using hotjob.Models.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace hotjob.Data;

/// <summary>Seeds a demo dataset for local Swagger testing: approved company, owner employer,
/// published job, candidate with resume, and one pending application. Idempotent.</summary>
public static class DemoSeeder
{
    public static async Task SeedAsync(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        SeedOptions seed,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(userManager);
        ArgumentNullException.ThrowIfNull(roleManager);
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(logger);

        var demo = seed.Demo;
        if (demo is null
            || string.IsNullOrWhiteSpace(demo.CompanyName)
            || string.IsNullOrWhiteSpace(demo.EmployerEmail)
            || string.IsNullOrWhiteSpace(demo.EmployerPassword)
            || string.IsNullOrWhiteSpace(demo.CandidateEmail)
            || string.IsNullOrWhiteSpace(demo.CandidatePassword))
        {
            logger.LogInformation("Demo seeding skipped: Seed:Demo is not configured.");
            return;
        }

        foreach (var role in new[] { AppRoles.Employer, AppRoles.Candidate })
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await roleManager.RoleExistsAsync(role).ConfigureAwait(false))
            {
                var result = await roleManager.CreateAsync(new IdentityRole(role)).ConfigureAwait(false);
                if (!result.Succeeded)
                    throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        }

        var companyName = demo.CompanyName.Trim();
        var company = await db.CompanyProfiles
            .FirstOrDefaultAsync(c => c.CompanyName == companyName, cancellationToken)
            .ConfigureAwait(false);
        if (company is null)
        {
            company = new CompanyProfile
            {
                CompanyName = companyName,
                Location = "Ho Chi Minh City",
                Industry = "Software",
                Scale = CompanyScale.From50To199,
                Description = "Demo company for local Swagger testing.",
                Status = CompanyStatus.Approved,
                VerifiedAt = DateTime.UtcNow,
            };
            db.CompanyProfiles.Add(company);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Seeded demo company {Name}.", companyName);
        }
        else if (company.Status != CompanyStatus.Approved)
        {
            company.Status = CompanyStatus.Approved;
            company.VerifiedAt ??= DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var employerUser = await EnsureUserAsync(
            userManager, demo.EmployerEmail.Trim(), demo.EmployerPassword, AppRoles.Employer, logger).ConfigureAwait(false);
        var employer = await db.Employers
            .FirstOrDefaultAsync(e => e.UserId == employerUser.Id, cancellationToken)
            .ConfigureAwait(false);
        if (employer is null)
        {
            db.Employers.Add(new Models.Users.Employer
            {
                UserId = employerUser.Id,
                CompanyProfileId = company.Id,
                FullName = string.IsNullOrWhiteSpace(demo.EmployerName) ? "Demo Employer" : demo.EmployerName.Trim(),
                Position = "HR Manager",
                IsOwner = true,
                MembershipStatus = MembershipStatus.Active,
            });
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (employer.MembershipStatus != MembershipStatus.Active)
        {
            employer.MembershipStatus = MembershipStatus.Active;
            employer.CompanyProfileId = company.Id;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        employer = await db.Employers
            .FirstAsync(e => e.UserId == employerUser.Id, cancellationToken)
            .ConfigureAwait(false);

        // Published job with the seeded reference categories/skills (DatabaseSeeder runs first).
        var job = await db.JobPostings
            .Include(j => j.JobSkills)
            .Include(j => j.CategoryMappings)
            .FirstOrDefaultAsync(j => j.Title == "Senior .NET Developer" && j.CompanyProfileId == company.Id, cancellationToken)
            .ConfigureAwait(false);
        if (job is null)
        {
            var category = await db.JobCategories
                .FirstOrDefaultAsync(c => c.Name == "IT - Software", cancellationToken)
                .ConfigureAwait(false);
            var skills = await db.Skills
                .Where(s => s.Name == "C#" || s.Name == "SQL" || s.Name == "React")
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            job = new JobPosting
            {
                CompanyProfileId = company.Id,
                CreatedByEmployerId = employer.Id,
                Title = "Senior .NET Developer",
                Description = "Demo posting for Swagger testing: build and maintain .NET APIs.",
                SalaryMin = 1500,
                SalaryMax = 3000,
                ExperienceRequired = 3,
                JobType = JobType.FullTime,
                ExpiryDate = DateTime.UtcNow.AddMonths(3),
                Status = JobStatus.Published,
            };
            db.JobPostings.Add(job);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (category is not null)
                db.JobCategoryMappings.Add(new JobCategoryMapping { JobPostingId = job.Id, JobCategoryId = category.Id });
            foreach (var skill in skills)
                db.JobSkills.Add(new JobSkill { JobPostingId = job.Id, SkillId = skill.Id, ImportanceWeight = 8 });
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Seeded demo job {JobId}.", job.Id);
        }

        var candidateUser = await EnsureUserAsync(
            userManager, demo.CandidateEmail.Trim(), demo.CandidatePassword, AppRoles.Candidate, logger).ConfigureAwait(false);
        var candidate = await db.Candidates
            .FirstOrDefaultAsync(c => c.UserId == candidateUser.Id, cancellationToken)
            .ConfigureAwait(false);
        if (candidate is null)
        {
            candidate = new Candidate
            {
                UserId = candidateUser.Id,
                FullName = string.IsNullOrWhiteSpace(demo.CandidateName) ? "Demo Candidate" : demo.CandidateName.Trim(),
                Address = "Ho Chi Minh City",
            };
            db.Candidates.Add(candidate);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var resume = await db.Resumes
            .FirstOrDefaultAsync(r => r.CandidateId == candidate.Id, cancellationToken)
            .ConfigureAwait(false);
        if (resume is null)
        {
            var csharp = await db.Skills.FirstOrDefaultAsync(s => s.Name == "C#", cancellationToken).ConfigureAwait(false);
            var sql = await db.Skills.FirstOrDefaultAsync(s => s.Name == "SQL", cancellationToken).ConfigureAwait(false);
            resume = new Resume
            {
                CandidateId = candidate.Id,
                Title = "Backend .NET Resume",
                Summary = "3 years building .NET APIs and SQL databases.",
                IsPrimary = true,
            };
            db.Resumes.Add(resume);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            db.Educations.Add(new Education
            {
                ResumeId = resume.Id,
                SchoolName = "Demo University",
                Major = "Software Engineering",
                StartDate = new DateOnly(2018, 9, 1),
                EndDate = new DateOnly(2022, 6, 30),
                Gpa = 3.5,
            });
            db.WorkExperiences.Add(new WorkExperience
            {
                ResumeId = resume.Id,
                CompanyName = "Demo Soft",
                JobTitle = ".NET Developer",
                StartDate = new DateOnly(2022, 7, 1),
                Description = "Built REST APIs with ASP.NET Core and MySQL.",
            });
            if (csharp is not null)
                db.ResumeSkills.Add(new ResumeSkill { ResumeId = resume.Id, SkillId = csharp.Id, YearsOfExperience = 3 });
            if (sql is not null)
                db.ResumeSkills.Add(new ResumeSkill { ResumeId = resume.Id, SkillId = sql.Id, YearsOfExperience = 3 });
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Seeded demo resume {ResumeId}.", resume.Id);
        }

        var application = await db.Applications
            .FirstOrDefaultAsync(a => a.JobPostingId == job.Id && a.CandidateId == candidate.Id, cancellationToken)
            .ConfigureAwait(false);
        if (application is null)
        {
            application = new Models.Application.Application
            {
                JobPostingId = job.Id,
                ResumeId = resume.Id,
                CandidateId = candidate.Id,
                CoverLetter = "Demo application for Swagger testing.",
                CurrentStatus = ApplicationStatus.Pending,
                SnapshotSummary = "3 years .NET + SQL.",
            };
            db.Applications.Add(application);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            db.ApplicationStatusHistories.Add(new ApplicationStatusHistory
            {
                ApplicationId = application.Id,
                Status = ApplicationStatus.Pending,
                Notes = "Applied via demo seed",
                ChangedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Seeded demo application {ApplicationId}.", application.Id);
        }
    }

    private static async Task<ApplicationUser> EnsureUserAsync(
        UserManager<ApplicationUser> userManager,
        string email,
        string password,
        string role,
        ILogger logger)
    {
        var existing = await userManager.FindByEmailAsync(email).ConfigureAwait(false);
        if (existing is not null)
        {
            if (!await userManager.IsInRoleAsync(existing, role).ConfigureAwait(false))
            {
                var addRole = await userManager.AddToRoleAsync(existing, role).ConfigureAwait(false);
                if (!addRole.Succeeded)
                    throw new InvalidOperationException(string.Join("; ", addRole.Errors.Select(e => e.Description)));
            }
            return existing;
        }

        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, IsActive = true };
        var created = await userManager.CreateAsync(user, password).ConfigureAwait(false);
        if (!created.Succeeded)
            throw new InvalidOperationException(string.Join("; ", created.Errors.Select(e => e.Description)));
        var roled = await userManager.AddToRoleAsync(user, role).ConfigureAwait(false);
        if (!roled.Succeeded)
            throw new InvalidOperationException(string.Join("; ", roled.Errors.Select(e => e.Description)));
        logger.LogInformation("Seeded demo user {Email}.", email);
        return user;
    }
}

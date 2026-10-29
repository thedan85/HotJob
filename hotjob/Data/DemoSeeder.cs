using hotjob.Models;
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
                Website = "https://demo.example.com",
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

        await SeedCompanyCatalogAsync(db, userManager, logger, cancellationToken).ConfigureAwait(false);

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

        await SeedAdditionalJobsAsync(db, company.Id, employer.Id, logger, cancellationToken).ConfigureAwait(false);

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

        await SeedAdditionalApplicationsAsync(db, company.Id, candidate.Id, resume.Id, logger, cancellationToken).ConfigureAwait(false);

        await SeedReviewsAsync(db, userManager, logger, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Extra postings so Swagger has list/search, draft and featured-ordering data.</summary>
    private static readonly (string Title, string Description, string[] SkillNames, JobStatus Status, int SalaryMin, int SalaryMax, int Experience)[] AdditionalJobs =
    [
        ("Frontend React Developer", "Build candidate-facing React screens for the demo job board.", ["React", "SQL"], JobStatus.Published, 900, 1800, 2),
        ("Full Stack .NET Developer", "Own an ASP.NET Core API and its React front end.", ["C#", "React"], JobStatus.Published, 1200, 2400, 3),
        ("QA Engineer", "Draft posting used to exercise update and delete flows.", ["SQL"], JobStatus.Draft, 700, 1200, 1),
    ];

    /// <summary>Extra applications so ATS status filters and history have data.</summary>
    private static readonly (string JobTitle, ApplicationStatus Status, int DaysAgo, string Note)[] AdditionalApplications =
    [
        ("Frontend React Developer", ApplicationStatus.Viewed, 3, "Viewed by demo seed"),
        ("Full Stack .NET Developer", ApplicationStatus.Shortlisted, 5, "Shortlisted by demo seed"),
    ];

    /// <summary>Fixed catalog of Vietnamese tech companies so the public directory
    /// has realistic browse/filter/pagination data. Idempotent by company name;
    /// every company gets an owner employer (required by the domain model).</summary>
    private static readonly (string Name, string Industry, string Location, CompanyScale Scale, string Description)[] CompanyCatalog =
    [
        ("FPT Software", "Software", "Hanoi", CompanyScale.MoreThan500, "Vietnam's largest software outsourcing provider."),
        ("VNG Corporation", "Game", "Ho Chi Minh City", CompanyScale.MoreThan500, "Creator of Zalo and top online games."),
        ("Tiki", "E-commerce", "Ho Chi Minh City", CompanyScale.From200To499, "Leading Vietnamese e-commerce platform."),
        ("MoMo", "Fintech", "Ho Chi Minh City", CompanyScale.From200To499, "Vietnam's top e-wallet and payment platform."),
        ("Axon Active", "Software", "Ho Chi Minh City", CompanyScale.From200To499, "Swiss-Vietnamese agile software builder."),
        ("KMS Technology", "Software", "Ho Chi Minh City", CompanyScale.From200To499, "US-Vietnamese software product engineering firm."),
        ("NashTech Vietnam", "Software", "Ho Chi Minh City", CompanyScale.From200To499, "Global technology consultancy with a large Vietnam hub."),
        ("CMC Global", "Cloud", "Hanoi", CompanyScale.From200To499, "Cloud and digital transformation services."),
        ("Rikkeisoft", "Software", "Hanoi", CompanyScale.From200To499, "Japanese-Vietnamese software outsourcing company."),
        ("FPT AI", "AI", "Hanoi", CompanyScale.From50To199, "Applied AI research and products at FPT."),
        ("Viettel AI", "AI", "Hanoi", CompanyScale.From200To499, "AI research arm of Viettel Group."),
        ("TopCV", "SaaS", "Hanoi", CompanyScale.From50To199, "Online recruitment platform for Vietnam."),
        ("Base.vn", "SaaS", "Ho Chi Minh City", CompanyScale.From50To199, "Business management SaaS suite."),
        ("Got It AI", "AI", "Ho Chi Minh City", CompanyScale.From50To199, "Machine learning products and expert network."),
        ("Coc Coc", "Software", "Hanoi", CompanyScale.From50To199, "Vietnamese browser and search engine maker."),
        ("Be Group", "Logistics", "Ho Chi Minh City", CompanyScale.From200To499, "Ride-hailing and delivery super-app."),
        ("GiaoHangNhanh", "Logistics", "Ho Chi Minh City", CompanyScale.From200To499, "Nationwide express delivery network."),
        ("Elsa Speak", "Edtech", "Ho Chi Minh City", CompanyScale.From50To199, "AI English pronunciation coach."),
        ("Topica", "Edtech", "Hanoi", CompanyScale.From50To199, "Online education for adults and kids."),
        ("Jio Health", "Healthtech", "Ho Chi Minh City", CompanyScale.From50To199, "On-demand clinics and home care."),
        ("VinBigData", "Data", "Hanoi", CompanyScale.From50To199, "Big data and AI institute of Vingroup."),
        ("WhiteHat Security", "Cybersecurity", "Hanoi", CompanyScale.From10To49, "Security operations and consulting."),
        ("Shopee Vietnam", "E-commerce", "Ho Chi Minh City", CompanyScale.MoreThan500, "Regional e-commerce leader's Vietnam hub."),
        ("Hybrid Labs", "SaaS", "Remote", CompanyScale.From10To49, "Remote-first product studio for startups."),
    ];

    /// <summary>Reviewer candidates used to seed company reviews (one review per company each).</summary>
    private static readonly (string Name, string Email)[] ReviewerCatalog =
    [
        ("Minh Tran", "reviewer.minh@example.com"),
        ("Lan Pham", "reviewer.lan@example.com"),
        ("Huy Nguyen", "reviewer.huy@example.com"),
        ("Trang Le", "reviewer.trang@example.com"),
        ("Duc Vo", "reviewer.duc@example.com"),
        ("Mai Hoang", "reviewer.mai@example.com"),
    ];

    private static readonly (int Rating, string Text)[] ReviewTemplates =
    [
        (5, "Great place to grow — clear roadmap and supportive teammates."),
        (4, "Solid engineering culture and competitive pay."),
        (4, "Good work-life balance most of the time."),
        (3, "Decent, but processes can be slow."),
        (5, "Learned a lot here; mentoring is strong."),
        (4, "Friendly colleagues and interesting projects."),
    ];

    /// <summary>Seeds reviews for every approved company so the public detail page's
    /// review section has data. Idempotent per (company, reviewer candidate).</summary>
    private static async Task SeedReviewsAsync(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var companyIds = await db.CompanyProfiles
            .Where(c => c.Status == CompanyStatus.Approved)
            .OrderBy(c => c.Id)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        for (var r = 0; r < ReviewerCatalog.Length; r++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (name, email) = ReviewerCatalog[r];
            var user = await EnsureUserAsync(
                userManager, email, "Reviewer123!", AppRoles.Candidate, logger).ConfigureAwait(false);
            var candidate = await db.Candidates
                .FirstOrDefaultAsync(c => c.UserId == user.Id, cancellationToken)
                .ConfigureAwait(false);
            if (candidate is null)
            {
                candidate = new Candidate { UserId = user.Id, FullName = name };
                db.Candidates.Add(candidate);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            else if (candidate.FullName != name)
            {
                candidate.FullName = name;
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            for (var i = 0; i < companyIds.Count; i++)
            {
                var companyId = companyIds[i];
                var exists = await db.Reviews.AnyAsync(
                    review => review.CompanyProfileId == companyId && review.CandidateId == candidate.Id,
                    cancellationToken).ConfigureAwait(false);
                if (exists)
                    continue;

                var template = ReviewTemplates[(i + r) % ReviewTemplates.Length];
                db.Reviews.Add(new Review
                {
                    CompanyProfileId = companyId,
                    CandidateId = candidate.Id,
                    RatingScore = template.Rating,
                    ReviewText = template.Text,
                    CreatedAt = DateTime.UtcNow.AddDays(-(i + 1)),
                });
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task SeedAdditionalJobsAsync(
        ApplicationDbContext db,
        int companyId,
        int employerId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var category = await db.JobCategories
            .FirstOrDefaultAsync(c => c.Name == "IT - Software", cancellationToken)
            .ConfigureAwait(false);

        foreach (var entry in AdditionalJobs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var exists = await db.JobPostings.AnyAsync(
                j => j.Title == entry.Title && j.CompanyProfileId == companyId,
                cancellationToken).ConfigureAwait(false);
            if (exists)
                continue;

            var job = new JobPosting
            {
                CompanyProfileId = companyId,
                CreatedByEmployerId = employerId,
                Title = entry.Title,
                Description = entry.Description,
                SalaryMin = entry.SalaryMin,
                SalaryMax = entry.SalaryMax,
                ExperienceRequired = entry.Experience,
                JobType = JobType.FullTime,
                ExpiryDate = DateTime.UtcNow.AddMonths(3),
                Status = entry.Status,
            };
            db.JobPostings.Add(job);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (category is not null)
                db.JobCategoryMappings.Add(new JobCategoryMapping { JobPostingId = job.Id, JobCategoryId = category.Id });

            var skillNames = entry.SkillNames.ToList();
            var skills = await db.Skills
                .Where(s => skillNames.Contains(s.Name))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var skill in skills)
                db.JobSkills.Add(new JobSkill { JobPostingId = job.Id, SkillId = skill.Id, ImportanceWeight = 8 });

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Seeded demo job {JobId} ({Status}).", job.Id, job.Status);
        }
    }

    private static async Task SeedAdditionalApplicationsAsync(
        ApplicationDbContext db,
        int companyId,
        int candidateId,
        int resumeId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        foreach (var entry in AdditionalApplications)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var job = await db.JobPostings.FirstOrDefaultAsync(
                j => j.Title == entry.JobTitle && j.CompanyProfileId == companyId,
                cancellationToken).ConfigureAwait(false);
            if (job is null)
                continue;

            var exists = await db.Applications.AnyAsync(
                a => a.JobPostingId == job.Id && a.CandidateId == candidateId,
                cancellationToken).ConfigureAwait(false);
            if (exists)
                continue;

            var appliedAt = DateTime.UtcNow.AddDays(-entry.DaysAgo - 1);
            var application = new Models.Application.Application
            {
                JobPostingId = job.Id,
                ResumeId = resumeId,
                CandidateId = candidateId,
                CoverLetter = "Demo application for Swagger testing.",
                CurrentStatus = entry.Status,
                SnapshotSummary = "3 years .NET + SQL.",
                AppliedAt = appliedAt,
            };
            db.Applications.Add(application);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            db.ApplicationStatusHistories.Add(new ApplicationStatusHistory
            {
                ApplicationId = application.Id,
                Status = ApplicationStatus.Pending,
                Notes = "Applied via demo seed",
                ChangedAt = appliedAt,
            });
            if (entry.Status != ApplicationStatus.Pending)
            {
                db.ApplicationStatusHistories.Add(new ApplicationStatusHistory
                {
                    ApplicationId = application.Id,
                    Status = entry.Status,
                    Notes = entry.Note,
                    ChangedAt = DateTime.UtcNow.AddDays(-entry.DaysAgo),
                });
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Seeded demo application {ApplicationId} ({Status}).", application.Id, application.CurrentStatus);
        }
    }

    private static async Task SeedCompanyCatalogAsync(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        foreach (var entry in CompanyCatalog)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var company = await db.CompanyProfiles
                .FirstOrDefaultAsync(c => c.CompanyName == entry.Name, cancellationToken)
                .ConfigureAwait(false);
            var slug = new string(entry.Name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
            var website = $"https://{slug}.example.com";
            if (company is null)
            {
                company = new CompanyProfile
                {
                    CompanyName = entry.Name,
                    Website = website,
                    Industry = entry.Industry,
                    Location = entry.Location,
                    Scale = entry.Scale,
                    Description = entry.Description,
                    Status = CompanyStatus.Approved,
                    VerifiedAt = DateTime.UtcNow,
                };
                db.CompanyProfiles.Add(company);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                logger.LogInformation("Seeded catalog company {Name}.", entry.Name);
            }
            else
            {
                var changed = false;
                if (company.Status != CompanyStatus.Approved)
                {
                    company.Status = CompanyStatus.Approved;
                    company.VerifiedAt ??= DateTime.UtcNow;
                    changed = true;
                }

                if (string.IsNullOrWhiteSpace(company.Website))
                {
                    company.Website = website;
                    changed = true;
                }

                if (changed)
                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            var ownerUser = await EnsureUserAsync(
                userManager, $"owner.{slug}@example.com", "Owner123!", AppRoles.Employer, logger).ConfigureAwait(false);
            var employer = await db.Employers
                .FirstOrDefaultAsync(e => e.UserId == ownerUser.Id, cancellationToken)
                .ConfigureAwait(false);
            if (employer is null)
            {
                db.Employers.Add(new Employer
                {
                    UserId = ownerUser.Id,
                    CompanyProfileId = company.Id,
                    FullName = $"{entry.Name} HR",
                    Position = "HR Manager",
                    IsOwner = true,
                    MembershipStatus = MembershipStatus.Active,
                });
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var changed = false;
                if (employer.CompanyProfileId != company.Id) { employer.CompanyProfileId = company.Id; changed = true; }
                if (!employer.IsOwner) { employer.IsOwner = true; changed = true; }
                if (employer.MembershipStatus != MembershipStatus.Active) { employer.MembershipStatus = MembershipStatus.Active; changed = true; }
                if (changed)
                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
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

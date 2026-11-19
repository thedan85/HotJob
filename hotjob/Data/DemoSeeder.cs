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
        await SeedCatalogJobsAsync(db, logger, cancellationToken).ConfigureAwait(false);

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
                // Opted in so employer candidate search (and its skill filter) has data on a fresh install.
                IsDiscoverable = true,
            };
            db.Candidates.Add(candidate);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (!candidate.IsDiscoverable)
        {
            // Existing dev databases predate opt-in seeding; keep the demo searchable.
            candidate.IsDiscoverable = true;
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
        await SeedCandidateCatalogAsync(db, userManager, logger, cancellationToken).ConfigureAwait(false);
        await SeedCandidateApplicationsAsync(db, logger, cancellationToken).ConfigureAwait(false);

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

    /// <summary>Role templates assigned to catalog companies to fill the public board.
    /// The first skill is the primary requirement; weights and minimum years derive from it.</summary>
    private static readonly (string Title, string Description, string[] SkillNames, JobType Type, int SalaryMin, int SalaryMax, int Experience, string Category)[] JobTemplateCatalog =
    [
        ("Senior Backend Engineer", "Design and operate .NET services that handle high-volume traffic.", ["C#", ".NET", "SQL", "Docker"], JobType.FullTime, 1800, 3500, 4, "IT - Software"),
        ("Frontend Engineer (React)", "Build accessible React interfaces for customer-facing products.", ["React", "TypeScript", "CSS", "Git"], JobType.FullTime, 1200, 2400, 2, "IT - Software"),
        ("Full Stack Developer", "Own features end to end across ASP.NET Core APIs and React screens.", ["C#", "React", "SQL", "Git"], JobType.FullTime, 1500, 3000, 3, "IT - Software"),
        ("DevOps Engineer", "Automate delivery pipelines and keep production environments healthy.", ["Docker", "Kubernetes", "AWS", "CI/CD", "Linux"], JobType.FullTime, 1800, 3800, 3, "DevOps & Cloud"),
        ("Data Engineer", "Build batch and streaming pipelines feeding analytics products.", ["Python", "SQL", "Kafka", "AWS"], JobType.FullTime, 1600, 3200, 3, "Data & Analytics"),
        ("Data Scientist", "Turn product and customer data into models and decisions.", ["Python", "Machine Learning", "Pandas", "SQL"], JobType.FullTime, 1800, 3600, 3, "Data & Analytics"),
        ("Machine Learning Engineer", "Train, evaluate and serve ML models in production.", ["Python", "PyTorch", "TensorFlow", "Docker"], JobType.FullTime, 2000, 4000, 3, "Data & Analytics"),
        ("Mobile Engineer (Flutter)", "Ship cross-platform mobile features with a Flutter codebase.", ["Flutter", "Kotlin", "Git"], JobType.FullTime, 1200, 2500, 2, "Mobile"),
        ("iOS Engineer", "Develop and maintain native iOS applications.", ["Swift", "Git", "Agile/Scrum"], JobType.FullTime, 1500, 3000, 3, "Mobile"),
        ("Android Engineer", "Build reliable Android apps used by millions.", ["Kotlin", "Java", "Git"], JobType.FullTime, 1300, 2600, 2, "Mobile"),
        ("QA Automation Engineer", "Own end-to-end browser automation and release quality gates.", ["Playwright", "Selenium", "TypeScript", "CI/CD"], JobType.FullTime, 1100, 2200, 2, "QA & Testing"),
        ("Manual QA Engineer", "Write test plans, verify releases and report defects clearly.", ["Manual Testing", "SQL", "Agile/Scrum"], JobType.FullTime, 700, 1400, 1, "QA & Testing"),
        ("Product Manager", "Shape the roadmap and turn customer problems into shipped outcomes.", ["Product Management", "Agile/Scrum", "Business Analysis", "Communication"], JobType.FullTime, 1800, 3600, 4, "Product"),
        ("UI/UX Designer", "Design flows, wireframes and polished interfaces with the product team.", ["Figma", "UI/UX Design", "Communication"], JobType.FullTime, 1000, 2000, 2, "Design"),
        ("Java Backend Engineer", "Build Spring Boot services and integrate internal systems.", ["Java", "Spring Boot", "SQL", "Docker"], JobType.FullTime, 1600, 3200, 3, "IT - Software"),
        ("Node.js Engineer", "Develop TypeScript services and APIs on Node.js.", ["Node.js", "TypeScript", "PostgreSQL", "Docker"], JobType.FullTime, 1300, 2600, 2, "IT - Software"),
        ("PHP Developer", "Maintain and extend Laravel applications for enterprise clients.", ["PHP", "Laravel", "MySQL", "Git"], JobType.FullTime, 900, 1800, 2, "IT - Software"),
        ("Python Backend Engineer", "Build Django services and data integrations.", ["Python", "Django", "PostgreSQL", "Docker"], JobType.FullTime, 1500, 3000, 3, "IT - Software"),
        ("Cloud Engineer", "Design and operate secure cloud landing zones.", ["AWS", "Azure", "Terraform", "Docker"], JobType.FullTime, 1500, 3000, 2, "DevOps & Cloud"),
        ("Site Reliability Engineer", "Keep critical services fast, observable and available.", ["Kubernetes", "Linux", "CI/CD", "AWS"], JobType.FullTime, 1700, 3400, 3, "DevOps & Cloud"),
        ("Security Engineer", "Harden systems, review architecture and respond to incidents.", ["Cybersecurity", "Linux", "Python", "Communication"], JobType.FullTime, 1600, 3200, 3, "Cybersecurity"),
        ("Business Analyst", "Translate business needs into clear requirements for delivery teams.", ["Business Analysis", "SQL", "Agile/Scrum", "Communication"], JobType.FullTime, 1100, 2200, 2, "Product"),
        ("Digital Marketing Executive", "Run campaigns, track performance and improve acquisition.", ["Digital Marketing", "SEO", "Content Marketing", "Communication"], JobType.FullTime, 700, 1400, 1, "Marketing"),
        ("Graphic Designer", "Produce brand and campaign visuals for digital channels.", ["Figma", "UI/UX Design", "Communication"], JobType.FullTime, 800, 1600, 2, "Design"),
        ("IT Support Engineer", "Troubleshoot hardware, accounts and internal systems.", ["Windows Server", "Linux", "SQL", "Communication"], JobType.FullTime, 600, 1200, 1, "IT Support"),
        ("Technical Lead", "Guide a cross-functional squad and own technical quality.", ["C#", ".NET", "React", "Agile/Scrum", "Communication"], JobType.FullTime, 2500, 5000, 6, "IT - Software"),
    ];

    /// <summary>Searchable candidate profiles with a primary resume so employer sourcing has data.</summary>
    private static readonly (string Name, string Position, string City, (string Skill, int Years)[] Skills)[] CandidateCatalog =
    [
        ("Nguyen Van An", "Backend Engineer", "Ho Chi Minh City", [("C#", 4), ("SQL", 4), ("Docker", 3)]),
        ("Tran Thi Bich", "Frontend Engineer", "Hanoi", [("React", 3), ("TypeScript", 3), ("CSS", 3)]),
        ("Le Minh Quan", "Full Stack Developer", "Da Nang", [("C#", 3), ("React", 2), ("SQL", 3)]),
        ("Pham Thu Ha", "Data Scientist", "Hanoi", [("Python", 3), ("Machine Learning", 2), ("Pandas", 3)]),
        ("Hoang Duc Long", "DevOps Engineer", "Ho Chi Minh City", [("Docker", 4), ("Kubernetes", 3), ("AWS", 3), ("Linux", 5)]),
        ("Vu Ngoc Mai", "QA Automation Engineer", "Hanoi", [("Playwright", 2), ("Selenium", 2), ("TypeScript", 2)]),
        ("Do Quang Huy", "Mobile Engineer", "Da Nang", [("Flutter", 3), ("Kotlin", 2), ("Git", 3)]),
        ("Bui Thanh Truc", "UI/UX Designer", "Ho Chi Minh City", [("Figma", 4), ("UI/UX Design", 4), ("Communication", 5)]),
        ("Dang Hong Nhung", "Product Manager", "Hanoi", [("Product Management", 3), ("Agile/Scrum", 3), ("Business Analysis", 2)]),
        ("Ngo Tuan Kiet", "Java Backend Engineer", "Ho Chi Minh City", [("Java", 4), ("Spring Boot", 3), ("SQL", 3)]),
        ("Dinh Bao Chau", "Digital Marketing Executive", "Ho Chi Minh City", [("Digital Marketing", 3), ("SEO", 2), ("Content Marketing", 3)]),
        ("Cao Minh Tri", "Security Engineer", "Da Nang", [("Cybersecurity", 3), ("Linux", 4), ("Python", 2)]),
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
        ("Zalo", "Software", "Ho Chi Minh City", CompanyScale.MoreThan500, "Messaging and social platform used across Vietnam."),
        ("VNPay", "Fintech", "Hanoi", CompanyScale.From200To499, "Vietnam's leading payment gateway."),
        ("Zalopay", "Fintech", "Ho Chi Minh City", CompanyScale.From200To499, "Digital wallet and financial services super-app."),
        ("Sendo", "E-commerce", "Ho Chi Minh City", CompanyScale.From200To499, "Vietnamese online marketplace for local sellers."),
        ("Teko Vietnam", "E-commerce", "Hanoi", CompanyScale.From200To499, "E-commerce and logistics technology group."),
        ("Silicon Stack", "Software", "Ho Chi Minh City", CompanyScale.From50To199, "Product engineering partner for Australian clients."),
        ("SmartDev", "Software", "Da Nang", CompanyScale.From200To499, "Software development services for global clients."),
        ("Orient Software", "Software", "Ho Chi Minh City", CompanyScale.From200To499, "Outsourced product development and QA."),
        ("Footprint Vietnam", "Software", "Da Nang", CompanyScale.From10To49, "Boutique offshore development team."),
        ("CodeLink", "Software", "Ho Chi Minh City", CompanyScale.From10To49, "Agile web and mobile product studio."),
        ("MService", "Software", "Ho Chi Minh City", CompanyScale.From50To199, "Mobile and web applications for enterprises."),
        ("Kyanon Digital", "Software", "Ho Chi Minh City", CompanyScale.From200To499, "Digital transformation and product engineering."),
        ("HCLTech Vietnam", "Software", "Hanoi", CompanyScale.MoreThan500, "Global technology services with a Hanoi delivery center."),
        ("Endava Vietnam", "Software", "Ho Chi Minh City", CompanyScale.MoreThan500, "Agile software engineering for payments and fintech."),
        ("Global Fashion Group", "E-commerce", "Ho Chi Minh City", CompanyScale.MoreThan500, "Fashion e-commerce technology hub."),
        ("Trusting Social", "AI", "Ho Chi Minh City", CompanyScale.From200To499, "AI credit scoring and financial inclusion."),
        ("VinAI", "AI", "Hanoi", CompanyScale.From200To499, "AI research and applied computer vision."),
        ("Vinova", "Software", "Ho Chi Minh City", CompanyScale.From50To199, "Mobile and web product development company."),
        ("Ahamove", "Logistics", "Ho Chi Minh City", CompanyScale.From200To499, "On-demand delivery platform."),
        ("Loship", "Logistics", "Ho Chi Minh City", CompanyScale.From200To499, "Local food and grocery delivery app."),
        ("MISA", "SaaS", "Hanoi", CompanyScale.MoreThan500, "Business management and accounting software."),
        ("Amaris Consulting", "Software", "Hanoi", CompanyScale.From200To499, "Technology consulting for enterprise clients."),
        ("Netcompany Vietnam", "Software", "Ho Chi Minh City", CompanyScale.MoreThan500, "European IT consultancy's Vietnam hub."),
        ("S3 Corporation", "Software", "Hanoi", CompanyScale.From50To199, "Digital solutions for banking and telecom."),
        ("TMA Solutions", "Software", "Ho Chi Minh City", CompanyScale.MoreThan500, "Long-established Vietnamese software outsourcing company."),
        ("VMO Holdings", "Software", "Hanoi", CompanyScale.From200To499, "Digital product and engineering services group."),
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

    /// <summary>One published role per catalog company so the board, filters and pagination have volume.
    /// Idempotent per (company, title).</summary>
    private static async Task SeedCatalogJobsAsync(
        ApplicationDbContext db,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var catalogNames = CompanyCatalog.Select(entry => entry.Name).ToList();
        var companies = await db.CompanyProfiles
            .Where(company => catalogNames.Contains(company.CompanyName))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (companies.Count == 0)
            return;

        var companyByName = companies.ToDictionary(company => company.CompanyName);
        var companyIds = companies.Select(company => company.Id).ToList();
        var owners = await db.Employers
            .Where(employer => companyIds.Contains(employer.CompanyProfileId) && employer.IsOwner)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var ownerByCompany = owners
            .GroupBy(employer => employer.CompanyProfileId)
            .ToDictionary(group => group.Key, group => group.First().Id);
        var categories = await db.JobCategories
            .ToDictionaryAsync(category => category.Name, category => category.Id, cancellationToken)
            .ConfigureAwait(false);
        var skills = await db.Skills
            .ToDictionaryAsync(skill => skill.Name, skill => skill.Id, cancellationToken)
            .ConfigureAwait(false);

        var seeded = 0;
        for (var i = 0; i < CompanyCatalog.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!companyByName.TryGetValue(CompanyCatalog[i].Name, out var company))
                continue;

            var template = JobTemplateCatalog[i % JobTemplateCatalog.Length];
            var exists = await db.JobPostings.AnyAsync(
                job => job.CompanyProfileId == company.Id && job.Title == template.Title,
                cancellationToken).ConfigureAwait(false);
            if (exists)
                continue;

            var job = new JobPosting
            {
                CompanyProfileId = company.Id,
                CreatedByEmployerId = ownerByCompany.TryGetValue(company.Id, out var employerId) ? employerId : null,
                Title = template.Title,
                Description = $"{template.Description} Join {company.CompanyName} ({company.Location ?? "Vietnam"}).",
                SalaryMin = template.SalaryMin,
                SalaryMax = template.SalaryMax,
                ExperienceRequired = template.Experience,
                JobType = template.Type,
                ExpiryDate = DateTime.UtcNow.AddMonths(3),
                Status = JobStatus.Published,
                CreatedAt = DateTime.UtcNow.AddDays(-((i * 3) % 45)),
                FeaturedUntil = i % 10 == 0 ? DateTime.UtcNow.AddDays(14) : null
            };
            db.JobPostings.Add(job);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (categories.TryGetValue(template.Category, out var categoryId))
            {
                db.JobCategoryMappings.Add(new JobCategoryMapping
                {
                    JobPostingId = job.Id,
                    JobCategoryId = categoryId
                });
            }

            var added = new HashSet<int>();
            for (var s = 0; s < template.SkillNames.Length; s++)
            {
                if (!skills.TryGetValue(template.SkillNames[s], out var skillId) || !added.Add(skillId))
                    continue;

                db.JobSkills.Add(new JobSkill
                {
                    JobPostingId = job.Id,
                    SkillId = skillId,
                    ImportanceWeight = s == 0 ? 10 : s == 1 ? 7 : 4,
                    MinYears = s == 0
                        ? template.Experience
                        : s == 1 ? Math.Max(0, template.Experience - 1) : 0
                });
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            seeded++;
        }

        if (seeded > 0)
            logger.LogInformation("Seeded {Count} catalog jobs.", seeded);
    }

    /// <summary>Creates searchable candidate profiles with primary resumes. Idempotent by email.</summary>
    private static async Task SeedCandidateCatalogAsync(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var skills = await db.Skills
            .ToDictionaryAsync(skill => skill.Name, skill => skill.Id, cancellationToken)
            .ConfigureAwait(false);

        for (var i = 0; i < CandidateCatalog.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = CandidateCatalog[i];
            var user = await EnsureUserAsync(
                userManager,
                $"demo.candidate{i + 1}@example.com",
                "Candidate123!",
                AppRoles.Candidate,
                logger).ConfigureAwait(false);

            var candidate = await db.Candidates
                .FirstOrDefaultAsync(candidate => candidate.UserId == user.Id, cancellationToken)
                .ConfigureAwait(false);
            if (candidate is null)
            {
                candidate = new Candidate
                {
                    UserId = user.Id,
                    FullName = entry.Name,
                    Address = entry.City,
                    Position = entry.Position,
                    IsDiscoverable = true
                };
                db.Candidates.Add(candidate);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            else if (!candidate.IsDiscoverable || candidate.Position != entry.Position)
            {
                candidate.IsDiscoverable = true;
                candidate.Position = entry.Position;
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            var resume = await db.Resumes
                .Include(resume => resume.ResumeSkills)
                .FirstOrDefaultAsync(
                    resume => resume.CandidateId == candidate.Id && resume.IsPrimary,
                    cancellationToken)
                .ConfigureAwait(false);
            if (resume is null)
            {
                var topYears = entry.Skills.Max(skill => skill.Years);
                resume = new Resume
                {
                    CandidateId = candidate.Id,
                    Title = $"{entry.Position} Resume",
                    Summary = $"{topYears} years of hands-on {entry.Position} experience.",
                    IsPrimary = true
                };
                db.Resumes.Add(resume);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            var existing = resume.ResumeSkills.Select(resumeSkill => resumeSkill.SkillId).ToHashSet();
            var added = 0;
            foreach (var (skillName, years) in entry.Skills)
            {
                if (!skills.TryGetValue(skillName, out var skillId) || !existing.Add(skillId))
                    continue;

                db.ResumeSkills.Add(new ResumeSkill
                {
                    ResumeId = resume.Id,
                    SkillId = skillId,
                    YearsOfExperience = years
                });
                added++;
            }

            if (added > 0)
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Two applications per catalog candidate so ATS lists, filters and history have data.</summary>
    private static async Task SeedCandidateApplicationsAsync(
        ApplicationDbContext db,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var names = CandidateCatalog.Select(entry => entry.Name).ToList();
        var candidates = await db.Candidates
            .Where(candidate => names.Contains(candidate.FullName))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (candidates.Count == 0)
            return;

        var candidateByName = candidates
            .GroupBy(candidate => candidate.FullName)
            .ToDictionary(group => group.Key, group => group.First());
        var jobIds = await db.JobPostings
            .Where(job => job.Status == JobStatus.Published)
            .OrderBy(job => job.Id)
            .Select(job => job.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (jobIds.Count == 0)
            return;

        ApplicationStatus[] statuses =
        [
            ApplicationStatus.Pending,
            ApplicationStatus.Viewed,
            ApplicationStatus.Shortlisted,
            ApplicationStatus.Interviewing,
            ApplicationStatus.Rejected
        ];

        for (var i = 0; i < CandidateCatalog.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!candidateByName.TryGetValue(CandidateCatalog[i].Name, out var candidate))
                continue;

            var resumeId = await db.Resumes
                .Where(resume => resume.CandidateId == candidate.Id)
                .OrderByDescending(resume => resume.IsPrimary)
                .Select(resume => (int?)resume.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (resumeId is null)
                continue;

            for (var k = 0; k < 2; k++)
            {
                var jobId = jobIds[(i * 7 + k * 3) % jobIds.Count];
                var exists = await db.Applications.AnyAsync(
                    application => application.JobPostingId == jobId && application.CandidateId == candidate.Id,
                    cancellationToken).ConfigureAwait(false);
                if (exists)
                    continue;

                var status = statuses[(i + k) % statuses.Length];
                var appliedAt = DateTime.UtcNow.AddDays(-(i + k * 2 + 1));
                var application = new Models.Application.Application
                {
                    JobPostingId = jobId,
                    ResumeId = resumeId.Value,
                    CandidateId = candidate.Id,
                    CoverLetter = $"I am interested in this role and bring {CandidateCatalog[i].Position} experience.",
                    CurrentStatus = status,
                    SnapshotSummary = $"{CandidateCatalog[i].Position} based in {CandidateCatalog[i].City}.",
                    AppliedAt = appliedAt
                };
                db.Applications.Add(application);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                db.ApplicationStatusHistories.Add(new ApplicationStatusHistory
                {
                    ApplicationId = application.Id,
                    Status = ApplicationStatus.Pending,
                    Notes = "Applied via demo seed",
                    ChangedAt = appliedAt
                });
                if (status != ApplicationStatus.Pending)
                {
                    db.ApplicationStatusHistories.Add(new ApplicationStatusHistory
                    {
                        ApplicationId = application.Id,
                        Status = status,
                        Notes = "Updated by demo seed",
                        ChangedAt = DateTime.UtcNow.AddDays(-k)
                    });
                }

                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        logger.LogInformation("Seeded catalog candidate applications.");
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

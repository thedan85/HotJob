using hotjob.Models;
using hotjob.Models.Billing;
using hotjob.Models.Application;
using hotjob.Models.Job;
using hotjob.Models.ML;
using hotjob.Models.Resume;
using hotjob.Models.Users;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Data;

/// <summary>EF Core context for the HotJob domain, including Identity.</summary>
/// <param name="options">DbContext options (connection string, provider).</param>
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Candidate> Candidates => Set<Candidate>();
    public DbSet<Employer> Employers => Set<Employer>();
    public DbSet<CompanyProfile> CompanyProfiles => Set<CompanyProfile>();
    public DbSet<Resume> Resumes => Set<Resume>();
    public DbSet<Education> Educations => Set<Education>();
    public DbSet<WorkExperience> WorkExperiences => Set<WorkExperience>();
    public DbSet<ResumeSkill> ResumeSkills => Set<ResumeSkill>();
    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<JobPosting> JobPostings => Set<JobPosting>();
    public DbSet<JobCategory> JobCategories => Set<JobCategory>();
    public DbSet<JobCategoryMapping> JobCategoryMappings => Set<JobCategoryMapping>();
    public DbSet<JobSkill> JobSkills => Set<JobSkill>();
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<ApplicationStatusHistory> ApplicationStatusHistories => Set<ApplicationStatusHistory>();
    public DbSet<Interview> Interviews => Set<Interview>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<SavedJob> SavedJobs => Set<SavedJob>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<MatchScore> MatchScores => Set<MatchScore>();
    public DbSet<ModelTrainingLog> ModelTrainingLogs => Set<ModelTrainingLog>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Payment> Payments => Set<Payment>();

    /// <summary>Configures relationships, delete behaviors, indexes and column precision.</summary>
    /// <param name="modelBuilder">Fluent API builder.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationUser>()
            .HasMany(x => x.Notifications).WithOne(x => x.User).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Candidate>().HasOne(x => x.User).WithOne(x => x.Candidate).HasForeignKey<Candidate>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Candidate>().HasIndex(x => x.UserId).IsUnique();

        modelBuilder.Entity<Employer>().HasOne(x => x.User).WithOne(x => x.Employer).HasForeignKey<Employer>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Employer>().HasIndex(x => x.UserId).IsUnique();
        // ponytail: Restrict (not Cascade) so deleting a company never deletes Identity users.
        modelBuilder.Entity<Employer>().HasOne(x => x.Company).WithMany(x => x.Employers).HasForeignKey(x => x.CompanyProfileId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CompanyProfile>()
            .HasOne(x => x.VerifiedBy).WithMany().HasForeignKey(x => x.VerifiedByAdminId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<CompanyProfile>()
            .HasIndex(x => x.CompanyName).IsUnique();

        modelBuilder.Entity<Resume>().HasOne(x => x.Candidate).WithMany(x => x.Resumes).HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Education>().HasOne(x => x.Resume).WithMany(x => x.Educations).HasForeignKey(x => x.ResumeId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<WorkExperience>().HasOne(x => x.Resume).WithMany(x => x.WorkExperiences).HasForeignKey(x => x.ResumeId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ResumeSkill>().HasKey(x => new { x.ResumeId, x.SkillId });
        modelBuilder.Entity<ResumeSkill>().HasOne(x => x.Resume).WithMany(x => x.ResumeSkills).HasForeignKey(x => x.ResumeId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ResumeSkill>().HasOne(x => x.Skill).WithMany(x => x.ResumeSkills).HasForeignKey(x => x.SkillId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Skill>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<JobCategory>().HasIndex(x => x.Name).IsUnique();

        modelBuilder.Entity<JobPosting>().HasOne(x => x.Company).WithMany(x => x.JobPostings).HasForeignKey(x => x.CompanyProfileId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<JobPosting>().HasOne(x => x.CreatedBy).WithMany(x => x.CreatedJobs).HasForeignKey(x => x.CreatedByEmployerId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<JobPosting>().Property(x => x.SalaryMin).HasPrecision(18, 2);
        modelBuilder.Entity<JobPosting>().Property(x => x.SalaryMax).HasPrecision(18, 2);

        modelBuilder.Entity<JobCategoryMapping>().HasKey(x => new { x.JobPostingId, x.JobCategoryId });
        modelBuilder.Entity<JobCategoryMapping>().HasOne(x => x.JobPosting).WithMany(x => x.CategoryMappings).HasForeignKey(x => x.JobPostingId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<JobCategoryMapping>().HasOne(x => x.Category).WithMany(x => x.Mappings).HasForeignKey(x => x.JobCategoryId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<JobSkill>().HasKey(x => new { x.JobPostingId, x.SkillId });
        modelBuilder.Entity<JobSkill>().HasOne(x => x.JobPosting).WithMany(x => x.JobSkills).HasForeignKey(x => x.JobPostingId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<JobSkill>().HasOne(x => x.Skill).WithMany(x => x.JobSkills).HasForeignKey(x => x.SkillId).OnDelete(DeleteBehavior.Cascade);

        // ponytail: unique per (Job, Candidate) — same candidate re-applying with another resume is still one application.
        modelBuilder.Entity<Application>().HasOne(x => x.JobPosting).WithMany(x => x.Applications).HasForeignKey(x => x.JobPostingId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Application>().HasOne(x => x.Resume).WithMany(x => x.Applications).HasForeignKey(x => x.ResumeId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Application>().HasOne(x => x.Candidate).WithMany(x => x.Applications).HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Application>().HasIndex(x => new { x.JobPostingId, x.CandidateId }).IsUnique();

        modelBuilder.Entity<ApplicationStatusHistory>().HasOne(x => x.Application).WithMany(x => x.StatusHistory).HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ApplicationStatusHistory>().HasOne(x => x.ChangedBy).WithMany().HasForeignKey(x => x.ChangedByUserId).OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Interview>().HasOne(x => x.Application).WithMany(x => x.Interviews).HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Review>().HasOne(x => x.Company).WithMany(x => x.Reviews).HasForeignKey(x => x.CompanyProfileId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Review>().HasOne(x => x.Candidate).WithMany(x => x.Reviews).HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Review>().HasIndex(x => new { x.CompanyProfileId, x.CandidateId }).IsUnique();

        modelBuilder.Entity<SavedJob>().HasKey(x => new { x.CandidateId, x.JobPostingId });
        modelBuilder.Entity<SavedJob>().HasOne(x => x.Candidate).WithMany(x => x.SavedJobs).HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SavedJob>().HasOne(x => x.JobPosting).WithMany(x => x.SavedBy).HasForeignKey(x => x.JobPostingId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Notification>().HasIndex(x => new { x.UserId, x.IsRead });
        modelBuilder.Entity<Notification>().HasIndex(x => new { x.UserId, x.Kind, x.CreatedAt });
        // ponytail: MySQL and SQLite both treat NULLs as distinct in a unique index, so the
        // many notifications without a related job are unaffected. Job alerts have both
        // values set, making (candidate, job) unique; the service also checks first.
        modelBuilder.Entity<Notification>()
            .HasIndex(x => new { x.UserId, x.Kind, x.RelatedJobPostingId })
            .IsUnique();
        modelBuilder.Entity<Notification>()
            .HasOne(x => x.RelatedJob).WithMany().HasForeignKey(x => x.RelatedJobPostingId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<MatchScore>().HasKey(x => new { x.JobPostingId, x.ResumeId });
        modelBuilder.Entity<MatchScore>().HasOne(x => x.JobPosting).WithMany(x => x.MatchScores).HasForeignKey(x => x.JobPostingId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<MatchScore>().HasOne(x => x.Resume).WithMany(x => x.MatchScores).HasForeignKey(x => x.ResumeId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ModelTrainingLog>().HasOne(x => x.TrainedBy).WithMany().HasForeignKey(x => x.TrainedByUserId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<ModelTrainingLog>().HasIndex(x => new { x.IsActive, x.TrainedAt });

        modelBuilder.Entity<RefreshToken>()
            .HasOne(x => x.User).WithMany(x => x.RefreshTokens).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<RefreshToken>().HasIndex(x => x.TokenHash).IsUnique();
        modelBuilder.Entity<RefreshToken>().HasIndex(x => new { x.UserId, x.RevokedAtUtc });

        ConfigureBilling(modelBuilder);
    }

    // ponytail: every Payment FK is Restrict or SetNull — deleting a company/user/plan must not erase billing history,
    // and deleting a job only detaches it (SetNull) so jobs stay deletable.
    private static void ConfigureBilling(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Plan>(plan =>
        {
            plan.HasIndex(x => x.Code).IsUnique();
            plan.Property(x => x.Price).HasPrecision(18, 0);
        });

        modelBuilder.Entity<CompanyProfile>()
            .HasOne(x => x.Plan).WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Candidate>()
            .HasOne(x => x.PremiumPlan).WithMany().HasForeignKey(x => x.PremiumPlanId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<JobPosting>().HasIndex(x => x.FeaturedUntil);

        modelBuilder.Entity<Payment>(payment =>
        {
            payment.Property(x => x.Amount).HasPrecision(18, 0);
            payment.HasIndex(x => x.VnpTxnRef).IsUnique();
            payment.HasIndex(x => new { x.Status, x.PaidAt });
            payment.HasIndex(x => x.CompanyProfileId);
            payment.HasIndex(x => x.CandidateId);
            payment.HasIndex(x => x.PayerUserId);
            payment.HasIndex(x => x.JobPostingId);

            // ponytail: Status is a concurrency token so a SaveChanges "Pending -> Paid" claim becomes
            // UPDATE ... WHERE Id = @id AND Status = @original; a second concurrent delivery loses (contract D5).
            payment.Property(x => x.Status).IsConcurrencyToken();

            payment.HasOne(x => x.PayerUser).WithMany().HasForeignKey(x => x.PayerUserId).OnDelete(DeleteBehavior.Restrict);
            payment.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyProfileId).OnDelete(DeleteBehavior.Restrict);
            payment.HasOne(x => x.Candidate).WithMany().HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Restrict);
            payment.HasOne(x => x.Plan).WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
            payment.HasOne(x => x.JobPosting).WithMany().HasForeignKey(x => x.JobPostingId).OnDelete(DeleteBehavior.SetNull);
        });
    }
}

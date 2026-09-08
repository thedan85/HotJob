using hotjob.Models;
using hotjob.Models.Application;
using hotjob.Models.Job;
using hotjob.Models.ML;
using hotjob.Models.Resume;
using hotjob.Models.Users;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Data;

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

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Candidate>().HasOne(x => x.User).WithOne(x => x.Candidate).HasForeignKey<Candidate>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Candidate>().HasIndex(x => x.UserId).IsUnique();

        b.Entity<Employer>().HasOne(x => x.User).WithOne(x => x.Employer).HasForeignKey<Employer>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Employer>().HasIndex(x => x.UserId).IsUnique();
        b.Entity<Employer>().HasOne(x => x.Company).WithMany(x => x.Employers).HasForeignKey(x => x.CompanyProfileId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Resume>().HasOne(x => x.Candidate).WithMany(x => x.Resumes).HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Education>().HasOne(x => x.Resume).WithMany(x => x.Educations).HasForeignKey(x => x.ResumeId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<WorkExperience>().HasOne(x => x.Resume).WithMany(x => x.WorkExperiences).HasForeignKey(x => x.ResumeId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<ResumeSkill>().HasKey(x => new { x.ResumeId, x.SkillId });
        b.Entity<ResumeSkill>().HasOne(x => x.Resume).WithMany(x => x.ResumeSkills).HasForeignKey(x => x.ResumeId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ResumeSkill>().HasOne(x => x.Skill).WithMany(x => x.ResumeSkills).HasForeignKey(x => x.SkillId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<JobPosting>().HasOne(x => x.Company).WithMany(x => x.JobPostings).HasForeignKey(x => x.CompanyProfileId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<JobPosting>().HasOne(x => x.CreatedBy).WithMany(x => x.CreatedJobs).HasForeignKey(x => x.CreatedByEmployerId).OnDelete(DeleteBehavior.SetNull);

        b.Entity<JobCategoryMapping>().HasKey(x => new { x.JobPostingId, x.JobCategoryId });
        b.Entity<JobCategoryMapping>().HasOne(x => x.JobPosting).WithMany(x => x.CategoryMappings).HasForeignKey(x => x.JobPostingId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<JobCategoryMapping>().HasOne(x => x.Category).WithMany(x => x.Mappings).HasForeignKey(x => x.JobCategoryId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<JobSkill>().HasKey(x => new { x.JobPostingId, x.SkillId });
        b.Entity<JobSkill>().HasOne(x => x.JobPosting).WithMany(x => x.JobSkills).HasForeignKey(x => x.JobPostingId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<JobSkill>().HasOne(x => x.Skill).WithMany(x => x.JobSkills).HasForeignKey(x => x.SkillId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Application>().HasOne(x => x.JobPosting).WithMany(x => x.Applications).HasForeignKey(x => x.JobPostingId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Application>().HasOne(x => x.Resume).WithMany(x => x.Applications).HasForeignKey(x => x.ResumeId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Application>().HasOne(x => x.Candidate).WithMany().HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Application>().HasIndex(x => new { x.JobPostingId, x.ResumeId }).IsUnique();

        b.Entity<ApplicationStatusHistory>().HasOne(x => x.Application).WithMany(x => x.StatusHistory).HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ApplicationStatusHistory>().HasOne(x => x.ChangedBy).WithMany().HasForeignKey(x => x.ChangedByUserId).OnDelete(DeleteBehavior.SetNull);

        b.Entity<Interview>().HasOne(x => x.Application).WithMany(x => x.Interviews).HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Review>().HasOne(x => x.Company).WithMany(x => x.Reviews).HasForeignKey(x => x.CompanyProfileId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Review>().HasOne(x => x.Candidate).WithMany(x => x.Reviews).HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Review>().HasIndex(x => new { x.CompanyProfileId, x.CandidateId }).IsUnique();

        b.Entity<SavedJob>().HasKey(x => new { x.CandidateId, x.JobPostingId });
        b.Entity<SavedJob>().HasOne(x => x.Candidate).WithMany(x => x.SavedJobs).HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<SavedJob>().HasOne(x => x.JobPosting).WithMany(x => x.SavedBy).HasForeignKey(x => x.JobPostingId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Notification>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<MatchScore>().HasKey(x => new { x.JobPostingId, x.ResumeId });
        b.Entity<MatchScore>().HasOne(x => x.JobPosting).WithMany(x => x.MatchScores).HasForeignKey(x => x.JobPostingId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<MatchScore>().HasOne(x => x.Resume).WithMany().HasForeignKey(x => x.ResumeId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<ModelTrainingLog>().HasOne(x => x.TrainedBy).WithMany().HasForeignKey(x => x.TrainedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

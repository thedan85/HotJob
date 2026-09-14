# HotJob — Database Design (ASP.NET Identity + ATS Job Board)

7 bảng Identity (`AspNetUsers`...`AspNetRoleClaims` do `IdentityDbContext<ApplicationUser>` sinh) + 20 bảng domain = **27 bảng**.

## Entities (20 domain)

| # | Entity | File | FK chính |
|---|--------|------|----------|
| 1 | `Candidate` | Models/Users/Candidate.cs | UserId → AspNetUsers (1-1) |
| 2 | `Employer` | Models/Users/Employer.cs | UserId → AspNetUsers (1-1), CompanyProfileId → CompanyProfiles (n-1), IsOwner, MembershipStatus Pending/Active/Rejected |
| 3 | `CompanyProfile` | Models/Users/CompanyProfile.cs | Status Pending/Approved/Rejected, VerifiedAt, VerifiedByAdminId, RejectionReason |
| 4 | `Resume` | Models/Resume/Resume.cs | CandidateId → Candidates |
| 5 | `Education` | Models/Resume/Education.cs | ResumeId → Resumes (cascade) |
| 6 | `WorkExperience` | Models/Resume/WorkExperience.cs | ResumeId → Resumes (cascade) |
| 7 | `Skill` | Models/Skill.cs | dùng chung |
| 8 | `ResumeSkill` | Models/Resume/ResumeSkill.cs | PK(ResumeId, SkillId), YearsOfExperience |
| 9 | `JobPosting` | Models/Job/JobPosting.cs | CompanyProfileId, CreatedByEmployerId (SetNull), Status Draft/Published/Closed/Expired, ExpiryDate |
| 10 | `JobCategory` | Models/Job/JobCategory.cs | master |
| 11 | `JobCategoryMapping` | Models/Job/JobCategoryMapping.cs | PK(JobPostingId, JobCategoryId), n-n |
| 12 | `JobSkill` | Models/Job/JobSkill.cs | PK(JobPostingId, SkillId), ImportanceWeight |
| 13 | `Application` | Models/Application/Application.cs | JobPostingId, ResumeId (Restrict), CandidateId, CurrentStatus, SnapshotFileUrl + SnapshotSummary (freeze), unique(JobPostingId, ResumeId) |
| 14 | `ApplicationStatusHistory` | Models/Application/ApplicationStatusHistory.cs | ApplicationId (cascade), ChangedByUserId → AspNetUsers (SetNull) — audit ATS |
| 15 | `Interview` | Models/Application/Interview.cs | ApplicationId (cascade) |
| 16 | `Review` | Models/Review.cs | CompanyProfileId, CandidateId, unique(Company, Candidate) |
| 17 | `SavedJob` | Models/SavedJob.cs | PK(CandidateId, JobPostingId) |
| 18 | `Notification` | Models/Notification.cs | UserId → AspNetUsers |
| 19 | `MatchScore` | Models/ML/MatchScore.cs | PK(JobPostingId, ResumeId) |
| 20 | `ModelTrainingLog` | Models/ML/ModelTrainingLog.cs | TrainedByUserId → AspNetUsers (SetNull) |

ATS = Applicant Tracking System: hệ theo dõi tuyển dụng Job → Apply → Viewed → Shortlisted → Interview → Offer/Reject.

## Flows

1. **Auth (Identity):** Register → role Candidate (tạo `Candidates`). Become Employer → tạo `Employers` + gán role Employer (1 user giữ cả 2 profile). Admin seed sẵn. `Candidates.UserId` / `Employers.UserId` FK string → AspNetUsers.Id.
2. **Company verify:** Employer (từ Become Employer) tạo CompanyProfiles(Status=Pending) → Admin Approve/Rejected(+RejectionReason) → chỉ Approved mới Post Job. Member xin join Employers(MembershipStatus=Pending) → Owner duyệt Active.
3. **Apply:** check Job Published + ExpiryDate > Now → copy file CV → Applications.SnapshotFileUrl + SnapshotSummary (freeze, HR xem bản lúc nộp) → unique(Job, Resume) chống trùng.
4. **ATS state machine:** Pending → Viewed → Shortlisted → Interviewing → Offered/Rejected; Candidate chỉ Withdrawn (khi chưa Offered); Employer chỉ Job thuộc Company mình; Admin all. Mỗi chuyển insert ApplicationStatusHistory.
5. **Close/Expire:** Close hoặc ExpiryDate qua → apps đang Pending/Viewed/Shortlisted/Interviewing mass chuyển Rejected, Notes="Job closed". Hết hạn không cho apply mới.
6. **Ownership:** mọi query lọc theo CandidateId / CompanyProfileId. ML MatchScore tính từ ResumeSkills ↔ JobSkills.

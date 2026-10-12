-- HotJob example data: one row per table, showing how data flows between tables.
-- Run AFTER project.sql on a fresh DB: mysql -u root -p hotjob < project.seed.example.sql
--
-- DATA FLOW (follow the IDs):
--   1. AspNetRoles + AspNetUsers + AspNetUserRoles ......... identity: who can log in, and which role they have
--   2. AspNetUsers -> Candidates ........................... user 'user-candidate-001' gets candidate profile #1 (1-to-1 via UserId)
--      AspNetUsers -> Employers ............................ user 'user-employer-001' gets employer profile #1 (1-to-1 via UserId)
--   3. Employers -> CompanyProfiles ....................... employer #1 belongs to company #1 (N-to-1, RESTRICT on delete)
--      CompanyProfiles -> AspNetUsers ..................... company #1 verified by admin 'user-admin-001' (SET NULL on delete)
--   4. Candidates -> Resumes -> Educations / WorkExperiences  candidate #1 owns resume #1, which owns its education/experience rows (CASCADE)
--      Resumes + Skills -> ResumeSkills ................... resume #1 lists skills #1 (C#) and #2 (SQL), join table PK (ResumeId, SkillId)
--   5. CompanyProfiles + Employers -> JobPostings ......... company #1 publishes job #1, authored by employer #1 (SET NULL if author leaves)
--      JobPostings + JobCategories -> JobCategoryMappings . job #1 filed under category #1, PK (JobPostingId, JobCategoryId)
--      JobPostings + Skills -> JobSkills ................. job #1 requires skills #1 and #2 with weights
--   6. Candidates + Resumes + JobPostings -> Applications . candidate #1 applies to job #1 with resume #1.
--      UNIQUE (JobPostingId, CandidateId): one application per candidate per job (CASCADE from job, RESTRICT from resume/candidate)
--      Applications -> ApplicationStatusHistories ......... every status change is appended here (row 1: Pending by system, row 2: Viewed by employer)
--      Applications -> Interviews ........................ application #1 gets interview #1 (CASCADE)
--   7. Candidates + JobPostings -> SavedJobs .............. candidate #1 bookmarks job #1, PK (CandidateId, JobPostingId)
--      JobPostings + Resumes -> MatchScores .............. ML score between job #1 and resume #1, PK (JobPostingId, ResumeId)
--   8. Candidates + CompanyProfiles -> Reviews ............ candidate #1 reviews company #1, UNIQUE (CompanyProfileId, CandidateId)
--   9. AspNetUsers -> Notifications / RefreshTokens ...... user-scoped rows keyed by UserId (CASCADE)
--      AspNetUsers -> ModelTrainingLogs .................. admin 'user-admin-001' trained model run #1 (SET NULL)
--
-- Enum values used below (stored as INT):
--   MembershipStatus Active=1; CompanyStatus Approved=1; CompanyScale From50To199=2;
--   JobType FullTime=0; JobStatus Published=1;
--   ApplicationStatus Pending=0, Viewed=1; InterviewStatus Scheduled=0; InterviewType Online=0.

USE `hotjob`;

-- ---------------------------------------------------------------------------
-- 1. Identity: roles, users, membership
-- ---------------------------------------------------------------------------

-- EXAMPLE: the three roles the seeders create (IdentitySeeder.AppRoles).
-- FLOWS TO: AspNetUserRoles, AspNetRoleClaims.
INSERT INTO `AspNetRoles` (`Id`, `Name`, `NormalizedName`, `ConcurrencyStamp`) VALUES
  ('role-admin',     'Admin',     'ADMIN',     'seed-concurrency-admin'),
  ('role-candidate', 'Candidate', 'CANDIDATE', 'seed-concurrency-candidate'),
  ('role-employer',  'Employer',  'EMPLOYER',  'seed-concurrency-employer');

-- EXAMPLE: one admin (verifies companies, trains models), one employer, one candidate.
-- PasswordHash is NULL here: the real app writes an Identity password hash on registration;
-- SecurityStamp/ConcurrencyStamp are random per user in production.
-- FLOWS TO: Candidates (1-to-1), Employers (1-to-1), CompanyProfiles.VerifiedByAdminId,
--   ApplicationStatusHistories.ChangedByUserId, ModelTrainingLogs.TrainedByUserId,
--   Notifications, RefreshTokens.
INSERT INTO `AspNetUsers`
  (`Id`, `CreatedAt`, `IsActive`, `UserName`, `NormalizedUserName`, `Email`, `NormalizedEmail`,
   `EmailConfirmed`, `PasswordHash`, `SecurityStamp`, `ConcurrencyStamp`,
   `PhoneNumber`, `PhoneNumberConfirmed`, `TwoFactorEnabled`, `LockoutEnd`, `LockoutEnabled`, `AccessFailedCount`) VALUES
  ('user-admin-001',    '2026-10-01 08:00:00.000000', 1, 'admin1@example.com',    'ADMIN1@EXAMPLE.COM',    'admin1@example.com',    'ADMIN1@EXAMPLE.COM',    1, NULL, 'security-stamp-admin',    'concurrency-admin',    NULL, 0, 0, NULL, 0, 0),
  ('user-employer-001', '2026-10-02 08:00:00.000000', 1, 'employer@example.com',  'EMPLOYER@EXAMPLE.COM',  'employer@example.com',  'EMPLOYER@EXAMPLE.COM',  1, NULL, 'security-stamp-employer', 'concurrency-employer', NULL, 0, 0, NULL, 0, 0),
  ('user-candidate-001','2026-10-03 08:00:00.000000', 1, 'candidate@example.com', 'CANDIDATE@EXAMPLE.COM', 'candidate@example.com', 'CANDIDATE@EXAMPLE.COM', 1, NULL, 'security-stamp-candidate','concurrency-candidate',NULL, 0, 0, NULL, 0, 0);

-- EXAMPLE: which user holds which role.
-- FLOWS FROM: AspNetRoles + AspNetUsers.
INSERT INTO `AspNetUserRoles` (`UserId`, `RoleId`) VALUES
  ('user-admin-001',    'role-admin'),
  ('user-employer-001', 'role-employer'),
  ('user-candidate-001','role-candidate');

-- EXAMPLE: extra claim on the admin role (e.g. permission granted to the whole role).
-- Normally empty unless the app adds role claims. FLOWS FROM: AspNetRoles.
INSERT INTO `AspNetRoleClaims` (`Id`, `RoleId`, `ClaimType`, `ClaimValue`) VALUES
  (1, 'role-admin', 'permission', 'companies.verify');

-- EXAMPLE: extra claim on the candidate user. Normally empty. FLOWS FROM: AspNetUsers.
INSERT INTO `AspNetUserClaims` (`Id`, `UserId`, `ClaimType`, `ClaimValue`) VALUES
  (1, 'user-candidate-001', 'FullName', 'Demo Candidate');

-- EXAMPLE: candidate linked a Google login. Only used with external providers; empty for password-only users.
-- FLOWS FROM: AspNetUsers.
INSERT INTO `AspNetUserLogins` (`LoginProvider`, `ProviderKey`, `ProviderDisplayName`, `UserId`) VALUES
  ('Google', 'google-sub-12345', 'Google', 'user-candidate-001');

-- EXAMPLE: authenticator key for the employer user. Only used when 2FA/external tokens exist.
-- FLOWS FROM: AspNetUsers.
INSERT INTO `AspNetUserTokens` (`UserId`, `LoginProvider`, `Name`, `Value`) VALUES
  ('user-employer-001', 'HotJobApp', 'AuthenticatorKey', 'example-authenticator-key');

-- ---------------------------------------------------------------------------
-- 2. Reference data: skills + categories (DatabaseSeeder values)
-- ---------------------------------------------------------------------------

-- EXAMPLE: canonical skills shared by resumes and jobs. FLOWS TO: ResumeSkills, JobSkills.
INSERT INTO `Skills` (`Id`, `Name`, `Category`) VALUES
  (1, 'C#',    'Backend'),
  (2, 'SQL',   'Database'),
  (3, 'React', 'Frontend');

-- EXAMPLE: job categories. FLOWS TO: JobCategoryMappings.
INSERT INTO `JobCategories` (`Id`, `Name`, `Description`) VALUES
  (1, 'IT - Software', 'Software engineering roles'),
  (2, 'Design',        'UI/UX and product design roles');

-- ---------------------------------------------------------------------------
-- 3. Profiles: candidate, company, employer
-- ---------------------------------------------------------------------------

-- EXAMPLE: candidate profile for 'user-candidate-001'.
-- FLOWS FROM: AspNetUsers. FLOWS TO: Resumes, Applications, SavedJobs, Reviews.
INSERT INTO `Candidates`
  (`Id`, `UserId`, `FullName`, `DateOfBirth`, `Gender`, `Address`, `AvatarUrl`, `GithubUrl`, `LinkedInUrl`,
   `Position`, `Summary`, `JobSeekingStatus`, `PreferredWorkModel`, `PreferredLocation`) VALUES
  (1, 'user-candidate-001', 'Demo Candidate', '1998-05-20', NULL,
   'Ho Chi Minh City', NULL, 'https://github.com/demo-candidate', 'https://linkedin.com/in/demo-candidate',
   'Backend Developer', '3 years building .NET APIs and SQL databases.',
   'OpenToWork', 'Hybrid', 'Ho Chi Minh City');

-- EXAMPLE: company verified by the admin.
-- FLOWS FROM: AspNetUsers (VerifiedByAdminId). FLOWS TO: Employers, JobPostings, Reviews.
INSERT INTO `CompanyProfiles`
  (`Id`, `CompanyName`, `Website`, `LogoUrl`, `Location`, `Industry`, `Scale`, `Description`,
   `Status`, `RejectionReason`, `VerifiedAt`, `VerifiedByAdminId`) VALUES
  (1, 'Acme Corp', 'https://acme.example.com', NULL, 'Ho Chi Minh City', 'Software', 2,
   'Demo company for local Swagger testing.', 1, NULL, '2026-10-04 09:00:00.000000', 'user-admin-001');

-- EXAMPLE: employer profile = membership of 'user-employer-001' inside company #1 as owner.
-- FLOWS FROM: AspNetUsers + CompanyProfiles. FLOWS TO: JobPostings.CreatedByEmployerId.
INSERT INTO `Employers`
  (`Id`, `UserId`, `CompanyProfileId`, `FullName`, `Position`, `IsOwner`, `MembershipStatus`, `JoinedAt`) VALUES
  (1, 'user-employer-001', 1, 'Demo Employer', 'HR Manager', 1, 1, '2026-10-04 09:30:00.000000');

-- ---------------------------------------------------------------------------
-- 4. Resume and its children
-- ---------------------------------------------------------------------------

-- EXAMPLE: candidate #1's primary resume. FLOWS FROM: Candidates.
-- FLOWS TO: Educations, WorkExperiences, ResumeSkills, Applications, MatchScores.
INSERT INTO `Resumes` (`Id`, `CandidateId`, `Title`, `Summary`, `FileUrl`, `IsPrimary`, `CreatedAt`) VALUES
  (1, 1, 'Backend .NET Resume', '3 years building .NET APIs and SQL databases.', NULL, 1, '2026-10-05 10:00:00.000000');

-- EXAMPLE: school history on resume #1. FLOWS FROM: Resumes (CASCADE).
INSERT INTO `Educations` (`Id`, `ResumeId`, `SchoolName`, `Major`, `StartDate`, `EndDate`, `Gpa`, `Description`) VALUES
  (1, 1, 'Demo University', 'Software Engineering', '2018-09-01', '2022-06-30', 3.5, 'Graduated with honors.');

-- EXAMPLE: work history on resume #1. FLOWS FROM: Resumes (CASCADE).
INSERT INTO `WorkExperiences` (`Id`, `ResumeId`, `CompanyName`, `JobTitle`, `StartDate`, `EndDate`, `Description`) VALUES
  (1, 1, 'Demo Soft', '.NET Developer', '2022-07-01', NULL, 'Built REST APIs with ASP.NET Core and MySQL.');

-- EXAMPLE: resume #1 claims C# (3 yrs) and SQL (3 yrs). FLOWS FROM: Resumes + Skills.
INSERT INTO `ResumeSkills` (`ResumeId`, `SkillId`, `YearsOfExperience`) VALUES
  (1, 1, 3),
  (1, 2, 3);

-- ---------------------------------------------------------------------------
-- 5. Job posting and its children
-- ---------------------------------------------------------------------------

-- EXAMPLE: company #1 publishes a job, authored by employer #1.
-- FLOWS FROM: CompanyProfiles + Employers. FLOWS TO: JobCategoryMappings, JobSkills, Applications, SavedJobs, MatchScores.
INSERT INTO `JobPostings`
  (`Id`, `CompanyProfileId`, `CreatedByEmployerId`, `Title`, `Description`,
   `SalaryMin`, `SalaryMax`, `ExperienceRequired`, `JobType`, `ExpiryDate`, `Status`, `CreatedAt`) VALUES
  (1, 1, 1, 'Senior .NET Developer', 'Demo posting for Swagger testing: build and maintain .NET APIs.',
   1500.00, 3000.00, 3, 0, '2027-01-12 08:00:00.000000', 1, '2026-10-06 11:00:00.000000');

-- EXAMPLE: job #1 filed under "IT - Software". FLOWS FROM: JobPostings + JobCategories.
INSERT INTO `JobCategoryMappings` (`JobPostingId`, `JobCategoryId`) VALUES
  (1, 1);

-- EXAMPLE: job #1 requires C# (weight 8) and SQL (weight 6). FLOWS FROM: JobPostings + Skills.
INSERT INTO `JobSkills` (`JobPostingId`, `SkillId`, `ImportanceWeight`) VALUES
  (1, 1, 8),
  (1, 2, 6);

-- ---------------------------------------------------------------------------
-- 6. Application flow: apply -> status history -> interview
-- ---------------------------------------------------------------------------

-- EXAMPLE: candidate #1 applies to job #1 with resume #1.
-- UNIQUE (JobPostingId, CandidateId) means re-applying with another resume is still one row.
-- FLOWS FROM: JobPostings (CASCADE) + Resumes (RESTRICT) + Candidates (RESTRICT).
-- FLOWS TO: ApplicationStatusHistories, Interviews.
INSERT INTO `Applications`
  (`Id`, `JobPostingId`, `ResumeId`, `CandidateId`, `CoverLetter`, `CurrentStatus`,
   `SnapshotFileUrl`, `SnapshotSummary`, `AppliedAt`) VALUES
  (1, 1, 1, 1, 'Demo application for Swagger testing.', 1, NULL,
   '3 years .NET + SQL.', '2026-10-07 12:00:00.000000');

-- EXAMPLE: status trail for application #1: system records Pending at apply time,
-- then the employer marks it Viewed. FLOWS FROM: Applications (CASCADE) + AspNetUsers (SET NULL).
INSERT INTO `ApplicationStatusHistories`
  (`Id`, `ApplicationId`, `Status`, `ChangedByUserId`, `Notes`, `ChangedAt`) VALUES
  (1, 1, 0, NULL,               'Applied via demo seed', '2026-10-07 12:00:00.000000'),
  (2, 1, 1, 'user-employer-001','CV opened by HR',       '2026-10-08 09:00:00.000000');

-- EXAMPLE: online interview scheduled for application #1. FLOWS FROM: Applications (CASCADE).
INSERT INTO `Interviews`
  (`Id`, `ApplicationId`, `InterviewDate`, `LocationOrLink`, `InterviewType`, `Notes`, `Status`) VALUES
  (1, 1, '2026-10-15 14:00:00.000000', 'https://meet.example.com/acme-round1', 0, 'Round 1: .NET + SQL deep dive.', 0);

-- ---------------------------------------------------------------------------
-- 7. Engagement: bookmarks, ML scores, reviews
-- ---------------------------------------------------------------------------

-- EXAMPLE: candidate #1 bookmarks job #1. FLOWS FROM: Candidates + JobPostings.
INSERT INTO `SavedJobs` (`CandidateId`, `JobPostingId`, `SavedAt`) VALUES
  (1, 1, '2026-10-07 13:00:00.000000');

-- EXAMPLE: ML matcher scored resume #1 against job #1 at 87.5%. FLOWS FROM: JobPostings + Resumes.
INSERT INTO `MatchScores` (`JobPostingId`, `ResumeId`, `MatchPercentage`, `AlgorithmUsed`, `CalculatedAt`) VALUES
  (1, 1, 87.5, 'tfidf-v1', '2026-10-07 12:05:00.000000');

-- EXAMPLE: candidate #1 rates company #1 5/5. UNIQUE (CompanyProfileId, CandidateId): one review each.
-- FLOWS FROM: CompanyProfiles + Candidates.
INSERT INTO `Reviews` (`Id`, `CompanyProfileId`, `CandidateId`, `RatingScore`, `ReviewText`, `CreatedAt`) VALUES
  (1, 1, 1, 5, 'Fast process, friendly HR team.', '2026-10-10 15:00:00.000000');

-- ---------------------------------------------------------------------------
-- 8. User-scoped rows: notifications, refresh tokens, training logs
-- ---------------------------------------------------------------------------

-- EXAMPLE: candidate is told their application was viewed; employer is told a new application arrived.
-- FLOWS FROM: AspNetUsers (CASCADE).
INSERT INTO `Notifications` (`Id`, `UserId`, `Title`, `Content`, `IsRead`, `CreatedAt`) VALUES
  (1, 'user-candidate-001', 'Application viewed', 'Acme Corp viewed your application for Senior .NET Developer.', 0, '2026-10-08 09:01:00.000000'),
  (2, 'user-employer-001',  'New application',    'Demo Candidate applied for Senior .NET Developer.',             0, '2026-10-07 12:01:00.000000');

-- EXAMPLE: active refresh-token session for the candidate (only the SHA-256 hash is stored;
-- the raw token lives in the client's HttpOnly cookie). FLOWS FROM: AspNetUsers (CASCADE).
INSERT INTO `RefreshTokens`
  (`Id`, `TokenHash`, `UserId`, `CreatedAtUtc`, `ExpiresAtUtc`, `RevokedAtUtc`, `ReplacedByTokenHash`, `RevokedReason`) VALUES
  (1, '0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef',
   'user-candidate-001', '2026-10-12 08:00:00.000000', '2026-10-19 08:00:00.000000', NULL, NULL, NULL);

-- EXAMPLE: admin trained a model on 10k records with 0.92 accuracy. FLOWS FROM: AspNetUsers (SET NULL).
INSERT INTO `ModelTrainingLogs` (`Id`, `RecordCount`, `AccuracyScore`, `ModelFilePath`, `TrainedByUserId`, `TrainedAt`) VALUES
  (1, 10000, 0.92, '/models/match-v1.zip', 'user-admin-001', '2026-10-11 16:00:00.000000');

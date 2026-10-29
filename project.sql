-- HotJob MySQL schema
-- Generated from hotjob/Models + hotjob/Data/ApplicationDbContext (EF Core + Pomelo MySQL, MySQL 8.0).
-- Standalone DDL: creates database `hotjob` (utf8mb4) with all domain + ASP.NET Identity tables.
-- Enums are stored as INT (EF default). Datetime values are UTC.
--
-- Enum reference:
--   Candidate.Gender: 0=Male, 1=Female, 2=Other, 3=PreferNotToSay
--   Employer.MembershipStatus: 0=Pending, 1=Active, 2=Rejected
--   CompanyProfile.Status: 0=Pending, 1=Approved, 2=Rejected
--   CompanyProfile.Scale: 0=LessThan10, 1=From10To49, 2=From50To199, 3=From200To499, 4=MoreThan500
--   JobPosting.Status: 0=Draft, 1=Published, 2=Closed, 3=Expired
--   JobPosting.JobType: 0=FullTime, 1=PartTime, 2=Contract, 3=Internship, 4=Freelance, 5=Remote
--   Application.CurrentStatus / ApplicationStatusHistory.Status: 0=Pending, 1=Viewed, 2=Shortlisted, 3=Interviewing, 4=Offered, 5=Rejected, 6=Withdrawn
--   Interview.Status: 0=Scheduled, 1=Completed, 2=Cancelled
--   Interview.InterviewType: 0=Online, 1=Onsite, 2=Phone (NULL = unspecified)
--   PlanAudience: 0=Employer, 1=Candidate
--   PaymentPurpose: 0=EmployerPlan, 1=EmployerFeatured, 2=CandidatePremium
--   PaymentStatus: 0=Pending, 1=Paid, 2=Failed, 3=Expired
--
-- Usage: mysql -u root -p < project.sql

CREATE DATABASE IF NOT EXISTS `hotjob` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE `hotjob`;

SET FOREIGN_KEY_CHECKS = 0;

DROP TABLE IF EXISTS `Interviews`;
DROP TABLE IF EXISTS `ApplicationStatusHistories`;
DROP TABLE IF EXISTS `Applications`;
DROP TABLE IF EXISTS `SavedJobs`;
DROP TABLE IF EXISTS `MatchScores`;
DROP TABLE IF EXISTS `JobSkills`;
DROP TABLE IF EXISTS `JobCategoryMappings`;
DROP TABLE IF EXISTS `JobPostings`;
DROP TABLE IF EXISTS `WorkExperiences`;
DROP TABLE IF EXISTS `ResumeSkills`;
DROP TABLE IF EXISTS `Educations`;
DROP TABLE IF EXISTS `Resumes`;
DROP TABLE IF EXISTS `Reviews`;
DROP TABLE IF EXISTS `Employers`;
DROP TABLE IF EXISTS `Payments`;
DROP TABLE IF EXISTS `Plans`;
DROP TABLE IF EXISTS `RefreshTokens`;
DROP TABLE IF EXISTS `Notifications`;
DROP TABLE IF EXISTS `ModelTrainingLogs`;
DROP TABLE IF EXISTS `CompanyProfiles`;
DROP TABLE IF EXISTS `Candidates`;
DROP TABLE IF EXISTS `Skills`;
DROP TABLE IF EXISTS `JobCategories`;
DROP TABLE IF EXISTS `AspNetUserTokens`;
DROP TABLE IF EXISTS `AspNetUserRoles`;
DROP TABLE IF EXISTS `AspNetUserLogins`;
DROP TABLE IF EXISTS `AspNetUserClaims`;
DROP TABLE IF EXISTS `AspNetRoleClaims`;
DROP TABLE IF EXISTS `AspNetUsers`;
DROP TABLE IF EXISTS `AspNetRoles`;

SET FOREIGN_KEY_CHECKS = 1;

-- ---------------------------------------------------------------------------
-- ASP.NET Identity
-- ---------------------------------------------------------------------------

CREATE TABLE `AspNetRoles` (
  `Id` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
  `Name` varchar(256) CHARACTER SET utf8mb4 NULL,
  `NormalizedName` varchar(256) CHARACTER SET utf8mb4 NULL,
  `ConcurrencyStamp` longtext CHARACTER SET utf8mb4 NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `RoleNameIndex` (`NormalizedName`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `AspNetUsers` (
  `Id` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `IsActive` tinyint(1) NOT NULL DEFAULT 1,
  `UserName` varchar(256) CHARACTER SET utf8mb4 NULL,
  `NormalizedUserName` varchar(256) CHARACTER SET utf8mb4 NULL,
  `Email` varchar(256) CHARACTER SET utf8mb4 NULL,
  `NormalizedEmail` varchar(256) CHARACTER SET utf8mb4 NULL,
  `EmailConfirmed` tinyint(1) NOT NULL DEFAULT 0,
  `PasswordHash` longtext CHARACTER SET utf8mb4 NULL,
  `SecurityStamp` longtext CHARACTER SET utf8mb4 NULL,
  `ConcurrencyStamp` longtext CHARACTER SET utf8mb4 NULL,
  `PhoneNumber` longtext CHARACTER SET utf8mb4 NULL,
  `PhoneNumberConfirmed` tinyint(1) NOT NULL DEFAULT 0,
  `TwoFactorEnabled` tinyint(1) NOT NULL DEFAULT 0,
  `LockoutEnd` datetime(6) NULL,
  `LockoutEnabled` tinyint(1) NOT NULL DEFAULT 0,
  `AccessFailedCount` int NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `UserNameIndex` (`NormalizedUserName`),
  KEY `EmailIndex` (`NormalizedEmail`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `AspNetRoleClaims` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `RoleId` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
  `ClaimType` longtext CHARACTER SET utf8mb4 NULL,
  `ClaimValue` longtext CHARACTER SET utf8mb4 NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_AspNetRoleClaims_RoleId` (`RoleId`),
  CONSTRAINT `FK_AspNetRoleClaims_AspNetRoles_RoleId` FOREIGN KEY (`RoleId`) REFERENCES `AspNetRoles` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `AspNetUserClaims` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `UserId` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
  `ClaimType` longtext CHARACTER SET utf8mb4 NULL,
  `ClaimValue` longtext CHARACTER SET utf8mb4 NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_AspNetUserClaims_UserId` (`UserId`),
  CONSTRAINT `FK_AspNetUserClaims_AspNetUsers_UserId` FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `AspNetUserLogins` (
  `LoginProvider` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
  `ProviderKey` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
  `ProviderDisplayName` longtext CHARACTER SET utf8mb4 NULL,
  `UserId` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
  PRIMARY KEY (`LoginProvider`, `ProviderKey`),
  KEY `IX_AspNetUserLogins_UserId` (`UserId`),
  CONSTRAINT `FK_AspNetUserLogins_AspNetUsers_UserId` FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `AspNetUserRoles` (
  `UserId` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
  `RoleId` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
  PRIMARY KEY (`UserId`, `RoleId`),
  KEY `IX_AspNetUserRoles_RoleId` (`RoleId`),
  CONSTRAINT `FK_AspNetUserRoles_AspNetRoles_RoleId` FOREIGN KEY (`RoleId`) REFERENCES `AspNetRoles` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_AspNetUserRoles_AspNetUsers_UserId` FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `AspNetUserTokens` (
  `UserId` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
  `LoginProvider` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
  `Name` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
  `Value` longtext CHARACTER SET utf8mb4 NULL,
  PRIMARY KEY (`UserId`, `LoginProvider`, `Name`),
  CONSTRAINT `FK_AspNetUserTokens_AspNetUsers_UserId` FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ---------------------------------------------------------------------------
-- Billing: plans + payments
-- ---------------------------------------------------------------------------

CREATE TABLE `Plans` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `Audience` int NOT NULL,
  `Code` varchar(30) CHARACTER SET utf8mb4 NOT NULL,
  `Name` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
  `Price` decimal(18,0) NOT NULL,
  `JobQuota` int NOT NULL,
  `DurationDays` int NOT NULL,
  `IsActive` tinyint(1) NOT NULL,
  `SortOrder` int NOT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_Plans_Code` (`Code`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `Payments` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `PayerUserId` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
  `CompanyProfileId` int NULL,
  `CandidateId` int NULL,
  `Purpose` int NOT NULL,
  `PlanId` int NULL,
  `JobPostingId` int NULL,
  `Amount` decimal(18,0) NOT NULL,
  `Status` int NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `PaidAt` datetime(6) NULL,
  `VnpTxnRef` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
  `VnpTransactionNo` varchar(50) CHARACTER SET utf8mb4 NULL,
  `VnpResponseCode` varchar(10) CHARACTER SET utf8mb4 NULL,
  `VnpBankCode` varchar(20) CHARACTER SET utf8mb4 NULL,
  `VnpPayDate` varchar(14) CHARACTER SET utf8mb4 NULL,
  `Note` varchar(500) CHARACTER SET utf8mb4 NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_Payments_VnpTxnRef` (`VnpTxnRef`),
  KEY `IX_Payments_CandidateId` (`CandidateId`),
  KEY `IX_Payments_CompanyProfileId` (`CompanyProfileId`),
  KEY `IX_Payments_JobPostingId` (`JobPostingId`),
  KEY `IX_Payments_PayerUserId` (`PayerUserId`),
  KEY `IX_Payments_PlanId` (`PlanId`),
  KEY `IX_Payments_Status_PaidAt` (`Status`, `PaidAt`),
  CONSTRAINT `FK_Payments_AspNetUsers_PayerUserId` FOREIGN KEY (`PayerUserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE RESTRICT,
  CONSTRAINT `FK_Payments_Candidates_CandidateId` FOREIGN KEY (`CandidateId`) REFERENCES `Candidates` (`Id`) ON DELETE RESTRICT,
  CONSTRAINT `FK_Payments_CompanyProfiles_CompanyProfileId` FOREIGN KEY (`CompanyProfileId`) REFERENCES `CompanyProfiles` (`Id`) ON DELETE RESTRICT,
  CONSTRAINT `FK_Payments_JobPostings_JobPostingId` FOREIGN KEY (`JobPostingId`) REFERENCES `JobPostings` (`Id`) ON DELETE SET NULL,
  CONSTRAINT `FK_Payments_Plans_PlanId` FOREIGN KEY (`PlanId`) REFERENCES `Plans` (`Id`) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ---------------------------------------------------------------------------
-- Users / Companies
-- ---------------------------------------------------------------------------

CREATE TABLE `Candidates` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `UserId` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
  `FullName` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
  `DateOfBirth` date NULL,
  `Gender` int NULL,
  `Address` varchar(500) CHARACTER SET utf8mb4 NULL,
  `AvatarUrl` varchar(2048) CHARACTER SET utf8mb4 NULL,
  `GithubUrl` varchar(2048) CHARACTER SET utf8mb4 NULL,
  `LinkedInUrl` varchar(2048) CHARACTER SET utf8mb4 NULL,
  `Position` varchar(200) CHARACTER SET utf8mb4 NULL,
  `Summary` varchar(2000) CHARACTER SET utf8mb4 NULL,
  `JobSeekingStatus` varchar(50) CHARACTER SET utf8mb4 NULL,
  `PreferredWorkModel` varchar(50) CHARACTER SET utf8mb4 NULL,
  `PreferredLocation` varchar(500) CHARACTER SET utf8mb4 NULL,
  `BoostUntil` datetime(6) NULL,
  `PremiumPlanId` int NULL,
  `PremiumStartedAt` datetime(6) NULL,
  `PremiumUntil` datetime(6) NULL,
  `IsDiscoverable` tinyint(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_Candidates_UserId` (`UserId`),
  KEY `IX_Candidates_PremiumPlanId` (`PremiumPlanId`),
  CONSTRAINT `FK_Candidates_AspNetUsers_UserId` FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_Candidates_Plans_PremiumPlanId` FOREIGN KEY (`PremiumPlanId`) REFERENCES `Plans` (`Id`) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `CompanyProfiles` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `CompanyName` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
  `Website` varchar(2048) CHARACTER SET utf8mb4 NULL,
  `LogoUrl` varchar(2048) CHARACTER SET utf8mb4 NULL,
  `Location` varchar(200) CHARACTER SET utf8mb4 NULL,
  `Industry` varchar(100) CHARACTER SET utf8mb4 NULL,
  `Scale` int NULL,
  `Description` varchar(2000) CHARACTER SET utf8mb4 NULL,
  `Status` int NOT NULL DEFAULT 0,
  `RejectionReason` varchar(1000) CHARACTER SET utf8mb4 NULL,
  `VerifiedAt` datetime(6) NULL,
  `VerifiedByAdminId` varchar(255) CHARACTER SET utf8mb4 NULL,
  `PlanId` int NULL,
  `PlanStartedAt` datetime(6) NULL,
  `PlanExpiresAt` datetime(6) NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_CompanyProfiles_CompanyName` (`CompanyName`),
  KEY `IX_CompanyProfiles_PlanId` (`PlanId`),
  KEY `IX_CompanyProfiles_VerifiedByAdminId` (`VerifiedByAdminId`),
  CONSTRAINT `FK_CompanyProfiles_AspNetUsers_VerifiedByAdminId` FOREIGN KEY (`VerifiedByAdminId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE SET NULL,
  CONSTRAINT `FK_CompanyProfiles_Plans_PlanId` FOREIGN KEY (`PlanId`) REFERENCES `Plans` (`Id`) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `Employers` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `UserId` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
  `CompanyProfileId` int NOT NULL,
  `FullName` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
  `Position` varchar(100) CHARACTER SET utf8mb4 NULL,
  `IsOwner` tinyint(1) NOT NULL DEFAULT 0,
  `MembershipStatus` int NOT NULL DEFAULT 0,
  `JoinedAt` datetime(6) NOT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_Employers_UserId` (`UserId`),
  KEY `IX_Employers_CompanyProfileId` (`CompanyProfileId`),
  CONSTRAINT `FK_Employers_AspNetUsers_UserId` FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE,
  -- Restrict so deleting a company never deletes Identity users.
  CONSTRAINT `FK_Employers_CompanyProfiles_CompanyProfileId` FOREIGN KEY (`CompanyProfileId`) REFERENCES `CompanyProfiles` (`Id`) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `RefreshTokens` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `TokenHash` varchar(64) CHARACTER SET utf8mb4 NOT NULL COMMENT 'SHA-256 hex of the raw refresh token',
  `UserId` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
  `CreatedAtUtc` datetime(6) NOT NULL,
  `ExpiresAtUtc` datetime(6) NOT NULL,
  `RevokedAtUtc` datetime(6) NULL COMMENT 'NULL while active',
  `ReplacedByTokenHash` varchar(64) CHARACTER SET utf8mb4 NULL COMMENT 'Hash of replacement token when revoked via rotation',
  `RevokedReason` varchar(200) CHARACTER SET utf8mb4 NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_RefreshTokens_TokenHash` (`TokenHash`),
  KEY `IX_RefreshTokens_UserId_RevokedAtUtc` (`UserId`, `RevokedAtUtc`),
  CONSTRAINT `FK_RefreshTokens_AspNetUsers_UserId` FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `Notifications` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `UserId` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
  `Title` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
  `Content` varchar(2000) CHARACTER SET utf8mb4 NULL,
  `Kind` varchar(40) CHARACTER SET utf8mb4 NULL,
  `RelatedJobPostingId` int NULL,
  `IsRead` tinyint(1) NOT NULL DEFAULT 0,
  `CreatedAt` datetime(6) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_Notifications_UserId_IsRead` (`UserId`, `IsRead`),
  KEY `IX_Notifications_RelatedJobPostingId` (`RelatedJobPostingId`),
  KEY `IX_Notifications_UserId_Kind_CreatedAt` (`UserId`, `Kind`, `CreatedAt`),
  UNIQUE KEY `IX_Notifications_UserId_Kind_RelatedJobPostingId` (`UserId`, `Kind`, `RelatedJobPostingId`),
  CONSTRAINT `FK_Notifications_AspNetUsers_UserId` FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_Notifications_JobPostings_RelatedJobPostingId` FOREIGN KEY (`RelatedJobPostingId`) REFERENCES `JobPostings` (`Id`) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `ModelTrainingLogs` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `RecordCount` int NOT NULL,
  `AccuracyScore` double NOT NULL,
  `ModelFilePath` varchar(500) CHARACTER SET utf8mb4 NULL,
  `TrainedByUserId` varchar(255) CHARACTER SET utf8mb4 NULL,
  `TrainedAt` datetime(6) NOT NULL,
  `IsActive` tinyint(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`),
  KEY `IX_ModelTrainingLogs_TrainedByUserId` (`TrainedByUserId`),
  KEY `IX_ModelTrainingLogs_IsActive_TrainedAt` (`IsActive`, `TrainedAt`),
  CONSTRAINT `FK_ModelTrainingLogs_AspNetUsers_TrainedByUserId` FOREIGN KEY (`TrainedByUserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ---------------------------------------------------------------------------
-- Skills / Categories
-- ---------------------------------------------------------------------------

CREATE TABLE `Skills` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `Name` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
  `Category` varchar(100) CHARACTER SET utf8mb4 NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_Skills_Name` (`Name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `JobCategories` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `Name` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
  `Description` varchar(500) CHARACTER SET utf8mb4 NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_JobCategories_Name` (`Name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ---------------------------------------------------------------------------
-- Resumes
-- ---------------------------------------------------------------------------

CREATE TABLE `Resumes` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `CandidateId` int NOT NULL,
  `Title` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
  `Summary` varchar(2000) CHARACTER SET utf8mb4 NULL,
  `FileUrl` varchar(2048) CHARACTER SET utf8mb4 NULL,
  `IsPrimary` tinyint(1) NOT NULL DEFAULT 0,
  `CreatedAt` datetime(6) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_Resumes_CandidateId` (`CandidateId`),
  CONSTRAINT `FK_Resumes_Candidates_CandidateId` FOREIGN KEY (`CandidateId`) REFERENCES `Candidates` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `Educations` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `ResumeId` int NOT NULL,
  `SchoolName` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
  `Major` varchar(200) CHARACTER SET utf8mb4 NULL,
  `StartDate` date NULL,
  `EndDate` date NULL,
  `Gpa` double NULL,
  `Description` varchar(2000) CHARACTER SET utf8mb4 NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_Educations_ResumeId` (`ResumeId`),
  CONSTRAINT `FK_Educations_Resumes_ResumeId` FOREIGN KEY (`ResumeId`) REFERENCES `Resumes` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `WorkExperiences` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `ResumeId` int NOT NULL,
  `CompanyName` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
  `JobTitle` varchar(200) CHARACTER SET utf8mb4 NULL,
  `StartDate` date NULL,
  `EndDate` date NULL,
  `Description` varchar(2000) CHARACTER SET utf8mb4 NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_WorkExperiences_ResumeId` (`ResumeId`),
  CONSTRAINT `FK_WorkExperiences_Resumes_ResumeId` FOREIGN KEY (`ResumeId`) REFERENCES `Resumes` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `ResumeSkills` (
  `ResumeId` int NOT NULL,
  `SkillId` int NOT NULL,
  `YearsOfExperience` int NOT NULL DEFAULT 0,
  PRIMARY KEY (`ResumeId`, `SkillId`),
  KEY `IX_ResumeSkills_SkillId` (`SkillId`),
  CONSTRAINT `FK_ResumeSkills_Resumes_ResumeId` FOREIGN KEY (`ResumeId`) REFERENCES `Resumes` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_ResumeSkills_Skills_SkillId` FOREIGN KEY (`SkillId`) REFERENCES `Skills` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ---------------------------------------------------------------------------
-- Jobs
-- ---------------------------------------------------------------------------

CREATE TABLE `JobPostings` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `CompanyProfileId` int NOT NULL,
  `CreatedByEmployerId` int NULL COMMENT 'NULL if the author left',
  `Title` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
  `Description` varchar(10000) CHARACTER SET utf8mb4 NOT NULL,
  `SalaryMin` decimal(18,2) NULL,
  `SalaryMax` decimal(18,2) NULL,
  `ExperienceRequired` int NOT NULL DEFAULT 0,
  `JobType` int NOT NULL DEFAULT 0,
  `ExpiryDate` datetime(6) NULL,
  `Status` int NOT NULL DEFAULT 0,
  `CreatedAt` datetime(6) NOT NULL,
  `FeaturedUntil` datetime(6) NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_JobPostings_CompanyProfileId` (`CompanyProfileId`),
  KEY `IX_JobPostings_CreatedByEmployerId` (`CreatedByEmployerId`),
  KEY `IX_JobPostings_FeaturedUntil` (`FeaturedUntil`),
  CONSTRAINT `FK_JobPostings_CompanyProfiles_CompanyProfileId` FOREIGN KEY (`CompanyProfileId`) REFERENCES `CompanyProfiles` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_JobPostings_Employers_CreatedByEmployerId` FOREIGN KEY (`CreatedByEmployerId`) REFERENCES `Employers` (`Id`) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `JobCategoryMappings` (
  `JobPostingId` int NOT NULL,
  `JobCategoryId` int NOT NULL,
  PRIMARY KEY (`JobPostingId`, `JobCategoryId`),
  KEY `IX_JobCategoryMappings_JobCategoryId` (`JobCategoryId`),
  CONSTRAINT `FK_JobCategoryMappings_JobPostings_JobPostingId` FOREIGN KEY (`JobPostingId`) REFERENCES `JobPostings` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_JobCategoryMappings_JobCategories_JobCategoryId` FOREIGN KEY (`JobCategoryId`) REFERENCES `JobCategories` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `JobSkills` (
  `JobPostingId` int NOT NULL,
  `SkillId` int NOT NULL,
  `ImportanceWeight` double NOT NULL DEFAULT 1.0,
  PRIMARY KEY (`JobPostingId`, `SkillId`),
  KEY `IX_JobSkills_SkillId` (`SkillId`),
  CONSTRAINT `FK_JobSkills_JobPostings_JobPostingId` FOREIGN KEY (`JobPostingId`) REFERENCES `JobPostings` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_JobSkills_Skills_SkillId` FOREIGN KEY (`SkillId`) REFERENCES `Skills` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ---------------------------------------------------------------------------
-- Applications / Interviews / Reviews / Saved / ML scores
-- ---------------------------------------------------------------------------

CREATE TABLE `Applications` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `JobPostingId` int NOT NULL,
  `ResumeId` int NOT NULL,
  `CandidateId` int NOT NULL,
  `CoverLetter` varchar(2000) CHARACTER SET utf8mb4 NULL,
  `CurrentStatus` int NOT NULL DEFAULT 0,
  `SnapshotFileUrl` varchar(2048) CHARACTER SET utf8mb4 NULL,
  `SnapshotSummary` varchar(2000) CHARACTER SET utf8mb4 NULL,
  `AppliedAt` datetime(6) NOT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_Applications_JobPostingId_CandidateId` (`JobPostingId`, `CandidateId`) COMMENT 'One application per candidate per job',
  KEY `IX_Applications_CandidateId` (`CandidateId`),
  KEY `IX_Applications_ResumeId` (`ResumeId`),
  CONSTRAINT `FK_Applications_JobPostings_JobPostingId` FOREIGN KEY (`JobPostingId`) REFERENCES `JobPostings` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_Applications_Resumes_ResumeId` FOREIGN KEY (`ResumeId`) REFERENCES `Resumes` (`Id`) ON DELETE RESTRICT,
  CONSTRAINT `FK_Applications_Candidates_CandidateId` FOREIGN KEY (`CandidateId`) REFERENCES `Candidates` (`Id`) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `ApplicationStatusHistories` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `ApplicationId` int NOT NULL,
  `Status` int NOT NULL,
  `ChangedByUserId` varchar(255) CHARACTER SET utf8mb4 NULL COMMENT 'NULL for system changes',
  `Notes` varchar(1000) CHARACTER SET utf8mb4 NULL,
  `ChangedAt` datetime(6) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_ApplicationStatusHistories_ApplicationId` (`ApplicationId`),
  KEY `IX_ApplicationStatusHistories_ChangedByUserId` (`ChangedByUserId`),
  CONSTRAINT `FK_ApplicationStatusHistories_Applications_ApplicationId` FOREIGN KEY (`ApplicationId`) REFERENCES `Applications` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_ApplicationStatusHistories_AspNetUsers_ChangedByUserId` FOREIGN KEY (`ChangedByUserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `Interviews` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `ApplicationId` int NOT NULL,
  `InterviewDate` datetime(6) NOT NULL,
  `LocationOrLink` varchar(2048) CHARACTER SET utf8mb4 NULL,
  `InterviewType` int NULL,
  `Notes` varchar(2000) CHARACTER SET utf8mb4 NULL,
  `Status` int NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`),
  KEY `IX_Interviews_ApplicationId` (`ApplicationId`),
  CONSTRAINT `FK_Interviews_Applications_ApplicationId` FOREIGN KEY (`ApplicationId`) REFERENCES `Applications` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `Reviews` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `CompanyProfileId` int NOT NULL,
  `CandidateId` int NOT NULL,
  `RatingScore` int NOT NULL COMMENT '1-5',
  `ReviewText` varchar(2000) CHARACTER SET utf8mb4 NULL,
  `CreatedAt` datetime(6) NOT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_Reviews_CompanyProfileId_CandidateId` (`CompanyProfileId`, `CandidateId`) COMMENT 'One review per candidate per company',
  KEY `IX_Reviews_CandidateId` (`CandidateId`),
  CONSTRAINT `FK_Reviews_CompanyProfiles_CompanyProfileId` FOREIGN KEY (`CompanyProfileId`) REFERENCES `CompanyProfiles` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_Reviews_Candidates_CandidateId` FOREIGN KEY (`CandidateId`) REFERENCES `Candidates` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `CHK_Reviews_RatingScore` CHECK (`RatingScore` BETWEEN 1 AND 5)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `SavedJobs` (
  `CandidateId` int NOT NULL,
  `JobPostingId` int NOT NULL,
  `SavedAt` datetime(6) NOT NULL,
  PRIMARY KEY (`CandidateId`, `JobPostingId`),
  KEY `IX_SavedJobs_JobPostingId` (`JobPostingId`),
  CONSTRAINT `FK_SavedJobs_Candidates_CandidateId` FOREIGN KEY (`CandidateId`) REFERENCES `Candidates` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_SavedJobs_JobPostings_JobPostingId` FOREIGN KEY (`JobPostingId`) REFERENCES `JobPostings` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `MatchScores` (
  `JobPostingId` int NOT NULL,
  `ResumeId` int NOT NULL,
  `MatchPercentage` double NOT NULL COMMENT '0-100',
  `AlgorithmUsed` varchar(50) CHARACTER SET utf8mb4 NULL,
  `CalculatedAt` datetime(6) NOT NULL,
  PRIMARY KEY (`JobPostingId`, `ResumeId`),
  KEY `IX_MatchScores_ResumeId` (`ResumeId`),
  CONSTRAINT `FK_MatchScores_JobPostings_JobPostingId` FOREIGN KEY (`JobPostingId`) REFERENCES `JobPostings` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_MatchScores_Resumes_ResumeId` FOREIGN KEY (`ResumeId`) REFERENCES `Resumes` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

using hotjob.DTOs.Employer;
using hotjob.Models;
using hotjob.Models.Application;

namespace hotjob.Services.Employer;

/// <summary>Entity → DTO mapping shared by applicant/interview/notification services.</summary>
internal static class ApplicantMapping
{
    public static ApplicantListItemResponse ToListItem(Application a) =>
        new(a.Id, a.JobPostingId, a.JobPosting.Title,
            a.CandidateId, a.Candidate.FullName, a.Candidate.User?.Email,
            a.ResumeId, a.Resume.Title, a.CurrentStatus, a.AppliedAt);

    public static ApplicantDetailResponse ToDetail(Application a) =>
        new()
        {
            ApplicationId = a.Id,
            JobPostingId = a.JobPostingId,
            JobTitle = a.JobPosting.Title,
            CandidateId = a.CandidateId,
            CandidateFullName = a.Candidate.FullName,
            CandidateEmail = a.Candidate.User?.Email,
            ResumeId = a.ResumeId,
            CoverLetter = a.CoverLetter,
            CurrentStatus = a.CurrentStatus,
            SnapshotFileUrl = a.SnapshotFileUrl,
            SnapshotSummary = a.SnapshotSummary,
            AppliedAt = a.AppliedAt,
            Resume = new ResumeDetailDto
            {
                Id = a.Resume.Id,
                Title = a.Resume.Title,
                Summary = a.Resume.Summary,
                FileUrl = a.Resume.FileUrl,
                IsPrimary = a.Resume.IsPrimary,
                Educations = a.Resume.Educations
                    .Select(e => new EducationDto(e.Id, e.SchoolName, e.Major, e.StartDate, e.EndDate, e.Gpa, e.Description))
                    .ToArray(),
                WorkExperiences = a.Resume.WorkExperiences
                    .Select(w => new WorkExperienceDto(w.Id, w.CompanyName, w.JobTitle, w.StartDate, w.EndDate, w.Description))
                    .ToArray(),
                Skills = a.Resume.ResumeSkills
                    .Select(rs => new ResumeSkillDto(rs.SkillId, rs.Skill.Name, rs.YearsOfExperience))
                    .ToArray()
            },
            StatusHistory = a.StatusHistory
                .OrderByDescending(h => h.ChangedAt)
                .Select(h => new HistoryDto(h.Id, h.Status, h.ChangedByUserId, h.Notes, h.ChangedAt))
                .ToArray(),
            Interviews = a.Interviews
                .OrderBy(i => i.InterviewDate)
                .Select(ToInterviewDto)
                .ToArray()
        };

    public static InterviewDto ToInterviewDto(Interview i) =>
        new(i.Id, i.ApplicationId, i.InterviewDate, i.LocationOrLink, i.InterviewType, i.Notes, i.Status);

    public static InterviewResponse ToInterviewResponse(Interview i) =>
        new(i.Id, i.ApplicationId, i.InterviewDate, i.LocationOrLink, i.InterviewType, i.Notes, i.Status);

    public static NotificationResponse ToNotificationResponse(Notification n) =>
        new(n.Id, n.UserId, n.Title, n.Content, n.IsRead, n.CreatedAt);
}

using System.Text.Json;
using hotjob.DTOs.Employer;
using hotjob.Models;
using hotjob.Models.Application;
using hotjob.Models.Resume;

namespace hotjob.Services.Employer;

/// <summary>Entity → DTO mapping shared by applicant/interview/notification services.</summary>
internal static class ApplicantMapping
{
    public static ApplicantListItemResponse ToListItem(Application a, double? matchPercentage = null) =>
        new(a.Id, a.JobPostingId, a.JobPosting.Title,
            a.CandidateId, a.Candidate.FullName, a.Candidate.User?.Email,
            a.ResumeId, a.Resume.Title, a.CurrentStatus, a.AppliedAt, matchPercentage);

    public static ApplicantDetailResponse ToDetail(Application a)
    {
        var snapshot = DeserializeSnapshot(a.SnapshotJson);
        return new()
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
            IsSnapshot = snapshot is not null,
            Resume = snapshot is not null ? FromSnapshot(snapshot) : FromResume(a.Resume),
            StatusHistory = a.StatusHistory
                .OrderByDescending(h => h.ChangedAt)
                .Select(h => new HistoryDto(h.Id, h.Status, h.ChangedByUserId, h.Notes, h.ChangedAt))
                .ToArray(),
            Interviews = a.Interviews
                .OrderBy(i => i.InterviewDate)
                .Select(ToInterviewDto)
                .ToArray()
        };
    }

    private static ResumeDetailDto FromResume(Resume r) => new()
    {
        Id = r.Id,
        Title = r.Title,
        Summary = r.Summary,
        FileUrl = r.FileUrl,
        IsPrimary = r.IsPrimary,
        Educations = r.Educations
            .Select(e => new EducationDto(e.Id, e.SchoolName, e.Major, e.StartDate, e.EndDate, e.Gpa, e.Description))
            .ToArray(),
        WorkExperiences = r.WorkExperiences
            .Select(w => new WorkExperienceDto(w.Id, w.CompanyName, w.JobTitle, w.StartDate, w.EndDate, w.Description))
            .ToArray(),
        Skills = r.ResumeSkills
            .Select(rs => new ResumeSkillDto(rs.SkillId, rs.Skill.Name, rs.YearsOfExperience))
            .ToArray()
    };

    private static ResumeDetailDto FromSnapshot(ResumeSnapshot s) => new()
    {
        Title = s.Title,
        Summary = s.Summary,
        FileUrl = s.FileUrl,
        Educations = (s.Educations ?? Array.Empty<SnapshotEducation>())
            .Select(e => new EducationDto(0, e.SchoolName, e.Major, e.StartDate, e.EndDate, e.Gpa, e.Description))
            .ToArray(),
        WorkExperiences = (s.WorkExperiences ?? Array.Empty<SnapshotWorkExperience>())
            .Select(w => new WorkExperienceDto(0, w.CompanyName, w.JobTitle, w.StartDate, w.EndDate, w.Description))
            .ToArray(),
        Skills = (s.Skills ?? Array.Empty<SnapshotSkill>())
            .Select(skill => new ResumeSkillDto(skill.SkillId, skill.SkillName, skill.YearsOfExperience))
            .ToArray()
    };

    private static ResumeSnapshot? DeserializeSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<ResumeSnapshot>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static InterviewDto ToInterviewDto(Interview i) =>
        new(i.Id, i.ApplicationId, i.InterviewDate, i.LocationOrLink, i.InterviewType, i.Notes, i.Status);

    public static InterviewResponse ToInterviewResponse(Interview i) =>
        new(i.Id, i.ApplicationId, i.InterviewDate, i.LocationOrLink, i.InterviewType, i.Notes, i.Status);

    public static NotificationResponse ToNotificationResponse(Notification n) =>
        new(n.Id, n.UserId, n.Title, n.Content, n.IsRead, n.CreatedAt);
}

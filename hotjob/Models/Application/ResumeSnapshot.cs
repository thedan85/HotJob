namespace hotjob.Models.Application;

/// <summary>Frozen copy of a resume at application time.</summary>
public sealed record ResumeSnapshot(
    string Title,
    string? Summary,
    string? FileUrl,
    IReadOnlyList<SnapshotEducation> Educations,
    IReadOnlyList<SnapshotWorkExperience> WorkExperiences,
    IReadOnlyList<SnapshotSkill> Skills);

/// <summary>Education entry frozen into an application snapshot.</summary>
public sealed record SnapshotEducation(
    string SchoolName,
    string? Major,
    DateOnly? StartDate,
    DateOnly? EndDate,
    double? Gpa,
    string? Description);

/// <summary>Work experience entry frozen into an application snapshot.</summary>
public sealed record SnapshotWorkExperience(
    string CompanyName,
    string? JobTitle,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? Description);

/// <summary>Skill entry frozen into an application snapshot.</summary>
public sealed record SnapshotSkill(int SkillId, string SkillName, int YearsOfExperience);

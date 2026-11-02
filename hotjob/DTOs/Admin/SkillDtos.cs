namespace hotjob.DTOs.Admin;

public sealed record AdminSkillDto(
    int Id,
    string Name,
    string? Category,
    int ResumeCount,
    int JobCount);

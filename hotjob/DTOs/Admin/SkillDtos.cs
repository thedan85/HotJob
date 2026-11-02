using System.ComponentModel.DataAnnotations;

namespace hotjob.DTOs.Admin;

public sealed record AdminSkillDto(
    int Id,
    string Name,
    string? Category,
    int ResumeCount,
    int JobCount);

public sealed class CreateSkillRequest
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Category { get; set; }
}

public sealed class UpdateSkillRequest
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Category { get; set; }
}

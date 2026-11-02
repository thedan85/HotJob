using System.ComponentModel.DataAnnotations;

namespace hotjob.DTOs.Admin;

public sealed record AdminSkillDto(
    int Id,
    string Name,
    string? Category,
    int ResumeCount,
    int JobCount);

// Length validation belongs in AdminService, after trimming.
public sealed class CreateSkillRequest
{
    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Category { get; set; }
}

public sealed class UpdateSkillRequest
{
    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Category { get; set; }
}

using System.Security.Claims;
using hotjob.Data;
using hotjob.DTOs.Candidate;
using hotjob.Services.Common;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Services.Candidate.ResumeParsing;

internal sealed class ResumeParserService(
    ApplicationDbContext db, IHttpContextAccessor http, AuthenticationStateProvider authentication,
    ResumeTextExtractor extractor, RuleBasedResumeParser rulesParser, ParsedResumeNormalizer normalizer,
    DeepSeekResumeClient ai) : IResumeParserService
{
    public bool IsAiAvailable => ai.IsAiAvailable;

    public async Task<ServiceResult<ParsedResumeDto>> ParseAsync(string? userId, Stream file, string fileName,
        ResumeParseMode mode = ResumeParseMode.Rules, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var principal = http.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true)
                principal = (await authentication.GetAuthenticationStateAsync()).User;
            var trustedId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
            if (principal.Identity?.IsAuthenticated != true || !principal.IsInRole(AppRoles.Candidate)
                || string.IsNullOrWhiteSpace(userId) || !string.Equals(userId, trustedId, StringComparison.Ordinal))
                return Unauthorized();
            var active = await db.Users.AsNoTracking().AnyAsync(u => u.Id == trustedId && u.IsActive, ct);
            var role = await (from membership in db.UserRoles
                join storedRole in db.Roles on membership.RoleId equals storedRole.Id
                where membership.UserId == trustedId && storedRole.Name == AppRoles.Candidate
                select membership.UserId).AnyAsync(ct);
            if (!active || !role) return Unauthorized();
            if (!await db.Candidates.AsNoTracking().AnyAsync(c => c.UserId == trustedId, ct))
                return ServiceResult<ParsedResumeDto>.Fail(ServiceErrorType.NotFound, "Candidate profile not found.");
            if (!Enum.IsDefined(mode))
                return ServiceResult<ParsedResumeDto>.Fail(ServiceErrorType.Validation, "Choose a valid parsing mode.");
            var validated = await CvFileValidation.ReadValidatedAsync(file, fileName, ct);
            if (!validated.Success) return ServiceResult<ParsedResumeDto>.Fail(validated.ErrorType!.Value, validated.Errors);
            var extracted = await extractor.ExtractAsync(validated.Data!, ct);
            if (!extracted.Success) return ServiceResult<ParsedResumeDto>.Fail(extracted.ErrorType!.Value, extracted.Errors);
            var catalog = await db.Skills.AsNoTracking().OrderBy(s => s.Id)
                .Select(s => new SkillOptionDto(s.Id, s.Name, s.Category)).ToArrayAsync(ct);
            var local = rulesParser.Parse(extracted.Data!.Text, catalog);
            local = local with { Warnings = local.Warnings.Concat(extracted.Data.Warnings).Distinct().Take(50).ToArray() };
            if (mode == ResumeParseMode.Rules) return ServiceResult<ParsedResumeDto>.Ok(local);
            var suggested = await ai.ParseAsync(trustedId!, extracted.Data.Text, ct);
            if (!suggested.Success)
                return ServiceResult<ParsedResumeDto>.Ok(local with
                {
                    Warnings = local.Warnings.Take(48).Append("AI parsing unavailable, used standard parsing.")
                        .Concat(suggested.Errors.Take(1)).Distinct().ToArray()
                });
            var normalized = normalizer.Normalize(suggested.Data! with
            {
                Summary = local.Summary, SummaryConfidence = local.SummaryConfidence,
                Warnings = suggested.Data!.Warnings.Concat(extracted.Data.Warnings)
                    .Concat(local.Warnings.Where(w => w.StartsWith("Summary ", StringComparison.Ordinal))).ToArray(), ModeUsed = ResumeParseMode.Ai
            }, catalog);
            return ServiceResult<ParsedResumeDto>.Ok(normalized);
        }
        catch (OperationCanceledException)
        {
            return ServiceResult<ParsedResumeDto>.Fail(ServiceErrorType.Validation, "Resume parsing was canceled. Please try again.");
        }
    }
    private static ServiceResult<ParsedResumeDto> Unauthorized() => ServiceResult<ParsedResumeDto>.Fail(
        ServiceErrorType.Unauthorized, "An authenticated candidate is required.");
}

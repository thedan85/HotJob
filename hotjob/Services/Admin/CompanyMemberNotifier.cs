using hotjob.Data;
using hotjob.Models;
using hotjob.Models.Users;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Services.Admin;

/// <summary>Tells a company's employers about an admin decision through their in-app inbox.</summary>
internal static class CompanyMemberNotifier
{
    private const int MaxContentLength = 2000;

    /// <summary>
    /// Stages one notification per Active, non-deactivated membership (pending/rejected members are not told).
    /// Changes are only staged; the caller saves.
    /// </summary>
    public static async Task<int> StageForActiveMembersAsync(
        ApplicationDbContext dbContext,
        int companyId,
        string kind,
        string title,
        string content,
        int? relatedJobId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var recipientIds = await dbContext.Employers
            .Where(employer => employer.CompanyProfileId == companyId
                && employer.MembershipStatus == MembershipStatus.Active
                && employer.IsActive)
            .Select(employer => employer.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (content.Length > MaxContentLength)
            content = content[..MaxContentLength];

        foreach (var recipientId in recipientIds)
        {
            dbContext.Notifications.Add(new Notification
            {
                UserId = recipientId,
                Title = title,
                Content = content,
                Kind = kind,
                RelatedJobPostingId = relatedJobId,
                IsRead = false,
                CreatedAt = now
            });
        }

        return recipientIds.Count;
    }
}

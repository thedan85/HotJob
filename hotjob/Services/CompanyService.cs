using hotjob.Data;
using hotjob.Models.Users;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Services;

public interface ICompanyService
{
    Task<List<Employer>> GetPendingEmployersForOwnerAsync(string ownerUserId);
    Task<bool> ReviewEmployerAsync(int employerId, string actingUserId, bool approve);
}

public class CompanyService : ICompanyService
{
    private readonly ApplicationDbContext _db;

    public CompanyService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<List<Employer>> GetPendingEmployersForOwnerAsync(string ownerUserId)
    {
        var owner = await _db.Employers.FirstOrDefaultAsync(e => e.UserId == ownerUserId && e.IsOwner);
        if (owner == null) return new List<Employer>();

        return await _db.Employers
            .Where(e => e.CompanyProfileId == owner.CompanyProfileId && e.MembershipStatus == MembershipStatus.Pending)
            .Include(e => e.User)
            .ToListAsync();
    }

    public async Task<bool> ReviewEmployerAsync(int employerId, string actingUserId, bool approve)
    {
        var employer = await _db.Employers.Include(e => e.Company).FirstOrDefaultAsync(e => e.Id == employerId);
        if (employer == null) return false;

        // verify acting user is owner of the same company
        var owner = await _db.Employers.FirstOrDefaultAsync(e => e.UserId == actingUserId && e.IsOwner && e.CompanyProfileId == employer.CompanyProfileId);
        if (owner == null) return false;

        if (employer.MembershipStatus != MembershipStatus.Pending) return false;

        employer.MembershipStatus = approve ? MembershipStatus.Active : MembershipStatus.Rejected;
        if (approve)
            employer.JoinedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return true;
    }
}

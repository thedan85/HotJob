using hotjob.Data;
using hotjob.Models.Users;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Tests.AdminTests;

/// <summary>
/// CompanyProfile.IsActive / Employer.IsActive mean "not suspended by an admin". A row nobody
/// suspended must be active, whether EF or raw SQL inserted it, and an explicit false must survive
/// the database default (bool + HasDefaultValue needs the sentinel for that).
/// </summary>
public sealed class ActiveFlagDefaultTests
{
    [Fact]
    public async Task CompanyInsertedWithoutIsActive_StartsActive()
    {
        await using var fx = await TestFixture.CreateAsync();

        await fx.Db.Database.ExecuteSqlRawAsync(
            "INSERT INTO CompanyProfiles (CompanyName, Status) VALUES ('Raw Co', 1)");

        var company = await fx.Db.CompanyProfiles.AsNoTracking().SingleAsync(c => c.CompanyName == "Raw Co");
        Assert.True(company.IsActive);
    }

    [Fact]
    public async Task EmployerInsertedWithoutIsActive_StartsActive()
    {
        await using var fx = await TestFixture.CreateAsync();
        var user = await fx.SeedUserAsync("raw-employer@example.com", role: AppRoles.Employer);
        var company = await MatchTestData.CreateCompanyAsync(fx);

        await fx.Db.Database.ExecuteSqlRawAsync(
            "INSERT INTO Employers (UserId, CompanyProfileId, FullName, IsOwner, MembershipStatus, JoinedAt) " +
            "VALUES ({0}, {1}, 'Raw Employer', 0, 1, {2})",
            user.Id, company.Id, DateTime.UtcNow);

        var employer = await fx.Db.Employers.AsNoTracking().SingleAsync(e => e.UserId == user.Id);
        Assert.True(employer.IsActive);
    }

    [Fact]
    public async Task NewCompanyAndEmployer_AreActiveByDefault()
    {
        await using var fx = await TestFixture.CreateAsync();
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var employer = await MatchTestData.CreateEmployerAsync(fx, company.Id, "default-employer@example.com");

        Assert.True((await fx.Db.CompanyProfiles.AsNoTracking().SingleAsync(c => c.Id == company.Id)).IsActive);
        Assert.True((await fx.Db.Employers.AsNoTracking().SingleAsync(e => e.Id == employer.Id)).IsActive);
    }

    [Fact]
    public async Task ExplicitlyInactiveCompanyAndEmployer_StayInactive()
    {
        await using var fx = await TestFixture.CreateAsync();
        var user = await fx.SeedUserAsync("inactive-employer@example.com", role: AppRoles.Employer);
        var company = new CompanyProfile
        {
            CompanyName = "Suspended Co",
            Status = CompanyStatus.Approved,
            IsActive = false
        };
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();
        var employer = new Employer
        {
            UserId = user.Id,
            CompanyProfileId = company.Id,
            FullName = "Suspended Employer",
            MembershipStatus = MembershipStatus.Active,
            IsActive = false
        };
        fx.Db.Employers.Add(employer);
        await fx.Db.SaveChangesAsync();

        Assert.False((await fx.Db.CompanyProfiles.AsNoTracking().SingleAsync(c => c.Id == company.Id)).IsActive);
        Assert.False((await fx.Db.Employers.AsNoTracking().SingleAsync(e => e.Id == employer.Id)).IsActive);
    }
}

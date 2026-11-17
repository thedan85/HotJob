using hotjob.Data;
using hotjob.DTOs.Employer;
using hotjob.Models;
using hotjob.Models.Resume;
using hotjob.Models.Users;
using hotjob.Repositories.Employer;
using hotjob.Services.Employer;
using hotjob.Tests.Support;

namespace hotjob.Tests;

public sealed class CandidateSearchTests
{
    [Fact]
    public async Task SetDiscoverable_ControlsEmployerSearchVisibility()
    {
        await using var fx = await TestFixture.CreateAsync();
        var employer = await fx.SeedUserAsync("hr@example.com", role: AppRoles.Employer);
        var company = new CompanyProfile { CompanyName = "Acme", Status = CompanyStatus.Approved };
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();
        fx.Db.Employers.Add(new Employer
        {
            UserId = employer.Id,
            CompanyProfileId = company.Id,
            FullName = "HR",
            MembershipStatus = MembershipStatus.Active
        });
        await fx.Db.SaveChangesAsync();

        var candidateUser = await fx.SeedUserAsync("cand@example.com", role: AppRoles.Candidate);
        var candidate = new Candidate { UserId = candidateUser.Id, FullName = "Lan", Position = "Dev" };
        fx.Db.Candidates.Add(candidate);
        await fx.Db.SaveChangesAsync();

        var search = new CandidateSearchService(
            fx.Db,
            new ApplicantRepository(fx.Db),
            new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        var hidden = await search.SearchAsync(employer.Id, new CandidateSearchQuery());
        Assert.True(hidden.Success, string.Join("; ", hidden.Errors));
        Assert.Empty(hidden.Data!.Items);

        var optIn = await search.SetDiscoverableAsync(candidateUser.Id, true);
        Assert.True(optIn.Success, string.Join("; ", optIn.Errors));
        Assert.True(optIn.Data);

        var visible = await search.SearchAsync(employer.Id, new CandidateSearchQuery());
        Assert.True(visible.Success, string.Join("; ", visible.Errors));
        Assert.Single(visible.Data!.Items);
        Assert.Equal(candidate.Id, visible.Data.Items[0].CandidateId);

        var optOut = await search.SetDiscoverableAsync(candidateUser.Id, false);
        Assert.True(optOut.Success, string.Join("; ", optOut.Errors));
        var hiddenAgain = await search.SearchAsync(employer.Id, new CandidateSearchQuery());
        Assert.Empty(hiddenAgain.Data!.Items);
    }
}

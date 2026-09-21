using hotjob.Data;
using hotjob.DTOs.Authentication;
using hotjob.Models.Users;
using hotjob.Services.Common;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Tests;

public sealed class RegisterCandidateTests
{
    private static RegisterAccountRequest ValidCandidate(string email = "cand@example.com") => new()
    {
        Email = email,
        Password = "Str0ng!Pass",
        FullName = "Test Candidate",
        AccountType = AccountType.Candidate
    };

    [Fact]
    public async Task RegisterCandidate_HappyPath_CreatesUserCandidateAndRole()
    {
        await using var fx = await TestFixture.CreateAsync();

        var result = await fx.Service.RegisterAsync(ValidCandidate());

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("cand@example.com", result.Data.Email);
        Assert.True(result.Data.HasCandidateProfile);
        Assert.False(result.Data.HasEmployerProfile);
        Assert.Null(result.Data.CompanyProfileId);
        Assert.False(string.IsNullOrWhiteSpace(result.Data.UserId));

        // DB state assertions
        var user = await fx.UserManager.FindByEmailAsync("cand@example.com");
        Assert.NotNull(user);
        Assert.True(user.IsActive);
        Assert.True(await fx.UserManager.IsInRoleAsync(user, AppRoles.Candidate));

        var candidate = await fx.Db.Candidates.SingleOrDefaultAsync(c => c.UserId == user.Id);
        Assert.NotNull(candidate);
        Assert.Equal("Test Candidate", candidate.FullName);
    }

    [Fact]
    public async Task RegisterCandidate_TrimsEmailAndFullName()
    {
        await using var fx = await TestFixture.CreateAsync();
        var req = ValidCandidate("  spaced@example.com  ");
        req.FullName = "  Padded Name  ";

        var result = await fx.Service.RegisterAsync(req);

        Assert.True(result.Success);
        Assert.Equal("spaced@example.com", result.Data!.Email);
        var user = await fx.UserManager.FindByEmailAsync("spaced@example.com");
        Assert.NotNull(user);
        var candidate = await fx.Db.Candidates.SingleAsync(c => c.UserId == user.Id);
        Assert.Equal("Padded Name", candidate.FullName);
    }

    [Fact]
    public async Task RegisterCandidate_DuplicateEmail_ReturnsConflict()
    {
        await using var fx = await TestFixture.CreateAsync();
        var first = await fx.Service.RegisterAsync(ValidCandidate("dup@example.com"));
        Assert.True(first.Success);

        var second = await fx.Service.RegisterAsync(ValidCandidate("dup@example.com"));

        Assert.False(second.Success);
        Assert.Equal(ServiceErrorType.Conflict, second.ErrorType);
        Assert.Contains(second.Errors, e => e.Contains("already exists", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RegisterCandidate_DuplicateEmail_DifferentCase_ConflictOrValidation()
    {
        // Documents current behaviour: FindByEmailAsync is case-insensitive (Identity normalization),
        // but the Trim-only check at service level may differ from store normalization.
        await using var fx = await TestFixture.CreateAsync();
        var first = await fx.Service.RegisterAsync(ValidCandidate("Case@Test.com"));
        Assert.True(first.Success);

        var second = await fx.Service.RegisterAsync(ValidCandidate("case@test.com"));

        Assert.False(second.Success);
        // Either an explicit Conflict pre-check or an Identity DuplicateEmail/Validation failure is acceptable.
        Assert.True(
            second.ErrorType is ServiceErrorType.Conflict or ServiceErrorType.Validation,
            $"Expected Conflict or Validation, got {second.ErrorType}: {string.Join("; ", second.Errors)}");
    }

    [Fact]
    public async Task RegisterCandidate_NoCandidateDuplicatedOnRetry()
    {
        await using var fx = await TestFixture.CreateAsync();
        await fx.Service.RegisterAsync(ValidCandidate("once@example.com"));
        await fx.Service.RegisterAsync(ValidCandidate("once@example.com"));

        Assert.Equal(1, await fx.Db.Candidates.CountAsync());
        Assert.Equal(1, await fx.Db.Users.CountAsync(u => u.Email == "once@example.com"));
    }
}

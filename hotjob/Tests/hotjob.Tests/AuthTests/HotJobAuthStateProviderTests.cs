using hotjob.Services.Authentication;

namespace hotjob.Tests;

public sealed class HotJobAuthStateProviderTests
{
    private static AuthTokens Tokens(string? displayName, string email = "user@example.com") => new(
        "access",
        DateTime.UtcNow.AddMinutes(60),
        string.Empty,
        DateTime.UtcNow,
        "user-1",
        email,
        ["Candidate"])
    {
        DisplayName = displayName
    };

    [Fact]
    public async Task RefreshIdentity_UpdatesNameAndEmailClaims()
    {
        var session = new AuthSession();
        var provider = new HotJobAuthStateProvider(session);
        provider.SignIn(Tokens("Old Name", "old@example.com"));

        provider.RefreshIdentity("New Name", "new@example.com");

        var after = await provider.GetAuthenticationStateAsync();
        Assert.Equal("New Name", after.User.Identity?.Name);
        Assert.Equal("new@example.com", after.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value);
        Assert.True(session.IsAuthenticated);
    }

    [Fact]
    public async Task RefreshIdentity_Anonymous_DoesNothing()
    {
        var session = new AuthSession();
        var provider = new HotJobAuthStateProvider(session);

        provider.RefreshIdentity("New Name", "new@example.com");

        var state = await provider.GetAuthenticationStateAsync();
        Assert.False(state.User.Identity?.IsAuthenticated ?? false);
    }
}

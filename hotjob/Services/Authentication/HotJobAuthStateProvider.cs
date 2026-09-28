using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace hotjob.Services.Authentication;

public sealed class AuthSession
{
    public AuthTokens? Tokens { get; private set; }

    public bool IsAuthenticated => Tokens is not null;

    public void SetTokens(AuthTokens tokens) => Tokens = tokens;

    public void Clear() => Tokens = null;
}

public sealed class HotJobAuthStateProvider(AuthSession session) : AuthenticationStateProvider
{
    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());

    public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
        Task.FromResult(new AuthenticationState(CreatePrincipal(session.Tokens)));

    public void SignIn(AuthTokens tokens)
    {
        session.SetTokens(tokens);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public void SignOut()
    {
        session.Clear();
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    private static ClaimsPrincipal CreatePrincipal(AuthTokens? tokens)
    {
        if (tokens is null)
        {
            return Anonymous;
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, tokens.UserId),
            new(ClaimTypes.Name, tokens.DisplayName ?? tokens.Email),
            new(ClaimTypes.Email, tokens.Email)
        };
        claims.AddRange(tokens.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "HotJobToken"));
    }
}

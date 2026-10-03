using System.Text.Json;
using hotjob.Components;

namespace hotjob.Tests;

public sealed class AuthApiResultTests
{
    // Mirrors the options Microsoft.JSInterop uses for InvokeAsync<T> (camelCase, case-insensitive).
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void AuthApiResult_DeserializesCamelCaseEnvelope_MapsTokensWithoutRefresh()
    {
        var json = """
        {
          "ok": true,
          "status": 200,
          "data": {
            "accessToken": "a.jwt.token",
            "accessTokenExpiresAtUtc": "2026-10-03T12:00:00Z",
            "userId": "user-1",
            "email": "user@example.com",
            "roles": ["Candidate", "Employer"],
            "displayName": "Nguyen Van A"
          },
          "errors": []
        }
        """;

        var result = JsonSerializer.Deserialize<AuthApiResult>(json, Web);

        Assert.NotNull(result);
        Assert.True(result.Ok);

        var tokens = result.ToTokens();
        Assert.Equal("a.jwt.token", tokens.AccessToken);
        Assert.Equal("user-1", tokens.UserId);
        Assert.Equal("user@example.com", tokens.Email);
        Assert.Equal("Nguyen Van A", tokens.DisplayName);
        Assert.Equal(new[] { "Candidate", "Employer" }, tokens.Roles);
        // The refresh token lives only in the HttpOnly cookie — never in the response body.
        Assert.Equal(string.Empty, tokens.RefreshToken);
    }

    [Fact]
    public void AuthApiResult_Failure_KeepsErrors_AndToTokensThrowsWithoutData()
    {
        var json = """{"ok":false,"status":401,"data":null,"errors":["Invalid email or password."]}""";

        var result = JsonSerializer.Deserialize<AuthApiResult>(json, Web);

        Assert.NotNull(result);
        Assert.False(result.Ok);
        Assert.Equal("Invalid email or password.", result.ErrorMessage);
        Assert.Throws<InvalidOperationException>(() => { _ = result.ToTokens(); });
    }

    [Fact]
    public void AuthApiResult_MissingErrors_UsesFallbackMessage()
    {
        var json = """{"ok":false,"status":500,"data":null,"errors":[]}""";

        var result = JsonSerializer.Deserialize<AuthApiResult>(json, Web);

        Assert.NotNull(result);
        Assert.Contains("try again", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}

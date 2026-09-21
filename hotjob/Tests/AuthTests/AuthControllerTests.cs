using System.Security.Claims;
using hotjob.Controllers;
using hotjob.DTOs.Authentication;
using hotjob.Services.Authentication;
using hotjob.Services.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace hotjob.Tests;

public sealed class AuthControllerTests
{
    private static AuthTokens SampleTokens(string refresh = "raw-refresh-token") => new(
        "access.jwt.token",
        new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc),
        refresh,
        new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc),
        "user-1",
        "user@example.com",
        ["Candidate"]);

    private sealed class StubAuthService : IAuthService
    {
        public ServiceResult<AuthTokens> LoginResult { get; set; } = ServiceResult<AuthTokens>.Ok(SampleTokens());
        public ServiceResult<AuthTokens> RefreshResult { get; set; } = ServiceResult<AuthTokens>.Ok(SampleTokens("rotated"));
        public ServiceResult LogoutResult { get; set; } = ServiceResult.Ok();

        public string? SeenLoginEmail;
        public string? SeenRefreshToken;
        public string? SeenLogoutCookie;
        public string? SeenLogoutUserId;

        public Task<ServiceResult<AuthTokens>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
        {
            SeenLoginEmail = request?.Email;
            return Task.FromResult(LoginResult);
        }

        public Task<ServiceResult<AuthTokens>> RefreshAsync(string? rawRefreshToken, CancellationToken cancellationToken = default)
        {
            SeenRefreshToken = rawRefreshToken;
            return Task.FromResult(RefreshResult);
        }

        public Task<ServiceResult> LogoutAsync(string? rawRefreshToken, string? currentUserId, CancellationToken cancellationToken = default)
        {
            SeenLogoutCookie = rawRefreshToken;
            SeenLogoutUserId = currentUserId;
            return Task.FromResult(LogoutResult);
        }
    }

    private static AuthController CreateController(
        StubAuthService stub,
        string? cookie = null,
        ClaimsPrincipal? user = null,
        bool isHttps = false)
    {
        var controller = new AuthController(stub);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = isHttps ? "https" : "http";
        if (cookie is not null)
        {
            httpContext.Request.Headers.Cookie = $"{AuthController.RefreshCookieName}={cookie}";
        }

        httpContext.User = user ?? new ClaimsPrincipal();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private static string? GetSetCookie(AuthController controller, string headerName = "Set-Cookie")
    {
        controller.Response.Headers.TryGetValue(headerName, out var values);
        return values.ToString();
    }

    [Fact]
    public async Task Login_Success_Returns200_WithBody_AndSetsHttpOnlyCookie()
    {
        var stub = new StubAuthService();
        var controller = CreateController(stub);

        var action = await controller.Login(
            new LoginRequest { Email = "user@example.com", Password = "Str0ng!Pass" },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action);
        var body = Assert.IsType<AuthResponse>(ok.Value);
        Assert.Equal("access.jwt.token", body.AccessToken);
        Assert.Equal("user-1", body.UserId);
        Assert.Equal("user@example.com", body.Email);
        // Response body must never contain the refresh token (cookie-only transport).
        Assert.DoesNotContain("raw-refresh-token", System.Text.Json.JsonSerializer.Serialize(body));

        var setCookie = GetSetCookie(controller);
        Assert.NotNull(setCookie);
        Assert.Contains($"{AuthController.RefreshCookieName}=raw-refresh-token", setCookie);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_Success_OverHttps_SetsSecureCookie()
    {
        var stub = new StubAuthService();
        var controller = CreateController(stub, isHttps: true);

        await controller.Login(new LoginRequest { Email = "u@e.c", Password = "p" }, CancellationToken.None);

        var setCookie = GetSetCookie(controller);
        Assert.NotNull(setCookie);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_Failure_MapsUnauthorized_NoCookie()
    {
        var stub = new StubAuthService
        {
            LoginResult = ServiceResult<AuthTokens>.Fail(ServiceErrorType.Unauthorized, "Invalid email or password.")
        };
        var controller = CreateController(stub);

        var action = await controller.Login(new LoginRequest { Email = "u@e.c", Password = "x" }, CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(action);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorized.StatusCode);
        Assert.True(string.IsNullOrEmpty(GetSetCookie(controller)));
    }

    [Fact]
    public async Task Refresh_PrefersCookieOverBody()
    {
        var stub = new StubAuthService();
        var controller = CreateController(stub, cookie: "cookie-token");

        var action = await controller.Refresh(new RefreshRequest { RefreshToken = "body-token" }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(action);
        Assert.Equal("cookie-token", stub.SeenRefreshToken);
        var setCookie = GetSetCookie(controller);
        Assert.NotNull(setCookie);
        Assert.Contains("rotated", setCookie);
    }

    [Fact]
    public async Task Refresh_FallsBackToBody_WhenNoCookie()
    {
        var stub = new StubAuthService();
        var controller = CreateController(stub);

        await controller.Refresh(new RefreshRequest { RefreshToken = "body-token" }, CancellationToken.None);

        Assert.Equal("body-token", stub.SeenRefreshToken);
    }

    [Fact]
    public async Task Refresh_MissingEverywhere_ForwardsNull()
    {
        var stub = new StubAuthService
        {
            RefreshResult = ServiceResult<AuthTokens>.Fail(ServiceErrorType.Unauthorized, "Refresh token is required.")
        };
        var controller = CreateController(stub);

        var action = await controller.Refresh(new RefreshRequest(), CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(action);
        Assert.Null(stub.SeenRefreshToken);
    }

    [Fact]
    public async Task Refresh_NullBody_StillReadsCookie()
    {
        var stub = new StubAuthService();
        var controller = CreateController(stub, cookie: "cookie-token");

        var action = await controller.Refresh(null, CancellationToken.None);

        Assert.IsType<OkObjectResult>(action);
        Assert.Equal("cookie-token", stub.SeenRefreshToken);
    }

    [Fact]
    public async Task Logout_Success_Returns204_ClearsCookie_ForwardsIdentity()
    {
        var stub = new StubAuthService();
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-9")], "jwt");
        var controller = CreateController(stub, cookie: "cookie-token", user: new ClaimsPrincipal(identity));

        var action = await controller.Logout(CancellationToken.None);

        Assert.IsType<NoContentResult>(action);
        Assert.Equal("cookie-token", stub.SeenLogoutCookie);
        Assert.Equal("user-9", stub.SeenLogoutUserId);
        var setCookie = GetSetCookie(controller);
        Assert.NotNull(setCookie);
        Assert.Contains(AuthController.RefreshCookieName, setCookie);
        Assert.Contains("expires=", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Logout_Unauthenticated_Returns401_StillClearsCookie()
    {
        var stub = new StubAuthService
        {
            LogoutResult = ServiceResult.Fail(ServiceErrorType.Unauthorized, "Not authenticated.")
        };
        var controller = CreateController(stub);

        var action = await controller.Logout(CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(action);
        Assert.NotNull(GetSetCookie(controller));
    }

    [Fact]
    public async Task Logout_ForwardsCancellationToken()
    {
        var stub = new StubAuthService();
        var controller = CreateController(stub);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        CancellationToken? seen = null;
        var capturing = new TokenCapturingAuthService(ct => seen = ct);
        var capturingController = new AuthController(capturing);
        capturingController.ControllerContext = controller.ControllerContext;

        await capturingController.Logout(cts.Token);

        Assert.True(seen?.IsCancellationRequested);
    }

    private sealed class TokenCapturingAuthService(Action<CancellationToken> capture) : IAuthService
    {
        public Task<ServiceResult<AuthTokens>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<ServiceResult<AuthTokens>> RefreshAsync(string? rawRefreshToken, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<ServiceResult> LogoutAsync(string? rawRefreshToken, string? currentUserId, CancellationToken cancellationToken = default)
        {
            capture(cancellationToken);
            return Task.FromResult(ServiceResult.Ok());
        }
    }
}

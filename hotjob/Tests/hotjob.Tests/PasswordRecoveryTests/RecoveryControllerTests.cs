using hotjob.Controllers;
using hotjob.DTOs.Authentication;
using hotjob.Services.Account;
using hotjob.Services.Account.PasswordRecovery;
using hotjob.Services.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace hotjob.Tests.PasswordRecoveryTests;

public sealed class RecoveryControllerTests
{
    [Fact]
    public async Task Forgot_ReturnsGenericNoticeAndUsesServerBaseUri()
    {
        var controller = new AccountController(null!);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.Request.Scheme = "https";
        controller.Request.Host = new HostString("example.test");
        controller.Request.PathBase = "/app";
        var recovery = new StubRecoveryService();
        var response = Assert.IsType<OkObjectResult>(await controller.ForgotPassword(
            new ForgotPasswordRequest { Email = "user@example.test" }, recovery, CancellationToken.None));
        Assert.Equal(PasswordResetService.RequestNotice, response.Value);
        Assert.Equal("https://example.test/app/", recovery.RequestedBaseUrl);
        Assert.NotNull(typeof(AccountController).GetMethod("ForgotPassword")!.GetCustomAttributes(typeof(AllowAnonymousAttribute), false).SingleOrDefault());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Reset_MapsSharedServiceResultsAndIsAnonymous(bool succeeds)
    {
        var recovery = new StubRecoveryService
        {
            ResetResult = succeeds ? ServiceResult.Ok() : ServiceResult.Fail(ServiceErrorType.Validation, PasswordResetService.InvalidLink)
        };
        var response = await new AccountController(null!).ResetPassword(
            new ResetPasswordRequest { Email = "user@example.test", Token = "dummy", NewPassword = "New123!Pass" },
            recovery, CancellationToken.None);
        if (succeeds) Assert.IsType<NoContentResult>(response);
        else Assert.Equal([PasswordResetService.InvalidLink],
            Assert.IsAssignableFrom<IReadOnlyList<string>>(Assert.IsType<BadRequestObjectResult>(response).Value));
        Assert.NotNull(typeof(AccountController).GetMethod("ResetPassword")!.GetCustomAttributes(typeof(AllowAnonymousAttribute), false).SingleOrDefault());
    }
}

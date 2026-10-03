using System.Security.Claims;
using hotjob.Controllers;
using hotjob.DTOs.Authentication;
using hotjob.Services.Account;
using hotjob.Services.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace hotjob.Tests;

public sealed class ProfileControllerTests
{
    [Fact]
    public async Task UpdateProfile_MapsConflict()
    {
        var service = new StubAccountService
        {
            ProfileResult = ServiceResult<ProfileResponse>.Fail(ServiceErrorType.Conflict, "taken")
        };
        var controller = CreateController(service);

        var result = await controller.UpdateProfile(new UpdateProfileRequest(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_Success_ReturnsNoContent()
    {
        var service = new StubAccountService
        {
            PasswordResult = ServiceResult.Ok()
        };
        var controller = CreateController(service);

        var result = await controller.ChangePassword(new ChangePasswordRequest(), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task GetProfile_WithoutClaim_ReturnsUnauthorizedWithoutCallingService()
    {
        var service = new StubAccountService();
        var controller = new AccountController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var result = await controller.GetProfile(CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsType<UnauthorizedObjectResult>(result).StatusCode);
        Assert.False(service.ProfileCalled);
    }

    private static AccountController CreateController(StubAccountService service)
    {
        var controller = new AccountController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, "user-1")],
                        "test"))
                }
            }
        };
        return controller;
    }

    private sealed class StubAccountService : IAccountService
    {
        public ServiceResult<ProfileResponse> ProfileResult { get; init; } =
            ServiceResult<ProfileResponse>.Fail(ServiceErrorType.NotFound);
        public ServiceResult PasswordResult { get; init; } =
            ServiceResult.Fail(ServiceErrorType.Unauthorized);
        public bool ProfileCalled { get; private set; }

        public Task<ServiceResult<RegisteredAccountResponse>> RegisterAsync(
            RegisterAccountRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<RegisteredAccountResponse>.Fail(ServiceErrorType.NotFound));

        public Task<ServiceResult<ProfileResponse>> GetProfileAsync(
            string userId, CancellationToken cancellationToken = default)
        {
            ProfileCalled = true;
            return Task.FromResult(ProfileResult);
        }

        public Task<ServiceResult<ProfileResponse>> UpdateProfileAsync(
            string userId, UpdateProfileRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProfileResult);

        public Task<ServiceResult> ChangePasswordAsync(
            string userId, ChangePasswordRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(PasswordResult);
    }
}

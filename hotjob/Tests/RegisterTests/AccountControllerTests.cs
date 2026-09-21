using hotjob.Controllers;
using hotjob.DTOs.Authentication;
using hotjob.Services.Account;
using hotjob.Services.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace hotjob.Tests;

public sealed class AccountControllerTests
{
    private sealed class StubAccountService : IAccountService
    {
        public ServiceResult<RegisteredAccountResponse> Next { get; set; } =
            ServiceResult<RegisteredAccountResponse>.Ok(
                new RegisteredAccountResponse("u1", "a@b.com", true, false, null));

        public Task<ServiceResult<RegisteredAccountResponse>> RegisterAsync(
            RegisterAccountRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(Next);
    }

    private static RegisterAccountRequest AnyRequest() => new()
    {
        Email = "a@b.com",
        Password = "Str0ng!Pass",
        FullName = "Name",
        AccountType = AccountType.Candidate
    };

    [Fact]
    public async Task Register_Success_Returns201_WithPayload()
    {
        var stub = new StubAccountService();
        var controller = new AccountController(stub);

        var action = await controller.Register(AnyRequest(), CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(action);
        Assert.Equal(StatusCodes.Status201Created, status.StatusCode);
        Assert.Equal(stub.Next.Data, status.Value);
    }

    [Theory]
    [InlineData(ServiceErrorType.Validation, StatusCodes.Status400BadRequest)]
    [InlineData(ServiceErrorType.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(ServiceErrorType.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(ServiceErrorType.Unauthorized, StatusCodes.Status401Unauthorized)]
    public async Task Register_Error_MapsToExpectedStatusCode(
        ServiceErrorType errorType, int expectedStatus)
    {
        var stub = new StubAccountService
        {
            Next = ServiceResult<RegisteredAccountResponse>.Fail(errorType, "boom")
        };
        var controller = new AccountController(stub);

        var action = await controller.Register(AnyRequest(), CancellationToken.None);

        var status = Assert.IsAssignableFrom<ObjectResult>(action);
        Assert.Equal(expectedStatus, status.StatusCode);
    }

    [Fact]
    public async Task Register_ForwardsCancellationToken()
    {
        var tcsService = new TokenCapturingService();
        var controller = new AccountController(tcsService);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await controller.Register(AnyRequest(), cts.Token);

        Assert.True(tcsService.Seen.IsCancellationRequested);
    }

    private sealed class TokenCapturingService : IAccountService
    {
        public CancellationToken Seen;
        public Task<ServiceResult<RegisteredAccountResponse>> RegisterAsync(
            RegisterAccountRequest request, CancellationToken cancellationToken = default)
        {
            Seen = cancellationToken;
            return Task.FromResult(ServiceResult<RegisteredAccountResponse>.Ok(
                new RegisteredAccountResponse("u1", "a@b.com", true, false, null)));
        }
    }
}

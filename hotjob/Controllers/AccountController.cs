using hotjob.DTOs.Authentication;
using hotjob.Services.Account;
using hotjob.Services.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace hotjob.Controllers;

[ApiController]
[Route("api/account")]
public sealed class AccountController(IAccountService accountService) : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(RegisteredAccountResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterAccountRequest request,
        CancellationToken cancellationToken)
    {
        var result = await accountService.RegisterAsync(request, cancellationToken);

        if (result.Success)
        {
            return StatusCode(StatusCodes.Status201Created, result.Data);
        }

        return result.ErrorType switch
        {
            ServiceErrorType.Conflict => Conflict(result.Errors),
            ServiceErrorType.NotFound => NotFound(result.Errors),
            ServiceErrorType.Unauthorized => Unauthorized(result.Errors),
            _ => BadRequest(result.Errors)
        };
    }
}
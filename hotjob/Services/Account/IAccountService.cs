using hotjob.DTOs.Authentication;
using hotjob.Services.Common;

namespace hotjob.Services.Account;

public interface IAccountService
{
    Task<ServiceResult<RegisteredAccountResponse>> RegisterAsync(
        RegisterAccountRequest request,
        CancellationToken cancellationToken = default);
}
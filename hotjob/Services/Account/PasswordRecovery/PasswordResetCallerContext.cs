namespace hotjob.Services.Account.PasswordRecovery;

public sealed class PasswordResetCallerContext
{
    private readonly IHttpContextAccessor accessor;
    private string? capturedIp;

    public PasswordResetCallerContext(IHttpContextAccessor accessor)
    {
        this.accessor = accessor;
        CaptureConnection();
    }

    public string IpAddress => capturedIp ?? "unknown";

    public void CaptureConnection()
    {
        var address = accessor.HttpContext?.Connection.RemoteIpAddress;
        if (address is not null)
            capturedIp = PasswordResetRateLimiter.NormalizeIp(address.ToString());
    }
}

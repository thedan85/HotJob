using Microsoft.AspNetCore.Components.Server.Circuits;

namespace hotjob.Services.Account.PasswordRecovery;

// Capture only the server-observed address while the SignalR connection context is available.
// https://learn.microsoft.com/en-us/aspnet/core/blazor/security/additional-scenarios?view=aspnetcore-10.0#circuit-handler-to-capture-users-for-custom-services
public sealed class PasswordResetCircuitHandler(PasswordResetCallerContext caller) : CircuitHandler
{
    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        caller.CaptureConnection();
        return Task.CompletedTask;
    }
}

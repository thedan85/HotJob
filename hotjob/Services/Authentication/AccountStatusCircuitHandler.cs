using hotjob.Data;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Services.Authentication;

/// <summary>
/// A Blazor circuit keeps its <see cref="AuthSession"/> for as long as the tab stays open, so a
/// user disabled by an admin would otherwise keep working. This handler re-checks the account
/// periodically and signs the circuit out once the user is disabled or deleted.
/// </summary>
public sealed class AccountStatusCircuitHandler(
    AuthSession session,
    HotJobAuthStateProvider authStateProvider,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<AccountStatusCircuitHandler> logger) : CircuitHandler, IDisposable
{
    public static readonly TimeSpan RevalidationInterval = TimeSpan.FromMinutes(1);

    private readonly CancellationTokenSource _stopping = new();

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _ = RunAsync(_stopping.Token);
        return Task.CompletedTask;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _stopping.Cancel();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Checks the signed-in user once; signs the circuit out and returns <c>false</c> when the
    /// account is no longer active. Anonymous circuits return <c>true</c>.
    /// </summary>
    public async Task<bool> RevalidateAsync(CancellationToken cancellationToken)
    {
        var userId = session.Tokens?.UserId;
        if (string.IsNullOrEmpty(userId))
            return true;

        // Own scope: the circuit's DbContext may be in use by a component right now.
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var isActive = await db.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == userId && user.IsActive, cancellationToken)
            .ConfigureAwait(false);
        if (isActive)
            return true;

        // Only sign out if the same user is still on the circuit (they may have switched meanwhile).
        if (session.Tokens?.UserId == userId)
        {
            logger.LogInformation("Signing out circuit for disabled user {UserId}.", userId);
            authStateProvider.SignOut();
        }

        return false;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(RevalidationInterval, timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await RevalidateAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A transient DB error must not kill the loop; the next tick retries.
                    logger.LogWarning(ex, "Account status revalidation failed.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Circuit closed.
        }
    }

    // Cancel only: the loop may still observe the token, and a timer-less CTS holds nothing to free.
    public void Dispose() => _stopping.Cancel();
}

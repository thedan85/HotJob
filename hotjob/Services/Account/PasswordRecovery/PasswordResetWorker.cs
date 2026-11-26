namespace hotjob.Services.Account.PasswordRecovery;

public sealed class PasswordResetWorker(
    PasswordResetQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<PasswordResetWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var job in queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<PasswordResetDelivery>().DeliverAsync(job, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception exception)
                {
                    logger.LogWarning("Password recovery job failed ({FailureType}).", exception.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        queue.Complete();
        return base.StopAsync(cancellationToken);
    }
}

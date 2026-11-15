namespace hotjob.Services.Common;

/// <summary>
/// Serializes database work per Blazor Server circuit. Every scoped service in a
/// circuit shares the one scoped DbContext, so overlapping queries from different
/// components (e.g. the nav badge and the page being restored from the same JS
/// auth call) abort each other with "a second operation was started...".
/// </summary>
public sealed class CircuitDbGate
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<T> RunAsync<T>(Func<Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await operation().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task RunAsync(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await operation().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }
}

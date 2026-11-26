using System.Threading.Channels;

namespace hotjob.Services.Account.PasswordRecovery;

public sealed record PasswordResetJob(string Email, string BaseUrl);

public sealed class PasswordResetQueue
{
    private readonly Channel<PasswordResetJob> channel = Channel.CreateBounded<PasswordResetJob>(
        new BoundedChannelOptions(100) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    public bool TryEnqueue(PasswordResetJob job) => channel.Writer.TryWrite(job);
    public IAsyncEnumerable<PasswordResetJob> ReadAllAsync(CancellationToken ct) => channel.Reader.ReadAllAsync(ct);
    public void Complete() => channel.Writer.TryComplete();
}

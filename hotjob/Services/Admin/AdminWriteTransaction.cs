using hotjob.Data;

namespace hotjob.Services.Admin;

/// <summary>
/// Runs an admin write and its audit entry in one transaction: either both are stored or neither.
/// Same contract as the inline transactions in <see cref="AdminService"/>: the caller must not already
/// own a transaction, and on failure the change tracker is cleared so the context can be reused.
/// </summary>
internal static class AdminWriteTransaction
{
    public static void EnsureNone(ApplicationDbContext dbContext)
    {
        if (dbContext.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Admin write operations require ownership of the database transaction.");
    }

    /// <param name="write">Applies and saves the change, then writes the audit entry.</param>
    public static async Task RunAsync(
        ApplicationDbContext dbContext,
        Func<Task> write,
        CancellationToken cancellationToken)
    {
        EnsureNone(dbContext);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var committed = false;
        try
        {
            await write();
            await transaction.CommitAsync(cancellationToken);
            committed = true;
        }
        finally
        {
            if (!committed)
            {
                try { await transaction.RollbackAsync(CancellationToken.None); }
                finally { dbContext.ChangeTracker.Clear(); }
            }
        }
    }
}

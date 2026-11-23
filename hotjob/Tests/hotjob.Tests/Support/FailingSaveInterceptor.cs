using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace hotjob.Tests.Support;

/// <summary>
/// Throws a unique-constraint <see cref="DbUpdateException"/> on the Nth <c>SaveChanges</c> after being armed.
/// Simulates the race where a pre-check passes and a concurrent writer wins the unique index.
/// </summary>
public sealed class FailingSaveInterceptor : SaveChangesInterceptor
{
    private int _remaining;

    /// <summary>The next <paramref name="nthSave"/>-th save (1 = the very next one) throws.</summary>
    public void ArmOnSave(int nthSave) => _remaining = nthSave;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (_remaining > 0 && --_remaining == 0)
        {
            throw new DbUpdateException(
                "Simulated concurrent insert.",
                new SqliteException("UNIQUE constraint failed: simulated.Column", 19, 2067));
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

using Microsoft.EntityFrameworkCore.Diagnostics;

namespace hotjob.Tests.Support.ResumeParsing;

internal sealed class ParseWriteGuardInterceptor : SaveChangesInterceptor
{
    public bool Armed { get; set; }
    public int WriteAttempts { get; private set; }
    private void Check()
    {
        if (!Armed) return;
        WriteAttempts++;
        throw new InvalidOperationException("Parsing must not save.");
    }
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    { Check(); return result; }
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    { Check(); return ValueTask.FromResult(result); }
}

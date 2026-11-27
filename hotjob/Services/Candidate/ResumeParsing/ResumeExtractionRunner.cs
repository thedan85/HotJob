using hotjob.Services.Common;

namespace hotjob.Services.Candidate.ResumeParsing;

/// <summary>Deadlines bound caller waiting, not synchronous library execution. Slots belong to workers.</summary>
internal sealed class ResumeExtractionRunner(TimeProvider clock)
{
    private readonly SemaphoreSlim slots = new(2, 2);

    public async Task<ServiceResult<ExtractedResumeText>> RunAsync(
        Func<CancellationToken, ExtractedResumeText> extract, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return Fail();
        if (!slots.Wait(0))
            return ServiceResult<ExtractedResumeText>.Fail(ServiceErrorType.Validation, "Resume parsing is busy. Please try again.");
        var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10), clock);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        var token = linked.Token;
        var worker = Task.Run(() =>
        {
            try { token.ThrowIfCancellationRequested(); return extract(token); }
            finally { slots.Release(); }
        }, CancellationToken.None);
        // The continuation, not the timed-out caller, owns disposal and observes late faults.
        _ = worker.ContinueWith(task =>
        {
            _ = task.Exception;
            linked.Dispose();
            timeout.Dispose();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        try
        {
            var result = await worker.WaitAsync(token);
            if (string.IsNullOrWhiteSpace(result.Text))
                return ServiceResult<ExtractedResumeText>.Fail(ServiceErrorType.Validation,
                    "No readable text was found. Please fill in manually; scanned files need OCR.");
            return ServiceResult<ExtractedResumeText>.Ok(result);
        }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
        {
            return Fail();
        }
    }
    private static ServiceResult<ExtractedResumeText> Fail() => ServiceResult<ExtractedResumeText>.Fail(
        ServiceErrorType.Validation, "We could not read this file automatically. Please fill in manually.");
}

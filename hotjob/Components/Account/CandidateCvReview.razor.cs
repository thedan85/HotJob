using System.Security.Claims;
using hotjob.DTOs.Candidate;
using hotjob.Services.Candidate;
using hotjob.Services.Common;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;

namespace hotjob.Components.Account;

/// <summary>
/// Parse a CV into unsaved suggestions, review them, and save exactly once on
/// an explicit Save. Parsing never writes; only Save calls the resume services.
/// </summary>
public sealed partial class CandidateCvReview : ComponentBase, IDisposable
{
    private const long MaxBytes = CvFileStorage.MaxFileSizeBytes;
    private const string GenericParseError = "We could not read this CV. Please try again or enter your details manually.";
    private const string PartialAttachmentMessage = "Resume saved, but the original file was not attached.";
    private const string RefreshFailureMessage = "Resume saved, but the CV list could not be refreshed.";

    [Inject] private IResumeParserService Parser { get; set; } = default!;
    [Inject] private IResumeService ResumeService { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;
    [Inject] private CircuitDbGate Gate { get; set; } = default!;

    [Parameter] public IReadOnlyList<SkillOptionDto> SkillOptions { get; set; } = [];
    [Parameter] public EventCallback<CvReviewSaveResult> OnSaved { get; set; }
    [Parameter] public EventCallback<bool> OnBusyChanged { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }

    private ResumeReviewDraft? draft;
    private EditContext? editContext;
    private IBrowserFile? sourceFile;
    private int? createdResumeId;
    private bool isParsing;
    private bool isSaving;
    private bool attachOriginal = true;
    private bool confirmReplace;
    private bool replaceConfirmed;
    private ResumeParseMode pendingReplaceMode;
    private bool disposed;
    private bool partialSuccess;
    private bool manualEntry;
    private string? statusMessage;
    private readonly List<string> clientErrors = [];
    private CancellationTokenSource? parseCts;
    private int skillPickerId;
    private int skillPickerYears;

    private bool Busy => isParsing || isSaving;

    /// <summary>Once a resume exists, this child never parses, resets or creates again.</summary>
    private bool Created => createdResumeId is not null;

    private async Task NotifyBusyAsync()
    {
        if (disposed) return;
        try { await OnBusyChanged.InvokeAsync(Busy); }
        catch (Exception) { /* parent coordination must not break the child flow */ }
    }

    private IEnumerable<SkillOptionDto> AvailableSkills => SkillOptions
        .Where(option => draft?.Skills.All(skill => skill.SkillId != option.Id) ?? true);

    private void OnFileSelected(InputFileChangeEventArgs args)
    {
        if (Busy || disposed || Created) return;
        ResetReview();
        sourceFile = null;
        statusMessage = null;
        partialSuccess = false;
        var file = args.File;
        if (file is null)
        {
            clientErrors.Add("Choose a CV file.");
            return;
        }
        if (file.Size <= 0)
        {
            clientErrors.Add("The selected file is empty.");
            return;
        }
        if (file.Size > MaxBytes)
        {
            clientErrors.Add("CV files must be 5 MB or smaller.");
            return;
        }

        var extension = Path.GetExtension(file.Name).ToLowerInvariant();
        if (extension == ".doc")
        {
            clientErrors.Add("Only PDF and DOCX files are supported. DOC is not supported; save as DOCX or enter your CV manually.");
            return;
        }
        if (extension is not (".pdf" or ".docx"))
        {
            clientErrors.Add("Only PDF and DOCX files are supported.");
            return;
        }

        sourceFile = file;
        attachOriginal = true;
    }

    private void StartManual()
    {
        if (Busy || disposed || Created) return;
        sourceFile = null;
        attachOriginal = false;
        manualEntry = true;
        confirmReplace = false;
        replaceConfirmed = false;
        statusMessage = null;
        partialSuccess = false;
        clientErrors.Clear();
        draft = new ResumeReviewDraft();
        editContext = new EditContext(draft);
    }

    private Task OnParseRulesClickAsync() => ParseAsync(ResumeParseMode.Rules);

    private Task OnParseAiClickAsync() => ParseAsync(ResumeParseMode.Ai);

    private async Task ParseAsync(ResumeParseMode mode)
    {
        if (Busy || disposed || Created || sourceFile is null) return;
        if (mode == ResumeParseMode.Ai && !Parser.IsAiAvailable)
        {
            clientErrors.Add("AI parsing is not available.");
            return;
        }
        if (draft is not null && !replaceConfirmed)
        {
            confirmReplace = true;
            pendingReplaceMode = mode;
            clientErrors.Clear();
            statusMessage = null;
            return;
        }

        // Busy state and cancellation ownership are set before the first await.
        isParsing = true;
        await NotifyBusyAsync();
        if (disposed) return;
        replaceConfirmed = false;
        confirmReplace = false;
        partialSuccess = false;
        statusMessage = null;
        clientErrors.Clear();
        parseCts?.Dispose();
        parseCts = new CancellationTokenSource();
        var token = parseCts.Token;
        var file = sourceFile;
        try
        {
            var userId = await GetCandidateIdAsync();
            if (disposed || token.IsCancellationRequested || userId is null) return;

            await using var stream = file.OpenReadStream(MaxBytes, token);
            if (disposed || token.IsCancellationRequested) return;

            var result = await Gate.RunAsync(() => Parser.ParseAsync(userId, stream, file.Name, mode, token));
            if (disposed || token.IsCancellationRequested) return;

            if (!result.Success || result.Data is null)
            {
                clientErrors.AddRange(result.Errors.Count == 0
                    ? [GenericParseError]
                    : result.Errors.Where(error => !string.IsNullOrWhiteSpace(error)));
                if (clientErrors.Count == 0) clientErrors.Add(GenericParseError);
                return;
            }

            draft = ResumeReviewDraft.FromParsed(result.Data);
            manualEntry = false;
            editContext = new EditContext(draft);
            if (mode == ResumeParseMode.Ai && result.Data.ModeUsed == ResumeParseMode.Rules)
                statusMessage = "AI was unavailable; local rules were used.";
        }
        catch (OperationCanceledException)
        {
            // Cancellation from leaving/closing is expected.
        }
        catch (Exception)
        {
            if (!disposed) clientErrors.Add(GenericParseError);
        }
        finally
        {
            isParsing = false;
            await NotifyBusyAsync();
        }
    }

    private async Task ConfirmReplaceAsync()
    {
        if (Busy || disposed || Created) return;
        var mode = pendingReplaceMode;
        confirmReplace = false;
        replaceConfirmed = true;
        await ParseAsync(mode);
    }

    private void CancelReplace()
    {
        if (Busy || disposed || Created) return;
        confirmReplace = false;
        replaceConfirmed = false;
        pendingReplaceMode = default;
    }

    private async Task CancelAsync()
    {
        if (isSaving) return;
        CancelParsing();
        isParsing = false;
        try { await OnBusyChanged.InvokeAsync(false); }
        catch (Exception) { /* parent may already be gone */ }
        disposed = true;
        await OnCancel.InvokeAsync();
    }

    private async Task SaveAsync()
    {
        if (Busy || disposed || draft is null || createdResumeId is not null || confirmReplace) return;

        clientErrors.Clear();
        var errors = draft.Validate(SkillOptions).ToList();
        if (editContext is not null)
            errors.AddRange(editContext.GetValidationMessages().Distinct(StringComparer.Ordinal));
        if (errors.Count > 0)
        {
            clientErrors.AddRange(errors);
            return;
        }

        // Busy guard before the first await; snapshot values so edits cannot change mid-save.
        isSaving = true;
        await NotifyBusyAsync();
        if (disposed) return;
        partialSuccess = false;
        statusMessage = null;
        var snapshot = draft.ToInput();
        var file = sourceFile;
        var attach = attachOriginal && file is not null;
        try
        {
            var userId = await GetCandidateIdAsync();
            if (disposed) return;
            if (userId is null)
            {
                clientErrors.Add("Please sign in again to save your CV.");
                return;
            }

            var created = await Gate.RunAsync(() => ResumeService.CreateAsync(userId, snapshot, CancellationToken.None));
            if (disposed) return;
            if (!created.Success || created.Data is null)
            {
                clientErrors.AddRange(created.Errors.Count == 0
                    ? ["We could not save your resume."]
                    : created.Errors.Where(error => !string.IsNullOrWhiteSpace(error)));
                return;
            }

            // Latch the created ID immediately so no later event can create a second row.
            createdResumeId = created.Data.Id;
            if (!attach)
            {
                statusMessage = "Resume saved.";
                if (await SignalSavedAsync(new CvReviewSaveResult(statusMessage, false)))
                {
                    ResetReview();
                    sourceFile = null;
                }
                return;
            }

            var resumeId = createdResumeId.Value;
            try
            {
                await using var uploadStream = file!.OpenReadStream(MaxBytes, CancellationToken.None);
                var uploaded = await Gate.RunAsync(() => ResumeService.UploadFileAsync(
                    userId, resumeId, file.Name, file.Size, uploadStream, CancellationToken.None));
                if (disposed) return;
                if (!uploaded.Success)
                {
                    ReportPartialAttachment(uploaded.Errors);
                    await SignalSavedAsync(new CvReviewSaveResult(statusMessage!, true));
                    return;
                }

                statusMessage = "Resume saved and the original file was attached.";
                // Keep the whole draft and latch until the parent refresh actually succeeds.
                if (await SignalSavedAsync(new CvReviewSaveResult(statusMessage, false)))
                {
                    ResetReview();
                    sourceFile = null;
                }
            }
            catch (Exception)
            {
                if (disposed) return;
                ReportPartialAttachment([]);
                await SignalSavedAsync(new CvReviewSaveResult(statusMessage!, true));
            }
        }
        catch (Exception)
        {
            if (!disposed) clientErrors.Add("We could not save your resume. Please try again.");
        }
        finally
        {
            isSaving = false;
            await NotifyBusyAsync();
        }
    }

    private void ReportPartialAttachment(IReadOnlyList<string> errors)
    {
        partialSuccess = true;
        statusMessage = PartialAttachmentMessage;
        foreach (var error in errors.Where(error => !string.IsNullOrWhiteSpace(error)))
            clientErrors.Add(error);
    }

    private async Task<bool> SignalSavedAsync(CvReviewSaveResult result)
    {
        try
        {
            await OnSaved.InvokeAsync(result);
            return true;
        }
        catch (Exception)
        {
            if (disposed) return false;
            partialSuccess = true;
            if (result.AttachmentFailed)
            {
                // Both failures happened: keep the attachment message and add the refresh message.
                statusMessage = result.Message;
                if (!clientErrors.Contains(RefreshFailureMessage)) clientErrors.Add(RefreshFailureMessage);
            }
            else
            {
                statusMessage = RefreshFailureMessage;
            }
            return false;
        }
    }

    private void AddEducation()
    {
        if (Busy || disposed || Created || draft is null) return;
        draft.Educations.Add(new ResumeReviewDraft.ReviewEducation { IsManual = true });
    }

    private void RemoveEducation(Guid key)
    {
        if (Busy || disposed || Created || draft is null) return;
        draft.Educations.RemoveAll(item => item.Key == key);
    }

    private void AddWork()
    {
        if (Busy || disposed || Created || draft is null) return;
        draft.WorkExperiences.Add(new ResumeReviewDraft.ReviewWork { IsManual = true });
    }

    private void RemoveWork(Guid key)
    {
        if (Busy || disposed || Created || draft is null) return;
        draft.WorkExperiences.RemoveAll(item => item.Key == key);
    }

    private void AddSkill()
    {
        if (Busy || disposed || Created || draft is null) return;
        clientErrors.Clear();
        if (skillPickerId <= 0)
        {
            clientErrors.Add("Choose a skill to add.");
            return;
        }
        if (draft.Skills.Any(skill => skill.SkillId == skillPickerId))
        {
            clientErrors.Add("That skill is already on this review.");
            return;
        }
        if (skillPickerYears is < 0 or > 50)
        {
            clientErrors.Add("Skill experience must be between 0 and 50 years.");
            return;
        }

        var option = SkillOptions.FirstOrDefault(candidate => candidate.Id == skillPickerId);
        draft.Skills.Add(new ResumeReviewDraft.ReviewSkill
        {
            SkillId = skillPickerId,
            SkillName = option?.Name ?? $"Skill #{skillPickerId}",
            YearsOfExperience = skillPickerYears,
            IsManual = true
        });
        skillPickerId = 0;
        skillPickerYears = 0;
    }

    private void RemoveSkill(Guid key)
    {
        if (Busy || disposed || Created || draft is null) return;
        draft.Skills.RemoveAll(skill => skill.Key == key);
    }

    private void ResetReview()
    {
        draft = null;
        editContext = null;
        manualEntry = false;
        // createdResumeId is a lifetime latch: never clear it from a reset path.
        confirmReplace = false;
        replaceConfirmed = false;
        pendingReplaceMode = default;
        clientErrors.Clear();
        skillPickerId = 0;
        skillPickerYears = 0;
    }

    private void CancelParsing()
    {
        parseCts?.Cancel();
        parseCts?.Dispose();
        parseCts = null;
    }

    private async Task<string?> GetCandidateIdAsync()
    {
        var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        var user = state.User;
        if (user.Identity?.IsAuthenticated != true || !user.IsInRole("Candidate")) return null;
        return user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        CancelParsing();
    }
}

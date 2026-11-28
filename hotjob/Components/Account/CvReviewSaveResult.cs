namespace hotjob.Components.Account;

/// <summary>
/// Typed save outcome for the parent page. Never branch on message text: the
/// attachment flag tells Profile whether the child must stay mounted.
/// </summary>
public sealed record CvReviewSaveResult(string Message, bool AttachmentFailed);

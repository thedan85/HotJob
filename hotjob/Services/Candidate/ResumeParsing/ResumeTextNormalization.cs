using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace hotjob.Services.Candidate.ResumeParsing;

internal static class ResumeTextNormalization
{
    public static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    public static string Fold(string text)
    {
        var output = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                output.Append(c is 'đ' or 'Đ' ? 'd' : char.ToLowerInvariant(c));
        return output.ToString().Normalize(NormalizationForm.FormC);
    }
    public static string? Clean(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = Regex.Replace(text.Normalize(), @"[ \t]+", " ", RegexOptions.None, RegexTimeout).Trim();
        return Limit(text, maxLength);
    }
    public static string Limit(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        var end = maxLength;
        if (end > 0 && char.IsHighSurrogate(text[end - 1])) end--;
        return text[..end];
    }
}

using System.Text.RegularExpressions;
using hotjob.Services.Common;

namespace hotjob.Services.Candidate.ResumeParsing;

internal sealed class ResumeContactRedactor(TimeProvider clock)
{
    public ServiceResult<string> Redact(string text)
    {
        var started = clock.GetTimestamp();
        try
        {
            if (text.Length > 100_000) return Fail();
            void Budget()
            {
                if (clock.GetElapsedTime(started) >= TimeSpan.FromSeconds(1)) throw new TimeoutException();
            }
            string Replace(string source, string pattern, MatchEvaluator replacement)
            {
                Budget();
                var value = Regex.Replace(source, pattern, replacement, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
                    ResumeTextNormalization.RegexTimeout);
                Budget();
                return value;
            }
            text = text.Normalize();
            text = string.Join("\n", text.Replace("\r", "").Split('\n').Select(line =>
            {
                Budget();
                var folded = ResumeTextNormalization.Fold(line);
                return Regex.IsMatch(folded, @"^\s*(address|dia chi)\s*:|^\s*\d+[\w /-]*\s+.*\b(street|road|avenue|lane|apartment|duong|pho|ngo|hem)\b",
                    RegexOptions.None, ResumeTextNormalization.RegexTimeout) ? "[CONTACT]" : line;
            }));
            text = Replace(text, @"(?:mailto:)?[\w.!#$%&'*+/=?^`{|}~-]+@[\w-]+(?:\.[\w-]+)+", _ => "[CONTACT]");
            text = Replace(text, @"(?:https?://|ftp://|www\.)[^\s<>]+", _ => "[CONTACT]");
            text = Replace(text, @"\b(?:[\w-]+\.)+[a-z]{2,}(?:/[^\s<>]*)?", m =>
                m.Value.Equals("Node.js", StringComparison.OrdinalIgnoreCase) || m.Value.Equals("ASP.NET", StringComparison.OrdinalIgnoreCase)
                    ? m.Value : "[CONTACT]");
            // Protect actual date ranges before examining numeric contact runs. A greedy
            // phone candidate must never swallow a date and then bypass redaction by length.
            const string date = @"(?:[0-9]{4}(?:-[0-9]{2}-[0-9]{2})?|(?:(?:tháng|thang)\s*)?[0-9]{1,2}/[0-9]{4}|[a-z]{3,9}\s+[0-9]{4})";
            var dates = new List<(string Marker, string Text)>();
            text = Replace(text, $@"\b{date}[ \t]*(?:[-–—]|to|đến|den)[ \t]*(?:{date}|present|hiện tại|hien tai)\b", m =>
            {
                Budget();
                var parsed = new ResumeDateParser().ParseRange(m.Value);
                if (parsed.StartDate is null) return m.Value;
                var marker = $"\uE000RESUMEDATE{dates.Count}\uE001";
                dates.Add((marker, m.Value));
                return marker;
            });
            // Adjacent phone numbers may form a run longer than 15 digits. Redact that
            // whole contact-like run rather than returning it unchanged and leaking both.
            text = Replace(text, @"(?:\+|\b)[0-9][0-9 \t().-]{5,}[0-9](?:[ \t]*(?:ext|x)\.?\s*[0-9]+)?",
                m => m.Value.Count(char.IsDigit) >= 9 ? "[CONTACT]" : m.Value);
            foreach (var (marker, original) in dates)
            {
                Budget();
                text = text.Replace(marker, original, StringComparison.Ordinal);
            }
            text = Replace(text, @"\b(?:linkedin|github|facebook|twitter|social)\s*:\s*@[^\s]+", _ => "[CONTACT]");
            Budget();
            if (string.IsNullOrWhiteSpace(text.Replace("[CONTACT]", ""))) return Fail();
            return ServiceResult<string>.Ok(ResumeTextNormalization.Limit(text, 30_000));
        }
        catch (Exception e) when (e is RegexMatchTimeoutException or TimeoutException or ArgumentException)
        {
            return Fail();
        }
    }
    private static ServiceResult<string> Fail() => ServiceResult<string>.Fail(ServiceErrorType.Validation,
        "Contact redaction could not be completed safely.");
}

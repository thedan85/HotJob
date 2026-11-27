using System.Globalization;
using System.Text.RegularExpressions;

namespace hotjob.Services.Candidate.ResumeParsing;

internal sealed record ResumeDateRange(DateOnly? StartDate, DateOnly? EndDate, bool IsCurrent,
    bool HasInferredPrecision, IReadOnlyList<string> Warnings);

internal sealed class ResumeDateParser
{
    private const string DateToken = @"[0-9]{4}-[0-9]{2}-[0-9]{2}|(?:thang\s*)?[0-9]{1,2}/[0-9]{4}|(?:jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|jun(?:e)?|jul(?:y)?|aug(?:ust)?|sep(?:tember)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?)\s+[0-9]{4}|[0-9]{4}";
    private const string Token = DateToken + "|present|hien tai";
    public ResumeDateRange ParseRange(string source)
    {
        var warnings = new List<string>();
        var folded = ResumeTextNormalization.Fold(source);
        if (folded.Any(c => char.IsDigit(c) && c is not (>= '0' and <= '9')))
            warnings.Add("A date using unsupported digits was omitted.");
        var range = Regex.Match(folded,
            $@"(?<![\p{{L}}\d])(?<start>{DateToken})\s*(?:[-–—]|to|den)\s*(?<end>{Token})(?!\d)",
            RegexOptions.None, ResumeTextNormalization.RegexTimeout);
        var tokens = range.Success ? new[] { range.Groups["start"].Value, range.Groups["end"].Value }
            : Regex.Matches(folded, $@"(?<![\p{{L}}\d])(?:{Token})(?!\d)", RegexOptions.None, ResumeTextNormalization.RegexTimeout)
                .Cast<Match>().Take(1).Select(m => m.Value).ToArray();
        if (tokens.Length == 0) return new(null, null, false, false, warnings);
        var inferred = false;
        DateOnly? Date(string value, bool end)
        {
            if (value is "present" or "hien tai") return null;
            if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact)) return exact;
            if (value.Contains('-')) { warnings.Add("An invalid date was omitted."); return null; }
            int year, month;
            if (Regex.IsMatch(value, @"^[0-9]{4}$", RegexOptions.None, ResumeTextNormalization.RegexTimeout))
            {
                if (!int.TryParse(value, CultureInfo.InvariantCulture, out year)) return null;
                month = end ? 12 : 1;
            }
            else if (value.Contains('/'))
            {
                var split = value.Replace("thang", "").Trim().Split('/');
                if (!int.TryParse(split[0], CultureInfo.InvariantCulture, out month)
                    || !int.TryParse(split[1], CultureInfo.InvariantCulture, out year)) return null;
            }
            else
            {
                var split = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                month = Array.IndexOf(new[] { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" }, split[0][..3]) + 1;
                if (split.Length < 2 || !int.TryParse(split[1], CultureInfo.InvariantCulture, out year)) return null;
            }
            if (year is < 1 or > 9999 || month is < 1 or > 12) { warnings.Add("An invalid date was omitted."); return null; }
            inferred = true;
            return new(year, month, end ? DateTime.DaysInMonth(year, month) : 1);
        }
        var start = Date(tokens[0], false);
        var finish = tokens.Length > 1 ? Date(tokens[1], true) : null;
        var current = tokens.Any(t => t is "present" or "hien tai");
        if (start.HasValue && finish < start) { start = finish = null; warnings.Add("A reversed date range was omitted."); }
        if (inferred) warnings.Add("Incomplete dates use the first day for starts and the last day for ends; please review.");
        return new(start, finish, current, inferred, warnings);
    }
}

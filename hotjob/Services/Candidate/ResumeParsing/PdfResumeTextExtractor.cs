using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace hotjob.Services.Candidate.ResumeParsing;

internal sealed class PdfResumeTextExtractor
{
    public ExtractedResumeText Extract(ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        using var document = PdfDocument.Open(bytes.ToArray());
        var output = new StringBuilder();
        var warnings = new List<string>();
        if (document.NumberOfPages > 15) warnings.Add("Only the first 15 PDF pages were parsed.");
        for (var i = 1; i <= Math.Min(15, document.NumberOfPages); i++)
        {
            ct.ThrowIfCancellationRequested();
            var text = ContentOrderTextExtractor.GetText(document.GetPage(i));
            var remaining = 100_000 - output.Length;
            if (text.Length >= remaining)
            {
                output.Append(ResumeTextNormalization.Limit(text, remaining));
                warnings.Add("Extracted text was limited to 100,000 characters.");
                break;
            }
            output.Append(text);
            if (output.Length < 100_000) output.Append('\n');
        }
        return new(output.ToString(), warnings);
    }
}

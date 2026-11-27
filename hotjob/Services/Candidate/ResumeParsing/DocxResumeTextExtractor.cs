using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace hotjob.Services.Candidate.ResumeParsing;

internal sealed class DocxResumeTextExtractor
{
    public ExtractedResumeText Extract(ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        new DocxArchiveGuard().Validate(bytes, ct);
        using var memory = new MemoryStream(bytes.ToArray());
        using var doc = WordprocessingDocument.Open(memory, false,
            new OpenSettings { AutoSave = false, MaxCharactersInPart = 2_000_000 });
        var part = doc.MainDocumentPart ?? throw new InvalidDataException();
        using var reader = OpenXmlReader.Create(part);
        var text = new StringBuilder();
        var warnings = new List<string>();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            string? next = null;
            if (reader.ElementType == typeof(Text) && reader.IsStartElement) next = reader.GetText();
            else if (reader.ElementType == typeof(Paragraph) && reader.IsEndElement) next = "\n";
            else if (reader.ElementType == typeof(Break) || reader.ElementType == typeof(TabChar)) next = " ";
            if (next is null) continue;
            var remaining = 100_000 - text.Length;
            if (next.Length >= remaining)
            {
                text.Append(ResumeTextNormalization.Limit(next, remaining));
                warnings.Add("Extracted text was limited to 100,000 characters.");
                break;
            }
            text.Append(next);
        }
        return new(text.ToString(), warnings);
    }
}

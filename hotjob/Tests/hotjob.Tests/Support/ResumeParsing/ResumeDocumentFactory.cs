using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace hotjob.Tests.Support.ResumeParsing;

internal static class ResumeDocumentFactory
{
    public static byte[] Pdf(params string[] pages)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var text in pages)
        {
            var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
            var y = 800d;
            foreach (var line in text.Split('\n'))
            {
                page.AddText(line.TrimEnd('\r'), 10, new PdfPoint(20, y), font);
                y -= 13;
            }
        }
        return builder.Build();
    }
    public static byte[] Docx(string text, bool compress = true)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var part = doc.AddMainDocumentPart();
            part.Document = new Document(new Body(text.Split('\n')
                .Select(line => new Paragraph(new Run(new Text(line) { Space = SpaceProcessingModeValues.Preserve })))));
        }
        if (compress) return stream.ToArray();
        using var input = new System.IO.Compression.ZipArchive(new MemoryStream(stream.ToArray()));
        using var output = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(output, System.IO.Compression.ZipArchiveMode.Create, true))
            foreach (var entry in input.Entries)
            {
                using var source = entry.Open();
                using var target = zip.CreateEntry(entry.FullName, System.IO.Compression.CompressionLevel.NoCompression).Open();
                source.CopyTo(target);
            }
        return output.ToArray();
    }
}

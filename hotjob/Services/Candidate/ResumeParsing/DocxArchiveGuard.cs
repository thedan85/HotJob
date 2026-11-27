using System.IO.Compression;
using System.Xml;

namespace hotjob.Services.Candidate.ResumeParsing;

internal sealed class DocxArchiveGuard
{
    public void Validate(ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes.ToArray()), ZipArchiveMode.Read);
        if (zip.Entries.Count > 256 || zip.GetEntry("word/document.xml") is null) throw new InvalidDataException();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var name = entry.FullName;
            if (!names.Add(name) || name.StartsWith('/') || name.Contains('\\') || name.Contains(':')
                || name.Split('/').Any(p => p is "." or "..")) throw new InvalidDataException();
            if (entry.Length > 10 * 1024 * 1024 || entry.Length > Math.Max(1, entry.CompressedLength) * 200L)
                throw new InvalidDataException();
            using var content = entry.Open();
            using var expanded = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = content.Read(buffer)) != 0)
            {
                ct.ThrowIfCancellationRequested();
                total = checked(total + read);
                if (total > 25 * 1024 * 1024 || expanded.Length + read > 10 * 1024 * 1024
                    || expanded.Length + read > Math.Max(1, entry.CompressedLength) * 200L) throw new InvalidDataException();
                expanded.Write(buffer, 0, read);
            }
            if (expanded.Length != entry.Length) throw new InvalidDataException();
            if (!name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
                continue;
            expanded.Position = 0;
            using var reader = XmlReader.Create(expanded, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = 2_000_000, MaxCharactersFromEntities = 1
            });
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                if (reader.Depth > 64) throw new InvalidDataException();
            }
        }
    }
}

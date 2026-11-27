using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace hotjob.Tests.Support.ResumeParsing;

/// <summary>Generated test documents only. Legacy PDF crypto is fixture encoding, never production security.</summary>
internal static class SpecialPdfFactory
{
    public static byte[] ImageOnly()
    {
        using var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        void Chunk(string name, byte[] data)
        {
            var type = Encoding.ASCII.GetBytes(name);
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
            png.Write(length); png.Write(type); png.Write(data);
            var crc = uint.MaxValue;
            foreach (var b in type.Concat(data))
            {
                crc ^= b;
                for (var i = 0; i < 8; i++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320 : crc >> 1;
            }
            BinaryPrimitives.WriteUInt32BigEndian(length, ~crc);
            png.Write(length);
        }
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, 1);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), 1);
        header[8] = 8; header[9] = 2;
        Chunk("IHDR", header);
        using var pixels = new MemoryStream();
        using (var zlib = new ZLibStream(pixels, CompressionLevel.Optimal, true)) zlib.Write(new byte[] { 0, 255, 0, 0 });
        Chunk("IDAT", pixels.ToArray()); Chunk("IEND", []);
        var builder = new PdfDocumentBuilder();
        builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4).AddPng(png.ToArray(), new PdfRectangle(10, 10, 100, 100));
        return builder.Build();
    }

    public static byte[] PasswordProtected(string password)
    {
        byte[] padding = [0x28,0xbf,0x4e,0x5e,0x4e,0x75,0x8a,0x41,0x64,0x00,0x4e,0x56,0xff,0xfa,0x01,0x08,
            0x2e,0x2e,0x00,0xb6,0xd0,0x68,0x3e,0x80,0x2f,0x0c,0xa9,0xfe,0x64,0x53,0x69,0x7a];
        byte[] Pad(string value) => Encoding.ASCII.GetBytes(value).Concat(padding).Take(32).ToArray();
        var owner = Rc4(MD5.HashData(Pad("fixture-owner"))[..5], Pad(password));
        byte[] id = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        byte[] permissions = [0xfc, 0xff, 0xff, 0xff]; // -4, revision 2
        var key = MD5.HashData(Pad(password).Concat(owner).Concat(permissions).Concat(id).ToArray())[..5];
        var user = Rc4(key, padding);
        var objectKey = MD5.HashData(key.Concat(new byte[] { 4, 0, 0, 0, 0 }).ToArray())[..10];
        var content = Rc4(objectKey, "BT /F1 12 Tf 30 700 Td (Skills C#) Tj ET"u8.ToArray());
        using var output = new MemoryStream();
        void Write(string value) => output.Write(Encoding.ASCII.GetBytes(value));
        Write("%PDF-1.4\n");
        var offsets = new List<long> { 0 };
        void Object(int number, string value)
        {
            offsets.Add(output.Position);
            Write($"{number} 0 obj\n{value}\nendobj\n");
        }
        Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Object(2, "<< /Type /Pages /Count 1 /Kids [3 0 R] >>");
        Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>");
        offsets.Add(output.Position);
        Write($"4 0 obj\n<< /Length {content.Length} >>\nstream\n"); output.Write(content); Write("\nendstream\nendobj\n");
        Object(5, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        Object(6, $"<< /Filter /Standard /V 1 /R 2 /Length 40 /O <{Convert.ToHexString(owner)}> /U <{Convert.ToHexString(user)}> /P -4 >>");
        var xref = output.Position;
        Write("xref\n0 7\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) Write($"{offset:D10} 00000 n \n");
        Write($"trailer\n<< /Size 7 /Root 1 0 R /Encrypt 6 0 R /ID [<{Convert.ToHexString(id)}><{Convert.ToHexString(id)}>] >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }
    private static byte[] Rc4(byte[] key, byte[] data)
    {
        var s = Enumerable.Range(0, 256).ToArray();
        var j = 0;
        for (var i = 0; i < 256; i++) { j = (j + s[i] + key[i % key.Length]) & 255; (s[i], s[j]) = (s[j], s[i]); }
        var a = 0; j = 0;
        return data.Select(b =>
        {
            a = (a + 1) & 255; j = (j + s[a]) & 255; (s[a], s[j]) = (s[j], s[a]);
            return (byte)(b ^ s[(s[a] + s[j]) & 255]);
        }).ToArray();
    }
}

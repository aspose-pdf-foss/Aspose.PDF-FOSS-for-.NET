using System.IO;
using Aspose.Pdf;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Optimization;

public class UaUnicodeMappingTests
{
    /// <summary>A page in a Type 1 font whose /Differences give its codes the producer's own
    /// glyph names ("g18" for code 65) over an explicit WinAnsi base encoding.</summary>
    private static byte[] BuildProducerNamedFont()
    {
        const string content = "BT /F1 24 Tf 72 700 Td (AB) Tj ET";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /FirstChar 65 /LastChar 66 /Widths [667 667] " +
                "/Encoding << /Type /Encoding /BaseEncoding /WinAnsiEncoding /Differences [65 /g18 /g19] >> >>",
        };
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Compat.Latin1.GetBytes(s));
        Write("%PDF-1.7\n");
        var offsets = new long[objects.Length];
        for (var i = 0; i < objects.Length; i++)
        {
            offsets[i] = ms.Position;
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = ms.Position;
        Write($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) Write($"{o:D10} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    [Fact]
    public void ProducerGlyphNames_AreMappedToTheBaseEncodingsCharacters_TheTextReadsTheSame()
    {
        using var source = new Document(new MemoryStream(BuildProducerNamedFont()));
        var before = new TextAbsorber();
        source.Pages[1].Accept(before);
        Assert.Equal("AB", before.Text.Trim());

        using var doc = new Document(new MemoryStream(BuildProducerNamedFont()));
        doc.Convert(new PdfFormatConversionOptions(new MemoryStream(), PdfFormat.PDF_UA_1, ConvertErrorAction.None));
        using var output = new MemoryStream();
        doc.Save(output);

        using var saved = new Document(new MemoryStream(output.ToArray()));
        var after = new TextAbsorber();
        saved.Pages[1].Accept(after);
        Assert.Equal(before.Text.Trim(), after.Text.Trim());
        Assert.Contains("<41> <0041>", ToUnicodeText(saved));
    }

    private static string ToUnicodeText(Document doc)
    {
        var reader = doc.Reader;
        var resources = reader.ResolveDict(doc.Pages[1].Dict.Get("Resources"));
        var font = reader.ResolveDict(reader.ResolveDict(resources?.Get("Font"))?.Get("F1"));
        var stream = reader.ResolveStream(font?.Get("ToUnicode"));
        return stream is null ? "" : Compat.Latin1.GetString(reader.DecodeStream(stream));
    }
}

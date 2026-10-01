using System.IO;
using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Fonts;

public class DingbatsEmbeddingTests
{
    /// <summary>A page showing ZapfDingbats code "4" (a check mark), the font not embedded.</summary>
    private static byte[] BuildDingbatsPage()
    {
        const string content = "BT /F1 24 Tf 72 700 Td (4) Tj ET";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /ZapfDingbats >>",
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
    public void Conversion_EmbedsZapfDingbatsAsDingbats_NotAsAText()
    {
        using var doc = new Document(new MemoryStream(BuildDingbatsPage()));
        doc.Convert(new PdfFormatConversionOptions(new MemoryStream(), PdfFormat.PDF_UA_1, ConvertErrorAction.None));
        using var output = new MemoryStream();
        doc.Save(output);

        using var saved = new Document(new MemoryStream(output.ToArray()));
        var raw = Compat.Latin1.GetString(output.ToArray());
        Assert.Contains("+ZapfDingbats", raw);
        Assert.DoesNotContain("Arial", raw);

        var absorber = new TextAbsorber();
        saved.Pages[1].Accept(absorber);
        Assert.Contains("✔", absorber.Text); // HEAVY CHECK MARK, not the digit 4
    }
}

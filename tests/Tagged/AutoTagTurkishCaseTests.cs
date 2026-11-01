using System.IO;
using System.Linq;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

/// <summary>Auto-tagging reads a page the same whatever the culture of the machine: under a Turkish culture, whose
/// case rules pair 'I' with a dotless small letter, an upper-case roman page number is still a page number.</summary>
public class AutoTagTurkishCaseTests
{
    /// <summary>Letter pages drawing <paramref name="pages"/> with F1 Helvetica.</summary>
    private static byte[] Build(params string[] pages)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{string.Join(" ", pages.Select((_, i) => $"{4 + 2 * i} 0 R"))}] /Count {pages.Length} >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
        };
        foreach (var content in pages)
        {
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents {objects.Count + 2} 0 R " +
                        "/Resources << /Font << /F1 3 0 R >> >> >>");
            objects.Add($"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
        }
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Compat.Latin1.GetBytes(s));
        Write("%PDF-1.7\n");
        var offsets = new long[objects.Count];
        for (var i = 0; i < objects.Count; i++)
        {
            offsets[i] = ms.Position;
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = ms.Position;
        Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) Write($"{o:D10} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    private const string Body = "BT /F1 12 Tf 72 700 Td"
        + " (Amber meadows lie beyond the northern fjord where willow and juniper grow along) Tj"
        + " 0 -14 Td (the quiet valley floor and the slopes above it, and the river turns east under) Tj ET\n";

    /// <summary>The text the tagged structure holds, tagged under <paramref name="culture"/>.</summary>
    private static string TaggedText(byte[] pdf, string culture)
    {
        using var scope = new CultureScope(culture);
        using var doc = new Document(new MemoryStream(pdf));
        var options = new PdfFormatConversionOptions(new MemoryStream(), PdfFormat.PDF_UA_1, ConvertErrorAction.None)
        {
            AutoTaggingSettings = new AutoTaggingSettings { EnableAutoTagging = true },
        };
        doc.Convert(options);
        return string.Concat(doc.TaggedContent.StructTreeRootElement.GetMarkedContent(true).Select(i => i.Text));
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("az-Latn-AZ")]
    public void AnUpperCaseRomanPageNumberAtThePageFootIsNoText(string culture)
    {
        // Tagged under the culture first: in a run of this test alone the tagger's patterns are built under it.
        var pdf = Build(Body + "BT /F1 9 Tf 300 30 Td (XI) Tj ET\n", Body + "BT /F1 9 Tf 300 30 Td (XII) Tj ET\n");

        var text = TaggedText(pdf, culture);

        Assert.Contains("Amber meadows", text, StringComparison.Ordinal);
        Assert.DoesNotContain("XI", text, StringComparison.Ordinal);
    }
}

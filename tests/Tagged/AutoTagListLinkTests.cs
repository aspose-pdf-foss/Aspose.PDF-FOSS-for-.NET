using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aspose.Pdf;
using Aspose.Pdf.LogicalStructure;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

/// <summary>Auto-tagging of a list whose items hold links: a link annotation standing on an item's lines is a Link
/// inside the item's body, the body's text around it, as a paragraph's links are.</summary>
public class AutoTagListLinkTests
{
    /// <summary>A Letter page drawing <paramref name="content"/> with F1 Times-Roman, with <paramref name="annots"/>
    /// (a /Annots array's entries).</summary>
    private static byte[] Build(string content, string annots)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [4 0 R] /Count 1 >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Times-Roman /Encoding /WinAnsiEncoding >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 5 0 R /Resources << /Font << /F1 3 0 R >> >> /Annots [{annots}] >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
        };
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

    private static string Uri(string uri, double x0, double y0, double x1, double y1)
        => System.FormattableString.Invariant($"<< /Type /Annot /Subtype /Link /Rect [{x0} {y0} {x1} {y1}] /Border [0 0 0] /A << /S /URI /URI ({uri}) >> >>");

    // Two numbered references, each label a show of its own, its text 31.5 pt further on, the later lines under the
    // text; each ends with its address on a line of its own, and the second's first line names one as well.
    private const string References =
        "BT /F1 12 Tf 77.4 720 Td ([1] ) Tj 31.5 0 Td (Rose S, Borchert O, Mitchell S, Connelly S \\(2020\\) Zero Trust) Tj\n"
        + "0 -13.8 Td (Architecture. NIST Special Publication 800-207.) Tj\n"
        + "0 -13.8 Td (https://doi.org/10.6028/NIST.SP.800-207) Tj ET\n"
        + "BT /F1 12 Tf 77.4 664.8 Td ([2] ) Tj 31.5 0 Td (Weidman J, Grossklags J \\(2017\\) I Like It but I Hate It, see) Tj ET\n"
        + "BT /F1 12 Tf 420 664.8 Td (https://example.org/talk) Tj ET BT /F1 12 Tf 108.9 664.8 Td\n"
        + "0 -13.8 Td (Proceedings of the 33rd Annual Computer Security Applications Conference, pp 212-224.) Tj\n"
        + "0 -13.8 Td (https://doi.org/10.1145/3134600.3134629) Tj ET\n";

    private static readonly string Annots = string.Join(" ",
        Uri("https://doi.org/10.6028/NIST.SP.800-207", 107, 690, 310, 704),
        Uri("https://example.org/talk", 420, 662, 540, 676),
        Uri("https://doi.org/10.1145/3134600.3134629", 107, 635, 310, 649));

    private static Document Tag(byte[] pdf)
    {
        var doc = new Document(new MemoryStream(pdf));
        doc.Convert(new PdfFormatConversionOptions(new MemoryStream(), PdfFormat.PDF_UA_1, ConvertErrorAction.None)
        {
            AutoTaggingSettings = new AutoTaggingSettings { EnableAutoTagging = true },
        });
        return doc;
    }

    private static string TagOf(StructureElement el) => el.StructureType?.Tag ?? el.S.Name;
    private static string TextOf(StructureElement el) => string.Concat(el.GetMarkedContent(true).Select(i => i.Text)).Trim();

    [Fact]
    public void ALinkOnAnItemsLineIsALinkInsideTheItemsBody()
    {
        using var doc = Tag(Build(References, Annots));
        var bodies = doc.TaggedContent.StructTreeRootElement.FindElements<StructureElement>(true).Where(e => TagOf(e) == "LBody").ToList();
        Assert.Equal(2, bodies.Count);

        var links = bodies.Select(b => b.ChildElements.OfType<StructureElement>().Where(c => TagOf(c) == "Link").Select(TextOf).ToList()).ToList();
        Assert.Equal(new[] { "https://doi.org/10.6028/NIST.SP.800-207" }, links[0]);
        Assert.Equal(new[] { "https://example.org/talk", "https://doi.org/10.1145/3134600.3134629" }, links[1]);
        // The body reads on in order, its text around the links.
        Assert.StartsWith("Weidman J, Grossklags J (2017) I Like It but I Hate It, see https://example.org/talk Proceedings", TextOf(bodies[1]), System.StringComparison.Ordinal);
        Assert.EndsWith("pp 212-224. https://doi.org/10.1145/3134600.3134629", TextOf(bodies[1]), System.StringComparison.Ordinal);
    }

    [Fact]
    public void EveryLinkAnnotationIsOwnedOnceAndByItsItem()
    {
        using var doc = Tag(Build(References, Annots));
        var links = doc.TaggedContent.StructTreeRootElement.FindElements<StructureElement>(true).Where(e => TagOf(e) == "Link").ToList();
        // Three Links, none a stray one at the document's end: each inside a list item's body.
        Assert.Equal(3, links.Count);
        Assert.All(links, l => Assert.Equal("LBody", TagOf((StructureElement)l.ParentElement!)));
    }
}

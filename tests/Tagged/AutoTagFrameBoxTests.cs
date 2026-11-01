using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aspose.Pdf.LogicalStructure;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

/// <summary>Auto-tagging of a page laid out in boxes a report generator frames: each side of a box a thin bar filled
/// whole, cut to a mitre at its ends, the boxes a few points apart. The boxes' rules part their text: a title set large in
/// one box beside lines of another reads as its own box's.</summary>
public class AutoTagFrameBoxTests
{
    /// <summary>A letter page drawing <paramref name="content"/> with F1 Helvetica and F2 Helvetica-Bold.</summary>
    private static byte[] Build(string content)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [5 0 R] /Count 1 >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 6 0 R /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> >>",
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

    private static Document Tag(byte[] pdf)
    {
        var doc = new Document(new MemoryStream(pdf));
        doc.Convert(new PdfFormatConversionOptions(new MemoryStream(), PdfFormat.PDF_UA_1, ConvertErrorAction.None)
        {
            AutoTaggingSettings = new AutoTaggingSettings { EnableAutoTagging = true },
        });
        return doc;
    }

    /// <summary>A box framed as the generator draws it: four bars a point thick, each filled whole as a path of four
    /// corners, its inner edge a point shorter at each end (the mitre).</summary>
    private static string Box(double l, double b, double r, double t)
    {
        string Bar(double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3)
            => System.FormattableString.Invariant($"{x0} {y0} m {x1} {y1} l {x2} {y2} l {x3} {y3} l h f*\n");
        return "0 0.5 0.125 rg\n"
               + Bar(l - 0.5, b - 0.5, l - 0.5, t + 0.5, l + 0.5, t - 0.5, l + 0.5, b + 0.5)
               + Bar(r + 0.5, b - 0.5, r + 0.5, t + 0.5, r - 0.5, t - 0.5, r - 0.5, b + 0.5)
               + Bar(l - 0.5, b - 0.5, r + 0.5, b - 0.5, r - 0.5, b + 0.5, l + 0.5, b + 0.5)
               + Bar(l - 0.5, t + 0.5, r + 0.5, t + 0.5, r - 0.5, t - 0.5, l + 0.5, t - 0.5)
               + "0 g\n";
    }

    private static string Text(string font, double size, double x, double y, string text)
        => System.FormattableString.Invariant($"BT /{font} {size} Tf {x} {y} Td ({text}) Tj ET\n");

    [Fact]
    public void ATitleSetLargeInABoxBesideAnothersLinesIsItsOwnBoxsText()
    {
        // The title's box over the logo's box, the field box beside both, 5 pt apart; the title, 22.7 pt, stands level
        // with the field box's third and fourth lines. A box of notes stands 5 pt under them, the footer's box at the foot.
        var fields = new[] { ("Grower", "Farms Ryan"), ("Farm Name", "Ryan Farm Partnership"), ("Farm Size", "1562.2 acres"),
                             ("Field Name", "Matter"), ("Field Size", "145.8 acres"), ("Crop", "Corn"), ("Hybrid", "DKC62-97RIB") };
        var content = Box(72, 654.8, 307.3, 720) + Box(72, 620.8, 307.3, 654.8) + Box(312.9, 620.8, 539.7, 720)
                      + Text("F2", 22.68, 123.3, 679.6, "Field Report");
        for (var i = 0; i < fields.Length; i++)
            content += Text("F2", 10, 318.6, 699.6 - i * 11.5, fields[i].Item1) + Text("F2", 10, 397.7, 699.6 - i * 11.5, fields[i].Item2);
        content += Box(72, 280.6, 539.7, 615.1)
                   + Text("F1", 10, 90, 590, "Field will be managed for max yield using the Winfield Acre approach")
                   + Text("F1", 10, 90, 578.5, "detailed in the crop plan for the coming season and the next.")
                   + Box(72, 79.1, 539.7, 101.8)
                   + Text("F1", 10, 220.8, 85.3, "Prepared on 5/16/2016 by Tom Ryan");
        using var doc = Tag(Build(content));

        // The text of the page's cells and paragraphs, in reading order.
        var texts = doc.TaggedContent.StructTreeRootElement.FindElements<StructureElement>(true)
            .Where(e => e.S.Name is "TD" or "TH" or "P" or "H1" or "H2" or "H3")
            .Select(e => string.Concat(e.GetMarkedContent(true).Select(i => i.Text)).Trim())
            .Where(t => t.Length > 0).ToList();
        var all = string.Join(" || ", texts);

        Assert.DoesNotContain(texts, t => t.Contains("Field Report") && t.Contains("Farm"));
        var title = texts.FindIndex(t => t.Contains("Field Report"));
        var grower = texts.FindIndex(t => t.Contains("Grower"));
        Assert.True(title >= 0 && grower > title, all);
        Assert.True(texts.FindIndex(t => t.Contains("Hybrid")) < texts.FindIndex(t => t.Contains("Field will be managed")), all);
    }
}

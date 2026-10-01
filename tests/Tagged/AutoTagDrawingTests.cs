using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aspose.Pdf;
using Aspose.Pdf.LogicalStructure;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

/// <summary>Auto-tagging of drawings: a cluster of painted paths with its labels is a Figure
/// stating its box, its content read back as a drawing; a frame round a paragraph is none.</summary>
public class AutoTagDrawingTests
{
    private static byte[] Build(string content)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [4 0 R] /Count 1 >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 5 0 R /Resources << /Font << /F1 3 0 R >> >> >>",
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

    private static readonly string[] BodyLines =
    {
        "Amber meadows lie beyond the northern fjord where willow and juniper grow along",
        "the quiet valley floor and the slopes above it, and the river turns east under",
        "the ridge before it reaches the lake, whose shore the road follows for a mile.",
    };

    private static string Body(double top)
        => $"BT /F1 12 Tf 72 {top} Td " + string.Join(" 0 -14 Td ", BodyLines.Select(l => $"({l}) Tj")) + " ET\n";

    /// <summary>A bar chart 200 x 150 pt at (100, 400): an axis, six bars, two labels.</summary>
    private const string Chart =
        "0.5 w 100 400 m 300 400 l S 100 400 m 100 550 l S\n"
        + "0.2 0.4 0.8 rg 110 400 20 60 re f 140 400 20 90 re f 170 400 20 120 re f 200 400 20 70 re f 230 400 20 100 re f 260 400 20 40 re f\n"
        + "BT /F1 8 Tf 110 390 Td (2019) Tj 150 0 Td (2024) Tj ET\n"
        + "BT /F1 8 Tf 60 470 Td (rate) Tj ET\n";

    private const string Caption = "BT /F1 10 Tf 100 372 Td (Figure 1. Rates by year.) Tj ET\n";

    private static Document Tag(byte[] pdf)
    {
        var doc = new Document(new MemoryStream(pdf));
        doc.Convert(new PdfFormatConversionOptions(new MemoryStream(), PdfFormat.PDF_UA_1, ConvertErrorAction.None)
        {
            AutoTaggingSettings = new AutoTaggingSettings { EnableAutoTagging = true },
        });
        return doc;
    }

    private static string Text(StructureElement el) => string.Concat(el.GetMarkedContent(true).Where(i => i.Kind == MarkedContentKind.Text).Select(i => i.Text));

    [Fact]
    public void AClusterOfPathsWithItsLabelsIsAFigureStatingItsBox()
    {
        using var doc = Tag(Build(Body(700) + Chart + Caption));
        var root = doc.TaggedContent.StructTreeRootElement;
        var figure = Assert.Single(root.FindElements<FigureElement>(true));
        var items = figure.GetMarkedContent(true);
        var drawing = Assert.Single(items, i => i.Kind == MarkedContentKind.Drawing);
        Assert.InRange(drawing.Rectangle.LLX, 99, 101);
        Assert.InRange(drawing.Rectangle.URX, 299, 301);
        Assert.InRange(drawing.Rectangle.LLY, 399, 401);
        Assert.InRange(drawing.Rectangle.URY, 549, 551);
        // The labels are the figure's, not the page's text.
        Assert.Contains("2019", Text(figure));
        // The figure row's own paragraph holds the figure; no OTHER paragraph holds its labels.
        var text = root.FindElements<ParagraphElement>(true).Where(p => p.FindElements<FigureElement>(true).Count == 0).ToList();
        Assert.DoesNotContain(text, p => Text(p).Contains("2019") || Text(p).Contains("rate"));
        Assert.Contains(text, p => Text(p).StartsWith("Figure 1."));
        var box = figure.Attributes.GetAttributes(AttributeOwnerStandard.Layout).GetAttribute(AttributeKey.BBox)?.GetArrayNumberValue();
        Assert.NotNull(box);
        // The stated box takes the labels beside the paths in: the "rate" label starts at x 60.
        Assert.InRange(box![0]!.Value, 59, 61);
        Assert.InRange(box[3]!.Value, 549, 551);
    }

    /// <summary>A circle of radius <paramref name="r"/> round (<paramref name="x"/>, <paramref name="y"/>):
    /// four curves.</summary>
    private static string Circle(double x, double y, double r)
    {
        var k = 0.5523 * r;
        return $"{x + r} {y} m {x + r} {y + k} {x + k} {y + r} {x} {y + r} c {x - k} {y + r} {x - r} {y + k} {x - r} {y} c "
               + $"{x - r} {y - k} {x - k} {y - r} {x} {y - r} c {x + k} {y - r} {x + r} {y - k} {x + r} {y} c h ";
    }

    [Fact]
    public void AnEmblemOfAFewShapesFilledWholeIsAFigure()
    {
        // Three shapes, each two rings filled at once (sixteen curves among them all): a logo beside the title.
        var emblem = "0 0 0 rg " + Circle(450, 750, 12) + Circle(450, 750, 6) + "f* "
                     + Circle(480, 750, 12) + Circle(480, 750, 6) + "f* "
                     + Circle(510, 750, 12) + Circle(510, 750, 6) + "f*\n";
        using var doc = Tag(Build(Body(700) + emblem));
        var root = doc.TaggedContent.StructTreeRootElement;
        var figure = Assert.Single(root.FindElements<FigureElement>(true));
        var drawing = Assert.Single(figure.GetMarkedContent(true), i => i.Kind == MarkedContentKind.Drawing);
        Assert.InRange(drawing.Rectangle.LLX, 437, 439);
        Assert.InRange(drawing.Rectangle.URX, 521, 523);
    }

    [Fact]
    public void AFrameRoundAParagraphIsNoFigure()
    {
        var frame = "0.5 w 66 654 m 520 654 l 520 712 l 66 712 l h S\n";
        using var doc = Tag(Build(Body(700) + frame));
        var root = doc.TaggedContent.StructTreeRootElement;
        Assert.Empty(root.FindElements<FigureElement>(true));
        Assert.Contains(root.FindElements<ParagraphElement>(true), p => Text(p).StartsWith("Amber meadows"));
    }
}

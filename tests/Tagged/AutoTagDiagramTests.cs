using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.LogicalStructure;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

/// <summary>Auto-tagging of a diagram drawn as columns of boxes - each column a few boxes and the arrows between them,
/// too few paths to be a drawing alone - side by side a little apart, the last one reached by a connector drawn dot by
/// dot with a gap left in it, labels in the boxes a row across them: the whole diagram is one Figure.</summary>
public class AutoTagDiagramTests
{
    /// <summary>A Letter page drawing <paramref name="content"/> with F1 Helvetica.</summary>
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

    private static string Show(double x, double y, string text) => System.FormattableString.Invariant($"BT /F1 10 Tf {x} {y} Td ({text}) Tj ET\n");

    // A column at x: three boxes 30 by 20, one under another 20 pt apart, a line joining each to the next, each box labelled.
    private static string Column(double x, string index)
        => System.FormattableString.Invariant($"0.75 w {x} 680 30 20 re S {x} 620 30 20 re S {x} 560 30 20 re S ")
           + System.FormattableString.Invariant($"{x + 15} 680 m {x + 15} 640 l S {x + 15} 620 m {x + 15} 580 l S\n")
           + Show(x + 8, 687, "X" + index) + Show(x + 8, 627, "H" + index) + Show(x + 8, 567, "Y" + index);

    // A dotted connector: dots half a point square, a point apart, from one x to another at a height.
    private static string Dots(double from, double to, double y)
    {
        var sb = new StringBuilder();
        for (var x = from; x < to; x += 1) sb.Append(System.FormattableString.Invariant($"{x} {y} 0.5 0.5 re f "));
        return sb.Append('\n').ToString();
    }

    private static string Page()
        => Show(72, 740, "The function is illustrated in the figure below, without the zero block, which changes nothing.")
           + Show(72, 728, "Each block is multiplied in turn, and the products are added into the result as they come.")
           // Two columns 20 pt apart - further than the gap that joins a drawing's paths - and a third further on, a
           // dotted connector running to it with a 10 pt gap left in it.
           + Column(200, "1") + Column(250, "2") + Dots(280, 300, 570) + Dots(310, 330, 570) + Column(330, "m")
           + Show(200, 530, "Figure 1: the function, block by block.")
           + Show(72, 500, "The steps of the function are the following, each applied to the block before it in turn.");

    // A paragraph ending over the diagram in a short line - as short as a label, within a label's reach of the columns.
    private static string PageUnderAParagraph()
        => Show(72, 745, "The function is illustrated in the figure below, without the zero block, which changes nothing.")
           + Show(72, 731, "Each block is multiplied in turn, and the products are added into the result as they come in")
           + Show(72, 717, "into accumulated intermediate results.")
           + Column(200, "1") + Column(250, "2") + Dots(280, 300, 570) + Dots(310, 330, 570) + Column(330, "m")
           + Show(200, 530, "Figure 1: the function, block by block.")
           + Show(72, 500, "The steps of the function are the following, each applied to the block before it in turn.");

    [Fact]
    public void AParagraphsLastLineOverTheDiagramIsNoLabelOfIt()
    {
        using var doc = Tag(Build(PageUnderAParagraph()));
        var root = doc.TaggedContent.StructTreeRootElement;
        var last = Assert.Single(root.GetMarkedContent(true), i => i.Text.Contains("into accumulated intermediate results."));
        Assert.NotEqual("Figure", last.Element?.S.Name);
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

    [Fact]
    public void ADiagramOfColumnsSideBySideIsOneFigure()
    {
        using var doc = Tag(Build(Page()));
        var figures = doc.TaggedContent.StructTreeRootElement.FindElements<StructureElement>(true).Where(e => e.S.Name == "Figure").ToList();
        var figure = Assert.Single(figures);
        var drawn = figure.GetMarkedContent(true).Where(i => i.Kind == MarkedContentKind.Drawing).ToList();
        // It reaches from the first column to the last.
        Assert.InRange(drawn.Min(i => i.Rectangle.LLX), 195, 205);
        Assert.InRange(drawn.Max(i => i.Rectangle.URX), 355, 365);
    }
}

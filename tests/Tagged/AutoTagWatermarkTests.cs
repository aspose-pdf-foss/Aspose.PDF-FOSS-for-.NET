using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aspose.Pdf;
using Aspose.Pdf.LogicalStructure;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

/// <summary>Auto-tagging of text over a watermark: a paragraph's lines running across a shape laid behind them - a
/// running header's mark, drawn as a filled outline of curves - are the paragraph's text, however their shows start
/// inside the shape's box; the shape is a figure of its own.</summary>
public class AutoTagWatermarkTests
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

    private static Document Tag(byte[] pdf)
    {
        var doc = new Document(new MemoryStream(pdf));
        doc.Convert(new PdfFormatConversionOptions(new MemoryStream(), PdfFormat.PDF_UA_1, ConvertErrorAction.None)
        {
            AutoTaggingSettings = new AutoTaggingSettings { EnableAutoTagging = true },
        });
        return doc;
    }

    private static string Text(StructureElement el) => string.Concat(el.GetMarkedContent(true).Select(i => i.Text));

    /// <summary>A light grey band drawn as a filled outline of curves, from (x, y) up and to the right to (x + 360, y + 360):
    /// a watermark's stroke as a header artifact of the page.</summary>
    private static string Watermark(double x, double y)
    {
        var sb = new System.Text.StringBuilder("/Artifact << /Type /Pagination /Subtype /Header >> BDC q 0.753 g\n");
        sb.Append(System.FormattableString.Invariant($"{x} {y} m\n"));
        for (var k = 1; k <= 12; k++)
            sb.Append(System.FormattableString.Invariant($"{x + 30 * k - 20} {y + 30 * k - 10} {x + 30 * k - 10} {y + 30 * k - 20} {x + 30 * k} {y + 30 * k} c\n"));
        for (var k = 12; k >= 1; k--)
            sb.Append(System.FormattableString.Invariant($"{x + 30 * k - 4} {y + 30 * k - 2} {x + 30 * k - 8} {y + 30 * k + 4} {x + 30 * k - 30 + 6} {y + 30 * k - 30 + 8} c\n"));
        return sb.Append("h f* Q EMC\n").ToString();
    }

    // A paragraph's lines as a word processor writes them, each part of a line a text object of its own placed by a
    // matrix, its words kerned apart in a TJ array; the second part of a line starting over the watermark.
    private static string Part(double x, double y, string text)
        => System.FormattableString.Invariant($"q 0 0 612 792 re W* n BT /F1 10.56 Tf 1 0 0 1 {x} {y} Tm 0 g 0 G [({text})] TJ ET Q\n");

    private static double Width(string text) => Aspose.Pdf.Text.FontRepository.FindFont("Helvetica").MeasureString(text, 10.56);

    [Fact]
    public void AParagraphsLinesRunningAcrossAWatermarkAreItsTextNoPartOfTheFigure()
    {
        var content = Watermark(120, 260)
                      + Part(72.024, 600.3, "Lorem Ipsum is simply dummy text of the printing and typesetting industry. It")
                      + Part(72.024, 585.99, "has been the standard dummy text ever since the early days of printing, and it")
                      + Part(72.024, 571.68, "has survived not only five centuries but also the leap into electronic type,")
                      + Part(72.024, 557.37, "and more recently desktop publishing software like ") + Part(72.024 + Width("and more recently desktop publishing software like "), 557.37, "Aldus PageMaker including versions of")
                      + Part(72.024, 543.06, "Lorem Ipsum. It is a long established fact that a reader will be distracted by")
                      + Part(72.024, 528.75, "the readable content of a page when looking at its layout, and the point of it")
                      + Part(72.024, 514.44, "is that it has a more") + Part(72.024 + Width("is that it has a more"), 514.44, "-or-less normal distribution of letters.");
        using var doc = Tag(Build(content));
        var root = doc.TaggedContent.StructTreeRootElement;

        var paragraphs = string.Concat(root.FindElements<ParagraphElement>(true).Select(Text));
        Assert.Contains("software like Aldus PageMaker including versions of", paragraphs);
        Assert.Contains("a more-or-less normal distribution", paragraphs);
        Assert.DoesNotContain(root.FindElements<FigureElement>(true), f => Text(f).Contains("Aldus") || Text(f).Contains("-or-less"));
    }
}

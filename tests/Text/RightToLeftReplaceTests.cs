using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

/// <summary>A Latin placeholder inside a right-to-left line, drawn the way a word processor exports
/// it: every run seated by its own Tm, the Arabic runs in visual order, the line's reading order
/// running from the right, and the placeholder itself split over two runs.</summary>
public class RightToLeftReplaceTests
{
    private const double Baseline = 700;
    private const double FontSize = 12;
    // Helvetica advances: & K E Y = 667 each, _ = 556, X = 667; the Arabic codes are 500 wide.
    private const double KeyWidth = 4 * 667 * FontSize / 1000;
    private const double TailWidth = (556 + 667 + 667) * FontSize / 1000;
    private const double ArabicWidth = 3 * 500 * FontSize / 1000;
    private const double LineRight = 400;
    private const double TailX = LineRight - TailWidth;
    private const double KeyX = TailX - KeyWidth;
    private const double LeftArabicX = KeyX - ArabicWidth;
    private const double LeftColumnX = 72;

    private static string Num(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static Document Build()
    {
        string Run(string font, double x, string text) =>
            $"BT /{font} 12 Tf 1 0 0 1 {Num(x)} {Num(Baseline)} Tm ({text}) Tj ET\n";
        // Stream order follows the reading order: the right Arabic word first, then the
        // placeholder's two halves, then the Arabic word left of it; a Latin column on the same
        // baseline comes last.
        var content = Run("F2", LineRight, "\\001\\002\\003")
                      + Run("F1", KeyX, "&KEY")
                      + Run("F1", TailX, "_X&")
                      + Run("F2", LeftArabicX, "\\003\\002\\001")
                      + Run("F1", LeftColumnX, "Left column");
        var cmap = "/CIDInit /ProcSet findresource begin 12 dict begin begincmap\n" +
                   "/CMapName /Test def /CMapType 2 def\n1 begincodespacerange <00> <FF> endcodespacerange\n" +
                   "3 beginbfchar\n<01> <0627>\n<02> <0644>\n<03> <0645>\nendbfchar\n" +
                   "endcmap CMapName currentdict /CMap defineresource pop end end";
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [5 0 R] /Count 1 >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /FirstChar 1 /LastChar 3 /Widths [500 500 500] /ToUnicode 7 0 R >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 6 0 R /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            $"<< /Length {cmap.Length} >>\nstream\n{cmap}\nendstream",
        };
        var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s), 0, s.Length);
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
        ms.Position = 0;
        return new Document(ms);
    }

    private static List<TextSegment> Segments(Document doc)
    {
        var absorber = new TextFragmentAbsorber();
        doc.Pages[1].Accept(absorber);
        return absorber.TextFragments.SelectMany(f => f.Segments).ToList();
    }

    [Fact]
    public void APlaceholderDrawnAsTwoRunsInARightToLeftLineIsFound()
    {
        using var doc = Build();
        var absorber = new TextFragmentAbsorber("&KEY_X&");
        doc.Pages[1].Accept(absorber);
        Assert.Equal(1, absorber.TextFragments.Count);
    }

    [Fact]
    public void ShiftingTheRestOfARightToLeftLineKeepsTheRightEdgeAndMovesTheWordsToItsLeft()
    {
        using var doc = Build();
        var absorber = new TextFragmentAbsorber("&KEY_X&");
        doc.Pages[1].Accept(absorber);
        var fragment = Assert.Single(absorber.TextFragments.Cast<TextFragment>());
        fragment.ReplaceOptions.ReplaceAdjustmentAction = TextReplaceOptions.ReplaceAdjustment.ShiftRestOfLine;
        fragment.Text = "AB";

        var saved = new MemoryStream();
        doc.Save(saved);
        using var reopened = new Document(saved);
        var segments = Segments(reopened);
        var replaced = Assert.Single(segments, s => s.Text.Contains("AB"));
        var newWidth = 2 * 667 * FontSize / 1000;
        // The replacement ends where the placeholder ended, against the word before it.
        Assert.Equal(LineRight - newWidth, replaced.Position!.XIndent, 1);
        // The word after it in reading order (to its left) closes up by the width lost.
        var lost = KeyWidth + TailWidth - newWidth;
        var leftWord = segments.Where(s => s.Text.Any(c => c >= '؀' && c <= 'ۿ'))
            .OrderBy(s => s.Position!.XIndent).First();
        Assert.Equal(LeftArabicX + lost, leftWord.Position!.XIndent, 1);
        // The Latin column on the same baseline stays where it was.
        var column = Assert.Single(segments, s => s.Text.Contains("Left column"));
        Assert.Equal(LeftColumnX, column.Position!.XIndent, 1);
    }
}

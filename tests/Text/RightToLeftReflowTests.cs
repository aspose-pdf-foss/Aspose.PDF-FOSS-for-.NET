using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

/// <summary>A two-line right-to-left paragraph in a clipped table cell, every run seated by its own
/// Tm the way a word processor exports it, with a Latin placeholder on its first line: a whole-word
/// replacement re-breaks the paragraph in reading order, flush right.</summary>
public class RightToLeftReflowTests
{
    private const double FontSize = 12;
    private const double FirstBaseline = 700;
    private const double LinePitch = 14;
    private const double SecondBaseline = FirstBaseline - LinePitch;
    private const double Right = 400;
    private const double ClipLeft = 320;
    // The Arabic codes are 500 wide, the space 250; K E Y = 667 each in Helvetica.
    private const double Word = 3 * 500 * FontSize / 1000;
    private const double Space = 250 * FontSize / 1000;
    private const double Key = 3 * 667 * FontSize / 1000;

    private static string Num(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static Document Build()
    {
        string Run(string font, double x, double y, string text) =>
            $"q {Num(ClipLeft)} 600 {Num(Right - ClipLeft)} 150 re W n BT /{font} 12 Tf 1 0 0 1 {Num(x)} {Num(y)} Tm ({text}) Tj ET Q\n";
        // Line 1 from the right: W1, space, KEY, space, W2 (full up to the clip); line 2: W3, space, W4.
        var keyX = Right - Word - Space - Key;
        var content = Run("F2", Right - Word, FirstBaseline, "\\003\\003\\003")
                      + Run("F2", Right - Word - Space, FirstBaseline, "\\004")
                      + Run("F1", keyX, FirstBaseline, "KEY")
                      + Run("F2", keyX - Space, FirstBaseline, "\\004")
                      + Run("F2", keyX - Space - Word, FirstBaseline, "\\001\\002\\003")
                      + Run("F2", Right - Word, SecondBaseline, "\\001\\001\\001")
                      + Run("F2", Right - Word - Space, SecondBaseline, "\\004")
                      + Run("F2", Right - 2 * Word - Space, SecondBaseline, "\\002\\002\\002");
        var cmap = "/CIDInit /ProcSet findresource begin 12 dict begin begincmap\n" +
                   "/CMapName /Test def /CMapType 2 def\n1 begincodespacerange <00> <FF> endcodespacerange\n" +
                   "4 beginbfchar\n<01> <0627>\n<02> <0644>\n<03> <0645>\n<04> <0020>\nendbfchar\n" +
                   "endcmap CMapName currentdict /CMap defineresource pop end end";
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [5 0 R] /Count 1 >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /FirstChar 1 /LastChar 4 /Widths [500 500 500 250] /ToUnicode 7 0 R >>",
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

    private static List<TextSegment> ReplaceAndRead(string replacement)
    {
        using var doc = Build();
        var absorber = new TextFragmentAbsorber("KEY");
        doc.Pages[1].Accept(absorber);
        var fragment = Assert.Single(absorber.TextFragments.Cast<TextFragment>());
        fragment.ReplaceOptions.ReplaceAdjustmentAction = TextReplaceOptions.ReplaceAdjustment.WholeWordsHyphenation;
        fragment.Text = replacement;
        var saved = new MemoryStream();
        doc.Save(saved);
        using var reopened = new Document(saved);
        var all = new TextFragmentAbsorber();
        reopened.Pages[1].Accept(all);
        return all.TextFragments.SelectMany(f => f.Segments).ToList();
    }

    // Matched as a set of letters: extraction may give a right-to-left run in either order.
    private static TextSegment Find(List<TextSegment> segments, string letters) =>
        Assert.Single(segments, s => s.Text.Trim().Length == letters.Length && letters.All(c => s.Text.Contains(c))
            && s.Text.Trim().Distinct().Count() == letters.Distinct().Count());

    [Fact]
    public void AShorterReplacementPullsTheNextLinesFirstWordUpFlushWithTheParagraph()
    {
        var segments = ReplaceAndRead("AB");
        var ab = Find(segments, "AB");
        var newWidth = 2 * 667 * FontSize / 1000;
        Assert.Equal(Right - Word - Space - newWidth, ab.Position!.XIndent, 1);
        // W2 closes up to the replacement, and W3 comes up from the second line after a word space.
        var w2 = Find(segments, "الم");
        Assert.Equal(Right - 2 * Word - 2 * Space - newWidth, w2.Position!.XIndent, 1);
        var w3 = Find(segments, "ااا");
        Assert.Equal(ab.Position.YIndent, w3.Position!.YIndent, 1);
        Assert.Equal(Right - 3 * Word - 3 * Space - newWidth, w3.Position!.XIndent, 1);
        // W4 does not fit and opens the second line at the paragraph's right edge.
        var w4 = Find(segments, "للل");
        Assert.Equal(ab.Position.YIndent - LinePitch, w4.Position!.YIndent, 1);
        Assert.Equal(Right - Word, w4.Position!.XIndent, 1);
    }

    [Fact]
    public void ALongerReplacementPushesTheLastWordOfItsLineDownToTheNextLine()
    {
        var segments = ReplaceAndRead("ABCDEFG");
        var w1 = Find(segments, "ممم");
        var w2 = Find(segments, "الم");
        Assert.Equal(w1.Position!.YIndent - LinePitch, w2.Position!.YIndent, 1);
        Assert.Equal(Right - Word, w2.Position!.XIndent, 1);
        var w4 = Find(segments, "للل");
        Assert.Equal(w1.Position.YIndent - LinePitch, w4.Position!.YIndent, 1);
        Assert.Equal(Right - 3 * Word - 2 * Space, w4.Position!.XIndent, 1);
    }
}

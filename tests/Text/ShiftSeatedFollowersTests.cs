using System.IO;
using System.Linq;
using Aspose.Pdf;
using Aspose.Pdf.Tests.Redaction;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

/// <summary>A line exported run by run, each run in a text object of its own seated by an absolute
/// Tm and clipped to its table cell: shifting the rest of the line moves the runs after a
/// replacement by the width it gained or lost, up to the end of the cell; the next cell's text on
/// the same baseline stays put.</summary>
public sealed class ShiftSeatedFollowersTests
{
    private const double FontSize = 12;
    private const double KeyX = 100;
    // Helvetica: K E Y = 667 each; the period after it is drawn 0.05 pt away, the next word a space further.
    private const double KeyWidth = 3 * 667 * FontSize / 1000;
    private const double PeriodX = KeyX + KeyWidth + 0.05;
    private const double WordX = PeriodX + 3.336 + 3.336;
    private const double NextCellX = 320;

    private const string Fonts = "<< /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >> >> >>";

    private static string Num(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static string Run(double clipLeft, double x, string text) =>
        $"q {Num(clipLeft)} 600 250 200 re W n BT /F1 12 Tf 1 0 0 1 {Num(x)} 700 Tm ({text}) Tj ET Q\n";

    private static TextSegment[] ReplaceAndRead(TextReplaceOptions.ReplaceAdjustment adjustment, string replacement)
    {
        var pdf = RawPage.Build(Run(50, KeyX, "KEY") + Run(50, PeriodX, ".") + Run(50, WordX, "Word") + Run(310, NextCellX, "Cell"), Fonts);
        using var doc = new Document(new MemoryStream(pdf));
        var absorber = new TextFragmentAbsorber("KEY");
        doc.Pages[1].Accept(absorber);
        var fragment = Assert.Single(absorber.TextFragments.Cast<TextFragment>());
        fragment.ReplaceOptions.ReplaceAdjustmentAction = adjustment;
        fragment.Text = replacement;
        var saved = new MemoryStream();
        doc.Save(saved);
        using var reopened = new Document(saved);
        var all = new TextFragmentAbsorber();
        reopened.Pages[1].Accept(all);
        return all.TextFragments.SelectMany(f => f.Segments).ToArray();
    }

    private static double X(TextSegment[] segments, string text) =>
        Assert.Single(segments, s => s.Text.Trim() == text).Position!.XIndent;

    [Theory]
    [InlineData("KEYKEY")]
    [InlineData("K")]
    public void TheRunsAfterTheReplacementMoveByTheWidthItGainedOrLost(string replacement)
    {
        var segments = ReplaceAndRead(TextReplaceOptions.ReplaceAdjustment.ShiftRestOfLine, replacement);
        var delta = (replacement.Length - 3) * 667 * FontSize / 1000;
        Assert.Equal(PeriodX + delta, X(segments, "."), 1);
        Assert.Equal(WordX + delta, X(segments, "Word"), 1);
        Assert.Equal(NextCellX, X(segments, "Cell"), 1);
    }

    /// <summary>A source rule under the match that runs on under the next run: the part dressing
    /// that run is drawn again, under it, where the shift moved it.</summary>
    [Fact]
    public void TheSourceRuleUnderAShiftedRunMovesWithIt()
    {
        const double ruleRight = 150;
        var rule = $"q 0 0 1 rg {Num(KeyX)} 698 {Num(ruleRight - KeyX)} 0.72 re f Q\n";
        var pdf = RawPage.Build(rule + Run(50, KeyX, "KEY") + Run(50, PeriodX, ".") + Run(50, WordX, "Word"), Fonts);
        using var doc = new Document(new MemoryStream(pdf));
        var absorber = new TextFragmentAbsorber("KEY") { TextSearchOptions = new TextSearchOptions(false) { SearchForTextRelatedGraphics = true } };
        doc.Pages[1].Accept(absorber);
        var fragment = Assert.Single(absorber.TextFragments.Cast<TextFragment>());
        fragment.ReplaceOptions.ReplaceAdjustmentAction = TextReplaceOptions.ReplaceAdjustment.ShiftRestOfLine;
        fragment.Text = "KEYKEY";
        doc.Save(new MemoryStream());
        var delta = KeyWidth;
        var rects = doc.Pages[1].Contents.Where(op => op.CommandName == "re")
            .Select(op => op.ToString().Split(' ').Take(4).Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray())
            .ToList();
        Assert.Contains(rects, r => System.Math.Abs(r[0] - (KeyX + KeyWidth + delta)) < 0.5
                                    && System.Math.Abs(r[2] - (ruleRight - KeyX - KeyWidth)) < 0.5);
    }

    [Fact]
    public void WithoutAShiftTheRunsAfterTheReplacementStayPut()
    {
        var segments = ReplaceAndRead(TextReplaceOptions.ReplaceAdjustment.None, "KEYKEY");
        Assert.Equal(PeriodX, X(segments, "."), 1);
        Assert.Equal(WordX, X(segments, "Word"), 1);
    }
}

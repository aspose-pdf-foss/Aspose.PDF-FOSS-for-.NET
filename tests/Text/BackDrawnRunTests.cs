using System.IO;
using System.Linq;
using Aspose.Pdf;
using Aspose.Pdf.Tests.Redaction;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

/// <summary>A run drawn back into a gap its line leaves for it - a smaller face, a hair above the
/// baseline, shown after the whole line - reads in place, not at the line's end; drawn twice over
/// itself, it reads once.</summary>
public sealed class BackDrawnRunTests
{
    private const string Before = "All work has been done using accepted standard protocols, practices and methods.";
    private const string After = "test methods may vary";
    private const string Run = "the lab";
    private const double Size = 7;
    private const double RunSize = 5.95;
    private const double Baseline = 700;
    private const double RunRise = 0.4;
    private const double NextLine = 8.1;
    private const double Footer = 77;
    // The line's gap, in thousandths of an em: 19 pt at 7 pt, the run's own width and a hair -
    // the room a proportional line leaves for a name filled in after it.
    private const int Gap = 2720;

    [Fact]
    public void ARunDrawnIntoItsLinesGapReadsInPlace()
    {
        var line = Line(Extracted(Page(runs: 1)));
        Assert.True(System.Text.RegularExpressions.Regex.IsMatch(line, $@"methods\.\s+{Run}\s+test methods may"), line);
        Assert.EndsWith(After, line.TrimEnd());
    }

    [Fact]
    public void ARunDrawnTwiceOverItselfReadsOnce()
    {
        var line = Line(Extracted(Page(runs: 2)));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(line, Run));
        Assert.EndsWith(After, line.TrimEnd());
    }

    [Fact]
    public void ARunDrawnPastItsLinesEndReadsAfterIt()
    {
        // Drawn after the page text, a hair past the line's last word: a line of narrow glyphs holds
        // more characters than grid columns, so the run's grid column lands inside the line - it
        // still reads after the line.
        const string narrow = "ill will fill till lilt" + " ill will fill till lilt ill will fill methods.";
        var lineEnd = GapStart(narrow);
        var content = FormattableString.Invariant($"BT /F1 {Size} Tf 1 0 0 1 72 {Baseline} Tm ({narrow}) Tj ") +
                      FormattableString.Invariant($"0 -{NextLine} Td (from the specified reference methods.) Tj ET\n") +
                      FormattableString.Invariant($"BT /F2 {RunSize} Tf 1 0 0 1 300 {Footer} Tm (Accredited to applicable standards) Tj ET\n") +
                      FormattableString.Invariant($"BT /F2 {RunSize} Tf 1 0 0 1 {lineEnd + 6:R} {Baseline + RunRise:R} Tm (Email: sales) Tj ET\n");
        var line = Line(Extracted(RawPage.Build(content, Fonts)));
        Assert.True(System.Text.RegularExpressions.Regex.IsMatch(line, @"methods\.\s+Email: sales\s*$"), line);
    }

    /// <summary>The line, the paragraph's next line and a footer, then <paramref name="runs"/> copies
    /// of the run at the gap's start - drawn after the page text, back up the page, as a filled-in
    /// name is.</summary>
    private static byte[] Page(int runs)
    {
        var gapStart = GapStart();
        var content = FormattableString.Invariant($"BT /F1 {Size} Tf 1 0 0 1 72 {Baseline} Tm [({Before}) -{Gap} ({After})] TJ ") +
                      FormattableString.Invariant($"0 -{NextLine} Td (from the specified reference methods.) Tj ET\n") +
                      FormattableString.Invariant($"BT /F2 {RunSize} Tf 1 0 0 1 300 {Footer} Tm (Accredited to applicable standards) Tj ET\n");
        for (var i = 0; i < runs; i++)
            content += FormattableString.Invariant($"BT /F2 {RunSize} Tf 1 0 0 1 {gapStart:R} {Baseline + RunRise:R} Tm ({Run}) Tj ET\n");
        return RawPage.Build(content, Fonts);
    }

    /// <summary>Where a line's text ends, read back from the line drawn alone.</summary>
    private static double GapStart(string text = Before)
    {
        var alone = RawPage.Build(FormattableString.Invariant($"BT /F1 {Size} Tf 1 0 0 1 72 {Baseline} Tm ({text}) Tj ET"), Fonts);
        using var document = new Document(new MemoryStream(alone));
        var absorber = new TextFragmentAbsorber();
        document.Pages[1].Accept(absorber);
        return absorber.TextFragments.Cast<TextFragment>().Single().Rectangle!.URX;
    }

    private const string Fonts =
        "<< /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Oblique /Encoding /WinAnsiEncoding >> " +
        "/F2 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >> >> >>";

    private static string Extracted(byte[] pdf)
    {
        using var document = new Document(new MemoryStream(pdf));
        var absorber = new TextAbsorber();
        document.Pages.Accept(absorber);
        return absorber.Text;
    }

    private static string Line(string text) =>
        text.Split('\n').Select(l => l.TrimEnd('\r')).Single(l => l.Contains("methods.") && !l.Contains("from"));
}

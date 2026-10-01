using Aspose.Pdf;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

/// <summary>A paragraph justified by spacing: every line but the last is
/// stretched to the measure with character and word spacing shared by the
/// ratio the caller gives, each line its own text object.</summary>
public sealed class SpacingJustifyTests
{
    private const double Margin = 36;
    private const double Band = 595 - 2 * Margin;
    private const float Size = 12;
    private const string Text = "a justified paragraph whose text is long enough to break into several lines so that the spaces of every line but the last are stretched to the margin and the last line stays as it is";

    [Fact]
    public void EveryLineButTheLastReachesTheMeasure()
    {
        var lines = Lines(Render(f => f.TextState.FormattingOptions.JustifySpacingRatio = 0.75));
        Assert.True(lines.Count >= 2);
        var measurer = TextPaginator.CreateMeasurer("Helvetica", Size, null);
        for (var i = 0; i < lines.Count - 1; i++)
        {
            var (text, tc, tw) = lines[i];
            var glyphs = text.Length;
            var spaces = text.Count(c => c == ' ');
            var stretched = measurer(text) + tc * glyphs + tw * spaces;
            // Tc rides the last glyph too, which the share left out: the line's
            // ink ends one Tc past the measure.
            Assert.Equal(Band + tc, stretched, 2);
            Assert.Equal(3, tw / tc, 3);
        }
        var (_, lastTc, lastTw) = lines[^1];
        Assert.Equal(0, lastTc);
        Assert.Equal(0, lastTw);
    }

    [Fact]
    public void TheRatioSharesTheSlackAndTheLastLineCanStretchToo()
    {
        var glyphsOnly = Lines(Render(f => f.TextState.FormattingOptions.JustifySpacingRatio = 0));
        Assert.True(glyphsOnly[0].Tc > 0);
        Assert.Equal(0, glyphsOnly[0].Tw);
        var wordsOnly = Lines(Render(f => f.TextState.FormattingOptions.JustifySpacingRatio = 1));
        Assert.Equal(0, wordsOnly[0].Tc);
        Assert.True(wordsOnly[0].Tw > 0);
        var all = Lines(Render(f => { f.TextState.FormattingOptions.JustifySpacingRatio = 0.75; f.TextState.FormattingOptions.JustifyLastLine = true; }));
        Assert.True(all[^1].Tc > 0);
    }

    [Fact]
    public void ABaseSpacingRidesEveryGlyphAndTheWrapHonoursWordSpacing()
    {
        var spaced = Lines(Render(f => { f.TextState.FormattingOptions.JustifySpacingRatio = 0.75; f.TextState.CharacterSpacing = 0.5f; }));
        Assert.True(spaced[0].Tc > 0.5);
        var measurer = TextPaginator.CreateMeasurer("Helvetica", Size, null);
        // The justifying share alone reaches past the measure by one glyph's worth;
        // the base half point already rode every glyph when the line was measured.
        var (text, tc, tw) = spaced[0];
        Assert.Equal(Band + (tc - 0.5), measurer(text) + tc * text.Length + tw * text.Count(c => c == ' '), 2);

        var narrow = TextPaginator.WrapToWidth(Text, "Helvetica", Size, 300, null, 0, 0, true, 0);
        var wide = TextPaginator.WrapToWidth(Text, "Helvetica", Size, 300, null, 0, 0, true, 20);
        Assert.True(wide.Count > narrow.Count);
    }

    /// <summary>Each shown line's ink text with the spacing in force when it was shown.</summary>
    private static List<(string Text, double Tc, double Tw)> Lines(List<Operator> ops)
    {
        var lines = new List<(string, double, double)>();
        double tc = 0, tw = 0;
        foreach (var op in ops)
        {
            switch (op)
            {
                case SetCharacterSpacing c: tc = c.CharSpacing; break;
                case SetWordSpacing w: tw = w.WordSpacing; break;
                case ShowText s when s.Text.Trim().Length > 0: lines.Add((s.Text, tc, tw)); break;
            }
        }
        return lines;
    }

    private static List<Operator> Render(Action<TextFragment> shape)
    {
        using var doc = new Document();
        var page = doc.Pages.Add();
        page.PageInfo.Margin = new MarginInfo(Margin, Margin, Margin, Margin);
        var fragment = new TextFragment(Text);
        fragment.TextState.FontSize = Size;
        fragment.HorizontalAlignment = HorizontalAlignment.Justify;
        fragment.TextState.FormattingOptions.HangingBreakSpace = true;
        shape(fragment);
        page.Paragraphs.Add(fragment);
        using var reopened = Document.Open(doc.ToArray());
        return reopened.Pages[1].Contents.ToList();
    }
}

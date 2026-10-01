using Aspose.Pdf;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

/// <summary>How a flow paragraph's text state shapes its glyphs: a rise, a
/// horizontal scaling that every advance follows, and a shear in either
/// direction -- with the paragraph's clip growing to keep them in view.</summary>
public sealed class TextShapeTests
{
    private const double Margin = 36;
    private const double Band = 595 - 2 * Margin;
    private const float Size = 12;

    [Fact]
    public void ARiseAndAScalingAreWrittenAfterThePositionAndTheStroke()
    {
        var ops = Render(f =>
        {
            f.TextState.TextRise = 4;
            f.TextState.HorizontalScaling = 130;
            f.TextState.FormattingOptions.SyntheticBoldPen = 0.4;
        });
        var order = ops.Select(o => o.GetType().Name).ToList();
        Assert.Equal(4, ops.OfType<SetTextRise>().Single().Rise, 6);
        Assert.Equal(130, ops.OfType<SetHorizontalTextScaling>().Single().Scale, 6);
        Assert.True(order.IndexOf(nameof(MoveTextPosition)) < order.IndexOf(nameof(SetTextRenderingMode)));
        Assert.True(order.IndexOf(nameof(SetLineWidth)) < order.IndexOf(nameof(SetTextRise)));
        Assert.True(order.IndexOf(nameof(SetTextRise)) < order.IndexOf(nameof(SetHorizontalTextScaling)));
        Assert.True(order.IndexOf(nameof(SetHorizontalTextScaling)) < order.IndexOf(nameof(ShowText)));
    }

    [Fact]
    public void AScalingWidensTheAlignmentTheWrapAndTheRule()
    {
        var plain = Render(f => f.HorizontalAlignment = HorizontalAlignment.Right);
        var wide = Render(f => { f.HorizontalAlignment = HorizontalAlignment.Right; f.TextState.HorizontalScaling = 150; });
        var measurer = TextPaginator.CreateMeasurer("Helvetica", Size, null);
        var width = measurer("decorated");
        Assert.Equal(plain.OfType<MoveTextPosition>().Single().X - width / 2, wide.OfType<MoveTextPosition>().Single().X, 3);

        var text = "words that wrap onto a second line here";
        var narrowLines = Render(f => { }, text, width: 120).OfType<ShowText>().Count();
        var wideLines = Render(f => f.TextState.HorizontalScaling = 200, text, width: 120).OfType<ShowText>().Count();
        Assert.True(wideLines > narrowLines);

        var ruled = Render(f => { f.TextState.IsUnderline = true; f.TextState.HorizontalScaling = 130; });
        Assert.Equal(width * 1.3, ruled.OfType<Re>().Single(r => r.Height < Size).Width, 3);
    }

    [Fact]
    public void AShearIsTextStateAloneAndASyntheticLeanOccupiesItsLean()
    {
        var upright = Render(f => f.HorizontalAlignment = HorizontalAlignment.Right);
        var sheared = Render(f => { f.HorizontalAlignment = HorizontalAlignment.Right; f.TextState.FormattingOptions.Skew = 0.2; f.TextState.FormattingOptions.Slope = 0.1; });
        var leaning = Render(f => { f.HorizontalAlignment = HorizontalAlignment.Right; f.TextState.FormattingOptions.SyntheticItalicLean = 0.2; });
        var matrix = sheared.OfType<SetTextMatrix>().Single();
        Assert.Equal(0.1, matrix.B, 6);
        Assert.Equal(0.2, matrix.C, 6);
        Assert.Equal(upright.OfType<MoveTextPosition>().Single().X, matrix.E, 6);
        Assert.Equal(upright.OfType<MoveTextPosition>().Single().X - 0.2 * Size, leaning.OfType<SetTextMatrix>().Single().E, 6);
    }

    [Fact]
    public void TheClipGrowsWithASlopeAndARise()
    {
        var level = Clip(Render(f => { }));
        var sloped = Clip(Render(f => f.TextState.FormattingOptions.Slope = 0.2));
        Assert.Equal(level.Y + level.Height + 0.2 * Band, sloped.Y + sloped.Height, 3);
        var lowered = Clip(Render(f => f.TextState.TextRise = -6));
        Assert.Equal(level.Y - 6, lowered.Y, 6);
        var raised = Clip(Render(f => f.TextState.TextRise = 5));
        Assert.Equal(level.Y + level.Height + 5, raised.Y + raised.Height, 6);
    }

    /// <summary>The paragraph's clip: the band-wide rectangle that is not filled.</summary>
    private static Re Clip(List<Operator> ops)
    {
        for (var i = 0; i < ops.Count - 1; i++)
            if (ops[i] is Re re && re.Width >= Band - 1e-6 && ops[i + 1] is Aspose.Pdf.Operators.Clip)
                return re;
        throw new Xunit.Sdk.XunitException("no clip");
    }

    private static List<Operator> Render(Action<TextFragment> shape, string text = "decorated", double width = Band)
    {
        using var doc = new Document();
        var page = doc.Pages.Add();
        page.PageInfo.Margin = new MarginInfo(Margin, Margin, Margin + Band - width, Margin);
        var fragment = new TextFragment(text);
        fragment.TextState.FontSize = Size;
        shape(fragment);
        page.Paragraphs.Add(fragment);
        using var reopened = Document.Open(doc.ToArray());
        return reopened.Pages[1].Contents.ToList();
    }
}

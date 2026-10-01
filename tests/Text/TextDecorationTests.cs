using Aspose.Pdf;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

/// <summary>What a flow paragraph paints beyond its glyphs: a caller's own rule
/// geometry, a synthetic weight or slant and what they occupy, the stroke of a
/// rendering mode, and real-valued opacities on the text, its rules and its
/// background.</summary>
public sealed class TextDecorationTests
{
    private const double Margin = 36;
    private const double Band = 595 - 2 * Margin;
    private const float Size = 12;

    [Fact]
    public void AStyledUnderlineIsCentredAtItsOffsetWithItsThickness()
    {
        var ops = Render(f =>
        {
            f.TextState.IsUnderline = true;
            f.TextState.FormattingOptions.UnderlineStyle = new TextDecorationStyle
                { Thickness = 1, ThicknessEm = 0.5, Offset = -2, OffsetEm = 0.3, Color = Color.Red, LineCap = 1 };
        });
        var rule = ops.OfType<Re>().Single(r => r.Height < Size);
        var baseline = ops.OfType<MoveTextPosition>().Single().Y;
        Assert.Equal(1 + 0.5 * Size, rule.Height, 6);
        Assert.Equal(baseline + (-2 + 0.3 * Size) - rule.Height / 2, rule.Y, 6);
        Assert.Contains(ops.OfType<SetLineCap>(), _ => true);
        Assert.Contains(ops.OfType<SetRGBColor>(), c => c.R > 0.99 && c.G < 0.01);
    }

    [Fact]
    public void AStrikeoutStyleAndAnOpaqueRuleTakeTheirOwnAlphaState()
    {
        var ops = Render(f =>
        {
            f.TextState.IsStrikeOut = true;
            f.TextState.FormattingOptions.StrikeoutStyle = new TextDecorationStyle { Thickness = 0.75, OffsetEm = 7.0 / 24, Opacity = 0.5 };
        });
        var rule = ops.OfType<Re>().Single(r => r.Height < Size);
        var baseline = ops.OfType<MoveTextPosition>().Single().Y;
        Assert.Equal(baseline + 7.0 / 24 * Size - 0.375, rule.Y, 6);
        Assert.Single(ops.OfType<GS>());
    }

    [Fact]
    public void AWrappedRuleStopsBeforeTheHangingSpace()
    {
        var text = "words that wrap onto a second line here";
        var full = Render(f => { f.TextState.IsUnderline = true; f.TextState.FormattingOptions.HangingBreakSpace = true; },
            text, width: 120).OfType<Re>().Where(r => r.Height < Size).ToList();
        Assert.True(full.Count >= 2);
        var measurer = TextPaginator.CreateMeasurer("Helvetica", Size, null);
        var firstLine = TextPaginator.WrapToWidth(text, "Helvetica", Size, 120, null, 0, 0, true)[0];
        Assert.EndsWith(" ", firstLine);
        Assert.Equal(measurer(firstLine.TrimEnd(' ')), full[0].Width, 3);
    }

    [Fact]
    public void ASyntheticWeightStrokesAndOccupiesItsPen()
    {
        var plain = Render(f => f.HorizontalAlignment = HorizontalAlignment.Right);
        var bold = Render(f => { f.HorizontalAlignment = HorizontalAlignment.Right; f.TextState.FormattingOptions.SyntheticBoldPen = 0.4; });
        Assert.Equal(2, bold.OfType<SetTextRenderingMode>().Single().RenderingMode);
        Assert.Equal(0.4, bold.OfType<SetLineWidth>().Single().LineWidth, 6);
        Assert.Equal(plain.OfType<MoveTextPosition>().Single().X - 0.4, bold.OfType<MoveTextPosition>().Single().X, 6);
        Assert.Empty(plain.OfType<SetTextRenderingMode>());
    }

    [Fact]
    public void ASyntheticLeanLeansEveryLineFromTheMarginAndOccupiesItsLean()
    {
        var text = "words that wrap onto a second line here";
        var ops = Render(f => f.TextState.FormattingOptions.SyntheticItalicLean = 0.25, text, width: 120);
        var matrices = ops.OfType<SetTextMatrix>().ToList();
        Assert.True(matrices.Count >= 2);
        Assert.All(matrices, m => Assert.Equal(0.25, m.C, 6));
        Assert.All(matrices, m => Assert.Equal(Margin, m.E, 6));
        var centred = Render(f => { f.HorizontalAlignment = HorizontalAlignment.Center; f.TextState.FormattingOptions.SyntheticItalicLean = 0.25; });
        var upright = Render(f => f.HorizontalAlignment = HorizontalAlignment.Center);
        Assert.Equal(upright.OfType<MoveTextPosition>().Single().X - 0.25 * Size / 2,
            centred.OfType<SetTextMatrix>().Single().E, 6);
    }

    [Fact]
    public void AnExplicitStrokeModeWritesItsPenAndColourAfterThePosition()
    {
        var ops = Render(f =>
        {
            f.TextState.RenderingMode = TextRenderingMode.StrokeText;
            f.TextState.LineWidth = 0.8;
            f.TextState.StrokingColor = Color.Blue;
        });
        var order = ops.Select(o => o.GetType().Name).ToList();
        Assert.True(order.IndexOf(nameof(MoveTextPosition)) < order.IndexOf(nameof(SetTextRenderingMode)));
        Assert.True(order.IndexOf(nameof(SetTextRenderingMode)) < order.IndexOf(nameof(SetLineWidth)));
        Assert.True(order.IndexOf(nameof(SetLineWidth)) < order.IndexOf(nameof(SetRGBColorStroke)));
    }

    [Fact]
    public void RealOpacitiesKeepTheirValues()
    {
        using var doc = new Document();
        var page = doc.Pages.Add();
        page.PageInfo.Margin = new MarginInfo(Margin, Margin, Margin, Margin);
        var fragment = new TextFragment("seen through");
        fragment.TextState.FontSize = Size;
        fragment.TextState.FormattingOptions.Opacity = 0.3;
        page.Paragraphs.Add(fragment);
        var background = new TextFragment("on a wash");
        background.TextState.FontSize = Size;
        background.TextState.BackgroundColor = Color.Yellow;
        background.TextState.FormattingOptions.BlockBackground = true;
        background.TextState.FormattingOptions.BackgroundOpacity = 0.7;
        background.TextState.FormattingOptions.BlockBackgroundOutset = new MarginInfo(5, 20, 15, 10);
        page.Paragraphs.Add(background);
        using var reopened = Document.Open(doc.ToArray());
        Assert.Equal(2, reopened.Pages[1].Contents.OfType<GS>().Select(g => g.Name).Distinct().Count());
        // The wash reaches 5 left and 15 right of the band, 10 above and 20 below it.
        var wash = reopened.Pages[1].Contents.OfType<Re>().Where(r => r.Width > Band).ToList();
        Assert.Contains(wash, r => Math.Abs(r.Width - (Band + 20)) < 1e-6 && Math.Abs(r.X - (Margin - 5)) < 1e-6);
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

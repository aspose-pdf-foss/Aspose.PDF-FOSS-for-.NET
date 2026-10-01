using Aspose.Pdf;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

/// <summary>A flow paragraph with a <see cref="TextFragment.BlockBorder"/>: the bands
/// stand outside the line boxes, the text starts inside the left band, the box
/// grows by the top and bottom bands, and the box's paint precedes its glyphs.</summary>
public sealed class BlockBorderTests
{
    private const double Margin = 36;

    [Fact]
    public void TheBandsStandOutsideTheLineBoxAndTheTextInsideTheLeftOne()
    {
        var (fills, shows) = Render(new BorderInfo(BorderSide.All, 2, Color.Red), background: null);
        var show = Assert.Single(shows);
        Assert.Equal(Margin + 2, show.X, 6);
        // Four bands around one 18 pt line box: 22 tall, the page's content width.
        var top = fills.Single(f => f.H == 2 && f.Y > show.Y);
        var bottom = fills.Single(f => f.H == 2 && f.Y < show.Y);
        Assert.Equal(22, top.Y + 2 - bottom.Y, 3);
        Assert.Equal(Margin, top.X, 6);
        Assert.Equal(595 - 2 * Margin, top.W, 6);
        Assert.Equal(2, fills.Count(f => f.W == 2));
    }

    [Fact]
    public void ABackgroundCoversTheWholeBoxAndPrecedesTheText()
    {
        var (fills, shows) = Render(new BorderInfo(BorderSide.All, 6, Color.Blue), background: Color.Yellow);
        var box = fills.First();
        Assert.Equal(Color.Yellow, box.Color);
        Assert.Equal(595 - 2 * Margin, box.W, 6);
        Assert.Equal(18 + 12, box.H, 3);
        Assert.True(box.Order < shows.Single().Order);
    }

    [Fact]
    public void ASingleSideAddsOnlyItsOwnBand()
    {
        var border = new BorderInfo(BorderSide.Left, 4, Color.Green);
        var (fills, shows) = Render(border, background: null);
        var band = Assert.Single(fills);
        Assert.Equal(4, band.W, 6);
        Assert.Equal(18, band.H, 3);
        Assert.Equal(Margin + 4, shows.Single().X, 6);
    }

    [Fact]
    public void ABackgroundKeptInsideTheBandsLeavesTheTextItsWholeClip()
    {
        var clips = new List<Painted>();
        var (fills, shows) = Render(new BorderInfo(BorderSide.All, 2, Color.Blue), Color.Yellow,
            fragment => fragment.TextState.FormattingOptions.BlockBackgroundOutset = new MarginInfo(-2, -2, -2, -2), clips);
        var box = fills.First(f => f.Color == Color.Yellow);
        Assert.Equal(595 - 2 * Margin - 4, box.W, 6);
        Assert.Equal(Margin + 2, box.X, 6);
        // The line's own clip is not narrowed by the inward reach.
        Assert.All(clips, c => Assert.True(c.X <= Margin + 2 + 1e-6));
        Assert.Equal(Margin + 2, shows.Single().X, 6);
    }

    [Fact]
    public void ABoxBackgroundIsPaintedAtItsOwnOpacity()
    {
        var gs = new List<string>();
        Render(new BorderInfo(BorderSide.All, 2, Color.Blue), Color.Yellow,
            fragment => fragment.TextState.FormattingOptions.BackgroundOpacity = 0.5, states: gs);
        Assert.Single(gs);
    }

    [Fact]
    public void RoundedCornersClipTheBackgroundAndTheRing()
    {
        var states = new List<string>();
        var (fills, _) = Render(new BorderInfo(BorderSide.All, 2, Color.Blue), Color.Yellow,
            fragment => fragment.BlockCornerRadii = new CornerRadii(new CornerRadius(10)), states: states);
        // The background still fills the whole box; the rounding is in its clips,
        // and the ring is cut one inner corner at a time.
        Assert.Contains(fills, f => f.Color == Color.Yellow && Math.Abs(f.W - (595 - 2 * Margin)) < 1e-6);
        Assert.Equal(4, states.Count(s => s == "W*"));
    }

    private static (List<Painted> Fills, List<Show> Shows) Render(BorderInfo border, Color? background,
        Action<TextFragment>? configure = null, List<Painted>? clips = null, List<string>? states = null)
    {
        using var doc = new Document();
        var page = doc.Pages.Add();
        page.PageInfo.Margin = new MarginInfo(Margin, Margin, Margin, Margin);
        var fragment = new TextFragment("bordered") { BlockBorder = border };
        fragment.TextState.FontSize = 12;
        fragment.TextState.LineSpacing = 6;
        fragment.TextState.FormattingOptions.LineSpacing = TextFormattingOptions.LineSpacingMode.LineBox;
        fragment.TextState.LineBoxAscentEm = 0.8;
        fragment.TextState.LineBoxDescentEm = 0.2;
        if (background is { } bg)
        {
            fragment.TextState.BackgroundColor = bg;
            fragment.TextState.FormattingOptions.BlockBackground = true;
        }
        configure?.Invoke(fragment);
        page.Paragraphs.Add(fragment);
        using var reopened = Document.Open(doc.ToArray());

        var fills = new List<Painted>();
        var shows = new List<Show>();
        var order = 0;
        Color? colour = null;
        (double X, double Y, double W, double H)? rect = null;
        double x = 0, y = 0;
        foreach (var op in reopened.Pages[1].Contents)
        {
            order++;
            switch (op)
            {
                case SetRGBColor rg: colour = Color.FromRgb(rg.R, rg.G, rg.B); break;
                case Re re: rect = (re.X, re.Y, re.Width, re.Height); break;
                case Aspose.Pdf.Operators.Fill when rect is { } r: fills.Add(new Painted(r.X, r.Y, r.W, r.H, colour, order)); rect = null; break;
                case Aspose.Pdf.Operators.Clip when rect is { } c: clips?.Add(new Painted(c.X, c.Y, c.W, c.H, null, order)); rect = null; break;
                case GS gs: states?.Add(gs.Name); break;
                case EOClip: states?.Add("W*"); break;
                case MoveTextPosition td: x = td.X; y = td.Y; break;
                case ShowText: shows.Add(new Show(x, y, order)); break;
            }
        }
        return (fills, shows);
    }

    private sealed record Painted(double X, double Y, double W, double H, Color? Color, int Order);

    private sealed record Show(double X, double Y, int Order);
}

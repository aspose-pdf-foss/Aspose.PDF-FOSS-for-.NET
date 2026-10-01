using Aspose.Pdf;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

/// <summary>A flow paragraph laid as a block box: margins, padding, a content
/// width and height, where it stands and where its line sits.</summary>
public sealed class BlockBoxTests
{
    private const double Margin = 36;
    private const double Band = 595 - 2 * Margin;

    [Fact]
    public void PaddingLiesInsideTheBackgroundAndOutsideTheLine()
    {
        var (box, show) = Render(f => f.BlockPadding = new MarginInfo(10, 10, 10, 10));
        Assert.Equal(Band, box.W, 6);
        Assert.Equal(18 + 20, box.H, 3);
        Assert.Equal(Margin + 10, show.X, 6);
    }

    [Fact]
    public void AContentWidthStandsWhereTheBlockIsAligned()
    {
        var (left, _) = Render(f => { f.BlockWidth = 200; f.BlockPadding = new MarginInfo(8, 8, 8, 8); });
        Assert.Equal(216, left.W, 6);
        Assert.Equal(Margin, left.X, 6);
        var (centred, _) = Render(f => { f.BlockWidth = 200; f.BlockHorizontalAlignment = HorizontalAlignment.Center; });
        Assert.Equal(Margin + (Band - 200) / 2, centred.X, 6);
        var (right, _) = Render(f => { f.BlockWidth = 200; f.BlockHorizontalAlignment = HorizontalAlignment.Right; });
        Assert.Equal(Margin + Band - 200, right.X, 6);
    }

    [Fact]
    public void LeftAndRightMarginsNarrowTheBoxWhenBoxed()
    {
        var (box, show) = Render(f => { f.BlockBoxed = true; f.Margin.Left = 40; f.Margin.Right = 60; });
        Assert.Equal(Margin + 40, box.X, 6);
        Assert.Equal(Band - 100, box.W, 6);
        Assert.Equal(Margin + 40, show.X, 6);
    }

    [Fact]
    public void AFixedHeightSeatsOneLineTopMiddleOrBottom()
    {
        var (top, topShow) = Render(f => f.BoxHeight = 60);
        Assert.Equal(60, top.H, 6);
        var (_, bottomShow) = Render(f => { f.BoxHeight = 60; f.BlockVerticalAlignment = VerticalAlignment.Bottom; });
        var (_, middleShow) = Render(f => { f.BoxHeight = 60; f.BlockVerticalAlignment = VerticalAlignment.Center; });
        // The face's descent (0.2 em of 12 = 2.4) sits flush with the box bottom;
        // the top seat is the declared line box's (half of 6 surplus + 0.8 em = 12.6).
        var boxTop = top.Y + top.H;
        Assert.Equal(boxTop - 12.6, topShow.Y, 3);
        Assert.Equal(boxTop - 60 + 2.4, bottomShow.Y, 3);
        Assert.Equal((topShow.Y + bottomShow.Y) / 2, middleShow.Y, 3);
    }

    [Fact]
    public void LinesBeyondAFixedHeightAreDropped()
    {
        var shows = RenderAll(f => { f.BlockWidth = 120; f.BoxHeight = 30; },
            "many words that wrap onto several lines in a narrow box for sure");
        Assert.Single(shows);
    }

    [Fact]
    public void AMinHeightFloorsTheBoxAndAMaxWidthCapsIt()
    {
        var (floored, _) = Render(f => f.BoxMinHeight = 50);
        Assert.Equal(50, floored.H, 6);
        var (capped, _) = Render(f => f.BlockMaxWidth = 150);
        Assert.Equal(150, capped.W, 6);
    }

    private static (Painted Box, Show Show) Render(Action<TextFragment> shape, string text = "boxed")
    {
        var (boxes, shows) = RenderRaw(shape, text);
        return (boxes.Single(), shows.Single());
    }

    private static List<Show> RenderAll(Action<TextFragment> shape, string text) => RenderRaw(shape, text).Shows;

    private static (List<Painted> Boxes, List<Show> Shows) RenderRaw(Action<TextFragment> shape, string text)
    {
        using var doc = new Document();
        var page = doc.Pages.Add();
        page.PageInfo.Margin = new MarginInfo(Margin, Margin, Margin, Margin);
        var fragment = new TextFragment(text);
        fragment.TextState.FontSize = 12;
        fragment.TextState.LineSpacing = 6;
        fragment.TextState.FormattingOptions.LineSpacing = TextFormattingOptions.LineSpacingMode.LineBox;
        fragment.TextState.LineBoxAscentEm = 0.8;
        fragment.TextState.LineBoxDescentEm = 0.2;
        fragment.TextState.BackgroundColor = Color.Yellow;
        fragment.TextState.FormattingOptions.BlockBackground = true;
        shape(fragment);
        page.Paragraphs.Add(fragment);
        using var reopened = Document.Open(doc.ToArray());

        var boxes = new List<Painted>();
        var shows = new List<Show>();
        (double X, double Y, double W, double H)? rect = null;
        double x = 0, y = 0;
        foreach (var op in reopened.Pages[1].Contents)
        {
            switch (op)
            {
                case Re re: rect = (re.X, re.Y, re.Width, re.Height); break;
                case Aspose.Pdf.Operators.Fill when rect is { } r: boxes.Add(new Painted(r.X, r.Y, r.W, r.H)); rect = null; break;
                case MoveTextPosition td: x = td.X; y = td.Y; break;
                case ShowText: shows.Add(new Show(x, y)); break;
            }
        }
        return (boxes, shows);
    }

    private sealed record Painted(double X, double Y, double W, double H);

    private sealed record Show(double X, double Y);
}

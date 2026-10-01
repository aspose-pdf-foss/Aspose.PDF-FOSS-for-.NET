using Aspose.Pdf;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Generator;

/// <summary>A <see cref="ListBlock"/>: items laid one under another in a text column
/// the list shares - its left edge, plus the widest marker, plus the gap - each
/// marker standing on its item's first baseline, right-aligned to the column unless
/// asked to start at the left edge, and drawn after the item's content. A nested
/// list starts its own column where its item's text does. Positions are read back
/// from the page.</summary>
public sealed class ListBlockTests
{
    private const double Margin = 36;
    private const double Size = 12;
    private const double Gap = 5;
    // Helvetica advance widths, in thousandths of an em.
    private const double Digit = 556;
    private const double Period = 278;
    private const double Space = 278;
    private const double Bullet = 350;
    private static double Width(double units) => units * Size / 1000;
    private static readonly double NineWidth = Width(Digit + Period + Space);
    private static readonly double TenWidth = Width(2 * Digit + Period + Space);

    [Fact]
    public void MarkersEndOnTheWidestAndTheTextSharesOneColumn()
    {
        var list = new ListBlock { MarkerGap = Gap };
        list.Items.Add(Item("9. ", "nine"));
        list.Items.Add(Item("10. ", "ten"));
        var texts = Render(list);
        var column = Margin + TenWidth + Gap;
        Assert.Equal(column, texts.Single(t => t.Text == "nine").X, 2);
        Assert.Equal(column, texts.Single(t => t.Text == "ten").X, 2);
        Assert.Equal(Margin + TenWidth - NineWidth, texts.Single(t => t.Text.StartsWith('9')).X, 2);
        Assert.Equal(Margin, texts.Single(t => t.Text.StartsWith("10")).X, 2);
    }

    [Fact]
    public void ALeftAlignedMarkerStartsAtTheListsEdge()
    {
        var list = new ListBlock { MarkerGap = Gap, MarkerAlignment = HorizontalAlignment.Left };
        list.Items.Add(Item("9. ", "nine"));
        list.Items.Add(Item("10. ", "ten"));
        var texts = Render(list);
        Assert.Equal(Margin, texts.Single(t => t.Text.StartsWith('9')).X, 2);
        Assert.Equal(Margin + TenWidth + Gap, texts.Single(t => t.Text == "nine").X, 2);
    }

    [Fact]
    public void AMarkerStandsOnItsItemsFirstBaselineAndIsDrawnAfterIt()
    {
        var list = new ListBlock();
        list.Items.Add(Item("1. ", string.Join(" ", Enumerable.Repeat("words", 40))));
        var texts = Render(list);
        var marker = texts.Single(t => t.Text.StartsWith('1'));
        var lines = texts.Where(t => t.Text.StartsWith("words")).ToList();
        Assert.True(lines.Count > 1, "the item wraps");
        Assert.Equal(lines.Max(t => t.Y), marker.Y, 2);
        Assert.Equal(texts.Count - 1, texts.IndexOf(marker));
    }

    [Fact]
    public void ANestedListStartsItsColumnWhereItsItemsTextDoes()
    {
        var inner = new ListBlock { MarkerGap = Gap };
        inner.Items.Add(Item("9. ", "inner"));
        var outer = new ListBlock { MarkerGap = Gap };
        var item = Item("9. ", "outer");
        item.Paragraphs.Add(inner);
        outer.Items.Add(item);
        var texts = Render(outer);
        var outerColumn = Margin + NineWidth + Gap;
        Assert.Equal(outerColumn, texts.Single(t => t.Text == "outer").X, 2);
        Assert.Equal(outerColumn + NineWidth + Gap, texts.Single(t => t.Text == "inner").X, 2);
        // The inner marker stands at the outer column; the outer one, drawn after its
        // whole item, nested list included, comes last.
        var markers = texts.Where(t => t.Text.StartsWith('9')).ToList();
        Assert.Equal(outerColumn, markers[0].X, 2);
        Assert.Equal(Margin, markers[^1].X, 2);
        Assert.Equal(texts.Count - 1, texts.IndexOf(markers[^1]));
    }

    [Fact]
    public void TheListsLeftMarginMovesTheWholeColumn()
    {
        var list = new ListBlock { MarkerGap = Gap };
        list.Margin.Left = 20;
        list.Items.Add(Item("9. ", "nine"));
        var texts = Render(list);
        Assert.Equal(Margin + 20, texts.Single(t => t.Text.StartsWith('9')).X, 2);
        Assert.Equal(Margin + 20 + NineWidth + Gap, texts.Single(t => t.Text == "nine").X, 2);
    }

    [Fact]
    public void ABulletIsMeasuredAsTheBulletItDraws()
    {
        var list = new ListBlock();
        list.Items.Add(Item("•", "bullet"));
        var texts = Render(list);
        Assert.Equal(Margin + Width(Bullet), texts.Single(t => t.Text == "bullet").X, 2);
    }

    [Fact]
    public void ASymbolicFaceKeepsItsOwnEncoding()
    {
        var list = new ListBlock();
        var marker = Marker("a. ", "Symbol");
        var item = new ListBlockItem { Marker = marker };
        item.Paragraphs.Add(Plain("alpha"));
        list.Items.Add(item);
        using var reopened = Document.Open(Save(list));
        var page = reopened.Pages[1];
        var resources = page.Reader.ResolveDict(page.Dict.Get("Resources"))!;
        var fonts = page.Reader.ResolveDict(resources.Get("Font"))!;
        var encodings = fonts.Keys
            .Select(k => page.Reader.ResolveDict(fonts.Get(k))!)
            .ToDictionary(f => f.GetName("BaseFont")!, f => f.GetName("Encoding"));
        Assert.Null(encodings["Symbol"]);
        Assert.Equal("WinAnsiEncoding", encodings["Helvetica"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ALineThatLeavesNoRoomForItsBottomMarginMovesWhenAsked(bool inside)
    {
        using var doc = new Document();
        var page = doc.Pages.Add();
        page.PageInfo.Margin = new MarginInfo(Margin, Margin, Margin, Margin);
        // Fill the page to within one line of its bottom edge, then a paragraph whose
        // line fits but whose bottom margin would run past the edge.
        var filler = Plain("filler");
        var fillerBox = Size + 6;
        var room = 842 - 2 * Margin;
        filler.Margin.Top = room - 2 * fillerBox - 1;
        page.Paragraphs.Add(filler);
        var last = Plain("last");
        last.Margin.Bottom = 4;
        last.TextState.FormattingOptions.BottomMarginInsideRegion = inside;
        page.Paragraphs.Add(last);
        using var reopened = Document.Open(doc.ToArray());
        Assert.Equal(inside ? 2 : 1, reopened.Pages.Count);
    }

    [Fact]
    public void OnlyTheLastLineNeedsRoomForTheBottomMargin()
    {
        using var doc = new Document();
        var page = doc.Pages.Add();
        page.PageInfo.Margin = new MarginInfo(Margin, Margin, Margin, Margin);
        // Three lines that fill the page to within 1 pt of its bottom edge: the first
        // two stay, the last moves, since the margin under it would cross the edge.
        var lineBox = Size + 6;
        var room = 842 - 2 * Margin;
        var paragraph = Plain(string.Join(" ", Enumerable.Repeat("words", 40)));
        paragraph.Margin.Top = room - 3 * lineBox - 1;
        paragraph.Margin.Bottom = 4;
        paragraph.TextState.FormattingOptions.BottomMarginInsideRegion = true;
        page.Paragraphs.Add(paragraph);
        using var reopened = Document.Open(doc.ToArray());
        Assert.Equal(3, LinesOn(reopened.Pages[1]) + LinesOn(reopened.Pages[2]));
        Assert.Equal(2, reopened.Pages.Count);
        Assert.Equal(2, LinesOn(reopened.Pages[1]));
        Assert.Equal(1, LinesOn(reopened.Pages[2]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AMarkerStandsOnAnEmbeddedFaceItemsFirstBaseline(bool markerEmbedded)
    {
        // The item's text is written through a face program (deferred, seated on its box
        // bottom and lifted by the face's descent): the line captured for the marker must be
        // where the glyphs stand, not the seat, whatever face the marker itself has.
        var face = FontRepository.OpenFont(new MemoryStream(Helpers.PdfBuilder.BuildMinimalTrueTypeFont()), FontTypes.TTF);
        var marker = Marker("- ");
        if (markerEmbedded) marker.Segments[1].TextState.Font = face;
        var item = new ListBlockItem { Marker = marker };
        var text = Plain("embedded");
        text.TextState.Font = face;
        item.Paragraphs.Add(text);
        var list = new ListBlock { MarkerGap = Gap };
        list.Items.Add(item);
        // The content's own baselines: the absorber reports an embedded face at its descent.
        var shows = ShowBaselines(list);
        Assert.Equal(2, shows.Count);
        Assert.Equal(shows[0], shows[1], 2);
    }

    /// <summary>The baseline of every show on the saved page, as the content places it.</summary>
    private static List<double> ShowBaselines(ListBlock list)
    {
        using var reopened = Document.Open(Save(list));
        var baselines = new List<double>();
        double y = 0;
        foreach (var op in reopened.Pages[1].Contents)
        {
            switch (op)
            {
                case Operators.BT: y = 0; break;
                case Operators.SetTextMatrix tm: y = tm.Matrix.F; break;
                case Operators.MoveTextPosition td: y += td.Y; break;
                case Operators.ShowText or Operators.SetGlyphsPositionShowText: baselines.Add(y); break;
            }
        }
        return baselines;
    }

    private static int LinesOn(Page page)
    {
        var absorber = new TextFragmentAbsorber();
        absorber.Visit(page);
        return absorber.TextFragments.Select(f => Math.Round(f.Position!.YIndent, 1)).Distinct().Count();
    }

    private static ListBlockItem Item(string marker, string text)
    {
        var item = new ListBlockItem { Marker = Marker(marker) };
        item.Paragraphs.Add(Plain(text));
        return item;
    }

    private static TextFragment Marker(string text, string? face = null)
    {
        var marker = new TextFragment();
        var segment = new TextSegment(text);
        segment.TextState.FontSize = (float)Size;
        if (face is not null) segment.TextState.FontName = face;
        marker.Segments.Add(segment);
        return marker;
    }

    private static TextFragment Plain(string text)
    {
        var fragment = new TextFragment(text);
        fragment.TextState.FontSize = (float)Size;
        fragment.TextState.LineSpacing = 6;
        fragment.TextState.FormattingOptions.LineSpacing = TextFormattingOptions.LineSpacingMode.LineBox;
        fragment.TextState.LineBoxAscentEm = 0.8;
        fragment.TextState.LineBoxDescentEm = 0.2;
        return fragment;
    }

    private static byte[] Save(ListBlock list)
    {
        using var doc = new Document();
        var page = doc.Pages.Add();
        page.PageInfo.Margin = new MarginInfo(Margin, Margin, Margin, Margin);
        page.Paragraphs.Add(list);
        return doc.ToArray();
    }

    private static List<Placed> Render(ListBlock list)
    {
        using var reopened = Document.Open(Save(list));
        var absorber = new TextFragmentAbsorber();
        absorber.Visit(reopened.Pages[1]);
        return absorber.TextFragments.Select(f => new Placed(f.Text, f.Position!.XIndent, f.Position.YIndent)).ToList();
    }

    private sealed record Placed(string Text, double X, double Y);
}

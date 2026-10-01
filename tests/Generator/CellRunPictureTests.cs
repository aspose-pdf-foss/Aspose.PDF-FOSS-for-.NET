using Aspose.Pdf;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Generator;

/// <summary>A picture among the runs of a cell paragraph
/// (<see cref="TextFormattingOptions.SegmentsFlowAsRuns"/> with a segment carrying an
/// <see cref="TextSegment.InlineImage"/>): a word of its own, as wide as its box,
/// standing on the baseline. It raises the line's ascent to its height when it is
/// taller than the text's, the text's descent stays under the line, a picture
/// alone on a line has none, and on every line after the paragraph's first the
/// line holding a picture drops by the text's em descent. The geometry is read
/// back from the page: the picture's placement matrix and the text positions.</summary>
public sealed class CellRunPictureTests
{
    private const double Margin = 36;
    private const double PageTop = 842 - Margin;
    private const double Rule = 0.5;
    private const double Pad = 2;
    private const double Size = 12;
    private const double Leading = 6;
    private const double AscentEm = 0.8;
    private const double DescentEm = 0.2;
    private const double LineBox = Size + Leading;
    // The declared line box seats its baseline half the surplus leading, then the ascent, under its top.
    private const double TextAscent = (LineBox - (AscentEm + DescentEm) * Size) / 2 + AscentEm * Size;
    private const double TextDescent = LineBox - TextAscent;
    private const double ContentTop = PageTop - Rule - Pad;

    [Fact]
    public void APictureStandsOnTheBaselineAndRaisesTheLineToItsHeight()
    {
        var page = Render(Cell(Text("pic "), Picture(20, 15), Text(" after")));
        var picture = page.Pictures.Single();
        var baseline = ContentTop - Math.Max(TextAscent, 15);
        Assert.Equal(baseline, picture.Y, 2);
        Assert.Equal(15, picture.Height, 2);
        Assert.Equal(baseline, page.Texts.Single(t => t.Text == "pic ").Y, 2);
        // The text after the picture starts at its right edge.
        Assert.Equal(picture.X + 20, page.Texts.Single(t => t.Text == " after").X, 2);
        // The row, line to line of the collapsed grid: half a rule, padding, the raised
        // ascent, the text's descent, padding, half a rule.
        Assert.Equal(Rule + 2 * Pad + 15 + TextDescent, page.RowHeight, 2);
    }

    [Fact]
    public void ASmallPictureLeavesTheLineAsItIs()
    {
        var page = Render(Cell(Text("small "), Picture(8, 6), Text(" text")));
        Assert.Equal(ContentTop - TextAscent, page.Pictures.Single().Y, 2);
        Assert.Equal(Rule + 2 * Pad + LineBox, page.RowHeight, 2);
    }

    [Fact]
    public void APictureAloneIsItsHeightWithNoDescent()
    {
        var page = Render(Cell(Picture(30, 22.5)));
        Assert.Equal(ContentTop - 22.5, page.Pictures.Single().Y, 2);
        Assert.Equal(Rule + 2 * Pad + 22.5, page.RowHeight, 2);
    }

    [Fact]
    public void ALaterLineWithAPictureDropsByTheTextsDescent()
    {
        // A narrow column: the first line takes the words, the picture wraps to the second.
        var page = Render(Cell(Text("words before the picture "), Picture(30, 22.5), Text(" and")), column: 90);
        var picture = page.Pictures.Single();
        // The picture's line sits under the last line of words: that line's descent, then
        // the picture's height, then the drop of a later line.
        var lineAbove = page.Texts.Where(t => t.Y > picture.Y + 0.01).Min(t => t.Y);
        Assert.Equal(lineAbove - TextDescent - 22.5 - DescentEm * Size, picture.Y, 2);
    }

    [Fact]
    public void ALeadingSpaceIsDroppedAtTheStartOfALine()
    {
        var page = Render(Cell(Text(" and"), Picture(30, 22.5)));
        Assert.Equal(Margin + Rule + Pad, page.Texts.Single(t => t.Text == "and").X, 2);
    }

    [Fact]
    public void TheCellsTextClipReachesThePicture()
    {
        var page = Render(Cell(Text("pic "), Picture(20, 15), Text(" after")));
        var picture = page.Pictures.Single();
        var clip = page.Clips.Single(c => c.X < picture.X && c.X + c.Width > picture.X + 20);
        Assert.True(clip.Y <= picture.Y + 0.01 && clip.Y + clip.Height >= picture.Y + 15 - 0.01,
            $"clip {clip.Y}..{clip.Y + clip.Height} against picture {picture.Y}..{picture.Y + 15}");
    }

    private static TextSegment Text(string text)
    {
        var segment = new TextSegment(text);
        Style(segment.TextState);
        return segment;
    }

    private static TextSegment Picture(double width, double height)
    {
        var segment = new TextSegment(string.Empty);
        Style(segment.TextState);
        segment.InlineImage = new Image
        {
            ImageStream = new MemoryStream(Aspose.Pdf.Tests.FlowImageTests.Png((int)width, (int)Math.Ceiling(height))),
            FixWidth = width,
            FixHeight = height,
        };
        return segment;
    }

    private static void Style(TextState state)
    {
        state.FontSize = (float)Size;
        state.LineSpacing = (float)Leading;
        state.LineBoxAscentEm = AscentEm;
        state.LineBoxDescentEm = DescentEm;
    }

    private static Aspose.Pdf.Cell Cell(params TextSegment[] segments)
    {
        var fragment = new TextFragment();
        Style(fragment.TextState);
        fragment.TextState.FormattingOptions.LineSpacing = TextFormattingOptions.LineSpacingMode.LineBox;
        fragment.TextState.FormattingOptions.SegmentsFlowAsRuns = true;
        foreach (var segment in segments) fragment.Segments.Add(segment);
        var cell = new Aspose.Pdf.Cell { VerticalAlignment = VerticalAlignment.Top };
        cell.Paragraphs.Add(fragment);
        return cell;
    }

    private static RenderedPage Render(Aspose.Pdf.Cell cell, double column = 300)
    {
        var table = new Table
        {
            ColumnWidths = column.ToString(System.Globalization.CultureInfo.InvariantCulture),
            DefaultCellBorder = new BorderInfo(BorderSide.All, (float)Rule),
            DefaultCellPadding = new MarginInfo(Pad, Pad, Pad, Pad),
            IsBordersCollapsed = true,
        };
        table.Rows.Add().Cells.Add(cell);
        using var doc = new Document();
        var page = doc.Pages.Add();
        page.PageInfo.Margin = new MarginInfo(Margin, Margin, Margin, Margin);
        page.Paragraphs.Add(table);
        using var reopened = Document.Open(doc.ToArray());
        var first = reopened.Pages[1];
        var absorber = new TextFragmentAbsorber();
        absorber.Visit(first);
        var texts = absorber.TextFragments.Select(f => new Placed(f.Text, f.Position!.XIndent, f.Position.YIndent)).ToList();
        var pictures = new List<PlacedBox>();
        var clips = new List<PlacedBox>();
        var rules = new List<double>();
        Re? pending = null;
        double? fromY = null;
        foreach (var op in first.Contents)
        {
            switch (op)
            {
                case ConcatenateMatrix cm when cm.Matrix.A > 0 && cm.Matrix.D > 0 && cm.Matrix.B == 0 && cm.Matrix.C == 0:
                    pictures.Add(new PlacedBox(cm.Matrix.E, cm.Matrix.F, cm.Matrix.A, cm.Matrix.D));
                    break;
                case Re re: pending = re; break;
                case Clip when pending is { } re: clips.Add(new PlacedBox(re.X, re.Y, re.Width, re.Height)); pending = null; break;
                case MoveTo m: fromY = m.Y; break;
                // (the row's boundaries: the horizontal rules)
                case LineTo l when fromY is { } y0 && Math.Abs(l.Y - y0) < 1e-6: rules.Add(y0); fromY = null; break;
            }
        }
        // (a collapsed grid's row: the boundary lines above and under it)
        var lines = rules.Distinct().OrderByDescending(y => y).ToList();
        var rowHeight = lines.Count >= 2 ? lines[0] - lines[^1] : 0;
        return new RenderedPage(texts, pictures, clips, rowHeight);
    }

    private sealed record Placed(string Text, double X, double Y);

    private sealed record PlacedBox(double X, double Y, double Width, double Height);

    private sealed record RenderedPage(List<Placed> Texts, List<PlacedBox> Pictures, List<PlacedBox> Clips, double RowHeight);
}

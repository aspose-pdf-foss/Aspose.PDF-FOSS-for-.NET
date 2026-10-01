using Aspose.Pdf;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Generator;

/// <summary>A cell paragraph whose segments flow as runs
/// (<see cref="TextFormattingOptions.SegmentsFlowAsRuns"/>): the runs draw on one
/// baseline in their own sizes and faces, the line is boxed by its largest run,
/// the text wraps across the runs, and a content-sized column measures the runs
/// at their own sizes.</summary>
public sealed class CellSegmentRunsTests
{
    private const double Margin = 36;

    [Fact]
    public void RunsShareOneBaselineAtTheirOwnSizes()
    {
        var shows = Render(runs: new[] { ("small ", 12.0, (string?)null), ("BIG", 24.0, null), (" small", 12.0, null) });
        Assert.Equal(3, shows.Count);
        Assert.Equal(shows[0].Y, shows[1].Y, 6);
        Assert.Equal(shows[1].Y, shows[2].Y, 6);
        Assert.Equal(12, shows[0].Size, 6);
        Assert.Equal(24, shows[1].Size, 6);
        // "small " at 12 pt is 31.332 wide in Helvetica, "BIG" at 24 pt 41.352.
        Assert.Equal(shows[0].X + 31.332, shows[1].X, 3);
        Assert.Equal(shows[1].X + 41.352, shows[2].X, 3);
    }

    [Fact]
    public void TheLineIsBoxedAndSeatedByItsLargestRun()
    {
        // A one-run 24 pt paragraph and a mixed 12/24 one seat their baseline the
        // same way and cost the row the same height: the largest run is the line.
        var plain = Render(runs: new[] { ("BIG", 24.0, (string?)null) });
        var mixed = Render(runs: new[] { ("small ", 12.0, (string?)null), ("BIG", 24.0, null) });
        Assert.Equal(plain[0].Y, mixed[1].Y, 3);
        Assert.Equal(RuleBottom(plain), RuleBottom(mixed), 3);
        // …and the row of a 12 pt paragraph is shorter than either.
        var small = Render(runs: new[] { ("small", 12.0, (string?)null), (" small", 12.0, null) });
        Assert.True(RuleBottom(small) > RuleBottom(mixed) + 5, "a 12 pt row is shorter than a 24 pt one");
    }

    [Fact]
    public void TheTextWrapsAcrossTheRunsAndAWordStraddlingThemStaysWhole()
    {
        // In a 160 pt column (155.5 inside its padding and rules) "alphabet" + "ical"
        // is one 67 pt word at 12/14 pt: four of them wrap two to a line, the halves
        // of a word on one baseline.
        var runs = new List<(string, double, string?)>();
        for (var i = 0; i < 4; i++) { runs.Add(("alphabet", 12, null)); runs.Add(("ical ", 14, null)); }
        var shows = Render(runs.ToArray(), columnWidth: 160);
        Assert.Equal(8, shows.Count);
        for (var i = 0; i < 8; i += 2) Assert.Equal(shows[i].Y, shows[i + 1].Y, 6);
        var baselines = shows.Select(s => Math.Round(s.Y, 3)).Distinct().Count();
        Assert.Equal(2, baselines);
    }

    [Fact]
    public void ARunDrawsInItsOwnFace()
    {
        var shows = Render(runs: new[] { ("plain ", 12.0, (string?)null), ("bold", 12.0, "Helvetica-Bold") });
        Assert.NotEqual(shows[0].Font, shows[1].Font);
        Assert.Equal(shows[0].Y, shows[1].Y, 6);
    }

    [Fact]
    public void ARunKeepsItsOwnColourAndTheOthersDrawInTheParagraphs()
    {
        // The red run is the second of three; the first and third stay black even
        // though the red one is the first run to declare a colour at all.
        var fragment = Fragment(new[] { ("plain ", 12.0, (string?)null), ("red", 12.0, null), (" plain", 12.0, null) });
        foreach (var segment in fragment.Segments)
            if (segment.Text == "red") segment.TextState.ForegroundColor = Color.Red;
        var shows = RenderFragment(fragment, columnWidth: 0, sized: false);
        Assert.Equal(3, shows.Count);
        Assert.False(shows[0].Red);
        Assert.True(shows[1].Red);
        Assert.False(shows[2].Red);
    }

    [Fact]
    public void AContentSizedColumnMeasuresTheRunsAtTheirOwnSizes()
    {
        // The runs column holds "small BIG small" with BIG at 24 pt: 104.02 of text,
        // where the same words at 12 pt are 83.34. The neighbour's text starts that
        // far to the right, plus the padding, the rules' halves and the guard.
        var shows = Render(runs: new[] { ("small ", 12.0, (string?)null), ("BIG", 24.0, null), (" small", 12.0, null) },
            sized: true);
        Assert.Equal(104.016 + 4 + 0.5 + 0.01, shows[0].NeighbourX - shows[0].X, 2);
    }

    private static double RuleBottom(List<Show> shows) => shows[0].RuleBottom;

    private static List<Show> Render((string Text, double Size, string? Face)[] runs, double columnWidth = 0, bool sized = false) =>
        RenderFragment(Fragment(runs), columnWidth, sized);

    private static List<Show> RenderFragment(TextFragment fragment, double columnWidth, bool sized)
    {
        using var doc = new Document();
        var page = doc.Pages.Add(595, 842);
        page.PageInfo.Margin = new MarginInfo(Margin, Margin, Margin, Margin);
        var table = new Table
        {
            DefaultCellBorder = new BorderInfo(BorderSide.All, 0.5f),
            DefaultCellPadding = new MarginInfo(2, 2, 2, 2),
            IsBordersCollapsed = true,
        };
        if (sized) table.SizesColumnsToContent = true;
        else table.ColumnWidths = columnWidth > 0 ? columnWidth + " 100" : "200 100";
        var row = table.Rows.Add();
        var cell = row.Cells.Add();
        cell.VerticalAlignment = VerticalAlignment.Top;
        cell.Paragraphs.Add(fragment);
        var other = row.Cells.Add();
        other.VerticalAlignment = VerticalAlignment.Top;
        other.Paragraphs.Add(Fragment(new[] { ("x", 12.0, (string?)null), ("y", 12.0, null) }));
        page.Paragraphs.Add(table);
        using var reopened = Document.Open(doc.ToArray());

        var shows = new List<Show>();
        double ruleBottom = double.MaxValue;
        string font = "";
        double size = 0, x = 0, y = 0;
        var red = false;
        foreach (var op in reopened.Pages[1].Contents)
        {
            switch (op)
            {
                case SelectFont tf: font = tf.Name; size = tf.Size; break;
                case MoveTextPosition td: x = td.X; y = td.Y; break;
                case SetRGBColor rg: red = rg.R > 0.99 && rg.G < 0.01 && rg.B < 0.01; break;
                case ShowText tj: shows.Add(new Show(font, size, x, y) { Red = red }); break;
                case Re re:
                    if (re.Height < 1.5 || re.Width < 1.5) ruleBottom = Math.Min(ruleBottom, re.Y);
                    break;
                case LineTo lt: ruleBottom = Math.Min(ruleBottom, lt.Y); break;
            }
        }
        // The cell under test comes first; the neighbour's two runs are dropped.
        Assert.True(shows.Count >= 3);
        var neighbourX = shows[^2].X;
        shows.RemoveRange(shows.Count - 2, 2);
        return shows.Select(s => s with { RuleBottom = ruleBottom, NeighbourX = neighbourX }).ToList();
    }

    private static TextFragment Fragment((string Text, double Size, string? Face)[] runs)
    {
        var fragment = new TextFragment();
        fragment.TextState.FontSize = 12;
        fragment.TextState.LineSpacing = 6;
        fragment.TextState.FormattingOptions.LineSpacing = TextFormattingOptions.LineSpacingMode.LineBox;
        fragment.TextState.LineBoxAscentEm = 0.8;
        fragment.TextState.LineBoxDescentEm = 0.2;
        fragment.TextState.FormattingOptions.SegmentsFlowAsRuns = true;
        foreach (var (text, runSize, face) in runs)
        {
            var segment = new TextSegment(text);
            segment.TextState.FontSize = (float)runSize;
            segment.TextState.LineSpacing = (float)(runSize * 0.5);
            segment.TextState.LineBoxAscentEm = 0.8;
            segment.TextState.LineBoxDescentEm = 0.2;
            if (face is not null) segment.TextState.FontName = face;
            fragment.Segments.Add(segment);
        }
        return fragment;
    }

    private sealed record Show(string Font, double Size, double X, double Y)
    {
        public double RuleBottom { get; init; }
        public double NeighbourX { get; init; }
        public bool Red { get; init; }
    }
}

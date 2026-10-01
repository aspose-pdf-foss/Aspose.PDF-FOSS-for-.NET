using Aspose.Pdf;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Generator;

/// <summary>A content-sized grid (<see cref="Table.SizesColumnsToContent"/>) whose
/// columns declare shares, widths or nothing, and whose cells may span columns.
/// The numbers are the boxes' own: a 12 pt Helvetica word plus 4 pt of padding,
/// half a rule at each end and the measure guard.</summary>
public sealed class ContentSizedColumnTests
{
    private const double Margin = 36;
    private const double Band = 595 - 2 * Margin - 0.5;

    [Fact]
    public void SharesAloneMakeTheGridAsWideAsTheyAsk()
    {
        // "pa" and "pb" are the same width; 25 % of the grid must hold one, so the
        // grid is four of them and the 75 % column three.
        var widths = ColumnWidths("25% 75%", "pa", "pb");
        Assert.Equal(widths[0] * 3, widths[1], 2);
        Assert.True(widths[0] + widths[1] < Band / 2);
    }

    [Fact]
    public void SharesShortOfTheWholeStretchToItWhenAlone()
    {
        var widths = ColumnWidths("20% 30%", "ka", "kb");
        Assert.Equal(widths[0] * 1.5, widths[1], 2);
        // …and the grid is what the RAW shares asked for: 20 % holding "ka", so
        // five of it, of which the stretched 40 % column takes two.
        var plain = ColumnWidths("25% 75%", "ka", "kb");
        Assert.Equal(plain[0] * 2, widths[0], 2);
    }

    [Fact]
    public void AShareColumnKeepsItsWidestWordAndTheOthersGiveItUp()
    {
        var widths = ColumnWidths("20% 20% 60%", "incomprehensibilitiesincomprehensibilities", "b", "c", stretch: true);
        Assert.Equal(Band, widths.Sum(), 2);
        Assert.True(widths[0] > Band * 0.2);
        // The two others gave up in proportion to their shares, 1 : 3.
        Assert.Equal((Band * 0.2 - widths[1]) * 3, Band * 0.6 - widths[2], 2);
    }

    [Fact]
    public void AShareBesideAWidthAndAnAutoColumn()
    {
        var widths = ColumnWidths("20% 100 auto", "ta", "tb", "tc");
        // The grid is what the width and the auto column need over the 80 % left
        // to them; the share column is 20 % of it.
        Assert.Equal(100, widths[1], 2);
        Assert.Equal((widths[1] + widths[2]) / 0.8 * 0.2, widths[0], 2);
    }

    [Fact]
    public void ASpanSplitsItsSurplusEquallyBetweenItsColumns()
    {
        var table = Grid("", ("y", 1), ("zzzz zzzz", 1), ("w", 1));
        var plain = Render(table);
        var spanned = Grid("", ("a long spanning heading here", 2), ("x", 1));
        var row = spanned.Rows.Add();
        foreach (var text in new[] { "y", "zzzz zzzz", "w" }) row.Cells.Add().Paragraphs.Add(new TextFragment(text));
        var widths = Render(spanned);
        Assert.Equal(widths[0] - plain[0], widths[1] - plain[1], 2);
        Assert.True(widths[0] - plain[0] > 1);
        Assert.Equal(plain[2], widths[2], 6);
    }

    private static double[] ColumnWidths(string declared, params string[] texts) => ColumnWidths(declared, texts, stretch: false);

    private static double[] ColumnWidths(string declared, string a, string b, bool stretch) => ColumnWidths(declared, new[] { a, b }, stretch);

    private static double[] ColumnWidths(string declared, string a, string b, string c, bool stretch) => ColumnWidths(declared, new[] { a, b, c }, stretch);

    private static double[] ColumnWidths(string declared, string[] texts, bool stretch)
    {
        var table = Grid(declared, texts.Select(t => (t, 1)).ToArray());
        table.StretchesToBand = stretch;
        return Render(table);
    }

    private static Table Grid(string declared, params (string Text, int Span)[] cells)
    {
        var table = new Table
        {
            ColumnWidths = declared,
            DefaultCellBorder = new BorderInfo(BorderSide.All, 0.5f),
            DefaultCellPadding = new MarginInfo(2, 2, 2, 2),
            IsBordersCollapsed = true,
            SizesColumnsToContent = true,
        };
        var row = table.Rows.Add();
        foreach (var (text, span) in cells)
        {
            var cell = row.Cells.Add();
            cell.ColSpan = span;
            cell.Paragraphs.Add(new TextFragment(text));
        }
        return table;
    }

    /// <summary>The column pitches read back from the rules: consecutive vertical
    /// lines of the LAST row (a span's row has fewer).</summary>
    private static double[] Render(Table table)
    {
        using var doc = new Document();
        var page = doc.Pages.Add();
        page.PageInfo.Margin = new MarginInfo(Margin, Margin, Margin, Margin);
        page.Paragraphs.Add(table);
        using var reopened = Document.Open(doc.ToArray());
        var lines = new List<(double X, double Bottom)>();
        double? x = null, y = null;
        foreach (var op in reopened.Pages[1].Contents)
        {
            switch (op)
            {
                case MoveTo m: x = m.X; y = m.Y; break;
                case LineTo l when x is { } x0 && y is { } y0 && Math.Abs(l.X - x0) < 1e-6:
                    lines.Add((x0, Math.Min(l.Y, y0)));
                    x = null;
                    break;
            }
        }
        var bottom = lines.Min(l => l.Bottom);
        var xs = lines.Where(l => Math.Abs(l.Bottom - bottom) < 0.01).Select(l => Math.Round(l.X, 3)).Distinct().OrderBy(v => v).ToArray();
        return xs.Zip(xs.Skip(1), (a, b) => b - a).ToArray();
    }
}

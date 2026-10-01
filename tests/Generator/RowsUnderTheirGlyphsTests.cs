using Aspose.Pdf;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Generator;

/// <summary><see cref="Table.RowsCloseUnderTheirGlyphs"/>: a row the page cannot hold
/// whole stays when its text fits down to its last glyphs, and closes at the page's
/// bottom. Swept over where the table starts, so the row at the page's foot passes
/// through every overhang.</summary>
public sealed class RowsUnderTheirGlyphsTests
{
    private const double Margin = 36;
    private const double Rule = 0.5;
    private const double Pad = 2;
    private const double Size = 12;
    private const double Leading = 6;
    private const double AscentEm = 0.8;
    private const double DescentEm = 0.2;
    // The line box's leading under the glyphs: half its surplus over the declared box.
    private const double UnderGlyphs = (Size + Leading - (AscentEm + DescentEm) * Size) / 2;
    private const double RowPitch = Size + Leading + 2 * Pad + Rule;

    [Fact]
    public void ARowKeptUnderItsGlyphsClosesAtTheMargin()
    {
        var kept = 0;
        for (var shift = 0.0; shift < RowPitch; shift += 0.25)
        {
            var (offRows, _) = FirstPage(shift, closeUnderGlyphs: false);
            var (onRows, lowestRule) = FirstPage(shift, closeUnderGlyphs: true);
            Assert.InRange(onRows, offRows, offRows + 1);
            // The grid's closing rule stands whole above the page's bottom margin.
            Assert.True(lowestRule - Rule / 2 >= Margin - 1e-6, $"shift {shift}: rule at {lowestRule}");
            if (onRows > offRows)
            {
                kept++;
                Assert.Equal(Margin + Rule / 2, lowestRule, 3);
            }
        }
        // The window a row is kept in is the leading under its glyphs, less the part of
        // the closing rule the plain fit already lets hang past the margin.
        Assert.InRange(kept * 0.25, UnderGlyphs - Rule - 0.5, UnderGlyphs + 0.5);
    }

    /// <summary>The rows the table's first page holds and the lowest horizontal rule drawn
    /// there, with the table started <paramref name="shift"/> points lower.</summary>
    private static (int Rows, double LowestRule) FirstPage(double shift, bool closeUnderGlyphs)
    {
        var table = new Table
        {
            ColumnWidths = "100",
            DefaultCellBorder = new BorderInfo(BorderSide.All, (float)Rule),
            DefaultCellPadding = new MarginInfo(Pad, Pad, Pad, Pad),
            IsBordersCollapsed = true,
            RowsCloseUnderTheirGlyphs = closeUnderGlyphs,
        };
        for (var i = 1; i <= 60; i++) table.Rows.Add().Cells.Add().Paragraphs.Add(Line("row " + i));
        table.Margin = new MarginInfo(0, 0, 0, shift);
        using var doc = new Document();
        var page = doc.Pages.Add();
        page.PageInfo.Margin = new MarginInfo(Margin, Margin, Margin, Margin);
        page.Paragraphs.Add(table);
        using var reopened = Document.Open(doc.ToArray());
        var first = reopened.Pages[1];
        var absorber = new TextFragmentAbsorber();
        absorber.Visit(first);
        var lowest = double.MaxValue;
        double? fromY = null;
        foreach (var op in first.Contents)
        {
            switch (op)
            {
                case MoveTo m: fromY = m.Y; break;
                case LineTo l when fromY is { } y0 && Math.Abs(l.Y - y0) < 1e-6: lowest = Math.Min(lowest, y0); fromY = null; break;
            }
        }
        return (absorber.TextFragments.Count, lowest);
    }

    private static TextFragment Line(string text)
    {
        var fragment = new TextFragment(text);
        fragment.TextState.FontSize = (float)Size;
        fragment.TextState.LineSpacing = (float)Leading;
        fragment.TextState.LineBoxAscentEm = AscentEm;
        fragment.TextState.LineBoxDescentEm = DescentEm;
        fragment.TextState.FormattingOptions.LineSpacing = TextFormattingOptions.LineSpacingMode.LineBox;
        return fragment;
    }
}

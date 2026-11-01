using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class Table
{
    /// <summary>The height a cell paragraph other than a text fragment takes for
    /// <see cref="GetHeight(Page?)"/>: a nested table its own measure at the cell's inner
    /// width, an HTML fragment the lines the row layout sets it on (see
    /// <see cref="MeasureCellHtmlHeight"/>), anything else nothing.</summary>
    private double MeasureCellObjectHeight(BaseParagraph paragraph, double availW, double cellFs) => paragraph switch
    {
        Table inner => inner.MeasureHeight(availW),
        HtmlFragment html => MeasureCellHtmlHeight(html, availW, cellFs),
        _ => 0,
    };

    /// <summary>An HTML fragment's height in a cell of the given inner width, measured as the
    /// row layout sets it: markup the HTML engine takes stacks the engine's own line boxes at
    /// the fragment's size (a plain-text generator block ends on its last baseline), and markup
    /// outside it wraps its tag-stripped text at the cell's font size.</summary>
    private double MeasureCellHtmlHeight(HtmlFragment html, double availW, double cellFs)
    {
        var size = html.TextState?.FontSize > 0 ? html.TextState.FontSize : HtmlCellFontSize;
        var plain = GeneratorCellModel && (html.HtmlContent?.IndexOf('<') ?? 0) < 0;
        if (ParseHtmlEngineCell(html.HtmlContent, availW, size, html.IsBreakWords, plainText: GeneratorCellModel, EngineRootFont(html)) is { } lines)
        {
            if (plain && lines.Count > 0) lines[^1].BoxH = SerifLineBox(size).Drop;
            double stacked = 0;
            foreach (var line in lines) stacked += line.BoxH > 0 ? line.BoxH : line.FontSize;
            return stacked;
        }
        var text = HtmlFragment.StripHtmlTags(html.HtmlContent ?? string.Empty);
        if (text.Trim().Length == 0) return 0;
        double h = 0;
        foreach (var logical in text.Replace("\r\n", "\n").Split('\n'))
            h += CountMeasuredLines(logical, cellFs, availW) * cellFs;
        return h;
    }

    /// <summary>The room the table needs to start on a page: its top margin and border with
    /// its repeated header rows and the first row under them, measured as
    /// <see cref="GetHeight(Page?)"/> measures rows. A first row that only holds a table
    /// starts where that table starts.</summary>
    internal double GetStartHeight(Page? parentPage)
        => MeasureStartHeight(parentPage is not null ? GetMeasureBandWidth(parentPage) : DefaultMeasureBandPt);

    private double MeasureStartHeight(double availWidth)
    {
        if (Rows.Count == 0) return MeasureHeight(availWidth);
        var row = Rows.At(0);
        if (RepeatingRowsCount <= 0 && row.FixedRowHeight <= 0 && row.Cells.Count == 1
            && row.Cells.At(0) is { Paragraphs.Count: 1 } cell && cell.Paragraphs.First() is Table inner)
        {
            var pad = cell.Margin ?? row.DefaultCellPadding ?? DefaultCellPadding;
            var colWidths = ParseColumnWidths(availWidth, clampToAvailable: false);
            var innerW = GetCellWidth(colWidths, 0, Math.Max(1, cell.ColSpan)) - (pad?.Left ?? 0) - (pad?.Right ?? 0);
            return (Margin?.Top ?? 0) + BorderTopBottom(Border) + (pad?.Top ?? 0) + inner.MeasureStartHeight(innerW);
        }
        var rows = Math.Min(Rows.Count, Math.Max(0, RepeatingRowsCount) + 1);
        return MeasureHeight(availWidth, rows) - (rows < Rows.Count ? Margin?.Bottom ?? 0 : 0);
    }
}

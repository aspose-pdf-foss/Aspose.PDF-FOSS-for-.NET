using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Sibling elements that declare <c>display:table-cell</c> form an anonymous
    /// table row: they sit side by side across the box that holds them, one column each.
    /// The columns divide that box the way a table with automatic layout does — every
    /// column keeps its own MIN-CONTENT (its widest single word, which cannot be broken),
    /// and whatever width is left over is shared in proportion to how much more each
    /// column would take if nothing wrapped (its max-content over its min-content).
    /// Probed against the reference engine on a 230 px box holding a label cell and a
    /// long-text cell: columns 58.335 / 112.665 pt, which this rule reproduces to a
    /// thousandth of a point.</summary>
    private static List<(string Inner, double StartFrac, double WidthFrac, double PadTopPt)>?
        TableCellColumns(string innerHtml, double contentWidthPt)
    {
        if (contentWidthPt <= 0) return null;
        var cells = ReadTableCellDivs(innerHtml);
        if (cells.Count < 2) return null;

        var mins = new double[cells.Count];
        var maxes = new double[cells.Count];
        for (var i = 0; i < cells.Count; i++)
        {
            var text = DomPlainText(cells[i]);
            if (text.Length == 0) return null;
            maxes[i] = UaCellTextWidth(text);
            foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                mins[i] = Math.Max(mins[i], UaCellTextWidth(word));
            if (maxes[i] <= 0 || mins[i] <= 0) return null;
        }

        var widths = ShareCellWidths(mins, maxes, contentWidthPt);
        var cols = new List<(string, double, double, double)>();
        var cursor = 0.0;
        for (var i = 0; i < cells.Count; i++)
        {
            cols.Add((cells[i], cursor / contentWidthPt, widths[i] / contentWidthPt, 0.0));
            cursor += widths[i];
        }
        return cols;
    }

    /// <summary>Stroke the frame of a box drawn at its DECLARED size: the declaration is
    /// the content box and the border sits outside it, so the stroke runs along a path
    /// half a stroke out from each content edge.</summary>
    private static void DrawDeclaredBox(ConvertState cv, double xLeft, double topY,
        double widthPt, double heightPt, double borderPt, double gray)
    {
        if (borderPt <= 0) return;
        var strokeG = gray > 0
            ? gray.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " G"
            : "0 G";
        var x = xLeft + borderPt / 2;
        var top = topY - borderPt / 2;
        var w = widthPt + borderPt;
        var h = heightPt + borderPt;
        var rect = FormattableString.Invariant(
            $"q {borderPt:0.###} w {strokeG} {x:0.###} {top - h:0.###} {w:0.###} {h:0.###} re S Q\n");
        cv.flow.page.AddContentStream(System.Text.Encoding.ASCII.GetBytes(rect));
    }

    /// <summary>The blocks of an anonymous table row: a float band whose columns are the
    /// cells, each laid out inside its own share of the box.</summary>
    private static List<Block> BuildTableCellBandBlocks(ConvertState cv, bool absSpanLedger,
        List<BeforeMarker> beforeMarkers, string? elementGridFace, bool htmlHasFormInput,
        bool inlineBlockColRules, bool perTableFormGate, List<Block> rowBlocks, bool sheetChromesCells,
        List<(string Inner, double StartFrac, double WidthFrac, double PadTopPt)> cols,
        double boxWidthPt, int depth)
    {
        var list = new List<Block> { new() { FloatBandStart = true } };
        foreach (var (inner, startFrac, widthFrac, padTopPt) in cols)
        {
            list.Add(new Block
            {
                FloatColStart = true,
                FloatStartFrac = startFrac,
                FloatWidthFrac = widthFrac,
                FloatPadTopPt = padTopPt,
            });
            list.AddRange(BuildStructuredBlocks(cv, absSpanLedger, beforeMarkers, elementGridFace,
                htmlHasFormInput, inlineBlockColRules, perTableFormGate, rowBlocks, sheetChromesCells,
                inner, depth + 1, widthFrac * boxWidthPt));
        }
        list.Add(new Block { FloatBandEnd = true });
        return list;
    }

    /// <summary>Automatic table layout across <paramref name="available"/>: every column
    /// takes its min-content, and the surplus goes out in proportion to each column's
    /// remaining appetite. Columns that all fit unwrapped simply take their max-content.</summary>
    private static double[] ShareCellWidths(double[] mins, double[] maxes, double available)
    {
        var widths = new double[mins.Length];
        double totalMin = 0, totalMax = 0, totalAppetite = 0;
        for (var i = 0; i < mins.Length; i++)
        {
            totalMin += mins[i];
            totalMax += maxes[i];
            totalAppetite += maxes[i] - mins[i];
        }
        if (totalMax <= available)
        {
            Array.Copy(maxes, widths, maxes.Length);
            return widths;
        }
        var surplus = Math.Max(0, available - totalMin);
        for (var i = 0; i < mins.Length; i++)
            widths[i] = mins[i] + (totalAppetite > 0 ? surplus * (maxes[i] - mins[i]) / totalAppetite : 0);
        return widths;
    }

    /// <summary>The inner HTML of every <c>display:table-cell</c> div in
    /// <paramref name="html"/>, outermost first and never one nested inside another.
    /// The walk descends through plain wrapper divs, so cells wrapped in an anonymous row
    /// are found; it does NOT separate several rows — every cell in the box reads as one
    /// row, which is what the single-row boxes measured so far are.</summary>
    private static List<string> ReadTableCellDivs(string html)
    {
        var cells = new List<string>();
        var divRx = new Regex(@"<div\b[^>]*>", RegexOptions.IgnoreCase);
        var pos = 0;
        for (var m = divRx.Match(html, pos); m.Success; m = divRx.Match(html, pos))
        {
            var (end, contentEnd) = FindDivEnd(html, m.Index + m.Length);
            if (end < 0) break;
            if (Regex.IsMatch(DivStyleOf(m.Value), @"display\s*:\s*table-cell", RegexOptions.IgnoreCase))
            {
                cells.Add(html[(m.Index + m.Length)..contentEnd]);
                pos = end;
            }
            else
            {
                pos = m.Index + m.Length;
            }
        }
        return cells;
    }

    /// <summary>The text a cell draws, with its markup and entity spellings resolved and
    /// its whitespace collapsed the way the flow collapses it.</summary>
    private static string DomPlainText(string html)
        => Regex.Replace(DecodeEntities(Regex.Replace(html, @"<[^>]*>", " ")), @"\s+", " ").Trim();

    /// <summary>Width of a cell run in the UA serif at the body size, pair-kerned as the
    /// engine draws it (an unkerned measure sizes the column a quarter point wide).</summary>
    private static double UaCellTextWidth(string text)
    {
        var kerned = Table.UaSerifKernedWidth(text, DefaultBodyFontPt);
        return kerned > 0 ? kerned : MeasureFaceText("Times New Roman", text, DefaultBodyFontPt);
    }
}

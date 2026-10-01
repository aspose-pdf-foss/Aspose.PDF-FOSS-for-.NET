using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A declared box wider than the natural width fills the columns out to it, a narrower one caps them, and the hard minimum and the CJK span floor hold either way.</summary>
    private static void SettleTableFillAndCapWidths(TableWidthSolveState ws, TableColumnModel colModel, double fillBoxPt, double declBoxPt)
    {
        if (fillBoxPt > 0 && ws.naturalWidthPt > 0 && fillBoxPt > ws.naturalWidthPt
            && ws.table.ColumnWidths is { Length: > 0 } cwAbs && !cwAbs.Contains('%'))
        {
            var scale = fillBoxPt / ws.naturalWidthPt;
            var parts = cwAbs.Split(' ');
            var sb = new StringBuilder();
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                var w = double.Parse(parts[i], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture) * scale;
                sb.Append(w.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            }
            ws.table.ColumnWidths = sb.ToString();
            ws.naturalWidthPt = fillBoxPt;
        }
        // …and the declared box caps the columns the other way: a grid whose cells measure PAST
        // its declared width keeps the box and its over-declared columns yield inside it, so
        // neither its host cell nor the sheet grows to columns the grid never draws (probed on
        // the e-mail statement: sixteen 40 px boxes in a `width="600"` container keep the 450 pt
        // box, and the sheet follows the box). A cell that cannot wrap keeps its content.
        // …but never below its min-content (a nested grid at ITS min-content; an unbreakable line
        // keeps the sheet it needs) nor the hard floor a nested declared box and its host pads set:
        // the box overflows to those (the statement's 600 px class box inks 22.5 pt past itself).
        // (a grid that grew a phantom column for a <pre> line keeps it: that line is unbreakable content)
        var grewForPreLine = (ws.table.ColumnWidths ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > ws.colModel.maxCols;
        double minFloorPt = (ws.colModel.maxCols + 1) * ws.colModel.tblCellSpacingPt;
        foreach (var w in ws.colModel.colMinFloorW) minFloorPt += w;
        var capToPt = Math.Max(declBoxPt, Math.Max(ws.colModel.hardMinPt, minFloorPt));
        // (a POSITIONED grid overflows its declared box the way it overflows the page: the sheet grows
        //  to its ink - the absolutely placed 763 px report grid keeps its 887 pt content)
        if (declBoxPt > 0 && ws.naturalWidthPt > capToPt && !grewForPreLine
            && !Regex.IsMatch(ws.tblTag.Value, @"white-space\s*:\s*(nowrap|pre(?![-\w]))|position\s*:\s*(absolute|fixed)", RegexOptions.IgnoreCase))
        {
            ws.naturalWidthPt = capToPt;
            ws.table.HtmlPreferredWidthPt = capToPt;
        }
        // (…and the host grid is no narrower than that floor: the statement's 600 px class box measures
        //  its nested 600 px grid plus the 15 px pads the cell's shorthand spells)
        if (ws.colModel.hardMinPt > ws.naturalWidthPt)
        {
            ws.naturalWidthPt = ws.colModel.hardMinPt;
            ws.table.HtmlPreferredWidthPt = ws.colModel.hardMinPt;
        }
        // The width PROBE reports a grid no narrower than the whole line of a spanning nowrap cell
        // (probed: 96 + the 102-character Courier New 8 disclaimer + 90 = 675.67 on the land-register
        // order); its declared columns fit inside that box at layout, and the sheet grows to the line
        // the way it grows to a min-floor grid - one page margin past the ink, no legacy slack.
        // A grid with such a cell reports its widest ROW's bare demand (the per-column maxima over-count
        // a grid whose rows floor different columns, and the legacy per-cell slack is no ink).
        // Only a line that runs across EVERY column holding content is the grid's own width (a
        // partial nowrap span floors its columns the ordinary way - the tax return's date spans).
        if (ws.fullWidthCjkMin && colModel.spanNoWrapMinW > 0
            && SpanCoversContentColumns(colModel)
            && Math.Max(colModel.spanNoWrapMinW, colModel.rowNoWrapMinW) is var rowDemand && rowDemand > 0)
        {
            ws.naturalWidthPt = rowDemand;
            ws.table.HtmlPreferredWidthPt = rowDemand;
            ws.table.HtmlPctMinNatural = true;
            ws.table.HtmlRowDemandNatural = true;
        }
    }

    /// <summary>The declared column widths add up to the grid's own box, and the table tag's declared width is read over them.</summary>
    private static void MeasureDeclaredColumnSum(TableWidthSolveState ws, TableColumnModel colModel, bool nestedGrid, double pctBoxPt, double declSumPt, bool uaCellBoxes)
    {
        const double PxToPt = 0.75;
        if (uaCellBoxes && nestedGrid && !(pctBoxPt > 0 && declSumPt > pctBoxPt + 0.01))
            for (var i = 0; i < colModel.colDeclW.Count && i < colModel.colMinW.Count; i++)
                if (colModel.colDeclW[i] > 0 && (i >= colModel.colPctW.Count || colModel.colPctW[i] <= 0))
                {
                    var declBox = colModel.colDeclW[i] + colModel.cellExtraPt;
                    if (colModel.colMinW[i] < declBox) colModel.colMinW[i] = declBox;
                    if (i < colModel.colMaxW.Count && colModel.colMaxW[i] < declBox) colModel.colMaxW[i] = declBox;
                }
        SolvePercentColumnWidths(ws);
        ChooseColumnWidthStrategy(ws);

        // A <pre> cell's longest source line is UNBREAKABLE content: the sheet
        // grows past every declared width to hold it (probed: page = margin +
        // longest line + margin), while the DECLARED columns keep their
        // geometry — headers still centre over them and the row bands keep the
        // declared box. The surplus rides an appended phantom column that only
        // the pre cells span, so their lines draw whole.
        GrowForUnbreakablePreLine(ws);

        // The declared cellspacing is real horizontal space between and around the
        // columns, and each cell keeps the UA's 1px padding pair — both are part
        // of the sized sheet (the status report's pair
        // row + 3·cellspacing + 4·0.75 = its 698.62 content width).
        if (ws.chainBase is not null && ws.colModel.tblCellSpacingPt > 0 && ws.colModel.maxCols > 0 && ws.naturalWidthPt > 0)
            ws.naturalWidthPt += (ws.colModel.maxCols + 1) * ws.colModel.tblCellSpacingPt + ws.colModel.maxCols * 1.5;

        ws.tableWidthAbsPt = 0;
        if (ws.tblTag.Success)
        {
            var twAbs = Regex.Match(ws.tblTag.Value, @"\bwidth\s*=\s*[""']?(\d+(?:\.\d+)?)\s*(px)?\s*[""'\s/>]",
                RegexOptions.IgnoreCase);
            if (twAbs.Success && double.TryParse(twAbs.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var twAbsPx))
                ws.tableWidthAbsPt = twAbsPx * PxToPt;
        }
    }
}

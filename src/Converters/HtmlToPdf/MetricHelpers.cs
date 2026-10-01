using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The table parser's working set, lifted out of BuildTableFromHtml: each
// method takes the parse state, the column model and the settled dialect
// scalars it reads. Bodies are verbatim.
    private static string CellFaceName(string face, string boldFace, MetricCell mc) => mc.Face is { } cf
        ? cf + (mc.Bold ? " Bold" : "") + (mc.Italic ? " Italic" : "")
        : (mc.Bold ? boldFace : face);

    private static (double asc, double sum) CellFm((double asc, double sum) fm, MetricCell mc) => mc.Face is { } cf
        ? (WinMetricsFor(cf) ?? fm) : fm;

    private static double CellLineOf(MetricParseState mps, bool stdSerif, bool wrapperStacks, double hheaSum, string face, (double asc, double sum) fm, MetricCell mc, double cellFs)
    {
        // the collapsed class grid's LINE-HEIGHT pitches every cell line
        if (mps.collapsedLineH > 0) return mps.collapsedLineH;
        // the cell's own declared line-height fixes its box
        if (mc.LineHeightPt > 0) return mc.LineHeightPt;
        var cSum0 = CellFm(fm, mc).sum;
        // pt-report cells pace on the face's hhea line (probed: 9pt Arial
        // rows pitch 10.5 = 14px, not the win-metric 13px) — and so do the
        // saved-statement grid's inline-sized cells (11pt Times rows pitch
        // 12.75 = 17px, the hhea box).
        // …and so does a <font size=N> cell: its band is its OWN hhea box,
        // not the table base size (probed on the font-size ladder: the
        // 6.75/9.75/13.5/36 pt rows band 10/15/21/55 px, the hhea line
        // WITH its gap, and each pitches on its own box alone).
        // …and so does a UA block cell's paragraph (measured on the invoice email: 9 pt
        // paragraphs pitch 10.5 = 14px, the hhea box, where the win sum's 13px gave 9.75).
        // …and a UA cell that states its size INLINE (probed: a `font-size: 34px` th lines 39 px =
        // 29.25, where the win sum gave 38). A class-sized cell keeps the win box its greens were
        // calibrated on (the 65 % statement grid pitches 14 px at 12.8 px, not the hhea 15).
        // …and a QUIRKS UA cell sized by its class, whose row has no strut to pace it: its line is
        // its own hhea box (measured on the enterprise summary: `td.label` / `span.label` Arial 9
        // rows pitch 10.5 = 14 px, where the win sum's 13 px gave 9.75).
        if ((!stdSerif && wrapperStacks) || mps.inlineStatementGrid || mc.FontTagSized || mps.uaBlockCells
            || (stdSerif && mc.FontInline && !mc.FontFromClass && mc.FontSize is not null)
            || (stdSerif && _quirksRowStrut && mc.FontFromClass && mc.FontSize is not null && mc.Face is not null))
            cSum0 = HheaLineSumFor(mc.Face ?? face) ?? cSum0;
        return MetricLineHeight(cellFs, cSum0 <= 1.0 ? 1.2 : cSum0);
    }

    /// <summary>A cell rule's `line-height` as a line box: a px or pt length as it stands, a percent,
    /// plain number or em of the cell's font size; null for `normal` or anything else.</summary>
    private static double? CellRuleLineHeightPt(string value, double cellFs)
    {
        var m = Regex.Match(value.Trim(), @"^(\d+(?:\.\d+)?|\.\d+)\s*(px|pt|em|%)?$", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var n = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        return m.Groups[2].Value.ToLowerInvariant() switch
        {
            "px" => n * PxPt,
            "pt" => n,
            "%" => n / 100.0 * cellFs,
            _ => n * cellFs,
        };
    }

    /// <summary>A row with no content of any kind — no text, segments, grids, image or rule.</summary>
    private static bool MetricRowIsBare(List<MetricCell> row)
    {
        foreach (var mc in row)
            if (mc.Text.Length > 0 || mc.DivSegs is { Count: > 0 }
                || mc.SubTables is { Count: > 0 } || mc.ImgHPt > 0 || mc.HrRule
                // (a form control is content: a row of bare inputs bands on their boxes)
                || mc.InputBoxes is { Count: > 0 } || mc.LeadCheckboxName is not null)
                return false;
        return true;
    }

    /// <summary>Every text-bearing cell of the row declares a line-height of its own.</summary>
    /// <summary>A CSS family is case-insensitive: a Standard-14 base family (or Arial) spelled in another case takes
    /// its canonical name, so the cell draws the real face rather than a literally-named Type1 no rasteriser maps.</summary>
    private static string CanonicalStandardFaceName(string face)
    {
        foreach (var canon in new[] { "Helvetica", "Arial", "Courier", "Times", "Times New Roman", "Courier New" })
            if (face.Equals(canon, StringComparison.OrdinalIgnoreCase)) return canon;
        return face;
    }

    /// <summary>Every text-bearing cell of the row holds its text in floated spans only (no line box, no strut).</summary>
    private static bool MetricRowTextAllFloated(List<MetricCell> row)
    {
        var any = false;
        foreach (var mc in row)
        {
            if (mc.InkOutsideFloat) return false;
            any |= mc.InkInFloat;
        }
        return any;
    }

    /// <summary>Every text cell of the row takes its typography from the row's own inline
    /// declaration, so the row pitches on those boxes and the table's strut does not apply.</summary>
    private static bool MetricRowTextAllRowStyled(List<MetricCell> row, bool nbspCounts = false)
    {
        var sawText = false;
        foreach (var mc in row)
        {
            if ((nbspCounts ? mc.Text.Trim(' ') : mc.Text.Trim()).Length == 0) continue;
            // …stated by the row itself, or by the cell for itself: either way the row
            // pitches on the boxes it was given rather than on the table's base line
            // (measured: cells declaring Arial 10 band 11.25, the strut gave 13.5).
            if (!mc.RowInlineTypo && !mc.CellInlineTypo) return false;
            sawText = true;
        }
        return sawText;
    }

    /// <summary>Every text cell of the row draws at a size its CLASS stated (a quirks-mode row of
    /// such cells pitches on its own line boxes, not on the table's base line).</summary>
    /// <summary>Every text-bearing cell of the row names its own face (through its class or inline).</summary>
    private static bool MetricRowTextAllClassFaced(List<MetricCell> row)
    {
        var sawText = false;
        foreach (var mc in row)
        {
            if (mc.Text.Trim().Length == 0) continue;
            if (mc.Face is null) return false;
            sawText = true;
        }
        return sawText;
    }

    private static bool MetricRowTextAllClassSized(List<MetricCell> row)
    {
        var sawText = false;
        foreach (var mc in row)
        {
            if (mc.Text.Trim().Length == 0) continue;
            if (!mc.FontFromClass || mc.FontSize is null) return false;
            sawText = true;
        }
        return sawText;
    }

    /// <summary>Whether the row or one of its own (non-spanning) cells declares a height.</summary>
    private static bool MetricRowDeclaresHeight(MetricRowsState mr, int ri)
    {
        if (ri < mr.mps.rowHeights.Count && mr.mps.rowHeights[ri] > 0) return true;
        foreach (var mc in mr.r)
            if (mc.RowSpan <= 1 && (mc.HeightPt > 0 || mc.HeightStylePt > 0)) return true;
        return false;
    }

    private static bool MetricRowDeclaresLineBox(List<MetricCell> row)
    {
        var any = false;
        foreach (var mc in row)
            if (mc.Text.Length > 0)
            {
                if (mc.LineHeightPt <= 0) return false;
                any = true;
            }
        return any;
    }

    private static double CellDropOf(MetricParseState mps, bool stdSerif, (double asc, double sum) fm, MetricCell mc, double cellFs, double box)
        => MetricBaselineDrop(cellFs, box, CellFm(fm, mc));

    private static string ResOfFlatOn(MetricParseState mps, Dictionary<string, string> flatRes, string face, string boldFace, Page pg, MetricCell mc)
    {
        var fn = CellFaceName(face, boldFace, mc);
        if (!flatRes.TryGetValue(fn, out var rn))
        {
            // Pick a name no page-level font has already claimed for
            // something else — the flow's Type0 embeds share this /Font
            // dictionary and count through the same F-numbers.
            var fd = (pg.Dict.Get("Resources") as Core.PdfDictionary)?
                .Get("Font") as Core.PdfDictionary;
            var idx = 8 + flatRes.Count;
            while (fd?.Get("F" + idx) is { } takenObj
                   && (takenObj is not Core.PdfDictionary taken
                       || taken.GetName("BaseFont") != fn.Replace(" ", "")))
                idx++;
            rn = "F" + idx;
            flatRes[fn] = rn;
        }
        EnsureFont(pg, fn.Replace(" ", ""), rn);
        return rn;
    }
}

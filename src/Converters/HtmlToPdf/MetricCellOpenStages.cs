using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The cell's valign, align, width (percent or pixels) and, for RTL or report cells, height attributes onto the cell and the pending row height.</summary>
    private static void ApplyMetricCellAttributes(MetricTableState mt, MetricCell cell, Dictionary<string, string> ca)
    {
        if (ca.TryGetValue("valign", out var va))
        {
            if (va.Trim().Equals("top", StringComparison.OrdinalIgnoreCase))
            { cell.VAlignTop = true; cell.VAlignBottom = false; }
            else if (va.Trim().Equals("bottom", StringComparison.OrdinalIgnoreCase))
            { cell.VAlignBottom = true; cell.VAlignTop = false; }
            // (the legacy `center` spelling asks for middle too)
            else if (va.Trim().Equals("middle", StringComparison.OrdinalIgnoreCase) || va.Trim().Equals("center", StringComparison.OrdinalIgnoreCase))
            { cell.VAlignMiddle = true; cell.VAlignTop = false; cell.VAlignBottom = false; }
        }
        if (ca.TryGetValue("align", out var al))
            cell.Align = al.Trim().ToLowerInvariant() switch
            {
                "right" => HorizontalAlignment.Right,
                "center" => HorizontalAlignment.Center,
                _ => HorizontalAlignment.Left,
            };
        // A style-declared width outranks the width attribute (CSS precedence): the
        // attribute is applied after the style here, so without this guard a
        // `width="247"` attribute overwrote a `style="width:148.35pt"` and the column
        // came out too wide to wrap its text as the reference does.
        var cellWidthFromStyle = cell.WidthPxStyle || (cell.WidthPct > 0 && ca.TryGetValue("style", out var stW)
            && Regex.IsMatch(stW, @"(?<![-\w])width\s*:\s*[\d.]+\s*%", RegexOptions.IgnoreCase));
        if (!cellWidthFromStyle)
        {
            if (ca.TryGetValue("width", out var wv) && wv.Trim().EndsWith('%')
                && double.TryParse(wv.Trim().TrimEnd('%'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var pct))
                cell.WidthPct = pct;
            // width="300" / width="300px": a pixel width fixes the
            // column's content width outright (legacy attribute grid).
            else if (ca.TryGetValue("width", out var wpv)
                && double.TryParse(wpv.Trim().TrimEnd('p', 'x'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var wpx)
                && wpx > 0)
                cell.WidthPx = wpx * PxPt;
        }
        // height="69": a cell's pixel height floors its whole row
        // (the RTL attr grid's banded rows; the report flow's
        // spacer rows pace on it too).
        if (ca.TryGetValue("height", out var hpv)
            && double.TryParse(hpv.Trim().TrimEnd('p', 'x'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var hpx))
        {
            // (a row-spanning UA cell's height is its own: it spans its rows, banding none alone -
            // measured on the work permit: a height=50 rowspan=2 cell shares 42.75 over two rows)
            if (mt.stdSerif && cell.RowSpan > 1) cell.HeightPt = Math.Max(cell.HeightPt, hpx * PxPt);
            else if (hpx * PxPt > mt.mps.pendingRowH) mt.mps.pendingRowH = hpx * PxPt;
        }
    }

    /// <summary>A style border-bottom draws the rule under the cell (order-free tokens, none draws nothing, double kept); a style border-right draws that one edge with its colour.</summary>
    /// <summary>The Words export spells a cell's borders as longhands (`border-bottom-color`,
    /// `-style`, `-width`): folded into the shorthand the readers know (probed on the Words letter:
    /// the bordered 2-column table draws every side 0.75 pt).</summary>
    private static string FoldBorderLonghands(string style)
    {
        if (style.IndexOf("-width", StringComparison.OrdinalIgnoreCase) < 0) return style;
        var folded = style;
        foreach (var side in new[] { "top", "right", "bottom", "left" })
        {
            var w = Regex.Match(style, @"(?<![-\w])border-" + side + @"-width\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
            var st = Regex.Match(style, @"(?<![-\w])border-" + side + @"-style\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
            if (!w.Success || !st.Success || Regex.IsMatch(style, @"(?<![-\w])border-" + side + @"\s*:", RegexOptions.IgnoreCase)) continue;
            var col = Regex.Match(style, @"(?<![-\w])border-" + side + @"-color\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
            folded += "; border-" + side + ": " + w.Groups[1].Value.Trim() + " " + st.Groups[1].Value.Trim()
                + (col.Success ? " " + col.Groups[1].Value.Trim() : "");
        }
        return folded;
    }

    private static void ApplyMetricCellStyleBorders(MetricCell cell, string tdst)
    {
        tdst = FoldBorderLonghands(tdst);
        // a style border-bottom draws the rule under the cell
        // (the financial-statement idiom — order-free tokens,
        // `none` draws nothing)
        var bbm = Regex.Match(tdst,
            @"border-bottom\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
        if (bbm.Success
            && !bbm.Groups[1].Value.Contains("none", StringComparison.OrdinalIgnoreCase)
            && Regex.IsMatch(bbm.Groups[1].Value, @"solid|double", RegexOptions.IgnoreCase))
        {
            var bbv = bbm.Groups[1].Value;
            var bbw = Regex.Match(bbv, @"([\d.]+)\s*(pt|px)", RegexOptions.IgnoreCase);
            cell.BorderBottomW = bbw.Success
                ? double.Parse(bbw.Groups[1].Value,
                      System.Globalization.CultureInfo.InvariantCulture)
                  * (bbw.Groups[2].Value.Equals("px",
                      StringComparison.OrdinalIgnoreCase) ? PxPt : 1.0)
                : 0.75;
            cell.BorderBottomDouble = bbv.Contains("double",
                StringComparison.OrdinalIgnoreCase);
            if (ParseCssColor(Regex.Replace(bbv, @"solid|double|[\d.]+\s*(?:pt|px)", "", RegexOptions.IgnoreCase).Trim())
                is { } bbc)
                cell.BorderBottomCol = bbc;
        }
        // a style border-top draws the rule over the cell in its colour (the report
        // footer's `BORDER-TOP: black 2pt solid` band rule)
        var btm = Regex.Match(tdst, @"border-top\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
        if (btm.Success
            && !btm.Groups[1].Value.Contains("none", StringComparison.OrdinalIgnoreCase)
            && btm.Groups[1].Value.Contains("solid", StringComparison.OrdinalIgnoreCase)
            && Regex.Match(btm.Groups[1].Value, @"([\d.]+)\s*(pt|px)", RegexOptions.IgnoreCase) is { Success: true } btw)
        {
            cell.BorderTopW = DtpNum(btw.Groups[1].Value)
                * (btw.Groups[2].Value.Equals("px", StringComparison.OrdinalIgnoreCase) ? PxPt : 1.0);
            if (ParseCssColor(Regex.Replace(btm.Groups[1].Value, @"solid|[\d.]+\s*(?:pt|px)", "", RegexOptions.IgnoreCase).Trim())
                is { } btc)
                cell.BorderTopCol = btc;
        }
        // a style border-right draws that one edge (the legacy
        // separator-column idiom: border-right: solid black 2px)
        // (…and a border-left the same way, both in pixels or points - the Words letter's 0.75pt sides)
        foreach (var side in new[] { "right", "left" })
        {
            var brm = Regex.Match(tdst, @"(?<![-\w])border-" + side + @"\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
            if (!brm.Success) continue;
            var brv = brm.Groups[1].Value;
            var bwm = Regex.Match(brv, @"([\d.]+)\s*(px|pt)", RegexOptions.IgnoreCase);
            if (!bwm.Success || !brv.Contains("solid", StringComparison.OrdinalIgnoreCase)) continue;
            var sideW = DtpNum(bwm.Groups[1].Value) * (bwm.Groups[2].Value.Equals("px", StringComparison.OrdinalIgnoreCase) ? PxPt : 1.0);
            var sideCol = ParseCssColor(Regex.Replace(brv, @"solid|[\d.]+\s*(?:px|pt)", "", RegexOptions.IgnoreCase).Trim());
            if (side == "right")
            {
                cell.BorderRightW = sideW;
                if (sideCol is { } brc) cell.BorderRightCol = brc;
            }
            else
            {
                cell.BorderLeftW = sideW;
                if (sideCol is { } blc) cell.BorderLeftCol = blc;
            }
        }
    }

    /// <summary>A `vertical-align:` declaration in a cell's style attribute.</summary>
    private const string CellVerticalAlignRx = @"(?<![-\w])vertical-align\s*:\s*([a-zA-Z]+)";

    /// <summary>The style width (percent, or an absolute mm/cm/in/pt/px box, a min-width marking the setter cell) and an absolute height onto the cell.</summary>
    private static void ApplyMetricCellStyleSize(MetricCell cell, string tdst)
    {
        var twm2 = Regex.Match(tdst, @"width\s*:\s*(\d+(?:\.\d+)?)\s*%");
        if (twm2.Success)
            cell.WidthPct = double.Parse(twm2.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture);
        // An ABSOLUTE inline width (the SSRS width-setter
        // rows: `WIDTH: 12.7mm; MIN-WIDTH: 12.7mm`) fixes
        // the column outright.
        var twAbs = Regex.Match(tdst,
            @"(?<![-\w])width\s*:\s*([\d.]+)\s*(mm|cm|in|pt|px)",
            RegexOptions.IgnoreCase);
        var twAbsV = 0.0;
        var twAbsParsed = twAbs.Success && double.TryParse(twAbs.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out twAbsV);
        // A declared ZERO width is a column of no width at all (the report grid's
        // `WIDTH: 0mm` spacer column measures 0 in the expected render, where an
        // undeclared empty column takes the font-size floor).
        if (twAbsParsed && twAbsV == 0) cell.WidthZero = true;
        if (twAbsParsed && twAbsV > 0)
        {
            cell.WidthPx = twAbs.Groups[2].Value.ToLowerInvariant() switch
            {
                "mm" => twAbsV * 72.0 / 25.4,
                "cm" => twAbsV * 72.0 / 2.54,
                "in" => twAbsV * 72.0,
                "px" => twAbsV * PxPt,
                _ => twAbsV,
            };
            cell.WidthPxStyle = true;
        }
        if (twAbs.Success
            && Regex.IsMatch(tdst, @"min-width\s*:", RegexOptions.IgnoreCase))
            cell.WidthSetterCell = true;
        // An ABSOLUTE inline height (the report grid's row pacers: `HEIGHT: 6.35mm`) floors
        // the row band; an EMPTY spacer row is EXACTLY that height. PIXELS count too - a
        // `height: 20px` cell bands its row at 15 pt and a middle-aligned neighbour then
        // centres in it (measured: the request form's rows pitch 13.125, which is the
        // 11.25 line box plus half the 3.75 the band exceeds it by).
        var thAbs = Regex.Match(tdst,
            @"(?<![-\w])height\s*:\s*([\d.]+)\s*(mm|cm|in|pt|px)\b",
            RegexOptions.IgnoreCase);
        if (thAbs.Success && double.TryParse(thAbs.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var thAbsV) && thAbsV >= 0)
        {
            var thPt = thAbs.Groups[2].Value.ToLowerInvariant() switch
            {
                "mm" => thAbsV * 72.0 / 25.4,
                "cm" => thAbsV * 72.0 / 2.54,
                "in" => thAbsV * 72.0,
                "px" => thAbsV * PxPt,
                _ => thAbsV,
            };
            cell.HeightPt = Math.Max(cell.HeightPt, thPt);
            // …and an INLINE height is the band a middle-aligned cell centres in; a class
            // height keeps the calibrated seat (the report grid's millimetre pacer rows).
            cell.HeightStylePt = Math.Max(cell.HeightStylePt, thPt);
        }
    }

    /// <summary>One side's `padding-&lt;side>` from the cell's inline style, in points (px, pt or em), or null when it states none.</summary>
    private static double? CellSidePaddingPt(string tdst, string prop, MetricCell cell, double emPt = 0)
    {
        var m = Regex.Match(tdst, @"(?<![-\w])" + prop + @"\s*:\s*([\d.]+)\s*(px|pt|em)", RegexOptions.IgnoreCase);
        if (!m.Success) return CellShorthandPaddingPt(tdst, prop, cell, emPt);
        var v = DtpNum(m.Groups[1].Value);
        return m.Groups[2].Value.ToLowerInvariant() switch
        {
            "px" => v * PxPt,
            // (an em padding is of the cell's own size - the table's base where the cell states
            // none: the cheque's `padding-bottom: 3em` cell under a 14 px table pads 31.5)
            "em" => v * (cell.FontSize ?? (emPt > 0 ? emPt : UaDefaultFontPt)),
            _ => v,
        };
    }

    /// <summary>The side a `padding:` shorthand gives the cell (top right bottom left, CSS's 1-4 value
    /// grammar), in pt from any absolute unit (a Word export pads `0in 5.4pt 0in 5.4pt`); null when the
    /// cell declares no shorthand or a value does not parse.</summary>
    private static double? CellShorthandPaddingPt(string tdst, string prop, MetricCell cell, double emPt = 0)
    {
        var sh = Regex.Match(tdst, @"(?<![-\w])padding\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
        if (!sh.Success) return null;
        var parts = sh.Groups[1].Value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 4) return null;
        var v = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            var pm = Regex.Match(parts[i], @"^([\d.]+)\s*(px|pt|em|in|cm|mm)?$", RegexOptions.IgnoreCase);
            if (!pm.Success) return null;
            var n = DtpNum(pm.Groups[1].Value);
            v[i] = pm.Groups[2].Value.ToLowerInvariant() switch
            {
                "px" => n * PxPt,
                "em" => n * (cell.FontSize ?? (emPt > 0 ? emPt : UaDefaultFontPt)),
                "in" => n * 72.0,
                "cm" => n * 72.0 / 2.54,
                "mm" => n * 72.0 / 25.4,
                _ => n,
            };
        }
        var right = parts.Length >= 2 ? v[1] : v[0];
        var left = parts.Length == 4 ? v[3] : right;
        var bottom = parts.Length >= 3 ? v[2] : v[0];
        return prop.EndsWith("left", StringComparison.OrdinalIgnoreCase) ? left
            : prop.EndsWith("right", StringComparison.OrdinalIgnoreCase) ? right
            : prop.EndsWith("top", StringComparison.OrdinalIgnoreCase) ? v[0]
            : bottom;
    }

    /// <summary>The cell's inline style: size, font-size, padding-top on report cells, text-align, background, a non-black colour, bold, nowrap, and the border edges.</summary>
    private static void ApplyMetricCellStyle(MetricTableState mt, MetricCell cell, string tdst)
    {
        ApplyMetricCellStyleSize(cell, tdst);
        // The cell's own inline vertical-align, which the VALIGN attribute and a class rule
        // already reached but a style attribute did not: a `vertical-align: top` cell seats
        // its line at the band top instead of centring in it.
        var tvam = Regex.Match(tdst, CellVerticalAlignRx, RegexOptions.IgnoreCase);
        if (tvam.Success)
        {
            var tva = tvam.Groups[1].Value.Trim().ToLowerInvariant();
            if (tva == "top") { cell.VAlignTop = true; cell.VAlignBottom = false; cell.VAlignMiddle = false; }
            else if (tva == "bottom") { cell.VAlignBottom = true; cell.VAlignTop = false; cell.VAlignMiddle = false; }
            else if (tva is "middle" or "center")
            { cell.VAlignMiddle = true; cell.VAlignTop = false; cell.VAlignBottom = false; }
        }
        var tfm = Regex.Match(tdst, @"font-size\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
        if (tfm.Success && TryParseCssFontSize(tfm.Groups[1].Value.Trim()) is { } tdfs)
        { cell.FontSize = tdfs; cell.FontInline = true; }
        // The cell's own family and line box: a td that styles its text
        // carries both to every line it holds (probed on the letter grid:
        // `font-family: Calibri` draws Calibri, and `line-height: 14px`
        // pitches its wrapped lines 10.5 rather than the face's own box).
        var tffm = Regex.Match(tdst, @"font-family\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
        if (tffm.Success && FirstFontFamily(tffm.Groups[1].Value) is { Length: > 0 } tfam
            && (!mt.stdSerif || SourceEngineFaces.Contains(tfam)))
        {
            cell.Face = tfam;
            // …and a cell naming its own FACE pitches its row on that box, not on the
            // table's base line (the same law as a row naming one).
            cell.CellInlineTypo = true;
        }
        var tlhm = Regex.Match(tdst, @"line-height\s*:\s*([\d.]+)\s*(px|pt)", RegexOptions.IgnoreCase);
        if (tlhm.Success)
            cell.LineHeightPt = DtpNum(tlhm.Groups[1].Value)
                * (tlhm.Groups[2].Value.Equals("pt", StringComparison.OrdinalIgnoreCase) ? 1 : PxPt);
        // The attribute grid honours the cell's own side padding (the Word export's
        // `padding-left:5.4pt; padding-right:5.4pt`): the text insets by it and wraps
        // inside it (measured on the kit-lot grid: `(1) 1003870` breaks in its 54.55 pt
        // column only once both pads are charged to the line).
        // …and so does every UA grid cell (the letter's `padding-right:3px` city cell).
        if (mt.mps.bordered || mt.stdSerif)
        {
            if (CellSidePaddingPt(tdst, "padding-left", cell, mt.mps.fontSize) is { } tplPt) cell.PadLeft = tplPt;
            if (CellSidePaddingPt(tdst, "padding-right", cell, mt.mps.fontSize) is { } tprPt) cell.PadRight = tprPt;
        }
        // newsletter cells honor a style padding-top as box space - and a UA grid cell honours
        // both block paddings (measured: the letter's `padding-bottom: 17px` row is its line plus
        // 12.75, its `padding-top: 25px; padding-bottom: 15px` row its grid plus 18.75 and 11.25).
        if (mt.reportCells || mt.stdSerif)
        {
            // (a cell's own vertical padding REPLACES the table's cellpadding, as its side
            // padding does - measured on the worksheet: a `padding: 5px` cell bands 21 = its
            // 13.5 line + 2 x 3.75, not a point and a half more)
            if (CellSidePaddingPt(tdst, "padding-top", cell, mt.mps.fontSize) is { } tptPt) cell.PadTopPt = Math.Max(0, tptPt - mt.p);
            if (mt.stdSerif && CellSidePaddingPt(tdst, "padding-bottom", cell, mt.mps.fontSize) is { } tpbPt) cell.PadBottomPt = Math.Max(0, tpbPt - mt.p);
        }
        var tam = Regex.Match(tdst, @"text-align\s*:\s*(left|center|right)", RegexOptions.IgnoreCase);
        if (tam.Success)
            cell.Align = tam.Groups[1].Value.ToLowerInvariant() switch
            {
                "right" => HorizontalAlignment.Right,
                "center" => HorizontalAlignment.Center,
                _ => HorizontalAlignment.Left,
            };
        var tbgm = Regex.Match(tdst, @"background(?:-color)?\s*:\s*([^;]+)",
            RegexOptions.IgnoreCase);
        if (tbgm.Success && ParseCssColor(tbgm.Groups[1].Value.Trim()) is { } tdsbg)
            cell.Bg = tdsbg;
        var tcm = Regex.Match(tdst, @"(?<![-\w])color\s*:\s*([^;]+)",
            RegexOptions.IgnoreCase);
        if (tcm.Success && ParseCssColor(tcm.Groups[1].Value.Trim()) is { } tdcol
            && (tdcol.R != 0 || tdcol.G != 0 || tdcol.B != 0))
            cell.Fore = tdcol;
        if (Regex.IsMatch(tdst, @"font-weight\s*:\s*bold", RegexOptions.IgnoreCase))
            cell.Bold = true;
        // `pre` keeps the source line whole, exactly as `nowrap` does — but `pre-wrap`
        // and `pre-line` break, so the keyword must not match by prefix.
        if (Regex.IsMatch(tdst, @"white-space\s*:\s*(nowrap|pre(?![-\w]))", RegexOptions.IgnoreCase))
            cell.NoWrap = true;
        if (Regex.IsMatch(tdst, @"white-space\s*:\s*pre-wrap", RegexOptions.IgnoreCase))
            cell.PreWrap = true;
        ApplyMetricCellStyleBorders(cell, tdst);
    }

    /// <summary>The cell's class names: each class rule's font-size onto the cell, and the whole class bag under the wrapper-stack model.</summary>
    private static void ApplyMetricCellClasses(MetricTableState mt, MetricCell cell, string tdcls)
    {
        cell.ClassNames = new List<string>(
            tdcls.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        foreach (var cn in tdcls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            // TAG-prefixed selectors (TD.rubric — the pt-report
            // sheets) resolve like the bare class.
            if (!mt.css.TryGetValue("." + cn, out var cnr))
                mt.css.TryGetValue("td." + cn, out cnr);
            // (…a class the sheet scopes to the grid - `table .th-md`, `th.cls` - dresses the cell the same way)
            if (cnr is null) mt.css.TryGetValue("table ." + cn, out cnr);
            if (cnr is null) mt.css.TryGetValue("th." + cn, out cnr);
            if (cnr is null) continue;
            if (cnr.TryGetValue("font-size", out var cnfs)
                && TryParseCssFontSize(cnfs.Trim()) is { } cnpt)
                cell.FontSize = cnpt;
            // class-driven cell chrome (the header band
            // and boleto skins): typography, fill, ink,
            // geometry and per-side borders
            if (mt.wrapperStacks)
                ApplyCellClassBag(mt.mps, mt.css, mt.text, mt.reportCells, mt.stdSerif, cell, cnr);
        }
    }
}

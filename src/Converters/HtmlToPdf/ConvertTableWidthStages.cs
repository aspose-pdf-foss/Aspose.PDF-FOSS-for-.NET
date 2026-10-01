using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Each table tag in the source contributes its declared width to the widest-table measurement.</summary>
    private static void MeasureDeclaredTableWidths(ConvertState cv)
    {
        foreach (Match tm in Regex.Matches(cv.html, @"<table\b[^>]*>", RegexOptions.IgnoreCase))
        {
            // An unpainted wrapper (no border, no background, nothing inside but another
            // table) declares a box nobody sees: the sheet follows the table it wraps
            // (measured: a 600 pt wrapper round a 562.5 pt band leaves the page at 748.5).
            if (cv.profile.wordMailDoc && IsUnpaintedWrapperTable(cv.html, tm.Index)) continue;
            double w = 0;
            var attr = Regex.Match(tm.Value,
                @"\bwidth\s*=\s*[""']?\s*(\d+(?:\.\d+)?)\s*(?:px)?\s*[""'\s/>]", RegexOptions.IgnoreCase);
            if (attr.Success && double.TryParse(attr.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var attrPx))
                w = attrPx * 0.75;
            var styleW = Regex.Match(DivStyleOf(tm.Value),
                @"(?<![-\w])width\s*:\s*([\d.]+\s*(?:px|pt|in|cm|mm))", RegexOptions.IgnoreCase);
            if (styleW.Success && TryParseLength(styleW.Groups[1].Value.Replace(" ", "")) is { } stylePt)
                w = stylePt;
            // a width CLASS on the table (the boleto's .w666 skin) declares the
            // same fixed box as an attribute
            if (w == 0)
            {
                var clsM = Regex.Match(tm.Value, @"class\s*=\s*[""']?([\w \-]+)",
                    RegexOptions.IgnoreCase);
                if (clsM.Success)
                    foreach (var tcl in clsM.Groups[1].Value.Split(' ',
                        StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (cv.css.TryGetValue("." + tcl, out var tclR)
                            && tclR.TryGetValue("width", out var tclW)
                            && TryParseLength(tclW.Trim()) is { } tclPt)
                            w = Math.Max(w, tclPt);
                        // (`table.cls { width: 800px }` declares the box the same way)
                        if (cv.css.TryGetValue("table." + tcl, out var ttclR)
                            && ttclR.TryGetValue("width", out var ttclW)
                            && TryParseLength(ttclW.Trim()) is { } ttclPt)
                            w = Math.Max(w, ttclPt);
                    }
            }
            // A table that is the first child of a wrapper whose id/class rule declares a box (`<div
            // id=divTable><table>` under `#divTable { width: 2000px }`) is laid out in that box: the
            // sheet grows to it as to a declared table width (probed: 96 + 1500 + 90 = 1684.5).
            if (w == 0 && cv.uaFlow && HostDivRuleWidthPt(cv, tm.Index) is { } hostBoxW && hostBoxW > 0)
                w = hostBoxW;
            // A Word grid's box is as wide as its PAINTED columns: the sheet ends one page margin past
            // the last cell border or fill, and the bare columns declared past it are laid out off the
            // sheet, as the reference clips them (probed: the 1289 pt grid pages 773.27, not 1469).
            if (cv.profile.wordMailDoc && w > 0
                && WordGridPaintedWidthPt(cv.html, tm.Index) is var paintedW && paintedW > 0 && paintedW < w)
                w = paintedW;
            // (two grids declaring the same box: the one standing deeper in a host cell's chrome
            // is the one whose ink reaches further)
            if (w > 0 && Math.Abs(w - cv.declaredTableW) < 1e-6)
                cv.declaredTableHostChromePt = Math.Max(cv.declaredTableHostChromePt, HostCellChromePt(cv.html, tm.Index));
            if (w > cv.declaredTableW)
            {
                cv.declaredTableW = w;
                cv.declaredTableHostChromePt = HostCellChromePt(cv.html, tm.Index);
                var csM = Regex.Match(tm.Value, @"\bcellspacing\s*=\s*[""']?([^""'\s>]+)", RegexOptions.IgnoreCase);
                cv.declaredTableSpacingPt = csM.Success && PresentationalLengthPt(csM.Groups[1].Value) is { } csMPt ? csMPt : MetricDefaultSpacingPt;
                cv.declaredTableClassed = Regex.IsMatch(tm.Value, @"\bclass\s*=", RegexOptions.IgnoreCase);
                cv.declaredTableFramed = Regex.IsMatch(tm.Value, @"\bborder\s*=\s*[""']?[1-9]", RegexOptions.IgnoreCase)
                    || Regex.IsMatch(DivStyleOf(tm.Value), @"(?<![-\w])border(?:-(?:top|left|right|bottom))?\s*:\s*(?!none|0)", RegexOptions.IgnoreCase);
            }
            // a bordered COLLAPSE grid (border=N + border-collapse:collapse)
            // keeps its declared width exactly — the sheet grows to page margin
            // + declared box + page margin, with no slack and no body inset.
            // A style-collapsed table with its own declared width follows the
            // same model without the border attribute (probed: the widest
            // width:491.4pt collapse grid grows the sheet to 96 + 491.4 + 90).
            if (w > cv.collapseTableW
                && Regex.IsMatch(tm.Value, @"border-collapse\s*:\s*collapse", RegexOptions.IgnoreCase)
                && (Regex.IsMatch(tm.Value, @"\bborder\s*=\s*[""']?[1-9]", RegexOptions.IgnoreCase)
                    || styleW.Success))
                cv.collapseTableW = w;
        }
    }
}

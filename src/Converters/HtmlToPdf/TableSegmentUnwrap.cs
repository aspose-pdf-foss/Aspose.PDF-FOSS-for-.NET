using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Flow blocks: a single-column wrapper table unwrapped into ordinary flow blocks.</summary>
    private static bool UnwrapSingleColumnTable(FlowBlocksState fbk, string seg, bool isTable)
    {
        // Single-column wrapper table in the UA flow: unwrap — its cell
        // content parses as ordinary flow blocks, every block inset by
        // the cell chrome and the first padded down by it (chrome is
        // box space, it never margin-collapses).
        if (isTable && fbk.cv.profile.uaStdSerif && !fbk.cv.profile.escapedAttrDoc
            && IsSingleColumnWrapperTable(fbk.sheetChromesCells, seg))
        {
            // Per ROW: the cell content flows with its own block margins
            // (a p pairs only with an in-cell sibling — a lone p in a
            // row carries none, probed on the licensing letter's grid),
            // rows advance one row chrome apart (2 x cellpadding + the
            // UA 2px cellspacing), and a td align=center centres each
            // wrapped line over the table's attribute-width band.
            var openTag = Regex.Match(seg, @"<table\b[^>]*>", RegexOptions.IgnoreCase);
            double wrapBandW = 0;
            var wAttrM = Regex.Match(openTag.Value, @"\bwidth\s*=\s*[""']?(\d+(?:\.\d+)?)",
                RegexOptions.IgnoreCase);
            if (wAttrM.Success && double.TryParse(wAttrM.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var wAttrPx))
                wrapBandW = wAttrPx * 0.75 - 2 * UaCellChromePt;
            // A wrapper table WITHOUT a width attribute is shrink-to-fit: its box is its
            // widest line's max-content. An RTL document anchors that box on the right
            // edge, so a centred cell centres its lines over the box, not the page
            // (probed on the RTL letter: the subject line centres at 372.25 =
            // 496.75 - 249 / 2, the 249 being the cell's nbsp spacer line).
            UnwrapWrapperTableRows(fbk, seg, wrapBandW);
            fbk.lastUnwrapEnd = fbk.list.Count;
            return true;
        }
        return false;
    }

    /// <summary>The max-content width of a wrapper table's cells: the widest br-separated line of any
    /// cell, measured in the flow face at the cell's own size (its no-break spaces kept, its
    /// ordinary whitespace collapsed the way the cell lays it out).</summary>
    private static double ShrinkToFitWrapperWidth(FlowBlocksState fbk, string seg)
    {
        double w = 0;
        var rootPt = FlowRootFontPt(fbk.cv, sheetBodyRule: false);
        if (rootPt <= 0) rootPt = DefaultBodyFontPt;   // the UA default when the document sizes nothing
        foreach (Match tdM in Regex.Matches(seg, @"<td\b([^>]*)>([\s\S]*?)</td\s*>", RegexOptions.IgnoreCase))
        {
            var cellPt = rootPt;
            var fsM = Regex.Match(tdM.Groups[1].Value, @"font-size\s*:\s*([\d.]+)\s*(px|pt)", RegexOptions.IgnoreCase);
            if (fsM.Success && double.TryParse(fsM.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var fsV))
                cellPt = fsM.Groups[2].Value.Equals("px", StringComparison.OrdinalIgnoreCase) ? fsV * 0.75 : fsV;
            foreach (var lineHtml in Regex.Split(tdM.Groups[2].Value, @"<br\s*/?>", RegexOptions.IgnoreCase))
            {
                var text = Regex.Replace(DecodeEntities(Regex.Replace(lineHtml, @"<[^>]*>", "")), @"[ \t\r\n]+", " ").Trim(' ');
                if (text.Length == 0) continue;
                w = Math.Max(w, MeasureFaceText(fbk.cv.profile.metricFace, text, cellPt));
            }
        }
        return w;
    }
}

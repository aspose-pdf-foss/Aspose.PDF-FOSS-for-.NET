using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Flow blocks: a fragment holding tables split into segments and parsed segment by segment.</summary>
    private static void BuildTableSegmentBlocks(FlowBlocksState fbk)
    {
        var segs = SegmentHtmlTables(fbk.frag);
        // A PAGE-BREAK-AFTER div wrapping tables: its close tag parses in
        // a LATER segment where the pending-break state cannot reach, so
        // the break attaches to the div's LAST table segment instead.
        HashSet<int>? breakAfterSegs = null;
        if (fbk.cv.profile.uaStdSerif && Regex.IsMatch(fbk.frag, @"page-break-after\s*:\s*always",
                RegexOptions.IgnoreCase))
        {
            var spans = new (int start, int end)[segs.Count];
            var segPos = 0;
            for (var si = 0; si < segs.Count; si++)
            {
                spans[si] = (segPos, segPos + segs[si].html.Length);
                segPos += segs[si].html.Length;
            }
            var dRx = new Regex(@"<(/?)div\b[^>]*>", RegexOptions.IgnoreCase);
            foreach (Match bm in Regex.Matches(fbk.frag,
                @"<div\b[^>]*page-break-after\s*:\s*always[^>]*>", RegexOptions.IgnoreCase))
            {
                var depth = 1;
                var endPos = -1;
                for (var dm = dRx.Match(fbk.frag, bm.Index + bm.Length); dm.Success;
                     dm = dRx.Match(fbk.frag, dm.Index + dm.Length))
                {
                    depth += dm.Groups[1].Value.Length > 0 ? -1 : 1;
                    if (depth == 0) { endPos = dm.Index; break; }
                }
                if (endPos < 0) continue;
                var lastTableSeg = -1;
                for (var si = 0; si < segs.Count; si++)
                    if (segs[si].isTable && spans[si].start >= bm.Index
                        && spans[si].end <= endPos)
                        lastTableSeg = si;
                if (lastTableSeg >= 0)
                    (breakAfterSegs ??= new HashSet<int>()).Add(lastTableSeg);
            }
        }
        // A <header> that flows (it names no fixed region) still stands its own
        // margin-bottom between itself and the block after it — probed on the reference at
        // 0, .5em and 1em, each spent in full. The charge rides the following block's top
        // margin, which the UA flow already knows how to spend.
        var hdrMargin = FlowHeaderBottomMarginPt(fbk.cv);
        var afterHeader = hdrMargin > 0
            ? fbk.frag.IndexOf("</header>", StringComparison.OrdinalIgnoreCase) : -1;
        var segStart = 0;
        // The stylesheet reaches a grid through the elements it is nested in
        // (`#right_column TABLE TD`): one stack walks the segments, so every table
        // carries the containers open above it into its own build.
        var openChain = new List<CssElem>();
        for (var segIdx = 0; segIdx < segs.Count; segIdx++)
        {
            var chargeHeader = afterHeader >= 0 && segStart >= afterHeader;
            if (chargeHeader) afterHeader = -1;
            var before = fbk.list.Count;
            segStart += segs[segIdx].html.Length;
            // (the quirks-mode chain sheet's dialect: a standards-mode document keeps the
            // calibrated grid its greens were measured on)
            fbk.openChain = _ancestorGridSheet && openChain.Count > 0 ? new List<CssElem>(openChain) : null;
            fbk.hostChain = openChain.Count > 0 ? new List<CssElem>(openChain) : null;
            var built = BuildOneTableSegment(fbk, segs, breakAfterSegs, segIdx);
            AdvanceOpenContainerChain(openChain, segs[segIdx].html);
            if (!built) break;
            if (chargeHeader && fbk.list.Count > before)
                fbk.list[before].MarginTop += hdrMargin;
        }
    }

    /// <summary>The bottom margin the sheet gives a FLOWING &lt;header&gt; element, in points; zero
    /// when it declares none or when the header was lifted out as a fixed page region (whose
    /// own 24 pt charge stands instead). An em resolves against the body's own size.</summary>
    private static double FlowHeaderBottomMarginPt(ConvertState cv)
    {
        if (!string.IsNullOrEmpty(cv.runHeader)) return 0;
        if (!cv.css.TryGetValue("header", out var rule)
            || !rule.TryGetValue("margin-bottom", out var mv)) return 0;
        var m = Regex.Match(mv.Trim(), @"^([\d.]+)\s*(em|rem|px|pt)$", RegexOptions.IgnoreCase);
        if (!m.Success || !double.TryParse(m.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var n) || n <= 0) return 0;
        return m.Groups[2].Value.ToLowerInvariant() switch
        {
            "em" or "rem" => n * DefaultBodyFontPt,
            "px" => n * PxPt,
            _ => n,
        };
    }
}

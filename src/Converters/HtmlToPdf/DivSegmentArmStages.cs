using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The stages of the div segmenting: the column-or-plain arm.

    /// <summary>The arms of <see cref="SegmentNextDiv"/> after the scope tests: a float column div or a
    /// plain div; false when the segment cannot be placed, true when the cursor moved on.</summary>
    private static bool SegmentColumnOrPlainDiv(DivSegmentState dv, string style, int contentEnd)
    {
        if (IsFloatColStyle(style, dv.allowPxCols))
        {
            var cols = new List<(string, double, double, double)>();
            var cursor = 0.0;
            // px→fraction against the content box (px-width Bootstrap columns).
            double PxFrac(string st3, string prop)
            {
                if (dv.contentWidthPt <= 0) return 0;
                var pm = Regex.Match(st3, prop + @"\s*:\s*(\d+(?:\.\d+)?)px", RegexOptions.IgnoreCase);
                return pm.Success && double.TryParse(pm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var pv)
                    ? pv * 0.75 / dv.contentWidthPt : 0;
            }
            void AddCol(string st2, string inner)
            {
                var w = StylePct(st2, "width") / 100.0;
                if (w <= 0) w = PxFrac(st2, "width");
                var ml = StylePct(st2, "margin-left") / 100.0;
                if (ml <= 0) ml = PxFrac(st2, "margin-left");
                var pr = StylePct(st2, "padding-right") / 100.0;
                if (pr <= 0) pr = PxFrac(st2, "padding-right") + PxFrac(st2, "margin-right");
                var start = cursor + ml;
                cols.Add((inner, start, w, StyleLenPt(st2, "padding-top")));
                cursor = start + w + pr;
            }
            AddCol(style, dv.html[dv.afterOpen..contentEnd]);
            dv.pos = dv.end;
            while (true)
            {
                var nm = dv.divRx.Match(dv.html, dv.pos);
                if (!nm.Success || !string.IsNullOrWhiteSpace(dv.html[dv.pos..nm.Index])) break;
                var st3 = DivStyleOf(nm.Value);
                if (!IsFloatColStyle(st3, dv.allowPxCols)) break;
                // A float that cannot fit beside the ones already collected wraps
                // below them (px-column dialect): stop this band — the next loop
                // pass starts a fresh band for it, stacking it as its own row
                // (two 380px floats inside a 380px parent stack, not overlap).
                if (dv.allowPxCols)
                {
                    var w3 = StylePct(st3, "width") / 100.0;
                    if (w3 <= 0) w3 = PxFrac(st3, "width");
                    var ml3 = StylePct(st3, "margin-left") / 100.0;
                    if (ml3 <= 0) ml3 = PxFrac(st3, "margin-left");
                    if (cursor + ml3 + w3 > 1.02) break;
                }
                (var ne, var nce) = FindDivEnd(dv.html, nm.Index + nm.Length);
                if (ne < 0) break;
                AddCol(st3, dv.html[(nm.Index + nm.Length)..nce]);
                dv.pos = ne;
            }
            dv.segs.Add(new DivSeg { Kind = DivSeg.Band, Cols = cols });
        }
        else
        {
            // A `padding: Npx` shorthand covers all four sides when no side-specific
            // declaration is present (the class-box form).
            var padAll = StyleLenPt(style, "padding");
            var padT = StyleLenPt(style, "padding-top");
            var padB = StyleLenPt(style, "padding-bottom");
            var padL = StyleLenPt(style, "padding-left");
            var padR = StyleLenPt(style, "padding-right");
            dv.segs.Add(new DivSeg
            {
                Kind = DivSeg.Box,
                Html = dv.html[dv.afterOpen..contentEnd],
                BorderPt = BorderSolidPt(style),
                PadTopPt = padT > 0 ? padT : padAll,
                PadBottomPt = padB > 0 ? padB : padAll,
                PadSidePt = padL + padR > 0 ? padL + padR : 2 * padAll,
                MarginBottomPt = StyleLenPt(style, "margin-bottom"),
                BorderGray = BorderGrayOf(style),
                // A box that states its own width and height IN ITS OWN STYLE is drawn at
                // that size: the declaration is content-box, so the border rides outside
                // it. Print-grid mode folds class rules into this style, and a grid skin's
                // class width is not the element's own declaration — those boxes keep
                // taking the ambient width.
                BoxWidthPt = dv.classCss is null ? StyleOwnLenPt(style, "width") : 0,
                BoxHeightPt = dv.classCss is null ? StyleOwnLenPt(style, "height") : 0,
            });
            dv.pos = dv.end;
        }
        return true;
    }
}

using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The stages of the RTL Word-section form render: the paragraph read and the flow.
    private static List<RtlPara> ReadRtlParas(RtlFormRenderState rl, string frag)
    {
        var list = new List<RtlPara>();
        foreach (Match pm in Regex.Matches(frag, @"<p\b([^>]*)>([\s\S]*?)</p\s*>",
                     RegexOptions.IgnoreCase))
        {
            var inner = pm.Groups[2].Value;
            var st = Regex.Match(pm.Groups[1].Value, @"style\s*=\s*([""'])([\s\S]*?)\1",
                RegexOptions.IgnoreCase);
            var sz = Regex.Match(inner, @"font-size\s*:\s*([\d.]+)\s*pt", RegexOptions.IgnoreCase);
            // the paragraph's own spans, each with the size it declares
            var runs = new List<(double, string)>();
            var last = 0.0;
            foreach (Match sm2 in Regex.Matches(inner,
                         @"<span\b([^>]*)>((?:(?!</?span)[\s\S])*)</span\s*>", RegexOptions.IgnoreCase))
            {
                var rs = Regex.Match(sm2.Groups[1].Value, @"font-size\s*:\s*([\d.]+)\s*pt",
                    RegexOptions.IgnoreCase);
                if (rs.Success && double.TryParse(rs.Groups[1].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var rv) && rv > 0)
                    last = rv;
                var rt = CollapseWs(DecodeEntities(
                    Regex.Replace(sm2.Groups[2].Value, @"<[^>]+>", ""))).Trim();
                if (rt.Length > 0 && last > 0) runs.Add((last, rt));
            }
            list.Add(new RtlPara
            {
                Runs = runs,
                Size = sz.Success && double.TryParse(sz.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0 ? v : 12.0,
                Text = CollapseWs(DecodeEntities(Regex.Replace(inner, @"<[^>]+>", ""))).Trim(),
                Zeroed = st.Success
                    && st.Groups[2].Value.Replace(" ", "").Contains("margin:0cm",
                        StringComparison.OrdinalIgnoreCase),
                Bold = Regex.IsMatch(inner, @"<b\b|font-weight\s*:\s*bold", RegexOptions.IgnoreCase),
            });
        }
        return list;
    }

    // Emit one paragraph list from `y`, anchored on `right`; returns the
    // cursor it leaves behind. A line that would cross the content bottom
    // opens a fresh page at the raw top.
    private static double FlowRtlParas(RtlFormRenderState rl, List<RtlPara> items, double y, double right, bool trailingCounts)
    {
        var prevBot = 0.0;
        for (var i = 0; i < items.Count; i++)
        {
            var p = items[i];
            var mt = p.Zeroed ? 0.0 : RtlBlockMarginPt;
            y += Math.Max(prevBot, mt);
            if (p.Text.Length > 0)
            {
                if (y + p.Size * RtlLineFactor > rl.bottom)
                {
                    rl.page = rl.doc.Pages.Add(rl.pageWidth, rl.pageHeight);
                    EnsureFonts(rl.page);
                    y = ColMarginTop;
                }
                // Measure with the face the emitter will actually draw:
                // a Hebrew run goes out through an embedded Unicode face
                // whose advances are not the UA serif's, and anchoring on
                // the serif's width pushes the line past the box edge.
                var baseY = rl.pageHeight - (y + p.Size * RtlDropFactor);
                // one span per size, laid right to left
                var parts = p.Runs.Count > 1 && p.Runs.Exists(rr => rr.Size != p.Runs[0].Size)
                    ? p.Runs : new List<(double Size, string Text)> { (p.Size, p.Text) };
                var pen = right;
                foreach (var (rsz, rtxt) in parts)
                {
                    var mFace = p.Bold ? rl.face + "-Bold" : rl.face;
                    if (NeedsUnicode(rtxt) && ResolveUnicodeFont(rtxt) is { } uf
                        && uf.FontName is { Length: > 0 } ufn
                        && WinMetricsFor(ufn) is not null)
                        mFace = ufn;
                    var rw = MeasureFaceText(mFace, rtxt, rsz);
                    EmitPositionedRun(rl.page, p.Bold ? "F6" : "F5", rsz,
                        pen - rw, baseY, rtxt);
                    pen -= rw;
                }
            }
            // a cell's trailing empty paragraph closes the box without
            // adding a line of its own
            var last = !trailingCounts && i == items.Count - 1 && p.Text.Length == 0;
            if (!last) y += p.Size * RtlLineFactor;
            prevBot = p.Zeroed ? 0.0 : RtlBlockMarginPt;
        }
        return y;
    }
}

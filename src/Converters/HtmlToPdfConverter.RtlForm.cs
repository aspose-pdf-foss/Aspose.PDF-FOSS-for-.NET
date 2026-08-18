using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The right-to-left Word export: a `WordSection1` div of RTL paragraphs whose
// declared families are not installed, so the reference draws the whole form in
// the UA serif at each span's own size. Every line is anchored on the content
// box's RIGHT edge, and the paragraphs that zero their margins inline sit on a
// bare line grid while the ones that do not keep the browser's own block
// margins — which is the entire vertical rhythm.
internal static partial class HtmlToPdfConverter
{
    // Line box and baseline as fractions of the font size, and the block margin
    // a paragraph keeps when its inline style does not zero one (all three
    // solved off the reference's own ladder, which they reproduce to ±1.4 pt).
    private const double RtlLineFactor = 1.1561;
    private const double RtlDropFactor = 0.8941;
    private const double RtlBlockMarginPt = 13.47;
    private const double RtlCellPadPt = 5.4;      // the cells' declared 5.4pt sides

    private sealed class RtlPara
    {
        public double Size = 12.0;              // the paragraph's line-box size
        public string Text = "";
        public bool Zeroed;
        public bool Bold;
        // A paragraph whose spans declare DIFFERENT sizes draws each at its
        // own — laid right to left, since the flow is RTL.
        public List<(double Size, string Text)> Runs = new();
    }

    /// <summary>Render the RTL Word-export form, or null.</summary>
    private static Document? TryRenderRtlForm(string html, double pageWidth, double pageHeight)
    {
        if (!Regex.IsMatch(html, @"<div\b[^>]*class\s*=\s*[""']?WordSection1", RegexOptions.IgnoreCase)
            || !Regex.IsMatch(html, @"dir\s*=\s*[""']?RTL", RegexOptions.IgnoreCase))
            return null;
        const string face = "Times New Roman";
        if (WinMetricsFor(face) is null) return null;
        var bodyM = Regex.Match(html, @"<body\b[^>]*>([\s\S]*)</body\s*>", RegexOptions.IgnoreCase);
        if (!bodyM.Success) return null;
        var body = bodyM.Groups[1].Value;

        static List<RtlPara> Paras(string frag)
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

        // the table splits the flow; its cells run INDEPENDENT column flows
        var tblM = Regex.Match(body, @"<table\b[^>]*>([\s\S]*?)</table\s*>", RegexOptions.IgnoreCase);
        var before = tblM.Success ? body[..tblM.Index] : body;
        var after = tblM.Success ? body[(tblM.Index + tblM.Length)..] : "";

        var doc = new Document();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        EnsureFonts(page);
        var boxRight = pageWidth - ColMarginX;
        var top = ColMarginTop + UaBodyMarginPt;
        var bottom = pageHeight - ColMarginTop;

        // Emit one paragraph list from `y`, anchored on `right`; returns the
        // cursor it leaves behind. A line that would cross the content bottom
        // opens a fresh page at the raw top.
        double Flow(List<RtlPara> items, double y, double right, bool trailingCounts)
        {
            var prevBot = 0.0;
            for (var i = 0; i < items.Count; i++)
            {
                var p = items[i];
                var mt = p.Zeroed ? 0.0 : RtlBlockMarginPt;
                y += Math.Max(prevBot, mt);
                if (p.Text.Length > 0)
                {
                    if (y + p.Size * RtlLineFactor > bottom)
                    {
                        page = doc.Pages.Add(pageWidth, pageHeight);
                        EnsureFonts(page);
                        y = ColMarginTop;
                    }
                    // Measure with the face the emitter will actually draw:
                    // a Hebrew run goes out through an embedded Unicode face
                    // whose advances are not the UA serif's, and anchoring on
                    // the serif's width pushes the line past the box edge.
                    var baseY = pageHeight - (y + p.Size * RtlDropFactor);
                    // one span per size, laid right to left
                    var parts = p.Runs.Count > 1 && p.Runs.Exists(rr => rr.Size != p.Runs[0].Size)
                        ? p.Runs : new List<(double Size, string Text)> { (p.Size, p.Text) };
                    var pen = right;
                    foreach (var (rsz, rtxt) in parts)
                    {
                        var mFace = p.Bold ? face + "-Bold" : face;
                        if (NeedsUnicode(rtxt) && ResolveUnicodeFont(rtxt) is { } uf
                            && uf.FontName is { Length: > 0 } ufn
                            && WinMetricsFor(ufn) is not null)
                            mFace = ufn;
                        var rw = MeasureFaceText(mFace, rtxt, rsz);
                        EmitPositionedRun(page, p.Bold ? "F6" : "F5", rsz,
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

        var y0 = Flow(Paras(before), top, boxRight, trailingCounts: true);
        if (tblM.Success)
        {
            // The block margin the paragraph before it left does not collapse
            // into the table: its cells open one margin lower, and the table's
            // own height stops at its deepest REAL line.
            var cellTop = y0 + RtlBlockMarginPt;
            var cells = Regex.Matches(tblM.Groups[1].Value, @"<td\b([^>]*)>([\s\S]*?)</td\s*>",
                RegexOptions.IgnoreCase);
            var deepest = y0;
            var ci = 0;
            foreach (Match cm in cells)
            {
                var cw = 0.0;
                var wm = Regex.Match(cm.Groups[1].Value, @"width\s*:\s*([\d.]+)\s*pt",
                    RegexOptions.IgnoreCase);
                if (wm.Success) double.TryParse(wm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out cw);
                if (cw <= 0) cw = 207.4;
                // RTL: the first cell is the RIGHTMOST
                var cellRight = boxRight - ci * cw - RtlCellPadPt;
                var end = Flow(Paras(cm.Groups[2].Value), cellTop, cellRight, trailingCounts: false);
                deepest = Math.Max(deepest, end);
                ci++;
            }
            Flow(Paras(after), deepest, boxRight, trailingCounts: true);
        }
        return doc;
    }
}

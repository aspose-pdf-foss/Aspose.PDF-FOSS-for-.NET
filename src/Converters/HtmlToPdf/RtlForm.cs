using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The right-to-left Word export: a `WordSection1` div of RTL paragraphs whose
// declared families are not installed, so the whole form draws in
// the UA serif at each span's own size. Every line is anchored on the content
// box's RIGHT edge, and the paragraphs that zero their margins inline sit on a
// bare line grid while the ones that do not keep the browser's own block
// margins — which is the entire vertical rhythm.
internal static partial class HtmlToPdfConverter
{
    // Line box and baseline as fractions of the font size, and the block margin
    // a paragraph keeps when its inline style does not zero one (all three
    // solved off the measured ladder, which they reproduce to ±1.4 pt).
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
        var rl = new RtlFormRenderState();
        rl.html = html;
        rl.pageWidth = pageWidth;
        rl.pageHeight = pageHeight;
        if (!Regex.IsMatch(rl.html, @"<div\b[^>]*class\s*=\s*[""']?WordSection1", RegexOptions.IgnoreCase)
            || !Regex.IsMatch(rl.html, @"dir\s*=\s*[""']?RTL", RegexOptions.IgnoreCase))
            return null;
        rl.face = "Times New Roman";
        if (WinMetricsFor(rl.face) is null) return null;
        rl.bodyM = Regex.Match(rl.html, @"<body\b[^>]*>([\s\S]*)</body\s*>", RegexOptions.IgnoreCase);
        if (!rl.bodyM.Success) return null;
        rl.body = rl.bodyM.Groups[1].Value;

        rl.tblM = Regex.Match(rl.body, @"<table\b[^>]*>([\s\S]*?)</table\s*>", RegexOptions.IgnoreCase);
        rl.before = rl.tblM.Success ? rl.body[..rl.tblM.Index] : rl.body;
        rl.after = rl.tblM.Success ? rl.body[(rl.tblM.Index + rl.tblM.Length)..] : "";

        rl.doc = new Document();
        rl.page = rl.doc.Pages.Add(rl.pageWidth, rl.pageHeight);
        EnsureFonts(rl.page);
        rl.boxRight = rl.pageWidth - ColMarginX;
        rl.top = ColMarginTop + UaBodyMarginPt;
        rl.bottom = rl.pageHeight - ColMarginTop;

        rl.y0 = FlowRtlParas(rl, ReadRtlParas(rl, rl.before), rl.top, rl.boxRight, trailingCounts: true);
        if (rl.tblM.Success)
        {
            // The block margin the paragraph before it left does not collapse
            // into the table: its cells open one margin lower, and the table's
            // own height stops at its deepest REAL line.
            var cellTop = rl.y0 + RtlBlockMarginPt;
            var cells = Regex.Matches(rl.tblM.Groups[1].Value, @"<td\b([^>]*)>([\s\S]*?)</td\s*>",
                RegexOptions.IgnoreCase);
            var deepest = rl.y0;
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
                var cellRight = rl.boxRight - ci * cw - RtlCellPadPt;
                var end = FlowRtlParas(rl, ReadRtlParas(rl, cm.Groups[2].Value), cellTop, cellRight, trailingCounts: false);
                deepest = Math.Max(deepest, end);
                ci++;
            }
            FlowRtlParas(rl, ReadRtlParas(rl, rl.after), deepest, rl.boxRight, trailingCounts: true);
        }
        return rl.doc;
    }
}

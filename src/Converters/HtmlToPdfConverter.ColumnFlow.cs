using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The INLINE CSS multi-column dialect (a container div whose own style declares
// `columns: N …`): the source renderer pours the container's block flow down N
// equal columns, filling each to the page's content bottom before opening the
// next and starting a fresh page after the last — no balancing, unlike the
// class-rule dialect in CssColumns.cs, which lays a single balanced page.
//
// The document this models is a filing export: every paragraph declares
// `margin: 0` and the vertical rhythm comes from EMPTY paragraphs, so the whole
// flow sits on one uniform line grid. Its numbered section headings are tiny
// percent-width tables, which pour through the same grid a line at a time.
internal static partial class HtmlToPdfConverter
{
    /// <summary>Render an inline-`columns` document, or null when no container
    /// declares them in its own style attribute.</summary>
    private static Document? TryRenderInlineCssColumns(string html, HtmlLoadOptions? options,
        double pageWidth, double pageHeight)
    {
        var contM = Regex.Match(html,
            @"<div\b[^>]*style\s*=\s*([""'])(?<st>(?:(?!\1).)*?(?<![-\w])columns\s*:\s*(?<n>\d+)[^""']*)\1[^>]*>",
            RegexOptions.IgnoreCase);
        if (!contM.Success) return null;
        var nCols = int.Parse(contM.Groups["n"].Value);
        if (nCols is < 2 or > 12) return null;
        var body = html[(contM.Index + contM.Length)..];
        var endM = Regex.Match(body, @"</body\s*>", RegexOptions.IgnoreCase);
        if (endM.Success) body = body[..endM.Index];

        // `column-gap` on the same rule; the CSS initial value is 1 em.
        var gapM = Regex.Match(contM.Groups["st"].Value,
            @"column-gap\s*:\s*([\d.]+\s*\w*)", RegexOptions.IgnoreCase);

        // The body's own inline style seeds the flow: its face, its size and the
        // horizontal padding that insets the content box on both sides.
        var bodyM = Regex.Match(html, @"<body\b[^>]*style\s*=\s*([""'])(?<st>(?:(?!\1).)*)\1",
            RegexOptions.IgnoreCase);
        var bodySt = bodyM.Success ? DecodeEntities(bodyM.Groups["st"].Value) : "";
        var face = "Times New Roman";
        if (Regex.Match(bodySt, @"font-family\s*:\s*([^;]+)", RegexOptions.IgnoreCase)
                is { Success: true } bfM
            && FirstFontFamily(bfM.Groups[1].Value) is { Length: > 0 } bfName
            && WinMetricsFor(bfName) is not null)
            face = bfName;
        if (WinMetricsFor(face) is not { } fm) return null;
        var fs = 10.0;
        if (Regex.Match(bodySt, @"font-size\s*:\s*([\d.]+\s*\w*)", RegexOptions.IgnoreCase)
                is { Success: true } bsM
            && TryParseLength(bsM.Groups[1].Value.Replace(" ", ""), out var bsPt) && bsPt > 0)
            fs = bsPt;
        var padX = 0.0;
        if (Regex.Match(bodySt, @"padding\s*:\s*([\d.]+\s*\w*)\s+([\d.]+\s*\w*)",
                RegexOptions.IgnoreCase) is { Success: true } bpM
            && TryParseLength(bpM.Groups[2].Value.Replace(" ", ""), out var padXv))
            padX = padXv;

        var lineH = MetricLineHeight(fs, HheaLineSumFor(face) ?? fm.sum);
        var drop = MetricBaselineDrop(fs, lineH, fm);
        var gap = gapM.Success && TryParseLength(gapM.Groups[1].Value.Replace(" ", ""), out var gv)
            ? gv : fs;

        var pageInfo = options?.PageInfo;
        var marginTop = pageInfo?.Margin?.IsTouched == true ? pageInfo.Margin.Top : ColMarginTop;
        var marginBottom = pageInfo?.Margin?.IsTouched == true ? pageInfo.Margin.Bottom : ColMarginTop;
        var marginX = (pageInfo?.Margin?.IsTouched == true ? pageInfo.Margin.Left : ColMarginX) + padX;
        var contentW = pageWidth - 2 * marginX;
        var colW = (contentW - (nCols - 1) * gap) / nCols;
        if (colW <= fs) return null;

        // ── the flow, as lines ────────────────────────────────────────────
        // Each entry is one line box: its runs (x offset inside the column,
        // text, bold) and whether its word gaps stretch to fill the column.
        var flow = new List<(List<(double Dx, string Text, bool Bold)> Runs, bool Justify)>();
        void AddBlank() => flow.Add((new List<(double, string, bool)>(), false));

        foreach (Match im in Regex.Matches(body, @"<(p|table)\b([^>]*)>([\s\S]*?)</\1\s*>",
                     RegexOptions.IgnoreCase))
        {
            if (im.Groups[1].Value.Equals("p", StringComparison.OrdinalIgnoreCase))
            {
                var inner = im.Groups[3].Value;
                var text = CollapseWs(DecodeEntities(Regex.Replace(inner, @"<[^>]+>", ""))).Trim();
                if (text.Length == 0) { AddBlank(); continue; }
                var justify = Regex.IsMatch(im.Groups[2].Value,
                    @"text-align\s*:\s*justify", RegexOptions.IgnoreCase);
                // A paragraph wholly inside <b>/<i> keeps that weight; mixed
                // emphasis draws at the paragraph's own weight (this dialect's
                // headings are wholly bold, its body wholly plain).
                var bold = Regex.IsMatch(inner, @"^\s*(?:<[bi]\b[^>]*>\s*)+")
                    && Regex.IsMatch(inner, @"(?:</[bi]\s*>\s*)+$");
                var lines = MeasuredWordWrap(text, colW, bold ? face + " Bold" : face, fs);
                for (var li = 0; li < lines.Length; li++)
                    flow.Add((new List<(double, string, bool)> { (0, lines[li], bold) },
                        justify && li < lines.Length - 1));
                continue;
            }
            // A percent-width table: its columns take their declared shares of
            // that box, each cell's lines pouring through the same grid. A row
            // with no ink is a blank line, exactly like an empty paragraph.
            var tw = colW;
            if (Regex.Match(im.Groups[2].Value, @"width\s*:\s*([\d.]+)%",
                    RegexOptions.IgnoreCase) is { Success: true } twM
                && double.TryParse(twM.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var twPct))
                tw = colW * twPct / 100.0;
            foreach (Match rm in Regex.Matches(im.Groups[3].Value,
                         @"<tr\b[^>]*>([\s\S]*?)</tr\s*>", RegexOptions.IgnoreCase))
            {
                var cells = new List<(double W, string Text, bool Bold)>();
                foreach (Match cm in Regex.Matches(rm.Groups[1].Value,
                             @"<t[dh]\b([^>]*)>([\s\S]*?)</t[dh]\s*>", RegexOptions.IgnoreCase))
                {
                    var cw = tw / Math.Max(1, cells.Count + 1);
                    if (Regex.Match(cm.Groups[1].Value, @"width\s*:\s*([\d.]+)%",
                            RegexOptions.IgnoreCase) is { Success: true } cwM
                        && double.TryParse(cwM.Groups[1].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var cwPct))
                        cw = tw * cwPct / 100.0;
                    var ctext = CollapseWs(DecodeEntities(
                        Regex.Replace(cm.Groups[2].Value, @"<[^>]+>", ""))).Trim();
                    cells.Add((cw, ctext, Regex.IsMatch(cm.Groups[2].Value, @"<b\b", RegexOptions.IgnoreCase)));
                }
                if (cells.Count == 0) continue;
                var wrapped = new List<string[]>();
                var deep = 0;
                foreach (var (cw, ctext, cb) in cells)
                {
                    var ls = ctext.Length == 0
                        ? System.Array.Empty<string>()
                        : MeasuredWordWrap(ctext, cw, cb ? face + " Bold" : face, fs);
                    wrapped.Add(ls);
                    deep = Math.Max(deep, ls.Length);
                }
                if (deep == 0) { AddBlank(); continue; }
                for (var li = 0; li < deep; li++)
                {
                    var runs = new List<(double, string, bool)>();
                    var cx = 0.0;
                    for (var ci = 0; ci < cells.Count; ci++)
                    {
                        if (li < wrapped[ci].Length && wrapped[ci][li].Length > 0)
                            runs.Add((cx, wrapped[ci][li], cells[ci].Bold));
                        cx += cells[ci].W;
                    }
                    flow.Add((runs, false));
                }
            }
        }
        if (flow.Count == 0) return null;

        // ── pour the lines down the columns ───────────────────────────────
        var doc = new Document();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        EnsureFonts(page);
        var col = 0;
        var y = marginTop;
        var bottom = pageHeight - marginBottom;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var (runs, justify) in flow)
        {
            if (y + lineH > bottom + 0.01)
            {
                if (++col >= nCols)
                {
                    page = doc.Pages.Add(pageWidth, pageHeight);
                    EnsureFonts(page);
                    col = 0;
                }
                y = marginTop;
            }
            var colX = marginX + col * (colW + gap);
            foreach (var (dx, text, bold) in runs)
            {
                var res = bold ? "F6" : "F5";
                var mFace = bold ? face + " Bold" : face;
                // A justified line stretches its word gaps to the column edge;
                // the paragraph's last line and every table cell stay natural.
                var tw2 = 0.0;
                if (justify)
                {
                    var spaces = 0;
                    foreach (var ch in text) if (ch == ' ') spaces++;
                    var natural = MeasureFaceText(mFace, text, fs);
                    if (spaces > 0 && natural < colW)
                        tw2 = (colW - natural) / spaces;
                }
                if (tw2 > 0)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("BT");
                    sb.Append(string.Create(inv, $"/{res} {fs:F1} Tf {tw2:F3} Tw "));
                    sb.Append(string.Create(inv, $"1 0 0 1 {colX + dx:F2} {pageHeight - y - drop:F2} Tm "));
                    sb.Append($"({EscapePdfString(text)}) Tj 0 Tw ");
                    sb.AppendLine("ET");
                    page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
                }
                else EmitPositionedRun(page, res, fs, colX + dx, pageHeight - y - drop, text);
            }
            y += lineH;
        }
        return doc;
    }
}

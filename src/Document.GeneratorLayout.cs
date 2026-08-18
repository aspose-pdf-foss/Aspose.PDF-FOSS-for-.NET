using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document : IDisposable
{
    /// <summary>Copy the font resource entries of <paramref name="src"/>'s page into
    /// <paramref name="dst"/>'s (additive; existing names win). Pre-built table content
    /// spilled onto a materialised overflow page references fonts registered on the flow's
    /// start page — without the merge those names dangle on the new page.</summary>
    private static void MergePageFontResources(Page src, Page dst)
    {
        try
        {
            var srcRes = src.Reader.ResolveDict(src.Dict.Get("Resources"));
            var srcFonts = srcRes is null ? null : src.Reader.ResolveDict(srcRes.Get("Font"));
            if (srcFonts is null) return;
            var dstFonts = Table.ResolvePageFontDict(dst);
            foreach (var key in srcFonts.Keys)
                if (!dstFonts.ContainsKey(key) && srcFonts.Get(key) is { } fo)
                    dstFonts.Set(key, fo);
        }
        catch { /* resource shapes vary; a failed merge just leaves the legacy behavior */ }
    }

    /// <summary>Resolve a <see cref="ColumnInfo"/> into per-column left edges and
    /// widths. Columns start at <paramref name="marginLeft"/> and run left-to-right
    /// separated by the spacing. Explicit ColumnWidths win; when fewer than
    /// ColumnCount are given the columns share the available width evenly.</summary>
    private static (double[] lefts, double[] widths) BuildColumnGeometry(
        ColumnInfo info, double marginLeft, double contentWidth)
    {
        var count = info.ColumnCount;
        if (count < 1) count = 1;

        var spacing = ParseFirst(info.ColumnSpacing, 0);

        var parsed = ParseLengths(info.ColumnWidths);
        var widths = new double[count];
        if (parsed.Count >= count)
        {
            for (var i = 0; i < count; i++) widths[i] = parsed[i];
        }
        else
        {
            // Not enough explicit widths — divide the content area evenly.
            var even = (contentWidth - spacing * (count - 1)) / count;
            if (even <= 0) even = contentWidth / count;
            for (var i = 0; i < count; i++) widths[i] = even;
        }

        var lefts = new double[count];
        var x = marginLeft;
        for (var i = 0; i < count; i++)
        {
            lefts[i] = x;
            x += widths[i] + spacing;
        }
        return (lefts, widths);
    }

    /// <summary>Greedily word-wrap <paramref name="text"/> to lines that fit
    /// <paramref name="availWidth"/> points, estimating glyph advance as
    /// <paramref name="charWidth"/> per character. A word longer than the line is
    /// hard-broken. Always returns at least one (possibly empty) line.</summary>
    private static List<string> WrapToWidth(string text, double availWidth, double charWidth)
    {
        var lines = new List<string>();
        if (charWidth <= 0) charWidth = 6;
        var maxChars = System.Math.Max(4, (int)(availWidth / charWidth));
        var remaining = text ?? string.Empty;
        while (remaining.Length > maxChars)
        {
            var breakAt = remaining.LastIndexOf(' ', System.Math.Min(maxChars, remaining.Length - 1));
            if (breakAt <= 0) breakAt = maxChars;
            lines.Add(remaining[..breakAt]);
            remaining = remaining[breakAt..].TrimStart();
        }
        lines.Add(remaining);
        return lines;
    }

    /// <summary>Parse a space/comma-separated length list (e.g. "105 105 105").</summary>
    private static List<double> ParseLengths(string? s)
    {
        var result = new List<double>();
        if (string.IsNullOrWhiteSpace(s)) return result;
        foreach (var tok in s.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            if (double.TryParse(tok, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v))
                result.Add(v);
        return result;
    }

    /// <summary>First length in <paramref name="s"/>, or <paramref name="fallback"/>.</summary>
    private static double ParseFirst(string? s, double fallback)
    {
        var list = ParseLengths(s);
        return list.Count > 0 ? list[0] : fallback;
    }

    /// <summary>
    /// Split an HTML block's TextFragment into segments so each inline &lt;a href&gt;
    /// range carries a <see cref="WebHyperlink"/>. The fragment text is unchanged
    /// (segment texts concatenate back to it); the layout engine emits a Link
    /// annotation over each hyperlinked segment's rendered run.
    /// </summary>
    /// <summary>The browser default (user-agent) body face for HTML that declares no
    /// font-family: a serif, which resolves to the Standard-14 Times family — no font
    /// program is embedded for it.</summary>
    private const string HtmlUaSerifFontName = "Times-Roman";

    /// <summary>The browser default block font size — 16 px = 12 pt.</summary>
    private const double HtmlUaBlockFontSize = 12.0;

    // ---- procedure-step acknowledge cluster geometry (the sr-ack DIV generation) ----
    // Every value is calibrated to 0.01 pt against the stylesheet's own
    // declarations: blank border-boxes (boolean height:18px + borders, checkbox
    // min-height:10px, signature min-height:15px, each with its 1px bottom rule stroked
    // half a width inside), the 8px hair-space line the generator writes above a
    // checkbox blank, 6pt bold labels on a 9pt slot (label box + padding-top 3px).
    private const double ackCellPitch = 112.5;   // widget cell pitch

    private const double ackRightInset = 158.10; // sheet right → last widget's left

    private const double ackBlankW = 105.0;      // checkbox/signature rule width

    private const double ackBoolRule = 13.125;   // cluster top → boolean blank rule

    private const double ackChkRule = 7.125;     // cluster top → checkbox blank rule

    private const double ackSigRule = 11.625;    // cluster top → signature blank rule

    private const double ackHairLine = 6.75;     // the hair space's own 8px line box

    private const double ackLabelDrop = 8.08;    // rule → first caption/label baseline

    private const double ackLabelPitch = 9.0;    // 6pt label box + padding-top 3px

    // last label baseline → the cluster's own bottom edge (the 6pt label's descent);
    // the inter-row paragraph gap is the row loop's own and must not be spent twice
    private const double ackAfter = 1.29;

    private const double ackBoolBoxW = 49.95;    // option box width (45% of the cell)

    private const double ackBoolBox2 = 59.79;    // cell left → second option box left

    private const double ackBoolCap2 = 58.13;    // cell left → second caption left

    private const double ackBoolBoxH = 13.5;     // option box border-box height

    private const double ackBoxStroke = 2.25;    // the picked option's 3px frame

    // a fresh page's first step row keeps its top margin (15px), uncollapsed there
    private const double ackRowPageTopMargin = 11.25;

    // the sheet's `.step-col-full .acw/asw-label:last-child { margin-bottom: 12px }`
    private const double ackChkSigLabelMargin = 9.0;

    /// <summary>The parsed report-band wrapper: the band's centred title lines, its packed
    /// caption line, and the data table's rows (description + up to three values).</summary>
    private sealed record RbBand(
        System.Collections.Generic.List<string> Titles,
        string Caption,
        System.Collections.Generic.List<(string Desc, System.Collections.Generic.List<string> Vals,
            bool NewBlock)> Rows);

    /// <summary>Recognise the report-band wrapper (see the render site): a two-row
    /// one-column table whose first cell's fragment holds a title block plus a
    /// header-caption <c>&lt;thead&gt;</c> table and whose second holds the data table.
    /// The dialect writes JSON-escaped attribute quotes; they are unescaped here.
    /// Null when the shape doesn't match.</summary>
    private static RbBand? RbTryParse(Table table)
    {
        static string? CellHtml(Row r)
        {
            if (r.Cells.Count != 1) return null;
            string? all = null;
            foreach (var p in r.Cells.At(0).Paragraphs)
            {
                if (p is not HtmlFragment hf || hf.HtmlContent is not { Length: > 0 } hc) return null;
                all = (all ?? "") + hc;
            }
            return all?.Replace("\\\"", "\"");
        }
        var head = CellHtml(table.Rows.At(0));
        var body = CellHtml(table.Rows.At(1));
        if (head is null || body is null) return null;
        if (!System.Text.RegularExpressions.Regex.IsMatch(head, @"<thead\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            || !System.Text.RegularExpressions.Regex.IsMatch(body, @"<table\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return null;

        static string Clean(string s) => System.Text.RegularExpressions.Regex.Replace(
            System.Net.WebUtility.HtmlDecode(HtmlFragment.StripHtmlTags(s)), @"\s+", " ").Trim();

        // Title lines: the text chunks of the band ABOVE the caption table, split at
        // block/break boundaries.
        var titles = new System.Collections.Generic.List<string>();
        var tableAt = head.IndexOf("<table", StringComparison.OrdinalIgnoreCase);
        var titleHtml = System.Text.RegularExpressions.Regex.Replace(
            tableAt > 0 ? head[..tableAt] : head,
            @"<style[^>]*>[\s\S]*?</style>|<head\b[^>]*>[\s\S]*?</head>|<title\b[^>]*>[\s\S]*?</title>", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (var piece in System.Text.RegularExpressions.Regex.Split(titleHtml,
                     @"<br\s*/?>|</div>|</center>|</p>",
                     System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            var t = Clean(piece);
            if (t.Length > 0) titles.Add(t);
        }
        if (titles.Count == 0) return null;

        // The caption: the header table's th texts, packed into one line.
        var caps = new System.Collections.Generic.List<string>();
        foreach (System.Text.RegularExpressions.Match th in
                 System.Text.RegularExpressions.Regex.Matches(head[tableAt..],
                     @"<th\b[^>]*>([\s\S]*?)</th>",
                     System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            var t = Clean(th.Groups[1].Value);
            if (t.Length > 0) caps.Add(t);
        }
        if (caps.Count == 0) return null;

        // The data rows: description + values per <tr>, table by table — the first
        // row of every table AFTER the first carries the inter-table seam.
        var rows = new System.Collections.Generic.List<(string, System.Collections.Generic.List<string>, bool)>();
        var tableIdx = 0;
        foreach (System.Text.RegularExpressions.Match tbl in
                 System.Text.RegularExpressions.Regex.Matches(body,
                     @"<table\b[^>]*>([\s\S]*?)</table>",
                     System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            var firstOfTable = tableIdx++ > 0;
            foreach (System.Text.RegularExpressions.Match tr in
                     System.Text.RegularExpressions.Regex.Matches(tbl.Groups[1].Value,
                         @"<tr\b[^>]*>([\s\S]*?)</tr>",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                var cells = new System.Collections.Generic.List<string>();
                foreach (System.Text.RegularExpressions.Match td in
                         System.Text.RegularExpressions.Regex.Matches(tr.Groups[1].Value,
                             @"<td\b[^>]*>([\s\S]*?)</td>",
                             System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    cells.Add(Clean(td.Groups[1].Value));
                if (cells.Count == 0) continue;
                rows.Add((cells[0], cells.GetRange(1, cells.Count - 1), firstOfTable));
                firstOfTable = false;
            }
        }
        if (rows.Count == 0) return null;
        return new RbBand(titles, string.Join(" ", caps), rows);
    }

    /// <summary>Standard-14 advance width of <paramref name="text"/> in points.</summary>
    private static double MeasureStd14Width(string text, string fontName, double fontSize)
    {
        double w = 0;
        foreach (var c in text)
        {
            var cw = Text.Standard14Fonts.GetWidth(fontName, c < 256 ? c : '?');
            w += (cw > 0 ? cw : 500) * fontSize / 1000.0;
        }
        return w;
    }

    /// <summary>The shared blank-rule depth and the whole cluster's height for a step
    /// row's acknowledge widgets — used identically by the keep-together measurer and
    /// the renderer, so pagination prices exactly what the page draws.</summary>
    private static (double MaxStack, double ClusterH) PsAckClusterGeom(
        Converters.HtmlToPdfConverter.StepRow row)
    {
        var maxStack = 0.0;
        var maxBottom = 0.0;
        foreach (var w in row.Acks)
        {
            var st = w.Kind switch
            {
                "boolean" => ackBoolRule,
                "signature" => ackSigRule,
                _ => ackChkRule + (w.Hair ? ackHairLine : 0),
            };
            if (st > maxStack) maxStack = st;
            var lines = w.Labels.Count + (w.Kind == "boolean" ? 1 : 0);
            if (lines <= 0) continue;
            // the sheet gives a checkbox/signature widget's LAST label a 12px bottom
            // margin (the boolean's labels carry none), so a cluster whose deepest
            // column is one of those ends that much lower
            var bottom = ackLabelDrop + ackLabelPitch * (lines - 1)
                + (w.Kind != "boolean" ? ackChkSigLabelMargin : 0);
            if (bottom > maxBottom) maxBottom = bottom;
        }
        return (maxStack, maxStack + maxBottom + ackAfter);
    }

    /// <summary>Split a parsed HTML block's text into consecutive runs of uniform inline
    /// emphasis from the <c>&lt;b&gt;</c>/<c>&lt;strong&gt;</c> and <c>&lt;u&gt;</c> ranges
    /// the parser recorded. Returns null when the block needs no split — one style
    /// throughout and nothing underlined — so it keeps drawing through the ordinary
    /// whole-block writer.</summary>
    private static System.Collections.Generic.List<(int Start, int Length, bool Bold, bool Underline)>?
        HtmlEmphasisRuns(Converters.HtmlToPdfConverter.Block b)
    {
        var n = b.Text?.Length ?? 0;
        if (n == 0) return null;
        var haveBold = b.BoldRuns is { Count: > 0 };
        var haveUnder = b.UnderlineRuns is { Count: > 0 };
        if (!haveBold && !haveUnder) return null;

        var bold = new bool[n];
        var under = new bool[n];
        static void Mark(System.Collections.Generic.List<(int Start, int Length)>? src, bool[] flags)
        {
            if (src is null) return;
            foreach (var (s, len) in src)
                for (var i = Math.Max(s, 0); i < Math.Min(s + len, flags.Length); i++)
                    flags[i] = true;
        }
        Mark(b.BoldRuns, bold);
        Mark(b.UnderlineRuns, under);

        var runs = new System.Collections.Generic.List<(int Start, int Length, bool Bold, bool Underline)>();
        var start = 0;
        for (var i = 1; i <= n; i++)
            if (i == n || bold[i] != bold[start] || under[i] != under[start])
            {
                runs.Add((start, i - start, bold[start], under[start]));
                start = i;
            }
        // Uniformly-styled and undecorated: nothing the block-wide face cannot express.
        if (runs.Count == 1 && !runs[0].Underline) return null;
        return runs;
    }

    private static void ApplyHtmlAnchorSegments(Text.TextFragment bf, string text,
        System.Collections.Generic.List<(int Start, int Length, string Url)> anchors)
    {
        var ordered = new System.Collections.Generic.List<(int Start, int Length, string Url)>();
        foreach (var a in anchors)
            if (a.Start >= 0 && a.Length > 0 && a.Start < text.Length && !string.IsNullOrEmpty(a.Url))
                ordered.Add(a);
        ordered.Sort((x, y) => x.Start.CompareTo(y.Start));
        if (ordered.Count == 0) return;

        var parts = new System.Collections.Generic.List<(string Txt, string? Url)>();
        int pos = 0;
        foreach (var (start, len, url) in ordered)
        {
            var s = Math.Max(start, pos);
            if (s >= text.Length) break;
            if (s > pos) parts.Add((text.Substring(pos, s - pos), null));
            var end = Math.Min(start + len, text.Length);
            if (end > s) parts.Add((text.Substring(s, end - s), url));
            pos = Math.Max(pos, end);
        }
        if (pos < text.Length) parts.Add((text.Substring(pos), null));
        if (parts.Count == 0) return;

        bf.Segments.Clear();
        foreach (var (txt, url) in parts)
        {
            if (txt.Length == 0) continue;
            var seg = new Text.TextSegment(txt);
            seg.TextState.FontSize = bf.TextState.FontSize;
            seg.TextState.IsBold = bf.TextState.IsBold;
            seg.TextState.IsItalic = bf.TextState.IsItalic;
            if (bf.TextState.FontName is not null) seg.TextState.FontName = bf.TextState.FontName;
            if (bf.TextState.Font is not null) seg.TextState.Font = bf.TextState.Font;
            if (bf.TextState.ForegroundColor is not null) seg.TextState.ForegroundColor = bf.TextState.ForegroundColor;
            if (url is not null) seg.Hyperlink = new WebHyperlink(url);
            bf.Segments.Add(seg);
        }
    }

    /// Render every &lt;img&gt; element whose source resolves to a readable local file
    /// (a <c>file://</c> URI or a plain path) as an image XObject in the flowing HTML
    /// content. Remote (http/https) sources are skipped — they are not fetched — leaving
    /// the existing alt-text fallback in place. Each image is placed at the current flow
    /// cursor, scaled to its HTML width/height attributes (falling back to the intrinsic
    /// size and aspect ratio), and clamped to the content width.
    /// </summary>
    private void RenderHtmlImages(string htmlContent, FlowLayout flow, double marginLeft, double marginRight,
        System.Collections.Generic.List<byte[]>? inlineSvgs = null)
    {
        if (string.IsNullOrEmpty(htmlContent)) return;
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                     htmlContent, @"<img\b[^>]*>", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            var tag = m.Value;
            var srcM = System.Text.RegularExpressions.Regex.Match(tag,
                @"\bsrc\s*=\s*['""]?([^'""\s>]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!srcM.Success) continue;
            var src = srcM.Groups[1].Value;
            // Vector sources (inline-<svg> placeholders and SVG files) rasterize through
            // the SVG engine; their natural size is the SVG viewport in CSS px (× 0.75 pt).
            byte[]? bytes;
            double svgNatW = 0, svgNatH = 0;
            var isInlineSvg = src.StartsWith("inline-svg:", StringComparison.Ordinal);
            if (isInlineSvg)
                bytes = inlineSvgs is not null
                        && int.TryParse(src["inline-svg:".Length..], out var svgIdx)
                        && svgIdx >= 0 && svgIdx < inlineSvgs.Count
                    ? inlineSvgs[svgIdx] : null;
            else
                bytes = LoadHtmlImageBytes(src);
            if (bytes is null) continue;
            byte[]? rawSvg = null;
            if (Converters.HtmlToPdfConverter.IsSvgBytes(bytes))
            {
                rawSvg = bytes;
                bytes = ImageRasterizer.RasterizeSvg(bytes, out var vw, out var vh);
                if (bytes is null) continue;
                svgNatW = vw * 0.75; svgNatH = vh * 0.75;
            }

            double natW = 0, natH = 0;
            if (svgNatW > 0 && svgNatH > 0) { natW = svgNatW; natH = svgNatH; }
            else if (TryGetImageNaturalSizePt(bytes, applyResolution: false, out natW, out natH))
            {
                // HTML sizes an unsized <img> in CSS pixels regardless of the file's
                // embedded DPI: natural px × 0.75 pt.
                natW *= SvgPxToPt; natH *= SvgPxToPt;
            }
            var w = ParseHtmlImgDimension(tag, "width");
            var h = ParseHtmlImgDimension(tag, "height");
            // An inline-<svg> placeholder's width/height came from the SVG root attributes,
            // which are CSS pixels — scale to points like the natural size.
            if (isInlineSvg) { w *= 0.75; h *= 0.75; }
            if (w <= 0 && h <= 0) { w = natW > 0 ? natW : 72; h = natH > 0 ? natH : 72; }
            else if (h <= 0) h = (natW > 0 && natH > 0) ? w * natH / natW : w;
            else if (w <= 0) w = (natW > 0 && natH > 0) ? h * natW / natH : h;

            var availW = flow.CurrentPage.Width - marginLeft - marginRight;
            if (availW > 0 && w > availW) { h *= availW / w; w = availW; }

            var topY = flow.CurrentY;
            // An SVG that draws nothing but <text> is TYPE, not a picture: the HTML
            // engine sets its runs in the UA serif at the size the viewBox transform
            // gives them, so the page keeps selectable text and exact glyph placement
            // instead of a resampled bitmap.
            if (rawSvg is not null
                && TryParseSvgTextRuns(rawSvg, w, h) is { Count: > 0 } svgRuns)
            {
                var svgFont = SvgTextFont();
                foreach (var (rx, rbase, rtext, rsize, ranchor) in svgRuns)
                {
                    var runW = MeasureSvgTextWidth(rtext, rsize);
                    var x0 = ranchor switch
                    {
                        "middle" => rx - runW / 2,
                        "end" => rx - runW,
                        _ => rx,
                    };
                    flow.WriteAbsoluteText(marginLeft + x0, topY - rbase, rtext, rsize, svgFont);
                }
                flow.AdvanceY(h);
                continue;
            }
            flow.CurrentPage.AddImage(bytes, new Rectangle(marginLeft, topY - h, marginLeft + w, topY));
            flow.AdvanceY(h);
        }
    }

    /// CSS pixel → PDF point (96 dpi reference pixel).
    private const double SvgPxToPt = 0.75;

    /// CSS `medium`, the initial font-size an SVG &lt;text&gt; inherits when none is declared.
    private const double SvgDefaultFontSizePx = 16.0;

    /// Elements that put ink on the page other than text — their presence sends the
    /// whole SVG down the rasterizer.
    private static readonly System.Text.RegularExpressions.Regex SvgNonTextInkRegex = new(
        @"<(rect|circle|ellipse|line|polyline|polygon|path|image|use|foreignObject)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase
        | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>The UA serif the HTML engine sets SVG text in (an SVG font-family naming
    /// a face that is not installed falls back to it).</summary>
    private static Text.Font? SvgTextFont()
    {
        try { return Text.FontRepository.FindFont("Times New Roman", ignoreCase: true); }
        catch { return null; }
    }

    private static double MeasureSvgTextWidth(string text, double sizePt)
    {
        double w = 0;
        foreach (var c in text)
        {
            var cw = Text.Standard14Fonts.GetWidth("Times-Roman", c < 256 ? c : '?');
            w += (cw < 0 ? 500 : cw) * sizePt / 1000.0;
        }
        return w;
    }

    /// <summary>The <c>&lt;text&gt;</c> runs of an SVG that draws nothing else, mapped
    /// through the viewBox transform into the element box: <c>X</c>/<c>Baseline</c> are
    /// points from the box's top-left and <c>SizePt</c> is the transformed font size.
    /// The transform is the default <c>xMidYMid meet</c>: one uniform scale
    /// <c>min(boxW/viewBoxW, boxH/viewBoxH)</c>, the surplus split as centring on each
    /// axis. Null when the SVG paints shapes or images too, or carries no text — those
    /// keep the rasterizer.</summary>
    private static System.Collections.Generic.List<(double X, double Baseline, string Text,
        double SizePt, string Anchor)>? TryParseSvgTextRuns(byte[] svgBytes, double boxWpt, double boxHpt)
    {
        if (svgBytes is null || svgBytes.Length == 0 || boxWpt <= 0 || boxHpt <= 0) return null;
        string svg;
        try { svg = System.Text.Encoding.UTF8.GetString(svgBytes); }
        catch { return null; }
        if (svg.IndexOf("<text", StringComparison.OrdinalIgnoreCase) < 0) return null;
        if (SvgNonTextInkRegex.IsMatch(svg)) return null;

        var rootEnd = svg.IndexOf('>');
        var root = rootEnd > 0 ? svg[..(rootEnd + 1)] : svg;
        double scale = 1, tx = 0, ty = 0, vbX = 0, vbY = 0;
        var vb = System.Text.RegularExpressions.Regex.Match(root,
            @"viewBox\s*=\s*['""]\s*(-?[\d.]+)[,\s]+(-?[\d.]+)[,\s]+([\d.]+)[,\s]+([\d.]+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var boxWpx = boxWpt / SvgPxToPt;
        var boxHpx = boxHpt / SvgPxToPt;
        if (vb.Success
            && SvgNum(vb.Groups[1].Value) is { } x0 && SvgNum(vb.Groups[2].Value) is { } y0
            && SvgNum(vb.Groups[3].Value) is { } vbW && SvgNum(vb.Groups[4].Value) is { } vbH
            && vbW > 0 && vbH > 0)
        {
            vbX = x0; vbY = y0;
            scale = Math.Min(boxWpx / vbW, boxHpx / vbH);
            tx = (boxWpx - vbW * scale) / 2;
            ty = (boxHpx - vbH * scale) / 2;
        }

        var runs = new System.Collections.Generic.List<(double, double, string, double, string)>();
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                     svg, @"<text\b(?<a>[^>]*)>(?<t>[\s\S]*?)</text\s*>",
                     System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            var attrs = m.Groups["a"].Value;
            var text = System.Text.RegularExpressions.Regex.Replace(
                System.Text.RegularExpressions.Regex.Replace(m.Groups["t"].Value, @"<[^>]*>", ""),
                @"\s+", " ").Trim();
            if (text.Length == 0) continue;
            var xv = SvgAttrNumber(attrs, "x") ?? 0;
            var yv = SvgAttrNumber(attrs, "y") ?? 0;
            var fontPx = SvgFontSizePx(attrs) ?? SvgDefaultFontSizePx;
            var anchor = SvgStyleValue(attrs, "text-anchor") ?? "start";
            runs.Add((((xv - vbX) * scale + tx) * SvgPxToPt,
                      ((yv - vbY) * scale + ty) * SvgPxToPt,
                      text, fontPx * scale * SvgPxToPt, anchor));
        }
        return runs.Count > 0 ? runs : null;
    }

    private static double? SvgNum(string s) =>
        double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;

    /// <summary>A presentation attribute's number (<c>x="130"</c>).</summary>
    private static double? SvgAttrNumber(string attrs, string name)
    {
        var m = System.Text.RegularExpressions.Regex.Match(attrs,
            @"(?<![-\w])" + name + @"\s*=\s*['""]?\s*(-?[\d.]+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? SvgNum(m.Groups[1].Value) : null;
    }

    /// <summary>A property from the element's <c>style</c> attribute, else the
    /// same-named presentation attribute.</summary>
    private static string? SvgStyleValue(string attrs, string name)
    {
        var st = System.Text.RegularExpressions.Regex.Match(attrs,
            @"style\s*=\s*['""]([^'""]*)['""]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (st.Success)
        {
            var p = System.Text.RegularExpressions.Regex.Match(st.Groups[1].Value,
                @"(?<![-\w])" + name + @"\s*:\s*([^;]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (p.Success) return p.Groups[1].Value.Trim();
        }
        var a = System.Text.RegularExpressions.Regex.Match(attrs,
            @"(?<![-\w])" + name + @"\s*=\s*['""]([^'""]*)['""]",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return a.Success ? a.Groups[1].Value.Trim() : null;
    }

    /// <summary>The declared font size in CSS px; a <c>pt</c> value converts, a bare
    /// number is px (SVG user units, which the element box is measured in).</summary>
    private static double? SvgFontSizePx(string attrs)
    {
        var raw = SvgStyleValue(attrs, "font-size");
        if (raw is null) return null;
        var m = System.Text.RegularExpressions.Regex.Match(raw, @"([\d.]+)\s*(px|pt)?",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success || SvgNum(m.Groups[1].Value) is not { } v) return null;
        return m.Groups[2].Value.Equals("pt", StringComparison.OrdinalIgnoreCase) ? v / SvgPxToPt : v;
    }

    /// <summary>Load the bytes for an &lt;img&gt; source if it is a readable local file
    /// (file:// URI or a path on disk). Returns null for remote or unreadable sources.</summary>
    private static byte[]? LoadHtmlImageBytes(string src)
    {
        if (string.IsNullOrWhiteSpace(src)) return null;
        try
        {
            if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                return null;
            var path = src;
            if (src.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                && Uri.TryCreate(src, UriKind.Absolute, out var uri) && uri.IsFile)
                path = uri.LocalPath;
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch { return null; }
    }

    /// <summary>Parse a numeric width/height attribute (px) from an &lt;img&gt; tag; 0 if absent.</summary>
    private static double ParseHtmlImgDimension(string tag, string name)
    {
        var m = System.Text.RegularExpressions.Regex.Match(tag,
            @"\b" + name + @"\s*=\s*['""]?(\d+(?:\.\d+)?)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success && double.TryParse(m.Groups[1].Value,
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? v : 0;
    }

    /// <summary>
    /// Read an image's intrinsic size in PDF points from its PNG/JPEG header without
    /// decoding pixels: point size = pixels * 72 / DPI, with DPI taken from the PNG
    /// pHYs chunk or JPEG JFIF density (defaulting to 72 when absent). Returns false
    /// for formats this can't parse, leaving the caller to fall back to the page budget.
    /// </summary>
    /// <summary>Whether a JPEG uses progressive (SOF2) encoding, which the embedded-image
    /// decoder cannot read — such images are re-encoded to a baseline raster instead.</summary>
    private static bool IsProgressiveJpeg(byte[] d)
    {
        if (d is null || d.Length < 4 || d[0] != 0xFF || d[1] != 0xD8) return false;
        int i = 2;
        while (i + 3 < d.Length)
        {
            if (d[i] != 0xFF) { i++; continue; }
            int m = d[i + 1];
            if (m == 0xC2) return true;                       // SOF2 = progressive
            if (m == 0xD8 || m == 0xD9 || (m >= 0xD0 && m <= 0xD7)) { i += 2; continue; }
            if (m == 0xDA) return false;                      // Start of scan: no SOF2 before it
            int seg = (d[i + 2] << 8) | d[i + 3];
            if (seg < 2) return false;
            i += 2 + seg;
        }
        return false;
    }

    /// <summary>
    /// Decode a raster image of any platform-supported format (TIFF, BMP, GIF, ...) into one
    /// PNG per frame (a multi-page TIFF yields one PNG per page), preserving each frame's DPI
    /// so its natural size is recovered. Returns <c>null</c> when the bytes cannot be decoded
    /// or the platform image codec is unavailable.
    /// </summary>
    private static System.Collections.Generic.List<byte[]>? TryDecodeImageFramesAsPng(byte[] data)
    {
        if (data is null || data.Length < 4) return null;
        // DICOM (.dcm) decodes with the built-in managed decoder — the platform
        // codec below has no DICOM support and would silently drop the image.
        if (IO.DicomDecoder.IsDicom(data)
            && IO.DicomDecoder.DecodeFramesAsPng(data) is { Count: > 0 } dicomFrames)
            return dicomFrames;
        // TIFF decodes with the built-in managed decoder — platform-independent,
        // and resilient to damaged multi-frame files (corrupt frames are skipped,
        // the rest still paginate). The platform codec below remains the fallback
        // for TIFF flavours the managed decoder declines (e.g. JPEG-in-TIFF) and
        // for the other raster formats (BMP / GIF / ...).
        if (IO.TiffDecoder.IsTiff(data)
            && IO.TiffDecoder.DecodeFramesAsPng(data) is { Count: > 0 } tiffFrames)
            return tiffFrames;
#pragma warning disable CA1416 // platform-guarded: System.Drawing image codecs (Windows)
        try
        {
            using var src = new System.IO.MemoryStream(data);
            using var img = System.Drawing.Image.FromStream(src);
            var frames = new System.Collections.Generic.List<byte[]>();
            int frameCount;
            try { frameCount = img.GetFrameCount(System.Drawing.Imaging.FrameDimension.Page); }
            catch { frameCount = 1; }
            if (frameCount < 1) frameCount = 1;
            for (int fr = 0; fr < frameCount; fr++)
            {
                // A corrupt frame in a multi-frame file (SelectActiveFrame or the
                // decode throws) is skipped rather than dropping the whole image —
                // the remaining frames still paginate, preserving the page
                // count for such files.
                try
                {
                    if (frameCount > 1) img.SelectActiveFrame(System.Drawing.Imaging.FrameDimension.Page, fr);
                    using var bmp = new System.Drawing.Bitmap(img.Width, img.Height,
                        System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    bmp.SetResolution(img.HorizontalResolution > 0 ? img.HorizontalResolution : 96f,
                                      img.VerticalResolution > 0 ? img.VerticalResolution : 96f);
                    using (var g = System.Drawing.Graphics.FromImage(bmp))
                    {
                        g.Clear(System.Drawing.Color.White);
                        g.DrawImage(img, 0, 0, img.Width, img.Height);
                    }
                    using var outMs = new System.IO.MemoryStream();
                    bmp.Save(outMs, System.Drawing.Imaging.ImageFormat.Png);
                    frames.Add(outMs.ToArray());
                }
                catch when (frameCount > 1)
                {
                }
            }
            return frames.Count > 0 ? frames : null;
        }
        catch { return null; }
#pragma warning restore CA1416
    }

    internal static bool TryGetImageNaturalSizePt(byte[] d, out double widthPt, out double heightPt)
        => TryGetImageNaturalSizePt(d, applyResolution: true, out widthPt, out heightPt);

    /// <summary>Natural image size in points. When <paramref name="applyResolution"/>
    /// is false (the <see cref="Image.IsApplyResolution"/> default) the embedded DPI is
    /// ignored and one pixel maps to one point, matching how an unsized generator
    /// <see cref="Image"/> is laid out.</summary>
    internal static bool TryGetImageNaturalSizePt(byte[] d, bool applyResolution, out double widthPt, out double heightPt)
    {
        widthPt = 0; heightPt = 0;
        if (d is null || d.Length < 24) return false;
        int BE16(int o) => (d[o] << 8) | d[o + 1];
        int BE32(int o) => (d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3];

        // JPEG 2000 box file (.jp2/.jpx): dimensions live in the 'ihdr' box (height@0,
        // width@4 of its data). One pixel maps to one point (JP2 carries DPI only in the
        // optional 'res' box, which we ignore for parity with unsized generator images).
        if (d.Length >= 12 && d[0] == 0x00 && d[1] == 0x00 && d[2] == 0x00 && d[3] == 0x0C
            && d[4] == 0x6A && d[5] == 0x50 && d[6] == 0x20 && d[7] == 0x20)
        {
            for (int i = 8; i + 16 <= d.Length; i++)
            {
                if (d[i] == 'i' && d[i + 1] == 'h' && d[i + 2] == 'd' && d[i + 3] == 'r')
                {
                    int ph = BE32(i + 4), pw = BE32(i + 8);
                    if (pw > 0 && ph > 0) { widthPt = pw; heightPt = ph; return true; }
                    break;
                }
            }
            return false;
        }
        // Raw JPEG 2000 codestream: SOC (FF4F) then SIZ (FF51); Xsiz@8, Ysiz@12.
        if (d.Length >= 16 && d[0] == 0xFF && d[1] == 0x4F && d[2] == 0xFF && d[3] == 0x51)
        {
            long xs = (uint)BE32(8), ys = (uint)BE32(12);
            if (xs > 0 && ys > 0) { widthPt = xs; heightPt = ys; return true; }
            return false;
        }

        // BMP: 'BM' signature; BITMAPINFOHEADER width@18, height@22 (both LE;
        // height may be negative for top-down rows), biXPelsPerMeter@38.
        if (d[0] == 0x42 && d[1] == 0x4D && d.Length >= 30)
        {
            int LE32(int o) => d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24);
            int bw = LE32(18), bh = Math.Abs(LE32(22));
            if (bw <= 0 || bh <= 0) return false;
            double bDpi = 72;
            if (applyResolution && d.Length >= 46)
            {
                var ppm = LE32(38);
                if (ppm > 0) bDpi = ppm * 0.0254;
            }
            widthPt = bw * 72.0 / bDpi;
            heightPt = bh * 72.0 / bDpi;
            return true;
        }

        // PNG: 8-byte signature, then IHDR (width@16, height@20). pHYs gives DPI.
        if (d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47)
        {
            int pw = BE32(16), ph = BE32(20);
            if (pw <= 0 || ph <= 0) return false;
            double dpiX = 72, dpiY = 72;
            for (int i = 8; i + 12 <= d.Length;)
            {
                int len = BE32(i);
                if (len < 0) break;
                if (d[i + 4] == 'p' && d[i + 5] == 'H' && d[i + 6] == 'Y' && d[i + 7] == 's' && i + 8 + 9 <= d.Length)
                {
                    long ppuX = (uint)BE32(i + 8), ppuY = (uint)BE32(i + 12);
                    if (d[i + 16] == 1 && ppuX > 0 && ppuY > 0) // unit = metre
                    {
                        dpiX = ppuX * 0.0254;
                        dpiY = ppuY * 0.0254;
                    }
                    break;
                }
                if (d[i + 4] == 'I' && d[i + 5] == 'D' && d[i + 6] == 'A' && d[i + 7] == 'T') break;
                i += 12 + len; // length + type + data + CRC
            }
            if (dpiX <= 0 || !applyResolution) dpiX = 72;
            if (dpiY <= 0 || !applyResolution) dpiY = 72;
            widthPt = pw * 72.0 / dpiX;
            heightPt = ph * 72.0 / dpiY;
            return true;
        }

        // JPEG: scan markers for a Start-Of-Frame (dimensions) and JFIF APP0 (density).
        if (d[0] == 0xFF && d[1] == 0xD8)
        {
            double dpiX = 72, dpiY = 72; int pw = 0, ph = 0;
            int p = 2;
            while (p + 4 < d.Length)
            {
                if (d[p] != 0xFF) { p++; continue; }
                int marker = d[p + 1];
                if (marker == 0xD8 || marker == 0xD9 || (marker >= 0xD0 && marker <= 0xD7)) { p += 2; continue; }
                int seg = BE16(p + 2);
                if (seg < 2) break;
                if (marker == 0xE0 && p + 4 + 14 <= d.Length
                    && d[p + 4] == (byte)'J' && d[p + 5] == (byte)'F' && d[p + 6] == (byte)'I' && d[p + 7] == (byte)'F')
                {
                    int units = d[p + 11];
                    int dx = BE16(p + 12), dy = BE16(p + 14);
                    if (dx > 0 && dy > 0)
                    {
                        if (units == 1) { dpiX = dx; dpiY = dy; }            // dots per inch
                        else if (units == 2) { dpiX = dx * 2.54; dpiY = dy * 2.54; } // dots per cm
                    }
                }
                else if ((marker >= 0xC0 && marker <= 0xCF)
                         && marker != 0xC4 && marker != 0xC8 && marker != 0xCC
                         && p + 9 <= d.Length)
                {
                    ph = BE16(p + 5);
                    pw = BE16(p + 7);
                }
                p += 2 + seg;
            }
            if (pw <= 0 || ph <= 0) return false;
            if (dpiX <= 0 || !applyResolution) dpiX = 72;
            if (dpiY <= 0 || !applyResolution) dpiY = 72;
            widthPt = pw * 72.0 / dpiX;
            heightPt = ph * 72.0 / dpiY;
            return true;
        }

        return false;
    }
}

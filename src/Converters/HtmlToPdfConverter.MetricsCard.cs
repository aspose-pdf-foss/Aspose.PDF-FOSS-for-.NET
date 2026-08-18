using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // ── The body-face METRICS CARD ──────────────────────────────────────────────
    // A one-card report: `body { font-family: Verdana-ish; font-size: Npx;
    // width: 100% }`, a `.main` border-collapse table whose single column stacks
    // a tinted HEADING band over a body cell, and a nested `.metrics` table
    // (width: inherit — the enclosing td's percent class) of bold-label /
    // value rows. The source renderer draws the card with the real body face at
    // the px-derived sizes, the collapsed 2px frame around both cells, and the
    // nested grid's 75%/25% columns.
    //
    // Geometry (measured on the reference unless derived):
    //   page 601 × 842; card [96 .. 517] (right band 84); frame 1.5 pt black,
    //   heading band fill from the heading class; heading text 0.6em of the
    //   9px body = 4.05 pt at (97.5, band top + 5.8); labels x 98.93, values
    //   x 201.38 (col1 = 75% of the metrics table's inherited 33% box);
    //   first row baseline 98.4; single-line row pitch 9.75 (13px), wrapped
    //   in-cell line 8.25 (11px = the face's hhea box); a <sup> grows its
    //   value line: main run +1.16 below the label baseline, the sup at
    //   0.833× size raised 2.36 above the value baseline.

    private const double McPageWidthPt = 601.0;        // measured page box
    private const double McCardLeftPt = 96.0;          // card frame left edge
    private const double McCardRightPt = 517.0;        // card frame right edge
    private const double McFramePt = 1.5;              // collapsed 2px border
    private const double McHeadBandTopPt = 78.75;      // heading band top (stroke centre)
    private const double McHeadBandBotPt = 86.71;      // heading band bottom
    private const double McCardBottomPt = 179.71;      // body cell bottom (stroke centre)
    private const double McHeadBaseDropPt = 5.8;       // band top → heading baseline
    private const double McLabelXPt = 98.93;           // label pen
    private const double McValueXPt = 201.38;          // value pen
    private const double McFirstBasePt = 98.4;         // first row baseline
    private const double McRowPitchPt = 9.75;          // row-to-row (13px)
    private const double McCellLinePt = 8.25;          // wrapped in-cell line (11px)
    private const double McSupValueDropPt = 1.16;      // sup-bearing value baseline drop
    private const double McSupRaisePt = 2.36;          // sup baseline above the value's
    private const double McSupSizeFactor = 0.833;      // sup size vs the value size

    private static Document? TryRenderMetricsCard(string html,
        IReadOnlyDictionary<string, Dictionary<string, string>> css,
        double pageHeight)
    {
        // Gate: a px-sized 100%-width body naming a resolvable face, a
        // border-collapse card class, a width:inherit nested-table class, and
        // a heading class with a background and an em font-size.
        if (!css.TryGetValue("body", out var bodyRule)
            || !bodyRule.TryGetValue("font-family", out var bodyFam)
            || !bodyRule.TryGetValue("font-size", out var bodyFsV)
            || !Regex.IsMatch(bodyFsV, @"^\s*[\d.]+\s*px\s*$", RegexOptions.IgnoreCase)
            || !(bodyRule.TryGetValue("width", out var bodyW)
                 && bodyW.Trim() == "100%"))
            return null;
        var face = FirstFontFamily(bodyFam);
        if (face is null || WinMetricsFor(face) is not { } fm) return null;
        Dictionary<string, string>? headCls = null;
        var hasCollapse = false;
        var hasInherit = false;
        foreach (var (sel, props) in css)
        {
            if (props.TryGetValue("border-collapse", out var bc)
                && bc.Contains("collapse", StringComparison.OrdinalIgnoreCase))
                hasCollapse = true;
            if (props.TryGetValue("width", out var wv)
                && wv.Trim().Equals("inherit", StringComparison.OrdinalIgnoreCase))
                hasInherit = true;
            if (sel.StartsWith('.')
                && (props.ContainsKey("background-color") || props.ContainsKey("background"))
                && props.TryGetValue("font-size", out var hfs)
                && hfs.Contains("em", StringComparison.OrdinalIgnoreCase))
                headCls = props;
        }
        if (!hasCollapse || !hasInherit || headCls is null) return null;

        const double PxPt = 0.75;
        var bodyFs = double.Parse(Regex.Match(bodyFsV, @"[\d.]+").Value,
            System.Globalization.CultureInfo.InvariantCulture) * PxPt;   // 9px → 6.75
        var headFs = headCls.TryGetValue("font-size", out var hfv)
            && Regex.Match(hfv, @"([\d.]+)\s*em") is { Success: true } hem
            ? double.Parse(hem.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture) * bodyFs
            : 0.6 * bodyFs;
        var headBg = (headCls.TryGetValue("background-color", out var hbg)
                ? ParseCssColor(hbg) : null)
            ?? Color.FromRgb(0x4E, 0x58, 0x9E);

        // Heading text = the first heading-class cell; rows = the nested table.
        var headTdM = Regex.Match(html,
            @"<td\b[^>]*class\s*=\s*[""'][^""']*heading[^""']*[""'][^>]*>([\s\S]*?)</td>",
            RegexOptions.IgnoreCase);
        if (!headTdM.Success) return null;
        var headText = CollapseWs(DecodeEntities(
            Regex.Replace(headTdM.Groups[1].Value, "<[^>]+>", " "))).Trim();

        var innerTblM = Regex.Match(html,
            @"<table\b[^>]*class\s*=\s*[""'][^""']*metrics[^""']*[""'][^>]*>([\s\S]*?)</table>",
            RegexOptions.IgnoreCase);
        if (!innerTblM.Success) return null;
        var rows = new List<(string label, string valueMain, string valueSup)>();
        foreach (Match rm in Regex.Matches(innerTblM.Groups[1].Value,
                     @"<tr\b[^>]*>([\s\S]*?)</tr\s*>", RegexOptions.IgnoreCase))
        {
            var cells = Regex.Matches(rm.Groups[1].Value,
                @"<td\b[^>]*>([\s\S]*?)</td\s*>", RegexOptions.IgnoreCase);
            if (cells.Count < 2) continue;
            var label = CollapseWs(DecodeEntities(
                Regex.Replace(cells[0].Groups[1].Value, "<[^>]+>", " "))).Trim();
            var valRaw = cells[1].Groups[1].Value;
            var supM = Regex.Match(valRaw, @"<sup\b[^>]*>([\s\S]*?)</sup\s*>",
                RegexOptions.IgnoreCase);
            var sup = supM.Success
                ? CollapseWs(DecodeEntities(supM.Groups[1].Value)).Trim() : "";
            var main = CollapseWs(DecodeEntities(Regex.Replace(
                supM.Success ? valRaw[..supM.Index] : valRaw, "<[^>]+>", " "))).Trim();
            rows.Add((label, main, sup));
        }
        if (rows.Count == 0) return null;

        var doc = Document.Create();
        var docFontDict = new Core.PdfDictionary();
        var page = doc.Pages.Add(McPageWidthPt, pageHeight);
        EnsureFonts(page, docFontDict);
        var faceRes = face.Replace(" ", "");
        EnsureFont(page, faceRes, "F8");
        EnsureFont(page, faceRes + "Bold", "F9");

        var sb = new StringBuilder();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        void Run(string res, double fs, double x, double yTd, string text)
            => sb.AppendLine(string.Create(inv,
                $"BT /{res} {fs:0.##} Tf 1 0 0 1 {x:F2} {pageHeight - yTd:F2} Tm ({EscapePdfString(text)}) Tj ET"));

        // Heading band fill, then the collapsed frame: the band box and the body
        // cell box share their middle edge (stroke centres at the measured lines).
        var fillL = McCardLeftPt + McFramePt / 2;
        var fillR = McCardRightPt - McFramePt / 2;
        sb.AppendLine(string.Create(inv,
            $"q {headBg.R / 255.0:0.###} {headBg.G / 255.0:0.###} {headBg.B / 255.0:0.###} rg " +
            $"{fillL:F2} {pageHeight - McHeadBandBotPt:F2} {fillR - fillL:F2} {McHeadBandBotPt - McHeadBandTopPt:F2} re f Q"));
        sb.AppendLine(string.Create(inv,
            $"q 0 0 0 RG {McFramePt:0.##} w " +
            $"{fillL:F2} {pageHeight - McHeadBandBotPt:F2} {fillR - fillL:F2} {McHeadBandBotPt - McHeadBandTopPt:F2} re S " +
            $"{fillL:F2} {pageHeight - McCardBottomPt:F2} {fillR - fillL:F2} {McCardBottomPt - McHeadBandBotPt:F2} re S Q"));

        // Heading text on the band.
        if (headText.Length > 0)
            Run("F9", headFs, fillL + McFramePt / 2, McHeadBandTopPt + McHeadBaseDropPt, headText);

        // Label/value rows: labels wrap at the 75% column of the inherited-33%
        // nested box (= value pen − label pen); wrapped lines pitch at the
        // in-cell line, rows at the row pitch.
        var labelBoxW = McValueXPt - McLabelXPt;
        var yBase = McFirstBasePt;
        foreach (var (label, valueMain, valueSup) in rows)
        {
            var lines = MeasuredWordWrap(label, labelBoxW, face + " Bold", bodyFs);
            var lb = yBase;
            foreach (var line in lines)
            {
                Run("F9", bodyFs, McLabelXPt, lb, line);
                lb += McCellLinePt;
            }
            if (valueMain.Length > 0)
            {
                var vBase = valueSup.Length > 0 ? yBase + McSupValueDropPt : yBase;
                Run("F8", bodyFs, McValueXPt, vBase, valueMain);
                if (valueSup.Length > 0)
                    Run("F8", bodyFs * McSupSizeFactor,
                        McValueXPt + MeasureFaceText(face, valueMain, bodyFs),
                        vBase - McSupRaisePt, valueSup);
            }
            yBase += McRowPitchPt + (lines.Length - 1) * McCellLinePt;
        }

        page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
        _ = fm;
        return doc;
    }

    // ── The XML-DUMP VIEWER ─────────────────────────────────────────────────────
    // The "styled XML dump" export idiom: html/body margins zeroed, one root
    // class whose `font:` shorthand carries a KEYWORD size and a quoted family
    // (`font: small 'Verdana'`), a `.root *` rule turning every descendant into
    // a padded block (`display: block; padding-left: 2em`), and nested
    // per-element divs whose leaf anchors/spans carry the element markup as
    // colored runs with a negative em margin pulling them back onto the
    // container's line. The source renderer applies the whole chain: line x =
    // page margin + root padding-left + one `*` padding per open container +
    // the leaf's own (negative) margin-left, every em resolved at the root's
    // keyword size.
    //
    // Vertical model (formula-exact against the reference): line box =
    // MetricLineHeight(fs, winSum) (Verdana small: 16px = 12pt), baseline =
    // MetricBaselineDrop; the first line opens at top margin + the root's
    // inline padding-top.

    // The reference's untouched page margins (the converter's caller-facing
    // defaults; the early dialect call sites see the legacy 96/72 pair, so the
    // dialect pins its own).
    private const double XvMarginLeftPt = 90.0;
    private const double XvMarginTopPt = 72.0;
    private const double XvMarginBottomPt = 72.0;

    private static Document? TryRenderXmlViewer(string html, double pageWidth, double pageHeight)
    {
        if (Regex.IsMatch(html, @"<table\b", RegexOptions.IgnoreCase)) return null;
        var styleM = Regex.Match(html, @"<style\b[^>]*>([\s\S]*?)</style>", RegexOptions.IgnoreCase);
        if (!styleM.Success) return null;
        var sheet = styleM.Groups[1].Value;

        // Root class: `font: <keyword> '<family>'` (quoted or bare family).
        var rootM = Regex.Match(sheet,
            @"\.(\w[\w-]*)\s*\{[^}]*\bfont\s*:\s*(xx-small|x-small|small|medium|large|x-large|xx-large)\s+['""]?([\w ]+)['""]?",
            RegexOptions.IgnoreCase);
        if (!rootM.Success) return null;
        var rootCls = rootM.Groups[1].Value;
        var face = rootM.Groups[3].Value.Trim();
        if (WinMetricsFor(face) is not { } wm) return null;
        if (!TryParseCssFontSize(rootM.Groups[2].Value, out var fs) || fs <= 0) return null;

        // The `.root *` block rule with its em padding.
        var starM = Regex.Match(sheet,
            @"\." + Regex.Escape(rootCls) + @"\s+\*\s*\{[^}]*\bdisplay\s*:\s*block[^}]*\bpadding-left\s*:\s*([\d.]+)\s*em",
            RegexOptions.IgnoreCase);
        if (!starM.Success) return null;
        var starPad = double.Parse(starM.Groups[1].Value,
            System.Globalization.CultureInfo.InvariantCulture) * fs;

        // html/body margins zeroed — the fingerprint of the full-bleed dump sheet.
        if (!Regex.IsMatch(sheet, @"(?:html\s*,\s*body|body)\s*(?:[^{}]*)?\{[^}]*\bmargin\s*:\s*0",
                RegexOptions.IgnoreCase)) return null;

        // The root DIV in the document; everything renders from its subtree.
        var rootDivM = Regex.Match(html,
            @"<div\b[^>]*class\s*=\s*[""'][^""']*\b" + Regex.Escape(rootCls) + @"\b[^""']*[""'][^>]*>",
            RegexOptions.IgnoreCase);
        if (!rootDivM.Success) return null;
        var rootInner = BalancedInner(html, rootDivM.Index + rootDivM.Length, "div");
        if (rootInner is null) return null;

        // Root box: class padding-left (its own longhand beats the shorthand),
        // inline style padding-top overrides the class shorthand.
        var rootRule = Regex.Match(sheet, @"\." + Regex.Escape(rootCls) + @"\s*\{([^}]*)\}");
        var rootDecl = rootRule.Success ? rootRule.Groups[1].Value : "";
        var rootPadLeft = CssEmLen(rootDecl, "padding-left", fs)
            ?? CssEmLen(rootDecl, "padding", fs) ?? 0;
        var rootStyle = AttrValue(rootDivM.Value, "style") ?? "";
        var rootPadTop = CssEmLen(rootStyle, "padding-top", fs)
            ?? CssEmLen(rootDecl, "padding", fs) ?? 0;

        // The `.cls:before` pseudo marker (`content: '+'; color: red; left: -1em`).
        (string text, Color color, double left)? Before(string cls)
        {
            var m = Regex.Match(sheet,
                @"\.\w[\w-]*\s+\." + Regex.Escape(cls) + @":before\s*\{([^}]*)\}",
                RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            var d = m.Groups[1].Value;
            var cm = Regex.Match(d, @"content\s*:\s*'([^']*)'|content\s*:\s*""([^""]*)""");
            if (!cm.Success) return null;
            var text = cm.Groups[1].Success ? cm.Groups[1].Value : cm.Groups[2].Value;
            var col = Regex.Match(d, @"(?<![-\w])color\s*:\s*([^;]+)") is { Success: true } colM
                ? ParseCssColor(colM.Groups[1].Value.Trim()) ?? Color.FromRgb(0, 0, 0)
                : Color.FromRgb(0, 0, 0);
            var left = CssEmLen(d, "left", fs) ?? 0;
            return (text.Trim(), col, left);
        }

        var doc = Document.Create();
        var docFontDict = new Core.PdfDictionary();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        EnsureFonts(page, docFontDict);
        var faceRes = face.Replace(" ", "");
        EnsureFont(page, faceRes, "F8");
        EnsureFont(page, faceRes + "Bold", "F9");

        var lineBox = MetricLineHeight(fs, wm.sum);
        var drop = MetricBaselineDrop(fs, lineBox, wm);
        var y = XvMarginTopPt + rootPadTop;
        var sb = new StringBuilder();
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        void Run(double x, double yTop, string text, Color c, bool bold)
        {
            if (text.Length == 0) return;
            sb.AppendLine(string.Create(inv,
                $"q {c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} rg " +
                $"BT /{(bold ? "F9" : "F8")} {fs:0.##} Tf 1 0 0 1 {x:F2} {pageHeight - (yTop + drop):F2} Tm " +
                $"({EscapePdfString(text)}) Tj ET Q"));
        }

        // One rendered line: the leaf's flattened runs at the container indent
        // plus its own margin-left, the optional :before marker hanging left.
        void EmitLine(string leafTag, string leafAttrs, string leafInner, double indent)
        {
            var style = AttrValue("<x " + leafAttrs + ">", "style") ?? "";
            var ownMargin = ParseInlineMarginBox(style, fs).left;
            var lineX = indent + ownMargin;
            if (y + lineBox > pageHeight - XvMarginBottomPt)
            {
                page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
                sb.Clear();
                page = doc.Pages.Add(pageWidth, pageHeight);
                EnsureFonts(page, docFontDict);
                EnsureFont(page, faceRes, "F8");
                EnsureFont(page, faceRes + "Bold", "F9");
                y = XvMarginTopPt;
            }
            var cls = AttrValue("<x " + leafAttrs + ">", "class") ?? "";
            foreach (var c in cls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (Before(c) is { } marker)
                    Run(lineX + marker.left, y, marker.text, marker.color, false);

            // Flatten to runs: text nodes carry the innermost span's inline
            // color/weight; the leaf's own inline color is the default.
            var defColor = Regex.Match(style, @"(?<![-\w])color\s*:\s*([^;]+)") is { Success: true } dc
                ? ParseCssColor(dc.Groups[1].Value.Trim()) ?? Color.FromRgb(0, 0, 0)
                : Color.FromRgb(0, 0, 0);
            var runs = new List<(string text, Color color, bool bold)>();
            var stack = new Stack<(Color color, bool bold)>();
            stack.Push((defColor, false));
            var pos = 0;
            foreach (Match t in Regex.Matches(leafInner, @"<(/?)(\w+)([^>]*?)(/?)>"))
            {
                var txt = leafInner[pos..t.Index];
                if (txt.Length > 0)
                    runs.Add((DecodeEntities(txt), stack.Peek().color, stack.Peek().bold));
                pos = t.Index + t.Length;
                if (t.Groups[4].Value == "/") continue;             // self-closing
                if (t.Groups[1].Value == "/") { if (stack.Count > 1) stack.Pop(); continue; }
                var st = AttrValue(t.Value, "style") ?? "";
                var col = Regex.Match(st, @"(?<![-\w])color\s*:\s*([^;]+)") is { Success: true } cm2
                    ? ParseCssColor(cm2.Groups[1].Value.Trim()) ?? stack.Peek().color
                    : stack.Peek().color;
                var bold = Regex.IsMatch(st, @"font-weight\s*:\s*bold", RegexOptions.IgnoreCase)
                    || (stack.Peek().bold
                        && !Regex.IsMatch(st, @"font-weight\s*:\s*normal", RegexOptions.IgnoreCase));
                stack.Push((col, bold));
            }
            var tail = leafInner[pos..];
            if (tail.Length > 0)
                runs.Add((DecodeEntities(tail), stack.Peek().color, stack.Peek().bold));

            // Whitespace: collapse interior runs, trim the line's two ends.
            var x = lineX;
            for (var i = 0; i < runs.Count; i++)
            {
                var text = Regex.Replace(runs[i].text, @"[ \t\r\n\f]+", " ");
                if (i == 0) text = text.TrimStart();
                if (i == runs.Count - 1) text = text.TrimEnd();
                if (text.Length == 0) continue;
                Run(x, y, text, runs[i].color, runs[i].bold);
                x += MeasureFaceText(runs[i].bold ? face + " Bold" : face, text, fs);
            }
            y += lineBox;
            _ = leafTag;
        }

        // Recursive container walk: a div deepens the indent by the `*` padding;
        // a leaf anchor/span renders one line.
        void Walk(string inner, double indent)
        {
            var pos = 0;
            while (true)
            {
                var t = Regex.Match(inner[pos..], @"<(/?)(\w+)([^>]*?)(/?)>|<!--[\s\S]*?-->");
                if (!t.Success) return;
                var abs = pos + t.Index;
                if (t.Value.StartsWith("<!--", StringComparison.Ordinal))
                {
                    pos = abs + t.Length;
                    continue;
                }
                var closing = t.Groups[1].Value == "/";
                var tag = t.Groups[2].Value.ToLowerInvariant();
                var self = t.Groups[4].Value == "/";
                pos = abs + t.Length;
                if (closing || self) continue;
                if (tag == "div")
                {
                    var innerDiv = BalancedInner(inner, pos, "div");
                    if (innerDiv is null) return;
                    Walk(innerDiv, indent + starPad);
                    pos += innerDiv.Length + tag.Length + 3;
                }
                else if (tag is "a" or "span")
                {
                    var innerLeaf = BalancedInner(inner, pos, tag);
                    if (innerLeaf is null) return;
                    EmitLine(tag, t.Groups[3].Value, innerLeaf, indent);
                    pos += innerLeaf.Length + tag.Length + 3;
                }
                else if (tag is "style" or "script")
                {
                    var end = inner.IndexOf("</" + tag, pos, StringComparison.OrdinalIgnoreCase);
                    if (end < 0) return;
                    pos = inner.IndexOf('>', end) + 1;
                }
            }
        }

        Walk(rootInner, XvMarginLeftPt + rootPadLeft);
        page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
        return doc;
    }

    // ── The PRINT-MEDIA JOB AD ──────────────────────────────────────────────────
    // A job-ad export rendered on its @media print rules: the container class
    // (max-width + padding + px font + unitless line-height) sizes the sheet at
    // zero margins (page = UA body margin + padding + max-width + padding), the
    // print block hides the apply/benefits/salary chrome, h6 section headers
    // set uppercase over a bottom hairline, and list items draw the
    // `li:before` content marker with its margin-right. All vertical geometry
    // is the win-metric line model: line = px-round(fs·factor), baseline =
    // half-leading + winAscent; block gaps are the print rules' margins,
    // parent/child bottoms MAX-collapsing.

    private static Document? TryRenderPrintAd(string html,
        IReadOnlyDictionary<string, Dictionary<string, string>> css,
        double pageHeight, HtmlLoadOptions? options)
    {
        // Gate: an explicit zero-margin conversion of a document carrying an
        // @media print block and a container class with max-width + padding +
        // a px font-size + a unitless line-height.
        if (!Regex.IsMatch(html, @"@media\s+print", RegexOptions.IgnoreCase)) return null;
        string? contCls = null;
        Dictionary<string, string>? contRule = null;
        foreach (var (sel, props) in css)
            if (sel.StartsWith('.') && !sel.Contains(' ')
                && props.ContainsKey("max-width") && props.ContainsKey("padding")
                && props.TryGetValue("font-size", out var cfs)
                && cfs.Contains("px", StringComparison.OrdinalIgnoreCase)
                && props.ContainsKey("line-height"))
            { contCls = sel[1..]; contRule = props; break; }
        if (contCls is null || contRule is null) return null;
        var bodyM = Regex.Match(html, @"<body\b[^>]*style\s*=\s*[""']([^""']*)[""']",
            RegexOptions.IgnoreCase);
        var face = bodyM.Success
            && Regex.Match(bodyM.Groups[1].Value, @"font-family\s*:\s*([^;]+)",
                RegexOptions.IgnoreCase) is { Success: true } bfm
            ? FirstFontFamily(bfm.Groups[1].Value) : null;
        if (face is null || WinMetricsFor(face) is not { } wm) return null;
        if (!TryParseLength(contRule["max-width"].Trim(), out var maxW) || maxW <= 0) return null;
        if (!TryParseLength(contRule["padding"].Trim(), out var pad) || pad < 0) return null;
        var baseFs = TryParseLength(contRule["font-size"].Trim(), out var bfs) ? bfs : 11.25;
        var lineFactor = double.TryParse(contRule["line-height"].Trim(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var lf) && lf > 0 ? lf : 1.25;

        var contM = Regex.Match(html,
            @"<div\b[^>]*class\s*=\s*[""'][^""']*\b" + Regex.Escape(contCls) + @"\b[^""']*[""'][^>]*>",
            RegexOptions.IgnoreCase);
        if (!contM.Success) return null;
        var inner = BalancedInner(html, contM.Index + contM.Length, "div");
        if (inner is null) return null;

        var pageWidth = UaBodyMarginPt + maxW + 2 * pad;
        var xText = UaBodyMarginPt + pad;
        var contentW = maxW;

        // The `li:before` marker (content + margin-right), read off the raw css.
        var beforeM = Regex.Match(html,
            @"li:before\s*\{[^}]*content\s*:\s*[""']([^""']*)[""'][^}]*margin-right\s*:\s*([\d.]+)\s*px",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var liMarker = beforeM.Success ? beforeM.Groups[1].Value : "";
        var liMarkerGap = beforeM.Success
            ? double.Parse(beforeM.Groups[2].Value,
                System.Globalization.CultureInfo.InvariantCulture) * 0.75 : 0;

        double FsOf(string sel, double dflt)
            => css.TryGetValue(sel, out var r) && r.TryGetValue("font-size", out var v)
               && TryParseLength(v.Trim(), out var pt) && pt > 0 ? pt : dflt;
        double MarginBottomOf(string sel, double dflt)
        {
            if (!css.TryGetValue(sel, out var r)) return dflt;
            if (r.TryGetValue("margin-bottom", out var mbv)
                && TryParseLength(mbv.Trim().Replace("!important", "").Trim(), out var mbPt))
                return mbPt;
            if (r.TryGetValue("margin", out var mv)
                && TryParseCssMarginBox(Regex.Replace(mv, @"!important", "").Trim(), out var mBox))
                return mBox.bottom;
            return dflt;
        }
        bool HiddenCls(string cls)
            => css.TryGetValue("." + contCls + " ." + cls, out var hr)
               && hr.TryGetValue("display", out var hd)
               && hd.Contains("none", StringComparison.OrdinalIgnoreCase);

        var h6Fs = FsOf("." + contCls + " h6", 10.5);
        var h1Fs = FsOf("." + contCls + " h1", 17.25);
        var h1SpanFs = FsOf("." + contCls + " h1 span", 24);
        var descFs = FsOf("." + contCls + " .companyDescription", baseFs);
        var pMb = MarginBottomOf("." + contCls + " p", 15);
        var h1Mb = MarginBottomOf("." + contCls + " h1", 11.25);
        var h6Rule = css.TryGetValue("." + contCls + " h6", out var h6R) ? h6R : null;
        var h6PadBottom = h6Rule is not null && h6Rule.TryGetValue("padding-bottom", out var h6pb)
            && TryParseLength(h6pb.Trim(), out var h6pbPt) ? h6pbPt : 3.75;
        var h6Mb = MarginBottomOf("." + contCls + " h6", 7.5);
        var h6Border = h6Rule is not null && h6Rule.TryGetValue("border-bottom", out var h6bb)
            ? ParseCssColor(h6bb) : null;
        var blockMb = MarginBottomOf("." + contCls + " .jobBlock", 22.5);

        var doc = Document.Create();
        var docFontDict = new Core.PdfDictionary();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        EnsureFonts(page, docFontDict);
        var sb = new StringBuilder();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        double Drop(double fs, double box) => (box - fs * wm.sum) / 2 + fs * wm.asc;

        var y = UaBodyMarginPt + pad;      // container top = UA body margin; first line at padding
        var pendingGap = 0.0;              // MAX-collapsing bottom margins
        void Gap(double g) => pendingGap = Math.Max(pendingGap, g);
        void Spend() { y += pendingGap; pendingGap = 0; }

        void EmitLine(string text, double fs, bool bold, double box, double x)
        {
            var (rn, hex) = Text.Type0FontEmbedder.Embed(
                (page.Dict.Get("Resources") as Core.PdfDictionary)!.Get("Font") as Core.PdfDictionary
                    ?? throw new InvalidOperationException(),
                PosFace(face + (bold ? " Bold" : "")).ttf ?? PosFace(face).ttf!,
                face.Replace(" ", "") + (bold ? "Bold" : ""), text, stripSpacesInBaseFont: true);
            sb.AppendLine(string.Create(inv,
                $"BT /{rn} {fs:0.##} Tf 1 0 0 1 {x:F2} {pageHeight - (y + Drop(fs, box)):F2} Tm <{System.Convert.ToHexString(hex)}> Tj ET"));
        }

        void EmitWrapped(string text, double fs, bool bold, double box, double x, double w)
        {
            foreach (var ln in MeasuredWordWrap(text, w, face + (bold ? " Bold" : ""), fs))
            {
                if (ln.Length > 0) EmitLine(ln, fs, bold, box, x);
                y += box;
            }
        }

        // ── the walk: h1 / h6 / p / ul / bare text / img-alt, hidden classes skipped
        var tagRx = new Regex(@"<(/?)(\w+)([^>]*?)(/?)>|<!--[\s\S]*?-->", RegexOptions.Singleline);
        string PlainText(string s2) => CollapseWs(DecodeEntities(
            Regex.Replace(s2, @"<[^>]+>", ""))).Trim();

        void WalkAd(string frag)
        {
            var p2 = 0;
            while (true)
            {
                var t = tagRx.Match(frag[p2..]);
                // trailing bare text
                var lead = t.Success ? frag[p2..(p2 + t.Index)] : frag[p2..];
                if (lead.Trim().Length > 0)
                {
                    Spend();
                    var box0 = baseFs * lineFactor;
                    EmitWrapped(PlainText(lead), baseFs, false, box0, xText, contentW);
                }
                if (!t.Success) return;
                var abs = p2 + t.Index;
                p2 = abs + t.Length;
                if (t.Value.StartsWith("<!--", StringComparison.Ordinal)) continue;
                if (t.Groups[1].Value == "/" || t.Groups[4].Value == "/") continue;
                var tag = t.Groups[2].Value.ToLowerInvariant();
                var cls = AttrValue("<x " + t.Groups[3].Value + ">", "class") ?? "";
                var hidden = false;
                foreach (var c in cls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    if (HiddenCls(c)) { hidden = true; break; }
                string InnerOf(string tg)
                {
                    var innerX = BalancedInner(frag, p2, tg);
                    if (innerX is null) { innerX = ""; }
                    else p2 += innerX.Length + tg.Length + 3;
                    return innerX;
                }
                switch (tag)
                {
                    case "style":
                    case "script":
                        InnerOf(tag);
                        break;
                    case "img":
                    {
                        // A broken image leaves its alt text as one base-size line.
                        var src = AttrValue(t.Value, "src") ?? "";
                        var alt = AttrValue(t.Value, "alt") ?? "";
                        if (LoadConverterImage(src, options) is null && alt.Length > 0)
                        {
                            Spend();
                            var box0 = baseFs * lineFactor;
                            EmitLine(alt, baseFs, false, box0, xText);
                            y += box0;
                        }
                        break;
                    }
                    case "h1":
                    {
                        var innerH = InnerOf("h1");
                        if (hidden) break;
                        Spend();
                        // span-sized first line(s), the remainder at the h1 size;
                        // a <br> splits the lines.
                        foreach (var seg in Regex.Split(innerH, @"<br\b[^>]*/?>",
                                     RegexOptions.IgnoreCase))
                        {
                            var segText = PlainText(seg);
                            if (segText.Length == 0) continue;
                            var isSpan = Regex.IsMatch(seg, @"<span\b", RegexOptions.IgnoreCase);
                            var fs2 = isSpan ? h1SpanFs : h1Fs;
                            var box2 = fs2 * lineFactor;
                            EmitLine(segText, fs2, false, box2, xText);
                            y += box2;
                        }
                        Gap(h1Mb);
                        break;
                    }
                    case "h6":
                    {
                        var innerH = InnerOf("h6");
                        if (hidden) break;
                        Spend();
                        var box2 = h6Fs * 1.28571;
                        EmitLine(PlainText(innerH).ToUpperInvariant(), h6Fs, true, box2, xText);
                        y += box2 + h6PadBottom;
                        if (h6Border is { } hbCol)
                        {
                            sb.AppendLine(string.Create(inv,
                                $"q {hbCol.R / 255.0:0.###} {hbCol.G / 255.0:0.###} {hbCol.B / 255.0:0.###} RG 0.75 w " +
                                $"{xText:F2} {pageHeight - (y + 0.375):F2} m {xText + contentW:F2} {pageHeight - (y + 0.375):F2} l S Q"));
                            y += 0.75;
                        }
                        Gap(h6Mb);
                        break;
                    }
                    case "p":
                    {
                        var innerP = InnerOf("p");
                        if (hidden) break;
                        var fs2 = cls.Contains("companyDescription") ? descFs : baseFs;
                        var factor = cls.Contains("companyDescription") ? 4.0 / 3.0 : lineFactor;
                        var box2 = fs2 * factor;
                        var any = false;
                        foreach (var seg in Regex.Split(innerP, @"<br\b[^>]*/?>",
                                     RegexOptions.IgnoreCase))
                        {
                            var segText = PlainText(seg);
                            if (segText.Length == 0) continue;
                            if (!any) Spend();
                            any = true;
                            EmitWrapped(segText, fs2, false, box2, xText, contentW);
                        }
                        if (any)
                            Gap(cls.Contains("companyDescription")
                                ? MarginBottomOf("." + contCls + " .companyDescription", pMb) : pMb);
                        break;
                    }
                    case "ul":
                    {
                        var innerU = InnerOf("ul");
                        if (hidden) break;
                        Spend();
                        var box2 = baseFs * lineFactor;
                        foreach (Match li in Regex.Matches(innerU, @"<li\b[^>]*>([\s\S]*?)</li\s*>",
                                     RegexOptions.IgnoreCase))
                        {
                            var liText = PlainText(li.Groups[1].Value);
                            if (liText.Length == 0) continue;
                            var markerAdv = liMarker.Length > 0
                                ? MeasureFaceText(face, liMarker, baseFs) + liMarkerGap : 0;
                            if (liMarker.Length > 0) EmitLine(liMarker, baseFs, false, box2, xText);
                            var first = true;
                            foreach (var ln in MeasuredWordWrap(liText, contentW - markerAdv,
                                         face, baseFs))
                            {
                                if (ln.Length > 0)
                                    EmitLine(ln, baseFs, false, box2, first ? xText + markerAdv : xText);
                                y += box2;
                                first = false;
                            }
                        }
                        Gap(MarginBottomOf("." + contCls + " ul", 15));
                        break;
                    }
                    case "div":
                    {
                        var innerD = InnerOf("div");
                        if (hidden || cls.Contains("applyButton")) break;
                        WalkAd(innerD);
                        if (cls.Contains("jobBlock"))
                            Gap(blockMb);
                        break;
                    }
                    case "a":
                    {
                        var innerA = InnerOf("a");
                        // apply buttons are hidden in print
                        if (!hidden && !cls.Contains("applyButton"))
                        {
                            var aText = PlainText(innerA);
                            if (aText.Length > 0)
                            {
                                Spend();
                                var box2 = baseFs * lineFactor;
                                EmitWrapped(aText, baseFs, false, box2, xText, contentW);
                            }
                        }
                        break;
                    }
                    case "h2":
                    case "h3":
                    case "h4":
                    case "h5":
                    case "span":
                    case "strong":
                    case "b":
                    {
                        // flatten inline-ish leftovers as base text
                        var innerO = InnerOf(tag);
                        var oText = PlainText(innerO);
                        if (!hidden && oText.Length > 0)
                        {
                            Spend();
                            var box2 = Math.Round(baseFs / 0.75 * lineFactor,
                                MidpointRounding.AwayFromZero) * 0.75;
                            EmitWrapped(oText, baseFs, false, box2, xText, contentW);
                        }
                        break;
                    }
                }
            }
        }

        WalkAd(inner);
        page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
        return doc;
    }

    /// <summary>Inner markup of the balanced element whose OPEN tag ends at
    /// <paramref name="afterOpen"/> — null when unbalanced.</summary>
    // ── Contract-invoice sheets on remote faces ─────────────────────────────
    // The Lato invoice: a Google-Fonts stylesheet resolves the sheet's faces
    // (the source engine FETCHES the css and its TTF programs, exactly as it
    // fetches remote images), the 600 px min-width body sets the 450 pt
    // column (page = 90 + UA 6 + 450 + 90 = 636), the float header pairs the
    // #5d80ba info panel with the fetched logo, and each `.contract` block —
    // h2 label line, right-floated description, orange collapsed table —
    // moves WHOLE to the next page under its page-break-inside: avoid.

    private const double CiPageWPt = 636.0;
    private const double CiContentXPt = 96.0;         // 90 margin + UA 6 body
    private const double CiContentWPt = 450.0;        // 600 px min-width
    private const double CiHeaderHPt = 113.2;         // header { height:150px }
    private const double CiH2AfterDividerPt = 45.2;   // divider → h2 baseline
    private const double CiFreshTopBasePt = 114.9;    // fresh page h2 baseline
    private const double CiTableAfterH2Pt = 23.5;     // h2 base → thead band top
    private const double CiTheadHPt = 51.3;           // two 15 pt lines + pads
    private const double CiRowHPt = 31.5;             // 13 pt line + 10 px pads
    private const double CiRowBasePt = 21.2;          // row top → baseline
    private const double CiDividerGapPt = 16.6;       // table bottom → divider

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,
        Dictionary<(int weight, bool italic), string>> RemoteFaceCache = new(StringComparer.Ordinal);

    /// <summary>Fetch the @font-face programs a stylesheet names over http(s),
    /// register them as font sources, and map (weight, italic) to the face
    /// names the repository now resolves. Cached per css text hash.</summary>
    private static Dictionary<(int weight, bool italic), string> LoadRemoteFaces(string css)
    {
        var key = css.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ":" + css.GetHashCode().ToString("x", System.Globalization.CultureInfo.InvariantCulture);
        return RemoteFaceCache.GetOrAdd(key, _ =>
        {
            var map = new Dictionary<(int, bool), string>();
            foreach (Match m in Regex.Matches(css, @"@font-face\s*\{(?<b>[^{}]*)\}",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var body = m.Groups["b"].Value;
                var wM = Regex.Match(body, @"font-weight\s*:\s*(\d+)", RegexOptions.IgnoreCase);
                var weight = wM.Success
                    ? int.Parse(wM.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
                    : 400;
                var italic = Regex.IsMatch(body, @"font-style\s*:\s*(italic|oblique)",
                    RegexOptions.IgnoreCase);
                var uM = Regex.Match(body, @"url\(\s*[""']?(https?://[^""')]+)[""']?\s*\)",
                    RegexOptions.IgnoreCase);
                if (!uM.Success || map.ContainsKey((weight, italic))) continue;
                var bytes = FetchRemoteImage(uM.Groups[1].Value.Trim());
                if (bytes is null || bytes.Length < 12) continue;
                string? fam = null, sub = null;
                try
                {
                    var tp = new Text.TrueTypeParser(bytes);
                    tp.Parse();
                    fam = tp.FamilyName; sub = tp.SubfamilyName;
                }
                catch { continue; }
                if (string.IsNullOrWhiteSpace(fam) || fam == "Unknown") continue;
                Text.FontRepository.Sources.Add(new Text.MemoryFontSource(bytes));
                // resolve back through the repository by whichever name the
                // program's own name table answers to
                foreach (var cand in new[]
                    { fam + " " + (sub ?? ""), fam, fam + (sub ?? "") })
                {
                    var candT = cand.Trim();
                    if (candT.Length == 0 || PosFace(candT).ttf is null) continue;
                    map[(weight, italic)] = candT;
                    break;
                }
            }
            return map;
        });
    }

    private static Document? TryRenderContractInvoice(string html, double pageHeight)
    {
        if (!html.Contains("class='contract'", StringComparison.OrdinalIgnoreCase)
            && !html.Contains("class=\"contract\"", StringComparison.OrdinalIgnoreCase))
            return null;
        if (!Regex.IsMatch(html, @"fonts\.googleapis\.com/css\?family=Lato",
                RegexOptions.IgnoreCase)
            && !Regex.IsMatch(html, @"@font-face[^}]*Lato", RegexOptions.IgnoreCase))
            return null;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        // the linked stylesheet is inlined by the preprocessor when reachable;
        // fetch it directly when the raw link is still in place
        var cssText = html;
        if (!Regex.IsMatch(html, @"@font-face", RegexOptions.IgnoreCase))
        {
            var linkM = Regex.Match(html,
                @"<link[^>]*href\s*=\s*[""'](https?://fonts\.googleapis\.com[^""']+)[""']",
                RegexOptions.IgnoreCase);
            if (!linkM.Success) return null;
            var fetched = FetchRemoteImage(DecodeEntities(linkM.Groups[1].Value));
            if (fetched is null) return null;
            cssText = Encoding.UTF8.GetString(fetched);
        }
        var faces = LoadRemoteFaces(cssText);
        if (!faces.TryGetValue((300, false), out var lightFace)
            || !faces.TryGetValue((400, false), out var regFace))
            return null;
        if (WinMetricsFor(lightFace) is not { } wm) return null;

        var ink = ParseCssColor("#627881") ?? Color.FromRgb(98, 120, 129);
        var blue = ParseCssColor("#5d80ba") ?? Color.FromRgb(93, 128, 186);
        var orange = ParseCssColor("#f69a1b") ?? Color.FromRgb(246, 154, 27);
        var white = Color.FromRgb(255, 255, 255);

        var doc = Document.Create();
        var docFontDict = new Core.PdfDictionary();
        var page = doc.Pages.Add(CiPageWPt, pageHeight);
        EnsureFonts(page, docFontDict);
        var sb = new StringBuilder();
        var streams = new List<(Page pg, StringBuilder ops)> { (page, sb) };

        double Drop(double fs, double box) => (box - fs * wm.sum) / 2 + fs * wm.asc;
        double W(string t, string face, double fs) => MeasureFaceText(face, t, fs);
        void EmitRun(string text, double fs, string face, double x, double baseline, Color col)
        {
            if (text.Length == 0) return;
            var (rn, hex) = Text.Type0FontEmbedder.Embed(
                (page.Dict.Get("Resources") as Core.PdfDictionary)!.Get("Font") as Core.PdfDictionary
                    ?? throw new InvalidOperationException(),
                PosFace(face).ttf ?? PosFace(lightFace).ttf!,
                face.Replace(" ", "").Replace("-", ""), text, stripSpacesInBaseFont: true);
            sb.AppendLine(string.Create(inv,
                $"q {col.R / 255.0:0.###} {col.G / 255.0:0.###} {col.B / 255.0:0.###} rg " +
                $"BT /{rn} {fs:0.##} Tf 1 0 0 1 {x:F2} {pageHeight - baseline:F2} Tm " +
                $"<{System.Convert.ToHexString(hex)}> Tj ET Q"));
        }
        void FillRect(double x, double yTop, double w, double h, Color col)
            => sb.AppendLine(string.Create(inv,
                $"q {col.R / 255.0:0.###} {col.G / 255.0:0.###} {col.B / 255.0:0.###} rg " +
                $"{x:F2} {pageHeight - yTop - h:F2} {w:F2} {h:F2} re f Q"));
        void Line(double x0, double y0, double x1, double y1, Color col, double lw, bool dotted)
        {
            var dash = dotted ? "[2.25 2.25] 0 d " : "";
            sb.AppendLine(string.Create(inv,
                $"q {col.R / 255.0:0.###} {col.G / 255.0:0.###} {col.B / 255.0:0.###} RG {lw:0.##} w " +
                $"{dash}{x0:F2} {pageHeight - y0:F2} m {x1:F2} {pageHeight - y1:F2} l S Q"));
        }

        // ── the header: blue info panel + fetched logo ──
        var yTop = 72.0 + 6.0;
        {
            var h1s = new List<string>();
            var infoM = Regex.Match(html, @"class='info'[^>]*>(.*?)</div>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (infoM.Success)
                foreach (Match hM in Regex.Matches(infoM.Groups[1].Value, @"<h1[^>]*>(.*?)</h1>",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline))
                    h1s.Add(CollapseWs(DecodeEntities(
                        Regex.Replace(hM.Groups[1].Value, "<[^>]+>", " "))).Trim());
            double boxW = 0;
            foreach (var t in h1s) boxW = Math.Max(boxW, W(t, lightFace, 16));
            boxW += 45.0;                             // 30 px side pads
            var boxH = 30.0 + h1s.Count * 19.5;       // 20 px pads + 16 pt lines
            FillRect(CiContentXPt, yTop, boxW, boxH, blue);
            var yH1 = yTop + 15.0;
            foreach (var t in h1s)
            {
                EmitRun(t, 16, lightFace, CiContentXPt + 22.5, yH1 + Drop(16, 19.5), white);
                yH1 += 19.5;
            }
            var logoM = Regex.Match(html, @"class='logo'[^>]*>\s*<img[^>]*src='(https?://[^']+)'",
                RegexOptions.IgnoreCase);
            if (logoM.Success && FetchRemoteImage(logoM.Groups[1].Value) is { } logoBytes
                && logoBytes.Length > 24)
            {
                double natW = 0, natH = 0;
                if (logoBytes[1] == 'P' && logoBytes.Length > 24)
                {
                    natW = (logoBytes[16] << 24) | (logoBytes[17] << 16) | (logoBytes[18] << 8) | logoBytes[19];
                    natH = (logoBytes[20] << 24) | (logoBytes[21] << 16) | (logoBytes[22] << 8) | logoBytes[23];
                }
                if (natW > 0 && natH > 0)
                {
                    var iw = natW * 0.75; var ih = natH * 0.75;
                    page.AddImage(logoBytes, new Rectangle(
                        CiContentXPt + CiContentWPt - iw, pageHeight - yTop - ih,
                        CiContentXPt + CiContentWPt, pageHeight - yTop));
                }
            }
        }
        var y = yTop + CiHeaderHPt;                   // the first divider's seat

        // ── the contract blocks ──
        var colShare = new[] { 141.5 / 405.0, 177.3 / 405.0, 85.8 / 405.0 };
        var tX = CiContentXPt + 22.5;
        var tW = CiContentWPt - 45.0;
        var colX = new double[4];
        colX[0] = tX;
        for (var i = 0; i < 3; i++) colX[i + 1] = colX[i] + colShare[i] * tW;

        void NewPage()
        {
            page = doc.Pages.Add(CiPageWPt, pageHeight);
            EnsureFonts(page, docFontDict);
            sb = new StringBuilder();
            streams.Add((page, sb));
        }

        var first = true;
        foreach (Match cM in Regex.Matches(html, @"<div class='(contract|total)'[^>]*>",
            RegexOptions.IgnoreCase))
        {
            var inner = BalancedInner(html, cM.Index + cM.Length, "div") ?? "";
            if (cM.Groups[1].Value.Equals("total", StringComparison.OrdinalIgnoreCase))
            {
                // the float-right total strip: 18 pt blue, borderless
                var tds = Regex.Matches(inner, @"<td[^>]*>(.*?)</td>",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (tds.Count >= 3)
                {
                    // measured: the strip draws at 15 pt, its amount cell
                    // right edge 45 in from the content right, the label a
                    // 30 pt cell-pad gap before it
                    var lbl = CollapseWs(DecodeEntities(tds[1].Groups[1].Value)).Trim();
                    var amt = CollapseWs(DecodeEntities(tds[2].Groups[1].Value)).Trim();
                    y += 15.7;
                    var xAmt = CiContentXPt + CiContentWPt - 45.0 - W(amt, regFace, 15);
                    EmitRun(amt, 15, regFace, xAmt, y + Drop(15, 18), blue);
                    EmitRun(lbl, 15, regFace, xAmt - 30.0 - W(lbl, regFace, 15),
                        y + Drop(15, 18), blue);
                }
                continue;
            }

            // measure the block: h2 (2 lines when the description floats) +
            // thead + body rows (the total row runs 2 lines in its column)
            var rows = new List<List<string>>();
            foreach (Match trM in Regex.Matches(inner, @"<tr[^>]*>(.*?)</tr>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var cells = new List<string>();
                foreach (Match tdM in Regex.Matches(trM.Groups[1].Value, @"<td[^>]*>(.*?)</td>",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline))
                    cells.Add(CollapseWs(DecodeEntities(tdM.Groups[1].Value)).Trim());
                if (cells.Count > 0) rows.Add(cells);
            }
            var ths = new List<string>();
            foreach (Match thM in Regex.Matches(inner, @"<th(?![a-z])[^>]*>(.*?)</th>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
                ths.Add(CollapseWs(DecodeEntities(thM.Groups[1].Value)).Trim());

            var nPlain = Math.Max(0, rows.Count - 1);
            var lastRowH = CiRowHPt + 18.0;           // the wrapped total row
            var blockH = CiH2AfterDividerPt + 38.7 + CiTheadHPt
                + nPlain * CiRowHPt + lastRowH + CiDividerGapPt;
            double h2Base;
            if (first) { h2Base = y + CiH2AfterDividerPt; first = false; }
            else
            {
                Line(CiContentXPt, y, CiContentXPt + CiContentWPt, y, blue, 1.5, true);
                if (y + blockH > pageHeight - 90.0)
                {
                    NewPage();
                    h2Base = CiFreshTopBasePt;
                }
                else h2Base = y + CiH2AfterDividerPt;
            }

            // h2: 'Contract #: ' label + value, description right-aligned on
            // its own line
            var spans = Regex.Matches(inner, @"<span(?<a>[^>]*)>(?<b>.*?)</span>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            string lbl1 = "", val1 = "", lbl2 = "", val2 = "";
            foreach (Match sM in spans)
            {
                var isDesc = sM.Groups["a"].Value.Contains("desc", StringComparison.OrdinalIgnoreCase);
                var lM = Regex.Match(sM.Groups["b"].Value, @"<label[^>]*>(.*?)</label>(.*)$",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (!lM.Success) continue;
                var l = DecodeEntities(lM.Groups[1].Value);
                var v = CollapseWs(DecodeEntities(Regex.Replace(lM.Groups[2].Value, "<[^>]+>", " "))).Trim();
                if (isDesc) { lbl2 = l; val2 = v; } else { lbl1 = l; val1 = v; }
            }
            EmitRun(lbl1, 16, regFace, tX, h2Base, ink);
            EmitRun(val1, 16, lightFace, tX + W(lbl1, regFace, 16), h2Base, ink);
            // a SHORT description floats beside the contract line; a long one
            // wraps under it, and the table then clears the taller float
            var line1W = W(lbl1, regFace, 16) + W(val1, lightFace, 16);
            var descW = W(lbl2, regFace, 16) + W(val2, lightFace, 16);
            var descSameLine = line1W + descW <= tW;
            if (lbl2.Length > 0 || val2.Length > 0)
            {
                var dx = CiContentXPt + CiContentWPt - 22.5 - descW;
                var dy2 = descSameLine ? h2Base : h2Base + 19.5;
                EmitRun(lbl2, 16, regFace, dx, dy2, ink);
                EmitRun(val2, 16, lightFace, dx + W(lbl2, regFace, 16), dy2, ink);
            }

            // thead band + centred 15 pt headers (the first wraps)
            // a same-line float leaves the h2 margin gap; a wrapped float
            // hugs the table (both measured)
            var thTop = h2Base + (descSameLine ? 16.0 : CiTableAfterH2Pt);
            FillRect(tX, thTop, tW, CiTheadHPt, orange);
            for (var i = 0; i < ths.Count && i < 3; i++)
            {
                var cw = colX[i + 1] - colX[i];
                var words = MeasuredWordWrap(ths[i], cw - 30.0, regFace, 15);
                var bandMid = thTop + CiTheadHPt / 2;
                var yLn = bandMid - words.Length * 9.0;
                foreach (var ln in words)
                {
                    EmitRun(ln, 15, regFace, colX[i] + (cw - W(ln, regFace, 15)) / 2,
                        yLn + Drop(15, 18), white);
                    yLn += 18.0;
                }
            }
            // body rows: bordered #f69a1b cells, centred values; the LAST row
            // is borderless — its middle cell right-aligns and wraps
            var rTop = thTop + CiTheadHPt;
            for (var ri = 0; ri < rows.Count; ri++)
            {
                var isLast = ri == rows.Count - 1;
                var cells = rows[ri];
                if (!isLast)
                {
                    // wrap each cell; the row grows to the tallest and the
                    // single-line cells centre in the taller band
                    var wrapped = new string[Math.Min(cells.Count, 3)][];
                    var maxLines = 1;
                    for (var ci = 0; ci < wrapped.Length; ci++)
                    {
                        wrapped[ci] = MeasuredWordWrap(cells[ci],
                            colX[ci + 1] - colX[ci] - 30.0, lightFace, 13);
                        maxLines = Math.Max(maxLines, wrapped[ci].Length);
                    }
                    var rowH = CiRowHPt + (maxLines - 1) * 15.8;
                    for (var ci = 0; ci < wrapped.Length; ci++)
                    {
                        var cw = colX[ci + 1] - colX[ci];
                        var yC = rTop + CiRowBasePt
                            + (maxLines - wrapped[ci].Length) * 15.8 / 2;
                        foreach (var ln in wrapped[ci])
                        {
                            EmitRun(ln, 13, lightFace,
                                colX[ci] + (cw - W(ln, lightFace, 13)) / 2, yC, ink);
                            yC += 15.8;
                        }
                    }
                    // the row's cell borders
                    Line(tX, rTop + rowH, tX + tW, rTop + rowH, orange, 0.75, false);
                    for (var ci = 0; ci <= 3; ci++)
                        Line(colX[ci], rTop, colX[ci], rTop + rowH, orange, 0.75, false);
                    rTop += rowH;
                }
                else
                {
                    var cw1 = colX[2] - colX[1];
                    var totLines = MeasuredWordWrap(cells.Count > 1 ? cells[1] : "",
                        cw1 - 30.0, regFace, 15);
                    var yT = rTop + CiRowBasePt + 1.9;
                    foreach (var ln in totLines)
                    {
                        EmitRun(ln, 15, regFace,
                            colX[2] - 15.0 - W(ln, regFace, 15), yT + 0, ink);
                        yT += 18.0;
                    }
                    var amt2 = cells.Count > 2 ? cells[2] : "";
                    var midY = rTop + CiRowBasePt + 1.9 + (totLines.Length - 1) * 9.0;
                    EmitRun(amt2, 15, regFace,
                        colX[2] + (colX[3] - colX[2] - W(amt2, regFace, 15)) / 2,
                        midY, ink);
                    rTop += CiRowHPt + (totLines.Length - 1) * 18.0;
                }
            }
            y = rTop + CiDividerGapPt;
        }

        foreach (var (pg, ops) in streams)
            pg.AddContentStream(Encoding.ASCII.GetBytes(ops.ToString() + "\n"));
        return doc;
    }

    // ── Resume-builder document sheets ──────────────────────────────────────
    // The LiveCareer resume export: a `div#document` whose dynamic stylesheet
    // resolves the sheet (11 pt Palatino Linotype at the 13 pt line, 30 pt
    // horizontal padding, 552 pt single column, 23/31 name, 10/12 address,
    // 13/15 section titles). The source engine emits each page's SECTION
    // TITLES first, then the name/address group, then the body — the text
    // fragments keep that order, and every markup span is its own fragment.
    // List items seat their marker at +12.21 and their text at +23 inside
    // the column (30 + 10 pt ul + 13 pt li = the asserted x 53).

    private const double RdPadXPt = 30.0;             // #document hmargins
    private const double RdColWPt = 552.0;            // singlecolumn width
    private const double RdLiMarkerXPt = 12.21;       // li bullet inset
    private const double RdLiTextXPt = 23.0;          // li text inset
    private const double RdLinePt = 13.0;             // resolved line-height
    private const double RdSingleLiPitchPt = 14.84;   // single-line li pitch
                                                      // (the 13 pt marker's box)
    private const double RdTitleToParaPt = 16.25;     // title base → first line
    private const double RdParaToTitlePt = 20.75;     // last line → next title

    private static Document? TryRenderResumeDoc(string html,
        double pageWidth, double pageHeight, HtmlLoadOptions? options)
    {
        if (!html.Contains("id=\"document\"", StringComparison.Ordinal)
            || !html.Contains("class=\"fontsize fontface hmargins", StringComparison.Ordinal)
            || !html.Contains("sectiontitle", StringComparison.Ordinal))
            return null;
        var face = "Palatino Linotype";
        if (PosFace(face).ttf is null || WinMetricsFor(face) is not { } wm) return null;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var c = Regex.Replace(html, @"\s+", " ");
        var marginTop = options?.PageInfo?.Margin?.Top ?? 13.0;
        var marginBottom = options?.PageInfo?.Margin?.Bottom ?? 13.0;

        var doc = Document.Create();
        var docFontDict = new Core.PdfDictionary();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        EnsureFonts(page, docFontDict);

        double Drop(double fs, double box) => (box - fs * wm.sum) / 2 + fs * wm.asc;
        double W(string t, bool bold, double fs)
            => MeasureFaceText(face + (bold ? " Bold" : ""), t, fs);

        // fragments buffer: titles flush FIRST on each page
        var frags = new List<(int grp, double x, double y, double fs, bool bold, Color col, string t)>();
        void FlushPage()
        {
            var sb = new StringBuilder();
            foreach (var f in frags.OrderBy(f => f.grp))
            {
                if (f.t.Length == 0) continue;
                var (rn, hex) = Text.Type0FontEmbedder.Embed(
                    (page.Dict.Get("Resources") as Core.PdfDictionary)!.Get("Font") as Core.PdfDictionary
                        ?? throw new InvalidOperationException(),
                    PosFace(face + (f.bold ? " Bold" : "")).ttf ?? PosFace(face).ttf!,
                    face.Replace(" ", "") + (f.bold ? "Bold" : ""), f.t,
                    stripSpacesInBaseFont: true);
                sb.AppendLine(string.Create(inv,
                    $"q {f.col.R / 255.0:0.###} {f.col.G / 255.0:0.###} {f.col.B / 255.0:0.###} rg " +
                    $"BT /{rn} {f.fs:0.##} Tf 1 0 0 1 {f.x:F2} {pageHeight - f.y:F2} Tm " +
                    $"<{System.Convert.ToHexString(hex)}> Tj ET Q"));
            }
            page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString() + "\n"));
            frags.Clear();
        }
        var black = Color.FromRgb(0, 0, 0);
        var y = marginTop;
        void BreakIf(double need)
        {
            if (y + need <= pageHeight - marginBottom) return;
            FlushPage();
            page = doc.Pages.Add(pageWidth, pageHeight);
            EnsureFonts(page, docFontDict);
            y = marginTop;
        }

        // rich text: runs of (text, bold, color) — each SPAN piece keeps its
        // own fragment, split further at line breaks
        var runRx = new Regex(@"<(/?)(\w[\w-]*)((?:[^>""']|""[^""]*""|'[^']*')*?)(/?)>|([^<]+)",
            RegexOptions.Singleline);
        List<(string t, bool bold, Color col)> ParseRuns(string inner)
        {
            var runs = new List<(string, bool, Color)>();
            var colStack = new Stack<Color>();
            var boldDepth = 0;
            foreach (Match m in runRx.Matches(inner))
            {
                if (m.Groups[5].Success)
                {
                    var txt = DecodeEntities(m.Groups[5].Value);
                    if (txt.Length > 0)
                        runs.Add((txt, boldDepth > 0,
                            colStack.Count > 0 ? colStack.Peek() : black));
                    continue;
                }
                var tag = m.Groups[2].Value.ToLowerInvariant();
                var closeT = m.Groups[1].Value == "/";
                if (tag == "font")
                {
                    if (!closeT)
                    {
                        var fcM = Regex.Match(m.Groups[3].Value, @"color\s*=\s*[""']?(#?\w+)");
                        colStack.Push(fcM.Success && ParseCssColor(fcM.Groups[1].Value) is { } fc
                            ? fc : black);
                    }
                    else if (colStack.Count > 0) colStack.Pop();
                }
                else if (tag is "b" or "strong")
                    boldDepth += closeT ? -1 : 1;
            }
            return runs;
        }

        // wrapped emission of runs into the column; returns the LINE COUNT
        int EmitRuns(List<(string t, bool bold, Color col)> runs, double x0, double availW,
            double fs, double lineH, int grp)
        {
            var flat = new List<(string w, bool bold, Color col)>();
            foreach (var (t, bold, col) in runs)
            {
                var norm = CollapseWs(t);
                foreach (var piece in Regex.Split(norm, @"(?<= )"))
                    if (piece.Length > 0) flat.Add((piece, bold, col));
            }
            var lines = 0;
            var lineRuns = new List<(string t, bool bold, Color col)>();
            double lineW = 0;
            void Flush()
            {
                if (lineRuns.Count == 0) return;
                BreakIf(lineH);
                var x = x0;
                foreach (var (t, bold, col) in lineRuns)
                {
                    frags.Add((grp, x, y + Drop(fs, lineH), fs, bold, col, t));
                    x += W(t, bold, fs);
                }
                y += lineH;
                lines++;
                lineRuns.Clear(); lineW = 0;
            }
            foreach (var (word, bold, col) in flat)
            {
                var wFit = W(word.TrimEnd(' '), bold, fs);
                if (lineRuns.Count > 0 && lineW + wFit > availW && word.Trim().Length > 0)
                    Flush();
                if (lineRuns.Count > 0 && lineRuns[^1].bold == bold
                    && lineRuns[^1].col.Equals(col))
                    lineRuns[^1] = (lineRuns[^1].t + word, bold, col);
                else if (lineRuns.Count == 0 && word.Trim().Length == 0 && lines > 0)
                    continue;                          // no leading space after a wrap
                else lineRuns.Add((word, bold, col));
                lineW += W(word, bold, fs);
            }
            Flush();
            return lines;
        }

        void EmitLis(string ulInner, double colX, double availW, int grp)
        {
            foreach (Match liM in Regex.Matches(ulInner, @"<li[^>]*>(.*?)</li>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                BreakIf(RdLinePt);
                var liTop = y;
                frags.Add((grp, colX + RdLiMarkerXPt, liTop + Drop(11, RdLinePt), 11, false,
                    black, "•"));
                var n = EmitRuns(ParseRuns(liM.Groups[1].Value), colX + RdLiTextXPt,
                    availW - RdLiTextXPt, 11, RdLinePt, grp);
                // a single-line item paces at the 13 pt MARKER box
                if (n <= 1) y = liTop + RdSingleLiPitchPt;
            }
        }

        // ── the walk ──
        var seq = 10;                                 // body group counter
        var firstSection = true;
        foreach (Match secM in Regex.Matches(c,
            @"<div id=""SECTION_(\w{4})\d+"" class=""[^""]*""[^>]*>", RegexOptions.IgnoreCase))
        {
            var kind = secM.Groups[1].Value.ToUpperInvariant();
            var inner = BalancedInner(c, secM.Index + secM.Length, "div") ?? "";
            if (inner.Trim().Length == 0) continue;

            var titleM = Regex.Match(inner, @"class=""sectiontitle"">([^<]*)</div>");
            if (kind == "NAME")
            {
                y += 6.0;                             // section margin-top
                var nameM = Regex.Match(inner, @"<div class=""name"">(.*?)</div>",
                    RegexOptions.Singleline);
                if (nameM.Success)
                {
                    BreakIf(31.0);
                    var runs = ParseRuns(nameM.Groups[1].Value);
                    double total = 0;
                    foreach (var (t, _, _) in runs) total += W(CollapseWs(t), true, 23);
                    var x = RdPadXPt + (RdColWPt - total) / 2;
                    foreach (var (t, _, col) in runs)
                    {
                        var txt = CollapseWs(t);
                        if (txt.Length == 0) continue;
                        frags.Add((1, x, y + Drop(23, 31), 23, true, col, txt));
                        x += W(txt, true, 23);
                    }
                    y += 31.0;
                }
                // the 3 pt lowerborder rule under the name
                if (inner.Contains("lowerborder", StringComparison.Ordinal))
                {
                    page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(inv,
                        $"q 0 0 0 rg {RdPadXPt:F2} {pageHeight - y - 5.0:F2} {RdColWPt:F2} 3 re f Q\n")));
                    y += 5.0;
                }
                firstSection = false;
                continue;
            }
            if (kind == "CNTC")
            {
                // the inline address list: items joined by 13 pt bullet
                // separators on one CENTRED 12 pt line, wrapping items whole
                y += 4.0;                             // address margin-top
                var lis = new List<List<(string t, bool bold, Color col)>>();
                foreach (Match liM in Regex.Matches(inner, @"<li[^>]*>(.*?)</li>",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline))
                    lis.Add(ParseRuns(liM.Groups[1].Value));
                var lineFr = new List<(double w, double fs, double dy, string t)>();
                var lineList = new List<List<(double w, double fs, double dy, string t)>> { lineFr };
                double used = 0;
                for (var i = 0; i < lis.Count; i++)
                {
                    var parts = new List<(double w, double fs, double dy, string t)>();
                    foreach (var (t, _, _) in lis[i])
                    {
                        var txt = CollapseWs(t);
                        if (txt.Trim().Length == 0 && txt.Length <= 1 && parts.Count == 0) continue;
                        if (txt.Length == 0) continue;
                        parts.Add((W(txt, false, 10), 10, 0, txt));
                    }
                    double itemW = 0;
                    foreach (var p in parts) itemW += p.w;
                    if (used > 0 && used + itemW > RdColWPt)
                    { lineFr = new List<(double, double, double, string)>(); lineList.Add(lineFr); used = 0; }
                    lineFr.AddRange(parts); used += itemW;
                    if (i < lis.Count - 1)
                    {
                        // the trailing separator: a text-size space + the
                        // 13 pt bullet riding 1.13 LOW
                        lineFr.Add((2.5, 10, 0, " "));
                        lineFr.Add((W("• ", false, 13), 13, 1.13, i == 0 ? "• " : "•"));
                        used += 2.5 + 11.13;
                    }
                }
                foreach (var ln in lineList)
                {
                    if (ln.Count == 0) continue;
                    BreakIf(12.0);
                    double lw = 0;
                    foreach (var p in ln) lw += p.w;
                    var x = RdPadXPt + (RdColWPt - lw) / 2;
                    foreach (var (w2, fs, dy, t) in ln)
                    {
                        frags.Add((2, x, y + Drop(10, 12) + dy, fs, false, black, t));
                        x += w2;
                    }
                    y += 12.0;
                }
                firstSection = false;
                continue;
            }

            // a titled body section
            y += firstSection ? 0 : 6.0;              // section margin-top
            firstSection = false;
            if (titleM.Success)
            {
                BreakIf(15.0 + RdTitleToParaPt);
                var title = CollapseWs(DecodeEntities(titleM.Groups[1].Value)).Trim();
                frags.Add((0, RdPadXPt, y + Drop(13, 15), 13, true, black, title));
                // title base → first content line base = 16.25 (measured)
                y += RdTitleToParaPt - Drop(11, RdLinePt) + Drop(13, 15);
            }
            seq++;
            var paraN = 0;
            foreach (Match paraM in Regex.Matches(inner,
                @"<div id=""PARAGRAPH_[^""]*"" class=""paragraph[^""]*""[^>]*>",
                RegexOptions.IgnoreCase))
            {
                var pInner = BalancedInner(inner, paraM.Index + paraM.Length, "div") ?? "";
                if (paraN++ > 0) y += 6.0;            // paragraph margin-top
                if (pInner.Contains("table", StringComparison.OrdinalIgnoreCase)
                    && pInner.Contains("twocol", StringComparison.Ordinal))
                {
                    // the two-column skills grid: bullets in 50% columns
                    var tds = Regex.Matches(pInner, @"<td[^>]*class=""field twocol_\d""[^>]*>(.*?)</td>",
                        RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    var colTops = y;
                    double maxY = y;
                    for (var ti = 0; ti < tds.Count; ti++)
                    {
                        y = colTops;
                        var colX = RdPadXPt + ti * (RdColWPt / 2 + 0.5);
                        EmitLis(tds[ti].Groups[1].Value, colX, RdColWPt / 2, seq);
                        maxY = Math.Max(maxY, y);
                    }
                    y = maxY;
                }
                else if (pInner.Contains("paddedline", StringComparison.Ordinal))
                {
                    // a JOB paragraph: title/date line, company line, bullets
                    var spl = Regex.Matches(pInner, @"<span class=""paddedline""[^>]*>(.*?)(?:<br>\s*)?</span>(?=\s*<span|\s*</div>|\s*$)",
                        RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    var jobRuns = new List<(string, bool, Color)>();
                    foreach (Match sM in spl)
                    {
                        if (sM.Groups[1].Value.Contains("<ul", StringComparison.OrdinalIgnoreCase))
                            continue;
                        foreach (Match innerSpan in Regex.Matches(sM.Groups[1].Value,
                            @"<span([^>]*)>(.*?)</span>", RegexOptions.Singleline))
                        {
                            var txt = CollapseWs(DecodeEntities(
                                Regex.Replace(innerSpan.Groups[2].Value, "<[^>]+>", "")));
                            if (txt.Length == 0) continue;
                            var boldSpan = Regex.IsMatch(innerSpan.Groups[1].Value,
                                @"jobtitle|companyname|degree", RegexOptions.IgnoreCase);
                            jobRuns.Add((txt, boldSpan, black));
                        }
                        var hasBr = sM.Value.Contains("<br", StringComparison.OrdinalIgnoreCase);
                        if (hasBr && jobRuns.Count > 0)
                        {
                            BreakIf(RdLinePt);
                            var x = RdPadXPt;
                            foreach (var (t, bold, col) in jobRuns)
                            {
                                frags.Add((seq, x, y + Drop(11, RdLinePt), 11, bold, col, t));
                                x += W(t, bold, 11);
                            }
                            y += RdLinePt;
                            jobRuns.Clear();
                        }
                    }
                    if (jobRuns.Count > 0)
                    {
                        BreakIf(RdLinePt);
                        var x = RdPadXPt;
                        foreach (var (t, bold, col) in jobRuns)
                        {
                            frags.Add((seq, x, y + Drop(11, RdLinePt), 11, bold, col, t));
                            x += W(t, bold, 11);
                        }
                        y += RdLinePt;
                    }
                    var ulM = Regex.Match(pInner, @"<ul>(.*?)</ul>",
                        RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    if (ulM.Success) EmitLis(ulM.Groups[1].Value, RdPadXPt, RdColWPt, seq);
                }
                else if (pInner.Contains("<ul", StringComparison.OrdinalIgnoreCase))
                {
                    var ulM = Regex.Match(pInner, @"<ul>(.*?)</ul>",
                        RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    if (ulM.Success) EmitLis(ulM.Groups[1].Value, RdPadXPt, RdColWPt, seq);
                }
                else
                {
                    var fieldM = Regex.Match(pInner,
                        @"<div class=""field singlecolumn""[^>]*>(.*?)</div>",
                        RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    if (fieldM.Success)
                        EmitRuns(ParseRuns(fieldM.Groups[1].Value), RdPadXPt, RdColWPt,
                            11, RdLinePt, seq);
                }
            }
            // last content line base → next title base = 20.75 (measured):
            // the walk already advanced one line box past the base
            y += RdParaToTitlePt - RdLinePt - (Drop(13, 15) - Drop(11, RdLinePt)) - 6.0;
        }
        FlushPage();
        return doc;
    }

    // ── Angular audit-report exports ────────────────────────────────────────
    // The audit finding sheet: an Angular app dump whose content lives under
    // `.report-container` (40 px padded white card inside the 17 px
    // main-content-middle), headed by h1–h4 at 14 px bold with left-line
    // dashes, then a label table — 9 pt bold th labels in a 50 pt column, a
    // dot cell, and content cells. Text inside the <gt-editor-content> custom
    // element does NOT inherit the table's 12 px size (the source engine's
    // inheritance breaks at the unknown element): it draws at the UA 16 px
    // base, italic via the sheet's span[lang] rule, #333 ink. The measured
    // constants carry their derivations.

    // content x = margins(0) + UA body 8 px + main-middle 17 px + card 40 px
    private const double ArContentXPt = 48.75;
    // th label column: label x and the dot x measured off the sheet
    // (65 px + the border-container's 2 px + the 25 px content-area pad).
    private const double ArThXPt = 69.8;
    private const double ArDotXPt = 119.8;
    // editor / value content opens 4 pt past the dot cell
    private const double ArTdXPt = 123.8;
    // the Bulgu-Kodu value's link ink and the abbrSpan's teal
    private const double ArBadgeFsPt = 8.25;
    // the label column's wrap width: 'Denetlenen' (48.5) keeps its line,
    // 'Bulgu Kodu' (48.7+) breaks — measured off the sheet
    private const double ArThColWPt = 48.95;

    private static Document? TryRenderAuditReport(string html,
        double pageWidth, double pageHeight, HtmlLoadOptions? options)
    {
        if (!html.Contains("audit-report-editable", StringComparison.Ordinal)
            || !html.Contains("gt-editor-content", StringComparison.Ordinal)
            || !html.Contains("report-content-area", StringComparison.Ordinal))
            return null;
        if (WinMetricsFor("Arial") is not { } wm) return null;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var c = Regex.Replace(html, @"\s+", " ");
        var marginTop = options?.PageInfo?.Margin?.Top ?? 20.0;
        var marginBottom = options?.PageInfo?.Margin?.Bottom ?? 10.0;
        // the editor column's wrap edge (measured: the widest paragraph line
        // runs to 567.7 — the card's inner box less the content-area pads)
        // …plus ~4 pt headroom over our Arial advances (the reference's
        // engine kerns its lines slightly tighter than our advance sums)
        // measured: the widest reference line runs to 577.7
        var contentRight = 578.5;

        var inkBody = ParseCssColor("#355154") ?? Color.FromRgb(53, 81, 84);
        var inkEditor = ParseCssColor("#333333") ?? Color.FromRgb(51, 51, 51);
        var inkAbbr = ParseCssColor("#008080") ?? Color.FromRgb(0, 128, 128);
        var inkCode = ParseCssColor("#0782C1") ?? Color.FromRgb(7, 130, 193);
        var railGray = ParseCssColor("#e4e5e9") ?? Color.FromRgb(228, 229, 233);

        var doc = Document.Create();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        var docFontDict = new Core.PdfDictionary();
        EnsureFonts(page, docFontDict);
        var sb = new StringBuilder();
        var streams = new List<(Page pg, StringBuilder ops)> { (page, sb) };

        double Drop(double fs, double box) => (box - fs * wm.sum) / 2 + fs * wm.asc;
        void EmitRun(string text, double fs, string variant, double x, double baseline, Color col)
        {
            if (text.Length == 0) return;
            var faceName = "Arial" + (variant.Length > 0 ? " " + variant : "");
            var (rn, hex) = Text.Type0FontEmbedder.Embed(
                (page.Dict.Get("Resources") as Core.PdfDictionary)!.Get("Font") as Core.PdfDictionary
                    ?? throw new InvalidOperationException(),
                PosFace(faceName).ttf ?? PosFace("Arial").ttf!,
                "Arial" + variant.Replace(" ", ""), text, stripSpacesInBaseFont: true);
            sb.AppendLine(string.Create(inv,
                $"q {col.R / 255.0:0.###} {col.G / 255.0:0.###} {col.B / 255.0:0.###} rg " +
                $"BT /{rn} {fs:0.##} Tf 1 0 0 1 {x:F2} {pageHeight - baseline:F2} Tm " +
                $"<{System.Convert.ToHexString(hex)}> Tj ET Q"));
        }
        double MeasureAr(string t, string variant, double fs)
            => MeasureFaceText("Arial" + (variant.Length > 0 ? " " + variant : ""), t, fs);
        void FillRect(double x, double yTop, double w, double h, Color col)
            => sb.AppendLine(string.Create(inv,
                $"q {col.R / 255.0:0.###} {col.G / 255.0:0.###} {col.B / 255.0:0.###} rg " +
                $"{x:F2} {pageHeight - yTop - h:F2} {w:F2} {h:F2} re f Q"));

        var y = marginTop + 6.0 + 12.75 + 30.0;       // body 8px + middle 17px + card 40px
        var railFrom = double.NaN;                    // border-container rail top (this page)
        void NewPage()
        {
            // close the rail on the finished page
            if (!double.IsNaN(railFrom))
                sb.AppendLine(string.Create(inv,
                    $"q {railGray.R / 255.0:0.###} {railGray.G / 255.0:0.###} {railGray.B / 255.0:0.###} RG 1.5 w " +
                    $"49.5 {pageHeight - railFrom:F2} m 49.5 {marginBottom:F2} l S Q"));
            page = doc.Pages.Add(pageWidth, pageHeight);
            EnsureFonts(page, docFontDict);
            sb = new StringBuilder();
            streams.Add((page, sb));
            y = marginTop;
            railFrom = marginTop;
        }

        // ── the heading group ──
        var h1M = Regex.Match(c, @"<h1 class=""ng-binding"">([^<]*)</h1>");
        if (h1M.Success)
        {
            EmitRun(CollapseWs(DecodeEntities(h1M.Groups[1].Value)).Trim(), 10.5, "Bold",
                ArContentXPt, y + Drop(10.5, 12.6), inkBody);
            y += 12.6 + 3.75;                         // h1 line + title-area 5 px pad
        }
        railFrom = y;                                 // the border-container opens here
        y += 7.5;                                     // content-container 10 px pad-top
        foreach (Match hM in Regex.Matches(c, @"<h([234]) class=""ng-binding""><span class=""left-line""></span>([^<]*)</h\1>"))
        {
            var lvl = int.Parse(hM.Groups[1].Value, inv);
            // measured: h2 +9 (2px border + 10px pad), h3 +12.75, h4 +18
            var xOff = lvl == 2 ? 9.0 : lvl == 3 ? 12.75 : 18.0;
            // the left-line dash under the heading (5/8/13 px wide, top:9px)
            FillRect(ArContentXPt + 1.5, y + 6.75, lvl == 2 ? 3.8 : lvl == 3 ? 6.0 : 9.8, 1.5, railGray);
            EmitRun(CollapseWs(DecodeEntities(hM.Groups[2].Value)).Trim(), 10.5, "Bold",
                ArContentXPt + xOff, y + Drop(10.5, 12.6), inkBody);
            y += 12.6 + 9.0;                          // line + 12 px pad-bottom
        }
        y += 26.25;                                   // content-area 35 px pad-top

        // ── the label rows ──
        // editor paragraph model: runs of (text, italic, color)
        var pRunRx = new Regex(@"<(/?)(\w[\w-]*)((?:[^>""']|""[^""]*""|'[^']*')*?)(/?)>|([^<]+)",
            RegexOptions.Singleline);
        List<List<(string t, bool it, Color col)>> ParseEditorParas(string tdInner)
        {
            var paras = new List<List<(string, bool, Color)>>();
            List<(string, bool, Color)>? cur = null;
            var curAdded = false;                     // div paras add LAZILY on first ink
            var langDepth = 0; var langStack = new Stack<bool>();
            var colStack = new Stack<Color>();
            var abbrStack = new Stack<bool>(); var abbrDepth = 0;
            foreach (Match m in pRunRx.Matches(tdInner))
            {
                if (m.Groups[5].Success)
                {
                    if (cur is null) continue;
                    var txt = DecodeEntities(m.Groups[5].Value);
                    if (txt.Length == 0) continue;
                    // inter-tag whitespace never OPENS a lazy div paragraph —
                    // but an &nbsp; (U+00A0, surviving the space trim) does
                    if (!curAdded && txt.Trim(' ').Length == 0) continue;
                    var col = colStack.Count > 0 ? colStack.Peek()
                        : abbrDepth > 0 ? inkAbbr : inkEditor;
                    if (!curAdded) { paras.Add(cur); curAdded = true; }
                    cur.Add((txt, langDepth > 0, col));
                    continue;
                }
                var tag = m.Groups[2].Value.ToLowerInvariant();
                var closeT = m.Groups[1].Value == "/";
                var selfC = m.Groups[4].Value == "/";
                if (tag == "p")
                {
                    if (!closeT)
                    { cur = new List<(string, bool, Color)>(); paras.Add(cur); curAdded = true; }
                    else cur = null;
                    continue;
                }
                // a content div carries its own line when it holds direct text
                // (the manual-editor '&nbsp;' and 'test' blocks)
                if (tag == "div" && m.Groups[3].Value.Contains("editor-content-readonly",
                        StringComparison.Ordinal))
                {
                    if (!closeT) { cur = new List<(string, bool, Color)>(); curAdded = false; }
                    continue;
                }
                if (cur is null || selfC) continue;
                if (tag == "span")
                {
                    if (!closeT)
                    {
                        var hasLang = m.Groups[3].Value.Contains("lang=", StringComparison.OrdinalIgnoreCase);
                        var isAbbr = m.Groups[3].Value.Contains("abbrSpan", StringComparison.OrdinalIgnoreCase);
                        langStack.Push(hasLang); if (hasLang) langDepth++;
                        abbrStack.Push(isAbbr); if (isAbbr) abbrDepth++;
                    }
                    else
                    {
                        if (langStack.Count > 0 && langStack.Pop()) langDepth--;
                        if (abbrStack.Count > 0 && abbrStack.Pop()) abbrDepth--;
                    }
                }
                else if (tag == "font")
                {
                    if (!closeT)
                    {
                        var fcM = Regex.Match(m.Groups[3].Value, @"color\s*=\s*[""']?(#?\w+)");
                        colStack.Push(fcM.Success && ParseCssColor(fcM.Groups[1].Value) is { } fc
                            ? fc : inkEditor);
                    }
                    else if (colStack.Count > 0) colStack.Pop();
                }
            }
            return paras;
        }

        // wrapped emission of one paragraph's runs at the editor 12 pt line
        void EmitPara(List<(string t, bool it, Color col)> runs)
        {
            const double fs = 12.0; const double lh = 13.5;
            var flat = new List<(string word, bool it, Color col)>();
            foreach (var (t, it, col) in runs)
                foreach (var piece in Regex.Split(t, @"(?<= )"))
                    if (piece.Length > 0) flat.Add((piece, it, col));
            if (flat.Count == 0)
            {
                if (y + lh > pageHeight - marginBottom) NewPage();
                EmitRun(" ", fs, "", ArTdXPt, y + Drop(fs, lh), inkEditor);
                y += lh;
                return;
            }
            var lineRuns = new List<(string t, bool it, Color col)>();
            double lineW = 0;
            void FlushLine()
            {
                if (lineRuns.Count == 0) return;
                if (y + 13.5 > pageHeight - marginBottom) NewPage();
                var x = ArTdXPt;
                foreach (var (t, it, col) in lineRuns)
                {
                    EmitRun(t, fs, it ? "Italic" : "", x, y + Drop(fs, lh), col);
                    // Arial's italic advances match the upright — measure the
                    // upright face so wrap and seats stay off the real widths
                    x += MeasureAr(t, "", fs);
                }
                y += lh;
                lineRuns.Clear(); lineW = 0;
            }
            foreach (var (word, it, col) in flat)
            {
                var wWidth = MeasureAr(word, "", fs);
                // a word's TRAILING space hangs past the wrap edge — the fit
                // check measures the trimmed word, the advance keeps the space
                var wFit = MeasureAr(word.TrimEnd(' '), "", fs);
                if (lineW + wFit > contentRight - ArTdXPt && lineRuns.Count > 0
                    && word.Trim().Length > 0)
                    FlushLine();
                if (lineRuns.Count > 0 && lineRuns[^1].it == it
                    && lineRuns[^1].col.Equals(col))
                {
                    var last = lineRuns[^1];
                    lineRuns[^1] = (last.t + word, it, col);
                }
                else lineRuns.Add((word, it, col));
                lineW += wWidth;
            }
            FlushLine();
        }

        // th labels wrap in their 50 pt column at the 10.5 pt label pitch
        void EmitLabel(string label, double atY)
        {
            var savedY = y;
            y = atY;
            foreach (var ln in MeasuredWordWrap(label, ArThColWPt, "Arial Bold", 9.0))
            {
                EmitRun(ln, 9.0, "Bold", ArThXPt, y + Drop(9.0, 10.5), inkBody);
                y += 10.5;
            }
            y = savedY;
        }

        var contentArea = c[(c.IndexOf("report-content-area", StringComparison.Ordinal))..];
        foreach (Match rowM in Regex.Matches(contentArea,
            @"<th class=""ng-binding"">([^<]{1,60})</th>\s*<td class=""dot"">:</td>\s*<td[^>]*>",
            RegexOptions.IgnoreCase))
        {
            var label = CollapseWs(DecodeEntities(rowM.Groups[1].Value)).Trim();
            var tdInner = BalancedInner(contentArea, rowM.Index + rowM.Length, "td") ?? "";
            if (y + 13.5 > pageHeight - marginBottom) NewPage();
            var rowTop = y;
            var rowPageCount = streams.Count;
            EmitLabel(label, rowTop);
            if (tdInner.Contains("gt-editor-content", StringComparison.Ordinal))
            {
                EmitRun(":", 9.0, "", ArDotXPt, rowTop + Drop(9.0, 10.5), inkBody);
                y = rowTop;                           // paragraphs seat on the row top
                foreach (var para in ParseEditorParas(tdInner)) EmitPara(para);
            }
            else if (tdInner.Contains("class=\"dtr\"", StringComparison.Ordinal))
            {
                EmitRun(":", 9.0, "", ArDotXPt, rowTop + Drop(9.0, 10.5), inkBody);
                y = rowTop + 7.5;
                EmitEvidenceGrid(tdInner);
            }
            else
            {
                // a plain value row: ': value' at the label size; a badge span
                // draws its pill; wrapped continuation re-seats at the td x
                var badgeM = Regex.Match(tdInner,
                    @"<span class=""badge-grey[^""]*""[^>]*>([^<]*)</span>", RegexOptions.IgnoreCase);
                var valTxt = CollapseWs(DecodeEntities(
                    Regex.Replace(Regex.Replace(tdInner,
                        @"<span class=""badge-grey.*?</span>", "", RegexOptions.Singleline),
                        "<[^>]+>", " "))).Trim();
                var linkVal = Regex.IsMatch(tdInner, @"<a\b", RegexOptions.IgnoreCase);
                var first = ": " + valTxt;
                var avail = contentRight - ArDotXPt;
                var lines = MeasuredWordWrap(first, avail, "Arial", 9.0);
                var yV = rowTop;
                for (var li = 0; li < lines.Length; li++)
                {
                    EmitRun(lines[li], 9.0, "", li == 0 ? ArDotXPt : ArTdXPt,
                        yV + Drop(9.0, 10.5), linkVal ? inkCode : inkBody);
                    yV += 10.5;
                }
                if (badgeM.Success)
                {
                    var bTxt = CollapseWs(DecodeEntities(badgeM.Groups[1].Value)).Trim();
                    var bX = ArDotXPt + MeasureAr(": " + valTxt, "", 9.0) + 7.5;
                    var bW = MeasureAr(bTxt, "", ArBadgeFsPt) + 5.0;
                    FillRect(bX, rowTop + 0.9, bW, 14.3,
                        ParseCssColor("#617778") ?? Color.FromRgb(97, 119, 120));
                    EmitRun(bTxt, ArBadgeFsPt, "", bX + 2.2, rowTop + 10.9,
                        Color.FromRgb(255, 255, 255));
                }
                y = yV;
                // the badge pill paces its row past the single text line
                if (badgeM.Success) y = Math.Max(y, rowTop + 14.3);
            }
            // the th block may run deeper than the value — but only while the
            // row is still on the page it OPENED on (a split row's clamp would
            // re-apply the previous page's extent)
            if (streams.Count == rowPageCount)
            {
                var thLines = MeasuredWordWrap(label, ArThColWPt, "Arial Bold", 9.0).Length;
                y = Math.Max(y, rowTop + thLines * 10.5);
            }
            y += 15.72;                               // 20 px padding-bottom (measured
                                                      // block pitch: 137.22 per row)
        }

        // ── the Tespitler evidence grid: measured columns (the sheet's mixed
        // %-and-min-content solve, overflowing the card to the right) ──
        void EmitEvidenceGrid(string tdInner)
        {
            double[] edges = { 123.8, 171.1, 219.4, 258.2, 310.7, 365.2, 413.7,
                               443.2, 497.2, 526.7, 706.6 };
            // the LAST column's TEXT wraps at its 15% share (~70 pt) even
            // though its band runs wider (measured: 'Giderilme / Durumu')
            double TextW(int i) => (i == edges.Length - 2 ? 597.2 : edges[i + 1]) - edges[i] - 13.5;
            // greedy wrap that also breaks AFTER hyphens (the sheet splits
            // '2016-01-11' at the hyphen inside its narrow date column)
            string[] GridWrap(string text, double availW, string variant)
            {
                var toks = new List<string>();
                foreach (var wpart in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var start = 0;
                    for (var k = 0; k < wpart.Length - 1; k++)
                        if (wpart[k] == '-') { toks.Add(wpart[start..(k + 1)]); start = k + 1; }
                    if (start < wpart.Length) toks.Add(wpart[start..]);
                }
                var outLines = new List<string>(); var curLn = "";
                foreach (var tok in toks)
                {
                    var cand = curLn.Length == 0 || curLn.EndsWith('-') ? curLn + tok
                        : curLn + " " + tok;
                    // the lira glyph measures as a plain cap in the sheet's
                    // solve (our width table lacks it)
                    if (curLn.Length > 0
                        && MeasureAr(cand.Replace('₺', 'T'), variant, 9.0) > availW)
                    { outLines.Add(curLn); curLn = tok; }
                    else curLn = cand;
                }
                if (curLn.Length > 0) outLines.Add(curLn);
                return outLines.Count > 0 ? outLines.ToArray() : new[] { "" };
            }
            var ths = new List<string>();
            foreach (Match thM in Regex.Matches(tdInner, @"<th[^>]*>(.*?)</th>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
                ths.Add(CollapseWs(DecodeEntities(Regex.Replace(thM.Groups[1].Value, "<[^>]+>", " "))).Trim());
            var rows = new List<List<string>>();
            var bodyIdx = tdInner.IndexOf("<tbody", StringComparison.OrdinalIgnoreCase);
            if (bodyIdx >= 0)
                foreach (Match trM in Regex.Matches(tdInner[bodyIdx..], @"<tr\b[^>]*>(.*?)</tr>",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline))
                {
                    var cells = new List<string>();
                    foreach (Match tdM in Regex.Matches(trM.Groups[1].Value, @"<td[^>]*>(.*?)</td>",
                        RegexOptions.IgnoreCase | RegexOptions.Singleline))
                        cells.Add(CollapseWs(DecodeEntities(
                            Regex.Replace(tdM.Groups[1].Value, "<[^>]+>", " "))).Trim());
                    if (cells.Count > 0) rows.Add(cells);
                }

            var gridTop = y;
            var maxHeadLines = 1;
            for (var i = 0; i < ths.Count && i < edges.Length - 1; i++)
            {
                var lines = GridWrap(ths[i], TextW(i), "Bold");
                var yH = gridTop;
                foreach (var ln in lines)
                {
                    if (yH + 10.5 > pageHeight - marginBottom) break;
                    EmitRun(ln, 9.0, "Bold", edges[i] + 7.5, yH + Drop(9.0, 10.5), inkBody);
                    yH += 10.5;
                }
                maxHeadLines = Math.Max(maxHeadLines, lines.Length);
            }
            y = gridTop + maxHeadLines * 10.5 + 3.0;
            // the header underline, per column
            for (var i = 0; i + 1 < edges.Length; i++)
                sb.AppendLine(string.Create(inv,
                    $"q 0.812 0.812 0.812 RG 0.75 w {edges[i]:F2} {pageHeight - y:F2} m " +
                    $"{edges[i + 1]:F2} {pageHeight - y:F2} l S Q"));
            y += 3.0;
            foreach (var row in rows)
            {
                var rowTop2 = y;
                double maxLines = 1;
                for (var i = 0; i < row.Count && i < edges.Length - 1; i++)
                {
                    var lines = GridWrap(row[i], TextW(i), "");
                    var yV = rowTop2;
                    foreach (var ln in lines)
                    {
                        EmitRun(ln, 9.0, "", edges[i] + 7.5, yV + Drop(9.0, 10.5), inkBody);
                        yV += 10.5;
                    }
                    maxLines = Math.Max(maxLines, lines.Length);
                }
                y = rowTop2 + maxLines * 10.5 + 3.0;
                sb.AppendLine(string.Create(inv,
                    $"q {railGray.R / 255.0:0.###} {railGray.G / 255.0:0.###} {railGray.B / 255.0:0.###} RG 0.75 w " +
                    $"{edges[0]:F2} {pageHeight - y:F2} m {edges[^1]:F2} {pageHeight - y:F2} l S Q"));
                y += 3.0;
            }
            // measured: the grid advances a small band past its last row rule
            // before the next label row opens
            y += 5.5;
        }

        // close the rail on the last page
        if (!double.IsNaN(railFrom))
            sb.AppendLine(string.Create(inv,
                $"q {railGray.R / 255.0:0.###} {railGray.G / 255.0:0.###} {railGray.B / 255.0:0.###} RG 1.5 w " +
                $"49.5 {pageHeight - railFrom:F2} m 49.5 {pageHeight - Math.Min(y + 30, pageHeight - marginBottom):F2} l S Q"));

        foreach (var (pg, ops) in streams)
            pg.AddContentStream(Encoding.ASCII.GetBytes(ops.ToString() + "\n"));
        return doc;
    }

    // ── Decision-notification letters ───────────────────────────────────────
    // A TCI notifications template: an all-inline-styled sheet under a centred
    // `basis` container (duplicate width declarations — the LAST wins), a float
    // header (broken logo/QR images keeping their divs' widths), bordered
    // `information-table`s with bold label spans riding their value lines,
    // `boxSection` panels whose white-backed titles sit ON the 2 px frame, and
    // a 48.5 % float column pair. All seats derive from the inline box model at
    // the UA 13 px body line; the measured constants below carry their
    // derivations.

    // Left page margin of the letter flow, and the right margin the widened
    // sheet keeps past the container (measured: page = 96 + 15 + 720 + 90).
    private const double DnLeftMarginPt = 96.0;
    private const double DnRightMarginPt = 90.0;
    // The header's broken images seat their frames at 86 = 72 pt content top
    // + the UA 8 px body margin + the header's 10 px padding (both 0.75-scaled).
    private const double DnHeaderImgTopPt = 86.0;
    // h3 title baseline (measured 117.9: image top + title 17 px padding +
    // the UA h3 margin and its 15 px line's seat).
    private const double DnH3BaselinePt = 117.9;
    // h3 → h1 baseline advance (h3 descent + UA h3/h1 margin collapse + the
    // 29 px line's ascent, measured).
    private const double DnH1AdvancePt = 36.9;
    // The bordered info area's top edge (measured 182.1: the float column's
    // bottom — h1 baseline + descent + its 0.67 em bottom margin + the title
    // div's 10 px bottom padding).
    private const double DnInfoTopPt = 182.1;
    // A box title's knockout drops (40 px header − 24 px title) under its
    // section top; the 2 px frame runs 40 px − the content's −12 px margin.
    private const double DnTitleKnockoutDropPt = 12.15;
    private const double DnFrameDropPt = 21.0;
    // The continuation page's content top (measured off the footer table's
    // vertically-centred rows: 2-line cell first baseline 88.5, 1-line 94.1).
    private const double DnPage2TopPt = 79.8;

    private static Document? TryRenderDecisionLetter(string html, double pageHeight)
    {
        if (!html.Contains("class=\"basis\"", StringComparison.OrdinalIgnoreCase)
            || !html.Contains("boxSection", StringComparison.OrdinalIgnoreCase)
            || !html.Contains("information-table", StringComparison.OrdinalIgnoreCase)
            || !html.Contains("box-header-title", StringComparison.OrdinalIgnoreCase))
            return null;
        var c = Regex.Replace(html, @"\s+", " ");
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var bodyM = Regex.Match(c, @"<body\b[^>]*style\s*=\s*[""']([^""']*)[""']",
            RegexOptions.IgnoreCase);
        if (!bodyM.Success) return null;
        var faceM = Regex.Match(bodyM.Groups[1].Value, @"font-family\s*:\s*([^;]+)",
            RegexOptions.IgnoreCase);
        var face = faceM.Success ? FirstFontFamily(faceM.Groups[1].Value) : null;
        if (face is null || WinMetricsFor(face) is not { } wm) return null;
        var bodyFs = CssEmLen(bodyM.Groups[1].Value, "font-size", 12) ?? 9.75;

        var basisM = Regex.Match(c,
            @"<div\b[^>]*class\s*=\s*[""']basis[""'][^>]*style\s*=\s*[""']([^""']*)[""']",
            RegexOptions.IgnoreCase);
        if (!basisM.Success) return null;
        double basisW = 0;
        foreach (Match wMatch in Regex.Matches(basisM.Groups[1].Value,
            @"(?<![-\w])width\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase))
            basisW = double.Parse(wMatch.Groups[1].Value, inv) * 0.75;   // LAST wins
        if (basisW <= 0) return null;
        var padM = Regex.Match(basisM.Groups[1].Value,
            @"padding\s*:\s*[\d.]+\w*\s+([\d.]+)px", RegexOptions.IgnoreCase);
        var sidePad = padM.Success ? double.Parse(padM.Groups[1].Value, inv) * 0.75 : 15.0;

        var pageWidth = DnLeftMarginPt + sidePad + basisW + DnRightMarginPt;
        var cx = DnLeftMarginPt + sidePad;
        var cw = basisW;
        var lineH = MetricLineHeight(bodyFs, wm.sum <= 1.0 ? 1.2 : wm.sum);

        var ink = ParseCssColor("#5c5c5c") ?? Color.FromRgb(92, 92, 92);
        var bandBlue = ParseCssColor("#d3e0ec") ?? Color.FromRgb(211, 224, 236);
        var borderLite = ParseCssColor("#f3f3f3") ?? Color.FromRgb(243, 243, 243);
        var frameGray = ParseCssColor("#e8e8e8") ?? Color.FromRgb(232, 232, 232);

        var doc = Document.Create();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        var docFontDict = new Core.PdfDictionary();
        EnsureFonts(page, docFontDict);
        var sb = new StringBuilder();
        var pageStreams = new List<(Page pg, StringBuilder ops)> { (page, sb) };

        double Drop(double fs, double box) => (box - fs * wm.sum) / 2 + fs * wm.asc;
        void SetFill(Color col) => sb.AppendLine(string.Create(inv,
            $"{col.R / 255.0:0.###} {col.G / 255.0:0.###} {col.B / 255.0:0.###} rg"));
        void Fill(double x, double yTop, double w, double h, Color col)
        {
            SetFill(col);
            sb.AppendLine(string.Create(inv,
                $"{x:F2} {pageHeight - yTop - h:F2} {w:F2} {h:F2} re f"));
        }
        void HLine(double x0, double x1, double yTop, Color col, double w = 0.75)
            => sb.AppendLine(string.Create(inv,
                $"q {col.R / 255.0:0.###} {col.G / 255.0:0.###} {col.B / 255.0:0.###} RG {w:0.##} w " +
                $"{x0:F2} {pageHeight - yTop:F2} m {x1:F2} {pageHeight - yTop:F2} l S Q"));
        void VLine(double x, double y0, double y1, Color col, double w = 0.75)
            => sb.AppendLine(string.Create(inv,
                $"q {col.R / 255.0:0.###} {col.G / 255.0:0.###} {col.B / 255.0:0.###} RG {w:0.##} w " +
                $"{x:F2} {pageHeight - y0:F2} m {x:F2} {pageHeight - y1:F2} l S Q"));
        void FrameRect(double x, double yTop, double w, double h, Color col, double lw)
            => sb.AppendLine(string.Create(inv,
                $"q {col.R / 255.0:0.###} {col.G / 255.0:0.###} {col.B / 255.0:0.###} RG {lw:0.##} w " +
                $"{x:F2} {pageHeight - yTop - h:F2} {w:F2} {h:F2} re S Q"));
        double Measure(string t, bool bold, double fs)
            => MeasureFaceText(face + (bold ? " Bold" : ""), t, fs);
        void EmitRun(string text, double fs, bool bold, double x, double baseline, Color? col)
        {
            if (text.Length == 0) return;
            var (rn, hex) = Text.Type0FontEmbedder.Embed(
                (page.Dict.Get("Resources") as Core.PdfDictionary)!.Get("Font") as Core.PdfDictionary
                    ?? throw new InvalidOperationException(),
                PosFace(face + (bold ? " Bold" : "")).ttf ?? PosFace(face).ttf!,
                face.Replace(" ", "") + (bold ? "Bold" : ""), text, stripSpacesInBaseFont: true);
            var cc = col ?? ink;
            sb.AppendLine(string.Create(inv,
                $"q {cc.R / 255.0:0.###} {cc.G / 255.0:0.###} {cc.B / 255.0:0.###} rg " +
                $"BT /{rn} {fs:0.##} Tf 1 0 0 1 {x:F2} {pageHeight - baseline:F2} Tm " +
                $"<{System.Convert.ToHexString(hex)}> Tj ET Q"));
        }
        // a label span + its value on ONE line: bold prefix, plain remainder
        void EmitLabelValue(string label, string value, double fs, double x, double baseline)
        {
            EmitRun(label, fs, true, x, baseline, null);
            EmitRun(value, fs, false, x + Measure(label, true, fs), baseline, null);
        }
        string Inner(string tagged) => CollapseWs(DecodeEntities(
            Regex.Replace(tagged, "<[^>]+>", " "))).Trim();

        // ── header: broken logo + floated title column + broken QR ──
        var iconRef = (Core.PdfIndirectRef?)null;
        void BrokenFrame(double x, double top)
        {
            var phName = RegisterPlaceholderIcon(doc, page, ref iconRef, masked: true);
            page.AddContentStream(Encoding.ASCII.GetBytes(FormattableString.Invariant(
                $"q 32 0 0 32 {x + 1:0.##} {pageHeight - top - 33:0.##} cm /{phName} Do Q\n")));
            var dk = ParseCssColor("#555555");
            var lt = ParseCssColor("#AAAAAA");
            DrawBox(page, x, pageHeight - top, 34, 1, null, 0, dk);
            DrawBox(page, x, pageHeight - top - 33, 34, 1, null, 0, lt);
            DrawBox(page, x, pageHeight - top - 33, 1, 34, null, 0, dk);
            DrawBox(page, x + 33, pageHeight - top - 33, 1, 34, null, 0, lt);
        }
        BrokenFrame(cx, DnHeaderImgTopPt);
        var qrPadM = Regex.Match(c, @"class\s*=\s*[""']qr-code[^""']*[""'][^>]*style\s*=\s*[""']([^""']*)[""']",
            RegexOptions.IgnoreCase);
        var qrPad = qrPadM.Success
            ? CssEmLen(qrPadM.Groups[1].Value.Replace("padding: 0 ", "padding-left: "),
                "padding-left", bodyFs) ?? 7.5 : 7.5;
        BrokenFrame(cx + cw - qrPad - 34, DnHeaderImgTopPt);

        var hdrImgW = 0.0;
        var hdrImgM = Regex.Match(c,
            @"class\s*=\s*[""']header-image[""'][^>]*style\s*=\s*[""']([^""']*)[""']",
            RegexOptions.IgnoreCase);
        if (hdrImgM.Success)
            hdrImgW = CssEmLen(hdrImgM.Groups[1].Value, "width", bodyFs) ?? 0;
        var titlePadLeft = 7.5;                       // page-title padding … 10px
        var titleX = cx + hdrImgW + titlePadLeft;

        var h3M = Regex.Match(c, @"<h3\b[^>]*>(.*?)</h3>", RegexOptions.IgnoreCase);
        var h1M = Regex.Match(c, @"<h1\b[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase);
        var y = DnH3BaselinePt;
        if (h3M.Success)
        {
            var h3Style = Regex.Match(h3M.Value, @"style\s*=\s*[""']([^""']*)[""']").Groups[1].Value;
            var h3Fs = CssEmLen(h3Style, "font-size", bodyFs) ?? 11.25;
            var h3Text = Inner(h3M.Groups[1].Value);
            if (h3Style.Contains("uppercase", StringComparison.OrdinalIgnoreCase))
                h3Text = h3Text.ToUpperInvariant();
            EmitRun(h3Text, h3Fs, true, titleX, y,
                ParseCssColor(Regex.Match(h3Style, @"color\s*:\s*([^;]+)").Groups[1].Value.Trim()));
        }
        y += DnH1AdvancePt;
        if (h1M.Success)
        {
            var h1Style = Regex.Match(h1M.Value, @"style\s*=\s*[""']([^""']*)[""']").Groups[1].Value;
            var h1Fs = CssEmLen(h1Style, "font-size", bodyFs) ?? 21.75;
            EmitRun(Inner(h1M.Groups[1].Value), h1Fs, true, titleX, y,
                ParseCssColor(Regex.Match(h1Style, @"color\s*:\s*([^;]+)").Groups[1].Value.Trim()));
        }

        // ── the info tables: cells = label-span lines ──
        var infoTables = new List<List<List<(string label, string val)>>>();
        foreach (Match tM in Regex.Matches(c,
            @"<table\b[^>]*class\s*=\s*[""']information-table[""'][^>]*>", RegexOptions.IgnoreCase))
        {
            var innerT = BalancedInner(c, tM.Index + tM.Length, "table");
            if (innerT is null) continue;
            var cells = new List<List<(string, string)>>();
            foreach (Match tdM in Regex.Matches(innerT, @"<td\b[^>]*>(.*?)</td>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var lines = new List<(string, string)>();
                foreach (var part in Regex.Split(tdM.Groups[1].Value, @"<br\s*/?>",
                    RegexOptions.IgnoreCase))
                {
                    var spM = Regex.Match(part, @"<span\b[^>]*>(.*?)</span>(.*)$",
                        RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    if (spM.Success)
                        lines.Add((Inner(spM.Groups[1].Value),
                            " " + Inner(spM.Groups[2].Value)));
                    else if (Inner(part).Length > 0)
                        lines.Add(("", Inner(part)));
                }
                cells.Add(lines);
            }
            infoTables.Add(cells);
        }
        if (infoTables.Count < 2) return null;

        // r1: the banded 3-column grid — equal declared columns, band fill,
        // #d3e0ec horizontals and #f3f3f3 verticals; 12px/15px cell padding
        var infoTop = DnInfoTopPt;
        var r1 = infoTables[0];
        var r1RowH = 9.0 + 2 * lineH + 9.0;           // 12 px pads over two lines
        Fill(cx + 0.75, infoTop + 0.4, cw - 1.5, r1RowH, bandBlue);
        var nCols1 = Math.Max(1, r1.Count);
        var colW1 = cw / nCols1;
        for (var i = 0; i <= nCols1; i++)
            VLine(cx + i * colW1, infoTop + 0.4, infoTop + 0.4 + r1RowH, borderLite);
        HLine(cx + 0.75, cx + cw - 0.75, infoTop + 0.75, bandBlue);
        HLine(cx + 0.75, cx + cw - 0.75, infoTop + 0.4 + r1RowH, bandBlue);
        for (var i = 0; i < r1.Count; i++)
        {
            var xCell = cx + i * colW1 + 0.75 + 11.25;   // border + 15 px pad
            var yLine = infoTop + 0.4 + 9.0 + Drop(bodyFs, lineH);
            foreach (var (lbl, val) in r1[i])
            {
                EmitLabelValue(lbl, val, bodyFs, xCell, yLine);
                yLine += lineH;
            }
        }
        // r2: the auto-layout contact strip — columns at max-content + 15 px
        // side pads, surplus ∝ content (the engine's auto solve)
        var r2 = infoTables[1];
        var r2Top = infoTop + 0.4 + r1RowH;
        var r2RowH = 3.75 + lineH + 3.75;             // 5 px pads over one line
        {
            var maxc = new double[r2.Count];
            double sum = 0;
            for (var i = 0; i < r2.Count; i++)
            {
                foreach (var (lbl, val) in r2[i])
                    maxc[i] = Math.Max(maxc[i],
                        Measure(lbl, true, bodyFs) + Measure(val, false, bodyFs));
                sum += maxc[i] + 22.5;
            }
            var surplus = Math.Max(0, cw - sum);
            double xCell = cx + 0.75 + 11.25;
            var yLine = r2Top + 3.75 + Drop(bodyFs, lineH);
            double contentSum = 0;
            foreach (var mcW in maxc) contentSum += mcW;
            for (var i = 0; i < r2.Count; i++)
            {
                if (r2[i].Count > 0)
                    EmitLabelValue(r2[i][0].label, r2[i][0].val, bodyFs, xCell, yLine);
                xCell += maxc[i] + 22.5
                    + (contentSum > 0 ? surplus * maxc[i] / contentSum : 0);
            }
        }
        var infoBottom = r2Top + r2RowH;
        FrameRect(cx, infoTop, cw, infoBottom - infoTop + 0.4, frameGray, 0.75);

        // ── boxSection machinery ──
        double BoxTitle(string title, double x, double sectionTop)
        {
            var kx = x + 18.0;                        // title left: 24 px
            var kTop = sectionTop + DnTitleKnockoutDropPt;
            Fill(kx, kTop, Measure(title, true, bodyFs) + 12.0, 18.0,
                Color.FromRgb(255, 255, 255));
            EmitRun(title, bodyFs, true, kx + 6.0, kTop + Drop(bodyFs, 18.0), null);
            return sectionTop + DnFrameDropPt;        // the 2 px frame's top
        }

        // ── Collateral: th band + value row, equal declared columns ──
        var sectionTop0 = infoBottom + 1.2;
        var colM = Regex.Match(c,
            @"<table\b[^>]*class\s*=\s*[""']application-approved-table[""'][^>]*>",
            RegexOptions.IgnoreCase);
        var colBottom = sectionTop0;
        if (colM.Success && BalancedInner(c, colM.Index + colM.Length, "table") is { } colInner)
        {
            var ths = new List<string>();
            foreach (Match thM in Regex.Matches(colInner, @"<th\b[^>]*>(.*?)</th>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
                ths.Add(Inner(thM.Groups[1].Value));
            var tds = new List<string>();
            foreach (Match tdM in Regex.Matches(colInner, @"<td\b[^>]*>(.*?)</td>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
                tds.Add(Inner(tdM.Groups[1].Value));

            var frameTop = BoxTitle("Collateral", cx, sectionTop0);
            var padSide = 7.5;                        // box-content 10 px sides
            var tX = cx + 2 + padSide;
            var tW = cw - 4 - 2 * padSide;
            var thTop = frameTop + 2 + 11.25;         // 15 px content top pad
            var thH = 19.5;                           // 26 px line-height rows
            var n = Math.Max(1, ths.Count);
            // the auto solve: each column floors at max(declared 98 px, its
            // content + the 10px/4px pads), the leftover splits EQUALLY
            var thDeclared = 73.5;
            {
                var thDeclM = Regex.Match(colInner, @"<th\b[^>]*width\s*:\s*([\d.]+)px",
                    RegexOptions.IgnoreCase);
                if (thDeclM.Success)
                    thDeclared = double.Parse(thDeclM.Groups[1].Value, inv) * 0.75;
            }
            var colWs = new double[n];
            double colSum = 0;
            for (var i = 0; i < n; i++)
            {
                var contentW = Math.Max(
                    i < ths.Count ? Measure(ths[i], true, bodyFs) : 0,
                    i < tds.Count ? Measure(tds[i], false, bodyFs) : 0) + 10.5;
                colWs[i] = Math.Max(thDeclared, contentW);
                colSum += colWs[i];
            }
            var share = Math.Max(0, tW - colSum) / n;
            for (var i = 0; i < n; i++) colWs[i] += share;
            var xTh = tX;
            for (var i = 0; i < ths.Count; i++)
            {
                Fill(xTh, thTop, colWs[i], thH + 0.75, bandBlue);
                FrameRect(xTh, thTop, colWs[i], thH + 0.75, borderLite, 0.75);
                EmitRun(ths[i], bodyFs, true,
                    xTh + (colWs[i] - Measure(ths[i], true, bodyFs)) / 2,
                    thTop + Drop(bodyFs, thH), null);
                xTh += colWs[i];
            }
            var tdTop = thTop + thH + 0.75;
            var xTd = tX;
            for (var i = 0; i < tds.Count && i < n; i++)
            {
                HLine(xTd, xTd + colWs[i], tdTop + thH + 0.75, bandBlue);
                VLine(xTd, tdTop, tdTop + thH + 0.75, borderLite);
                EmitRun(tds[i], bodyFs, false, xTd + 7.5,
                    tdTop + Drop(bodyFs, thH), null);
                xTd += colWs[i];
            }
            colBottom = tdTop + thH + 0.75 + 7.5 + 2;   // pad-bottom + frame
            FrameRect(cx, frameTop, cw, colBottom - frameTop, frameGray, 1.5);
        }

        // ── the 48.5 % float pair: left Dealer Structure, right the
        //    Stipulations + Comments stack ──
        var midTop = colBottom + 11.25;               // boxSection 15 px margin
        var halfM = Regex.Match(c, @"class\s*=\s*[""'][^""']*\bleft[""'][^>]*style\s*=\s*[""'][^""']*width\s*:\s*([\d.]+)%",
            RegexOptions.IgnoreCase);
        var halfPct = halfM.Success ? double.Parse(halfM.Groups[1].Value, inv) / 100.0 : 0.485;
        var halfW = halfPct * cw;
        var rightX = cx + cw - halfW;

        // left: bold-label / value rows at the UA 13 px line in 2px-padded rows
        double leftBottom = midTop;
        var dealerM = Regex.Match(c,
            @"<table\b[^>]*class\s*=\s*[""']dealer-structure-approved-table[""'][^>]*>",
            RegexOptions.IgnoreCase);
        if (dealerM.Success && BalancedInner(c, dealerM.Index + dealerM.Length, "table") is { } dInner)
        {
            var frameTop = BoxTitle("Dealer Structure", cx, midTop);
            var padSide = 11.25;                      // !important 15 px pads
            var tX = cx + 2 + padSide;
            var tW = halfW - 4 - 2 * padSide;
            var rowTop = frameTop + 2 + 11.25;
            var rowH = 15.0;                          // 2px pads + line + border
            var rows = new List<(string lbl, string val)>();
            foreach (Match trM in Regex.Matches(dInner, @"<tr\b[^>]*>(.*?)</tr>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var tds = Regex.Matches(trM.Groups[1].Value, @"<td\b[^>]*>(.*?)</td>",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (tds.Count >= 2)
                    rows.Add((Inner(tds[0].Groups[1].Value), Inner(tds[1].Groups[1].Value)));
            }
            var yRow = rowTop;
            foreach (var (lbl, val) in rows)
            {
                HLine(tX, tX + tW, yRow, bandBlue);
                EmitRun(lbl, bodyFs, true, tX + 11.25, yRow + Drop(bodyFs, rowH), null);
                EmitRun(val, bodyFs, false, tX + tW / 2 + 11.25, yRow + Drop(bodyFs, rowH), null);
                yRow += rowH;
            }
            HLine(tX, tX + tW, yRow, bandBlue);
            leftBottom = yRow + 15.0 + 2;             // 20 px !important bottom pad
            FrameRect(cx, frameTop, halfW, leftBottom - frameTop, frameGray, 1.5);
        }

        // right: Stipulations p-rows, then the Comments paragraph box
        double rightBottom = midTop;
        {
            var frameTop = BoxTitle("Stipulations", rightX, midTop);
            var padSide = 7.5;
            var stX = rightX + 2 + padSide;
            var stW = halfW - 4 - 2 * padSide;
            var stipM = Regex.Match(c, @"class\s*=\s*[""']stipulations-text[""'][^>]*>",
                RegexOptions.IgnoreCase);
            var yRow = frameTop + 2 + 11.25;
            if (stipM.Success && BalancedInner(c, stipM.Index + stipM.Length, "div") is { } stInner)
            {
                foreach (Match pM in Regex.Matches(stInner, @"<p\b[^>]*>(.*?)</p>",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline))
                {
                    var odd = pM.Value.Contains("odd", StringComparison.OrdinalIgnoreCase);
                    if (odd) HLine(stX, stX + stW, yRow, bandBlue);
                    EmitRun(Inner(pM.Groups[1].Value), bodyFs, false,
                        stX + 11.25, yRow + Drop(bodyFs, 15.0), null);
                    if (odd) HLine(stX, stX + stW, yRow + 15.0, bandBlue);
                    yRow += 15.0;
                }
            }
            var stipBottom = yRow + 7.5 + 2;
            FrameRect(rightX, frameTop, halfW, stipBottom - frameTop, frameGray, 1.5);

            // Comments box under it
            var cmTop = stipBottom + 11.25;
            var cmFrameTop = BoxTitle("Comments", rightX, cmTop);
            var cmM = Regex.Match(c, @"class\s*=\s*[""']approved-comments[^""']*[""']",
                RegexOptions.IgnoreCase);
            var cmText = "";
            var cmLineH = 12.75;                      // p line-height: 17px
            if (cmM.Success)
            {
                var pM = Regex.Match(c[cmM.Index..], @"<p\b[^>]*>(.*?)</p>",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (pM.Success) cmText = Inner(pM.Groups[1].Value);
            }
            var yCm = cmFrameTop + 2 + 11.25;
            foreach (var ln in MeasuredWordWrap(cmText, stW, face, bodyFs))
            {
                EmitRun(ln, bodyFs, false, rightX + 2 + padSide, yCm + Drop(bodyFs, cmLineH), null);
                yCm += cmLineH;
            }
            rightBottom = yCm + 7.5 + 2;
            FrameRect(rightX, cmFrameTop, halfW, rightBottom - cmFrameTop, frameGray, 1.5);
        }

        // ── Notification Disclosure (full width, after the clear) ──
        var discTop = Math.Max(leftBottom, rightBottom) + 11.25;
        {
            var frameTop = BoxTitle("Notification Disclosure", cx, discTop);
            var dM = Regex.Match(c, @"class\s*=\s*[""']notifications-disclosure[""']",
                RegexOptions.IgnoreCase);
            var dText = "";
            if (dM.Success)
            {
                var pM = Regex.Match(c[dM.Index..], @"<p\b[^>]*>(.*?)</p>",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (pM.Success) dText = Inner(pM.Groups[1].Value);
            }
            var yD = frameTop + 2 + 11.25;
            foreach (var ln in MeasuredWordWrap(dText, cw - 4 - 15, face, bodyFs))
            {
                EmitRun(ln, bodyFs, false, cx + 2 + 7.5, yD + Drop(bodyFs, lineH), null);
                yD += lineH;
            }
            var dBottom = yD + 7.5 + 2;
            FrameRect(cx, frameTop, cw, dBottom - frameTop, frameGray, 1.5);
            y = dBottom + 11.25;
        }

        // ── footer paragraph + the contact table (30/20/20/30 % columns,
        //    vertically-centred rows — the table opens the SECOND page) ──
        {
            var ftM = Regex.Match(c, @"class\s*=\s*[""']notifications-footer[""']",
                RegexOptions.IgnoreCase);
            var ftText = "";
            if (ftM.Success)
            {
                var pM = Regex.Match(c[ftM.Index..], @"<p\b[^>]*>(.*?)</p>",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (pM.Success) ftText = Inner(pM.Groups[1].Value);
            }
            var yF = y;   // the footer's 15 px top margin COLLAPSES with the
                          // disclosure boxSection's bottom margin (max-wise)
            foreach (var ln in MeasuredWordWrap(ftText, cw, face, bodyFs))
            {
                EmitRun(ln, bodyFs, false, cx, yF + Drop(bodyFs, lineH), null);
                yF += lineH;
            }
            yF += 7.5;                                // footer-information 10 px margin

            var fRows = infoTables.Count >= 3 ? infoTables[2] : null;
            if (fRows is not null)
            {
                // declared percent columns off the table markup
                var pcts = new List<double>();
                var ftTblM = Regex.Matches(c,
                    @"<table\b[^>]*class\s*=\s*[""']information-table[""'][^>]*>",
                    RegexOptions.IgnoreCase);
                if (ftTblM.Count >= 3
                    && BalancedInner(c, ftTblM[2].Index + ftTblM[2].Length, "table") is { } fInner)
                    foreach (Match tdM in Regex.Matches(fInner, @"<td\b[^>]*width\s*:\s*([\d.]+)%",
                        RegexOptions.IgnoreCase))
                        pcts.Add(double.Parse(tdM.Groups[1].Value, inv) / 100.0);
                while (pcts.Count < fRows.Count) pcts.Add(1.0 / Math.Max(1, fRows.Count));

                // wrap each cell at its column minus the 25 px right pad
                var colX = new double[fRows.Count];
                var xAcc = cx;
                for (var i = 0; i < fRows.Count; i++) { colX[i] = xAcc; xAcc += pcts[i] * cw; }
                var cellLines = new List<(string lbl, string val)[]>();
                var maxLines = 1;
                for (var i = 0; i < fRows.Count; i++)
                {
                    var (lbl, val) = fRows[i].Count > 0 ? fRows[i][0] : ("", "");
                    var avail = pcts[i] * cw - 18.75;
                    var lblW = Measure(lbl, true, bodyFs);
                    var wrapped = MeasuredWordWrap(val, avail - lblW, face, bodyFs);
                    var lines = new (string, string)[Math.Max(1, wrapped.Length)];
                    for (var k = 0; k < lines.Length; k++)
                        lines[k] = (k == 0 ? lbl : "",
                            // the value keeps its leading space after the label
                            k < wrapped.Length
                                ? (k == 0 && !wrapped[k].StartsWith(' ')
                                    ? " " + wrapped[k] : wrapped[k])
                                : "");
                    cellLines.Add(lines);
                    maxLines = Math.Max(maxLines, lines.Length);
                }
                var rowH = maxLines * lineH;
                if (yF + rowH > pageHeight - DnRightMarginPt)
                {
                    page = doc.Pages.Add(pageWidth, pageHeight);
                    EnsureFonts(page, docFontDict);
                    sb = new StringBuilder();
                    pageStreams.Add((page, sb));
                    yF = DnPage2TopPt;
                }
                for (var i = 0; i < cellLines.Count; i++)
                {
                    var lines = cellLines[i];
                    var yCell = yF + (rowH - lines.Length * lineH) / 2;
                    foreach (var (lbl, val) in lines)
                    {
                        if (lbl.Length > 0)
                            EmitLabelValue(lbl, val, bodyFs, colX[i], yCell + Drop(bodyFs, lineH));
                        else
                            EmitRun(val, bodyFs, false, colX[i], yCell + Drop(bodyFs, lineH), null);
                        yCell += lineH;
                    }
                }
            }
        }

        foreach (var (pg, ops) in pageStreams)
            pg.AddContentStream(Encoding.ASCII.GetBytes(ops.ToString() + "\n"));
        return doc;
    }

    private static string? BalancedInner(string html, int afterOpen, string tag)
    {
        var depth = 1;
        var rx = new Regex(@"<(/?)" + tag + @"\b[^>]*?(/?)>", RegexOptions.IgnoreCase);
        for (var m = rx.Match(html, afterOpen); m.Success; m = m.NextMatch())
        {
            if (m.Groups[2].Value == "/") continue;
            depth += m.Groups[1].Value == "/" ? -1 : 1;
            if (depth == 0) return html[afterOpen..m.Index];
        }
        return null;
    }

    /// <summary>A declaration's length in pt with em resolved at
    /// <paramref name="emPt"/> — null when the property is absent.</summary>
    private static double? CssEmLen(string decl, string prop, double emPt)
    {
        var m = Regex.Match(decl, @"(?<![-\w])" + Regex.Escape(prop) + @"\s*:\s*(-?[\d.]+)\s*(em|px|pt)?",
            RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var v = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        return m.Groups[2].Value.ToLowerInvariant() switch
        {
            "em" => v * emPt,
            "px" => v * 0.75,
            _ => v,
        };
    }

    /// <summary>Attribute value out of a single tag's markup — null when absent.</summary>
    private static string? AttrValue(string tagMarkup, string attr)
    {
        var m = Regex.Match(tagMarkup,
            attr + @"\s*=\s*(?:""([^""]*)""|'([^']*)')", RegexOptions.IgnoreCase);
        return m.Success ? (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value) : null;
    }
}

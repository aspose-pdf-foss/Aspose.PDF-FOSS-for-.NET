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
    // value rows. The expected render draws the card with the real body face at
    // the px-derived sizes, the collapsed 2px frame around both cells, and the
    // nested grid's 75%/25% columns.
    //
    // Geometry (measured unless derived):
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
            ?? Color.FromRgbBytes(0x4E, 0x58, 0x9E);

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
    // container's line. The expected render applies the whole chain: line x =
    // page margin + root padding-left + one `*` padding per open container +
    // the leaf's own (negative) margin-left, every em resolved at the root's
    // keyword size.
    //
    // Vertical model (formula-exact against the measured output): line box =
    // MetricLineHeight(fs, winSum) (Verdana small: 16px = 12pt), baseline =
    // MetricBaselineDrop; the first line opens at top margin + the root's
    // inline padding-top.

    // The untouched page margins (the converter's caller-facing
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
                ? ParseCssColor(colM.Groups[1].Value.Trim()) ?? Color.FromArgb(0, 0, 0)
                : Color.FromArgb(0, 0, 0);
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
                ? ParseCssColor(dc.Groups[1].Value.Trim()) ?? Color.FromArgb(0, 0, 0)
                : Color.FromArgb(0, 0, 0);
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
    // (the converter FETCHES the css and its TTF programs, exactly as it
    // fetches remote images), the 600 px min-width body sets the 450 pt
    // column (page = 90 + UA 6 + 450 + 90 = 636), the float header pairs the
    // #5d80ba info panel with the fetched logo, and each `.contract` block —
    // h2 label line, right-floated description, orange collapsed table —
    // moves WHOLE to the next page under its page-break-inside: avoid.

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

    // ── Resume-builder document sheets ──────────────────────────────────────
    // The LiveCareer resume export: a `div#document` whose dynamic stylesheet
    // resolves the sheet (11 pt Palatino Linotype at the 13 pt line, 30 pt
    // horizontal padding, 552 pt single column, 23/31 name, 10/12 address,
    // 13/15 section titles). The expected output emits each page's SECTION
    // TITLES first, then the name/address group, then the body — the text
    // fragments keep that order, and every markup span is its own fragment.
    // List items seat their marker at +12.21 and their text at +23 inside
    // the column (30 + 10 pt ul + 13 pt li = the asserted x 53).

    // ── Angular audit-report exports ────────────────────────────────────────
    // The audit finding sheet: an Angular app dump whose content lives under
    // `.report-container` (40 px padded white card inside the 17 px
    // main-content-middle), headed by h1–h4 at 14 px bold with left-line
    // dashes, then a label table — 9 pt bold th labels in a 50 pt column, a
    // dot cell, and content cells. Text inside the <gt-editor-content> custom
    // element does NOT inherit the table's 12 px size (the expected render's
    // inheritance breaks at the unknown element): it draws at the UA 16 px
    // base, italic via the sheet's span[lang] rule, #333 ink. The measured
    // constants carry their derivations.

    // ── Decision-notification letters ───────────────────────────────────────
    // A TCI notifications template: an all-inline-styled sheet under a centred
    // `basis` container (duplicate width declarations — the LAST wins), a float
    // header (broken logo/QR images keeping their divs' widths), bordered
    // `information-table`s with bold label spans riding their value lines,
    // `boxSection` panels whose white-backed titles sit ON the 2 px frame, and
    // a 48.5 % float column pair. All seats derive from the inline box model at
    // the UA 13 px body line; the measured constants below carry their
    // derivations.

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

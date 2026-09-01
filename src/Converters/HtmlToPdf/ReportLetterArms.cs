using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
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
        // …plus ~4 pt headroom over our Arial advances (the expected
        // lines are kerned slightly tighter than our advance sums)
        // measured: the widest expected line runs to 577.7
        var contentRight = 578.5;

        var inkBody = ParseCssColor("#355154") ?? Color.FromArgb(53, 81, 84);
        var inkEditor = ParseCssColor("#333333") ?? Color.FromArgb(51, 51, 51);
        var inkAbbr = ParseCssColor("#008080") ?? Color.FromArgb(0, 128, 128);
        var inkCode = ParseCssColor("#0782C1") ?? Color.FromArgb(7, 130, 193);
        var railGray = ParseCssColor("#e4e5e9") ?? Color.FromArgb(228, 229, 233);

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
                        ParseCssColor("#617778") ?? Color.FromArgb(97, 119, 120));
                    EmitRun(bTxt, ArBadgeFsPt, "", bX + 2.2, rowTop + 10.9,
                        Color.FromArgb(255, 255, 255));
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

        var ink = ParseCssColor("#5c5c5c") ?? Color.FromArgb(92, 92, 92);
        var bandBlue = ParseCssColor("#d3e0ec") ?? Color.FromArgb(211, 224, 236);
        var borderLite = ParseCssColor("#f3f3f3") ?? Color.FromArgb(243, 243, 243);
        var frameGray = ParseCssColor("#e8e8e8") ?? Color.FromArgb(232, 232, 232);

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
                Color.FromArgb(255, 255, 255));
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
}

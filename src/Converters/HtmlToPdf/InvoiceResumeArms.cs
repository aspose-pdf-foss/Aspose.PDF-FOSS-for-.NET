using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
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

        var ink = ParseCssColor("#627881") ?? Color.FromArgb(98, 120, 129);
        var blue = ParseCssColor("#5d80ba") ?? Color.FromArgb(93, 128, 186);
        var orange = ParseCssColor("#f69a1b") ?? Color.FromArgb(246, 154, 27);
        var white = Color.FromArgb(255, 255, 255);

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
        var black = Color.FromArgb(0, 0, 0);
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
}

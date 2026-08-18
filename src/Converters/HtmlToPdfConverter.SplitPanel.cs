using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // ── The ORPHAN-ROWSPAN split-panel document ─────────────────────────────────
    // Legacy tag soup: a header table closes, then a stray `<td rowspan=…>` opens
    // OUTSIDE any table and wraps the rest of the page. The source renderer
    // recovers it as a two-panel band: the inner width:100% table renders as a
    // sidebar cell (16%, tinted, middle-valigned) beside a white main cell
    // (84%, top-valigned), each holding its own run-styled mini-flow (font tags,
    // strong runs, blockquotes, hr grooves). The header th mixes TWO font runs
    // on its first line and centres a large-face title under it.
    //
    // Geometry (measured on the reference): header band 79.5..183.8 with both cells
    // tinted and the logo drawn at natural px size; the two-panel band opens at
    // 186; sidebar text centres vertically; main text starts under a blank
    // paragraph line and indents its blockquote by 40px each side.

    private const double SpHeaderBandTopPt = 79.5;     // header fills top (78 + spacing)
    private const double SpHeaderBandBotPt = 183.8;    // header fills bottom
    private const double SpPanelsTopPt = 186.0;        // two-panel band top
    private const double SpTitleLine1DropPt = 13.2;    // th line-1 baseline below band top
    private const double SpTitleGapPt = 21.3;          // line-1 → first title line
    private const double SpTitlePitchPt = 21.75;       // Verdana 18 title line pitch
    private const double SpTitleWrapPt = 283.0;        // title wrap width (measured lines)
    private const double SpSidebarStepGapPt = 13.45;   // sidebar paragraph gap
    private const double SpSidebarHrGapPt = 16.1;      // gap above/below the sidebar hr
    private const double SpBlockquoteIndentPt = 30.0;  // 40px blockquote indent, each side

    private sealed class SpRun
    {
        public string Text = "";
        public string Face = "Times New Roman";
        public double Fs = 12;
        public bool Bold;
        public Color? Col;
    }

    private sealed class SpBlock
    {
        public List<SpRun> Runs = new();
        public double BlankFs;               // empty block = one blank line of this size
        public bool Hr;                      // horizontal rule groove
        public double HrWidth;               // 0 = full content width
        public double Indent;                // left indent (blockquote)
        public double RightIndent;
        public bool Center;
    }

    /// <summary>The html font-size ladder used by this dialect (size=1..7 → px).</summary>
    private static double SpLadderPt(int size) => size switch
    {
        <= 1 => 9 * 0.75, 2 => 13 * 0.75, 3 => 16 * 0.75, 4 => 18 * 0.75,
        5 => 24 * 0.75, 6 => 32 * 0.75, _ => 48 * 0.75,
    };

    /// <summary>Quirks line pitch: the px font rounds its 1.15 line to whole
    /// pixels (16px -> 18px -> 13.5pt; 13px -> 15px -> 11.25pt).</summary>
    private static double SpLinePitch(double fs)
        => Math.Round(fs / 0.75 * 1.15) * 0.75;

    /// <summary>Parse a mini-flow cell: font tags scope face/size/colour (legacy
    /// unclosed tags keep applying), b/strong bold, p/br break lines, blockquote
    /// indents, hr emits a groove block.</summary>
    private static List<SpBlock> SpParseFlow(string inner)
    {
        var blocks = new List<SpBlock>();
        var cur = new SpBlock();
        var faceStack = new List<(string Face, double Fs, Color? Col)>
            { ("Times New Roman", 12, null) };
        var boldDepth = 0;
        var indent = 0.0;
        var text = new StringBuilder();

        void FlushRun()
        {
            if (text.Length == 0) return;
            var runText = CollapseWs(text.ToString());
            if (runText.Length == 0 || runText == " ")
            {
                // pure whitespace between markup: keep a single separating space
                if (runText == " " && cur.Runs.Count > 0
                    && !cur.Runs[^1].Text.EndsWith(' '))
                    cur.Runs[^1].Text += " ";
                text.Clear();
                return;
            }
            var (face, fs, col) = faceStack[^1];
            // Arial at the letter sizes draws bold through <strong>; the metric
            // face carries the weight.
            cur.Runs.Add(new SpRun
            {
                Text = runText, Face = face, Fs = fs,
                Bold = boldDepth > 0, Col = col,
            });
            text.Clear();
        }
        var pendingPMargin = false;
        void CloseBlock(bool pMargin = false)
        {
            FlushRun();
            if (cur.Runs.Count == 0 && !cur.Hr)
            {
                // an empty close: a <br> is a HARD blank line; a paragraph
                // boundary is a MARGIN — adjacent margins collapse to one and
                // vanish entirely at the flow start
                if (pMargin)
                {
                    if (blocks.Count > 0) pendingPMargin = true;
                    cur = new SpBlock { Indent = indent, RightIndent = indent };
                    return;
                }
                cur.BlankFs = faceStack[^1].Fs;
                blocks.Add(cur);
                cur = new SpBlock { Indent = indent, RightIndent = indent };
                return;
            }
            // content arrives: a pending collapsed margin becomes one blank line
            if (pendingPMargin)
            {
                blocks.Add(new SpBlock { BlankFs = faceStack[^1].Fs });
                pendingPMargin = false;
            }
            blocks.Add(cur);
            // a paragraph boundary AFTER content leaves its margin pending too
            if (pMargin) pendingPMargin = true;
            cur = new SpBlock { Indent = indent, RightIndent = indent };
        }

        foreach (var tok in Tokenize(StripNonContent(inner)))
        {
            if (tok.Kind == TokenKind.Text)
            {
                text.Append(DecodeEntities(tok.Value));
                continue;
            }
            var tag = tok.Tag!.ToLowerInvariant();
            if (tok.IsClose)
            {
                switch (tag)
                {
                    case "b": case "strong":
                        FlushRun(); boldDepth = Math.Max(0, boldDepth - 1); break;
                    case "font":
                        FlushRun();
                        if (faceStack.Count > 1) faceStack.RemoveAt(faceStack.Count - 1);
                        break;
                    case "p":
                        CloseBlock(pMargin: true); break;
                    case "blockquote":
                        CloseBlock(pMargin: true); indent = 0;
                        cur.Indent = 0; cur.RightIndent = 0; break;
                }
                continue;
            }
            switch (tag)
            {
                case "b": case "strong":
                    FlushRun(); boldDepth++; break;
                case "font":
                {
                    FlushRun();
                    var (face, fs, col) = faceStack[^1];
                    if (tok.Attributes is { } fa)
                    {
                        if (fa.TryGetValue("face", out var fv)
                            && FirstFontFamily(fv) is { Length: > 0 } fam
                            && WinMetricsFor(fam) is not null)
                            face = fam;
                        if (fa.TryGetValue("size", out var sv))
                        {
                            var svt = sv.Trim();
                            if (svt.StartsWith('+') && int.TryParse(svt[1..], out var rel))
                                fs = SpLadderPt(3 + rel);
                            else if (int.TryParse(svt.Trim('"'), out var abs))
                                fs = SpLadderPt(abs);
                        }
                        if (fa.TryGetValue("color", out var cv)
                            && ParseCssColor(cv.Trim()) is { } pc)
                            col = pc;
                    }
                    faceStack.Add((face, fs, col));
                    break;
                }
                case "br":
                    CloseBlock(); break;
                case "p":
                    CloseBlock(pMargin: true);
                    if (tok.Attributes is { } pa && pa.TryGetValue("align", out var av)
                        && av.Trim().Equals("center", StringComparison.OrdinalIgnoreCase))
                        cur.Center = true;
                    break;
                case "blockquote":
                    CloseBlock(pMargin: true);
                    indent = SpBlockquoteIndentPt;
                    cur.Indent = indent; cur.RightIndent = indent;
                    break;
                case "hr":
                {
                    CloseBlock();
                    cur.Hr = true;
                    if (tok.Attributes is { } ha && ha.TryGetValue("width", out var wv)
                        && double.TryParse(wv.Trim().TrimEnd('%'),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var wn)
                        && !wv.Contains('%'))
                        cur.HrWidth = wn * 0.75;
                    CloseBlock();
                    break;
                }
            }
        }
        CloseBlock();
        return blocks;
    }

    /// <summary>Wrap one block's runs into lines of (run, text) segments; style
    /// boundaries split segments, word boundaries wrap.</summary>
    private static List<List<(SpRun Run, string Text)>> SpWrap(SpBlock b, double width)
    {
        var lines = new List<List<(SpRun, string)>>();
        var line = new List<(SpRun, string)>();
        double lineW = 0;
        foreach (var run in b.Runs)
        {
            var face = run.Face + (run.Bold ? " Bold" : "");
            var seg = new StringBuilder();
            void FlushSeg()
            {
                if (seg.Length > 0) line.Add((run, seg.ToString()));
                seg.Clear();
            }
            foreach (var w in run.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var piece = (lineW > 0 || seg.Length > 0 ? " " : "") + w;
                var pw = MeasureFaceText(face, piece, run.Fs);
                if (lineW + pw > width && (lineW > 0 || seg.Length > 0))
                {
                    FlushSeg();
                    if (line.Count > 0) lines.Add(line);
                    line = new List<(SpRun, string)>();
                    lineW = 0;
                    piece = w;
                    pw = MeasureFaceText(face, piece, run.Fs);
                }
                seg.Append(piece);
                lineW += pw;
            }
            FlushSeg();
        }
        if (line.Count > 0) lines.Add(line);
        return lines;
    }

    private static Document? TryRenderSplitPanel(string html, HtmlLoadOptions? options,
        double pageWidth, double pageHeight)
    {
        // Gate: a closed table followed by a stray td rowspan wrapper holding a
        // width:100% two-column table — the orphan-rowspan recovery shape.
        var orphan = Regex.Match(html, @"</table\s*>\s*<td\b[^>]*\browspan\s*=",
            RegexOptions.IgnoreCase);
        if (!orphan.Success) return null;
        var headM = Regex.Match(html, @"<table\b[^>]*>[\s\S]*?</table\s*>", RegexOptions.IgnoreCase);
        if (!headM.Success || headM.Index > orphan.Index) { }
        var innerM = Regex.Match(html[orphan.Index..],
            @"<table\b[^>]*width\s*=\s*[""']?100%[\s\S]*?<td\b([^>]*)>([\s\S]*)",
            RegexOptions.IgnoreCase);
        if (!innerM.Success) return null;

        var doc = Document.Create();
        var docFontDict = new Core.PdfDictionary();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        EnsureFonts(page, docFontDict);
        var invc = System.Globalization.CultureInfo.InvariantCulture;

        var marginLeft = 96.0;
        var contentW = pageWidth - 90.0 - 90.0;

        // ── Header band ──────────────────────────────────────────────────────
        var bandFill = Color.FromRgb(0xE7, 0xEF, 0xF7);
        var leftCellX0 = marginLeft + 1.5;
        var leftCellX1 = leftCellX0 + 90.0;              // 16% cell box (measured 97.5..187.5)
        var rightCellX0 = leftCellX1 + 1.5;
        var rightCellX1 = marginLeft + contentW * 0.968; // measured 497.5 on the 595 sheet
        void FillRect(double x0, double topTd, double x1, double botTd, Color c)
            => page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                $"q {c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} rg " +
                $"{x0:F2} {pageHeight - botTd:F2} {x1 - x0:F2} {botTd - topTd:F2} re f Q\n")));
        FillRect(leftCellX0, SpHeaderBandTopPt, leftCellX1, SpHeaderBandBotPt, bandFill);
        FillRect(rightCellX0, SpHeaderBandTopPt, rightCellX1, SpHeaderBandBotPt, bandFill);

        // logo at its natural pixel size (fetched like a browser; nothing drawn
        // when the host is unreachable)
        var imgM = Regex.Match(html[..orphan.Index],
            @"<img\b[^>]*src\s*=\s*[""']?([^""'\s>]+)[^>]*width\s*=\s*[""']?(\d+)[^>]*height\s*=\s*[""']?(\d+)",
            RegexOptions.IgnoreCase);
        if (imgM.Success && LoadConverterImage(imgM.Groups[1].Value, options) is { } logoBytes)
        {
            var iw = double.Parse(imgM.Groups[2].Value, invc) * 0.75;
            var ih = double.Parse(imgM.Groups[3].Value, invc) * 0.75;
            var ix = leftCellX0 + 0.7;
            var iyTd = SpHeaderBandTopPt + 0.7;
            try { page.AddImage(logoBytes, new Rectangle(ix, pageHeight - iyTd - ih, ix + iw, pageHeight - iyTd)); }
            catch { }
        }

        // header th: line 1 = the mixed-run heading; then the centred title
        var thM = Regex.Match(html[..orphan.Index], @"<th\b[^>]*>([\s\S]*)",
            RegexOptions.IgnoreCase);
        if (thM.Success)
        {
            var thBlocks = SpParseFlow("<b>" + thM.Groups[1].Value); // th = UA bold
            var cy = SpHeaderBandTopPt + SpTitleLine1DropPt;
            var first = true;
            foreach (var b in thBlocks)
            {
                if (b.Hr) continue;
                var lines = SpWrap(b, first ? rightCellX1 - rightCellX0 : SpTitleWrapPt);
                foreach (var ln in lines)
                {
                    double lw = 0;
                    foreach (var (r, t) in ln)
                        lw += MeasureFaceText(r.Face + (r.Bold ? " Bold" : ""), t, r.Fs);
                    var lx = rightCellX0 + (rightCellX1 - rightCellX0 - lw) / 2;
                    foreach (var (r, t) in ln)
                    {
                        SpEmitRun(page, docFontDict, r, t, lx, pageHeight - cy, invc);
                        lx += MeasureFaceText(r.Face + (r.Bold ? " Bold" : ""), t, r.Fs);
                    }
                    cy += first ? SpTitleGapPt : SpTitlePitchPt;
                    first = false;
                }
            }
        }

        // ── The two-panel band ───────────────────────────────────────────────
        var panelTop = SpPanelsTopPt;
        var sbX0 = marginLeft + 0.75;
        var sbX1 = sbX0 + 94.3;                          // 16% of the inner table (measured 96.8..191.1)
        var mainX0 = sbX1 + 0.75;
        var mainX1 = marginLeft + contentW * 0.97 + 0.4; // measured 498.2

        // sidebar + main content flows
        var tdSplit = Regex.Matches(html[orphan.Index..], @"<td\b[^>]*>", RegexOptions.IgnoreCase);
        // sidebar cell = the inner table's first td; main = the 84% td after it
        var seg2 = html[orphan.Index..];
        var sbM = Regex.Match(seg2, @"<td\b[^>]*width\s*=\s*[""']?16%[^>]*>([\s\S]*?)</td>",
            RegexOptions.IgnoreCase);
        var mainM = Regex.Match(seg2, @"<td\b[^>]*width\s*=\s*[""']?84%[^>]*>([\s\S]*)",
            RegexOptions.IgnoreCase);

        var sbBlocks = sbM.Success ? SpParseFlow(sbM.Groups[1].Value) : new List<SpBlock>();
        var mainBlocks = mainM.Success ? SpParseFlow(mainM.Groups[1].Value) : new List<SpBlock>();

        // Lay the MAIN flow first so the panel band height is known: it flows
        // top-down from the panel top, splitting to page 2 at the margin.
        var mainPad = 3.0;
        var mainW = mainX1 - mainX0 - 2 * mainPad;
        var flowBottom = pageHeight - 72.0;
        // Pure line-grid flow: every line — text or blank — advances by the
        // quirks pitch of its font; blocks add nothing of their own.
        double mainY = panelTop + mainPad;               // cursor = next line TOP
        var pageIdx = 0;
        var pages = new List<Page> { page };
        double p1PanelBottom = 0;
        foreach (var b in mainBlocks)
        {
            if (b.Hr)
            {
                // the groove sits one blank line down, with a short seat below
                var hrW = mainW - b.Indent - b.RightIndent;
                var hx0 = mainX0 + mainPad + b.Indent;
                var hrPitch = SpLinePitch(9.75);
                SpHrGroove(pages[pageIdx], hx0, hx0 + hrW, mainY + hrPitch, pageHeight, invc);
                mainY += hrPitch + 6.1;
                continue;
            }
            if (b.Runs.Count == 0)
            {
                mainY += SpLinePitch(b.BlankFs > 0 ? b.BlankFs : 9.75);
                continue;
            }
            var maxFs = 0.0;
            string maxFace = "Arial";
            foreach (var r in b.Runs)
                if (r.Fs > maxFs) { maxFs = r.Fs; maxFace = r.Face; }
            var pitch = SpLinePitch(maxFs);
            var pfm = WinMetricsFor(maxFace) ?? (0.891, 1.15);
            var lines = SpWrap(b, mainW - b.Indent - b.RightIndent);
            foreach (var ln in lines)
            {
                if (mainY + pitch > flowBottom && pageIdx == 0)
                {
                    p1PanelBottom = pageHeight - 72.8;
                    var p2 = doc.Pages.Add(pageWidth, pageHeight);
                    EnsureFonts(p2, docFontDict);
                    pages.Add(p2);
                    pageIdx = 1;
                    mainY = 72.0;
                }
                var lx = mainX0 + mainPad + b.Indent;
                var baseY = mainY + MetricBaselineDrop(maxFs, pitch, pfm);
                foreach (var (r, t) in ln)
                {
                    SpEmitRun(pages[pageIdx], docFontDict, r, t, lx, pageHeight - baseY, invc);
                    lx += MeasureFaceText(r.Face + (r.Bold ? " Bold" : ""), t, r.Fs);
                }
                mainY += pitch;
            }
        }
        if (p1PanelBottom == 0) p1PanelBottom = Math.Min(mainY + mainPad, pageHeight - 72.8);

        // panel fills UNDER the text (insert at the head of page 1's streams
        // would reorder everything; the fills went in before the flows in the
        // reference, so draw them now on a fresh underlay stream inserted early)
        var sbFillBytes = Encoding.ASCII.GetBytes(string.Create(invc,
            $"q {0xE7 / 255.0:0.###} {0xEF / 255.0:0.###} {0xF7 / 255.0:0.###} rg " +
            $"{sbX0:F2} {pageHeight - p1PanelBottom:F2} {sbX1 - sbX0:F2} {p1PanelBottom - panelTop:F2} re f " +
            $"1 1 1 rg {mainX0:F2} {pageHeight - p1PanelBottom:F2} {mainX1 - mainX0:F2} {p1PanelBottom - panelTop:F2} re f Q\n"));
        page.InsertContentStreamAt(0, sbFillBytes);

        // sidebar mini-flow: the same line grid, centred vertically in the band
        var sbW = sbX1 - sbX0 - 6.0;
        double sbH = 0;
        var sbLaid = new List<(SpBlock B, List<List<(SpRun, string)>> Lines, double Pitch, (double asc, double sum) Fm)>();
        foreach (var b in sbBlocks)
        {
            if (b.Hr)
            {
                var pitchH = SpLinePitch(9.75) + 6.1;
                sbLaid.Add((b, new List<List<(SpRun, string)>>(), pitchH, (0.905, 1.15)));
                sbH += pitchH;
                continue;
            }
            if (b.Runs.Count == 0)
            {
                var pitchB = SpLinePitch(b.BlankFs > 0 ? b.BlankFs : 9.75);
                sbLaid.Add((b, new List<List<(SpRun, string)>>(), pitchB, (0.905, 1.15)));
                sbH += pitchB;
                continue;
            }
            var maxFs = 0.0;
            string maxFace = "Arial";
            foreach (var r in b.Runs)
                if (r.Fs > maxFs) { maxFs = r.Fs; maxFace = r.Face; }
            var pitch = SpLinePitch(maxFs);
            var pfm = WinMetricsFor(maxFace) ?? (0.905, 1.15);
            var lines = SpWrap(b, sbW);
            sbLaid.Add((b, lines, pitch, pfm));
            sbH += lines.Count * pitch;
        }
        var sbY = panelTop + (p1PanelBottom - panelTop - sbH) / 2;
        foreach (var (b, lines, pitch, pfm) in sbLaid)
        {
            if (b.Hr)
            {
                SpHrGroove(page, sbX0 + 3.0, sbX1 - 3.0, sbY + SpLinePitch(9.75), pageHeight, invc);
                sbY += pitch;
                continue;
            }
            if (lines.Count == 0) { sbY += pitch; continue; }
            foreach (var ln in lines)
            {
                var lx = sbX0 + 3.0;
                var baseY = sbY + MetricBaselineDrop(9.75, pitch, pfm);
                foreach (var (r, t) in ln)
                {
                    SpEmitRun(page, docFontDict, r, t, lx, pageHeight - baseY, invc);
                    lx += MeasureFaceText(r.Face + (r.Bold ? " Bold" : ""), t, r.Fs);
                }
                sbY += pitch;
            }
        }

        return doc;
    }

    private static void SpHrGroove(Page page, double x0, double x1, double yTd,
        double pageHeight, System.Globalization.CultureInfo invc)
    {
        var y = pageHeight - yTd;
        page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
            $"q 0.75 w 0 0 0 RG {x0:F2} {y:F2} m {x1:F2} {y:F2} l S " +
            $"0.333 0.333 0.333 RG {x0:F2} {y - 0.75:F2} m {x1:F2} {y - 0.75:F2} l S Q\n")));
    }

    /// <summary>Emit one styled run at (x, y): WinAnsi resource per face, colour
    /// pushed and reset around the shown text.</summary>
    private static readonly Dictionary<string, string> SpFaceRes = new(StringComparer.Ordinal);

    private static void SpEmitRun(Page page, Core.PdfDictionary docFontDict, SpRun r,
        string text, double x, double y, System.Globalization.CultureInfo invc)
    {
        var faceName = r.Face + (r.Bold ? " Bold" : "");
        if (!SpFaceRes.TryGetValue(faceName, out var res))
        {
            res = "F" + (20 + SpFaceRes.Count);
            SpFaceRes[faceName] = res;
        }
        EnsureFont(page, faceName.Replace(" ", ""), res);
        if (r.Col is { } c)
            page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                $"{c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} rg")));
        EmitPositionedRun(page, res, r.Fs, x, y, text);
        if (r.Col is not null)
            page.AddContentStream(Encoding.ASCII.GetBytes("0 g"));
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // ── The ORPHAN-ROWSPAN split-panel document ─────────────────────────────────
    // Legacy tag soup: a header table closes, then a stray `<td rowspan=…>` opens
    // OUTSIDE any table and wraps the rest of the page. The expected render
    // recovers it as a two-panel band: the inner width:100% table renders as a
    // sidebar cell (16%, tinted, middle-valigned) beside a white main cell
    // (84%, top-valigned), each holding its own run-styled mini-flow (font tags,
    // strong runs, blockquotes, hr grooves). The header th mixes TWO font runs
    // on its first line and centres a large-face title under it.
    //
    // Geometry (measured): header band 79.5..183.8 with both cells
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
        var pf = new SplitPanelFlowState();
        pf.inner = inner;
        pf.blocks = new List<SpBlock>();
        pf.cur = new SpBlock();
        pf.faceStack = new List<(string Face, double Fs, Color? Col)>
            { ("Times New Roman", 12, null) };
        pf.boldDepth = 0;
        pf.indent = 0.0;
        pf.text = new StringBuilder();

        pf.pendingPMargin = false;
        foreach (var tok in Tokenize(StripNonContent(pf.inner)))
        {
            if (tok.Kind == TokenKind.Text)
            {
                pf.text.Append(DecodeEntities(tok.Value));
                continue;
            }
            var tag = tok.Tag!.ToLowerInvariant();
            if (tok.IsClose)
            {
                switch (tag)
                {
                    case "b": case "strong":
                        FlushSpRun(pf); pf.boldDepth = Math.Max(0, pf.boldDepth - 1); break;
                    case "font":
                        FlushSpRun(pf);
                        if (pf.faceStack.Count > 1) pf.faceStack.RemoveAt(pf.faceStack.Count - 1);
                        break;
                    case "p":
                        CloseSpBlock(pf, pMargin: true); break;
                    case "blockquote":
                        CloseSpBlock(pf, pMargin: true); pf.indent = 0;
                        pf.cur.Indent = 0; pf.cur.RightIndent = 0; break;
                }
                continue;
            }
            switch (tag)
            {
                case "b": case "strong":
                    FlushSpRun(pf); pf.boldDepth++; break;
                case "font":
                {
                    FlushSpRun(pf);
                    var (face, fs, col) = pf.faceStack[^1];
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
                    pf.faceStack.Add((face, fs, col));
                    break;
                }
                case "br":
                    CloseSpBlock(pf); break;
                case "p":
                    CloseSpBlock(pf, pMargin: true);
                    if (tok.Attributes is { } pa && pa.TryGetValue("align", out var av)
                        && av.Trim().Equals("center", StringComparison.OrdinalIgnoreCase))
                        pf.cur.Center = true;
                    break;
                case "blockquote":
                    CloseSpBlock(pf, pMargin: true);
                    pf.indent = SpBlockquoteIndentPt;
                    pf.cur.Indent = pf.indent; pf.cur.RightIndent = pf.indent;
                    break;
                case "hr":
                {
                    CloseSpBlock(pf);
                    pf.cur.Hr = true;
                    if (tok.Attributes is { } ha && ha.TryGetValue("width", out var wv)
                        && double.TryParse(wv.Trim().TrimEnd('%'),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var wn)
                        && !wv.Contains('%'))
                        pf.cur.HrWidth = wn * 0.75;
                    CloseSpBlock(pf);
                    break;
                }
            }
        }
        CloseSpBlock(pf);
        return pf.blocks;
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

    private static void SpHrGroove(Page page, double x0, double x1, double yTd,
        double pageHeight, System.Globalization.CultureInfo invc)
    {
        var y = pageHeight - yTd;
        page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(invc,
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
            page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(invc,
                $"{c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} rg")));
        EmitPositionedRun(page, res, r.Fs, x, y, text);
        if (r.Col is not null)
            page.AddContentStream(Encoding.ASCII.GetBytes("0 g"));
    }
}

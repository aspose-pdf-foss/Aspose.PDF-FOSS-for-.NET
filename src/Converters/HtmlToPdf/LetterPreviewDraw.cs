using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // The letter-preview text model: Tahoma runs on the metric line box (the line is the
    // face's win height rounded to whole pixels), a greedy wrap on kerned widths that breaks
    // at spaces and after a hyphen (an nbsp never breaks), a word wider than its box broken
    // by characters only where the sheet says word-wrap: break-word.
    private const string LpFace = "Tahoma";
    private const string LpBoldFace = "Tahoma-Bold";
    private const string LpFaceRes = "FT";
    private const string LpBoldRes = "FTB";
    private const double LpRuleW = 0.75;               // every 1px rule
    private const string LpHrTopRgb = "0 0 0";         // the UA hr: black over grey
    private const string LpHrBottomRgb = "0.333 0.333 0.333";

    private static readonly Regex LpTokenRx = new("[^ \\t\\r\\n]+[ \\t\\r\\n]*|[ \\t\\r\\n]+", RegexOptions.Compiled);

    private static double LpLineH(LetterPreviewState lp, double fs) => MetricLineHeight(fs, lp.tm.sum);

    private static double LpDrop(LetterPreviewState lp, double fs) => MetricBaselineDrop(fs, LpLineH(lp, fs), lp.tm);

    private static double LpMeasure(string face, string s, double fs) => s.Length == 0 ? 0 : MeasureFaceText(face, s, fs);

    private static string LpRes(string face) => face == LpBoldFace ? LpBoldRes : LpFaceRes;

    private static void LpRegisterFonts(Page page)
    {
        EnsureFonts(page);
        EnsureFont(page, LpFace, LpFaceRes);
        EnsureFont(page, LpBoldFace, LpBoldRes);
    }

    private static void LpFlush(LetterPreviewState lp)
    {
        if (lp.sb.Length == 0) return;
        lp.page.AddContentStream(Encoding.ASCII.GetBytes(lp.sb.ToString()));
        lp.sb.Clear();
    }

    private static void LpEmit(LetterPreviewState lp, LpRun r, double x, double baseTd, string rgb = "0 0 0")
    {
        if (r.Text.Length == 0) return;
        lp.sb.Append(Compat.Format(lp.invc,
            $"BT {rgb} rg /{LpRes(r.Face)} {r.Fs:0.###} Tf 1 0 0 1 {x:0.###} {lp.pageHeight - baseTd:0.###} Tm ({EscapePdfString(r.Text)}) Tj ET\n"));
    }

    private static void LpHLine(LetterPreviewState lp, double x0, double x1, double yTd, string rgb = "0 0 0")
        => lp.sb.Append(Compat.Format(lp.invc,
            $"q {rgb} RG {LpRuleW:0.##} w {x0:0.###} {lp.pageHeight - yTd:0.###} m {x1:0.###} {lp.pageHeight - yTd:0.###} l S Q\n"));

    private static void LpVLine(LetterPreviewState lp, double x, double y0Td, double y1Td, string rgb = "0 0 0")
        => lp.sb.Append(Compat.Format(lp.invc,
            $"q {rgb} RG {LpRuleW:0.##} w {x:0.###} {lp.pageHeight - y0Td:0.###} m {x:0.###} {lp.pageHeight - y1Td:0.###} l S Q\n"));

    /// <summary>The UA hr: a 1px black rule over a 1px grey one, with the end ticks.</summary>
    private static void LpHr(LetterPreviewState lp, double x0, double x1, double topTd)
    {
        LpHLine(lp, x0, x1, topTd + LpRuleW / 2, LpHrTopRgb);
        LpHLine(lp, x0, x1, topTd + LpRuleW * 1.5, LpHrBottomRgb);
        LpVLine(lp, x0 + LpRuleW / 2, topTd, topTd + 2 * LpRuleW, LpHrTopRgb);
        LpVLine(lp, x1 - LpRuleW / 2, topTd, topTd + 2 * LpRuleW, LpHrBottomRgb);
    }

    private static LpRun LpMakeRun(string text, bool bold, double fs)
        => new LpRun { Face = bold ? LpBoldFace : LpFace, Fs = fs, Text = text };

    /// <summary>Plain text of an HTML fragment: tags dropped, entities decoded, ASCII
    /// whitespace collapsed (an nbsp stays).</summary>
    private static string LpFlat(string frag)
        => Regex.Replace(DecodeEntities(Regex.Replace(frag, "<[^>]+>", "")), "[ \\t\\r\\n]+", " ").Trim(' ');

    /// <summary>The br-separated lines of a fragment; the last br ends its line without
    /// opening another (every earlier trailing br is an empty line - measured: five closing
    /// brs draw four blank lines).</summary>
    private static List<string> LpSegments(string frag)
    {
        var parts = Regex.Split(frag, "<br\\s*/?>", RegexOptions.IgnoreCase);
        var segs = new List<string>();
        foreach (var p in parts) segs.Add(LpFlat(p));
        if (segs.Count > 1 && segs[^1].Length == 0) segs.RemoveAt(segs.Count - 1);
        return segs;
    }

    /// <summary>Greedy wrap of inline runs into lines of at most <paramref name="width"/>;
    /// the block's own font is the strut of every line.</summary>
    private static List<LpLine> LpWrap(LetterPreviewState lp, List<LpRun> inline, double width, double strutFs, bool breakWord)
    {
        var lines = new List<LpLine>();
        var cur = new List<LpRun>();
        foreach (var run in inline)
        {
            foreach (Match m in LpTokenRx.Matches(run.Text))
                foreach (var tok in LpSplitHyphen(m.Value))
                {
                    if (LpTryAppend(cur, run, tok, width)) continue;
                    if (LpWidth(cur) > 0)
                    {
                        lines.Add(LpCloseLine(lp, cur, strutFs));
                        cur = new List<LpRun>();
                        if (LpTryAppend(cur, run, tok, width)) continue;
                    }
                    // a token wider than the box: broken by characters, else it overflows
                    if (!breakWord) { LpAppend(cur, run, tok); continue; }
                    foreach (var piece in LpBreakChars(run, tok, width))
                    {
                        if (LpWidth(cur) > 0) { lines.Add(LpCloseLine(lp, cur, strutFs)); cur = new List<LpRun>(); }
                        LpAppend(cur, run, piece);
                    }
                }
        }
        lines.Add(LpCloseLine(lp, cur, strutFs));
        return lines;
    }

    private static IEnumerable<string> LpSplitHyphen(string tok)
    {
        var start = 0;
        for (var i = 0; i < tok.Length - 1; i++)
            if (tok[i] == '-' && tok[i + 1] != ' ') { yield return tok[start..(i + 1)]; start = i + 1; }
        yield return tok[start..];
    }

    private static void LpAppend(List<LpRun> cur, LpRun src, string tok)
    {
        if (cur.Count > 0 && cur[^1].Face == src.Face && cur[^1].Fs == src.Fs && cur[^1].Underline == src.Underline) cur[^1].Text += tok;
        else cur.Add(new LpRun { Face = src.Face, Fs = src.Fs, Text = tok, Underline = src.Underline });
    }

    private static bool LpTryAppend(List<LpRun> cur, LpRun src, string tok, double width)
    {
        var probe = new List<LpRun>();
        foreach (var r in cur) probe.Add(new LpRun { Face = r.Face, Fs = r.Fs, Text = r.Text, Underline = r.Underline });
        LpAppend(probe, src, tok);
        if (LpWidth(probe) > width + 1e-6) return false;
        cur.Clear();
        cur.AddRange(probe);
        return true;
    }

    /// <summary>The kerned width of a line's runs, its trailing spaces not counted.</summary>
    private static double LpWidth(List<LpRun> runs)
    {
        double w = 0;
        for (var i = 0; i < runs.Count; i++)
        {
            var t = i == runs.Count - 1 ? runs[i].Text.TrimEnd(' ', '\t', '\r', '\n') : runs[i].Text;
            w += LpMeasure(runs[i].Face, t, runs[i].Fs);
        }
        return w;
    }

    private static IEnumerable<string> LpBreakChars(LpRun src, string tok, double width)
    {
        var start = 0;
        while (start < tok.Length)
        {
            var n = 1;
            while (start + n < tok.Length && LpMeasure(src.Face, tok.Substring(start, n + 1), src.Fs) <= width) n++;
            yield return tok.Substring(start, n);
            start += n;
        }
    }

    private static LpLine LpCloseLine(LetterPreviewState lp, List<LpRun> runs, double strutFs)
    {
        var line = new LpLine();
        var above = LpDrop(lp, strutFs);
        var below = LpLineH(lp, strutFs) - above;
        foreach (var r in runs)
        {
            r.Text = r.Text.TrimEnd(' ', '\t', '\r', '\n');
            if (r.Text.Length == 0) continue;
            r.Width = LpMeasure(r.Face, r.Text, r.Fs);
            var d = LpDrop(lp, r.Fs);
            above = Math.Max(above, d);
            below = Math.Max(below, LpLineH(lp, r.Fs) - d);
            line.Runs.Add(r);
            line.Width += r.Width;
        }
        line.Above = above;
        line.Height = above + below;
        return line;
    }

    /// <summary>Draw lines from a top edge, each aligned in its box: left, centred or right.</summary>
    private static void LpDrawLines(LetterPreviewState lp, List<LpLine> lines, double x0, double boxW, double topTd,
        bool center, bool right, string rgb = "0 0 0")
    {
        var y = topTd;
        foreach (var ln in lines)
        {
            var x = center ? x0 + (boxW - ln.Width) / 2 : right ? x0 + boxW - ln.Width : x0;
            foreach (var r in ln.Runs)
            {
                LpEmit(lp, r, x, y + ln.Above, rgb);
                x += r.Width;
            }
            y += ln.Height;
        }
    }

    private static double LpLinesH(List<LpLine> lines)
    {
        double h = 0;
        foreach (var ln in lines) h += ln.Height;
        return h;
    }
}

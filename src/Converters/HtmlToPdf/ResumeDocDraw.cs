using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// Resume document helpers: baseline drop, measurement, page flush, page breaks, run parsing and emitting, and list items.
    private static double Drop(ResumeDocState rd, double fs, double box) => (box - fs * rd.wm.sum) / 2 + fs * rd.wm.asc;

    private static double W(ResumeDocState rd, string t, bool bold, double fs)
        => MeasureFaceText(rd.face + (bold ? " Bold" : ""), t, fs);

    private static void FlushPage(ResumeDocState rd)
    {
        var sb = new StringBuilder();
        foreach (var f in rd.frags.OrderBy(f => f.grp))
        {
            if (f.t.Length == 0) continue;
            var (rn, hex) = Text.Type0FontEmbedder.Embed(
                (rd.page.Dict.Get("Resources") as Core.PdfDictionary)!.Get("Font") as Core.PdfDictionary
                    ?? throw new InvalidOperationException(),
                PosFace(rd.face + (f.bold ? " Bold" : "")).ttf ?? PosFace(rd.face).ttf!,
                rd.face.Replace(" ", "") + (f.bold ? "Bold" : ""), f.t,
                stripSpacesInBaseFont: true);
            sb.AppendLine(Compat.Format(rd.inv,
                $"q {f.col.R / 255.0:0.###} {f.col.G / 255.0:0.###} {f.col.B / 255.0:0.###} rg " +
                $"BT /{rn} {f.fs:0.##} Tf 1 0 0 1 {f.x:F2} {rd.pageHeight - f.y:F2} Tm " +
                $"<{Compat.ToHexString(hex)}> Tj ET Q"));
        }
        rd.page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString() + "\n"));
        rd.frags.Clear();
    }

    private static void BreakIf(ResumeDocState rd, double need)
    {
        if (rd.y + need <= rd.pageHeight - rd.marginBottom) return;
        FlushPage(rd);
        rd.page = rd.doc.Pages.Add(rd.pageWidth, rd.pageHeight);
        EnsureFonts(rd.page, rd.docFontDict);
        rd.y = rd.marginTop;
    }

    private static List<(string t, bool bold, Color col)> ParseRuns(ResumeDocState rd, string inner)
    {
        var runs = new List<(string, bool, Color)>();
        var colStack = new Stack<Color>();
        var boldDepth = 0;
        foreach (Match m in rd.runRx.Matches(inner))
        {
            if (m.Groups[5].Success)
            {
                var txt = DecodeEntities(m.Groups[5].Value);
                if (txt.Length > 0)
                    runs.Add((txt, boldDepth > 0,
                        colStack.Count > 0 ? colStack.Peek() : rd.black));
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
                        ? fc : rd.black);
                }
                else if (colStack.Count > 0) colStack.Pop();
            }
            else if (tag is "b" or "strong")
                boldDepth += closeT ? -1 : 1;
        }
        return runs;
    }

    // wrapped emission of runs into the column; returns the LINE COUNT
    private static int EmitRuns(ResumeDocState rd, List<(string t, bool bold, Color col)> runs, double x0, double availW,
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
            BreakIf(rd, lineH);
            var x = x0;
            foreach (var (t, bold, col) in lineRuns)
            {
                rd.frags.Add((grp, x, rd.y + Drop(rd, fs, lineH), fs, bold, col, t));
                x += W(rd, t, bold, fs);
            }
            rd.y += lineH;
            lines++;
            lineRuns.Clear(); lineW = 0;
        }
        foreach (var (word, bold, col) in flat)
        {
            var wFit = W(rd, word.TrimEnd(' '), bold, fs);
            if (lineRuns.Count > 0 && lineW + wFit > availW && word.Trim().Length > 0)
                Flush();
            if (lineRuns.Count > 0 && lineRuns[^1].bold == bold
                && lineRuns[^1].col.Equals(col))
                lineRuns[^1] = (lineRuns[^1].t + word, bold, col);
            else if (lineRuns.Count == 0 && word.Trim().Length == 0 && lines > 0)
                continue;                          // no leading space after a wrap
            else lineRuns.Add((word, bold, col));
            lineW += W(rd, word, bold, fs);
        }
        Flush();
        return lines;
    }

    private static void EmitLis(ResumeDocState rd, string ulInner, double colX, double availW, int grp)
    {
        foreach (Match liM in Regex.Matches(ulInner, @"<li[^>]*>(.*?)</li>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            BreakIf(rd, RdLinePt);
            var liTop = rd.y;
            rd.frags.Add((grp, colX + RdLiMarkerXPt, liTop + Drop(rd, 11, RdLinePt), 11, false,
                rd.black, "•"));
            var n = EmitRuns(rd, ParseRuns(rd, liM.Groups[1].Value), colX + RdLiTextXPt,
                availW - RdLiTextXPt, 11, RdLinePt, grp);
            // a single-line item paces at the 13 pt MARKER box
            if (n <= 1) rd.y = liTop + RdSingleLiPitchPt;
        }
    }
}

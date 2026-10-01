using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// Benefit review: 
    private static void Flush(BenefitReviewState bv)
    {
        if (bv.cur.Count > 0)
        {
            var line = new BrLine
            {
                Kind = bv.liDepth > 0 ? BrKind.Li : bv.h1Depth > 0 ? BrKind.H1
                    : bv.h3Depth > 0 ? BrKind.H3 : BrKind.Flow,
            };
            line.Runs.AddRange(bv.cur);
            bv.lines.Add(line);
        }
        bv.cur.Clear();
        bv.pendingSpace = false;
    }

    private static void AddText(BenefitReviewState bv, string raw)
    {
        if (bv.hiddenDepth > 0) return;
        var startsWs = raw.Length > 0 && char.IsWhiteSpace(raw[0]);
        var endsWs = raw.Length > 0 && char.IsWhiteSpace(raw[^1]);
        var t = CollapseWs(DecodeEntities(raw)).Trim();
        if (t.Length == 0) { bv.pendingSpace |= (startsWs || endsWs) && bv.cur.Count > 0; return; }
        var bold = bv.boldDepth > 0 || bv.h1Depth > 0 || bv.h3Depth > 0;
        var link = bv.linkDepth > 0;
        if ((bv.pendingSpace || startsWs) && bv.cur.Count > 0) t = " " + t;
        if (bv.cur.Count > 0 && bv.cur[^1].Bold == bold && bv.cur[^1].Link == link)
            bv.cur[^1].Text += t;
        else
            bv.cur.Add(new BrRun { Text = t, Bold = bold, Link = link });
        bv.pendingSpace = endsWs;
    }

    private static string N(BenefitReviewState bv, double v) => v.ToString("0.###", bv.inv);

    private static void OpenPage(BenefitReviewState bv)
    {
        bv.page = bv.doc.Pages.Add(bv.pageWidth, bv.pageHeight);
        EnsureFonts(bv.page);
        bv.sb = new StringBuilder();
        bv.pageBufs.Add((bv.page, bv.sb));
    }

    private static double Y(BenefitReviewState bv, double yTd) => bv.pageHeight - yTd;

    private static double FsOf(BenefitReviewState bv, BrKind k) => k switch
    {
        BrKind.H1 => BrH1Fs, BrKind.H3 => BrH3Fs, _ => BrBodyFs,
    };

    private static double TopSeat(BenefitReviewState bv, BrKind k) => k switch
    {
        BrKind.H1 => BrTopSeatH1, BrKind.H3 => BrTopSeatH3, _ => BrTopSeat12,
    };

    private static double Pitch(BenefitReviewState bv, BrKind prev, BrKind curK)
    {
        if (curK == BrKind.H1) return BrToH1Pt;
        if (prev == BrKind.H1) return BrH1OutPt;
        if (curK == BrKind.H3) return prev == BrKind.H3 ? BrH3ToH3Pt : BrToH3Pt;
        if (prev == BrKind.H3) return BrH3OutPt;
        if (curK == BrKind.Li && prev != BrKind.Li) return BrListMarginPt;
        if (prev == BrKind.Li && curK != BrKind.Li) return BrListMarginPt;
        return BrLinePt;
    }

    private static double Measure(BenefitReviewState bv, BrRun r, double fs) => MeasureFaceText(
        r.Bold ? "Times New Roman-Bold" : "Times New Roman", r.Text, fs);

    private static void EmitLineAt(BenefitReviewState bv, BrLine line, double baseTd, double x)
    {
        var pen = x;
        foreach (var r in line.Runs)
        {
            var fs = FsOf(bv, line.Kind);
            var w = Measure(bv, r, fs);
            var res = r.Bold || line.Kind is BrKind.H1 or BrKind.H3 ? "F6" : "F5";
            var rg = r.Link ? "0 0 1 rg" : "0 0 0 rg";
            bv.sb.AppendLine($"BT {rg} /{res} {fs.ToString("F2", bv.inv)} Tf "
                + $"1 0 0 1 {N(bv, pen)} {N(bv, Y(bv, baseTd))} Tm ({EscapePdfString(r.Text)}) Tj ET");
            if (r.Link)
                bv.sb.AppendLine($"0 0 1 RG 0.75 w {N(bv, pen)} {N(bv, Y(bv, baseTd + BrUnderlineDropPt))} m "
                    + $"{N(bv, pen + w)} {N(bv, Y(bv, baseTd + BrUnderlineDropPt))} l S");
            pen += w;
        }
    }

    // greedy wrap of a line's runs into sublines that fit the content box
    private static List<BrLine> WrapLine(BenefitReviewState bv, BrLine line)
    {
        var x0 = line.Kind == BrKind.Li ? BrLiTextX : BrTextX;
        var avail = BrTextRight - x0;
        var fs = FsOf(bv, line.Kind);
        var outLines = new List<BrLine>();
        var curL = new BrLine { Kind = line.Kind };
        var used = 0.0;
        foreach (var run in line.Runs)
        {
            var words = run.Text.Split(' ');
            var buf = "";
            void CloseRun()
            {
                if (buf.Length == 0) return;
                curL.Runs.Add(new BrRun { Text = buf, Bold = run.Bold, Link = run.Link });
                buf = "";
            }
            for (var wi = 0; wi < words.Length; wi++)
            {
                var word = words[wi];
                var probe = buf.Length == 0 ? word : buf + " " + word;
                var lead = curL.Runs.Count > 0 && buf.Length == 0 && word.Length > 0 ? " " : "";
                var wNew = MeasureFaceText(run.Bold ? "Times New Roman-Bold" : "Times New Roman",
                    lead + probe, fs);
                if (used + wNew > avail && (buf.Length > 0 || curL.Runs.Count > 0))
                {
                    CloseRun();
                    outLines.Add(curL);
                    curL = new BrLine { Kind = line.Kind };
                    used = 0.0;
                    buf = word;
                }
                else buf = lead.Length > 0 ? lead + probe : probe;
            }
            if (buf.Length > 0)
            {
                used += MeasureFaceText(run.Bold ? "Times New Roman-Bold" : "Times New Roman",
                    buf, fs);
                CloseRun();
            }
        }
        if (curL.Runs.Count > 0) outLines.Add(curL);
        return outLines;
    }
}

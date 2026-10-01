using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// Covering letter: 
    private static List<string> BrLines(CoveringLetterState cl, string frag)
    {
        var parts = Regex.Split(frag, @"<br\s*/?>", RegexOptions.IgnoreCase)
            .Select(LtFlat).ToList();
        while (parts.Count > 0 && parts[^1].Length == 0) parts.RemoveAt(parts.Count - 1);
        return parts;
    }

    private static void AddLines(CoveringLetterState cl, List<OsRun> runs, double x, double right, bool justify, bool liMode,
        double marginTop, double marginBottom, bool bullet)
    {
        var groups = OsBreakGroups(runs);
        var gap = Math.Max(cl.prevBottom, marginTop) + cl.firstGapExtra;
        cl.firstGapExtra = 0;
        var firstOfBlock = true;
        for (var g = 0; g < groups.Count; g++)
        {
            var lines = OsWrap(groups[g], right - x);
            if (lines.Count == 0) lines.Add(new List<OsRun>());
            for (var i = 0; i < lines.Count; i++)
            {
                var em = liMode || groups[g].Count == 0
                    || (i == 0 && g > 0) || (i == lines.Count - 1 && g < groups.Count - 1);
                var lastOfGroup = i == lines.Count - 1;
                cl.flow.Add(new LtLine
                {
                    Segs = lines[i],
                    X = x,
                    JustifyTo = justify && !lastOfGroup ? right : 0,
                    BoxH = em ? LtLineEm : LtLineP,
                    Drop = em ? cl.dropEm : cl.dropP,
                    GapBefore = firstOfBlock ? gap : 0,
                    Bullet = bullet && g == 0 && i == 0,
                });
                firstOfBlock = false;
            }
        }
        cl.prevBottom = marginBottom;
    }

    private static string ResFor(CoveringLetterState cl, string face) => face switch
    {
        "Arial Bold" => "F9",
        "Arial Italic" => "F11",
        "Candara Bold" => "F12",
        _ => "F8",
    };

    private static void Stream(CoveringLetterState cl, Page dst, string s) => dst.AddContentStream(Encoding.ASCII.GetBytes(s));

    private static double SegsW(CoveringLetterState cl, List<OsRun> segs)
    {
        double w = 0;
        foreach (var r in segs) w += MeasureFaceText(r.Face, r.Text, r.Fs);
        return w;
    }

    private static void EmitRun(CoveringLetterState cl, Page dst, OsRun r, double x, double yTd)
    {
        EmitPositionedRun(dst, ResFor(cl, r.Face), r.Fs, x, cl.pageHeight - yTd + r.Rise, r.Text);
        if (r.Under)
            Stream(cl, dst, Compat.Format(cl.inv,
                $"q 1.05 w {x:F2} {cl.pageHeight - yTd - 1.05:F2} m {x + MeasureFaceText(r.Face, r.Text, r.Fs):F2} {cl.pageHeight - yTd - 1.05:F2} l S Q\n"));
    }

    private static void EmitLine(CoveringLetterState cl, Page dst, LtLine ln, double baseTd)
    {
        var x = ln.X;
        if (ln.JustifyTo > 0)
        {
            // stretch the word spaces so the last glyph seats on the band edge
            var natural = SegsW(cl, ln.Segs);
            var spaces = ln.Segs.Sum(s => s.Text.Count(c => c == ' '));
            var extra = ln.JustifyTo - ln.X - natural;
            if (spaces > 0 && extra > 0.01)
            {
                var per = extra / spaces;
                foreach (var seg in ln.Segs)
                {
                    var pieces = seg.Text.Split(' ');
                    for (var k = 0; k < pieces.Length; k++)
                    {
                        if (pieces[k].Length > 0)
                        {
                            EmitRun(cl, dst, seg with { Text = pieces[k] }, x, baseTd);
                            x += MeasureFaceText(seg.Face, pieces[k], seg.Fs);
                        }
                        if (k < pieces.Length - 1)
                            x += MeasureFaceText(seg.Face, " ", seg.Fs) + per;
                    }
                }
                return;
            }
        }
        foreach (var seg in ln.Segs)
        {
            if (seg.Text.Length > 0) EmitRun(cl, dst, seg, x, baseTd);
            x += MeasureFaceText(seg.Face, seg.Text, seg.Fs);
        }
    }
}

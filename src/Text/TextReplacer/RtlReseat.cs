using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
    /// <summary>Baseline match for the runs of the edited line, in points.</summary>
    private const double RtlLineTolerance = 0.75;

    /// <summary>A run within this many ems of the line's left end still continues it.</summary>
    private const double RtlRunGapEms = 0.25;

    /// <summary>A gap this many ems wide ends the line whatever its script.</summary>
    private const double RtlColumnGapEms = 2.0;

    /// <summary>The advance, in ems, of a glyph whose font gives no width.</summary>
    private const double UnmeasuredGlyphEms = 0.5;

    /// <summary>
    /// Re-seats an in-place replacement on a right-to-left line the way that line is set:
    /// the replacement keeps the match's RIGHT edge and the runs that continue the line to
    /// its left (the rest of the line in reading order) move by the width the text lost or
    /// gained, instead of the replacement keeping the match's left edge and leaving a gap
    /// (or an overlap) between it and the words before it.
    ///
    /// The line ends, leftwards, at a gap wider than a word space before a run that draws
    /// something other than right-to-left text or spaces (the next column of a bilingual
    /// table), or at any gap of a column's width. Only runs seated by their own upright Tm
    /// move. Returns the page-space advance the replacement is drawn with, or null when
    /// nothing was re-seated.
    /// </summary>
    internal static double? ReseatRightToLeft(Page page, double baselineY, double matchX, double oldAdvance)
    {
        var reader = page.Reader;
        if (reader is null || page.RotateDegrees % 360 != 0) return null;
        var streams = GetContentStreams(page, reader);
        if (streams.Count == 0) return null;
        var bytes = CombineStreams(streams);
        var ops = CollectTextOps(bytes, TextAbsorber.ResolveFonts(page.Dict, reader), reader);
        var metrics = new Dictionary<PdfDictionary, FontMetrics?>();

        var line = new List<(CrossTextOp op, double x, double adv)>();
        foreach (var o in ops)
        {
            if (o.TmB != 0 || o.TmC != 0 || o.CtmB != 0 || o.CtmC != 0 || o.TmA <= 0 || o.CtmA <= 0) continue;
            if (Math.Abs(PageY(o) - baselineY) > RtlLineTolerance) continue;
            line.Add((o, PageX(o), RunAdvance(o, metrics, reader)));
        }

        // The runs now drawn where the match was: the replacement, written into one of them.
        var matchRight = matchX + oldAdvance;
        var seats = new List<CrossTextOp>();
        double newAdvance = 0, seatRight = double.MinValue;
        foreach (var (op, x, adv) in line)
        {
            if (x < matchX - RtlLineTolerance || x >= matchRight - RtlLineTolerance || op.Bytes.Length == 0
                || !ClipHolds(op, matchX, matchRight)) continue;
            if (!op.TmPositioned) return null;
            seats.Add(op);
            newAdvance += adv;
            seatRight = Math.Max(seatRight, x + adv);
        }
        if (seats.Count == 0) return null;
        var seatShift = matchRight - seatRight;
        var delta = oldAdvance - newAdvance;
        if (Math.Abs(delta) < SeatTolerance && Math.Abs(seatShift) < SeatTolerance) return newAdvance;

        var patches = new List<(int start, int end, string value)>();
        foreach (var op in seats) patches.Add(TmXPatch(bytes, op, seatShift));
        foreach (var op in RunsContinuingLeftward(line, matchX))
            if (op.TmPositioned) patches.Add(TmXPatch(bytes, op, delta));
        patches.Sort((a, b) => a.start.CompareTo(b.start));

        var result = new MemoryStream(bytes.Length + RewriteHeadroom);
        var last = 0;
        foreach (var (start, end, value) in patches)
        {
            if (start < last) continue; // one seat shared by two runs moves once
            result.Write(bytes, last, start - last);
            result.Write(Encoding.ASCII.GetBytes(value));
            last = end;
        }
        result.Write(bytes, last, bytes.Length - last);
        page.SetContentStream(result.ToArray());
        return newAdvance;
    }

    /// <summary>The edit that moves a run's own Tm seat by <paramref name="shift"/> page points.</summary>
    private static (int start, int end, string value) TmXPatch(byte[] bytes, CrossTextOp op, double shift)
    {
        // The lexer's token span opens on the whitespace before the number.
        var start = op.TmXTokStart;
        while (start < op.TmXTokEnd && IsWhitespace(bytes[start])) start++;
        return (start, op.TmXTokEnd, Num(op.TmXVal + shift / op.CtmA));
    }

    /// <summary>The runs left of the match that continue its line, nearest first.</summary>
    private static IEnumerable<CrossTextOp> RunsContinuingLeftward(
        List<(CrossTextOp op, double x, double adv)> line, double matchX)
    {
        var left = new List<(CrossTextOp op, double x, double adv)>();
        foreach (var r in line)
            if (r.x + r.adv <= matchX + RtlLineTolerance && r.x < matchX - RtlLineTolerance) left.Add(r);
        left.Sort((a, b) => b.x.CompareTo(a.x));
        var lineLeft = matchX;
        foreach (var (op, x, adv) in left)
        {
            var em = op.FontSize * TmScaleOf(op) * ScaleOf(op);
            var gap = lineLeft - (x + adv);
            if (gap > RtlColumnGapEms * em) yield break;
            if (gap > RtlRunGapEms * em && !BidiReorderer.ContainsRtl(op.Text) && !string.IsNullOrWhiteSpace(op.Text))
                yield break;
            lineLeft = Math.Min(lineLeft, x);
            yield return op;
        }
    }

    /// <summary>The page-space advance a run is drawn with: its glyphs, Tc per glyph and
    /// the kerns of its own array.</summary>
    private static double RunAdvance(CrossTextOp o, Dictionary<PdfDictionary, FontMetrics?> cache, PdfReader reader)
    {
        FontMetrics? m = null;
        if (o.FontDict is { } fd && !cache.TryGetValue(fd, out m))
        {
            try { m = FontMetrics.FromFontDict(fd, reader); } catch { m = null; }
            cache[fd] = m;
        }
        double w;
        try { w = m?.MeasureString(o.Bytes, o.FontSize) ?? o.FontSize * UnmeasuredGlyphEms * o.Bytes.Length; }
        catch { w = o.FontSize * UnmeasuredGlyphEms * o.Bytes.Length; }
        var glyphs = m?.IsCid == true ? (o.Bytes.Length + 1) / 2 : o.Bytes.Length;
        w += o.Tc * glyphs - o.KernSum / 1000.0 * o.FontSize;
        return w * TmScaleOf(o) * ScaleOf(o);
    }
}

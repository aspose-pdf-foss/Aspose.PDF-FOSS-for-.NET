using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
    /// <summary>
    /// After an in-place replacement that shifts the rest of its line, moves the runs that
    /// continue the line to the match's right and are seated by their own Tm (each in a
    /// text object of its own, the way a word processor exports a line of mixed styles) by
    /// the width the text gained or lost, so the punctuation and words after it keep their
    /// distance from it. The runs the match's own text object places by Td are moved by the
    /// in-place rewrite already. The line ends at a gap of a column's width or where the
    /// clip box changes (the next table cell); a run on the line that states no Tm of its
    /// own ends it too, since moving the runs beyond it would draw them over it.
    /// Returns the shift applied to those runs (0 when none moved).
    /// </summary>
    internal static double ShiftSeatedFollowers(Page page, double baselineY, double matchX, double oldAdvance, string replacement)
    {
        var reader = page.Reader;
        if (reader is null || page.RotateDegrees % 360 != 0) return 0;
        var streams = GetContentStreams(page, reader);
        if (streams.Count == 0) return 0;
        var bytes = CombineStreams(streams);
        if (!HasSeatOnBaselineAfter(bytes, baselineY, matchX)) return 0;
        var ops = CollectTextOps(bytes, TextAbsorber.ResolveFonts(page.Dict, reader), reader);
        var metrics = new Dictionary<PdfDictionary, FontMetrics?>();
        var line = new List<(CrossTextOp op, double x, double adv)>();
        foreach (var o in ops)
            if (o.TmB == 0 && o.TmC == 0 && o.CtmB == 0 && o.CtmC == 0 && o.TmA > 0 && o.CtmA > 0
                && Math.Abs(PageY(o) - baselineY) <= RtlLineTolerance)
                line.Add((o, PageX(o), RunAdvance(o, metrics, reader)));

        var matchRight = matchX + oldAdvance;
        var seats = line.Where(r => r.x >= matchX - RtlLineTolerance && r.x < matchRight - RtlLineTolerance && r.op.Bytes.Length > 0
            && ClipHolds(r.op, matchX, matchRight)).ToList();
        double delta, edge = matchRight;
        if (seats.Count == 1 && seats[0].op.Text.Length > replacement.Length
            && GainInsideRun(seats[0].op, replacement, matchX - seats[0].x, oldAdvance, metrics, reader) is { } headGain)
        {
            // The run starts with the match and goes on past it: its end moved by what the text gained.
            delta = headGain;
            edge = seats[0].x + seats[0].adv - headGain;
        }
        else if (seats.Count > 0)
            delta = seats.Max(r => r.x + r.adv) - matchRight;
        else
        {
            // The match was written inside a run that starts before it: the rewrite moved the
            // run's own tail, and the runs after the run follow it by what the text gained there.
            var host = line.Find(r => r.x < matchX - RtlLineTolerance && r.x + r.adv > matchX + RtlLineTolerance
                && r.op.Bytes.Length > 0 && ClipHolds(r.op, matchX, matchRight));
            if (host.op is null || GainInsideRun(host.op, replacement, matchX - host.x, oldAdvance, metrics, reader) is not { } gain)
                return 0;
            seats.Add(host);
            delta = gain;
            edge = host.x + host.adv - gain;
        }
        if (Math.Abs(delta) < SeatTolerance) return 0;
        var seatBt = seats[0].op.BtStart;
        var clip = seats[0].op.Clip;
        var em = EmOf(seats[0].op);

        var patches = new List<(int start, int end, string value)>();
        var lineFrom = edge;
        foreach (var (op, x, adv) in line.Where(r => r.x >= lineFrom - RtlLineTolerance).OrderBy(r => r.x))
        {
            // A run the rewrite already carried along its own text object's Td chain.
            var carried = op.BtStart == seatBt && !op.TmPositioned;
            var oldX = carried ? x - delta : x;
            if (oldX - edge > RtlColumnGapEms * em || !SameClip(op.Clip, clip)) break;
            if (!carried)
            {
                if (!op.TmPositioned) break;
                patches.Add(TmXPatch(bytes, op, delta));
            }
            edge = Math.Max(edge, oldX + adv);
        }
        if (patches.Count == 0) return 0;
        patches.Sort((a, b) => a.start.CompareTo(b.start));
        var result = new MemoryStream(bytes.Length + RewriteHeadroom);
        var last = 0;
        foreach (var (start, end, value) in patches)
        {
            if (start < last) continue;
            result.Write(bytes, last, start - last);
            result.Write(Encoding.ASCII.GetBytes(value));
            last = end;
        }
        result.Write(bytes, last, bytes.Length - last);
        page.SetContentStream(result.ToArray());
        return delta;
    }

    /// <summary>The width a replacement written inside a run gained over the match's old advance: the run's text
    /// shows the replacement where the match sat (<paramref name="lead"/> past the run's seat), measured as the run
    /// draws it. Null when the run does not show it there.</summary>
    private static double? GainInsideRun(CrossTextOp o, string replacement, double lead, double oldAdvance,
        Dictionary<PdfDictionary, FontMetrics?> cache, PdfReader reader)
    {
        if (replacement.Length == 0 || o.FontDict is not { } fd) return null;
        if (!cache.TryGetValue(fd, out var m))
        {
            try { m = FontMetrics.FromFontDict(fd, reader); } catch { m = null; }
            cache[fd] = m;
        }
        var codeBytes = m?.IsCid == true ? 2 : 1;
        if (m is null || o.Text.Length * codeBytes != o.Bytes.Length) return null;
        double Advance(int from, int to)
        {
            var w = m.MeasureString(o.Bytes[from..to], o.FontSize) + o.Tc * ((to - from) / codeBytes);
            return w * TmScaleOf(o) * ScaleOf(o) + KernRange(o, from, to);
        }
        double? gain = null;
        var nearest = RtlLineTolerance;
        try
        {
            for (var i = o.Text.IndexOf(replacement, StringComparison.Ordinal); i >= 0;
                 i = o.Text.IndexOf(replacement, i + 1, StringComparison.Ordinal))
            {
                var off = Math.Abs(Advance(0, i * codeBytes) - lead);
                if (off > nearest) continue;
                nearest = off;
                gain = Advance(i * codeBytes, (i + replacement.Length) * codeBytes) - oldAdvance;
            }
        }
        catch { return null; }
        return gain;
    }

    /// <summary>A pass over the raw stream, before the full walk: whether any Tm seats a run on the
    /// baseline right of <paramref name="fromX"/> (page space through the cm chain; a rotating cm
    /// answers yes). A line placed by Td alone has nothing to move here and costs only this pass.</summary>
    private static bool HasSeatOnBaselineAfter(byte[] bytes, double baselineY, double fromX)
    {
        var tokens = new List<(int start, int end)>(8);
        var saved = new Stack<(double a, double d, double e, double f)>();
        double a = 1, d = 1, e = 0, f = 0;
        var i = 0;
        while (i < bytes.Length)
        {
            var c = bytes[i];
            if (IsWhitespace(c)) { i++; continue; }
            if (c is (byte)'(' or (byte)'<' or (byte)'[' or (byte)'/') { i = SkipOperand(bytes, i); tokens.Clear(); continue; }
            var start = i;
            while (i < bytes.Length && !IsDelimiter(bytes[i])) i++;
            if (i == start) { i++; continue; }
            if (i - start == 2 && bytes[start] == (byte)'I' && bytes[start + 1] == (byte)'D') { i = SkipInlineImageData(bytes, i); tokens.Clear(); continue; }
            if (bytes[start] is not ((>= (byte)'A' and <= (byte)'z') or (byte)'\'' or (byte)'"'))
            {
                tokens.Add((start, i));
                if (tokens.Count > 6) tokens.RemoveAt(0);
                continue;
            }
            var len = i - start;
            if (len == 1 && bytes[start] == (byte)'q') saved.Push((a, d, e, f));
            else if (len == 1 && bytes[start] == (byte)'Q') { if (saved.Count > 0) (a, d, e, f) = saved.Pop(); }
            else if (len == 2 && tokens.Count == 6 && (bytes[start], bytes[start + 1]) is ((byte)'c', (byte)'m') or ((byte)'T', (byte)'m'))
            {
                var m = new double[6];
                for (var k = 0; k < 6; k++) m[k] = TryNum(bytes, tokens[k]) ?? double.NaN;
                if (bytes[start] == (byte)'c')
                {
                    if (Math.Abs(m[1]) > 0 || Math.Abs(m[2]) > 0) return true;
                    (e, f, a, d) = (m[4] * a + e, m[5] * d + f, m[0] * a, m[3] * d);
                }
                else if (Math.Abs(d * m[5] + f - baselineY) <= RtlLineTolerance && a * m[4] + e > fromX + RtlLineTolerance)
                    return true;
            }
            tokens.Clear();
        }
        return false;
    }
}

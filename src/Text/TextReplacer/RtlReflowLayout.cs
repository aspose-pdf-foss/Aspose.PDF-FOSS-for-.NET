using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
    /// <summary>The paragraph's runs cut at their spaces, in reading order (lines top-down, each from
    /// the right): a gap wider than abutting between two runs, and a line break with no space drawn
    /// at it, become space pieces of their own width.</summary>
    private static List<RtlPiece> RtlParagraphPieces(
        List<(double y, double right, List<RtlUnit> units)> lines,
        Dictionary<PdfDictionary, FontMetrics?> metrics, PdfReader reader, double matchX, double matchRight)
    {
        var pieces = new List<RtlPiece>();
        double? spaceWidth = null;
        for (var li = 0; li < lines.Count; li++)
        {
            if (li > 0 && pieces.Count > 0 && !pieces[^1].IsSpace)
            {
                pieces.Add(new RtlPiece { IsSpace = true, Width = spaceWidth ?? EmOf(lines[li].units[0].Head) * RtlDefaultSpaceEms });
            }
            double? prevLeft = null;
            foreach (var u in lines[li].units)
            {
                var seat = li == 0 && IsSeat(u, matchX, matchRight);
                double l = seat ? matchX : u.X, r = seat ? matchRight : u.Right;
                if (prevLeft is { } pl && pl - r > RtlAbutTolerance)
                    pieces.Add(new RtlPiece { IsSpace = true, Width = pl - r });
                var split = u.Members.Count == 1 ? SplitRunAtSpaces(u, metrics, reader)
                    : new List<RtlPiece> { new() { Unit = u, Rigid = true, Width = u.Right - u.X, IsSpace = string.IsNullOrWhiteSpace(u.Text) } };
                for (var k = split.Count - 1; k >= 0; k--)
                {
                    split[k].IsSeat = seat;
                    if (split[k].IsSpace && !split[k].Rigid && spaceWidth is null && split[k].End - split[k].Start <= GlyphBytes(u.Head, metrics))
                        spaceWidth = split[k].Width;
                    pieces.Add(split[k]);
                }
                prevLeft = prevLeft is { } p ? Math.Min(p, l) : l;
            }
        }
        return pieces;
    }

    private static int GlyphBytes(CrossTextOp op, Dictionary<PdfDictionary, FontMetrics?> metrics) =>
        op.FontDict is { } fd && metrics.TryGetValue(fd, out var m) && m?.IsCid == true ? 2 : 1;

    /// <summary>A run's glyphs grouped into alternating words and spaces, in drawn order; a kern inside
    /// a group stays with it, one between a word and a space goes with the space.</summary>
    private static List<RtlPiece> SplitRunAtSpaces(RtlUnit u, Dictionary<PdfDictionary, FontMetrics?> metrics, PdfReader reader)
    {
        var op = u.Head;
        var unit = GlyphBytes(op, metrics);
        var list = new List<RtlPiece>();
        for (var i = 0; i + unit <= op.Bytes.Length; i += unit)
        {
            var glyph = new byte[unit];
            Array.Copy(op.Bytes, i, glyph, 0, unit);
            var space = DecodeString(glyph, op.ToUnicode, op.FontDict, reader) == " ";
            if (list.Count == 0 || list[^1].IsSpace != space) list.Add(new RtlPiece { Unit = u, Start = i, End = i + unit, IsSpace = space });
            else list[^1].End = i + unit;
        }
        foreach (var (at, amount) in op.KernAt ?? new List<(int byteIndex, double amount)>())
        {
            var owner = list.FirstOrDefault(p => p.Start < at && at < p.End)
                ?? list.FirstOrDefault(p => p.IsSpace && (p.End == at || p.Start == at));
            owner?.Kerns.Add((at, amount));
        }
        FontMetrics? m = null;
        if (op.FontDict is { } fd) metrics.TryGetValue(fd, out m);
        var scale = TmScaleOf(op) * ScaleOf(op);
        foreach (var p in list)
        {
            var slice = new byte[p.End - p.Start];
            Array.Copy(op.Bytes, p.Start, slice, 0, slice.Length);
            var glyphs = slice.Length / unit;
            double w;
            try { w = m?.MeasureString(slice, op.FontSize) ?? op.FontSize * UnmeasuredGlyphEms * glyphs; }
            catch { w = op.FontSize * UnmeasuredGlyphEms * glyphs; }
            w += op.Tc * glyphs - p.Kerns.Sum(k => k.amount) / 1000.0 * op.FontSize;
            if (unit == 1) w += op.Tw * slice.Count(b => b == (byte)' ');
            p.Width = w * scale;
        }
        return list;
    }

    /// <summary>Breaks the pieces into lines greedily at their spaces: each line is set from its right
    /// edge leftward; a word that would cross the left limit starts the next line (an existing line of
    /// the paragraph, then new ones at its pitch). Spaces before a break stay at the end of their line.</summary>
    private static void RtlLayout(List<RtlPiece> pieces,
        List<(double y, double right, List<RtlUnit> units)> lines, double left)
    {
        var pitch = lines.Count > 1 ? lines[0].y - lines[1].y : EmOf(lines[0].units[0].Head) * RtlDefaultPitchEms;
        int li = 0;
        double y = lines[0].y, right = lines[0].right, cursor = right;
        var lineHasWord = false;
        for (var i = 0; i < pieces.Count;)
        {
            if (pieces[i].IsSpace) { Place(pieces[i++]); continue; }
            var j = i;
            double w = 0;
            while (j < pieces.Count && !pieces[j].IsSpace) w += pieces[j++].Width;
            if (lineHasWord && cursor - w < left - RtlAbutTolerance)
            {
                li++;
                y = li < lines.Count ? lines[li].y : y - pitch;
                right = li < lines.Count ? lines[li].right : right;
                cursor = right;
            }
            while (i < j) Place(pieces[i++]);
            lineHasWord = true;
        }

        void Place(RtlPiece p)
        {
            cursor -= p.Width;
            p.X = cursor;
            p.Y = y;
        }
    }

    /// <summary>Writes every run whose pieces moved as one Tm-seated TJ per piece, in place of its show
    /// operator; returns the replacement's extent on the line it now starts on.</summary>
    private static (double left, double right, double baseline)? RtlRewrite(Page page, byte[] bytes, List<RtlPiece> pieces)
    {
        var patches = new List<(int start, int end, string value)>();
        foreach (var group in pieces.Where(p => p.Unit is not null).GroupBy(p => p.Unit!))
        {
            var u = group.Key;
            var drawn = group.OrderBy(p => p.Start).ToList();
            var moved = Math.Abs(drawn[0].X - u.X) > SeatTolerance;
            for (var k = 0; k < drawn.Count; k++)
                moved |= Math.Abs(drawn[k].Y - u.Y) > SeatTolerance
                    || k > 0 && Math.Abs(drawn[k].X - (drawn[k - 1].X + drawn[k - 1].Width)) > SeatTolerance;
            if (!moved) continue;
            // The first piece takes the run's own Tm (when it has one); each later piece gets a Tm of its own.
            var head = u.Head;
            var ownSeat = head.TmPositioned;
            if (ownSeat)
            {
                patches.Add(TmXPatch(bytes, head, drawn[0].X - u.X));
                patches.Add(TmYPatch(bytes, head, drawn[0].Y));
            }
            var sb = new StringBuilder();
            if (drawn[0].Rigid)
            {
                if (ownSeat) continue;
                AppendTm(sb, head, PageX(head) + drawn[0].X - u.X, drawn[0].Y);
                patches.Add((head.OpStart, head.OpStart, sb.ToString()));
                continue;
            }
            for (var k = 0; k < drawn.Count; k++)
            {
                if (k > 0 || !ownSeat) AppendTm(sb, head, drawn[k].X, drawn[k].Y);
                AppendPieceArray(sb, head, drawn[k]);
            }
            patches.Add((head.OpStart, head.OpEnd, sb.ToString()));
        }
        if (patches.Count > 0)
        {
            patches.Sort((a, b) => a.start.CompareTo(b.start));
            var result = new MemoryStream(bytes.Length + RewriteHeadroom * patches.Count);
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
        }
        var seat = pieces.Where(p => p.IsSeat && !p.IsSpace).ToList();
        if (seat.Count == 0) return null;
        var lineY = seat[0].Y;
        var onLine = seat.Where(p => Math.Abs(p.Y - lineY) <= SeatTolerance).ToList();
        return (onLine.Min(p => p.X), onLine.Max(p => p.X + p.Width), lineY);
    }

    /// <summary>The edit that seats a run's own Tm on the baseline at page Y <paramref name="pageY"/>.</summary>
    private static (int start, int end, string value) TmYPatch(byte[] bytes, CrossTextOp op, double pageY)
    {
        var start = op.TmYTok.start;
        while (start < op.TmYTok.end && IsWhitespace(bytes[start])) start++;
        return (start, op.TmYTok.end, Num((pageY - op.CtmTy) / op.CtmD));
    }

    /// <summary>One piece as `[&lt;glyphs&gt; kern ...] TJ`.</summary>
    private static void AppendPieceArray(StringBuilder sb, CrossTextOp op, RtlPiece p)
    {
        sb.Append('[');
        var at = p.Start;
        foreach (var (byteIndex, amount) in p.Kerns.OrderBy(k => k.byteIndex))
        {
            AppendHex(sb, op.Bytes, at, byteIndex);
            sb.Append(' ').Append(Num(amount)).Append(' ');
            at = Math.Max(at, byteIndex);
        }
        AppendHex(sb, op.Bytes, at, p.End);
        sb.Append("] TJ ");
    }

    /// <summary>`a 0 0 d tx ty Tm ` seating the op's text matrix at a page position.</summary>
    private static void AppendTm(StringBuilder sb, CrossTextOp op, double pageX, double pageY) =>
        sb.Append(Num(op.TmA)).Append(" 0 0 ").Append(Num(op.TmD)).Append(' ')
          .Append(Num((pageX - op.CtmTx) / op.CtmA)).Append(' ').Append(Num((pageY - op.CtmTy) / op.CtmD)).Append(" Tm ");

    private static void AppendHex(StringBuilder sb, byte[] bytes, int from, int to)
    {
        if (to <= from) return;
        var slice = new byte[to - from];
        Array.Copy(bytes, from, slice, 0, slice.Length);
        sb.Append('<').Append(Compat.ToHexString(slice)).Append('>');
    }
}

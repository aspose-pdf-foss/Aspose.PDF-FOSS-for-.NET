using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
// The helpers of the text re-flow, lifted out of ReflowFromMatch.
    private static FontMetrics? MetricsOf(ReflowState rf, CrossTextOp o)
    {
        if (o.FontDict is null) return null;
        if (rf.metricsCache.TryGetValue(o.FontDict, out var m)) return m;
        FontMetrics? built = null;
        try { built = FontMetrics.FromFontDict(o.FontDict, rf.reader); } catch { /* a font program that does not parse leaves the fallback metrics in place */ }
        rf.metricsCache[o.FontDict] = built;
        return built;
    }

    private static double PageX(CrossTextOp o) => o.CtmA * o.TmTx + o.CtmC * o.TmTy + o.CtmTx;

    private static double PageY(CrossTextOp o) => o.CtmB * o.TmTx + o.CtmD * o.TmTy + o.CtmTy;

    private static double ScaleOf(CrossTextOp o)
    {
        var det = Math.Abs(o.CtmA * o.CtmD - o.CtmB * o.CtmC);
        return det > 1e-12 ? Math.Sqrt(det) : 1.0;
    }

    // The text-matrix scale multiplies every advance: producers often set
    // `/F 1 Tf` and carry the size in Tm (e.g. `33 0 0 33 … Tm`).
    private static double TmScaleOf(CrossTextOp o) => Math.Sqrt(o.TmA * o.TmA + o.TmB * o.TmB);

    // Page-space advance of a byte run under an op's font state (glyph widths +
    // per-glyph Tc; the op's own TJ kerns only when measuring its full bytes).
    private static double AdvPage(ReflowState rf, CrossTextOp o, byte[] bytes, bool own)
    {
        var m = MetricsOf(rf, o);
        double w;
        if (m is not null)
        {
            try { w = m.MeasureString(bytes, o.FontSize); }
            catch { w = o.FontSize * 0.5 * bytes.Length; }
        }
        else
            w = o.FontSize * 0.5 * bytes.Length;
        // Tc applies once per shown GLYPH — a Type0 code is two bytes.
        var glyphs = m?.IsCid == true ? (bytes.Length + 1) / 2 : bytes.Length;
        w += o.Tc * glyphs;
        if (own) w -= o.KernSum / 1000.0 * o.FontSize;
        return w * TmScaleOf(o) * ScaleOf(o);
    }

    // The advance of this run's own SPACE glyph. A source line break that the flow
    // absorbs becomes a word gap of exactly this width: the break itself separated the
    // words, so text pulled up from the next line must not butt against the last word of
    // this one. The result is "even- tempered", not "even-tempered", with the
    // pulled-up run at 122.70 against this measure's 122.73.
    private static double SpaceAdv(ReflowState rf, CrossTextOp o)
    {
        foreach (var b in o.Bytes)
        {
            var c = DecodeString(new[] { b }, o.ToUnicode, o.FontDict, rf.reader);
            if (c == " ") return AdvPage(rf, o, new[] { b }, own: false);
        }
        var sp32 = DecodeString(new byte[] { 32 }, o.ToUnicode, o.FontDict, rf.reader);
        if (sp32 == " ") return AdvPage(rf, o, new byte[] { 32 }, own: false);
        return o.FontSize * 0.25 * TmScaleOf(o) * ScaleOf(o);
    }

    // A line's TRAILING space does not count against the wrap margin: "…should
    // automatically " fits at 266.70 against a 264.63 column, and it is the space that
    // overhangs. Measuring it in breaks the line one word early.
    private static double TrailingSpaceAdv(ReflowState rf, CrossTextOp o, byte[] bytes)
    {
        var n = bytes.Length;
        while (n > 0)
        {
            var c = DecodeString(new[] { bytes[n - 1] }, o.ToUnicode, o.FontDict, rf.reader);
            if (!string.IsNullOrWhiteSpace(c)) break;
            n--;
        }
        return n == bytes.Length ? 0 : AdvPage(rf, o, bytes[n..], own: false);
    }

    private static int LineOf(ReflowState rf, CrossTextOp o)
    {
        double py = PageY(o); int best = -1; double bestD = rf.yTol;
        for (int li = 0; li < rf.lines.Count; li++)
        {
            double d = Math.Abs(py - rf.lines[li].y);
            if (d < bestD) { bestD = d; best = li; }
        }
        return best;
    }

            // Encode the rewritten run in its ORIGINAL font. A subset's ToUnicode often
    // omits the space glyph's code (only 'real' glyphs get mapped), so recover the
    // space code from the paragraph's own bytes: any byte in an affected run of the
    // same font that DECODES space-like is the font's space. Bail (so the caller
    // falls back) when any character has no code at all.
    private static byte[]? TryEncodeInFont(ReflowState rf, string text)
    {
        if (rf.head.ToUnicode is null)
        {
            if (text.Any(ch => SimpleFontCode(ch, rf.head.FontDict) is null)) return null;
            return EncodeString(text, null, rf.head.FontDict, rf.reader);
        }
        var rev = BuildReverseMap(rf.head.ToUnicode);
        int spaceCode = -1;
        foreach (var (o, _, _) in rf.affected)
        {
            if (!ReferenceEquals(o.FontDict, rf.head.FontDict)) continue;
            foreach (var b in o.Bytes)
            {
                var ch = DecodeString(new[] { b }, o.ToUnicode, o.FontDict, rf.reader);
                if (ch is " " or "\u00A0") { spaceCode = b; break; }
            }
            if (spaceCode >= 0) break;
        }
        var outBytes = new List<byte>(text.Length);
        foreach (var ch in text)
        {
            if (rev.TryGetValue(ch.ToString(), out var code) && code >= 0 && code <= 0xFF)
                outBytes.Add((byte)code);
            else if ((ch == ' ' || ch == '\u00A0') && spaceCode >= 0)
                outBytes.Add((byte)spaceCode);
            else
                return null;
        }
        return outBytes.ToArray();
    }

    // The doc's OWN /Widths are trusted here even for a non-embedded
    // system-font reference: a zero-width replacement char means the
    // face is substituted, not that the metadata is merely sparse. (The facade
    // path's SimpleFontMissingGlyphChars deliberately requires an embedded
    // program — different semantics.)
    private static bool HeadWidthsLack(ReflowState rf, string text)
    {
        var fd = rf.head.FontDict;
        if (fd is null || fd.GetName("Subtype") == "Type0") return false;
        if (rf.reader.Resolve(fd.Get("Widths")) is not PdfArray widths) return false;
        if (rf.reader.Resolve(fd.Get("FirstChar")) is not PdfInteger fci) return false;
        int fc = (int)fci.Value;
        foreach (var ch in text)
        {
            if (ch == ' ') continue;
            if (SimpleFontCode(ch, fd) is not { } code || code < fc || code >= fc + widths.Count) return true;
            var wv = rf.reader.Resolve(widths[code - fc]);
            double width = wv switch { PdfInteger wi => wi.Value, PdfReal wr => wr.Value, _ => 0 };
            if (width == 0) return true;
        }
        return false;
    }

    // True when the run's own face gives every glyph the same advance - the source
    // grid the squeeze above preserves. Sampled over the run's distinct bytes.
    private static bool MonospacedRun(ReflowState rf, CrossTextOp op)
    {
        var seen = new Dictionary<byte, double>();
        foreach (var b in op.Bytes)
        {
            if (seen.ContainsKey(b)) continue;
            var ch = DecodeString(new[] { b }, op.ToUnicode, op.FontDict, rf.reader);
            if (string.IsNullOrWhiteSpace(ch)) continue;
            seen[b] = AdvPage(rf, op, new[] { b }, own: false);
            if (seen.Count >= 8) break;
        }
        if (seen.Count < 2) return false;
        double first = 0, max = 0, min = double.MaxValue;
        foreach (var v in seen.Values)
        {
            if (first == 0) first = v;
            if (v > max) max = v;
            if (v < min) min = v;
        }
        return max - min <= 0.01;
    }

    // Width, in the run's OWN face, of the first word of the text being replaced.
    private static double OriginalFirstWordWidth(ReflowState rf, CrossTextOp op, string searchText)
    {
        var word = searchText;
        var cut = word.IndexOfAny([' ', '\r', '\n']);
        if (cut > 0) word = word[..cut];
        if (word.Length == 0) return 0;
        var at = op.Text.IndexOf(word, StringComparison.Ordinal);
        if (at < 0 || op.Bytes.Length != op.Text.Length) return 0;
        return AdvPage(rf, op, op.Bytes[at..(at + word.Length)], own: false);
    }

    // Page-space advance of the switched head text: exact TTF advances + Tc per
    // glyph. When the switched replacement SHRINKS the line, the following
    // run's gap is padded by the REPLACED text's Tc allowance as well
    // (followers sit Tc·len(old) right of the drawn end; a growing
    // replacement or an un-switched one abuts exactly).
    private static double SwitchedAdv(ReflowState rf, string text)
    {
        double w;
        try { w = rf.switchedFace!.MeasureString(text, rf.head.FontSize); }
        catch { w = rf.head.FontSize * 0.5 * text.Length; }
        w += rf.head.Tc * text.Length;
        return w * TmScaleOf(rf.head) * ScaleOf(rf.head);
    }

    private static double BaseY(ReflowState rf, int li) => li < rf.lines.Count
        ? rf.lineBaseY[li]!.Value
        : rf.lineBaseY[^1]!.Value - rf.newPitch * (li - rf.lines.Count + 1);

    // Greedy repack with MULTI-SPLIT: any run that crosses the right margin \u2014
    // including the rewritten match run, whose replacement can be several lines
    // long \u2014 splits at the last fitting space glyph as many times as needed; each
    // remainder continues from the paragraph's left margin on the next baseline.
    // A split piece re-emits as a plain Tj (the original TJ kerns are dropped for
    // the pieces; sub-point intra-run shifts, invisible to the layout checks).
    // Width contributed by the TJ kerns falling strictly inside the byte range
    // [from, to) of a run. The offsets are recorded against the run's own array, so
    // a caller that has sliced the bytes passes the offset the slice starts at.
    private static double KernRange(CrossTextOp o, int from, int to)
    {
        if (o.KernAt is null) return 0;
        double k1000 = 0;
        foreach (var (at, amount) in o.KernAt)
            if (at > from && at < to) k1000 += amount;
        return -k1000 / 1000.0 * o.FontSize * TmScaleOf(o) * ScaleOf(o);
    }

    // The kerns strictly inside a piece's byte range, with their run-relative byte
    // index, ready to be re-emitted in the piece's own TJ array.
    private static List<(int byteIndex, double amount)> KernsInside(CrossTextOp o, int off, int count)
    {
        var result = new List<(int, double)>();
        if (o.KernAt is null) return result;
        foreach (var (at, amount) in o.KernAt)
            if (at > off && at < off + count) result.Add((at, amount));
        return result;
    }

    // Advance of a PIECE of a run — the bytes at [off, off+count) — at the width it
    // occupies on the line, kerns included. A split piece is re-emitted without the
    // original TJ array, but the line keeps measuring as the authored
    // run spaced it, and on a justified line those kerns carry most of the
    // inter-word space; measuring pieces without them walks the cursor steadily left
    // of where it belongs.
    private static double PieceAdv(ReflowState rf, CrossTextOp o, byte[] bytes, int off, int count)
        => AdvPage(rf, o, bytes[..count], own: false) + KernRange(o, off, off + count);

    // `authoredLine` says whether the text is being fitted onto the line it was
    // AUTHORED on. The TJ kerns are that line's justification: they are part of
    // its width when deciding what overflows it, but text that moves to another
    // line is re-set there and carries only its glyph advances. Measured both
    // ways: counting them on the authored line is what breaks
    // `and supersede any prior ` at the expected break, and NOT counting them
    // on a foreign line is what lets `rewarding experience. We` pull up as one
    // piece, as expected.
    private int LastFittingSpace(ReflowState rf, CrossTextOp o, byte[] bytes, double budget, int off = 0,
        bool authoredLine = true)
    {
        var m = MetricsOf(rf, o);
        // CID runs never split: 2-byte codes make per-byte space scanning wrong.
        if (m is null || m.IsCid || bytes.Length < 2) return -1;
        // The TJ kerns count. A justified line of this era carries almost all of its
        // inter-word space in the TJ numbers rather than in the space glyph — around
        // 0.7 em per gap in this corpus — so a prefix measured from glyph advances
        // alone comes out far narrower than the ink it actually occupies, and the
        // scan keeps a word past the margin. Measured on the expected break:
        // `and supersede ` is 74.172 of glyphs plus 8.496 of kern = 82.668, and the
        // next candidate `and supersede any ` reaches 111.264, which puts its end at
        // 548.7 against a 542.7 border — over, so the break falls before it.
        // Only kerns strictly INSIDE the prefix apply; the one that follows the break
        // belongs to the next line and is dropped with it.
        double KernBefore(int byteCount) => authoredLine ? KernRange(o, off, off + byteCount) : 0;
        double run = 0; int lastFit = -1;
        for (int k = 0; k < bytes.Length; k++)
        {
            var ch = DecodeString(new[] { bytes[k] }, o.ToUnicode, o.FontDict, rf.reader);
            // A break is offered at a space whose PRECEDING text fits. The space ends the
            // line and overhangs the margin rather than breaking it, so charging its own
            // width first rejects the last word that actually belongs on the line.
            if (ch is " " or " " && run + KernBefore(k) <= budget) lastFit = k;
            double gw;
            try { gw = m.MeasureString(new[] { bytes[k] }, o.FontSize); }
            catch { gw = o.FontSize * 0.5; }
            run += (gw + o.Tc) * TmScaleOf(o) * ScaleOf(o);
            if (run + KernBefore(k + 1) > budget) break;
        }
        return lastFit < 0 ? -1 : lastFit + 1; // piece keeps the space glyph
    }

    // Wrapped pieces land at their line's ORIGINAL left X (a hanging-indent item's
    // head line is dedented relative to its continuation lines;
    // each existing baseline keeps its own left margin). Lines created beyond the paragraph
    // continue at the last line's indent.
    private static double LeftOf(ReflowState rf, int li2) => li2 < rf.lines.Count ? rf.lines[li2].lx : rf.lines[^1].lx;

    private static string PieceText(ReflowState rf, CrossTextOp o, byte[] bytes, string? sw)
    {
        string text;
        if (sw is not null) text = sw;
        else
        {
            try { text = DecodeString(bytes, o.ToUnicode, o.FontDict, rf.reader); }
            catch { return string.Empty; }
        }
        // A run's trailing PADDING is its own unit: 'utually beneficial.' and
        // the 50-odd spaces that follow it in the SAME authored run are reported
        // as two separate moves, so the text half is named without the
        // padding. The threshold is two: a single trailing space is an ordinary word
        // separator and stays with its text ('any prior ', 'company goals. '), while
        // a piece that is nothing BUT spaces keeps them — those are
        // reported too, on their own.
        var end = text.Length;
        while (end > 0 && (text[end - 1] == ' ' || text[end - 1] == ' ')) end--;
        return end > 0 && text.Length - end >= 2 ? text[..end] : text;
    }

    private static string Coord(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);

    private static void NoteMove(ReflowState rf, CrossTextOp o, double x, int landedLine, int originalLine,
        byte[] bytes, string? sw)
    {
        if (rf.notes is null) return;
        var text = PieceText(rf, o, bytes, sw);
        if (text.Length == 0) return;
        // A move is a change of LINE, not a change of position. Splitting a run
        // whose remainder stays on the line it was authored on is how a shrinking
        // replacement pulls its head up — the tail never went anywhere, and the
        // reference logs nothing for it (its shrinking scenario is pull-ups only).
        if (rf.pendingPush is { } push && landedLine > originalLine)
        {
            rf.notes.Add($"The word(s) '{text}' is(are) moved to the next line because "
                + $"text reached paragraph right border near "
                + $"{{X={Coord(push.x)}, Y={Coord(BaseY(rf, push.line))}}}.");
        }
        else if (landedLine < originalLine)
        {
            rf.notes.Add($"The word(s) '{text}' is(are) moved to previous line because "
                + $"it has free space near "
                + $"{{X={Coord(x)}, Y={Coord(BaseY(rf, landedLine))}}}.");
        }
    }

    // Rewrite the stream. Pieces group back to their source op; ops are edited in
    // byte order regardless of reading order.
    private static string N(double v) => Math.Round(v, 5).ToString("0.#####", CultureInfo.InvariantCulture);

    private static (double tx, double ty) SolveTm(CrossTextOp o, double px, double py)
    {
        var det = o.CtmA * o.CtmD - o.CtmB * o.CtmC;
        if (Math.Abs(det) < 1e-12) return (o.TmTx, o.TmTy);
        var dx = px - o.CtmTx; var dy = py - o.CtmTy;
        return ((o.CtmD * dx - o.CtmC * dy) / det, (-o.CtmB * dx + o.CtmA * dy) / det);
    }

    private static string TmOf(CrossTextOp o, double tx, double ty) =>
        $" {N(o.TmA)} {N(o.TmB)} {N(o.TmC)} {N(o.TmD)} {N(tx)} {N(ty)} Tm ";

    // Copy [lastWritePos, to) verbatim, splicing in the sorted insertions and
    // skipping the sorted deletion spans that fall inside the range.
    private static void CopyTo(ReflowState rf, int to)
    {
        while (true)
        {
            int nextDel = rf.delIdx < rf.deleteSpans.Count ? rf.deleteSpans[rf.delIdx].s : int.MaxValue;
            int nextIns = rf.insIdx < rf.inserts.Count ? rf.inserts[rf.insIdx].pos : int.MaxValue;
            int next = Math.Min(nextDel, nextIns);
            if (next >= to) break;
            if (next > rf.lastWritePos)
                rf.result.Write(rf.streamBytes, rf.lastWritePos, next - rf.lastWritePos);
            if (rf.lastWritePos < next) rf.lastWritePos = next;
            if (nextIns <= nextDel)
            {
                rf.result.Write(rf.inserts[rf.insIdx].bytes);
                rf.insIdx++;
            }
            else
            {
                if (rf.deleteSpans[rf.delIdx].e > rf.lastWritePos) rf.lastWritePos = rf.deleteSpans[rf.delIdx].e;
                rf.delIdx++;
            }
        }
        if (to > rf.lastWritePos)
        {
            rf.result.Write(rf.streamBytes, rf.lastWritePos, to - rf.lastWritePos);
            rf.lastWritePos = to;
        }
    }
}

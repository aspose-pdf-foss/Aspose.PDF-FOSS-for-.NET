using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
    /// <summary>A follower shift a rewrite owes the stream: recorded while the run is
    /// rewritten (when its seat and advance are known) and applied once the whole walk is
    /// done, on the written bytes. <c>offset</c> is where the rewritten run ends in the
    /// OUTPUT; <c>carried</c> is how far the line matrix there already sits from what the
    /// stream states (a suffix re-anchored by an absolute Tm moved it).</summary>
    private readonly struct PendingShift
    {
        public readonly int offset;
        public readonly double baselineY, fromX, delta, carried;
        public PendingShift(int offset, double baselineY, double fromX, double delta, double carried)
        { this.offset = offset; this.baselineY = baselineY; this.fromX = fromX; this.delta = delta; this.carried = carried; }
    }

    /// <summary>The text-space advance a shown array draws with: glyph widths, the TJ
    /// kerns and the per-glyph character spacing. Null when a string cannot be measured.</summary>
    private static double? ShownAdvance(PdfArray arr, FontMetrics metrics, double fontSize, double tc)
    {
        double w = 0;
        foreach (var el in arr)
        {
            switch (el)
            {
                case PdfString s:
                    try { w += metrics.MeasureString(s.Value, fontSize); } catch { return null; }
                    w += tc * (metrics.IsCid ? (s.Value.Length + 1) / 2 : s.Value.Length);
                    break;
                case PdfInteger k: w -= k.Value * fontSize / 1000.0; break;
                case PdfReal k: w -= k.Value * fontSize / 1000.0; break;
            }
        }
        return w;
    }

    /// <summary>Under a reflowing replacement, note the width the rewritten run gained so
    /// the runs after it on the line move by as much when the walk is done. Only an
    /// upright, unrotated text matrix is handled: along it the gain is a plain X.</summary>
    private void RecordFollowerShift(ReflowReplaceState rr, PdfArray oldArr, PdfArray newArr, PdfReader reader)
    {
        if (!ReflowLineOnReplace || rr.tmB != 0 || rr.tmC != 0 || rr.tmA <= 0) return;
        if (rr.currentFontDict is null || rr.currentFontSize <= 0) return;
        // One metrics lookup for both measures: resolving a font's metrics is the
        // costly part, the two measures over it are not.
        FontMetrics? metrics;
        try { metrics = FontMetrics.FromFontDict(rr.currentFontDict, reader); } catch { return; }
        if (metrics is null) return;
        var oldAdv = ShownAdvance(oldArr, metrics, rr.currentFontSize, rr.tcSpacing);
        var newAdv = ShownAdvance(newArr, metrics, rr.currentFontSize, rr.tcSpacing);
        if (oldAdv is not { } o || newAdv is not { } n) return;
        var delta = (n - o) * rr.tmA;
        if (Math.Abs(delta) < SeatTolerance) return;
        rr.pendingShifts.Add(new PendingShift((int)rr.result.Position, rr.tmTy, rr.tmTx, delta, 0));
    }

    /// <summary>Apply the recorded shifts to the written stream, last first: a shift only
    /// touches bytes after its own offset, so the earlier offsets stay valid.</summary>
    private static byte[] ApplyPendingShifts(byte[] output, List<PendingShift> shifts)
    {
        for (var i = shifts.Count - 1; i >= 0; i--)
        {
            var s = shifts[i];
            output = ShiftFollowingBlocks(output, s.baselineY, s.fromX, s.delta, s.offset, s.carried);
        }
        return output;
    }
}

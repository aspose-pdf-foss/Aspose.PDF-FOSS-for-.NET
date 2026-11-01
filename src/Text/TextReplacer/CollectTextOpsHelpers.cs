using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
// Collect text ops: 
    private static FontMetrics? MetricsFor(CollectTextOpsState ct, PdfDictionary? fd)
    {
        if (fd is null) return null;
        if (ct.metricsByDict.TryGetValue(fd, out var m)) return m;
        FontMetrics? built = null;
        try { built = FontMetrics.FromFontDict(fd, ct.reader); } catch { /* a font program that does not parse leaves the fallback metrics in place */ }
        ct.metricsByDict[fd] = built;
        return built;
    }

    /// <summary>An unclipped op's clip box.</summary>
    private static readonly (double left, double bottom, double right, double top) NoClip =
        (double.NegativeInfinity, double.NegativeInfinity, double.PositiveInfinity, double.PositiveInfinity);

    /// <summary>Follows the clip box through q/Q and `re W n`: a W after a path of one
    /// rectangle intersects the clip with it (in page space, upright CTM only); any other
    /// clip path leaves the box as it was.</summary>
    private static void TrackClip(CollectTextOpsState ct, string op)
    {
        switch (op)
        {
            case "q": ct.clipStack.Push(ct.clip); break;
            case "Q": if (ct.clipStack.Count > 0) ct.clip = ct.clipStack.Pop(); break;
            case "re" when ct.ops2.Count >= 4 && ct.ctmB == 0 && ct.ctmC == 0:
                double x = ToDouble(ct.ops2[0].obj), y = ToDouble(ct.ops2[1].obj);
                double x1 = ct.ctmA * x + ct.ctmTx, x2 = ct.ctmA * (x + ToDouble(ct.ops2[2].obj)) + ct.ctmTx;
                double y1 = ct.ctmD * y + ct.ctmTy, y2 = ct.ctmD * (y + ToDouble(ct.ops2[3].obj)) + ct.ctmTy;
                ct.pathRect = (Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));
                break;
            case "m" or "l" or "c" or "v" or "y" or "h" or "re": ct.pathRect = null; break;
            case "W" or "W*":
                if (ct.pathRect is { } r)
                    ct.clip = (Math.Max(ct.clip.left, r.left), Math.Max(ct.clip.bottom, r.bottom),
                        Math.Min(ct.clip.right, r.right), Math.Min(ct.clip.top, r.top));
                break;
            case "n" or "f" or "f*" or "F" or "S" or "s" or "B" or "B*" or "b" or "b*": ct.pathRect = null; break;
        }
    }

    private static void AdvanceText(CollectTextOpsState ct, byte[] bytes, double kern1000)
    {
        var m = MetricsFor(ct, ct.curFontDict);
        if (m is null) return;
        double w;
        try { w = m.MeasureString(bytes, ct.curFontSize); } catch { return; }
        var glyphs = m.IsCid ? (bytes.Length + 1) / 2 : bytes.Length;
        w += ct.curTc * glyphs;
        if (!m.IsCid && ct.curTw != 0)
            foreach (var b in bytes.Where(b => b == 32))
                w += ct.curTw;
        w -= kern1000 / 1000.0 * ct.curFontSize;
        ct.tmTx += w * ct.tmA;
        ct.tmTy += w * ct.tmB;
    }
}

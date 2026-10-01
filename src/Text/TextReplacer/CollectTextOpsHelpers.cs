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
        try { built = FontMetrics.FromFontDict(fd, ct.reader); } catch { }
        ct.metricsByDict[fd] = built;
        return built;
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
            foreach (var b in bytes)
                if (b == 32) w += ct.curTw;
        w -= kern1000 / 1000.0 * ct.curFontSize;
        ct.tmTx += w * ct.tmA;
        ct.tmTy += w * ct.tmB;
    }
}

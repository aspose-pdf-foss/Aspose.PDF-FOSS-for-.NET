using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
    /// <summary>The stages of the OCR overlay rebuild: one recognised region, then one output line.</summary>
    private void EmitOverlayLine(OcrOverlayState oo, int li)
    {
        var ys = new List<double>(oo.lines[li].Count);
        foreach (var w in oo.lines[li]) ys.Add(w.y);
        ys.Sort();
        oo.baseline[li] = ys[ys.Count / 2];
        oo.bottom[li] = ys[0]; // smallest page-space y = deepest point
    }

    /// <summary>The stages of the OCR overlay rebuild: one recognised region, then one output line.</summary>
    private void CollectOverlayRegion(OcrOverlayState oo, (string text, double x, double y, double fs, double width) r)
    {
        if (oo.lines.Count > 0)
        {
            // Tolerance scales with the INCOMING run's own font: a big glyph (a "/")
            // reaches up to join a small-text line, but a small word will not reach up
            // to a big-font line above it (which would merge two distinct rows).
            var cur = oo.lines[^1];
            if (cur[0].y - r.y < 0.4 * r.fs)
            {
                cur.Add((r.text, r.x, r.fs, r.width, r.y));
                return;
            }
        }
        oo.lines.Add(new List<(string, double, double, double, double)> { (r.text, r.x, r.fs, r.width, r.y) });
    }
}

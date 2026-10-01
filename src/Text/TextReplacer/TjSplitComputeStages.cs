using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
// The stages of the TJ split computation: the advance of one element, the byte offset of a code, and the element walk.
    // Advance of a byte run as originally drawn: glyph widths plus per-glyph Tc
    // and, for single-byte encodings, per-space (byte 0x20) Tw — the PDF text
    // state contributions FontMetrics doesn't know about.
    private static double AdvOf(TjSplitComputeState js, byte[] bytes)
    {
        if (bytes.Length == 0) return 0;
        double w = js.metrics!.MeasureString(bytes, js.fontSize);
        int glyphs = js.isCid ? bytes.Length / 2 : bytes.Length;
        w += glyphs * js.tc;
        if (!js.isCid && js.tw != 0)
            foreach (var b in bytes)
                if (b == 0x20) w += js.tw;
        return w;
    }

    // Byte offset of a char offset within element i; -1 when the mapping is
    // ambiguous (decoded length doesn't line up with the byte count).
    private static int ByteOff(TjSplitComputeState js, int i, int charOff)
    {
        var dec = js.decoded[i]!;
        var bytes = ((PdfString)js.arr[i]).Value;
        if (charOff == 0) return 0;
        if (charOff == dec.Length) return bytes.Length;
        if (bytes.Length == dec.Length) return charOff;          // 1 byte/char
        if (bytes.Length == dec.Length * 2) return charOff * 2;  // 2-byte CID
        return -1;
    }

    /// <summary></summary>
    private bool MapTjMatchToElements(TjSplitComputeState js)
    {
        js.startEl = -1;
        js.endEl = -1;
        js.startOff = 0;
        js.endOff = 0;
        for (int i = 0; i < js.arr.Count; i++)
        {
            if (js.arr[i] is not PdfString || js.decoded[i] is null) continue;
            int len = js.decoded[i]!.Length;
            if (js.startEl < 0 && js.matchStart >= js.charStart[i] && js.matchStart < js.charStart[i] + len)
            { js.startEl = i; js.startOff = js.matchStart - js.charStart[i]; }
            if (js.matchEnd > js.charStart[i] && js.matchEnd <= js.charStart[i] + len)
            { js.endEl = i; js.endOff = js.matchEnd - js.charStart[i]; }
        }
        if (js.startEl < 0) return false;
        if (js.endEl < 0)
        {
            // Match ends at/after the last text — trailing run is empty only if
            // it really ends past every string element.
            for (int i = js.arr.Count - 1; i >= 0; i--)
                if (js.arr[i] is PdfString && js.decoded[i] is not null)
                {
                    if (js.matchEnd < js.charStart[i] + js.decoded[i]!.Length) return false;
                    js.endEl = i; js.endOff = js.decoded[i]!.Length;
                    break;
                }
            if (js.endEl < 0) return false;
        }
        return true;
    }
}

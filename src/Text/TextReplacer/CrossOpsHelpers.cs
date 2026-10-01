using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
// Cross-operator replace helpers: metrics, advances, match search, number formatting, range copy and font encodability.
    private static FontMetrics? MetricsOf(CrossReplaceState cx, CrossTextOp o)
    {
        if (o.FontDict is null) return null;
        if (cx.metricsCache.TryGetValue(o.FontDict, out var m)) return m;
        FontMetrics? built = null;
        try { built = FontMetrics.FromFontDict(o.FontDict, cx.reader); } catch { }
        cx.metricsCache[o.FontDict] = built;
        return built;
    }

    private static double Adv(CrossReplaceState cx, CrossTextOp o, byte[] bytes, bool own)
    {
        var m = MetricsOf(cx, o);
        double w;
        if (m is not null)
        {
            try { w = m.MeasureString(bytes, o.FontSize); }
            catch { w = o.FontSize * 0.5 * bytes.Length; }
        }
        else
            w = o.FontSize * 0.5 * bytes.Length;
        var glyphs = m?.IsCid == true ? (bytes.Length + 1) / 2 : bytes.Length;
        w += o.Tc * glyphs;
        if (own) w -= o.KernSum / 1000.0 * o.FontSize;
        return w;
    }

    // Enumerate match spans as (start, length) — regex match, or a literal scan that
    // is ELASTIC over the synthetic gap-spaces (charToOp < 0): a synthetic space
    // matches a needle space OR nothing, so both "05 DEC 2012" and the fragment's
    // segment-joined "05DEC2012" find the same span.
    private (int idx, int len) NextMatch(CrossReplaceState cx, int from)
    {
        if (_isRegex && _regexPattern is not null)
        {
            var m = _regexPattern.Match(cx.fullText, from);
            return m.Success ? (m.Index, m.Length) : (-1, 0);
        }
        if (cx.normalizedSearch.Length == 0) return (-1, 0);
        for (var st = Math.Max(0, from); st < cx.fullText.Length; st++)
        {
            int h = st, n = 0;
            while (n < cx.normalizedSearch.Length && h < cx.fullText.Length)
            {
                if (cx.fullText[h] == cx.normalizedSearch[n]) { h++; n++; continue; }
                if (h < cx.charToOp.Count && cx.charToOp[h] == SyntheticSpaceOp) { h++; continue; } // step over a synthetic space
                break;
            }
            if (n == cx.normalizedSearch.Length) return (st, h - st);
        }
        return (-1, 0);
    }

    private static bool IsPdfWhitespace(byte b) =>
        b is (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t' or (byte)'\f' or 0;

    private static string FormatNum(CrossReplaceState cx, double v) => Math.Round(v, 4).ToString("0.####", CultureInfo.InvariantCulture);

    private static void CopyRange(CrossReplaceState cx, int to)
    {
        while (cx.patches.Count > 0)
        {
            var start = cx.patches.Keys[0];
            if (start >= to) break;
            var (pEnd, pText) = cx.patches.Values[0];
            cx.patches.RemoveAt(0);
            if (start < cx.lastWrite) continue; // inside an already-replaced span
            // The recorded span opens on the whitespace before the operand; keep it,
            // or the patched number fuses with the operand before it ("1 0 0 1209.8").
            var tokenStart = start;
            while (tokenStart < pEnd && IsPdfWhitespace(cx.streamBytes[tokenStart])) tokenStart++;
            cx.result.Write(cx.streamBytes, cx.lastWrite, tokenStart - cx.lastWrite);
            cx.result.Write(pText, 0, pText.Length);
            cx.lastWrite = pEnd;
        }
        if (to > cx.lastWrite)
        {
            cx.result.Write(cx.streamBytes, cx.lastWrite, to - cx.lastWrite);
            cx.lastWrite = to;
        }
    }

    // Can every char of the replacement be encoded in the op's own font? (Reverse
    // ToUnicode coverage; keeps the replacement in the source face — and measured
    // with the source metrics — instead of switching to a fallback font.)
    private static bool CanEncodeInFont(CrossReplaceState cx, CrossTextOp o, string text)
    {
        if (o.ToUnicode is null)
            return text.All(c => c <= 0xFF); // simple Latin1 encoding
        var reverse = BuildReverseMap(o.ToUnicode);
        return text.All(c => reverse.ContainsKey(c.ToString()));
    }
}

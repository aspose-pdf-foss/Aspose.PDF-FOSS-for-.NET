using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
// The content-op renderer's device-space mapping and string advance helpers.
// The device mapping and glyph-advance helpers of the content render, lifted out of RenderContentToHtml; each takes the render state.
    private static (double x, double y) Dp(ContentRenderState ct, double x, double y) =>
        (x * ct.ctm.A + y * ct.ctm.C + ct.ctm.E, x * ct.ctm.B + y * ct.ctm.D + ct.ctm.F);

    private static (double X, double Y) Dev(ContentRenderState ct, double x, double y) =>
        (ct.ctm.A * x + ct.ctm.C * y + ct.ctm.E, ct.ctm.B * x + ct.ctm.D * y + ct.ctm.F);

    private static (double pen, double glyphs) StringAdvance(ContentRenderState ct, PdfString ps,
        List<(double pen, double glyph)>? perChar = null,
        List<int>? perCode = null)
    {
        if (ct.currentFontKey is null || !ct.fonts.TryGetValue(ct.currentFontKey, out var fi)
            || fi.AdvanceOf is null) return (double.NaN, double.NaN);
        var bytes = ps.Value;
        double pen = 0, glyphs = 0;
        if (fi.IsCidFont)
        {
            for (var i = 0; i + 1 < bytes.Length; i += 2)
            {
                var code = (bytes[i] << 8) | bytes[i + 1];
                var a = fi.AdvanceOf(code) * ct.fontSize;
                glyphs += a;
                pen += a + ct.charSpacing;
                perChar?.Add((a + ct.charSpacing, a));
                perCode?.Add(code);
            }
        }
        else
        {
            foreach (var b8 in bytes)
            {
                var a = fi.AdvanceOf(b8) * ct.fontSize;
                glyphs += a;
                var p1 = a + ct.charSpacing + (b8 == 32 ? ct.wordSpacing : 0);
                pen += p1;
                perChar?.Add((p1, a));
                perCode?.Add(b8);
            }
        }
        return (pen, glyphs);
    }

    /// <summary>A shown glyph whose ToUnicode is U+200B (ZERO WIDTH SPACE) is a word gap when
    /// it advances the pen: the fixture's producer maps Arial's space glyph to U+200B, and the
    /// gap it draws is an inter-word space to a reader. Probed on the reference writer's rule
    /// for U+00A0 (the same glyph mapped to a no-break space): a positive net advance is
    /// written as an ordinary U+0020 inside the same run; a glyph kerned back to a zero net
    /// advance draws nothing and is dropped from the text. The per-character lists stay
    /// aligned with the text; without them the advance is unknown and the gap reads as a space.</summary>
    private static string FoldZeroWidthSpaces(string text,
        List<(double pen, double glyph)>? perChar, List<int>? perCode)
    {
        if (text.IndexOf(ZeroWidthSpace) < 0) return text;
        var sb = new StringBuilder(text.Length);
        var aligned = perChar is not null && perChar.Count == text.Length;
        for (int i = 0, k = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch != ZeroWidthSpace) { sb.Append(ch); k++; continue; }
            var pen = aligned ? perChar![k].pen : double.NaN;
            if (double.IsNaN(pen) || pen > ZeroAdvanceTolerance) { sb.Append(' '); k++; continue; }
            if (aligned) { perChar!.RemoveAt(k); perCode?.RemoveAt(k); }
        }
        return sb.ToString();
    }

    /// <summary>U+200B ZERO WIDTH SPACE as a shown glyph's ToUnicode value.</summary>
    private const char ZeroWidthSpace = '​';
    /// <summary>A net pen advance below this (pt) is a glyph kerned back onto its own origin.</summary>
    private const double ZeroAdvanceTolerance = 0.01;

    private static (List<(double pen, double glyph)> perChar, List<int> perCode)? DecodeAligned(ContentRenderState ct, 
        PdfString ps, string wholeText)
    {
        if (ct.currentFontKey is null || !ct.fonts.TryGetValue(ct.currentFontKey, out var fi)
            || fi.AdvanceOf is null) return null;
        var bytes = ps.Value;
        var step = fi.IsCidFont ? 2 : 1;
        var sbT = new StringBuilder();
        var pcs = new List<(double pen, double glyph)>();
        var codes = new List<int>();
        for (var i = 0; i + step - 1 < bytes.Length; i += step)
        {
            var code = step == 2 ? (bytes[i] << 8) | bytes[i + 1] : bytes[i];
            var seg = step == 2 ? new[] { bytes[i], bytes[i + 1] } : new[] { bytes[i] };
            var dec = DecodeString(new PdfString(seg), ct.currentFontKey, ct.fonts);
            if (dec.Length == 0) continue;
            var a = fi.AdvanceOf(code) * ct.fontSize;
            var p1 = a + ct.charSpacing + (step == 1 && code == 32 ? ct.wordSpacing : 0);
            for (var k = 0; k < dec.Length; k++)
            {
                sbT.Append(dec[k]);
                pcs.Add(k == 0 ? (p1, a) : (0.0, 0.0));
                codes.Add(code);
            }
        }
        return sbT.ToString() == wholeText ? (pcs, codes) : null;
    }
}

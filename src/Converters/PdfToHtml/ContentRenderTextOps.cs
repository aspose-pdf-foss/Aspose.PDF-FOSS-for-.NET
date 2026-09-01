using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void ShowTextOp(ContentRenderState ct, string op)
    {
        if (ct.operands.Count >= 1 && ct.operands[0] is PdfString s)
        {
            var text = DecodeString(s, ct.currentFontKey, ct.fonts);
            var pc = new List<(double pen, double glyph)>();
            var pcodes = new List<int>();
            var (adv, ext) = StringAdvance(ct, s, pc, pcodes);
            var ok = pc.Count == text.Length;
            if (!ok && pc.Count > 0 && DecodeAligned(ct, s, text) is { } al)
            { pc = al.perChar; pcodes = al.perCode; ok = pc.Count == text.Length; }
            ShowRun(ct, ct.fonts, ct.sb, ct.pageHeight, ct.pageWidth, ct.saveTransparentTexts, ct.emCompensation, ct.textOnly, ct.styleReg, ct.classNamer, ct.linkTargets, ct.rotReg, ct.pageLLX, ct.yTopRef, ct.zCounter, ct.pageTurnedOver, text, adv, ext, ok ? pc : null, ok ? pcodes : null);
            if (!double.IsNaN(adv))
            { ct.tm.Concat(1, 0, 0, 1, adv, 0); ct.tx = ct.tm.E; ct.ty = ct.tm.F; }
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void ShowTextArrayOp(ContentRenderState ct, string op)
    {
        if (ct.operands.Count >= 1 && ct.operands[0] is PdfArray arr)
        {
            var tjText = new StringBuilder();
            double tjAdv = 0, tjExt = 0;
            var tjChars = new List<(double pen, double glyph)>();
            var tjCodes = new List<int>();
            void TjKern(double num)
            {
                ct.pendingTjNum += num;
                var d = -num / 1000.0 * ct.fontSize;
                if (!double.IsNaN(tjAdv)) tjAdv += d;
                if (!double.IsNaN(tjExt)) tjExt += d;
                if (num < -100)
                {
                    // a deep kern is a synthesized word space
                    // whose advance IS the kern gap
                    tjText.Append(' ');
                    tjChars.Add((d, d));
                    tjCodes.Add(-1);
                }
                else if (tjChars.Count > 0)
                {
                    // a small kern tightens the preceding advance
                    var lastC = tjChars[^1];
                    tjChars[^1] = (lastC.pen + d, lastC.glyph + d);
                }
            }
            foreach (var item in arr)
            {
                if (item is PdfString ts)
                {
                    var t0 = tjText.Length;
                    var itemText = DecodeString(ts, ct.currentFontKey, ct.fonts);
                    tjText.Append(itemText);
                    var itemChars = new List<(double pen, double glyph)>();
                    var itemCodes = new List<int>();
                    var (a, e) = StringAdvance(ct, ts, itemChars, itemCodes);
                    // keep tjChars aligned with tjText even when a
                    // decode expands/contracts the char count
                    if (itemChars.Count != itemText.Length && itemChars.Count > 0
                        && DecodeAligned(ct, ts, itemText) is { } alItem)
                    {
                        itemChars = alItem.perChar;
                        itemCodes = alItem.perCode;
                    }
                    if (itemChars.Count == tjText.Length - t0)
                    {
                        tjChars.AddRange(itemChars);
                        tjCodes.AddRange(itemCodes);
                    }
                    else
                    {
                        for (var fill = t0; fill < tjText.Length; fill++)
                        {
                            tjChars.Add((double.NaN, double.NaN));
                            tjCodes.Add(-1);
                        }
                    }
                    tjAdv = double.IsNaN(tjAdv) || double.IsNaN(a)
                        ? double.NaN : tjAdv + a;
                    tjExt = double.IsNaN(tjExt) || double.IsNaN(e)
                        ? double.NaN : tjExt + e;
                }
                else if (item is PdfInteger ti)
                    TjKern(ti.Value);
                else if (item is PdfReal tr)
                    TjKern(tr.Value);
            }
            if (tjText.Length > 0)
            {
                var pcOk = !double.IsNaN(tjAdv) && tjChars.Count == tjText.Length;
                if (pcOk)
                    foreach (var e2 in tjChars)
                        if (double.IsNaN(e2.pen)) { pcOk = false; break; }
                ShowRun(ct, ct.fonts, ct.sb, ct.pageHeight, ct.pageWidth, ct.saveTransparentTexts, ct.emCompensation, ct.textOnly, ct.styleReg, ct.classNamer, ct.linkTargets, ct.rotReg, ct.pageLLX, ct.yTopRef, ct.zCounter, ct.pageTurnedOver, tjText.ToString(), tjAdv, tjExt,
                    pcOk ? tjChars : null, pcOk ? tjCodes : null);
            }
            if (!double.IsNaN(tjAdv))
            { ct.tm.Concat(1, 0, 0, 1, tjAdv, 0); ct.tx = ct.tm.E; ct.ty = ct.tm.F; }
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void NextLineShowTextOp(ContentRenderState ct, string op)
    {
        // Move to next line and show string
        ct.tlm.Concat(1, 0, 0, 1, 0, -(ct.hasLeading ? ct.leading : ct.fontSize * 1.2));
        ct.tm.CopyFrom(ct.tlm);
        ct.tx = ct.tm.E; ct.ty = ct.tm.F;
        if (ct.operands.Count >= 1 && ct.operands[0] is PdfString qs)
        {
            var text = DecodeString(qs, ct.currentFontKey, ct.fonts);
            var qc = new List<(double pen, double glyph)>();
            var qcodes = new List<int>();
            var (adv, ext) = StringAdvance(ct, qs, qc, qcodes);
            var qok = qc.Count == text.Length;
            if (!qok && qc.Count > 0 && DecodeAligned(ct, qs, text) is { } alq)
            { qc = alq.perChar; qcodes = alq.perCode; qok = qc.Count == text.Length; }
            ShowRun(ct, ct.fonts, ct.sb, ct.pageHeight, ct.pageWidth, ct.saveTransparentTexts, ct.emCompensation, ct.textOnly, ct.styleReg, ct.classNamer, ct.linkTargets, ct.rotReg, ct.pageLLX, ct.yTopRef, ct.zCounter, ct.pageTurnedOver, text, adv, ext, qok ? qc : null, qok ? qcodes : null);
            if (!double.IsNaN(adv))
            { ct.tm.Concat(1, 0, 0, 1, adv, 0); ct.tx = ct.tm.E; ct.ty = ct.tm.F; }
        }
    }
}

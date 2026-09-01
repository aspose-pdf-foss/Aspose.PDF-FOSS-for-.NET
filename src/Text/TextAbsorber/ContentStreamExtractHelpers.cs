using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
// The helpers of the content-stream text extraction, lifted out of ExtractTextFromContentStream.
    // Consume the next ActualText slice for a show inside a Type3 span and
    // record it as a grid run at the given device position.
    private void CollectType3SpanRun(ExtractState xs, int rawLen, double xDev, double yDev, double fsDev, double wDev)
    {
        if (xs.atSpan is null || rawLen <= 0) return;
        var take = Math.Min(rawLen, xs.atSpan.Length - xs.atOffset);
        if (take <= 0) return;
        var slice = xs.atSpan.Substring(xs.atOffset, take).Replace('\t', ' ');
        xs.atOffset += take;
        _ocrRuns.Add((slice, xDev, yDev, fsDev, wDev));
        _type3SpanRuns++;
    }

    private bool Type3SpanActive(ExtractState xs) => _collectOcrRuns && xs.atSpan is not null
        && xs.currentToUnicode is not null
        && xs.currentFontDict?.GetName("Subtype") == "Type3";

    // The single character a show operand (string or TJ array) decodes to,
    // or null when it decodes to zero or 2+ characters.
    private char? DecodedSingleShowChar(ExtractState xs, PdfObject showOperand)
    {
        string d = string.Empty;
        if (showOperand is PdfString ps1)
            d = NormalizeDecoded(DecodeString(ps1.Value, xs.currentToUnicode, xs.currentFontDict, xs.reader, xs.useFontEngine));
        else if (showOperand is PdfArray pa1)
            foreach (var it in pa1)
            {
                if (it is not PdfString ps2) continue;
                d += NormalizeDecoded(DecodeString(ps2.Value, xs.currentToUnicode, xs.currentFontDict, xs.reader, xs.useFontEngine));
                if (d.Length > 1) break;
            }
        return d.Length == 1 ? d[0] : null;
    }

    // True when the pending single-char ActualText should yield to the
    // show's own decode (same letter, different case only).
    private bool ActualTextYieldsToDecode(ExtractState xs, PdfObject showOperand)
        => DecodedSingleShowChar(xs, showOperand) is char dc
           && dc != xs.actualText![0]
           && char.ToUpperInvariant(dc) == char.ToUpperInvariant(xs.actualText[0]);

    private bool ReplaceOccludedPrevRun(ExtractState xs, string runText, double startPageX, double pageWidth, double baselineY)
    {
        var effFs = Math.Abs(_currentLineEffFs) > 0.001 ? Math.Abs(_currentLineEffFs) : xs.fontSize;
        var llx = Math.Min(startPageX, startPageX + pageWidth);
        var urx = Math.Max(startPageX, startPageX + pageWidth);
        var lly = baselineY - 0.2 * effFs;
        var ury = baselineY + 0.7 * effFs;
        var replaced = false;
        if (xs.textRenderMode != 3 && xs.textRenderMode != 7
            && runText == xs.dedupPrevText && xs.dedupPrevOffset >= 0
            && xs.dedupPrevOffset + runText.Length <= _text.Length)
        {
            var area = (xs.dedupPrevUrx - xs.dedupPrevLlx) * (xs.dedupPrevUry - xs.dedupPrevLly);
            var ix = Math.Min(urx, xs.dedupPrevUrx) - Math.Max(llx, xs.dedupPrevLlx);
            var iy = Math.Min(ury, xs.dedupPrevUry) - Math.Max(lly, xs.dedupPrevLly);
            if (area > 0.01 && ix > 0 && iy > 0 && ix * iy > area * 0.55)
            {
                // The victim must still be the output's tail (at most trailing
                // spaces after it) — a line break or other run in between
                // means the copies aren't an adjacent duplicate stack.
                var tailIsVictim = true;
                for (var t = 0; t < runText.Length && tailIsVictim; t++)
                    if (_text[xs.dedupPrevOffset + t] != runText[t]) tailIsVictim = false;
                for (var t = xs.dedupPrevOffset + runText.Length; t < _text.Length && tailIsVictim; t++)
                    if (_text[t] != ' ') tailIsVictim = false;
                if (tailIsVictim)
                {
                    _text.Length = xs.dedupPrevOffset;
                    while (_pageRunSpans.Count > 0 && _pageRunSpans[^1].Offset >= xs.dedupPrevOffset)
                        _pageRunSpans.RemoveAt(_pageRunSpans.Count - 1);
                    replaced = true;
                }
            }
        }
        xs.dedupPrevText = runText;
        xs.dedupPrevOffset = -1;               // the caller stamps the append offset
        xs.dedupPrevLlx = llx; xs.dedupPrevLly = lly; xs.dedupPrevUrx = urx; xs.dedupPrevUry = ury;
        return replaced;
    }

    // Line-level position filter (page bounds + search rectangle), evaluated at
    // every line reposition (Tm/Td/T*). Upright text filters on the baseline Y
    // (X is clipped per glyph); sideways text swaps the roles — the baseline's
    // page X is the line coordinate and the advance axis is clipped per glyph.
    private bool LineFiltered(ExtractState xs, double upY)
    {
        // tmE/tmF are TRUE page coordinates (composed Tm×CTM) — no cm re-add.
        // The upright branch composes the CTM's linear scale too: content nested
        // in a scaled Form XObject (resized page content invoked via
        // "0.6 0 0 0.6 tx ty cm /Fm Do") reports text-space line coordinates,
        // and translation alone would test the wrong band of the page.
        var cmSy = System.Math.Abs(xs.localCmD) > 1e-9 ? xs.localCmD : 1.0;
        var cmSx = System.Math.Abs(xs.cmLa) > 1e-9 ? xs.cmLa : 1.0;
        var py = xs.tmRotated ? xs.tmF : upY * cmSy + xs.localCmTy;
        var px = xs.tmRotated ? xs.tmE : xs.tlmX * cmSx + xs.localCmTx;
        var skip = false;
        // Page bounds filter the BASELINE axis only at line level — the
        // advance axis is clipped per glyph, so a line entering from
        // off-page keeps its on-page portion.
        if (xs.pageBounds is not null)
            skip = xs.tmRotated
                ? px < xs.pageBounds[0] - 1 || px > xs.pageBounds[2] + 1
                : py < xs.pageBounds[1] - 1 || py > xs.pageBounds[3] + 1;
        if (!skip && xs.searchRect is not null)
        {
            // The TOP edge tests the line's ASCENT BOX, not the bare baseline:
            // a row whose ascenders poke above the window is out (probed on the
            // reference extractor with a Helvetica ladder against URY: fs 10
            // keeps y ≤ 192, drops 193 — the AFM ascender 0.718 em; the same
            // law at fs 20 and on Type3 rows). The BOTTOM edge stays
            // baseline-based (a baseline ON LLY is kept).
            var fsEffUp = xs.fontSize * Math.Abs(xs.tmRotated ? xs.tmN : xs.tmD > 0 ? xs.tmD : xs.tmN)
                          * Math.Abs(cmSy);
            var ascEm = xs.currentMetrics is { Ascent: > 0 } m ? m.Ascent / 1000.0 : 0.718;
            skip = xs.tmRotated
                ? px < xs.searchRect.LLX || px > xs.searchRect.URX
                : py < xs.searchRect.LLY || py + ascEm * fsEffUp > xs.searchRect.URY;
        }
        if (GridDebug && xs.searchRect is not null)
            Console.Error.WriteLine($"[linefilt] upY={upY:F2} py={py:F2} px={px:F2} tmF={xs.tmF:F2} tmE={xs.tmE:F2} cmTy={xs.localCmTy:F2} cmD={xs.localCmD:F2} rot={xs.tmRotated} skip={skip}");
        return skip;
    }

    // Tc/Tw contribution to a whole run's advance (PDF 32000 §9.4.4: each
    // glyph advances w·fs + Tc, plus Tw for byte 32 in a 1-byte font).
    // MeasureString returns only the Σ w·fs part, so a Tc-spaced run's pen
    // otherwise drifts by n·Tc — enough to flip the word-gap law both ways
    // ("Leverings Datum" read as one word at Tc −0.035; a positive-Tc run
    // read as two).
    private double SpacingAdvance(ExtractState xs, byte[] bytes)
    {
        if (xs.charSpacing == 0 && xs.wordSpacing == 0) return 0;
        var isCidRun = xs.currentMetrics?.IsCid ?? false;
        var n = isCidRun ? bytes.Length / 2 : bytes.Length;
        var adv = n * xs.charSpacing;
        if (!isCidRun && xs.wordSpacing != 0)
            foreach (var b in bytes)
                if (b == 32) adv += xs.wordSpacing;
        return adv;
    }

    // Sideways-text glyph clip: keep glyphs whose advance span (which runs along
    // the page Y axis for rotated text) lies inside the rectangle's Y band. The
    // X band was already enforced at line level by LineFiltered.
    private void AppendClippedRunRot(ExtractState xs, StringBuilder sb, byte[] bytes, ref double penText)
    {
        const double eps = 0.05;
        var isCid = xs.currentMetrics?.IsCid ?? false;
        var step = isCid ? 2 : 1;
        for (var i = 0; i + step - 1 < bytes.Length; i += step)
        {
            var code = isCid ? ((bytes[i] << 8) | bytes[i + 1]) : bytes[i];
            var seg = isCid ? new[] { bytes[i], bytes[i + 1] } : new[] { bytes[i] };
            var glyph = NormalizeDecoded(DecodeString(seg, xs.currentToUnicode, xs.currentFontDict, xs.reader, xs.useFontEngine), foldNbsp: false);
            var w = ((xs.currentMetrics is not null
                ? xs.currentMetrics.GetWidth(code) * xs.fontSize / 1000.0
                : xs.fontSize * 0.5 * Math.Max(1, glyph.Length))
                + xs.charSpacing + (!isCid && code == 32 ? xs.wordSpacing : 0)) * xs.horizScale;
            // Distance from the CURRENT line origin (tlmX), whose page position is
            // (tmE, tmF) — Td displacements are already baked into tmF, so measuring
            // from the Tm-time tmOriginX would double-count them.
            var d0 = penText - xs.tlmX;
            var y0 = xs.tmF + d0 * xs.tmBr;
            var y1 = xs.tmF + (d0 + w) * xs.tmBr;
            var lo = Math.Min(y0, y1);
            var hi = Math.Max(y0, y1);
            if (lo >= xs.clipRect!.LLY - eps && hi <= xs.clipRect.URY + eps)
                sb.Append(glyph);
            penText += w;
        }
    }
}

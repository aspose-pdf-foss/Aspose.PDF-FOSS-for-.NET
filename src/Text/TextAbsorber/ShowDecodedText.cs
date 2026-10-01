using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
    /// <summary>Show text: the decoded string measured, clipped and appended to the extraction.</summary>
    private void ShowDecodedText(ExtractState xs, PdfString tjStr)
    {
        var fullDecoded = ApplyRtlIfPureRtl(NormalizeDecoded(DecodeString(tjStr.Value, xs.currentToUnicode, xs.currentFontDict, xs.reader, xs.useFontEngine), foldNbsp: xs.searchRect is null));
        if (xs.currentFontNonAgl)
            RecordAglError(xs.currentFontName, fullDecoded,
                xs.tmE + (xs.tx - xs.tlmX) * xs.tmAr, xs.tmY + xs.localCmTy);
        // When a search rectangle is active, clip the run to the
        // glyphs whose advance box falls inside it (page space).
        // Sideways text clips along its advance axis (page Y).
        var clipRot = xs.clipRect is not null && xs.tmRotated && xs.currentMetrics is not null;
        var clipping = clipRot || (xs.clipRect is not null && xs.tmD > 0 && xs.currentMetrics is not null);
        var decoded = fullDecoded;
        // A left-clipped run starts, for layout purposes, at its first
        // surviving glyph — the off-page prefix neither indents the line
        // nor widens the gap to the previous run.
        var txClip = xs.tx;
        if (clipping)
        {
            var clip = new StringBuilder();
            var pen = xs.tx;
            if (clipRot)
                pen = AppendClippedRunRot(xs, clip, tjStr.Value, pen);
            else
            {
                (pen, var keptStart) = AppendClippedRun(clip, tjStr.Value, xs.currentToUnicode, xs.currentFontDict, xs.reader, xs.useFontEngine, xs.currentMetrics, xs.fontSize, xs.horizScale, xs.clipRect!, xs.tmOriginX, xs.tmA, xs.localCmTx, xs.cmLa, pen, xs.charSpacing, xs.wordSpacing, xs.blankClip, dropLeadingSpaces: xs.searchRect is not null && !xs.blankClip
                        && (_text.Length == 0 || _text[^1] == '\n'));
                if (!double.IsNaN(keptStart)) txClip = keptStart;
            }
            decoded = clip.ToString();
        }
        var measuredWidth = xs.currentMetrics?.MeasureString(tjStr.Value, xs.fontSize);
        var width = ((measuredWidth ?? (xs.fontSize * 0.5 * fullDecoded.Length))
            + SpacingAdvance(xs, tjStr.Value)) * xs.horizScale;
        if (!clipping || decoded.Length > 0)
        {
            EmitDecodedRun(xs, tjStr, decoded, fullDecoded, width, txClip, clipping);
        }
        // Capture invisible (Tr 3) runs (with their rendered advance) for
        // hOCR-overlay reconstruction.
        if (_collectOcrRuns && xs.textRenderMode == 3 && fullDecoded.Length > 0)
            _ocrRuns.Add((fullDecoded,
                xs.tmOriginX + (xs.tx - xs.tmOriginX) * xs.tmA + xs.localCmTx, xs.tmY, xs.fontSize, width));
        xs.lastRunEndDevX = xs.tmRotated ? xs.tmOriginX + (xs.tx - xs.tmOriginX) * xs.tmA + xs.localCmTx + width * xs.tmA : double.NaN;
        xs.lastRunEndPageX = xs.tmRotated ? double.NaN : xs.tmE + (xs.tx + width - xs.tlmX) * xs.tmAr;
        xs.lastRunStartPageX = xs.tmRotated ? double.NaN : xs.tmE + (xs.tx - xs.tlmX) * xs.tmAr;
        xs.lastRunEndX = xs.tx + width * (xs.tmRotated ? xs.tmA : 1.0); // rotated: advance projects through the axis norm
        xs.lastRunEstWidth = width;
        xs.lastHadMetrics = measuredWidth.HasValue;
        xs.tx += width;
        // Track rendered Y so subsequent '/"/'Tm' can distinguish
        // same-row column repositioning from real line advances.
        xs.lastRenderedY = xs.tmY; xs.lastRenderedFs = xs.fontSize * (xs.tmRotated ? xs.tmN : 1.0); xs.lastRenderedCmTy = xs.localCmTy;
    }
}

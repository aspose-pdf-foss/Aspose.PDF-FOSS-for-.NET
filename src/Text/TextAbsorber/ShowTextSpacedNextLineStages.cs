using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
    /// <summary>The stage of the spaced next-line show operator: emitting the run's text as ActualText or glyphs.</summary>
    private void EmitSpacedNextLineText(ExtractState xs, PdfString qStr, bool sameRow, double newY)
    {
        if (xs.actualText is not null)
        {
            if (!xs.actualTextUsed)
            {
                AppendShowText(xs.actualText);
                xs.actualTextUsed = true;
            }
            // Advance the pen over the replaced glyphs (see the Tj note).
            xs.tx += (xs.currentMetrics?.MeasureString(qStr.Value, xs.fontSize)
                   ?? xs.fontSize * 0.5 * qStr.Value.Length) * xs.horizScale;
        }
        else
        {
            var fullDecoded = ApplyRtlIfPureRtl(NormalizeDecoded(
                DecodeString(qStr.Value, xs.currentToUnicode, xs.currentFontDict, xs.reader, xs.useFontEngine), foldNbsp: xs.searchRect is null));
            // When a search rectangle is active, clip the run to the
            // glyphs whose advance box falls inside it (page space).
            var clipping = xs.clipRect is not null && xs.tmD > 0 && xs.currentMetrics is not null;
            var decoded = fullDecoded;
            if (clipping)
            {
                var clip = new StringBuilder();
                var pen = xs.tx;
                AppendClippedRun(clip, qStr.Value, xs.currentToUnicode, xs.currentFontDict,
                    xs.reader, xs.useFontEngine, xs.currentMetrics, xs.fontSize, xs.horizScale,
                    xs.clipRect!, xs.tmOriginX, xs.tmA, xs.localCmTx, xs.cmLa, pen, xs.charSpacing, xs.wordSpacing, xs.blankClip);
                decoded = clip.ToString();
            }
            if (!clipping || decoded.Length > 0)
            {
                TrackRowX(xs.tmRotated
                    ? (_pageRotDominant ? xs.tmOriginX + (xs.tx - xs.tmOriginX) * xs.tmA + xs.localCmTx : xs.tmE)
                    : (xs.tmOriginX + (xs.tx - xs.tmOriginX) * xs.tmA) * xs.cmLa + xs.cmLe);
                // Same-row continuation: insert proportional spaces for the
                // horizontal gap (Pure mode), mirrors Tj/TJ gap logic.
                if (sameRow && !double.IsNaN(xs.lastRunEndX)
                    && _text.Length > 0 && _text[^1] != ' ' && _text[^1] != '\n')
                {
                    var gap = xs.tx - xs.lastRunEndX;
                    var threshold = xs.fontSize * 0.2;
                    var spaces = ComputeSpaceCount(gap, threshold, xs.fontSize);
                    if (spaces > 0) _sawIntraLineGapSpaces = true;
                    for (int si = 0; si < spaces; si++) _text.Append(' ');
                }
                AppendShowText(decoded);
            }
            var measuredWidth = xs.currentMetrics?.MeasureString(qStr.Value, xs.fontSize);
            var width = (measuredWidth ?? (xs.fontSize * 0.5 * fullDecoded.Length)) * xs.horizScale;
            xs.lastRunEndDevX = xs.tmRotated ? xs.tmOriginX + (xs.tx - xs.tmOriginX) * xs.tmA + xs.localCmTx + width * xs.tmA : double.NaN;
            xs.lastRunEndPageX = xs.tmRotated ? double.NaN : xs.tmE + (xs.tx + width - xs.tlmX) * xs.tmAr;
            xs.lastRunStartPageX = xs.tmRotated ? double.NaN : xs.tmE + (xs.tx - xs.tlmX) * xs.tmAr;
            xs.lastRunEndX = xs.tx + width * (xs.tmRotated ? xs.tmA : 1.0); // rotated: advance projects through the axis norm
            xs.lastRunEstWidth = width;
            xs.lastHadMetrics = measuredWidth.HasValue;
            xs.lastDecodedLength = decoded.Length;
            xs.tx += width;
            xs.lastRenderedY = newY; xs.lastRenderedFs = xs.fontSize * (xs.tmRotated ? xs.tmN : 1.0); xs.lastRenderedCmTy = xs.localCmTy;
        }
    }
}

using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
    /// <summary>Show text: the run's span recorded for the page's row rebuild.</summary>
    private void RecordRunSpan(ExtractState xs, PdfString tjStr, string decoded, string fullDecoded, double txClip, bool clipping)
    {
        if (decoded.Length > 0)
        {
            var spanScale = xs.horizScale * Math.Abs(xs.tmRotated ? xs.tmA : xs.tmAr);
            _pageRunSpans.Add(new RunSpan(_text.Length, decoded.Length,
                xs.tmRotated ? (_pageRotDominant ? xs.tmOriginX + (xs.tx - xs.tmOriginX) * xs.tmA + xs.localCmTx : xs.tmE)
                          : xs.tmE + (txClip - xs.tlmX) * xs.tmAr,
                (xs.currentMetrics?.MeasureString(tjStr.Value, xs.fontSize)
                 ?? (xs.fontSize * 0.5 * fullDecoded.Length)) * spanScale,
                !clipping && IsPureRtlRun(decoded),
                clipping ? null : BuildCharXs(tjStr.Value, xs.currentMetrics, xs.fontSize,
                    spanScale, decoded.Length, xs.charSpacing, xs.wordSpacing)));
        }
    }

    /// <summary>Show text: the run's page-space X captured and the line's grid origin tracked.</summary>
    private double TrackRunLineStart(ExtractState xs, string decoded, double txClip)
    {
        // In Pure mode, capture the current run's page-space X and keep the
        // per-line grid origin up to date before computing spacing.
        double runPageX = 0;
        if (_pageCellWidth > 0)
        {
            // Upright: composed device X (identical to the raw
            // expression under an identity CTM/Tm, correct under
            // scaled ones). Rotated keeps its projection frame on a
            // rotated-dominant page; a minority rotated run on an
            // upright page grids at its DEVICE x — the horizontal
            // position of its vertical baseline.
            runPageX = xs.tmRotated
                ? (_pageRotDominant
                    ? xs.tmOriginX + (xs.tx - xs.tmOriginX) * xs.tmA + xs.localCmTx
                    : xs.tmE)
                : xs.tmE + (txClip - xs.tlmX) * xs.tmAr;
            // An upside-down line (negative advance) reads from its RIGHT edge: its start
            // is tracked mirrored, so the leftmost-run rule finds the reading-order start
            // and the negative-origin rule gives it no leading grid column (the reference
            // extracts a 180-degree stamp's rows with no indent).
            if (!xs.tmRotated && xs.tmAr < 0) runPageX = -runPageX;
            TrackLineStart(runPageX, string.IsNullOrWhiteSpace(decoded));
        }
        return runPageX;
    }
}

using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
    /// <summary>Builds a TextState from the first run's font properties.</summary>
    private static TextState BuildTextState(RawTextRun run)
    {
        // Effective (device) font size: the Tm up-axis composed with the CTM —
        // a page that scales its content via `cm` (e.g. 0.75) reports the scaled
        // size (Tf 21.33 under 0.75 cm → 16).
        var upX = run.TmC * run.Ctm.A + run.TmD * run.Ctm.C;
        var upY = run.TmC * run.Ctm.B + run.TmD * run.Ctm.D;
        var tmScale = Math.Sqrt(upX * upX + upY * upY);
        var effectiveFs = tmScale > 0.001 && Math.Abs(tmScale - 1.0) > 0.001
            ? run.FontSize * tmScale : run.FontSize;
        var ts = new TextState
        {
            FontSize = (float)effectiveFs,
            FontName = run.FontName,
            RenderingMode = (Aspose.Pdf.Text.TextRenderingMode)run.RenderingMode,
                LineWidth = run.LineWidth,
            IsBold = run.IsBold,
            IsItalic = run.IsItalic,
            Font = run.FontInfoObj ?? FontInfo.DefaultHelvetica,
            TextRise = run.TextRise,
            IsSuperscript = run.TextRise > 0,
            IsSubscript = run.TextRise < 0,
        };
        ts.SetCapturedForegroundColor(ForegroundColorOf(run));
        ts.StrokingColor = run.StrokingColor;
        // The run's spacing state (Tz is stored as a fraction; the property is a
        // percentage).
        ts.CharacterSpacing = (float)run.CharSpacing;
        ts.WordSpacing = (float)run.WordSpacing;
        ts.HorizontalScaling = (float)(run.HScaling * 100);
        ts.SourceTmScale = Math.Abs(run.TmD) > 1e-9 ? run.TmA / run.TmD : 1.0;
        return ts;
    }

    /// <summary>
    /// Shaped Arabic/Hebrew PRESENTATION FORMS are emitted (by the generator / HTML converter)
    /// in VISUAL order — the glyphs as drawn left-to-right — so a pure run of them extracts
    /// reversed from logical reading order. Reverse it back to logical. Scoped to presentation
    /// forms so raw Hebrew/Arabic (stored visually in source PDFs and matched visually by
    /// TextReplacer) is left untouched.
    /// </summary>
    private static string LogicalizeRtlPresentationForms(string text)
    {
        if (text.Length < 2) return text;
        var hasPresForm = false;
        foreach (var c in text)
        {
            if ((c >= 0x0590 && c <= 0x05FF) || (c >= 0xFB1D && c <= 0xFDFF) || (c >= 0xFE70 && c <= 0xFEFF))
                hasPresForm = true;
            else if ((c >= 0x0600 && c <= 0x06FF) || (c >= 0x0750 && c <= 0x077F))
            { /* a raw Arabic letter rides along in a shaped run: a ToUnicode that
                 maps the isolated alef to U+0627 rather than U+FE8D still describes
                 visual-order glyphs, and the whole run is reversed (glyphs
                 drawn ... FEF9 0627 report as 0627 FEF9 ...). Raw HEBREW
                 stays a TRIGGER: the HTML pipeline stores a Hebrew line as visual-
                 order raw letters in one run, and extraction returns it in
                 logical order — the mend writer cooperates by storing its own RTL
                 fragments REVERSED (logical), so this reversal hands them back in
                 the searchable visual order. */ }
            else if (c == ' ' || c == '\t' || c == '\r' || c == '\n'
                     || (c >= '!' && c <= '/') || (c >= ':' && c <= '@')
                     || (c >= '[' && c <= '`') || (c >= '{' && c <= '~'))
            { /* neutral punctuation / whitespace — allowed inside an RTL run */ }
            else
                return text; // an LTR letter or a raw (unshaped) RTL char → leave as-is
        }
        if (!hasPresForm) return text;
        var arr = text.ToCharArray();
        System.Array.Reverse(arr);
        return new string(arr);
    }

    /// <summary>
    /// Builds per-source-run TextSegments for a fragment, each with accurate
    /// position, rectangle, and text state derived from its source run.
    /// </summary>
    private static void BuildFragmentSegments(TextFragment fragment, List<RawTextRun> rawFragments,
        int[] runStartChar, int firstRunIdx, int lastRunIdx, int startCharIdx, int endCharIdx,
        List<int>? charToRun = null)
    {
        fragment.Segments.Clear();

        // With a char-to-run map, walk the match's CHAR RANGE and group consecutive
        // chars by run — correct for any run order in char space (a back-jump
        // PREPEND places a later-drawn run in front of an earlier one, so the
        // run-index range [first..last] can miss runs the match actually covers).
        if (charToRun is not null)
        {
            var cc = startCharIdx;
            while (cc <= endCharIdx && cc < charToRun.Count)
            {
                var ri = charToRun[cc];
                var gStart = cc;
                while (cc <= endCharIdx && cc < charToRun.Count && charToRun[cc] == ri) cc++;
                if (ri < 0 || ri >= rawFragments.Count) continue;
                var grun = rawFragments[ri];
                if (grun.Text == "\r\n") continue; // newline sentinels

                var gSegStart = gStart - runStartChar[ri];
                var gSegEnd = (cc - 1) - runStartChar[ri];
                if (gSegStart < 0) gSegStart = 0;
                if (gSegEnd >= grun.Text.Length) gSegEnd = grun.Text.Length - 1;
                if (gSegEnd < gSegStart) continue;

                var gText = grun.Text.Substring(gSegStart, gSegEnd - gSegStart + 1);
                var gSeg = BuildSegment(grun, gText, gSegStart, gSegEnd, ri);
                gSeg.Position = ComputeSegmentPosition(grun, gSegStart);
                gSeg.Rectangle = ComputeSegmentRectangle(grun, gText, gSegStart, gSegEnd);
                PopulateCharacters(gSeg, grun, gSegStart, gSegEnd);
                fragment.Segments.Add(gSeg);
            }
            if (fragment.Segments.Count == 0)
                fragment.Segments.Add(new TextSegment(fragment.Text));
            return;
        }

        for (var ri = firstRunIdx; ri <= lastRunIdx; ri++)
        {
            var run = rawFragments[ri];
            if (run.Text == "\r\n") continue; // skip newline sentinels

            // Determine the portion of this run that is part of the match
            var runStart = runStartChar[ri];
            var segStartInRun = (ri == firstRunIdx) ? startCharIdx - runStart : 0;
            var segEndInRun = (ri == lastRunIdx) ? endCharIdx - runStart : run.Text.Length - 1;
            if (segStartInRun < 0) segStartInRun = 0;
            if (segEndInRun >= run.Text.Length) segEndInRun = run.Text.Length - 1;
            if (segEndInRun < segStartInRun) continue;

            var segText = run.Text.Substring(segStartInRun, segEndInRun - segStartInRun + 1);
            var seg = BuildSegment(run, segText, segStartInRun, segEndInRun, ri);

            // Compute segment position with within-run offset and descent
            seg.Position = ComputeSegmentPosition(run, segStartInRun);

            // Compute segment bounding rectangle
            seg.Rectangle = ComputeSegmentRectangle(run, segText, segStartInRun, segEndInRun);

            // Populate per-character layout (position + glyph rectangle).
            PopulateCharacters(seg, run, segStartInRun, segEndInRun);

            fragment.Segments.Add(seg);
        }
        if (fragment.Segments.Count == 0)
            fragment.Segments.Add(new TextSegment(fragment.Text));
    }

    /// <summary>Creates a TextSegment from a run with text state properties.</summary>
    private static TextSegment BuildSegment(RawTextRun run, string text,
        int startInRun, int endInRun, int runIndex)
    {
        var upX_ = run.TmC * run.Ctm.A + run.TmD * run.Ctm.C;
            var upY_ = run.TmC * run.Ctm.B + run.TmD * run.Ctm.D;
            var tmScale = Math.Sqrt(upX_ * upX_ + upY_ * upY_);
        var effectiveFs = tmScale > 0.001 && Math.Abs(tmScale - 1.0) > 0.001
            ? run.FontSize * tmScale : run.FontSize;
        var seg = new TextSegment(text)
        {
            StartCharIndex = startInRun,
            EndCharIndex = endInRun,
            SourceRunIndex = runIndex,
        };
        seg.TextState.FontSize = (float)effectiveFs;
        seg.TextState.RawFontSize = (float)run.FontSize;
        seg.TextState.TmD = run.TmD;
        seg.TextState.FontName = run.FontName;
        seg.TextState.RenderingMode = (Aspose.Pdf.Text.TextRenderingMode)run.RenderingMode;
        seg.TextState.LineWidth = run.LineWidth;
        seg.TextState.StrokingColor = run.StrokingColor;
        seg.TextState.IsBold = run.IsBold;
        seg.TextState.IsItalic = run.IsItalic;
        seg.TextState.Font = run.FontInfoObj ?? FontInfo.DefaultHelvetica;
        seg.TextState.TextRise = run.TextRise;
        seg.TextState.IsSuperscript = run.TextRise > 0;
        seg.TextState.IsSubscript = run.TextRise < 0;
        // The run's spacing state is part of what the segment reports back
        // (Tz is stored as a fraction; the property is a percentage).
        seg.TextState.CharacterSpacing = (float)run.CharSpacing;
        seg.TextState.WordSpacing = (float)run.WordSpacing;
        seg.TextState.HorizontalScaling = (float)(run.HScaling * 100);
        seg.TextState.OwnerSegment = seg;
        return seg;
    }

    private static void PopulateCharacters(TextSegment seg, RawTextRun run,
        int segStartInRun, int segEndInRun)
    {
        seg.Characters.Clear();
        for (var ci = segStartInRun; ci <= segEndInRun && ci < run.Text.Length; ci++)
        {
            var charText = run.Text.Substring(ci, 1);
            var pos = ComputeSegmentPosition(run, ci);
            var rect = ComputeSegmentRectangle(run, charText, ci, ci);
            seg.Characters.Add(new CharInfo(pos, rect));
        }
    }

    /// <summary>Computes a segment's page-space position from its run and within-run offset.</summary>
    private static Position ComputeSegmentPosition(RawTextRun run, int segStartInRun)
    {
        double segX = run.X, segY = run.Y;
        if (segStartInRun > 0 && segStartInRun < run.Text.Length)
        {
            var prefW = MeasureRunPrefix(run, segStartInRun);
            segX = run.X + run.TmA * prefW * run.HScaling;
            segY = run.Y + run.TmB * prefW * run.HScaling;
        }
        // Apply descent offset — fall back to Standard-14 AFM descent
        double segDescentOff = 0;
        double effectiveDescent = 0;
        if (run.Metrics is not null && run.Metrics.Descent != 0)
            effectiveDescent = run.Metrics.Descent;
        else if (!string.IsNullOrEmpty(run.FontName))
            effectiveDescent = Standard14Fonts.GetDescent(run.FontName!);
        if (effectiveDescent != 0)
            segDescentOff = effectiveDescent * run.FontSize / 1000.0;
        var (px, py) = ApplyCtm(segX + run.TmC * segDescentOff,
                                 segY + run.TmD * segDescentOff, run.Ctm);
        return new Position(Q(px), Q(py));
    }

    /// <summary>Computes a segment's bounding rectangle from its run, text, and character range.</summary>
    private static Rectangle ComputeSegmentRectangle(RawTextRun run, string segText,
        int segStartInRun, int segEndInRun)
    {
        double segW;
        if (run.CharCumWidths is not null)
        {
            var segEndPos = Math.Min(segEndInRun + 1, run.CharCumWidths.Length - 1);
            segW = run.CharCumWidths[segEndPos]
                 - (segStartInRun < run.CharCumWidths.Length ? run.CharCumWidths[segStartInRun] : 0);
        }
        else if (run.Metrics is not null)
            segW = run.Metrics.MeasureString(segText, run.FontSize);
        else
            segW = EstimateWidth(segText, run.FontSize);

        // Segment boxes share the fragment's canonical line box.
        var (descentOff, segAscentH) = ComputeDescentAscent(run, coreFaceDescent: false);

        double segX = run.X, segY = run.Y;
        if (segStartInRun > 0 && segStartInRun < run.Text.Length)
        {
            var prefW = MeasureRunPrefix(run, segStartInRun);
            segX = run.X + run.TmA * prefW * run.HScaling;
            segY = run.Y + run.TmB * prefW * run.HScaling;
        }
        var scaledSegW = segW * run.HScaling;
        var (x1, y1) = ApplyCtm(segX + run.TmC * descentOff, segY + run.TmD * descentOff, run.Ctm);
        var (x2, y2) = ApplyCtm(segX + run.TmA * scaledSegW + run.TmC * segAscentH,
                                 segY + run.TmB * scaledSegW + run.TmD * segAscentH, run.Ctm);
        return new Rectangle(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));
    }

    /// <summary>
    /// A simple 3x2 affine matrix (a, b, c, d, e, f) for CTM tracking.
    /// Represents the transformation: [a b 0; c d 0; e f 1]
    /// </summary>
    private readonly record struct Matrix(double A, double B, double C, double D, double E, double F)
    {
        public static readonly Matrix Identity = new(1, 0, 0, 1, 0, 0);

        /// <summary>
        /// Multiply this matrix by another: this * other
        /// </summary>
        public Matrix Multiply(Matrix other)
        {
            return new Matrix(
                A * other.A + B * other.C,
                A * other.B + B * other.D,
                C * other.A + D * other.C,
                C * other.B + D * other.D,
                E * other.A + F * other.C + other.E,
                E * other.B + F * other.D + other.F
            );
        }
    }

    /// <summary>
    /// Apply a CTM matrix to a point.
    /// </summary>
    /// <summary>Quantize an extracted position coordinate through single precision.
    /// Text positions are computed in float, so the reported XIndent for e.g.
    /// text-space 355 under a 0.24 scale is 85.19999695…, a hair BELOW the
    /// decimal 85.2 — while exact double arithmetic lands a hair above. Position
    /// expectations (85.19 ± 0.01) sit right at that boundary, so extracted
    /// positions must take the same rounding.</summary>
    private static double Q(double v) => (float)v;

    /// <summary>Whether the run's CTM carries no rotation (pure translation/scale/flip).
    /// Only such runs may be compared in PAGE space: under a rotated CTM the text-space
    /// X advance leaks into page-Y (a flat-Tm glyph-per-op producer would split into one
    /// line per glyph), and a rotated page CTM likewise turns rotated-Tm labels' raw-Y
    /// baseline into a page-Y spread. Both must keep the raw text-space comparison.</summary>
    private static bool IsUprightCtm(RawTextRun run) =>
        Math.Abs(run.Ctm.B) <= 1e-4 * Math.Abs(run.Ctm.A);

    /// <summary>
    /// Compute the page-rotation CTM for a page, matching the TypeScript
    /// <c>pageRotationCtm</c> function.  Returns null for Rotate=0/unset.
    /// </summary>
    private static Matrix? PageRotationCtm(Page page)
    {
        var rotate = ((page.RotateDegrees % 360) + 360) % 360;
        if (rotate == 0) return null;
        var mb = page.MediaBox;
        var w = mb.URX - mb.LLX;
        var h = mb.URY - mb.LLY;
        return rotate switch
        {
            90  => new Matrix( 0, -1,  1,  0,  0, w),
            180 => new Matrix(-1,  0,  0, -1,  w, h),
            270 => new Matrix( 0,  1, -1,  0,  h, 0),
            _   => null,
        };
    }

    /// <summary>
    /// Check if the given point is contained within (or on the boundary of) a rectangle.
    /// </summary>
    private static bool RectangleContainsPoint(Rectangle rect, double x, double y)
        => x >= rect.LLX && x <= rect.URX && y >= rect.LLY && y <= rect.URY;
}

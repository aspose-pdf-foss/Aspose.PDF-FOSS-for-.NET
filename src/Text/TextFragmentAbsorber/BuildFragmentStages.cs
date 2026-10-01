using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
    /// <summary>Text fragment build: the fragment built from the run's clipped text and added to the results.</summary>
    private void EmitRunFragment(BuildFragmentsState bf, RawTextRun run)
    {
        var frag = new TextFragment(LogicalizeRtlPresentationForms(bf.clipText), bf.rect, bf.textState)
        {
            PageIndex = bf.pageIndex,
            Position = new Position(Q(bf.px), Q(bf.py)),
            SourcePage = bf.sourcePage,
            Form = bf.sourceForm,
            SourceXObjStream = run.SourceXObj,
            TextDirX = bf.tdx,
            TextDirY = bf.tdy,
            ExtractionCtm = new Aspose.Pdf.Matrix(run.Ctm.A, run.Ctm.B, run.Ctm.C, run.Ctm.D, run.Ctm.E, run.Ctm.F),
            ExtractionTmTy = run.TmBaseY,
            ReplaceOptions = TextReplaceOptions,
        };
        if (frag.Segments.Count > 0)
        {
            frag.Segments[1].EndCharIndex = bf.clipText.Length - 1;
            // The whole-run segment IS the run: it carries the fragment's box and
            // position ("fragment right border matches its last segment's
            // right border" holds for every extracted fragment).
            frag.Segments[1].Rectangle = bf.rect;
            // The whole run's glyph-bearing box; a run clipped to a search rectangle
            // keeps the clipped box as it is.
            if (bf.clipText == run.Text) SetInkBox(frag.Segments[1], run, 0, run.Text.Length - 1);
            else if (bf.clipText.Trim().Length > 0) frag.Segments[1].InkRectangle = bf.rect;
            // The position only for UPRIGHT runs: a segment position scopes later
            // edits to that baseline, and a rotated run's box corner is not on it
            // (an invisible vertical OCR run must still be found when deleted).
            if (Math.Abs(run.TmB) <= Math.Abs(run.TmA) && IsUprightCtm(run))
                frag.Segments[1].Position = new Position(Q(bf.px), Q(bf.py));
            // Per-character layout for the whole-run segment. The unclipped case
            // (the common one) maps one character per glyph exactly from the run
            // start; PopulateCharacters bounds the range to the run text length.
            PopulateCharacters(frag.Segments[1], run, 0, bf.clipText.Length - 1);
        }
        if (bf.capturedUl is { } ulr)
            frag.MarkCapturedUnderlineSource(ulr.RawX, ulr.RawY, ulr.RawW, ulr.RawH);
        if (bf.capturedBg is { } bgr)
            frag.MarkCapturedBackgroundSource(bgr.RawX, bgr.RawY, bgr.RawW, bgr.RawH, bgr.FillColor);
        _fragments.Add(frag);
    }

    /// <summary>Text fragment build: a run painted over by a later cover rectangle reported invisible.</summary>
    private void ApplyRunCoverOcclusion(BuildFragmentsState bf)
    {
        // Hidden-by-occlusion: a body-sized opaque fill painted AFTER this run that
        // fully covers its box hides it (redaction-style). Surface it through
        // TextState.Invisible — the run's
        // RenderingMode stays FillText.
        if (bf.coverRects is not null && bf.coverRects.Count > 0)
        {
            // Vertical slack scales with the glyph size: the nominal ascent box can
            // poke a point or two past a cover that visually swallows the line
            // (e.g. a 12pt line row under a box aligned to the row grid).
            var tol = Math.Max(0.8, bf.effectiveFontSize * 0.25);
            foreach (var cover in bf.coverRects)
            {
                if (cover.RunsBefore <= bf.runIndex) continue; // painted before this run
                if (bf.rect.LLX >= cover.Llx - tol && bf.rect.URX <= cover.Urx + tol
                    && bf.rect.LLY >= cover.Lly - tol && bf.rect.URY <= cover.Ury + tol)
                {
                    bf.textState.SetCapturedOccluded(true);
                    break;
                }
            }
        }
    }

    /// <summary>Text fragment build: a run cut away by its clip region reported invisible.</summary>
    private void ApplyRunClipVisibility(BuildFragmentsState bf, RawTextRun run)
    {
        // Hidden-by-clipping: the clip region in effect when the run was shown
        // cuts more of its box away than is tolerated - a tenth of
        // the box in each direction, see ClipSlackFraction for the measured law.
        // Same reporting rule as occlusion: Invisible, RenderingMode untouched.
        if (run.ClipRect is { } runClip)
        {
            // The verdict is the FACE's line box's, not the reported rectangle's:
            // the two differ only for a descriptor-less core face, whose rectangle
            // still seats on its baseline here (see ComputeDescentAscent).
            var lineBox = RunLineBox(run);
            if (IsHiddenByClip(run, run.Text, lineBox.Llx, lineBox.Lly,
                    lineBox.Urx, lineBox.Ury, runClip))
                bf.textState.SetCapturedOccluded(true);
        }

        // Hidden-by-later-text: a stacked duplicate draw (or any later text ink
        // covering the glyph box) hides this run — every copy
        // but the last reports as Invisible.
        if (bf.occludedByLaterText)
            bf.textState.SetCapturedOccluded(true);

        // Hidden-by-occlusion: a body-sized opaque fill painted AFTER this run that
        // fully covers its box hides it (redaction-style). Surface it through
    }

    /// <summary>Text fragment build: the run's background, underline and strikeout captured from the fill rectangles.</summary>
    private void CaptureRunDecorations(BuildFragmentsState bf, RawTextRun run)
    {
        bf.capturedUl = null;
        bf.capturedBg = null;
        {
            var (_, baselineY) = ApplyCtm(run.X, run.Y, run.Ctm);

            // Background colour + underline capture only when the caller asked for
            // graphics-related results (or underline-from-source). When no search options
            // were supplied, honour TextSearchOptions' own default (SearchForTextRelatedGraphics
            // = true) for BOTH captures: a plain `new TextFragmentAbsorber()` recovers
            // TextState.BackgroundColor AND reports Underline for a rule the geometry
            // detector accepts (an HtmlFragment round trip's <u> reads back true
            // with default options), and strikeout
            // already detects by default.
            bool wantGraphics = _textSearchOptions?.SearchForTextRelatedGraphics ?? true;
            bool wantSourceDecorations = _textEditOptions?.ToAttemptGetUnderlineFromSource ?? false;
            bool wantUnderline = wantGraphics || wantSourceDecorations;
            if (bf.fillIndex is not null)
            {
                if (wantGraphics)
                {
                    var bg = bf.fillIndex.FindTopMatch(bf.py - FillRectIndex.Margin, bf.py + FillRectIndex.Margin,
                        fr => bf.px >= fr.Llx && bf.px <= fr.Urx && bf.py >= fr.Lly && bf.py <= fr.Ury);
                    if (bg is { } bgHit) bf.textState.SetCapturedBackgroundColor(bgHit.FillColor);
                }
                if (wantUnderline)
                {
                    bf.capturedUl = DetectUnderlineRect(bf.rect, baselineY, bf.effectiveFontSize, bf.fillIndex);
                    if (bf.capturedUl is not null) bf.textState.SetCapturedUnderline(true);
                }
                // Source-highlight capture: lets a later text replacement splice the
                // old background rect out and re-draw it at the replacement's width.
                if (wantSourceDecorations)
                    bf.capturedBg = DetectBackgroundRect(bf.rect, baselineY, bf.effectiveFontSize, bf.fillIndex);

                // Strikeout is detected by default (no option required).
                if (DetectStrikeoutRect(bf.rect, baselineY, bf.effectiveFontSize, bf.fillIndex) is not null)
                    bf.textState.SetCapturedStrikeOut(true);
            }
        }
    }

    /// <summary>Text fragment build: the run's text and box clipped to the search rectangle.</summary>
    private bool ClipRunToSearchRect(BuildFragmentsState bf, RawTextRun run)
    {
        bf.clipText = run.Text;
        bf.clipX = run.X;
        bf.clipY = run.Y;
        bf.clipWidth = bf.width;
        if (bf.searchRect is not null && run.Metrics is not null)
        {
            (bf.clipText, bf.clipX, bf.clipY, bf.clipWidth) =
                ClipRunToRect(run, bf.searchRect, bf.clipText, bf.clipX, bf.clipY, bf.clipWidth);
            if (bf.clipText.Length == 0) return true;
            // The clipped start moved along the text matrix on BOTH axes: a run drawn up
            // a rotated page advances in Y, and a fragment seated at the run's own Y would
            // report (and then rewrite) the token at the head of the run instead of its own.
            var cRectStartX = bf.clipX + run.TmC * bf.descentOffset;
            var cRectStartY = bf.clipY + run.TmD * bf.descentOffset;
            var (crx1, cry1) = ApplyCtm(cRectStartX, cRectStartY, run.Ctm);
            var cEndX = bf.clipX + run.TmA * bf.clipWidth + run.TmC * bf.ascentHeight;
            var cEndY = bf.clipY + run.TmB * bf.clipWidth + run.TmD * bf.ascentHeight;
            var (crx2, cry2) = ApplyCtm(cEndX, cEndY, run.Ctm);
            bf.llx = Math.Min(crx1, crx2);
            bf.lly = Math.Min(cry1, cry2);
            bf.urx = Math.Max(crx1, crx2);
            bf.ury = Math.Max(cry1, cry2);
            bf.rect = new Rectangle(bf.llx, bf.lly, bf.urx, bf.ury);
            bf.px = crx1;
            bf.py = cry1;
        }

        bf.tdx = run.Ctm.A * run.TmA + run.Ctm.C * run.TmB;
        bf.tdy = run.Ctm.B * run.TmA + run.Ctm.D * run.TmB;

        bf.rotDeg = RotationFromDirection(bf.tdx, bf.tdy);
        if (bf.rotDeg.HasValue) bf.textState.Rotation = bf.rotDeg.Value;
        return false;
    }

    /// <summary>Text fragment build: a run whose glyph band falls outside the search rectangle is skipped.</summary>
    private bool RunOutsideSearchRect(BuildFragmentsState bf, RawTextRun run)
    {
        if (bf.searchRect is not null && !bf.searchRect.IsEmpty)
        {
            // Horizontal gate: any X-span overlap between the run and the rectangle lets
            // the run through — ClipRunToRect then keeps only the glyphs that fall inside
            // (a run may START well left of the rect yet contribute its tail glyphs).
            // Vertical gate: at least half of the run's glyph band (descent..ascent) must
            // lie inside the rectangle. A strict baseline-in-rect test drops a line whose
            // baseline dips a fraction below the rect bottom even though the glyph bodies
            // are substantially inside, and such lines belong in the result.
            var (_, baseY) = ApplyCtm(run.X, run.Y, run.Ctm);
            var bandH = bf.ury - bf.lly;
            var overlapV = Math.Min(bf.ury, bf.searchRect.URY) - Math.Max(bf.lly, bf.searchRect.LLY);
            // Half a point of slack on the half-band rule: a 5 pt space glyph whose
            // canonical box straddles the rectangle's bottom edge by 49.6 % is
            // still reported inside.
            const double HalfBandSlack = 0.5;
            bool vOk = bandH > 1e-6
                ? overlapV * 2 >= bandH - HalfBandSlack
                : baseY >= bf.searchRect.LLY && baseY <= bf.searchRect.URY;
            if (!(bf.urx >= bf.searchRect.LLX && bf.llx <= bf.searchRect.URX && vOk))
                return true;
        }
        return false;
    }
}

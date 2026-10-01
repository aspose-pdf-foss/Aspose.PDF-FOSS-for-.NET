using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
    /// <summary>Search fragments: the match stage's guards, run resolution, underline detection and marks.</summary>
    private void TryUnderlineFromSource(SearchFragmentState sf, EmitMatchState em)
    {
        if ((_textEditOptions?.ToAttemptGetUnderlineFromSource ?? false)
            && sf.fillIndex is not null && em.fragment.Segments.Count > 1)
        {
            foreach (TextSegment seg in em.fragment.Segments)
            {
                if (seg.Rectangle is not { } segRect || seg.Position is not { } segPos) continue;
                // The segment position anchors at the rect bottom (baseline − descent);
                // lift it back to the true baseline so a rule hugging the baseline
                // stays inside the detector's window.
                var segBaseline = Math.Max(segPos.YIndent, segRect.LLY + 0.22 * em.textState.FontSize);
                if (DetectUnderlineRect(segRect, segBaseline, em.textState.FontSize, sf.fillIndex!) is not { } segUl) continue;
                // Raw coords repeat across cm-translated blocks — the width is
                // part of the identity.
                if (em.fragment.CapturedUnderlineSources is { } have
                    && have.Exists(t => Math.Abs(t.X - segUl.RawX) < 0.01 && Math.Abs(t.Y - segUl.RawY) < 0.01
                        && Math.Abs(t.W - segUl.RawW) < 0.01))
                    continue;
                em.fragment.MarkCapturedUnderlineSource(segUl.RawX, segUl.RawY, segUl.RawW, segUl.RawH);
            }
        }
    }

    /// <summary></summary>
    private void ApplyCapturedMarks(SearchFragmentState sf, EmitMatchState em)
    {
        if (em.capturedUl is { } ulr)
        {
            em.fragment.MarkCapturedUnderlineSource(ulr.RawX, ulr.RawY, ulr.RawW, ulr.RawH);
            em.fragment.CapturedUnderlinePageRect = (ulr.Llx, ulr.Lly, ulr.Urx, ulr.Ury);
            // What the source rule covers BEYOND the match: the tail of the last
            // spanned run, and where that run ends. A replacement re-seats the tail
            // at its own advance; switching the underline off leaves it underlined.
            var tailRun = sf.rawFragments[em.lastRunIdx];
            var tailFrom = em.endCharIdx - sf.runStartChar[em.lastRunIdx] + 1;
            em.fragment.SourceUnderlineTrailingText = tailFrom >= 0 && tailFrom < tailRun.Text.Length
                ? tailRun.Text.Substring(tailFrom)
                : string.Empty;
            // run.X/Width live in TEXT space; the rule's extent is page space.
            var (tailEndX, _) = ApplyCtm(
                tailRun.X + tailRun.TmA * tailRun.Width * tailRun.HScaling,
                tailRun.Y + tailRun.TmB * tailRun.Width * tailRun.HScaling, tailRun.Ctm);
            em.fragment.SourceUnderlineRunEndX = tailEndX;

            // The rules the LINE carries besides this one. A replacement re-lays the
            // line's decoration in the library's own band, so a rule under a
            // neighbouring run has to come with it - left where the source put it, it
            // sits a fraction off the band the re-laid rules share and keeps a thickness
            // none of them has.
            var (_, myBaseline) = ApplyCtm(em.firstRun.X, em.firstRun.Y, em.firstRun.Ctm);
            for (var ri = 0; sf.fillIndex is not null && ri < sf.rawFragments.Count; ri++)
            {
                if (ri >= em.firstRunIdx && ri <= em.lastRunIdx) continue;
                var compRun = sf.rawFragments[ri];
                if (string.IsNullOrWhiteSpace(compRun.Text)) continue;
                var (_, compBaseline) = ApplyCtm(compRun.X, compRun.Y, compRun.Ctm);
                if (Math.Abs(compBaseline - myBaseline) > 0.5) continue;
                var compRect = ComputeMatchBounds(sf.rawFragments, sf.runStartChar, ri, ri,
                    sf.runStartChar[ri], sf.runStartChar[ri] + compRun.Text.Length - 1);
                if (compRect.URX - compRect.LLX <= 0) continue;
                if (DetectUnderlineRect(compRect, compBaseline, em.textState.FontSize, sf.fillIndex)
                    is not { } compUl) continue;
                if (Math.Abs(compUl.RawX - ulr.RawX) < 0.01 && Math.Abs(compUl.RawY - ulr.RawY) < 0.01
                    && Math.Abs(compUl.RawW - ulr.RawW) < 0.01) continue;
                if (em.fragment.CompanionRuleSources is { } seenComp
                    && seenComp.Exists(t => Math.Abs(t.X - compUl.RawX) < 0.01
                        && Math.Abs(t.Y - compUl.RawY) < 0.01
                        && Math.Abs(t.W - compUl.RawW) < 0.01)) continue;
                em.fragment.MarkCompanionRule(compUl.RawX, compUl.RawY, compUl.RawW, compUl.RawH,
                    compRect.LLX, compRect.URX - compRect.LLX, compUl.FillColor);
            }
        }
        if (em.capturedBg is { } bgr)
            em.fragment.MarkCapturedBackgroundSource(bgr.RawX, bgr.RawY, bgr.RawW, bgr.RawH, bgr.FillColor);
        // A match spanning several lines can cover more than one source
        // underline (short rules under phrases on different lines). The
        // whole-fragment detection above sees only the first baseline, so
        // re-detect per segment and capture every rule found — toggling
    }

    /// <summary></summary>
    private void DetectMatchUnderline(SearchFragmentState sf, EmitMatchState em, Match match)
    {
        em.capturedUl = null;
        em.capturedBg = null;
        if (sf.fillIndex is not null)
        {
            var (_, baselineY) = ApplyCtm(em.firstRun.X, em.firstRun.Y, em.firstRun.Ctm);
            bool wantSourceDecorations = _textEditOptions?.ToAttemptGetUnderlineFromSource ?? false;
            // Same default as the absorb-all path above: underline capture follows
            // TextSearchOptions' own default (on) when no options were supplied.
            bool wantUnderline = (_textSearchOptions?.SearchForTextRelatedGraphics ?? true)
                || wantSourceDecorations;
            // Same rule as the absorb-all path: a fill rect containing the match's
            // baseline midpoint supplies TextState.BackgroundColor (later draw
            // order wins). The midpoint — not the start edge — probes: a source
            // highlight is often drawn a hair inside the first glyph's origin.
            if (_textSearchOptions?.SearchForTextRelatedGraphics ?? true)
            {
                var midX = em.rect.LLX + em.rect.Width / 2;
                var bgHit = sf.fillIndex.FindTopMatch(baselineY - FillRectIndex.Margin, baselineY + FillRectIndex.Margin,
                    fr => midX >= fr.Llx && midX <= fr.Urx && baselineY >= fr.Lly && baselineY <= fr.Ury);
                // The fragment snapshot-copied the built TextState at construction —
                // the capture must land on the fragment's own state object.
                if (bgHit is { } bgh) em.fragment.TextState.SetCapturedBackgroundColor(bgh.FillColor);
            }
            if (wantUnderline)
            {
                em.capturedUl = DetectUnderlineRect(em.rect, baselineY, em.textState.FontSize, sf.fillIndex);
                // Like the background above: the fragment snapshot-copied the built
                // TextState, so the capture must land on the fragment's own state.
                if (em.capturedUl is not null) em.fragment.TextState.SetCapturedUnderline(true);
            }
            // Source-highlight capture: lets a later text replacement splice the old
            // background rect out and re-draw it at the replacement's width. Gated with
            // the RULE, not with ToAttemptGetUnderlineFromSource alone: text-related
            // graphics already hand the caller the highlight's COLOUR, and a caller that
            // then sets BackgroundColor is replacing that highlight - painting the new
            // one while the old rect still stands leaves the old one on top.
            if (wantUnderline)
                em.capturedBg = DetectBackgroundRect(em.rect, baselineY, em.textState.FontSize, sf.fillIndex);
            if (DetectStrikeoutRect(em.rect, baselineY, em.textState.FontSize, sf.fillIndex) is not null)
                em.fragment.TextState.SetCapturedStrikeOut(true);
        }

        // Build per-run segments with position and rectangle
        BuildFragmentSegments(em.fragment, sf.rawFragments, sf.runStartChar,
            em.firstRunIdx, em.lastRunIdx, em.startCharIdx, em.endCharIdx, sf.charToRun);

        // ★ Segments are per-GLYPH-RUN, so they are necessarily in DRAWN order, and
        // adding them re-joins the fragment's text from them — which for an RTL run
        // silently hands back the reading order REVERSED. The segments keep drawn
        // order (their positions describe where the glyphs sit); the fragment's Text
        // is the reported reading order, so restore it.
        if (BidiReorderer.ContainsRtl(em.absorbedText))
            em.fragment.SetAbsorbedText(em.absorbedText);

        em.matchTrimmed = match.Value.Trim('\r', '\n');
        if (em.matchTrimmed.IndexOf('\r') >= 0 || em.matchTrimmed.IndexOf('\n') >= 0)
            em.fragment.SetAbsorbedText(LogicalizeRtlPresentationForms(em.matchTrimmed));
        // INTERIOR junction spaces synthesised during line assembly (word gaps,
        // back-jump splices) belong to no glyph run, so the segment join drops
        // them. The full matched text is reported — restore it when the
        // join lost characters. BOUNDARY spaces stay off (a match that merely
        // starts/ends on a junction space reads back without it, same as the
        // sentinel rule above).
        else
        {
            // Only the SYNTHETIC boundary spaces come off. A space the match starts
            // or ends on that a glyph run actually drew is part of the text and must
            // survive — trimming every one of them silently shortened lines that
            // open and close on real space glyphs the moment any interior junction
            // space sent them down this path.
            var matchInner = TrimSynthesizedEdges(em.matchTrimmed, em.fragment.Text);
            if (em.fragment.Segments.Count > 0
                && matchInner.Length > em.fragment.Text.Length
                && string.Equals(em.fragment.Text.Replace(" ", ""),
                       matchInner.Replace(" ", ""), StringComparison.Ordinal))
                em.fragment.SetAbsorbedText(LogicalizeRtlPresentationForms(matchInner));
        }
    }

    /// <summary></summary>
    private void ResolveMatchRuns(SearchFragmentState sf, EmitMatchState em, Match match)
    {
        em.firstRunIdx = sf.charToRun[em.startCharIdx];
        em.lastRunIdx = sf.charToRun[em.endCharIdx];
        // A back-jump PREPEND (see BuildConcatenatedText) makes run indexes
        // non-monotonic in char space: the match can START in a later-drawn run
        // and END in an earlier one. Segment/bounds builders walk an ordered
        // range, so normalise to [min, max].
        if (em.firstRunIdx > em.lastRunIdx)
            (em.firstRunIdx, em.lastRunIdx) = (em.lastRunIdx, em.firstRunIdx);

        em.rect = ComputeMatchBounds(sf.rawFragments, sf.runStartChar,
            em.firstRunIdx, em.lastRunIdx, em.startCharIdx, em.endCharIdx);

        // Compute position, text state, and trailing Tc for the fragment
        (em.posX, em.posY) = ComputeMatchPosition(sf.rawFragments[em.firstRunIdx],
            em.startCharIdx - sf.runStartChar[em.firstRunIdx]);
        em.firstRun = sf.rawFragments[em.firstRunIdx];
        em.textState = BuildTextState(em.firstRun);
        em.hiddenArea = 0;
        em.totalArea = 0;
        for (var ri = em.firstRunIdx; ri <= em.lastRunIdx && ri < sf.laterInk.Length; ri++)
        {
            if (sf.rawFragments[ri].Text == "\r\n") continue;
            var a = sf.runBoxArea[ri];
            em.totalArea += a;
            if (sf.laterInk[ri] || sf.clippedAway[ri]) em.hiddenArea += a;
        }
        if (em.totalArea > 0 && em.hiddenArea > em.totalArea * 0.5)
            em.textState.SetCapturedOccluded(true);
        em.trailingTc = ComputeTrailingTc(sf.rawFragments, sf.runStartChar, em.lastRunIdx, em.endCharIdx);

        em.sTdx = em.firstRun.Ctm.A * em.firstRun.TmA + em.firstRun.Ctm.C * em.firstRun.TmB;
        em.sTdy = em.firstRun.Ctm.B * em.firstRun.TmA + em.firstRun.Ctm.D * em.firstRun.TmB;
        em.sRot = RotationFromDirection(em.sTdx, em.sTdy);
        if (em.sRot.HasValue) em.textState.Rotation = em.sRot.Value;

        // Only the ANISOTROPIC part of the matrix is a horizontal scale. A matrix
        // that scales both axes alike carries the font size (a "1 Tf" run sized
        // by "7 0 0 7 Tm"), and that size is already in TextState.FontSize.
        em.textState.SourceTmScale = Math.Abs(em.firstRun.TmD) > 1e-9
            ? em.firstRun.TmA / em.firstRun.TmD
            : 1.0;
        em.absorbedText = sf.bidiPerm is not null
            ? match.Value
            : LogicalizeRtlPresentationForms(match.Value);
    }

    /// <summary></summary>
    private bool SkipsMatch(SearchFragmentState sf, EmitMatchState em, Match match)
    {
        if (match.Length == 0)
        {
            // A zero-length regex match (lookarounds, optional groups) is still a
            // result: an empty fragment positioned at the match
            // point.
            var anchorIdx = sf.bidiPerm is not null && match.Index < sf.bidiPerm.Length
                ? sf.bidiPerm[match.Index] : match.Index;
            var empty = new TextFragment(string.Empty)
            {
                PageIndex = sf.pageIndex,
                SourcePage = sf.sourcePage,
                Form = sf.sourceForm,
            };
            if (anchorIdx < sf.charToRun.Count)
            {
                var runIdx = sf.charToRun[anchorIdx];
                var (ex, ey) = ComputeMatchPosition(sf.rawFragments[runIdx], anchorIdx - sf.runStartChar[runIdx]);
                empty.Position = new Position(Q(ex), Q(ey));
            }
            _fragments.Add(empty);
            return true;
        }

        em.startCharIdx = sf.bidiPerm is not null ? sf.bidiPerm[match.Index] : match.Index;
        em.endCharIdx = sf.bidiPerm is not null
            ? sf.bidiPerm[match.Index + match.Length - 1]
            : match.Index + match.Length - 1;
        if (em.startCharIdx > em.endCharIdx)
            (em.startCharIdx, em.endCharIdx) = (em.endCharIdx, em.startCharIdx);

        if (em.startCharIdx >= sf.charToRun.Count || em.endCharIdx >= sf.charToRun.Count)
        {
            _fragments.Add(new TextFragment(LogicalizeRtlPresentationForms(match.Value)) { PageIndex = sf.pageIndex, SourcePage = sf.sourcePage, Form = sf.sourceForm });
            return true;
        }
        return false;
    }
}

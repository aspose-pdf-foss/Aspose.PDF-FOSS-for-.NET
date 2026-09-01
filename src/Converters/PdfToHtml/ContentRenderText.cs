using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
// The show-text run of the content render, lifted out of RenderContentToHtml; it takes the render state and the inputs it reads.
    private static void ShowRun(ContentRenderState ct, Dictionary<string, HtmlFontRecord> fonts, StringBuilder sb, double pageHeight, double pageWidth, bool saveTransparentTexts, bool emCompensation, bool textOnly, StyleRegistry? styleReg, ClassNamer classNamer, List<LinkTarget>? linkTargets, RotationRegistry? rotReg, double pageLLX, double yTopRef, ZCounter? zCounter, bool pageTurnedOver, string text, double advTextSpace = double.NaN, double extTextSpace = double.NaN,
        List<(double pen, double glyph)>? perChar = null, List<int>? perCode = null)
    {
        if (string.IsNullOrEmpty(text)) return;

        // An invisible run is dropped whole when the save does not ask for it. The
        // caller advances the text matrix after this returns, so a visible run
        // later on the same line still seats where its own pen puts it.
        if (Invisible(ct) && !saveTransparentTexts)
        {
            ct.pendingTjNum = 0;
            return;
        }

        var dev = ct.tm.Times(ct.ctm);
        var scale = Math.Sqrt(dev.C * dev.C + dev.D * dev.D);
        if (scale <= 0) scale = 1;
        var effSize = ct.fontSize * scale;
        var effRise = ct.rise * scale;
        var posX = dev.E;
        var posY = dev.F;

        // Baseline direction in device space. PDF angles are counter-clockwise
        // with y up; CSS rotation is clockwise with y down, so the CSS angle is
        // the negation (e.g. Tm [0 s -s 0 …] — text running upward — is CSS
        // rotate(-90deg)).
        var cssAngle = -Math.Atan2(dev.B, dev.A) * (180.0 / Math.PI);
        if (Math.Abs(cssAngle) < 0.05) cssAngle = 0;

        // A same-baseline show that lands well PAST where the previous show's
        // pen ended is a COLUMN, not a continuation: keep it as its own group so
        // every column keeps its own x, instead of one concatenated run whose
        // tail drifts by re-measured advances (an invoice's label/value columns
        // fused into single runs). Gaps up to one em of the font still merge —
        // a TOC number→title gap of ~0.9 font-em is bridged with a
        // stretched word space but ~1.06 splits — and so do BACKTRACKS: a
        // zero-leading ' wraps back to the line start at the same y, and
        // those halves join into one flowing line. The text-only
        // overlay's grouping is left as-is.
        // The gap is measured from the last TEXT pen edge: whitespace-only
        // shows are transparent to the split decision (they bridge into the
        // run when text resumes nearby, and never force a split themselves).
        // The stl_ dialects split at 87.5×fs milli-em of pen gap
        // (0.0875·fs² pt); the plain dialect keeps its one-em rule.
        var divGapPt = styleReg is not null ? 0.0875 * effSize * effSize : 1.0 * effSize;
        // The column split concerns a show on the SAME baseline: a show on a
        // different baseline no longer closes the line here — it parks it (below),
        // because the producer may come back to it. The baseline test is the same
        // one the sameLine decision makes, so a same-baseline column still cuts
        // exactly where it always did.
        var lineYTol = styleReg is not null && ct.mcSeq != ct.groupMcSeq
            ? 0.2
            : Math.Max(0.5, Math.Max(effSize, ct.groupFontSize) * 0.3);
        if (ct.groupActive && !textOnly && ct.groupSegs.Count > 0
            && !string.IsNullOrWhiteSpace(text)
            && Math.Abs(posY - ct.groupY) <= lineYTol
            && posX - Math.Max(ct.groupTextPenX, ct.groupX) > divGapPt)
        {
            // The severed line must not EMIT here: it keeps its place in the
            // first-use order and simply stops accepting shows — emitting at
            // the split point let it jump ahead of every line still parked,
            // scrambling the document-wide class numbering, which derives
            // from first use.
            if (styleReg is not null && Math.Abs(cssAngle) < 0.05)
            {
                var severed = ParkCurrentLine(ct, styleReg);
                if (severed is not null) severed.Closed = true;
            }
            else FlushGroup(ct, sb, pageHeight, pageWidth, textOnly, styleReg, classNamer, linkTargets, rotReg, pageLLX, yTopRef, zCounter, pageTurnedOver, emCompensation);
        }

        // A show that STARTS past the page's right edge is invisible (an
        // off-page TouchUp leftover, clipped by the page rect) — it must not
        // join a line and stretch the flow toward its phantom position.
        if (styleReg is not null && cssAngle == 0 && posX > pageWidth - 0.5)
        {
            ct.pendingTjNum = 0;
            return;
        }

        // A shadowed run is the same text drawn AGAIN a hair off the original
        // (fill pass over the shadow pass): a show restarting at the GROUP'S
        // OWN ORIGIN that repeats a substantial run of text the group already
        // carries is the duplicate pass, dropped whole. The guards keep every
        // legitimate backtrack: an RTL line's next word lands left of the pen
        // with fresh text; a wrap continuation carries new text; a repeated
        // word later in the line starts at the pen, not the origin.
        if (styleReg is not null && ct.groupActive && ct.groupSegs.Count > 0
            && !string.IsNullOrWhiteSpace(text)
            && posX < ct.groupTextPenX - 0.5
            && Math.Abs(posX - ct.groupX) <= 2 * effSize
            && Math.Abs(posY - ct.groupY) <= 0.5
            && text.Trim().Length >= 6
            && !HasRtlCodepoint(text))
        {
            var joined = new StringBuilder();
            foreach (var (_, seg, _, _) in ct.groupSegs) joined.Append(seg);
            if (joined.ToString().Contains(text.Trim(), StringComparison.Ordinal))
            {
                ct.pendingTjNum = 0;
                return;
            }
        }

        // An OVERSTRIKE: some producers thicken text by re-stroking the glyph
        // they just drew a fraction of a point away — the same character(s), a
        // hair left of the pen, ending exactly ON the pen. Counted, it doubles
        // the line's characters ("fun" "d" then "d" again reading as "fundd").
        // The suffix match plus the end-on-pen test keeps everything
        // legitimate: an RTL line's next word ends at the previous word's
        // START, not at the pen; a repeated word starts AT the pen or later;
        // a justified continuation carries fresh text. The line consulted is
        // whichever this show belongs to — the current group, or the parked
        // line on this baseline.
        if (styleReg is not null && cssAngle == 0
            && !string.IsNullOrWhiteSpace(text) && !double.IsNaN(advTextSpace))
        {
            string? lastTxt = null; double lastPen = 0, lastX0 = 0;
            if (ct.groupActive && Math.Abs(posY - ct.groupY) <= ParkBaselineTolPt)
            { lastTxt = ct.groupLastShowText; lastPen = ct.groupTextPenX; lastX0 = ct.groupX; }
            else if (!ct.groupActive || Math.Abs(posY - ct.groupY) > ParkBaselineTolPt)
            {
                if (FindParkedLine(ct, posY) is { } c)
                { lastTxt = c.LastShowText; lastPen = c.TextPenX; lastX0 = c.X; }
            }
            // Observed second strokes end within 0.06-0.24 pt of the pen; 1 pt
            // holds margin for coarser producers while staying well under half
            // a word space, so a genuinely new word can never qualify.
            const double OverstrikeEndTolPt = 1.0;
            if (!string.IsNullOrEmpty(lastTxt)
                && lastTxt.EndsWith(text, StringComparison.Ordinal)
                && posX >= lastX0 - PenSlackPt
                && posX < lastPen - PenSlackPt
                && Math.Abs(posX + advTextSpace * scale - lastPen) <= OverstrikeEndTolPt)
            {
                ct.pendingTjNum = 0;
                return;
            }
        }

        // A whitespace-only show continues the line regardless of its own
        // font/colour — a word gap drawn with a different font (a larger
        // space glyph between runs) is coerced to the group's font as its
        // own segment instead of breaking the div chain. stl_ dialects only;
        // the plain span dialect keeps strict font grouping.
        var wsOnlyShow = styleReg is not null && string.IsNullOrWhiteSpace(text);
        // In the stl_ dialects a font/size/colour switch cuts a SPAN, not the
        // line: shows keep merging while the line stays solver-eligible.
        // The stl_ dialects keep the loose 0.3-em baseline merge only for
        // shows WITHIN one marked-content item (or in untagged content):
        // across a BDC/EMC boundary two runs continue one line only on a
        // (near-)identical baseline — a tagged CV's date span and its
        // right-hand subtitle span sat 0.24pt apart and stayed two divs,
        // while an untagged report's footer runs 1–2pt apart still merge.
        bool sameLine = ct.groupActive &&
            Math.Abs(effRise - ct.groupRise) <= 0.01 &&
            Math.Abs(cssAngle - ct.groupAngle) <= 0.1 &&
            Math.Abs(posY - ct.groupY) <= lineYTol &&
            (wsOnlyShow || (styleReg is not null && ct.lineOk) ||
             (ct.fontFamily == ct.groupFamily && ct.fontCssFamily == ct.groupCssFamily &&
              ct.fontWeight == ct.groupWeight && ct.fontStyle == ct.groupStyle &&
              ct.r == ct.groupR && ct.g == ct.groupG && ct.b == ct.groupB &&
              Invisible(ct) == ct.groupTransparent));
                    // A run that starts LEFT of the accumulated pen (overlapping/backward
        // draw - e.g. word-gap space glyphs re-drawn over an already-shown line)
        // cannot continue the inline span flow; it opens its own positioned div.
        // (Overlay mode only - the SVG-text dialect keeps the legacy grouping.)
        // The em-compensation dialect tolerates a SQUEEZED inter-span word
        // space: a body span drawn 0.73 pt behind the title's pen (its
        // separator space compressed by justification) still continues the
        // line — the squeeze is solved as negative word-spacing
        // in ONE div. A genuine re-draw starts at least a word further back.
        var backTolPt = emCompensation ? 1.5 : 0.5;
        if (textOnly && sameLine && ct.groupSegs.Count > 0 && posX < ct.groupPenX - backTolPt)
            sameLine = false;
        if (!sameLine)
        {
            // A line the producer merely moved AWAY from is parked, not closed:
            // a show landing back on a parked baseline CONTINUES that line iff
            // it would have continued it had no interleave happened — at or
            // near the parked pen (the column split keeps its distance rule),
            // or behind it under the dialect's own backtrack semantics.
            var canPark = styleReg is not null && Math.Abs(cssAngle) < 0.05;
            StlLinePark? resumed = null;
            if (canPark)
            {
                ParkCurrentLine(ct, styleReg);
                var cand = FindParkedLine(ct, posY);
                if (cand is not null && cand.Segs.Count > 0
                    && Math.Abs(cand.Angle) < 0.05
                    && Math.Abs(effRise - cand.Rise) <= 0.01)
                {
                    var candPen = Math.Max(cand.TextPenX, cand.X);
                    // Only a genuine CONTINUATION resumes: the show picks up at
                    // (or within a word gap of) the parked pen. A show landing
                    // BEHIND the pen of an interrupted line is not one of its
                    // fragments — a subscript pass, a wrap-back, an annotation
                    // overlay — and legacy gave those their own div once the
                    // line had been left; that stands. (A same-line backtrack
                    // with the group still open never reaches here.)
                    var continues = posX >= cand.X - PenSlackPt
                        && (wsOnlyShow
                            ? posX >= candPen - PenSlackPt
                            : posX >= candPen - PenSlackPt && posX <= candPen + divGapPt);
                    // The em-compensation dialect also PREFIX-joins: a fragment
                    // drawn after its line whose pen END lands on the line's
                    // START (a title drawn second is assembled into
                    // ONE div with its body). The end must abut the start
                    // within a squeezed word space — a number-column head ends
                    // a full quad short and keeps its own div.
                    const double PrefixJoinTolPt = 2.5;
                    var prefixJoins = emCompensation && !wsOnlyShow
                        && !double.IsNaN(advTextSpace)
                        && posX < cand.X - PenSlackPt
                        && posX + advTextSpace * scale >= cand.X - PenSlackPt
                        && posX + advTextSpace * scale <= cand.X + PrefixJoinTolPt;
                    if (continues || prefixJoins) resumed = cand;
                }
            }
            else FlushGroup(ct, sb, pageHeight, pageWidth, textOnly, styleReg, classNamer, linkTargets, rotReg, pageLLX, yTopRef, zCounter, pageTurnedOver, emCompensation);

            if (resumed is not null)
            {
                ResumeParkedLine(ct, resumed);
            }
            else
            {
            ct.groupActive = true;
            ct.groupX = posX; ct.groupY = posY; ct.groupFontSize = effSize; ct.groupRise = effRise;
            ct.groupRawRise = ct.rise;
            ct.groupAngle = cssAngle;
            ct.groupFamily = ct.fontFamily; ct.groupCssFamily = ct.fontCssFamily;
            ct.groupWeight = ct.fontWeight; ct.groupStyle = ct.fontStyle;
            ct.groupFauxBold = FauxBold(ct);
            ct.groupDeclStyle = DeclStyle(ct);
            ct.groupR = ct.r; ct.groupG = ct.g; ct.groupB = ct.b;
            ct.groupTransparent = Invisible(ct);
            ct.groupAscent = ct.fontAscent;
            ct.groupLineHeight = ct.fontLineHeight;
            ct.groupIsType3 = ct.fontIsType3;
            ct.groupZ = 0;
            ct.groupLastShowText = "";
            ct.activePark = null;      // a fresh line owns no park slot yet
            }
        }
        ct.groupMcSeq = ct.mcSeq;
        ct.groupPenX = Math.Max(ct.groupPenX, posX);
        if (!wsOnlyShow) ct.groupLastShowText = text;

        // Append the run to the segment chain (one segment per repositioned
        // run, as before). With aligned per-char advances the OVERLAY run is
        // additionally CUT at word boundaries (space-to-nonspace edges), one
        // segment per word, each pinned separately at flush. Anchors accumulate
        // in Tc/Tw-FREE width space (glyph advances only) - the
        // same budget the per-segment letter-spacings solve against - while the
        // pen (Tc/Tw included) is kept for backward-draw detection only.
        var aligned = perChar is not null && perChar.Count == text.Length;

        // Record the show's glyphs for the stl_ line solver. Any show the
        // solver cannot model (no aligned advances, rotation) drops the whole
        // line back to the legacy emission.
        if (ct.lineGlyphs is not null && ct.lineOk)
        {
            if (!aligned || Math.Abs(cssAngle) > 0.05)
            {
                ct.lineOk = false;
            }
            else
            {
                if (ct.lineStyleIdx < 0
                    || ct.lineStyles![ct.lineStyleIdx].Family != ct.fontFamily
                    || ct.lineStyles[ct.lineStyleIdx].CssFamily != ct.fontCssFamily
                    || ct.lineStyles[ct.lineStyleIdx].FontSize != effSize
                    || ct.lineStyles[ct.lineStyleIdx].R != ct.r
                    || ct.lineStyles[ct.lineStyleIdx].G != ct.g
                    || ct.lineStyles[ct.lineStyleIdx].B != ct.b
                    || ct.lineStyles[ct.lineStyleIdx].Transparent != Invisible(ct)
                    || ct.lineStyles[ct.lineStyleIdx].UseFallbackMetrics)
                {
                    var faceName = HtmlToPdfConverter.ResolveStlFace(ct.fontFamily);
                    var fiNow = ct.currentFontKey is not null
                        && fonts.TryGetValue(ct.currentFontKey, out var fNow) ? fNow : null;
                    var subsetSpace = fiNow?.AdvanceOf is null || fiNow.AdvanceOf(32) > 0;
                    ct.lineStyles!.Add(new StlRunStyle
                    {
                        Family = ct.fontFamily,
                        CssFamily = ct.fontCssFamily,
                        FaceName = faceName,
                        FontSize = effSize,
                        FauxBold = FauxBold(ct), FontStyle = DeclStyle(ct),
                        R = ct.r, G = ct.g, B = ct.b, Transparent = Invisible(ct),
                        Ascent = ct.fontAscent,
                        LineHeightEm = ct.fontLineHeight,
                        SubsetHasSpace = subsetSpace,
                        SubsetHas = fiNow?.SubsetHas,
                        HasEmbeddedMetrics = fiNow?.EmbeddedAdvMilli is not null,
                        SubstituteFace = fiNow?.SubstituteFace ?? false,
                        ProgramCharMilli = fiNow?.ProgramCharAdvMilli,
                        SpaceAdvMilli = subsetSpace && faceName is not null
                            ? HtmlToPdfConverter.StlCharAdvanceMilli(faceName, ' ')
                            // No installed face to measure: the served program's
                            // own space advance beats the generic 250.
                            : fiNow?.EmbeddedAdvMilli?.Invoke(32)
                                ?? HtmlToPdfConverter.StlFallbackAdvanceMilli(' '),
                    });
                    ct.lineStyleIdx = ct.lineStyles.Count - 1;
                }
                // A glyph shown through a GID the embedded program's cmap
                // cannot address renders in the CSS fallback face: it takes a
                // sibling style whose metrics and font class carry the
                // fallback (spaces stay on the base).
                HtmlFontRecord? fRec = null;
                if (ct.currentFontKey is not null) fonts.TryGetValue(ct.currentFontKey, out fRec);
                var glyphMapped = fRec?.GlyphMapped;
                var baseIdx = ct.lineStyleIdx;
                var fbIdx = -1;
                var sxRec = posX;
                for (var ci = 0; ci < text.Length; ci++)
                {
                    var chRec = text[ci];
                    var idxRec = baseIdx;
                    var codeRec = perCode is not null && ci < perCode.Count ? perCode[ci] : -1;
                    if (chRec != ' ' && codeRec >= 0 && glyphMapped is not null && !glyphMapped(codeRec))
                    {
                        if (fbIdx < 0)
                        {
                            var b0 = ct.lineStyles[baseIdx];
                            ct.lineStyles.Add(new StlRunStyle
                            {
                                Family = b0.Family, CssFamily = b0.CssFamily,
                                FaceName = b0.FaceName, FontSize = b0.FontSize,
                                FauxBold = b0.FauxBold, FontStyle = b0.FontStyle,
                                R = b0.R, G = b0.G, B = b0.B, Transparent = b0.Transparent,
                                Ascent = b0.Ascent, LineHeightEm = b0.LineHeightEm,
                                SubsetHasSpace = b0.SubsetHasSpace,
                                SubsetHas = b0.SubsetHas,
                                SpaceAdvMilli = b0.SpaceAdvMilli,
                                HasEmbeddedMetrics = b0.HasEmbeddedMetrics,
                                SubstituteFace = b0.SubstituteFace,
                                ProgramCharMilli = b0.ProgramCharMilli,
                                UseFallbackMetrics = true,
                            });
                            fbIdx = ct.lineStyles.Count - 1;
                        }
                        idxRec = fbIdx;
                    }
                    double? embAdv = null;
                    if (codeRec >= 0)
                    {
                        // The em-compensation dialect measures by the embedded
                        // program's own advances (that program is re-served
                        // via @font-face, so the solve and the browser agree
                        // glyph by glyph); other dialects keep the face-metric model.
                        if (emCompensation && fRec?.ProgramAdvMilli is { } pa)
                            embAdv = pa(codeRec);
                        embAdv ??= fRec?.EmbeddedAdvMilli?.Invoke(codeRec);
                    }
                    // A code expanding to several chars shares its embedded
                    // advance with the FIRST char; the rest ride at (near-)zero —
                    // a TJ kern between the code and its neighbour can leak a
                    // sub-point residue onto the tail char, which is still no
                    // advance of its own.
                    // A multi-char code expansion fuses (head + tails solve as
                    // one item) when the font serves EMBEDDED advances — the
                    // code's whole advance rides the head and the tails add
                    // zero, so the pair's error stays the small ligature-vs-
                    // components difference instead of two large opposite
                    // errors that atomize the span. Face-metric fonts keep the
                    // unfused per-char model.
                    // Tail detection is relative to the EFFECTIVE size: the
                    // tail's own advance is at most kern residue (a few
                    // milli-em), never a real glyph advance — a Tf 1 font
                    // scaled up by Tm must not read every repeated character
                    // ("00") as an expansion.
                    // A REAL second character never has a negative advance — a
                    // tail whose residue is a big NEGATIVE kern (a line-final
                    // ligature pulled back by the kern before its trailing
                    // space) is still a tail.
                    var expansionTail = embAdv is not null && ci > 0 && perCode is not null
                        && ci < perCode.Count && perCode[ci - 1] == codeRec
                        && (emCompensation
                            ? perChar![ci].glyph * scale < 0.1 * effSize
                            : Math.Abs(perChar![ci].glyph) * scale < 0.1 * effSize);
                    if (expansionTail) embAdv = 0;
                    ct.lineGlyphs.Add(new StlLineGlyph
                    {
                        Ch = chRec,
                        Style = idxRec,
                        StartX = sxRec,
                        WidthsAdv = perChar![ci].glyph * scale,
                        TtfMilli = embAdv ?? ct.lineStyles![idxRec].TtfMilli(chRec),
                        ExpansionTail = expansionTail,
                        FuseByFace = expansionTail && glyphMapped is not null,
                        SynthSpace = chRec == ' ' && perCode is not null
                            && ci < perCode.Count && perCode[ci] < 0,
                    });
                    sxRec += perChar[ci].pen * scale;
                }
            }
        }

        if (ct.groupSegs.Count == 0 || ct.groupSegs[ct.groupSegs.Count - 1].X != posX)
            ct.groupSegs.Add((posX, new StringBuilder(), posX, posX));
        var segIdx = ct.groupSegs.Count - 1;
        var s0 = ct.groupSegs[segIdx];
        var penX = Math.Max(s0.PenEnd, posX);
        var glyphX = Math.Max(s0.GlyphEnd, posX);
        for (var ci = 0; ci < text.Length; ci++)
        {
            var ch = text[ci];
            if (textOnly && aligned && ct.groupSegs[segIdx].Text.Length > 0
                && !char.IsWhiteSpace(ch)
                && char.IsWhiteSpace(ct.groupSegs[segIdx].Text[ct.groupSegs[segIdx].Text.Length - 1]))
            {
                // close the current word segment and anchor the next at the
                // running width-only edge
                ct.groupSegs[segIdx] = (ct.groupSegs[segIdx].X, ct.groupSegs[segIdx].Text, penX, glyphX);
                ct.groupSegs.Add((glyphX, new StringBuilder(), penX, glyphX));
                segIdx++;
            }
            ct.groupSegs[segIdx].Text.Append(ch);
            if (aligned)
            {
                penX += perChar![ci].pen * scale;
                glyphX += perChar[ci].glyph * scale;
            }
        }
        if (!aligned)
        {
            penX = double.IsNaN(advTextSpace) ? penX : posX + advTextSpace * scale;
            glyphX = double.IsNaN(extTextSpace) ? glyphX : posX + extTextSpace * scale;
        }
        var cl = ct.groupSegs[segIdx];
        ct.groupSegs[segIdx] = (cl.X, cl.Text,
            Math.Max(cl.PenEnd, penX), Math.Max(cl.GlyphEnd, glyphX));
        ct.groupPenX = Math.Max(ct.groupPenX, penX);
        if (!string.IsNullOrWhiteSpace(text))
            ct.groupTextPenX = Math.Max(ct.groupTextPenX, penX);

        // Extent tracking: the run's device right edge from its width-only PDF
        // advances (the line-box budget ignores Tc/Tw).
        if (double.IsNaN(extTextSpace)) ct.groupPinned = false;
        else ct.groupEndX = Math.Max(ct.groupEndX, posX + extTextSpace * scale);
        ct.groupTjNum += ct.pendingTjNum;
        ct.pendingTjNum = 0;
        ct.groupChars += text.Length;
        // UseZOrder: every shown non-whitespace glyph advances the paint
        // counter; the div's z-index is the value at its last such glyph.
        if (zCounter is not null)
        {
            var nws = 0;
            foreach (var ch in text) if (!char.IsWhiteSpace(ch)) nws++;
            if (nws > 0) { zCounter.V += nws; ct.groupZ = zCounter.V; }
        }
    }
}

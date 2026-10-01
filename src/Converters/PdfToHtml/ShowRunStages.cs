using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
    /// <summary>Append the run to the group's current segment (or open one at its pen), advance the pen and glyph ends per character and settle the group's end and counters.</summary>
    private static void AppendRunToGroupSegment(ShowRunState sr, ContentRenderState ct, bool textOnly, ZCounter? zCounter, string text, double advTextSpace, double extTextSpace, List<(double pen, double glyph)>? perChar)
    {
        if (ct.groupSegs.Count == 0 || ct.groupSegs[ct.groupSegs.Count - 1].X != sr.posX)
            ct.groupSegs.Add((sr.posX, new StringBuilder(), sr.posX, sr.posX));
        sr.segIdx = ct.groupSegs.Count - 1;
        sr.s0 = ct.groupSegs[sr.segIdx];
        sr.penX = Math.Max(sr.s0.PenEnd, sr.posX);
        sr.glyphX = Math.Max(sr.s0.GlyphEnd, sr.posX);
        for (var ci = 0; ci < text.Length; ci++)
        {
            var ch = text[ci];
            if (textOnly && sr.aligned && ct.groupSegs[sr.segIdx].Text.Length > 0
                && !char.IsWhiteSpace(ch)
                && char.IsWhiteSpace(ct.groupSegs[sr.segIdx].Text[ct.groupSegs[sr.segIdx].Text.Length - 1]))
            {
                // close the current word segment and anchor the next at the
                // running width-only edge
                ct.groupSegs[sr.segIdx] = (ct.groupSegs[sr.segIdx].X, ct.groupSegs[sr.segIdx].Text, sr.penX, sr.glyphX);
                ct.groupSegs.Add((sr.glyphX, new StringBuilder(), sr.penX, sr.glyphX));
                sr.segIdx++;
            }
            ct.groupSegs[sr.segIdx].Text.Append(ch);
            if (sr.aligned)
            {
                sr.penX += perChar![ci].pen * sr.scale;
                sr.glyphX += perChar[ci].glyph * sr.scale;
            }
        }
        if (!sr.aligned)
        {
            sr.penX = double.IsNaN(advTextSpace) ? sr.penX : sr.posX + advTextSpace * sr.scale;
            sr.glyphX = double.IsNaN(extTextSpace) ? sr.glyphX : sr.posX + extTextSpace * sr.scale;
        }
        sr.cl = ct.groupSegs[sr.segIdx];
        ct.groupSegs[sr.segIdx] = (sr.cl.X, sr.cl.Text,
            Math.Max(sr.cl.PenEnd, sr.penX), Math.Max(sr.cl.GlyphEnd, sr.glyphX));
        ct.groupPenX = Math.Max(ct.groupPenX, sr.penX);
        if (!string.IsNullOrWhiteSpace(text))
            ct.groupTextPenX = Math.Max(ct.groupTextPenX, sr.penX);

        // Extent tracking: the run's device right edge from its width-only PDF
        // advances (the line-box budget ignores Tc/Tw).
        if (double.IsNaN(extTextSpace)) ct.groupPinned = false;
        else ct.groupEndX = Math.Max(ct.groupEndX, sr.posX + extTextSpace * sr.scale);
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

    /// <summary>Place the run's glyphs on the current line: per-character advances against the font record, the line's word gaps and the emitted spans.</summary>
    private static void EmitLineGlyphs(ShowRunState sr, ContentRenderState ct, Dictionary<string, HtmlFontRecord> fonts, bool emCompensation, string text, List<(double pen, double glyph)>? perChar, List<int>? perCode)
    {
        if (!sr.aligned || Math.Abs(sr.cssAngle) > 0.05)
        {
            ct.lineOk = false;
            return;
        }
        EnsureLineStyle(sr, ct, fonts);
        sr.fRec = null;
        if (ct.currentFontKey is not null) fonts.TryGetValue(ct.currentFontKey, out sr.fRec);
        sr.glyphMapped = sr.fRec?.GlyphMapped;
        sr.baseIdx = ct.lineStyleIdx;
        sr.fbIdx = -1;
        sr.sxRec = sr.posX;
        for (var ci = 0; ci < text.Length; ci++)
        {
            PlaceLineGlyph(sr, ct, emCompensation, text, perChar, perCode, ci);
        }
    }

    /// <summary>The run starts a new line: close the open group, then seat a fresh one at the run's baseline with its font and style.</summary>
    private static void OpenNewLineGroup(ShowRunState sr, ContentRenderState ct, StringBuilder sb, double pageHeight, double pageWidth, bool emCompensation, bool textOnly, StyleRegistry? styleReg, ClassNamer classNamer, List<LinkTarget>? linkTargets, RotationRegistry? rotReg, double pageLLX, double yTopRef, ZCounter? zCounter, bool pageTurnedOver, double advTextSpace)
    {
        // A line the producer merely moved AWAY from is parked, not closed:
        // a show landing back on a parked baseline CONTINUES that line iff
        // it would have continued it had no interleave happened — at or
        // near the parked pen (the column split keeps its distance rule),
        // or behind it under the dialect's own backtrack semantics.
        var canPark = styleReg is not null && Math.Abs(sr.cssAngle) < 0.05;
        StlLinePark? resumed = null;
        if (canPark)
        {
            ParkCurrentLine(ct, styleReg);
            var cand = FindParkedLine(ct, sr.posY);
            if (cand is not null && cand.Segs.Count > 0
                && Math.Abs(cand.Angle) < 0.05
                && Math.Abs(sr.effRise - cand.Rise) <= 0.01)
            {
                var candPen = Math.Max(cand.TextPenX, cand.X);
                // Only a genuine CONTINUATION resumes: the show picks up at
                // (or within a word gap of) the parked pen. A show landing
                // BEHIND the pen of an interrupted line is not one of its
                // fragments — a subscript pass, a wrap-back, an annotation
                // overlay — and legacy gave those their own div once the
                // line had been left; that stands. (A same-line backtrack
                // with the group still open never reaches here.)
                var continues = sr.posX >= cand.X - PenSlackPt
                    && (sr.wsOnlyShow
                        ? sr.posX >= candPen - PenSlackPt
                        : sr.posX >= candPen - PenSlackPt && sr.posX <= candPen + sr.divGapPt);
                // The em-compensation dialect also PREFIX-joins: a fragment
                // drawn after its line whose pen END lands on the line's
                // START (a title drawn second is assembled into
                // ONE div with its body). The end must abut the start
                // within a squeezed word space — a number-column head ends
                // a full quad short and keeps its own div.
                const double PrefixJoinTolPt = 2.5;
                var prefixJoins = emCompensation && !sr.wsOnlyShow
                    && !double.IsNaN(advTextSpace)
                    && sr.posX < cand.X - PenSlackPt
                    && sr.posX + advTextSpace * sr.scale >= cand.X - PenSlackPt
                    && sr.posX + advTextSpace * sr.scale <= cand.X + PrefixJoinTolPt;
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
        ct.groupX = sr.posX; ct.groupY = sr.posY; ct.groupFontSize = sr.effSize; ct.groupRise = sr.effRise;
        ct.groupRawRise = ct.rise;
        ct.groupAngle = sr.cssAngle;
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

    /// <summary>Decide whether the run joins the open group: the page-edge, angle and same-line tolerances that bail out of the run entirely.</summary>
    private static bool TryJoinRunToGroup(ShowRunState sr, ContentRenderState ct, StringBuilder sb, double pageHeight, double pageWidth, bool emCompensation, bool textOnly, StyleRegistry? styleReg, ClassNamer classNamer, List<LinkTarget>? linkTargets, RotationRegistry? rotReg, double pageLLX, double yTopRef, ZCounter? zCounter, bool pageTurnedOver, string text, double advTextSpace)
    {
        if (ct.groupActive && !textOnly && ct.groupSegs.Count > 0
            && !string.IsNullOrWhiteSpace(text)
            && Math.Abs(sr.posY - ct.groupY) <= sr.lineYTol
            && sr.posX - Math.Max(ct.groupTextPenX, ct.groupX) > sr.divGapPt)
        {
            // The severed line must not EMIT here: it keeps its place in the
            // first-use order and simply stops accepting shows — emitting at
            // the split point let it jump ahead of every line still parked,
            // scrambling the document-wide class numbering, which derives
            // from first use.
            if (styleReg is not null && Math.Abs(sr.cssAngle) < 0.05)
            {
                var severed = ParkCurrentLine(ct, styleReg);
                if (severed is not null) severed.Closed = true;
            }
            else FlushGroup(ct, sb, pageHeight, pageWidth, textOnly, styleReg, classNamer, linkTargets, rotReg, pageLLX, yTopRef, zCounter, pageTurnedOver, emCompensation);
        }

        // A show that STARTS past the page's right edge is invisible (an
        // off-page TouchUp leftover, clipped by the page rect) — it must not
        // join a line and stretch the flow toward its phantom position.
        if (styleReg is not null && sr.cssAngle == 0 && sr.posX > pageWidth - 0.5)
        {
            ct.pendingTjNum = 0;
            return false;
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
            && sr.posX < ct.groupTextPenX - 0.5
            && Math.Abs(sr.posX - ct.groupX) <= 2 * sr.effSize
            && Math.Abs(sr.posY - ct.groupY) <= 0.5
            && text.Trim().Length >= 6
            && !HasRtlCodepoint(text))
        {
            var joined = new StringBuilder();
            foreach (var (_, seg, _, _) in ct.groupSegs) joined.Append(seg);
            if (joined.ToString().Contains(text.Trim(), StringComparison.Ordinal))
            {
                ct.pendingTjNum = 0;
                return false;
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
        if (styleReg is not null && sr.cssAngle == 0
            && !string.IsNullOrWhiteSpace(text) && !double.IsNaN(advTextSpace))
        {
            string? lastTxt = null; double lastPen = 0, lastX0 = 0;
            if (ct.groupActive && Math.Abs(sr.posY - ct.groupY) <= ParkBaselineTolPt)
            { lastTxt = ct.groupLastShowText; lastPen = ct.groupTextPenX; lastX0 = ct.groupX; }
            else if (!ct.groupActive || Math.Abs(sr.posY - ct.groupY) > ParkBaselineTolPt)
            {
                if (FindParkedLine(ct, sr.posY) is { } c)
                { lastTxt = c.LastShowText; lastPen = c.TextPenX; lastX0 = c.X; }
            }
            // Observed second strokes end within 0.06-0.24 pt of the pen; 1 pt
            // holds margin for coarser producers while staying well under half
            // a word space, so a genuinely new word can never qualify.
            const double OverstrikeEndTolPt = 1.0;
            if (!string.IsNullOrEmpty(lastTxt)
                && lastTxt.EndsWith(text, StringComparison.Ordinal)
                && sr.posX >= lastX0 - PenSlackPt
                && sr.posX < lastPen - PenSlackPt
                && Math.Abs(sr.posX + advTextSpace * sr.scale - lastPen) <= OverstrikeEndTolPt)
            {
                ct.pendingTjNum = 0;
                return false;
            }
        }
        return true;
    }

    /// <summary>Place one character of the run on the line: its advance from the font record or the per-character pens, its fallback face and its glyph entry.</summary>
    private static void PlaceLineGlyph(ShowRunState sr, ContentRenderState ct, bool emCompensation, string text, List<(double pen, double glyph)>? perChar, List<int>? perCode, int ci)
    {
        var chRec = text[ci];
        var idxRec = sr.baseIdx;
        var codeRec = perCode is not null && ci < perCode.Count ? perCode[ci] : -1;
        if (chRec != ' ' && codeRec >= 0 && sr.glyphMapped is not null && !sr.glyphMapped(codeRec))
        {
            if (sr.fbIdx < 0)
            {
                var b0 = ct.lineStyles![sr.baseIdx];
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
                sr.fbIdx = ct.lineStyles.Count - 1;
            }
            idxRec = sr.fbIdx;
        }
        double? embAdv = null;
        if (codeRec >= 0)
        {
            // The em-compensation dialect measures by the embedded
            // program's own advances (that program is re-served
            // via @font-face, so the solve and the browser agree
            // glyph by glyph); other dialects keep the face-metric model.
            if (emCompensation && sr.fRec?.ProgramAdvMilli is { } pa)
                embAdv = pa(codeRec);
            embAdv ??= sr.fRec?.EmbeddedAdvMilli?.Invoke(codeRec);
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
                ? perChar![ci].glyph * sr.scale < 0.1 * sr.effSize
                : Math.Abs(perChar![ci].glyph) * sr.scale < 0.1 * sr.effSize);
        if (expansionTail) embAdv = 0;
        ct.lineGlyphs!.Add(new StlLineGlyph
        {
            Ch = chRec,
            Style = idxRec,
            StartX = sr.sxRec,
            WidthsAdv = perChar![ci].glyph * sr.scale,
            TtfMilli = embAdv ?? ct.lineStyles![idxRec].TtfMilli(chRec),
            ExpansionTail = expansionTail,
            FuseByFace = expansionTail && sr.glyphMapped is not null,
            SynthSpace = chRec == ' ' && perCode is not null
                && ci < perCode.Count && perCode[ci] < 0,
        });
        sr.sxRec += perChar[ci].pen * sr.scale;
    }

    /// <summary>Open a new line style when the run's face, size or colour differs from the line's current one.</summary>
    private static void EnsureLineStyle(ShowRunState sr, ContentRenderState ct, Dictionary<string, HtmlFontRecord> fonts)
    {
        if (ct.lineStyleIdx < 0
            || ct.lineStyles![ct.lineStyleIdx].Family != ct.fontFamily
            || ct.lineStyles[ct.lineStyleIdx].CssFamily != ct.fontCssFamily
            || ct.lineStyles[ct.lineStyleIdx].FontSize != sr.effSize
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
                FontSize = sr.effSize,
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
    }
}

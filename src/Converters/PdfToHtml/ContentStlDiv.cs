using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
    /// <summary>Solve one line div's glyphs into spans and emit them.
    /// Returns false when the data cannot be solved (caller falls back).</summary>
    private static bool EmitStlSolvedDiv(StringBuilder sb, List<StlLineGlyph> glyphs,
        List<StlRunStyle> styles, StyleRegistry styleReg, ClassNamer classNamer,
        string divCls, string zStyle, double pageLLX, double yTop, double baselineY,
        Func<double, double, LinkTarget?>? linkFor, List<(string Label, string Href)>? popupItems,
        double turnedOverShiftLeftEm = 0, double turnedOverShiftTopEm = 0,
        bool emGrid = false)
    {
        // A drawn NO-BREAK SPACE is a word gap to the line solver, exactly like a
        // drawn space: it is emitted as a plain word space inside one
        // span, and a line made of nothing else is dropped (a scanner's trailing nbsp
        // runs produce whole such rows). Treated as a character instead, it cut a
        // span on both sides - it usually arrives in its own font - atomizing the
        // line into one span per word.
        static bool IsSpaceGlyph(char c) => c is ' ' or ' ';

        // A prefix-joined line arrives with its fragments in DRAW order (body
        // first, title behind it): the em-compensation solve reads the line
        // left-to-right, so a real inversion re-orders by pen position. Kern
        // jitter under a point is left alone; other dialects never prefix-join.
        if (emGrid)
            for (var q = 1; q < glyphs.Count; q++)
                if (glyphs[q].StartX < glyphs[q - 1].StartX - 1.0)
                {
                    glyphs = glyphs.OrderBy(x => x.StartX).ToList();
                    break;
                }

        // 1. Trim line-edge space glyphs.
        int lo = 0, hi = glyphs.Count - 1;
        while (lo <= hi && IsSpaceGlyph(glyphs[lo].Ch)) lo++;
        while (hi >= lo && IsSpaceGlyph(glyphs[hi].Ch)) hi--;
        if (hi < lo) return true;   // whitespace-only line: nothing rendered

        // Every style needs a real browser-model advance: either it resolves to an
        // installed face, or its font serves the embedded program's own metrics.
        // The em-compensation dialect keeps the line in the solved path on the
        // fallback advance model instead — bailing to the legacy group emission
        // flattened a masthead's mixed font/size runs into ONE span (no per-font
        // spans, no column splits, no synthesized gaps) whenever a single piece
        // used a font that neither embeds nor installs.
        foreach (var st in styles)
        {
            if (st.FaceName is null && !st.HasEmbeddedMetrics)
            {
                if (!emGrid) return false;
                st.UseFallbackMetrics = true;
            }
            // A SUBSTITUTE face (SimSun standing in for a font that neither
            // embeds nor installs) measures approximately: the default
            // four-decimal dialect's outlier atomization would cut spans at
            // every drawn-vs-substitute divergence, so those lines keep the
            // legacy group emission there. The em-compensation dialect never
            // atomizes and solves against the substitute basis.
            if (!emGrid && st.SubstituteFace) return false;
        }

        // 2. Build the item stream: chars with advance errors, and space slots
        //    (kept, dropped or synthesized around 0.6×m).
        var items = new List<StlItem>();
        // Facts the number-column split below turns on: whether any REAL space
        // glyph is drawn on the line (a synthesized gap-space advances the pen
        // but carries no drawn advance of its own), and the raw gap behind a
        // single leading character (captured when the first slot lands at
        // items[1]).
        var lineHasSpaceGlyph = false;
        for (var t0 = lo; t0 <= hi; t0++)
            if (IsSpaceGlyph(glyphs[t0].Ch) && !glyphs[t0].SynthSpace) { lineHasSpaceGlyph = true; break; }
        // A uniformly letter-spread line (every inter-char pen gap carries the
        // same tracking) is LETTER-SPACING, not word gaps: such a heading is
        // emitted as plain words ("Journal of Xiangfan University"), not
        // atomized per gap into 'J o u r n a l …'. The em-compensation
        // synthesis therefore measures each gap against the line's TYPICAL
        // inter-char gap (median over positive gaps, 4+ samples) instead of
        // against zero — word boundaries still exceed it by a space width.
        var lineSpreadPt = 0.0;
        if (emGrid)
        {
            var gaps = new List<double>();
            for (var t0 = lo; t0 < hi; t0++)
            {
                if (IsSpaceGlyph(glyphs[t0].Ch) || IsSpaceGlyph(glyphs[t0 + 1].Ch)) continue;
                var rg = glyphs[t0 + 1].StartX - glyphs[t0].StartX - glyphs[t0].WidthsAdv;
                if (rg > 0.02) gaps.Add(rg);
            }
            const int SpreadMinSamples = 4;
            if (gaps.Count >= SpreadMinSamples)
            {
                gaps.Sort();
                lineSpreadPt = gaps[gaps.Count / 2];
            }
        }
        double headGapPt = 0, headFs = 0;
        var i = lo;
        while (i <= hi)
        {
            var g = glyphs[i];
            if (IsSpaceGlyph(g.Ch)) { i++; continue; }   // consumed by gap handling below
            var st = styles[g.Style];
            var fs = Math.Max(0.01, st.FontSize);
            var fsEff = Math.Floor(fs * 1000.0) / 1000.0;

            // A ligature code expanded to several chars renders as its COMPONENT
            // glyphs in the browser model, so the head and its expansion tails
            // fuse into ONE item whose natural width is the face's component
            // advances — the pair's whole advance error is the (small) ligature-
            // vs-components width difference, not two large opposite errors that
            // would atomize the span.
            var itemEnd = i;
            var wSum = g.WidthsAdv;
            double tailW = 0;
            string? itemText = null;
            var fuseByFace = false;
            while (itemEnd + 1 <= hi && glyphs[itemEnd + 1].ExpansionTail
                && glyphs[itemEnd + 1].Style == g.Style)
            {
                itemEnd++;
                wSum += glyphs[itemEnd].WidthsAdv;
                tailW += glyphs[itemEnd].WidthsAdv;
                itemText = (itemText ?? g.Ch.ToString()) + glyphs[itemEnd].Ch;
                fuseByFace |= glyphs[itemEnd].FuseByFace;
            }
            var ttfMilliItem = g.TtfMilli;
            // The components-vs-lig face delta charged to the ws numerator but
            // NOT to the ls mean (LsE): the ls classes ignore it.
            var lsAdjMilli = 0.0;
            if (itemText is not null)
            {
                if (fuseByFace && !emGrid && st.FaceName is not null)
                {
                    double faceSum = 0;
                    foreach (var chF in itemText) faceSum += st.TtfMilli(chF);
                    ttfMilliItem = faceSum;
                }
                else if (emGrid && st.ProgramCharMilli is not null)
                {
                    // The em-compensation FACE basis for a ligature is its
                    // COMPONENT advances from the embedded program's own metrics
                    // (an 'ft' ligature's components-vs-lig delta comes to
                    // +27.34 exactly; 'ffl' +39.55).
                    // Unresolvable components keep the LIG advance.
                    double compSum = 0;
                    var okComp = true;
                    foreach (var chF in itemText)
                    {
                        if (st.ProgramCharMilli(chF) is { } aC) compSum += aC;
                        else { okComp = false; break; }
                    }
                    if (okComp && compSum > 0)
                    {
                        lsAdjMilli = compSum - ttfMilliItem;
                        ttfMilliItem = compSum;
                    }
                    else
                        for (var t = i + 1; t <= itemEnd; t++) ttfMilliItem += glyphs[t].TtfMilli;
                }
                else
                {
                    for (var t = i + 1; t <= itemEnd; t++) ttfMilliItem += glyphs[t].TtfMilli;
                }
            }
            var ttfPt = ttfMilliItem / 1000.0 * fsEff;
            // The em-compensation PEN basis drops the /W-vs-program rounding
            // residue: such a /W is authored as round(program float),
            // so δ = round(float) − float per item — the solve sees each char's
            // error as exactly the kern/gap residue. Other dialects keep the
            // physical /W pen. (The drawn advance wSum itself carries the TJ
            // kern, which must stay.)
            var penPt = wSum;
            if (emGrid && st.HasEmbeddedMetrics)
                penPt -= (Math.Round(g.TtfMilli) - g.TtfMilli) / 1000.0 * fsEff;

            // Locate the next rendered char and whether real space glyphs sit between.
            var j = itemEnd + 1;
            var sawSpace = false;
            var spaceStyle = g.Style;
            double spaceGlyphMilli = 0;
            // The width the drawn space actually contributes, taken from the source's
            // own /Widths rather than from a face measurement: when the run's face is
            // not installed, an unmappable space measures as the half-em guess, which
            // is nearly twice a real space and silently swallows the word break.
            double spaceDrawnPt = 0;
            while (j <= hi && IsSpaceGlyph(glyphs[j].Ch))
            {
                sawSpace = true;
                spaceStyle = glyphs[j].Style;
                spaceGlyphMilli = glyphs[j].TtfMilli;
                spaceDrawnPt += glyphs[j].WidthsAdv;
                j++;
            }

            if (j > hi)
            {
                // Line-final char: error is advance-only and never enters ls. In
                // the em-compensation region an expansion tail's advance residue
                // is the kern ADJACENT TO THE TRAILING SPACE — excluded
                // (a ±288 kern before the trailing space is invisible).
                var eFin = (penPt - (emGrid ? tailW : 0) - ttfPt) / fs * 1000.0;
                items.Add(new StlItem { Ch = g.Ch, Text = itemText, Style = g.Style, StartX = g.StartX,
                    E = eFin, LsE = eFin + (wSum - penPt) / fs * 1000.0 + lsAdjMilli * fsEff / fs, LsEligible = false,
                    FaceMilli = ttfPt / fs * 1000.0 });
                break;
            }

            // The slot metric m: a space glyph of the LINE's own font measures by
            // the font's space advance; a foreign-font word gap (and a
            // synthesized slot) measures by the line font's space advance at the
            // line font's size.
            var mMilliSlot = sawSpace && spaceStyle == g.Style
                ? styles[spaceStyle].SpaceAdvMilli
                : st.SpaceAdvMilli;
            var mPt = mMilliSlot / 1000.0 * fs;
            var gapPt = glyphs[j].StartX - g.StartX - wSum;   // pen end → next char
            // Slot decision. For a synthesized/kern gap (no space glyph drawn) the gap
            // must reach 0.6 of the line font's nominal space advance. When a space glyph
            // WAS drawn, measure against the width that glyph actually contributes: a real
            // word space leaves a gap close to its drawn advance, whereas a space drawn for
            // justification/letter-spacing is pulled back by a following negative kern, so
            // its gap falls well short of the drawn width and must not open a word break.
            // (Against the nominal advance the two are indistinguishable — both ~0.45·m.)
            bool slotFires;
            if (sawSpace)
            {
                var drawnPt = spaceDrawnPt > 0.01
                    ? spaceDrawnPt
                    : spaceGlyphMilli / 1000.0 * styles[spaceStyle].FontSize;
                slotFires = drawnPt > 0.01 ? gapPt >= 0.6 * drawnPt : gapPt >= 0.6 * mPt;
            }
            else
            {
                // The line's uniform tracking (see lineSpreadPt above) is not a
                // word gap; only the excess over it opens a slot.
                slotFires = gapPt - lineSpreadPt >= 0.6 * mPt;
            }

            if (!slotFires)
            {
                // Plain gap (dropped space or kern): folds into this char's error.
                var eFold = (penPt + gapPt - ttfPt) / fs * 1000.0;
                items.Add(new StlItem { Ch = g.Ch, Text = itemText, Style = g.Style, StartX = g.StartX,
                    E = eFold, LsE = eFold + (wSum - penPt) / fs * 1000.0 + lsAdjMilli * fsEff / fs, LsEligible = true,
                    FaceMilli = ttfPt / fs * 1000.0 });
            }
            else
            {
                // Word-final char, then the space slot. The slot rides on the
                // LINE's style (a word-gap space drawn with its own font is
                // coerced — it burns no font class of its own).
                var eWf = (penPt - ttfPt) / fs * 1000.0;
                items.Add(new StlItem { Ch = g.Ch, Text = itemText, Style = g.Style, StartX = g.StartX,
                    E = eWf, LsE = eWf + (wSum - penPt) / fs * 1000.0 + lsAdjMilli * fsEff / fs, LsEligible = false,
                    FaceMilli = ttfPt / fs * 1000.0 });
                items.Add(new StlItem { IsSlot = true, Ch = ' ', Style = g.Style,
                    StartX = g.StartX + wSum,
                    E = (gapPt - mPt) / Math.Max(0.01, styles[spaceStyle].FontSize) * 1000.0,
                    GapPt = gapPt,
                    Synth = !sawSpace,
                    FaceMilli = mPt / Math.Max(0.01, styles[spaceStyle].FontSize) * 1000.0 });
                if (items.Count == 2) { headGapPt = gapPt; headFs = fs; }
            }
            i = j;
        }
        if (items.Count == 0) return true;

        // A single character standing a full quad ahead of the rest of a line
        // that draws NO space glyphs is a NUMBER COLUMN, and a fresh
        // positioned div starts at the text after the gap. The head must be
        // exactly one rendered char (a two-char head or
        // a trailing dot folds), the gap must exceed 0.95 of the font size
        // (0.95 folds, 0.955 splits; no upper bound), the line's font must be
        // under 11.5 pt (11.4 splits, 11.5 folds — headings escape), and one
        // real space glyph anywhere on the line disables the whole rule.
        // Colour, link annotations, font identity and line position play
        // no part.
        if (popupItems is null && !lineHasSpaceGlyph && items.Count >= 3
            && !items[0].IsSlot && items[1].IsSlot && !items[2].IsSlot
            && headGapPt > 0.95 * headFs && headFs < 11.5)
        {
            EmitStlPart(new List<StlItem> { items[0] });
            EmitStlPart(items.GetRange(2, items.Count - 2));
            return true;
        }
        // A TAB — a gap a quad or more past the pen on a line that draws real
        // space glyphs — starts a fresh positioned div per segment: a
        // single-char head splits past 1.05 of the font
        // size (1.00 folds, 1.10 splits), a longer head past 1.50 (1.45 folds,
        // 1.55 splits); smaller stretches stay in the line as word-spacing. A
        // spaceless line keeps the lone-char rule above instead.
        if (popupItems is null)
        {
            const double TabSplitLoneHeadEm = 1.05;
            const double TabSplitWordHeadEm = 1.50;
            // The em-compensation dialect splits a lone head (a list bullet) at a
            // smaller stretch: the bullet MERGES at a 0.8394 em gap and
            // SPLITS at 0.8758 — the default
            // dialect keeps its own 1.05.
            const double TabSplitLoneHeadEmGrid = 0.86;
            // Column-gap split for a SPACELESS em-compensation line: a
            // char-spaced masthead splits into per-name divs at ~1.46 em gaps
            // while a 0.79 em byline gap and a 0.66 em pre-tail gap stay in
            // the line — the spaced-line lone-head threshold sits between those.
            const double TabSplitNoSpaceEmGrid = 1.05;
            List<List<StlItem>>? tabParts = null;
            int partStart = 0, headChars = 0;
            for (var k = 0; k < items.Count; k++)
            {
                if (!items[k].IsSlot) { headChars++; continue; }
                var fsSlot = Math.Max(0.01, styles[items[k].Style].FontSize);
                var loneHeadEm = emGrid ? TabSplitLoneHeadEmGrid : TabSplitLoneHeadEm;
                var thr = (headChars <= 1 ? loneHeadEm : TabSplitWordHeadEm) * fsSlot;
                // A spaceless CJK line still splits at a COLUMN-sized gap in the
                // em-compensation dialect: a masthead's pieces sit 5+ em apart
                // (its word gaps stay under ~0.8 em) and each piece is emitted
                // as its own div rather than a giant word-spacing.
                var canSplit = lineHasSpaceGlyph
                    || (emGrid && items[k].GapPt > TabSplitNoSpaceEmGrid * fsSlot);
                if (canSplit && items[k].GapPt > thr && k + 1 < items.Count)
                {
                    tabParts ??= new List<List<StlItem>>();
                    tabParts.Add(items.GetRange(partStart, k - partStart));
                    partStart = k + 1;
                    headChars = 0;
                }
            }
            if (tabParts is not null)
            {
                tabParts.Add(items.GetRange(partStart, items.Count - partStart));
                foreach (var part in tabParts)
                    if (part.Count > 0) EmitStlPart(part);
                return true;
            }
        }
        EmitStlPart(items);
        return true;

        void EmitStlPart(List<StlItem> items)
        {

        // 3. Span boundaries: style changes and gap atomization.
        var cut = new bool[items.Count];   // cut[k] = span boundary BEFORE item k
        // A slot rides the style of the char it follows, so it stays inside the
        // span it trails; the boundary lands on the next RENDERED char whose
        // style differs from the last rendered one — a word gap between two
        // differently-sized runs must still cut.
        var lastRendered = -1;
        for (var k = 0; k < items.Count; k++)
        {
            if (items[k].IsSlot) continue;
            if (lastRendered >= 0
                && !styles[items[k].Style].SameSpan(styles[items[lastRendered].Style]))
                cut[k] = true;
            lastRendered = k;
        }

        // Atomization inside runs bounded by slots/style cuts. The EXPLICIT
        // em-compensation mode never atomizes: it emits ONE span per
        // style run and absorbs per-char outliers into the quantized line spacing.
        // (The trigger is the OPTION being set - the enum's em member is its
        // first value, but the field's DEFAULT is the pixel mode; a save that
        // never touches it solves at four decimals.)
        const double TAtom = 1000.0 / 11.0;
        var runStart = 0;
        if (!emGrid)
        for (var k = 1; k <= items.Count; k++)
        {
            if (k < items.Count && !items[k].IsSlot && !cut[k] && !items[k - 1].IsSlot) continue;
            // run = items[runStart..k)
            var internals = new List<int>();
            for (var t = runStart; t < k; t++)
                if (!items[t].IsSlot && items[t].LsEligible) internals.Add(t);
            if (internals.Count >= 2)
            {
                for (var t = 0; t < internals.Count; t++)
                {
                    double sum = 0;
                    foreach (var u in internals) if (u != internals[t]) sum += items[u].E;
                    var meanOther = sum / (internals.Count - 1);
                    if (Math.Abs(items[internals[t]].E - meanOther) > TAtom)
                    {
                        var carrier = internals[t];
                        if (carrier > runStart) cut[carrier] = true;                 // [prefix][carrier
                        if (carrier + 1 < items.Count) cut[carrier + 1] = true;      // carrier][first-after
                        if (carrier + 2 < items.Count && !items[carrier + 1].IsSlot
                            && !items[carrier + 2].IsSlot) cut[carrier + 2] = true;  // first-after][rest
                        break;   // one atomization per run
                    }
                }
            }
            runStart = k;
        }

        // 4. Assemble spans left-to-right, folding/externalizing slots.
        var spans = new List<(List<StlItem> Items, int Style, bool IsNbsp, double? InheritWs)>();
        List<StlItem>? cur = null;
        var curStyle = 0;
        var foldedSlots = new List<double>();
        double? pendingInheritWs = null;
        void Close()
        {
            if (cur is { Count: > 0 })
                spans.Add((cur, curStyle, false, pendingInheritWs));
            cur = null;
            foldedSlots.Clear();
            pendingInheritWs = null;
        }
        for (var k = 0; k < items.Count; k++)
        {
            var it = items[k];
            if (it.IsSlot)
            {
                if (cur is null || cur.Count == 0)
                {
                    // Slot with no open span (should not happen mid-line): externalize.
                    spans.Add((new List<StlItem> { it }, it.Style, true, null));
                    pendingInheritWs = it.E / 1000.0;
                    continue;
                }
                var mean = 0.0;
                if (foldedSlots.Count > 0)
                {
                    foreach (var v in foldedSlots) mean += v;
                    mean /= foldedSlots.Count;
                }
                var mMilli = styles[it.Style].SpaceAdvMilli;
                // The em-compensation dialect folds every DRAWN-space slot (the
                // solved ws absorbs their spread); a SYNTHESIZED slot keeps the
                // outlier rule — a char-spaced line's one wide gap becomes its
                // own nbsp span, not a ws inflation. A slot at
                // a CROSS-FONT boundary (next char changes family or size) is
                // charged to the boundary, never to the preceding span's ws —
                // dense-CJK spans carry NO ws; the connector
                // span after them takes the gap. Same-font boundaries keep the
                // fold (the anchored title solve depends on its inter-span
                // slot staying in the title span).
                var crossFont = k + 1 < items.Count && !items[k + 1].IsSlot
                    && (styles[items[k + 1].Style].CssFamily != styles[it.Style].CssFamily
                        || Math.Abs(styles[items[k + 1].Style].FontSize
                                    - styles[it.Style].FontSize) > 0.01);
                if (emGrid && crossFont)
                {
                    Close();
                    spans.Add((new List<StlItem> { it }, it.Style, true, null));
                    pendingInheritWs = it.E / 1000.0;
                    continue;
                }
                if ((emGrid && !it.Synth && !crossFont)
                    || foldedSlots.Count == 0 || Math.Abs(it.E - mean) <= 0.6 * mMilli)
                {
                    foldedSlots.Add(it.E);
                    cur.Add(it);
                }
                else
                {
                    Close();
                    spans.Add((new List<StlItem> { it }, it.Style, true, null));
                    pendingInheritWs = it.E / 1000.0;
                }
                continue;
            }
            if (cur is not null && (cut[k] || !styles[it.Style].SameSpan(styles[curStyle])))
            {
                var inherit = pendingInheritWs;
                Close();
                // A style-matching span straight after an externalized slot inherits
                // its ws; a font-change span does not.
                pendingInheritWs = styles[it.Style].SameSpan(styles[curStyle]) ? inherit : null;
            }
            if (cur is null) { cur = new List<StlItem>(); curStyle = it.Style; }
            cur.Add(it);
        }
        Close();
        if (spans.Count == 0) return;

        // 5. Emit. Div geometry: left from the first rendered item, top from the
        //    first span's font ascent.
        var first = spans[0];
        var st0 = styles[first.Style];
        var left = (first.Items[0].StartX - pageLLX) / 12.0 - turnedOverShiftLeftEm;
        var top = (yTop - baselineY - st0.Ascent * st0.FontSize) / 12.0 - turnedOverShiftTopEm;
        sb.Append($"<div class=\"{divCls}\" style=\"left:{Em4T(left)}em;top:{Em4T(top)}em;{zStyle}\">");

        var popupBoxNum = 0;
        if (popupItems is not null)
        {
            popupBoxNum = styleReg.PopupBox();
            sb.Append($"<div class=\"{classNamer.Cls(popupBoxNum)}\">");
        }

        var renderedChars = 0;
        foreach (var sp in spans)
            if (!sp.IsNbsp) renderedChars += sp.Items.Count(x => !x.IsSlot);

        int lastFontNum = 0, lastLhNum = 0, lastLsNum = 0;
        for (var s = 0; s < spans.Count; s++)
        {
            var (its, styleIdx, isNbsp, inheritWs) = spans[s];
            var st = styles[styleIdx];
            var fs = Math.Max(0.01, st.FontSize);
            // The em-compensation dialect emits the css font-size ROUNDED to the
            // 0.01-em grid (drawn 11 → 0.92em, 15 → 1.25em, 40 → 3.33em); the ws
            // solve above uses the TRUNCATED size — the dialect's own
            // deliberate inconsistency, not to be reconciled.
            var fontNum = styleReg.Font(st.CssFamily,
                emGrid
                    ? Math.Round(st.FontSize / 12.0, 2, MidpointRounding.AwayFromZero)
                    : st.FontSize / 12.0,
                st.CssColor, null,
                st.UseFallbackMetrics ? "Times New Roman" : null);
            var lhNum = styleReg.LineHeight(st.LineHeightEm > 0 ? Math.Round(st.LineHeightEm, 6) : 1.2);

            double lsMilli = 0;
            string text;
            double? wsEm = null;
            if (isNbsp)
            {
                text = "&nbsp;";
                wsEm = Math.Round(its[0].E / 1000.0, 4, MidpointRounding.AwayFromZero);
            }
            else
            {
                var eligible = its.Where(x => !x.IsSlot && x.LsEligible).ToList();
                // A span-final char whose word continues into the next span stays
                // ls-eligible; the builder marked word-finals ineligible already.
                // The mean reads the LIG-basis error (LsE): the components-vs-lig
                // face delta stays out of the ls classes.
                if (eligible.Count > 0)
                    lsMilli = eligible.Average(x => emGrid ? x.LsE : x.E);
                // The em-compensation mode keeps its spacing on a 0.01 em grid: the
                // letter-spacing FLOORS to the grid first (a floor, NOT
                // round-half-away) and the word-spacing
                // then solves against the floored value, absorbing the residue.
                if (emGrid)
                    lsMilli = Math.Floor(lsMilli / 10.0) * 10.0;
                var slots = its.Count(x => x.IsSlot);
                if (slots > 0 && !emGrid)
                {
                    double sumE = 0;
                    for (var t = 0; t < its.Count; t++)
                    {
                        // The four-decimal dialect excludes the line-final char's
                        // own advance residue.
                        var isLineFinal = s == spans.Count - 1 && t == its.Count - 1;
                        if (!isLineFinal) sumE += its[t].E;
                    }
                    // CSS letter-spacing lands after every character - the space
                    // slots included - except a span-final one, whose advance the
                    // next box absorbs; the solve counts terms the same way.
                    var lsTerms = its.Count - (its[^1].IsSlot ? 0 : 1);
                    wsEm = Math.Round(
                        (sumE - lsTerms * lsMilli) / slots / 1000.0,
                        4, MidpointRounding.AwayFromZero);
                }
                else if (slots > 0)
                {
                    // THE EM-COMPENSATION SOLVE:
                    //   ws = R2( (S·ΣE − S·(S−1)·Σface − n·lsFloor) / (D·1000) )
                    // · The solve runs at the css size TRUNCATED to the
                    //   0.01-em grid (0.91em·12 = 10.92 pt for a drawn 11) while
                    //   the markup emits the ROUNDED size (0.92em); S is the
                    //   drawn/solve ratio, and the face side scales by S again
                    //   (the ws deliberately lands short of its own ink).
                    // · n = every region item (interior slots included; the
                    //   trailing inter-span slot of a title span included — that
                    //   slot extends the region to the next span's start, which
                    //   is what makes the solve track the following span rather
                    //   than the title's own ink).
                    // · D counts only KERN-CARRYING slots: a bare drawn space
                    //   (pen gap = its own advance) contributes no divisor. The
                    //   membership floor lies in the (0.1, 48.8) milli-em
                    //   bracket; 20 sits mid-bracket.
                    const double EmGridSlotKernFloorMilli = 20.0;
                    var fsEm = st.FontSize / 12.0;
                    var cssEm = Math.Floor(fsEm * 100.0) / 100.0;
                    var scale = cssEm > 0 ? fsEm / cssEm : 1.0;
                    double sumE = 0, faceSum = 0;
                    foreach (var x in its) { sumE += x.E; faceSum += x.FaceMilli; }
                    var dKern = its.Count(x => x.IsSlot
                        && Math.Abs(x.E) >= EmGridSlotKernFloorMilli);
                    if (dKern == 0) dKern = slots;   // all-bare span: every slot carries
                    var lsTerms = its.Count;
                    wsEm = Math.Round(
                        (scale * sumE - scale * (scale - 1.0) * faceSum - lsTerms * lsMilli)
                        / dKern / 1000.0,
                        2, MidpointRounding.AwayFromZero);
                    var fitEnv = Environment.GetEnvironmentVariable("ASPOSE_PH2_FIT");
                    if (fitEnv is "1" or "2")
                    {
                        var head = new StringBuilder();
                        foreach (var x in its)
                        {
                            if (head.Length >= 28) break;
                            head.Append(x.IsSlot ? ' ' : x.Ch);
                        }
                        Console.Error.WriteLine(
                            $"[fit] n={its.Count} D={dKern}/{slots} S={scale:F5} " +
                            $"sumE={sumE:F2} face={faceSum:F1} ls={lsMilli:F1} " +
                            $"ws={wsEm:F2} |{head}|");
                        if (fitEnv == "2")
                            foreach (var x in its)
                                Console.Error.WriteLine(
                                    $"  [it] {(x.IsSlot ? "SLOT" : (x.Text ?? x.Ch.ToString())),-4} " +
                                    $"E={x.E:F2} face={x.FaceMilli:F1} x={x.StartX:F2}");
                    }
                }
                else if (inheritWs is { } iw && !emGrid)
                {
                    // The em-compensation dialect never inherits a filler's ws:
                    // a slotless span there carries NO word-spacing (the import
                    // charges ws at every adjacent-ideograph boundary, so an
                    // inherited filler rate would re-stretch the whole span).
                    wsEm = Math.Round(iw, 4, MidpointRounding.AwayFromZero);
                }
                var t2 = new StringBuilder();
                foreach (var x in its)
                {
                    if (x.IsSlot) t2.Append(' ');
                    else if (x.Text is not null) t2.Append(x.Text);
                    else t2.Append(x.Ch);
                }
                text = EscapeHtml(t2.ToString());
            }

            var emVal = Math.Round(lsMilli / 1000.0, 4, MidpointRounding.AwayFromZero);
            var pxVal = Math.Round(lsMilli * fs * 4.0 / 3.0 / 1000.0, 4, MidpointRounding.AwayFromZero);
            var lsNum = styleReg.LetterSpacingExact(emVal, pxVal);
            lastFontNum = fontNum; lastLhNum = lhNum; lastLsNum = lsNum;

            // A bold/italic run carries its weight inline: the emitted font class
            // names the FAMILY only, so a viewer falling back to a system face
            // would otherwise render the run regular.
            var weightCss = StlWeightStyleCss(st.FauxBold, st.FontStyle);
            var wsCss = wsEm is { } w
                ? $"word-spacing:{w.ToString("0.####", CultureInfo.InvariantCulture)}em;"
                : "";
            var wsAttr = weightCss.Length + wsCss.Length > 0
                ? $" style=\"{weightCss}{wsCss}\""
                : "";
            // A link annotation covers a RECTANGLE, not a line: each span resolves
            // its OWN target from its glyph extent, so a row of per-word hotspots
            // gives each word its own href instead of putting the whole line inside
            // the first rect's anchor. A span inside a line-wide rect still binds to
            // that rect (it is the first match), which is how a per-word hotspot
            // nested in a row-spanning link ends up with no anchor of its own.
            var spanLink = popupItems is null && linkFor is not null
                ? linkFor(its[0].StartX, its[^1].StartX)
                : null;
            if (spanLink is not null)
            {
                sb.Append($"<a href=\"{EscapeHtml(spanLink.Uri)}\"" +
                    (spanLink.Uri.StartsWith('#') ? ">" : " target=\"_blank\">"));
                spanLink.Wrapped = true;
            }
            sb.Append($"<span class=\"{classNamer.Attr(fontNum, lhNum, lsNum)}\"{wsAttr}>");
            sb.Append(text);
            if (s == spans.Count - 1 && renderedChars > 1 && popupItems is null)
                sb.Append(" &nbsp;");
            sb.Append("</span>");
            if (spanLink is not null) sb.Append("</a>");
        }

        if (popupItems is not null)
        {
            var listNum = styleReg.PopupList(popupBoxNum);
            sb.Append($"<div class=\"{classNamer.Cls(listNum)}\">");
            foreach (var (label, href) in popupItems)
                sb.Append($"<a href=\"{href}\" class=\"{classNamer.Cls(lastFontNum)} " +
                    $"{classNamer.Cls(lastLhNum)}  {classNamer.Cls(lastLsNum)}\">{EscapeHtml(label)}</a>");
            sb.Append("</div></div>");
        }
        sb.Append("</div>\n");
        }
    }

    private static IEnumerable<string> OrderStlRun(List<(double L, double T, string Html)> run)
    {
        if (run.Count <= 1)
        {
            foreach (var d in run) yield return d.Html;
            yield break;
        }
        const double RowTol = 0.05;   // divs within this top distance share a row
        const double LaneTol = 0.1;   // lefts within this distance share a column
        var rows = new List<List<(double L, double T, string Html)>>();
        foreach (var d in run.OrderBy(x => x.T).ToList())
        {
            if (rows.Count > 0 && Math.Abs(rows[^1][0].T - d.T) <= RowTol) rows[^1].Add(d);
            else rows.Add(new List<(double L, double T, string Html)> { d });
        }
        static bool Near(double a, double b) => Math.Abs(a - b) <= LaneTol;
        var regions = new List<List<List<(double L, double T, string Html)>>>();
        List<double>? regionLanes = null;
        foreach (var row in rows)
        {
            var rowLanes = new List<double>();
            foreach (var d in row)
                if (!rowLanes.Exists(x => Near(x, d.L))) rowLanes.Add(d.L);
            var chains = regionLanes is not null
                && (rowLanes.TrueForAll(l => regionLanes.Exists(r => Near(l, r)))
                    || regionLanes.TrueForAll(r => rowLanes.Exists(l => Near(l, r))));
            if (!chains)
            {
                regions.Add(new List<List<(double L, double T, string Html)>>());
                regionLanes = new List<double>();
            }
            regions[^1].Add(row);
            foreach (var l in rowLanes)
                if (!regionLanes!.Exists(r => Near(r, l))) regionLanes.Add(l);
        }
        // A LEADER region: every row is a label column plus a title cell whose
        // text runs out in a dot leader. Consecutive leader regions form a
        // CHAIN, and a chain emits as: the first region's label
        // cells plus the HEAD row's label of a deeper second region, then every
        // title cell in row order, then the remaining label cells in row order.
        // Anything else emits region-major: lanes left-to-right, columns
        // top-down.
        static bool IsLeaderRegion(List<List<(double L, double T, string Html)>> region)
        {
            foreach (var row in region)
            {
                if (row.Count < 2) return false;
                var rightmost = row[0];
                foreach (var d in row) if (d.L > rightmost.L) rightmost = d;
                if (!System.Text.RegularExpressions.Regex.IsMatch(rightmost.Html, @"\.{8,}"))
                    return false;
            }
            return true;
        }
        var ri = 0;
        while (ri < regions.Count)
        {
            var chainLen = 0;
            while (ri + chainLen < regions.Count && IsLeaderRegion(regions[ri + chainLen])) chainLen++;
            if (chainLen >= 2)
            {
                var chain = regions.GetRange(ri, chainLen);
                double LabelLeft(List<List<(double L, double T, string Html)>> region)
                {
                    var min = double.MaxValue;
                    foreach (var row in region) foreach (var d in row) min = Math.Min(min, d.L);
                    return min;
                }
                var deeperSecond = LabelLeft(chain[1]) > LabelLeft(chain[0]) + LaneTol;
                var labelsFirst = new List<string>();
                var titles = new List<string>();
                var labelsRest = new List<string>();
                for (var ci = 0; ci < chain.Count; ci++)
                {
                    for (var rowIdx = 0; rowIdx < chain[ci].Count; rowIdx++)
                    {
                        var row = chain[ci][rowIdx];
                        var rightmost = row[0];
                        foreach (var d in row) if (d.L > rightmost.L) rightmost = d;
                        var leading = ci == 0 || (ci == 1 && rowIdx == 0 && deeperSecond);
                        foreach (var d in row)
                        {
                            if (ReferenceEquals(d.Html, rightmost.Html)) titles.Add(d.Html);
                            else if (leading) labelsFirst.Add(d.Html);
                            else labelsRest.Add(d.Html);
                        }
                    }
                }
                foreach (var h in labelsFirst) yield return h;
                foreach (var h in titles) yield return h;
                foreach (var h in labelsRest) yield return h;
                ri += chainLen;
                continue;
            }
            var region = regions[ri];
            var lanes = new List<double>();
            foreach (var row in region)
                foreach (var d in row)
                    if (!lanes.Exists(x => Near(x, d.L))) lanes.Add(d.L);
            lanes.Sort();
            foreach (var lane in lanes)
                foreach (var row in region)
                    foreach (var d in row)
                        if (Near(d.L, lane)) yield return d.Html;
            ri++;
        }
    }
}

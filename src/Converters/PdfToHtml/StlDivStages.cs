using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
// Solved-div emission helpers.
    // A drawn NO-BREAK SPACE is a word gap to the line solver, exactly like a
    // drawn space: it is emitted as a plain word space inside one
    // span, and a line made of nothing else is dropped (a scanner's trailing nbsp
    // runs produce whole such rows). Treated as a character instead, it cut a
    // span on both sides - it usually arrives in its own font - atomizing the
    // line into one span per word.
    private static bool IsSpaceGlyph(char c) => c is ' ' or ' ';

    private static void EmitStlPart(StlDivState sd, List<StlItem> items)
    {
        sd.partItems = items;

        sd.cut = new bool[sd.partItems.Count];
        sd.lastRendered = -1;
        SeatStlPartItems(sd);

        sd.spans = new List<(List<StlItem> Items, int Style, bool IsNbsp, double? InheritWs)>();
        sd.cur = null;
        sd.curStyle = 0;
        sd.foldedSlots = new List<double>();
        sd.pendingInheritWs = null;
        for (var k = 0; k < sd.partItems.Count; k++)
        {
            CollectStlItemRun(sd, k);
        }
        Close(sd);
        if (sd.spans.Count == 0) return;

        sd.first = sd.spans[0];
        sd.st0 = sd.styles[sd.first.Style];
        sd.left = (sd.first.Items[0].StartX - sd.pageLLX) / 12.0 - sd.turnedOverShiftLeftEm;
        sd.top = (sd.yTop - sd.baselineY - sd.st0.Ascent * sd.st0.FontSize) / 12.0 - sd.turnedOverShiftTopEm;
        sd.sb.Append($"<div class=\"{sd.divCls}\" style=\"left:{Em4T(sd.left)}em;top:{Em4T(sd.top)}em;{sd.zStyle}\">");

        sd.popupBoxNum = 0;
        if (sd.popupItems is not null)
        {
            sd.popupBoxNum = sd.styleReg.PopupBox();
            sd.sb.Append($"<div class=\"{sd.classNamer.Cls(sd.popupBoxNum)}\">");
        }

        sd.renderedChars = 0;
        foreach (var sp in sd.spans)
            if (!sp.IsNbsp) sd.renderedChars += sp.Items.Count(x => !x.IsSlot);

        sd.lastFontNum = 0;
        sd.lastLhNum = 0;
        sd.lastLsNum = 0;
        for (var s = 0; s < sd.spans.Count; s++)
        {
            EmitStlSpan(sd, s);
        }

        if (sd.popupItems is not null)
        {
            var listNum = sd.styleReg.PopupList(sd.popupBoxNum);
            sd.sb.Append($"<div class=\"{sd.classNamer.Cls(listNum)}\">");
            foreach (var (label, href) in sd.popupItems)
                sd.sb.Append($"<a href=\"{href}\" class=\"{sd.classNamer.Cls(sd.lastFontNum)} " +
                    $"{sd.classNamer.Cls(sd.lastLhNum)}  {sd.classNamer.Cls(sd.lastLsNum)}\">{EscapeHtml(label)}</a>");
            sd.sb.Append("</div></div>");
        }
        sd.sb.Append("</div>\n");
    }

    /// <summary>An empty line ends here; a three-item line without space glyphs becomes a label popup, and a line without popup items emits its parts by span; true when the line was emitted, otherwise null.</summary>
    private static bool? EmitStlPopupsOrEarlyLine(StlDivState sd)
    {
        if (sd.items.Count == 0) return true;

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
        if (sd.popupItems is null && !sd.lineHasSpaceGlyph && sd.items.Count >= 3
            && !sd.items[0].IsSlot && sd.items[1].IsSlot && !sd.items[2].IsSlot
            && sd.headGapPt > 0.95 * sd.headFs && sd.headFs < 11.5)
        {
            EmitStlPart(sd, new List<StlItem> { sd.items[0] });
            EmitStlPart(sd, sd.items.GetRange(2, sd.items.Count - 2));
            return true;
        }
        // A TAB — a gap a quad or more past the pen on a line that draws real
        // space glyphs — starts a fresh positioned div per segment: a
        // single-char head splits past 1.05 of the font
        // size (1.00 folds, 1.10 splits), a longer head past 1.50 (1.45 folds,
        // 1.55 splits); smaller stretches stay in the line as word-spacing. A
        // spaceless line keeps the lone-char rule above instead.
        if (sd.popupItems is null)
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
            for (var k = 0; k < sd.items.Count; k++)
            {
                if (!sd.items[k].IsSlot) { headChars++; continue; }
                var fsSlot = Math.Max(0.01, sd.styles[sd.items[k].Style].FontSize);
                var loneHeadEm = sd.emGrid ? TabSplitLoneHeadEmGrid : TabSplitLoneHeadEm;
                var thr = (headChars <= 1 ? loneHeadEm : TabSplitWordHeadEm) * fsSlot;
                // A spaceless CJK line still splits at a COLUMN-sized gap in the
                // em-compensation dialect: a masthead's pieces sit 5+ em apart
                // (its word gaps stay under ~0.8 em) and each piece is emitted
                // as its own div rather than a giant word-spacing.
                var canSplit = sd.lineHasSpaceGlyph
                    || (sd.emGrid && sd.items[k].GapPt > TabSplitNoSpaceEmGrid * fsSlot);
                if (canSplit && sd.items[k].GapPt > thr && k + 1 < sd.items.Count)
                {
                    tabParts ??= new List<List<StlItem>>();
                    tabParts.Add(sd.items.GetRange(partStart, k - partStart));
                    partStart = k + 1;
                    headChars = 0;
                }
            }
            if (tabParts is not null)
            {
                tabParts.Add(sd.items.GetRange(partStart, sd.items.Count - partStart));
                foreach (var part in tabParts)
                    if (part.Count > 0) EmitStlPart(sd, part);
                return true;
            }
        }
        return null;
    }

    /// <summary>One glyph of the solved line: a space is consumed by the gap handling, a run glyph joins the current item with its advance and letter-spacing error, and the line-final glyph closes the line; false when the line is complete.</summary>
    private static bool EmitStlGlyph(StlDivState sd)
    {
        sd.g = sd.glyphs[sd.i];
        if (IsSpaceGlyph(sd.g.Ch)) { sd.i++; return true; }   // consumed by gap handling below
        sd.st = sd.styles[sd.g.Style];
        sd.fs = Math.Max(0.01, sd.st.FontSize);
        sd.fsEff = Math.Floor(sd.fs * 1000.0) / 1000.0;

        sd.itemEnd = sd.i;
        sd.wSum = sd.g.WidthsAdv;
        sd.tailW = 0;
        sd.itemText = null;
        sd.fuseByFace = false;
        MeasureStlItem(sd);
        sd.ttfPt = sd.ttfMilliItem / 1000.0 * sd.fsEff;
        sd.penPt = sd.wSum;
        if (sd.emGrid && sd.st.HasEmbeddedMetrics)
            sd.penPt -= (Math.Round(sd.g.TtfMilli) - sd.g.TtfMilli) / 1000.0 * sd.fsEff;

        sd.j = sd.itemEnd + 1;
        sd.sawSpace = false;
        sd.spaceStyle = sd.g.Style;
        sd.spaceGlyphMilli = 0;
        sd.spaceDrawnPt = 0;
        while (sd.j <= sd.hi && IsSpaceGlyph(sd.glyphs[sd.j].Ch))
        {
            sd.sawSpace = true;
            sd.spaceStyle = sd.glyphs[sd.j].Style;
            sd.spaceGlyphMilli = sd.glyphs[sd.j].TtfMilli;
            sd.spaceDrawnPt += sd.glyphs[sd.j].WidthsAdv;
            sd.j++;
        }

        if (sd.j > sd.hi)
        {
            // Line-final char: error is advance-only and never enters ls. In
            // the em-compensation region an expansion tail's advance residue
            // is the kern ADJACENT TO THE TRAILING SPACE — excluded
            // (a ±288 kern before the trailing space is invisible).
            var eFin = (sd.penPt - (sd.emGrid ? sd.tailW : 0) - sd.ttfPt) / sd.fs * 1000.0;
            sd.items.Add(new StlItem { Ch = sd.g.Ch, Text = sd.itemText, Style = sd.g.Style, StartX = sd.g.StartX,
                E = eFin, LsE = eFin + (sd.wSum - sd.penPt) / sd.fs * 1000.0 + sd.lsAdjMilli * sd.fsEff / sd.fs, LsEligible = false,
                FaceMilli = sd.ttfPt / sd.fs * 1000.0 });
            return false;
        }

        sd.mMilliSlot = sd.sawSpace && sd.spaceStyle == sd.g.Style
            ? sd.styles[sd.spaceStyle].SpaceAdvMilli
            : sd.st.SpaceAdvMilli;
        sd.mPt = sd.mMilliSlot / 1000.0 * sd.fs;
        sd.gapPt = sd.glyphs[sd.j].StartX - sd.g.StartX - sd.wSum;
        if (sd.sawSpace)
        {
            var drawnPt = sd.spaceDrawnPt > 0.01
                ? sd.spaceDrawnPt
                : sd.spaceGlyphMilli / 1000.0 * sd.styles[sd.spaceStyle].FontSize;
            sd.slotFires = drawnPt > 0.01 ? sd.gapPt >= 0.6 * drawnPt : sd.gapPt >= 0.6 * sd.mPt;
        }
        else
        {
            // The line's uniform tracking (see lineSpreadPt above) is not a
            // word gap; only the excess over it opens a slot.
            sd.slotFires = sd.gapPt - sd.lineSpreadPt >= 0.6 * sd.mPt;
        }

        if (!sd.slotFires)
        {
            // Plain gap (dropped space or kern): folds into this char's error.
            var eFold = (sd.penPt + sd.gapPt - sd.ttfPt) / sd.fs * 1000.0;
            sd.items.Add(new StlItem { Ch = sd.g.Ch, Text = sd.itemText, Style = sd.g.Style, StartX = sd.g.StartX,
                E = eFold, LsE = eFold + (sd.wSum - sd.penPt) / sd.fs * 1000.0 + sd.lsAdjMilli * sd.fsEff / sd.fs, LsEligible = true,
                FaceMilli = sd.ttfPt / sd.fs * 1000.0 });
        }
        else
        {
            // Word-final char, then the space slot. The slot rides on the
            // LINE's style (a word-gap space drawn with its own font is
            // coerced — it burns no font class of its own).
            var eWf = (sd.penPt - sd.ttfPt) / sd.fs * 1000.0;
            sd.items.Add(new StlItem { Ch = sd.g.Ch, Text = sd.itemText, Style = sd.g.Style, StartX = sd.g.StartX,
                E = eWf, LsE = eWf + (sd.wSum - sd.penPt) / sd.fs * 1000.0 + sd.lsAdjMilli * sd.fsEff / sd.fs, LsEligible = false,
                FaceMilli = sd.ttfPt / sd.fs * 1000.0 });
            sd.items.Add(new StlItem { IsSlot = true, Ch = ' ', Style = sd.g.Style,
                StartX = sd.g.StartX + sd.wSum,
                E = (sd.gapPt - sd.mPt) / Math.Max(0.01, sd.styles[sd.spaceStyle].FontSize) * 1000.0,
                GapPt = sd.gapPt,
                Synth = !sd.sawSpace,
                FaceMilli = sd.mPt / Math.Max(0.01, sd.styles[sd.spaceStyle].FontSize) * 1000.0 });
            if (sd.items.Count == 2) { sd.headGapPt = sd.gapPt; sd.headFs = sd.fs; }
        }
        sd.i = sd.j;
        return true;
    }

    /// <summary>The line's glyph range without its outer spaces, its style registrations, its space-glyph and em-grid facts; true when nothing remains to emit, otherwise null.</summary>
    private static bool? TrimAndClassifyStlLine(StlDivState sd)
    {
        sd.hi = sd.glyphs.Count - 1;
        while (sd.lo <= sd.hi && IsSpaceGlyph(sd.glyphs[sd.lo].Ch)) sd.lo++;
        while (sd.hi >= sd.lo && IsSpaceGlyph(sd.glyphs[sd.hi].Ch)) sd.hi--;
        if (sd.hi < sd.lo) return true;   // whitespace-only line: nothing rendered

        // Every style needs a real browser-model advance: either it resolves to an
        // installed face, or its font serves the embedded program's own metrics.
        // The em-compensation dialect keeps the line in the solved path on the
        // fallback advance model instead — bailing to the legacy group emission
        // flattened a masthead's mixed font/size runs into ONE span (no per-font
        // spans, no column splits, no synthesized gaps) whenever a single piece
        // used a font that neither embeds nor installs.
        foreach (var st in sd.styles)
        {
            if (st.FaceName is null && !st.HasEmbeddedMetrics)
            {
                if (!sd.emGrid) return false;
                st.UseFallbackMetrics = true;
            }
            // A SUBSTITUTE face (SimSun standing in for a font that neither
            // embeds nor installs) measures approximately: the default
            // four-decimal dialect's outlier atomization would cut spans at
            // every drawn-vs-substitute divergence, so those lines keep the
            // legacy group emission there. The em-compensation dialect never
            // atomizes and solves against the substitute basis.
            if (!sd.emGrid && st.SubstituteFace) return false;
        }

        sd.items = new List<StlItem>();
        sd.lineHasSpaceGlyph = false;
        for (var t0 = sd.lo; t0 <= sd.hi; t0++)
            if (IsSpaceGlyph(sd.glyphs[t0].Ch) && !sd.glyphs[t0].SynthSpace) { sd.lineHasSpaceGlyph = true; break; }
        sd.lineSpreadPt = 0.0;
        if (sd.emGrid)
        {
            var gaps = new List<double>();
            for (var t0 = sd.lo; t0 < sd.hi; t0++)
            {
                if (IsSpaceGlyph(sd.glyphs[t0].Ch) || IsSpaceGlyph(sd.glyphs[t0 + 1].Ch)) continue;
                var rg = sd.glyphs[t0 + 1].StartX - sd.glyphs[t0].StartX - sd.glyphs[t0].WidthsAdv;
                if (rg > 0.02) gaps.Add(rg);
            }
            const int SpreadMinSamples = 4;
            if (gaps.Count >= SpreadMinSamples)
            {
                gaps.Sort();
                sd.lineSpreadPt = gaps[gaps.Count / 2];
            }
        }
        sd.headGapPt = 0;
        sd.headFs = 0;
        sd.i = sd.lo;
        return null;
    }

    /// <summary>The item's glyph range extends over its expansion tails, and its text, face advance and pen width are measured.</summary>
    private static void MeasureStlItem(StlDivState sd)
    {
        while (sd.itemEnd + 1 <= sd.hi && sd.glyphs[sd.itemEnd + 1].ExpansionTail
            && sd.glyphs[sd.itemEnd + 1].Style == sd.g.Style)
        {
            sd.itemEnd++;
            sd.wSum += sd.glyphs[sd.itemEnd].WidthsAdv;
            sd.tailW += sd.glyphs[sd.itemEnd].WidthsAdv;
            sd.itemText = (sd.itemText ?? sd.g.Ch.ToString()) + sd.glyphs[sd.itemEnd].Ch;
            sd.fuseByFace |= sd.glyphs[sd.itemEnd].FuseByFace;
        }
        sd.ttfMilliItem = sd.g.TtfMilli;
        sd.lsAdjMilli = 0.0;
        if (sd.itemText is not null)
        {
            if (sd.fuseByFace && !sd.emGrid && sd.st.FaceName is not null)
            {
                double faceSum = 0;
                foreach (var chF in sd.itemText) faceSum += sd.st.TtfMilli(chF);
                sd.ttfMilliItem = faceSum;
            }
            else if (sd.emGrid && sd.st.ProgramCharMilli is not null)
            {
                // The em-compensation FACE basis for a ligature is its
                // COMPONENT advances from the embedded program's own metrics
                // (an 'ft' ligature's components-vs-lig delta comes to
                // +27.34 exactly; 'ffl' +39.55).
                // Unresolvable components keep the LIG advance.
                double compSum = 0;
                var okComp = true;
                foreach (var chF in sd.itemText)
                {
                    if (sd.st.ProgramCharMilli(chF) is { } aC) compSum += aC;
                    else { okComp = false; break; }
                }
                if (okComp && compSum > 0)
                {
                    sd.lsAdjMilli = compSum - sd.ttfMilliItem;
                    sd.ttfMilliItem = compSum;
                }
                else
                    for (var t = sd.i + 1; t <= sd.itemEnd; t++) sd.ttfMilliItem += sd.glyphs[t].TtfMilli;
            }
            else
            {
                for (var t = sd.i + 1; t <= sd.itemEnd; t++) sd.ttfMilliItem += sd.glyphs[t].TtfMilli;
            }
        }
    }
}

using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
// One table-of-contents entry: its title wrapping and its render on the TOC page.
    // Entries are set at the level format's size when the caller set
    // one, else the first explicitly-sized segment, else the 10 pt
    // LevelFormat default. The heading's OWN TextState is NOT in the
    // chain: a 24 pt heading's TOC line draws at the
    // plain 10 pt entry size (only the heading's in-content render
    // uses its TextState).
    private static List<string> WrapEntry(string s, double maxW, double fs, string face = "Helvetica")
    {
        var lines = new List<string>();
        var cur = new System.Text.StringBuilder();
        foreach (var word in s.Split(' '))
        {
            var trial = cur.Length == 0 ? word : cur + " " + word;
            if (MeasureEntry(trial, fs, face) <= maxW || cur.Length == 0)
            {
                if (cur.Length > 0) cur.Append(' ');
                cur.Append(word);
            }
            else
            {
                lines.Add(cur.ToString());
                cur.Clear();
                cur.Append(word);
            }
        }
        if (cur.Length > 0) lines.Add(cur.ToString());
        if (lines.Count == 0) lines.Add(string.Empty);
        return lines;
    }

    // Render ONE TOC entry with its line box starting at startY; returns
    // the Y the next entry (or following paragraph) continues from.
    private static double RenderTocEntry(PageLayoutState pl, Heading h, int destIdx, double startY)
    {
        pl.fontName ??= Table.RegisterFont(pl.page);
        pl.tocRendered.Add(h);
        // Top of the entry area — column jumps restart from here.
        pl.tocTopY ??= startY;
        var formatArray = pl.page.TocInfo!.FormatArray;
        var entryY = startY;

        var level = h.Level > 0 ? h.Level : 1;
        // TocInfo.FormatArray[level-1] carries this level's formatting
        // (font size, subsequent-lines indent, margins) — every entry
        // of a level is formatted from it. Falls back to the
        // heading's own TextState / margins when no level format is set.
        var fmt = formatArray is { Length: > 0 } fa && level - 1 < fa.Length
            ? fa[level - 1] : null;
        // The governing level format's TextState, bound once: it is null
        // exactly when no level format governs this entry, so every read
        // below resolves through that single null check.
        var fmtState = fmt?.TextState;
        // The entry's size and line spacing resolve through a fixed
        // chain: the level format wins, then the first SEGMENT
        // whose value was explicitly set (a segment font size set by the
        // caller beats the heading's own TextState), then the heading.
        double? segSize = null;
        double segSpacing = 0;
        foreach (Text.TextSegment hs in h.Segments)
        {
            if (segSize is null && hs.TextState.FontSizeTouched) segSize = hs.TextState.FontSize;
            if (segSpacing == 0) segSpacing = hs.TextState.LineSpacing;
            if (segSize is not null && segSpacing != 0) break;
        }
        var entrySize = fmtState is { FontSizeTouched: true } fts
            ? (double)fts.FontSize
            : segSize ?? DefaultTocEntrySize;
        // The level format's FontStyle picks the Standard-14 Helvetica
        // variant the whole entry (text, leader and page number) is set in.
        var entryFace = fmtState is null ? "Helvetica"
            : EntryFace((fmtState.IsBold ? Text.FontStyles.Bold : 0)
                | (fmtState.IsItalic ? Text.FontStyles.Italic : 0));
        // The level's leader style; the TocInfo-wide LineDash applies only
        // when no level format governs this entry.
        var entryLeader = fmt?.LineDash
            ?? pl.page.TocInfo?.LineDash ?? Text.TabLeaderType.Dot;
        // Subsequent (continuation) lines of a multi-line entry are
        // indented by this level's SubsequentLinesIndent.
        var subIndent = (double)(fmt?.SubsequentLinesIndent ?? 0);
        // The line pitch is the entry size PLUS the resolved LineSpacing —
        // plain entries pack one font-size apart, and an
        // entry with TextState.LineSpacing=18 at 11 pt steps 29 pt per line
        // (both its own wrapped lines and the gap to the next entry).
        var lineSpacing = fmtState is { } fmtTs && fmtTs.LineSpacing != 0
            ? (double)fmtTs.LineSpacing
            : segSpacing != 0 ? segSpacing : (double)h.TextState.LineSpacing;
        var lineH = entrySize + lineSpacing;
        // Every line box's BOTTOM sits exactly one pitch below the previous
        // line's bottom (the chain runs on rect bottoms, not baselines) —
        // startY is the previous line's bottom, so this entry's first
        // baseline is one pitch lower plus the font's descent.
        var descFrac = -Text.Standard14Fonts.GetDescent("Helvetica") / 1000.0;
        // The level format's (or heading's) Margin.Top is leading reserved
        // ABOVE the entry — every entry (the first
        // included) is pushed down by it, so consecutive entries sit lineH + Top apart.
        entryY -= fmt?.Margin?.Top ?? h.Margin?.Top ?? 0;
        entryY -= lineH - descFrac * entrySize;
        var prefix = string.Empty;
        if (h.IsAutoSequence)
        {
            if (level < pl.tocCounters.Length)
            {
                pl.tocCounters[level]++;
                for (var k = level + 1; k < pl.tocCounters.Length; k++) pl.tocCounters[k] = 0;
            }
            // The section number prints for every auto-
            // sequenced heading in the heading’s OWN style: the DEFAULT
            // style is arabic ("1  Heading 1") while
            // NumberingStyle.None prints no number at all and leaves
            // the bare separator ("  Heading 1").
            // The number is followed by TWO spaces (the
            // prefix fragment is "1  " — digit plus two space glyphs).
            var parts = new List<string>();
            for (var k = 1; k <= level && k < pl.tocCounters.Length; k++)
                parts.Add(Heading.FormatNumber(h.Style, pl.tocCounters[k]));
            if (parts.Count > 0) prefix = string.Join(".", parts) + "  ";
        }
        // A heading authored through SEGMENTS (Heading.Text left empty)
        // still titles its TOC entry — the entry text is the segment
        // chain's concatenation.
        var headingText = h.Text;
        if (string.IsNullOrEmpty(headingText) && h.Segments.Count > 0)
        {
            var segText = new System.Text.StringBuilder();
            foreach (Text.TextSegment hseg in h.Segments) segText.Append(hseg.Text);
            headingText = segText.ToString();
        }
        // The prefix is NOT part of the wrapped body: a numbering
        // style that prints no number leaves a whitespace-only
        // prefix, and the word splitter drops leading spaces. Line
        // one carries the prefix back verbatim once wrapped.
        var entryBody = headingText ?? string.Empty;
        // A segment carrying its OWN font measures the entry — and its
        // leader dots and page number — by that font's REAL advances:
        // the column-fit scale is computed in the entry font's metrics,
        // and positions are pinned to fractions of a point of those
        // advances (a Standard-14 approximation lands dots a few
        // hundredths of a point astray).
        System.Func<string, double>? entryAdvance = null;
        Text.Font? segFont0 = null;   // the collection indexer is 1-based
        foreach (Text.TextSegment hseg0 in h.Segments) { segFont0 = hseg0.TextState.Font; break; }
        if (segFont0?.SourceFontData?.TtfData is { Length: > 0 } segTtf)
        {
            try
            {
                var segParser = new Text.GlyphOutlineParser(segTtf);
                if (segParser.CMap.Count > 0)
                {
                    double upm = segParser.UnitsPerEm;
                    var sizeForMeasure = entrySize;
                    entryAdvance = s =>
                    {
                        double units = 0;
                        for (var ci = 0; ci < s.Length; ci++)
                        {
                            int cp = s[ci];
                            if (char.IsHighSurrogate(s[ci]) && ci + 1 < s.Length
                                && char.IsLowSurrogate(s[ci + 1]))
                            { cp = char.ConvertToUtf32(s[ci], s[ci + 1]); ci++; }
                            units += segParser.CMap.TryGetValue(cp, out var gid) && gid > 0
                                ? segParser.GetAdvanceWidth(gid)
                                : upm / 2.0;
                        }
                        return units / upm * sizeForMeasure;
                    };
                }
            }
            catch { /* unparsable program: Standard-14 measure */ }
        }
        var pageNumStr = destIdx > 0
            ? destIdx.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;
        var pageNumWidth = MeasureEntry(pageNumStr, entrySize, entryFace);

        double colLeft = pl.tocColLefts[pl.tocCol], colWidth = pl.tocColWidths[pl.tocCol];
        // Entries are NOT indented by heading level — every level starts
        // at the column left edge (plus any explicit level-
        // format left margin).
        var indent = colLeft + (double)(fmt?.Margin?.Left ?? 0);
        var rightStop = colLeft + colWidth - (double)(fmt?.Margin?.Right ?? 0);
        var pageNumX = rightStop - pageNumWidth;

        // The auto-number sits alone at the column left edge and the
        // entry TEXT hangs under it: every line after the first starts
        // at the prefix END, not back at the column edge, plus this
        // level’s SubsequentLinesIndent. A wrapped entry therefore
        // reads as one indented block beside its number.
        var prefixWidth = MeasureEntry(prefix, entrySize, entryFace);

        // Wrap the entry, honouring explicit line breaks in the heading
        // text (\r\n) first and width-wrapping each. Every rendered line
        // after the entry's first hangs at the prefix end plus
        // SubsequentLinesIndent, so its available width is
        // correspondingly narrower.
        List<(string text, double x)> WrapEntryLines()
        {
            var outLines = new List<(string, double)>();
            var firstX = indent + prefixWidth;
            var hangX = firstX + subIndent;
            var logical = entryBody.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (var ll in logical)
                foreach (var w in WrapEntry(ll, pageNumX - 6 - (outLines.Count == 0 ? firstX : hangX), entrySize, entryFace))
                    outLines.Add(outLines.Count == 0 ? (prefix + w, indent) : (w, hangX));
            return outLines;
        }
        var wrapped = WrapEntryLines();

        // Out of vertical room — the entry's line boxes plus its own
        // bottom margin must fit above the page's bottom margin (an
        // entry with Margin.Bottom=300 overflows even when its single
        // line alone would fit). Move to the next column for a
        // multi-column TOC; when the columns are exhausted, continue
        // on the next CONTINUATION page (one is inserted
        // after the TOC page — entries are never truncated).
        var entryMarginBottom = (double)(fmt?.Margin?.Bottom ?? h.Margin?.Bottom ?? 0);
        // LEVEL-GROUP ORPHAN CONTROL: an entry that STARTS a run of
        // same-level entries does not open the run at the page foot
        // alone — when fewer than TWO of the run's entries fit the
        // space left (and the whole run would fit one full TOC page),
        // the run opens on the next continuation page instead (the ten
        // level-2 entries open the final TOC page together while the
        // level-3 run fills the page before them). A run whose first
        // two entries fit — or one too long for any single page —
        // breaks wherever it runs out.
        var forceTocGroupBreak = false;
        var grpIdx = pl.tocEntries.FindIndex(e => ReferenceEquals(e.h, h));
        if (grpIdx > 0 && pl.tocColCount == 1
            && (pl.tocEntries[grpIdx - 1].h.Level > 0 ? pl.tocEntries[grpIdx - 1].h.Level : 1) != level)
        {
            double groupH = 0, firstTwoH = 0;
            var grpCount = 0;
            for (var gi = grpIdx; gi < pl.tocEntries.Count; gi++)
            {
                var gh = pl.tocEntries[gi].h;
                var gLevel = gh.Level > 0 ? gh.Level : 1;
                if (gLevel != level) break;
                var gText = gh.Text;
                if (string.IsNullOrEmpty(gText) && gh.Segments.Count > 0)
                {
                    var gsb = new System.Text.StringBuilder();
                    foreach (Text.TextSegment ghs in gh.Segments) gsb.Append(ghs.Text);
                    gText = gsb.ToString();
                }
                var gLines = 0;
                foreach (var gl in (gText ?? string.Empty)
                             .Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                    gLines += WrapEntry(gl, pageNumX - 6 - indent, entrySize, entryFace).Count;
                // An entry's footprint carries its level format's top AND
                // bottom margins — the fit rule below requires the bottom
                // margin to clear the page margin too.
                var gH = (double)(fmt?.Margin?.Top ?? gh.Margin?.Top ?? 0)
                    + System.Math.Max(1, gLines) * lineH
                    + (double)(fmt?.Margin?.Bottom ?? gh.Margin?.Bottom ?? 0);
                groupH += gH;
                grpCount++;
                if (grpCount <= 2) firstTwoH += gH;
            }
            var tocPageCapacity = pl.page.Height - pl.marginTop - pl.marginBottom;
            var tocRemaining = startY - pl.marginBottom;
            if (grpCount >= 2 && groupH <= tocPageCapacity + 0.5
                && firstTwoH > tocRemaining + 0.5)
                forceTocGroupBreak = true;
        }
        // Fit rule: the entry stays when its LAST line's ink bottom
        // (baseline minus the font descent) plus the entry's own
        // bottom margin clears the page bottom margin — a 45-entry
        // A4-landscape page keeps a line whose bottom lands 1 pt
        // above the margin; requiring a full
        // line height of clearance breaks one entry early.
        if (forceTocGroupBreak
            || entryY - (wrapped.Count - 1) * lineH - descFrac * entrySize - entryMarginBottom < pl.marginBottom)
        {
            if (pl.tocCol + 1 >= pl.tocColCount)
            {
                pl.tocSlot++;
                pl.tocCol = 0;
                // The continuation page has no title: its entry chain
                // anchors at the page's top margin cursor, first entry
                // one pitch below it (LLY = top − pitch, same rule as
                // any other line).
                pl.tocTopY = pl.page.Height - pl.marginTop;
                entryY = pl.tocTopY.Value
                    - (fmt?.Margin?.Top ?? h.Margin?.Top ?? 0)
                    - lineH + descFrac * entrySize;
            }
            else
            {
                pl.tocCol++;
                entryY = pl.tocTopY.Value - lineH + descFrac * entrySize;
            }
            colLeft = pl.tocColLefts[pl.tocCol];
            colWidth = pl.tocColWidths[pl.tocCol];
            indent = colLeft + (double)(fmt?.Margin?.Left ?? 0);
            rightStop = colLeft + colWidth - (double)(fmt?.Margin?.Right ?? 0);
            pageNumX = rightStop - pageNumWidth;
            wrapped = WrapEntryLines();
        }

        // All entry pieces are positioned with Tm (absolute text matrix),
        // not Td: column repositioning on one visual row goes out
        // via Tm, and RAW extraction merges same-row Tm shows into
        // one output line ("1  Heading 1.....2"), while Td-per-BT blocks
        // stay one-line-per-show (table cells). This operator
        // choice lets the extractor reassemble the entry as one
        // line without loosening any extraction heuristics.
        var b = new Content.ContentStreamBuilder();
        var entryFontRes = entryFace == "Helvetica"
            ? pl.fontName : Table.RegisterFont(pl.page, entryFace);
        void TmShow(double x, double y, string s)
        {
            // CJK entry text (Japanese outline titles and the like) has no
            // glyphs in the Standard-14 set — embed a script-matched CJK
            // face and show the run as CID hex.
            if (s.Length > 0 && ContainsCjkText(s))
            {
                pl.tocCjkTtf ??= Text.CjkFallbackFont.ResolveEmbeddableBytes(s);
                if (pl.tocCjkTtf is { Length: > 0 })
                {
                    var cjkDict = Table.ResolvePageFontDict(pl.page);
                    var (cjkRes, cjkHex) = Text.Type0FontEmbedder.Embed(
                        cjkDict, pl.tocCjkTtf, "CJK", s.Replace('\t', ' '),
                        stripSpacesInBaseFont: true);
                    b.BeginText().SetFont(cjkRes, entrySize).SetFillColor(0, 0, 0)
                        .SetTextMatrix(1, 0, 0, 1, x, y).ShowTextHex(cjkHex).EndText();
                    return;
                }
            }
            b.BeginText().SetFont(entryFontRes, entrySize).SetFillColor(0, 0, 0)
                .SetTextMatrix(1, 0, 0, 1, x, y).ShowText(s).EndText();
        }
        // Every entry opens with an EMPTY text show at the
        // line start (extraction reports an empty fragment before each
        // entry's text), keeping fragment counts and order stable.
        TmShow(wrapped[0].x, entryY, string.Empty);
        // A leadered, numbered entry defers its FINAL line to the leader
        // emission: that line is drawn horizontally scaled to the column
        // (Tz), and the scale depends on the page-number width — which is
        // only known after the final page sequence exists.
        var deferFinal = entryLeader != Text.TabLeaderType.None
            && pl.page.TocInfo?.IsShowPageNumbers != false
            && fmtState?.Underline != true;
        for (var li = 0; li < wrapped.Count; li++)
        {
            var finalLine = li == wrapped.Count - 1;
            // The first line carries the auto-number as its OWN show ("1  "
            // digit + two spaces, the prefix fragment) with the
            // heading text show starting exactly at the prefix's end.
            if (li == 0 && prefix.Length > 0 && wrapped[0].text.StartsWith(prefix, StringComparison.Ordinal))
            {
                TmShow(wrapped[0].x, entryY, prefix);
                if (!(deferFinal && finalLine))
                    TmShow(wrapped[0].x + prefixWidth, entryY,
                        wrapped[0].text.Substring(prefix.Length));
                continue;
            }
            if (deferFinal && finalLine) continue;
            TmShow(wrapped[li].x, entryY - li * lineH, wrapped[li].text);
        }

        var lastY = entryY - (wrapped.Count - 1) * lineH;
        var lastLineW = MeasureEntry(wrapped[^1].text, entrySize, entryFace);

        // The entry's link box spans the whole entry: height is exactly
        // the rendered-line count times the line PITCH (2×29 = 58 pt
        // for a two-line 11 pt entry with 18 pt line
        // spacing), top where the entry's box started, bottom at the last
        // line's rect bottom, right edge on the column's right stop.
        var linkTop = entryY - descFrac * entrySize + lineH;
        var linkRect = new Rectangle(indent, linkTop - wrapped.Count * lineH,
            colLeft + colWidth, linkTop);

        // Emission is deferred (see tocPending above): the leader's page
        // number must reflect the FINAL page sequence, which isn't known
        // until every entry is laid out and the continuation pages are
        // inserted. The pre-leader shows (empty + prefix + lines) are
        // final bytes already.
        pl.tocPending.Add((pl.tocSlot, b.Build(), wrapped[^1].x + lastLineW, lastY,
            entrySize, entryFace, entryLeader, rightStop,
            pl.page.TocInfo?.IsShowPageNumbers != false,
            fmtState?.Underline == true,
            prefix, wrapped[0].x,
            h.DestinationPage, destIdx, linkRect, h,
            deferFinal ? wrapped[^1].text : string.Empty, wrapped[^1].x,
            entryAdvance));

        // The next entry (or paragraph) continues from this entry's last
        // line-box BOTTOM (baseline minus descent) — the next entry then
        // subtracts its OWN pitch, chaining rect bottoms one pitch apart.
        // Margin.Bottom resolves like Margin.Top: level format first, then
        // the heading's own margin (which can space entries 300 pt apart
        // via heading.Margin.Bottom with no FormatArray set).
        return lastY - descFrac * entrySize - entryMarginBottom;
    }
}

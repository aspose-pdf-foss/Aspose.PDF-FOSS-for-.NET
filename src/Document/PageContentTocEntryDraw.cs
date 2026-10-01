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
// TOC entry helpers: the entry line wrap and the positioned text show.
    // Wrap the entry, honouring explicit line breaks in the heading
    // text (\r\n) first and width-wrapping each. Every rendered line
    // after the entry's first hangs at the prefix end plus
    // SubsequentLinesIndent, so its available width is
    // correspondingly narrower.
    private static List<(string text, double x)> WrapEntryLines(TocEntryState te)
    {
        var outLines = new List<(string, double)>();
        var firstX = te.indent + te.prefixWidth;
        var hangX = firstX + te.subIndent;
        var logical = te.entryBody.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        foreach (var ll in logical)
            foreach (var w in WrapEntry(ll, te.pageNumX - 6 - (outLines.Count == 0 ? firstX : hangX), te.entrySize, te.entryFace))
                outLines.Add(outLines.Count == 0 ? (te.prefix + w, te.indent) : (w, hangX));
        return outLines;
    }

    private static void TmShow(TocEntryState te, PageLayoutState pl, double x, double y, string s)
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
                te.b.BeginText().SetFont(cjkRes, te.entrySize).SetFillColor(0, 0, 0)
                    .SetTextMatrix(1, 0, 0, 1, x, y).ShowTextHex(cjkHex).EndText();
                return;
            }
        }
        te.b.BeginText().SetFont(te.entryFontRes, te.entrySize).SetFillColor(0, 0, 0)
            .SetTextMatrix(1, 0, 0, 1, x, y).ShowText(s).EndText();
    }

    /// <summary>Emit the entry: its lines and prefix as positioned text shows, the deferred leader and page number, the link rectangle and the pending slot.</summary>
    private static void EmitTocEntry(TocEntryState te, PageLayoutState pl, Heading h, int destIdx)
    {
        te.b = new Content.ContentStreamBuilder();
        te.entryFontRes = te.entryFace == "Helvetica"
            ? pl.fontName! : Table.RegisterFont(pl.page, te.entryFace);
        // Every entry opens with an EMPTY text show at the
        // line start (extraction reports an empty fragment before each
        // entry's text), keeping fragment counts and order stable.
        TmShow(te, pl, te.wrapped[0].x, te.entryY, string.Empty);
        te.deferFinal = te.entryLeader != Text.TabLeaderType.None
            && pl.page.TocInfo?.IsShowPageNumbers != false
            && te.fmtState?.Underline != true;
        for (var li = 0; li < te.wrapped.Count; li++)
        {
            var finalLine = li == te.wrapped.Count - 1;
            // The first line carries the auto-number as its OWN show ("1  "
            // digit + two spaces, the prefix fragment) with the
            // heading text show starting exactly at the prefix's end.
            if (li == 0 && te.prefix.Length > 0 && te.wrapped[0].text.StartsWith(te.prefix, StringComparison.Ordinal))
            {
                TmShow(te, pl, te.wrapped[0].x, te.entryY, te.prefix);
                if (!(te.deferFinal && finalLine))
                    TmShow(te, pl, te.wrapped[0].x + te.prefixWidth, te.entryY,
                        te.wrapped[0].text.Substring(te.prefix.Length));
                continue;
            }
            if (te.deferFinal && finalLine) continue;
            TmShow(te, pl, te.wrapped[li].x, te.entryY - li * te.lineH, te.wrapped[li].text);
        }

        te.lastY = te.entryY - (te.wrapped.Count - 1) * te.lineH;
        te.lastLineW = MeasureEntry(te.wrapped[^1].text, te.entrySize, te.entryFace);

        te.linkTop = te.entryY - te.descFrac * te.entrySize + te.lineH;
        te.linkRect = new Rectangle(te.indent, te.linkTop - te.wrapped.Count * te.lineH,
            te.colLeft + te.colWidth, te.linkTop);

        // Emission is deferred (see tocPending above): the leader's page
        // number must reflect the FINAL page sequence, which isn't known
        // until every entry is laid out and the continuation pages are
        // inserted. The pre-leader shows (empty + prefix + lines) are
        // final bytes already.
        pl.tocPending.Add((pl.tocSlot, te.b.Build(), te.wrapped[^1].x + te.lastLineW, te.lastY,
            te.entrySize, te.entryFace, te.entryLeader, te.rightStop,
            pl.page.TocInfo?.IsShowPageNumbers != false,
            te.fmtState?.Underline == true,
            te.prefix, te.wrapped[0].x,
            h.DestinationPage, destIdx, te.linkRect, h,
            te.deferFinal ? te.wrapped[^1].text : string.Empty, te.wrapped[^1].x,
            te.entryAdvance));

        // The next entry (or paragraph) continues from this entry's last
        // line-box BOTTOM (baseline minus descent) — the next entry then
        // subtracts its OWN pitch, chaining rect bottoms one pitch apart.
        // Margin.Bottom resolves like Margin.Top: level format first, then
        // the heading's own margin (which can space entries 300 pt apart
        // via heading.Margin.Bottom with no FormatArray set).
    }

    /// <summary>A grouped entry that would split from its group, or one forced to a new column, moves to the next column or page first.</summary>
    private static void BreakTocEntryColumn(TocEntryState te, PageLayoutState pl, Heading h, double startY)
    {
        te.grpIdx = pl.tocEntries.FindIndex(e => ReferenceEquals(e.h, h));
        if (te.grpIdx > 0 && pl.tocColCount == 1
            && (pl.tocEntries[te.grpIdx - 1].h.Level > 0 ? pl.tocEntries[te.grpIdx - 1].h.Level : 1) != te.level)
        {
            double groupH = 0, firstTwoH = 0;
            var grpCount = 0;
            for (var gi = te.grpIdx; gi < pl.tocEntries.Count; gi++)
            {
                var gh = pl.tocEntries[gi].h;
                var gLevel = gh.Level > 0 ? gh.Level : 1;
                if (gLevel != te.level) break;
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
                    gLines += WrapEntry(gl, te.pageNumX - 6 - te.indent, te.entrySize, te.entryFace).Count;
                // An entry's footprint carries its level format's top AND
                // bottom margins — the fit rule below requires the bottom
                // margin to clear the page margin too.
                var gH = (double)(te.fmt?.Margin?.Top ?? gh.Margin?.Top ?? 0)
                    + System.Math.Max(1, gLines) * te.lineH
                    + (double)(te.fmt?.Margin?.Bottom ?? gh.Margin?.Bottom ?? 0);
                groupH += gH;
                grpCount++;
                if (grpCount <= 2) firstTwoH += gH;
            }
            var tocPageCapacity = pl.page.Height - pl.marginTop - pl.marginBottom;
            var tocRemaining = startY - pl.marginBottom;
            if (grpCount >= 2 && groupH <= tocPageCapacity + 0.5
                && firstTwoH > tocRemaining + 0.5)
                te.forceTocGroupBreak = true;
        }
        // Fit rule: the entry stays when its LAST line's ink bottom
        // (baseline minus the font descent) plus the entry's own
        // bottom margin clears the page bottom margin — a 45-entry
        // A4-landscape page keeps a line whose bottom lands 1 pt
        // above the margin; requiring a full
        // line height of clearance breaks one entry early.
        if (te.forceTocGroupBreak
            || te.entryY - (te.wrapped.Count - 1) * te.lineH - te.descFrac * te.entrySize - te.entryMarginBottom < pl.marginBottom)
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
                te.entryY = pl.tocTopY.Value
                    - (te.fmt?.Margin?.Top ?? h.Margin?.Top ?? 0)
                    - te.lineH + te.descFrac * te.entrySize;
            }
            else
            {
                pl.tocCol++;
                te.entryY = pl.tocTopY!.Value - te.lineH + te.descFrac * te.entrySize;
            }
            te.colLeft = pl.tocColLefts[pl.tocCol];
            te.colWidth = pl.tocColWidths[pl.tocCol];
            te.indent = te.colLeft + (double)(te.fmt?.Margin?.Left ?? 0);
            te.rightStop = te.colLeft + te.colWidth - (double)(te.fmt?.Margin?.Right ?? 0);
            te.pageNumX = te.rightStop - te.pageNumWidth;
            te.wrapped = WrapEntryLines(te);
        }

    }

    /// <summary>The entry's prefix, heading text, per-glyph advance from the heading's own face, page number, indent and wrapped lines.</summary>
    private static void ResolveTocEntryText(TocEntryState te, PageLayoutState pl, Heading h, int destIdx)
    {
        if (h.IsAutoSequence)
        {
            if (te.level < pl.tocCounters.Length)
            {
                pl.tocCounters[te.level]++;
                for (var k = te.level + 1; k < pl.tocCounters.Length; k++) pl.tocCounters[k] = 0;
            }
            // The section number prints for every auto-
            // sequenced heading in the heading’s OWN style: the DEFAULT
            // style is arabic ("1  Heading 1") while
            // NumberingStyle.None prints no number at all and leaves
            // the bare separator ("  Heading 1").
            // The number is followed by TWO spaces (the
            // prefix fragment is "1  " — digit plus two space glyphs).
            var parts = new List<string>();
            for (var k = 1; k <= te.level && k < pl.tocCounters.Length; k++)
                parts.Add(Heading.FormatNumber(h.Style, pl.tocCounters[k]));
            if (parts.Count > 0) te.prefix = string.Join(".", parts) + "  ";
        }
        te.headingText = h.Text;
        if (string.IsNullOrEmpty(te.headingText) && h.Segments.Count > 0)
        {
            var segText = new System.Text.StringBuilder();
            foreach (Text.TextSegment hseg in h.Segments) segText.Append(hseg.Text);
            te.headingText = segText.ToString();
        }
        te.entryBody = te.headingText ?? string.Empty;
        te.entryAdvance = null;
        te.segFont0 = null;
        foreach (Text.TextSegment hseg0 in h.Segments) { te.segFont0 = hseg0.TextState.Font; break; }
        if (te.segFont0?.SourceFontData?.TtfData is { Length: > 0 } segTtf)
        {
            try
            {
                var segParser = new Text.GlyphOutlineParser(segTtf);
                if (segParser.CMap.Count > 0)
                {
                    double upm = segParser.UnitsPerEm;
                    var sizeForMeasure = te.entrySize;
                    te.entryAdvance = s =>
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
        te.pageNumStr = destIdx > 0
            ? destIdx.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;
        te.pageNumWidth = MeasureEntry(te.pageNumStr, te.entrySize, te.entryFace);

        te.colLeft = pl.tocColLefts[pl.tocCol];
        te.colWidth = pl.tocColWidths[pl.tocCol];
        te.indent = te.colLeft + (double)(te.fmt?.Margin?.Left ?? 0);
        te.rightStop = te.colLeft + te.colWidth - (double)(te.fmt?.Margin?.Right ?? 0);
        te.pageNumX = te.rightStop - te.pageNumWidth;

        te.prefixWidth = MeasureEntry(te.prefix, te.entrySize, te.entryFace);

        te.wrapped = WrapEntryLines(te);

        te.entryMarginBottom = (double)(te.fmt?.Margin?.Bottom ?? h.Margin?.Bottom ?? 0);
        te.forceTocGroupBreak = false;
    }

    /// <summary>The entry's format row, face, size, leader, indent, line pitch and the first baseline below the top margin.</summary>
    private static void ResolveTocEntryFormat(TocEntryState te, PageLayoutState pl, Heading h, double startY)
    {
        te.formatArray = pl.page.TocInfo!.FormatArray;
        te.entryY = startY;

        te.level = h.Level > 0 ? h.Level : 1;
        te.fmt = te.formatArray is { Length: > 0 } fa && te.level - 1 < fa.Length
            ? fa[te.level - 1] : null;
        te.fmtState = te.fmt?.TextState;
        te.segSize = null;
        te.segSpacing = 0;
        foreach (Text.TextSegment hs in h.Segments)
        {
            if (te.segSize is null && hs.TextState.FontSizeTouched) te.segSize = hs.TextState.FontSize;
            if (te.segSpacing == 0) te.segSpacing = hs.TextState.LineSpacing;
            if (te.segSize is not null && te.segSpacing != 0) break;
        }
        te.entrySize = te.fmtState is { FontSizeTouched: true } fts
            ? (double)fts.FontSize
            : te.segSize ?? DefaultTocEntrySize;
        te.entryFace = te.fmtState is null ? "Helvetica"
            : EntryFace((te.fmtState.IsBold ? Text.FontStyles.Bold : 0)
                | (te.fmtState.IsItalic ? Text.FontStyles.Italic : 0));
        te.entryLeader = te.fmt?.LineDash
            ?? pl.page.TocInfo?.LineDash ?? Text.TabLeaderType.Dot;
        te.subIndent = (double)(te.fmt?.SubsequentLinesIndent ?? 0);
        te.lineSpacing = te.fmtState is { } fmtTs && fmtTs.LineSpacing != 0
            ? (double)fmtTs.LineSpacing
            : te.segSpacing != 0 ? te.segSpacing : (double)h.TextState.LineSpacing;
        te.lineH = te.entrySize + te.lineSpacing;
        te.descFrac = -Text.Standard14Fonts.GetDescent("Helvetica") / 1000.0;
        // The level format's (or heading's) Margin.Top is leading reserved
        // ABOVE the entry — every entry (the first
        // included) is pushed down by it, so consecutive entries sit lineH + Top apart.
        te.entryY -= te.fmt?.Margin?.Top ?? h.Margin?.Top ?? 0;
        te.entryY -= te.lineH - te.descFrac * te.entrySize;
        te.prefix = string.Empty;
    }
}

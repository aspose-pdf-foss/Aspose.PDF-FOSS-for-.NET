using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The table parser's working set, lifted out of BuildTableFromHtml: each
// method takes the parse state, the column model and the settled dialect
// scalars it reads. Bodies are verbatim.
    private static void CloseSeg(MetricParseState mps, StringBuilder text, bool reportCells, bool stdSerif)
    {
        // Snapshot the typography the segment's first ink saw before clearing it: a
        // plain <div> band inherits the inline <font face>/<span> that dressed it,
        // the way the report-cell <p> path already assigns explicitly. Without it a
        // bordered div cell drew the flow face where the reference draws the inline one.
        var segHadInk = mps.segInkSeen; var segSnapFace = mps.segFace; var segSnapItalic = mps.segItalic;
        var segSnapFs = mps.segFs; var segSnapFore = mps.segFore;
        mps.segBoldChars = 0; mps.segPlainChars = 0; mps.segInkSeen = false;
        mps.segFs = null; mps.segFace = null; mps.segFore = null; mps.segItalic = false;
        if (mps.curSeg is null || mps.cell is null) { mps.curSeg = null; mps.divText.Clear(); return; }
        if (segHadInk)
        {
            mps.curSeg.Face ??= segSnapFace;
            mps.curSeg.FontSize ??= segSnapFs;
            mps.curSeg.Fore ??= segSnapFore;
            mps.curSeg.Italic |= segSnapItalic;
        }
        // Sub-table markers belong to the CELL (CloseCell lifts them into
        // SubTables) — never to a segment's drawn text.
        var segRaw = mps.divText.ToString();
        if (mps.uaBlockCells && segRaw.IndexOf('\u0002') >= 0)
        {
            SplitUaBlockAtNestedGrids(mps, segRaw);
            return;
        }
        if (segRaw.IndexOf('\u0002') >= 0)
        {
            var segMarkers = string.Concat(
                from Match sm in Regex.Matches(segRaw, "\u0002\\d+\u0003")
                select sm.Value);
            segRaw = Regex.Replace(segRaw, "\u0002\\d+\u0003", " ");
            text.Append(segMarkers);
        }
        // A leading <br> is an empty first line box (the boleto's address band draws
        // its "- / - CEP" under one); only a trailing break closes its line.
        mps.curSeg.Text = UaBlockText(mps, segRaw);
        if (mps.uaBlockCells && !segHadInk && mps.curSeg.Text.Length == 0 && segRaw.IndexOf('\u0001') < 0)
            mps.curSeg.EmptyBlock = true;
        if (stdSerif && !mps.uaBlockCells)
        {
            // (a UA cell's block: its bold marks leave the text and stay as runs; a block holding
            // nothing - the frame a nested block reopened - is no band)
            TakeRuns(mps, mps.curSeg);
            if (mps.curSeg.Text.Length == 0 && UaBandIsInert(mps.curSeg)) { mps.curSeg = null; mps.divText.Clear(); return; }
        }
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_CELL") == "1")
            Console.Error.WriteLine($"[seg] fs={mps.curSeg.FontSize} face={mps.curSeg.Face ?? "-"} b={mps.curSeg.Bold} i={mps.curSeg.Italic} ac={mps.curSeg.AlignCenter} ar={mps.curSeg.AlignRight} padL={mps.curSeg.PadLeft:0.##} padR={mps.curSeg.PadRight:0.##} mt={mps.curSeg.MarginTopPt:0.##} mb={mps.curSeg.MarginBottomPt:0.##} runs={mps.curSeg.Runs?.Count ?? 0} '{(mps.curSeg.Text.Length > 30 ? mps.curSeg.Text[..30] : mps.curSeg.Text)}'");
        (mps.cell.DivSegs ??= new List<MetricDivSeg>()).Add(mps.curSeg);
        mps.curSeg = null;
        mps.divText.Clear();
    }

    /// <summary>A pre-wrap cell's source text as cell text: every newline a hard break (a blank
    /// source line an empty line box), a run of two or more spaces kept as non-breaking spaces so
    /// the collapse leaves it standing, tabs as spaces.</summary>
    private static string PreWrapCellText(string raw)
    {
        var t = raw.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\t', ' ');
        t = Regex.Replace(t, " {2,}", m => new string('\u00A0', m.Length));
        return t.Replace('\n', '\u0001');
    }

    /// <summary>A block's drawn text: a UA block keeps the empty lines its EXTRA trailing breaks
    /// make (one closes the line, each further one is a line box - measured on the letter's
    /// `&lt;BR>&lt;BR>` paragraph tails); every other dialect drops the trailing breaks.</summary>
    private static string UaBlockText(MetricParseState mps, string raw)
    {
        var t = CollapseWs(raw).Trim(' ');
        return mps.uaBlockCells && t.EndsWith('\u0001') ? t[..^1].Trim(' ') : t.TrimEnd('\u0001').Trim(' ');
    }

    /// <summary>A UA block whose markup holds nested grids: its text before, between and after
    /// them stays in bands of the block's own typography, and each grid is a band of its own,
    /// all in source order (the letter's item grid sits between two paragraphs).</summary>
    private static void SplitUaBlockAtNestedGrids(MetricParseState mps, string segRaw)
    {
        var proto = mps.curSeg!;
        foreach (var piece in Regex.Split(segRaw, "(\u0002\\d+\u0003)"))
        {
            var gm = Regex.Match(piece, "^\u0002(\\d+)\u0003$");
            if (gm.Success)
            {
                (mps.cell!.DivSegs ??= new List<MetricDivSeg>()).Add(new MetricDivSeg { NestedTable = int.Parse(gm.Groups[1].Value) });
                continue;
            }
            var t = UaBlockText(mps, piece);
            if (t.Length == 0) continue;
            var seg = proto.CloneShallow();
            seg.Text = t;
            (mps.cell!.DivSegs ??= new List<MetricDivSeg>()).Add(seg);
        }
        mps.curSeg = null;
        mps.divText.Clear();
    }

    /// <summary>A UA block cell's grids that stand BETWEEN its blocks (their markers sit in the
    /// cell text, outside any segment) become grid bands where they stand, so the cell's bands
    /// keep the source order; stray text between them becomes a band of the cell's typography.</summary>
    private static void FlushUaBlockGridMarkers(MetricParseState mps, StringBuilder text)
    {
        if (!mps.uaBlockCells || mps.cell is null || text.Length == 0) return;
        var raw = text.ToString();
        if (raw.IndexOf('\u0002') < 0) return;
        foreach (var piece in Regex.Split(raw, "(\u0002\\d+\u0003)"))
        {
            var gm = Regex.Match(piece, "^\u0002(\\d+)\u0003$");
            if (gm.Success)
            {
                (mps.cell.DivSegs ??= new List<MetricDivSeg>()).Add(new MetricDivSeg { NestedTable = int.Parse(gm.Groups[1].Value) });
                continue;
            }
            var t = CollapseWs(piece).Trim(' ');
            if (t.Length == 0 || t.Trim('\u00A0', ' ').Length == 0) continue;
            (mps.cell.DivSegs ??= new List<MetricDivSeg>()).Add(new MetricDivSeg
            {
                Text = t, FontSize = mps.cell.FontSize, Face = mps.cell.Face, Bold = mps.cell.Bold, Fore = mps.cell.Fore,
            });
        }
        text.Clear();
    }

    /// <summary>The text a cell holds after its last block becomes a band of its own (grid markers
    /// under the UA block model, plain text under the UA cell model); the block frames close.</summary>
    private static void FlushCellTailBands(MetricParseState mps, StringBuilder text, bool reportCells, bool stdSerif)
    {
        if (mps.uaBlockCells && mps.cell is { DivSegs: { Count: > 0 } }) FlushUaBlockGridMarkers(mps, text);
        else if (stdSerif && mps.cell is { DivSegs: { Count: > 0 } }) FlushUaCellText(mps, text, reportCells);
        mps.divStyleStack.Clear(); mps.italicDepth = 0;
    }

    /// <summary>The cell as its nested grids see it: its own typography where the cell's text took
    /// its first ink's (see GridTypoSaved), the cell itself otherwise.</summary>
    private static MetricCell GridTypoCell(MetricCell mc)
    {
        if (!mc.GridTypoSaved) return mc;
        var g = mc.CloneShallow();
        g.FontSize = mc.GridFontSize;
        g.Face = mc.GridFace;
        return g;
    }

    private static void CloseCell(MetricParseState mps, StringBuilder text, bool reportCells, bool stdSerif)
    {
        CloseSeg(mps, text, reportCells, stdSerif);
        FlushCellTailBands(mps, text, reportCells, stdSerif);
        // report cells: a p-less cell wholly wrapped in b/strong is bold;
        // mixed-run cells stay in the body face
        if (reportCells && mps.cell is not null && !mps.cell.Bold
            && mps.cellBoldChars > 0 && mps.cellPlainChars == 0)
            mps.cell.Bold = true;
        // …and a UA block grid's p-less cell keeps the typography its first ink saw: the
        // sized span that dressed it has closed and restored the cell by now (measured on the
        // letter's address cells: 9 pt Arial spans in unsized cells).
        if (mps.uaBlockCells && mps.cell is { DivSegs: null } && mps.leadSeen)
        {
            mps.cell.FontSize = mps.leadFs ?? mps.cell.FontSize;
            mps.cell.Face = mps.leadFace ?? mps.cell.Face;
            mps.cell.Fore = mps.leadFore ?? mps.cell.Fore;
        }
        mps.cellBoldChars = 0; mps.cellPlainChars = 0;
        mps.leadSeen = false; mps.leadFs = null; mps.leadFace = null; mps.leadFore = null; mps.leadBold = false;
        if (mps.cell is null) return;
        mps.cell.Text = CollapseWs(mps.cell.PreWrap ? PreWrapCellText(text.ToString()) : text.ToString());
        // (a UA cell's run marks leave its text - the runs stay on the cell - and leave the raw
        // buffer too, which the nested-grid flow reads after this)
        if (stdSerif && !reportCells) { TakeRuns(mps, mps.cell); StripRunMarks(text); }
        // Interleaved cell flow: a nested grid that comes BEFORE text ink
        // keeps its source position — the cell draws text runs (bold per
        // run) and grids in order. Cells whose grids all trail the text
        // keep the calibrated stacked draw (text lines, then grids).
        if (mps.nestedTables is not null && !reportCells)
        {
            if (!InterleaveNestedGrids(mps, text, brAfterGridCounts: stdSerif)) return;
        }
        // Nested-table markers lift out of the text into the cell's grids.
        SplitNestedTableCellSegments(mps, stdSerif);
        // A container cell's whitespace-only paragraph segments (a
        // tellfriend <p> whose img is dead and whose text is one &nbsp;)
        // hold no band — dropping them keeps the nested grids at the top.
        if (reportCells && mps.cell.SubTables is { Count: > 0 }
            && mps.cell.DivSegs is { Count: > 0 })
        {
            mps.cell.DivSegs.RemoveAll(sg =>
            {
                foreach (var ch in sg.Text)
                    if (ch is not (' ' or '\u00A0' or '\u0001')) return false;
                return true;
            });
            if (mps.cell.DivSegs.Count == 0) mps.cell.DivSegs = null;
        }
        // A trailing <br> closes the cell's last line — it opens no new one
        // (mid-cell breaks keep their sentinel).
        mps.cell.Text = mps.cell.Text.TrimEnd('\u0001');
        // …but the two orphaned inlines around a block DO each keep a line box,
        // one on each side of the block's own line, so they are added after that
        // trim. Measured: the cell grows by two boxes and
        // its text still shares the row's baseline, because a middle-aligned
        // neighbour centres in exactly the pair this opens.
        if (mps.cell.OrphanInlineBoxes && mps.cell.Text.Length > 0)
            mps.cell.Text = '\u0001' + mps.cell.Text + '\u0001';
        // A UA cell whose hard lines opened in different <font> sizes draws each line at its own
        // (measured on the valuation report: an 18 px title line over a 13 px subtitle in one
        // centred cell); otherwise the cell keeps the typography its first ink saw, the fonts it
        // opened having closed by now.
        // (a font tag the markup never closes has closed with the cell)
        ApplySerifCellInkFloor(mps, stdSerif);
        mps.fontStack.Clear();
        mps.firstInkSeen = false; mps.firstInkFs = null; mps.firstInkFace = null; mps.firstInkFore = null;
        // Two real sizes met on the cell line: keep the per-size segments
        // so the draw can honour them (a single size stays on the flat path).
        SplitSizedCellSegments(mps);
        mps.sizedSegs.Clear();
        text.Clear();
        mps.cellBoldMarks.Clear();
        mps.runStyles.Clear(); mps.pendingRunPadLeft = 0; mps.textMarkCount = 0;
        // HTML grid placement: a row under a row-spanning cell starts its cells AFTER
        // the columns that cell still occupies. Each covered slot is a phantom, so the
        // row stays column-true for the column model and both painters (measured on the
        // kit-lot table: the rows under a rowspan=2 pair drew one column early).
        var col = 0;
        foreach (var placed in mps.row!) col += Math.Max(1, placed.ColSpan);
        while (RowSpanOccupies(mps, col))
        {
            mps.row.Add(new MetricCell { Text = "", Phantom = true, RowSpanCovered = true });
            col++;
        }
        if (mps.cell.RowSpan > 1)
            mps.rowspanOcc.Add((col, Math.Max(1, mps.cell.ColSpan), mps.cell.RowSpan));
        mps.row.Add(mps.cell);
        mps.cell = null;
    }

    /// <summary>The cell's text as one band per hard line when at least two lines opened in different
    /// font sizes: each line in the size its first ink saw and the bold at that point, a blank line an
    /// nbsp in the size current at its break. False (nothing built) when the lines share one size.</summary>
    private static bool BuildSizedLineSegments(MetricParseState mps)
    {
        if (mps.sizedSegs.Count < 2 || mps.cell is not { } cell) return false;
        var lines = new List<(string Text, double? Fs, bool Ink, int Pos)>();
        var cur = new StringBuilder();
        double? curFs = null;
        var curInk = false;
        var pos = 0;
        var inkAt = 0;
        foreach (var (sb, fs) in mps.sizedSegs)
        {
            foreach (var ch in sb.ToString())
            {
                if (ch == MetricHardBreakChar)
                {
                    lines.Add((cur.ToString(), curFs ?? fs, curInk, inkAt));
                    cur.Clear(); curFs = null; curInk = false; inkAt = pos + 1;
                }
                else
                {
                    if (!curInk && !char.IsWhiteSpace(ch) && ch != '\u00A0') { curInk = true; curFs = fs; inkAt = pos; }
                    cur.Append(ch);
                }
                pos++;
            }
            curFs ??= fs;
        }
        lines.Add((cur.ToString(), curFs, curInk, inkAt));
        var sizes = new HashSet<double>();
        foreach (var l in lines) if (l.Ink) sizes.Add(l.Fs ?? 0);
        if (sizes.Count < 2) return false;
        while (lines.Count > 0 && !lines[^1].Ink) lines.RemoveAt(lines.Count - 1);
        var segs = new List<MetricDivSeg>();
        foreach (var (raw, fs, ink, at) in lines)
            segs.Add(new MetricDivSeg
            {
                Text = ink ? CollapseWs(raw).Trim(' ') : "\u00A0",
                FontSize = fs,
                Face = mps.firstInkFace ?? cell.Face,
                Fore = mps.firstInkFore ?? cell.Fore,
                Bold = MetricBoldAt(mps, at),
            });
        cell.DivSegs = segs;
        return true;
    }

    /// <summary>Whether the cell's bold runs are on at a text position.</summary>
    private static bool MetricBoldAt(MetricParseState mps, int at)
    {
        var on = false;
        foreach (var (mp, mo) in mps.cellBoldMarks)
        {
            if (mp > at) break;
            on = mo;
        }
        return on;
    }

    /// <summary>Whether a row-spanning cell from a row above still occupies this column.</summary>
    private static bool RowSpanOccupies(MetricParseState mps, int col)
    {
        foreach (var (oc, os, orem) in mps.rowspanOcc)
            if (orem > 0 && col >= oc && col < oc + os) return true;
        return false;
    }

    private static void CloseRow(MetricParseState mps, List<List<MetricCell>> rows, StringBuilder text, bool reportCells, bool stdSerif)
    {
        CloseCell(mps, text, reportCells, stdSerif);
        // (a UA block grid keeps a row that opened and closed with no cell: it spends one
        // border-spacing of its own - measured: two empty rows stand the total row 3 pt lower)
        if ((mps.uaBlockCells || mps.uaFormCells) && mps.row is { Count: 0 } && rows.Count > 0)
        {
            rows.Add(mps.row);
            mps.rowHeights.Add(0); mps.rowHeightExact.Add(false); mps.rowHeightAttr.Add(false);
            mps.rowSections.Add(mps.curSection);
            mps.row = null;
            return;
        }
        if (mps.row is { Count: > 0 })
        {
            rows.Add(mps.row);
            // row-span occupancy ages one row per REAL row close (the redundant boundary
            // calls explicit </tr> markup produces must not expire it early)
            for (var oi = mps.rowspanOcc.Count - 1; oi >= 0; oi--)
            {
                var (oc, os, orem) = mps.rowspanOcc[oi];
                if (orem <= 1) mps.rowspanOcc.RemoveAt(oi);
                else mps.rowspanOcc[oi] = (oc, os, orem - 1);
            }
            mps.rowHeights.Add(mps.pendingRowH);
            mps.rowHeightExact.Add(mps.pendingRowHExact);
            mps.rowHeightAttr.Add(mps.pendingRowHAttr);
            mps.rowSections.Add(mps.curSection);
        }
        mps.row = null;
        mps.pendingRowH = 0;
        mps.pendingRowHExact = false;
        mps.pendingRowHAttr = false;
        mps.rowFs = null;
        mps.rowAlign = null;
        mps.rowBg = null;
        mps.rowFace = null;
        mps.rowFsFromClass = false;
        mps.rowInlineTypo = false;
        mps.rowBold = false;
        mps.rowFore = null;
        mps.rowVTop = false;
        mps.rowVBottom = false;
        mps.rowTdBags = null;
        mps.rowClasses = null;
    }

    /// <summary>Applies one matched class rule's declarations to a cell: font and colour
    /// properties always, box properties (alignment, width, padding, borders) only for a
    /// rule matched on the cell itself.</summary>
    /// <param name="mps">The table parse state (supplies the inherited font size and grid mode).</param>
    /// <param name="css">The document's class rules; not read by this method.</param>
    /// <param name="text">The cell's text buffer; not read by this method.</param>
    /// <param name="reportCells">The report-cell mode flag; not read by this method.</param>
    /// <param name="stdSerif">True when the table draws in the standard serif face.</param>
    /// <param name="mc">The cell that receives the properties.</param>
    /// <param name="bag">The rule's property/value declarations.</param>
    /// <param name="inlineBag">The rule was matched on an INLINE element inside the cell, so
    /// only the properties an inline passes to its text apply. An inline box states no
    /// alignment or geometry for the block it sits in: a `text-align: right` class on a span
    /// leaves its cell left-aligned (measured on the reference: the checkbox cells' bold
    /// `.label` spans draw from the cell's content edge, not from its right edge).</param>
    private static void ApplyCellClassBag(MetricParseState mps, IReadOnlyDictionary<string, Dictionary<string, string>> css, StringBuilder text, bool reportCells, bool stdSerif, MetricCell mc, IReadOnlyDictionary<string, string> bag, bool inlineBag = false)
    {
        foreach (var (bProp, bVal) in bag)
            switch (bProp.ToLowerInvariant())
            {
                // (an em resolves against the element's PARENT - MEASURED, quirks table font reset: the
                //  cell's own size for a span's bag (`.note { font-size: 1em }` in a 9 pt cell is 9),
                //  the UA 12 pt the table element resets to for a cell rule (`TD { .75em }` is 9))
                case "font-size" when stdSerif && _quirksRowStrut
                    && Regex.Match(bVal.Trim(), @"^(\d+(?:\.\d+)?|\.\d+)\s*em$", RegexOptions.IgnoreCase) is { Success: true } bagEm:
                    mc.FontSize = double.Parse(bagEm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
                        * (inlineBag ? (mc.FontSize ?? mps.fontSize) : UaDefaultFontPt);
                    mc.FontFromClass = true;
                    break;
                case "font": case "font-size": case "font-family": case "font-weight": case "color": case "background-color": case "background":
                    ApplyCellFontClassRule(mps, stdSerif, mc, bVal, bProp.ToLowerInvariant());
                    break;
                case "text-align": case "vertical-align": case "width": case "padding-top": case "padding-left": case "padding-right": case "padding-bottom": case "height": case "border-left": case "border-right": case "border-bottom": case "border-top":
                    if (!inlineBag) ApplyCellBoxClassRule(mps, mc, bProp, bVal, bProp.ToLowerInvariant());
                    break;
                // (a class `min-width` floors the cell's content box - the change-control grid's `.th-md { min-width: 7.5em }`)
                case "min-width":
                    if (!inlineBag && mps.sheetSpacingZero && TryParseLength(bVal.Trim()) is { } mwPt && mwPt > 0)
                        mc.MinWidthPt = Math.Max(mc.MinWidthPt, bVal.Trim().EndsWith("em", StringComparison.OrdinalIgnoreCase)
                            ? DtpNum(bVal.Trim()[..^2]) * (mc.FontSize ?? mps.fontSize) : mwPt);
                    break;
                // (…and a `border-left-width: 0` under it takes that side's rule away)
                case "border-left-width":
                    if (inlineBag || !mps.sheetSpacingZero) break;
                    if (IsZeroLength(bVal.Trim())) mc.BorderLeftW = 0;
                    else if (TryParseLength(bVal.Trim()) is { } blwPt && blwPt > 0) mc.BorderLeftW = blwPt;
                    break;
                // (the shorthands state every side at once in a COLLAPSED grid: `.tp01 td { padding: 5px;
                //  border: 1px solid #e4e4e4 }` pads the e-mail cards' cells and rules each of them; the
                //  calibrated separate-border grids keep reading the longhands alone)
                case "padding":
                    // (…and in the pt form grid: `.Form-table-title { padding: 2px }` pads its band 1.5 a side)
                    if (inlineBag || !(mps.collapsedGrid || mps.sheetSpacingZero || mps.ptFormCells)) break;
                    {
                        var parts = bVal.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length is >= 1 and <= 4)
                        {
                            var top = parts[0];
                            var right = parts.Length >= 2 ? parts[1] : parts[0];
                            var bottom = parts.Length >= 3 ? parts[2] : parts[0];
                            var left = parts.Length == 4 ? parts[3] : right;
                            ApplyCellBoxClassRule(mps, mc, "padding-top", top, "padding-top");
                            ApplyCellBoxClassRule(mps, mc, "padding-right", right, "padding-right");
                            ApplyCellBoxClassRule(mps, mc, "padding-bottom", bottom, "padding-bottom");
                            ApplyCellBoxClassRule(mps, mc, "padding-left", left, "padding-left");
                        }
                    }
                    break;
                case "border":
                    if (inlineBag || !(mps.collapsedGrid || mps.sheetSpacingZero)) break;
                    foreach (var side in new[] { "border-top", "border-right", "border-bottom", "border-left" })
                        ApplyCellBoxClassRule(mps, mc, side, bVal, side);
                    break;
                case "white-space":
                    // (`pre-wrap` keeps its breaks and spaces and still wraps; a class nowrap keeps the
                    // calibrated wrap - the keyword must not match by prefix)
                    if (Regex.IsMatch(bVal, @"^\s*pre-wrap", RegexOptions.IgnoreCase)) mc.PreWrap = true;
                    break;
            }
    }

    /// <summary>The class rules that dress the cell's text: its font shorthand, size, family, weight, colour and background.</summary>
    private static void ApplyCellFontClassRule(MetricParseState mps, bool stdSerif, MetricCell mc, string bVal, string prop)
    {
        switch (prop)
        {
            case "font":
            {
                // (the shorthand's size is any CSS length - probed: `font: 8pt "Courier New"` cells
                // draw Courier New 8, where a px-only read left them in the flow serif at 12)
                var fsh = Regex.Match(bVal, @"([\d.]+\s*(?:px|pt|em|rem|%))\s+(.+)$", RegexOptions.IgnoreCase);
                if (fsh.Success && TryParseCssFontSize(fsh.Groups[1].Value.Trim()) is { } fshPt && fshPt > 0)
                {
                    mc.FontSize = fshPt;
                    // a QUOTED family inside the shorthand is dropped with
                    // the shorthand's grammar (the sheet parser does the
                    // same) — the cell keeps the flow face
                    var shRaw = fsh.Groups[2].Value.TrimStart();
                    // ...unless the flow can draw the quoted face: the boleto's
                    // `font: 9px "arial narrow"` labels are a sans in the expected render
                    if (shRaw.Length > 0
                        && FirstFontFamily(shRaw) is { Length: > 0 } shFam
                        && WinMetricsFor(shFam) is not null)
                        mc.Face = shFam;
                }
                if (bVal.Contains("bold", StringComparison.OrdinalIgnoreCase))
                    mc.Bold = true;
                if (mc.FontSize is not null) mc.FontFromClass = true;
                break;
            }
            case "font-size":
                // a PERCENT size resolves against the cell's current size
                // (th { font-size: 80% } = 9.6 on the 12 pt base; the
                // .firm span's 400% = 48)
                if (bVal.Trim().EndsWith("%", StringComparison.Ordinal)
                    && double.TryParse(bVal.Trim().TrimEnd('%'),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var bagPct) && bagPct > 0)
                { mc.FontSize = (mc.FontSize ?? mps.fontSize) * bagPct / 100.0; mc.FontFromClass = true; }
                else if (TryParseCssFontSize(bVal.Trim()) is { } bagFs)
                { mc.FontSize = bagFs; mc.FontFromClass = true; }
                break;
            case "font-family":
                // Under the UA flow only the faces the HTML engine
                // resolves apply — 'Century Gothic' falls to the flow
                // serif.
                if (FirstFontFamily(bVal) is { Length: > 0 } bagFam
                    && WinMetricsFor(bagFam) is not null
                    && (!stdSerif || SourceEngineFaces.Contains(bagFam)))
                    mc.Face = bagFam;
                break;
            case "font-weight":
                if (bVal.Contains("bold", StringComparison.OrdinalIgnoreCase)
                    || (int.TryParse(bVal.Trim(), out var bagFw) && bagFw >= 600))
                    mc.Bold = true;
                break;
            case "color":
                if (ParseCssColor(bVal.Trim()) is { } bagFc
                    && (bagFc.R != 0 || bagFc.G != 0 || bagFc.B != 0)) mc.Fore = bagFc;
                break;
            case "background-color":
            case "background":
                if (ParseCssColor(bVal.Trim()) is { } bagBg) mc.Bg = bagBg;
                break;
        }
    }

    /// <summary>The class rules that shape the cell's box: alignment, width, padding, height and its side borders.</summary>
    private static void ApplyCellBoxClassRule(MetricParseState mps, MetricCell mc, string bProp, string bVal, string prop)
    {
        ApplyCellBoxClassProperty(mps, mc, prop, bProp, bVal);
    }
}

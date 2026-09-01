using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
    private void SortLinesByY(int textStartOffset, int yStartIndex)
    {
        // Record the last line's Y position
        RecordLineY();

        var sl = new SortLinesState();
        sl.textStartOffset = textStartOffset;
        sl.yStartIndex = yStartIndex;
        sl.yCount = _lineYPositions.Count - sl.yStartIndex;
        if (sl.yCount < 1) return;

        sl.pageText = _text.ToString(sl.textStartOffset, _text.Length - sl.textStartOffset);
        sl.lines = sl.pageText.Split('\n');

        sl.rtlMultiSpan = HasRtlMultiSpanLine(sl.lines, sl.textStartOffset);
        if (sl.yCount < 2 && !sl.rtlMultiSpan) return;

        sl.singleSpaceGlyphLine = new bool[sl.lines.Length];
        sl.lineEdgeX = new double[sl.lines.Length];
        {
            var off = sl.textStartOffset;
            for (int i = 0; i < sl.lines.Length; i++)
            {
                int lo = off, hi = off + sl.lines[i].Length, spans = 0, len = 0;
                double endX = double.NaN, glyphX = double.NaN;
                foreach (var s in _pageRunSpans)
                {
                    if (s.Offset < lo || s.Offset + s.Len > hi) continue;
                    spans++; len += s.Len; glyphX = s.X;
                    var w = !double.IsNaN(s.Width) && s.Width > 0 ? s.Width : s.Len * Math.Max(_pageCellWidth, 1.0);
                    if (double.IsNaN(endX) || s.X + w > endX) endX = s.X + w;
                }
                var blank = string.IsNullOrWhiteSpace(sl.lines[i]);
                sl.singleSpaceGlyphLine[i] = blank && spans == 1 && len == 1;
                sl.lineEdgeX[i] = blank ? glyphX : endX;
                if (GridDebug)
                    Console.Error.WriteLine($"[edge] i={i} blank={blank} lineLen={sl.lines[i].TrimEnd('\r').Length} spans={spans} spanLen={len} edgeX={sl.lineEdgeX[i]:F1} single={sl.singleSpaceGlyphLine[i]}");
                off += sl.lines[i].Length + 1;
            }
        }
        sl.adjacencyTol = Math.Max(1.5 * _pageCellWidth, 2.0);
        sl.pageYs = new List<double>();
        sl.pageXs = new List<double>();
        sl.pageFs = new List<double>();
        sl.pageRot = new List<bool>();
        sl.pageDesc = new List<double>();
        CollectLineMetrics(sl);
        // A search-RECTANGLE window re-anchors as a mini page: the extractor
        // emits its kept lines strictly top-to-bottom, however the
        // stream visited them (probed: a scrambled ladder inside a rect comes
        // back sorted; the same ladder full-page keeps stream order under the
        // 200 pt rule above). Any up-jump between kept lines forces the sort.
        // UPRIGHT pages only: a sideways page's tracked line positions live in
        // the projection frame, where an "up-jump" is a same-row column revisit
        // (a rotated form keeps its stream row structure).
        if (!sl.needsSort
            && !_pageRotDominant && !_pageHasRotatedText
            && ExtractionOptions?.FormattingMode != TextExtractionOptions.TextFormattingMode.Raw
            && (_effectiveSearchRect ?? TextSearchOptions?.Rectangle) is not null)
        {
            for (int i = 1; i < sl.pageYs.Count; i++)
            {
                if (!double.IsNaN(sl.pageYs[i]) && !double.IsNaN(sl.pageYs[i - 1])
                    && sl.pageYs[i] > sl.pageYs[i - 1] + 2.0)
                {
                    sl.needsSort = true;
                    break;
                }
            }
        }

        if (GridDebug)
        {
            Console.Error.WriteLine($"[sortpre] lines={sl.lines.Length} yCount={sl.yCount} needsSort={sl.needsSort}");
            for (int i = 0; i < Math.Min(sl.lines.Length, 70); i++)
                Console.Error.WriteLine($"[sortpre] y={sl.pageYs[i]:F2} '{sl.lines[i][..Math.Min(28, sl.lines[i].Length)]}'");
        }

        sl.blankFs = ExtractionOptions?.FormattingMode != TextExtractionOptions.TextFormattingMode.Raw
                      && _pageCellWidth > 0
            ? (_pageDominantFs > 0 ? _pageDominantFs : _pageCellWidth / 0.6 + 2)
            : 0;
        sl.rawMode = ExtractionOptions?.FormattingMode == TextExtractionOptions.TextFormattingMode.Raw;
        if (sl.rawMode) return;

        sl.hasSameYLines = false;
        if (!EmitLinesInOrder(sl)) return;

        IndexLinesForSort(sl);

        AssignRowGroups(sl);
        _text.Remove(sl.textStartOffset, _text.Length - sl.textStartOffset);
        sl.gStart2 = 0;
        sl.firstGroup = true;
        sl.prevGroupY = double.NaN;
        while (sl.gStart2 < sl.indexed.Count)
        {
            if (!EmitNextRowGroup(sl)) break;
        }
    }

    /// <summary>Takes the next group of same-row lines off the sorted index and emits it as one output row; false once the index is spent.</summary>
    private bool EmitNextRowGroup(SortLinesState sl)
    {
        int gEnd2 = sl.gStart2 + 1;
        var anchor = sl.indexed[sl.gStart2];
        while (gEnd2 < sl.indexed.Count && sl.groupOf[gEnd2] == sl.groupOf[sl.gStart2])
            gEnd2++;

        // Same-row segments read left-to-right: order the group by page X
        // (unknown X keeps its Y-sort position, after known Xs).
        var group = sl.indexed.GetRange(sl.gStart2, gEnd2 - sl.gStart2);
        if (group.Count > 1)
            group = group.OrderBy(t => double.IsNaN(AnchorX(sl, t.idx)) ? double.MaxValue : AnchorX(sl, t.idx)).ToList();

        // The row's BOTTOM is the lowest member's descent line; blank rows
        // between consecutive rows gate on the bottom-to-bottom gap with
        // F = the font size of the member that defines the arriving row's
        // bottom (a wrapped note whose row also carries a smaller side
        // note reaches less far down per F unit, opening a blank its
        // uniform twin doesn't).
        var rowBottom = double.NaN;
        var rowBottomFs = double.NaN;
        foreach (var t in group)
        {
            var db0 = DescLineY(sl, t.y, t.idx);
            if (double.IsNaN(db0)) continue;
            if (double.IsNaN(rowBottom) || db0 < rowBottom)
            {
                rowBottom = db0;
                rowBottomFs = sl.pageFs[t.idx];
            }
        }
        if (!sl.firstGroup)
        {
            _text.Append("\r\n");
            // Blank rows for the vertical gap between consecutive groups.
            var blanks = BlankRowsFor(sl, sl.prevGroupY, rowBottom, rowBottomFs);
            for (var b = 0; b < blanks; b++) _text.Append("\r\n");
        }
        sl.firstGroup = false;
        if (!double.IsNaN(rowBottom)) sl.prevGroupY = rowBottom;
        if (GridDebug)
        {
            var rowDbg = string.Join("|", group.Select(t =>
            {
                var s0 = t.line.Trim();
                return s0.Length > 16 ? s0[..16] : s0;
            }));
            Console.Error.WriteLine($"[row] y={anchor.y:F2} fs={sl.pageFs[anchor.idx]:F2} n={group.Count} '{rowDbg}'");
        }

        var rowStartLen = _text.Length;
        int firstPad = 0;
        var firstLine = group[0].line;
        while (firstPad < firstLine.Length && firstLine[firstPad] == ' ') firstPad++;
        // Character width for X->column mapping within this row: prefer the
        // page grid cell; else the cell rule from the anchor font.
        var anchorFs2 = sl.pageFs[group[0].idx];
        var rowCw = _pageCellWidth > 0
            ? _pageCellWidth
            : (!double.IsNaN(anchorFs2) && anchorFs2 > 0 ? Math.Max(1.0, 0.6 * (Math.Round(anchorFs2, MidpointRounding.AwayFromZero) - 2.0)) : 0.0);
        for (int gi = 0; gi < group.Count; gi++)
        {
            if (!EmitRowLine(sl, group, gi, rowCw, rowStartLen, firstPad)) break;
        }
        sl.gStart2 = gEnd2;
        return true;
    }

    /// <summary>Merges lines that share a baseline into rows and orders the rows top to bottom.</summary>
    private void GroupRowsByBaseline(SortLinesState sl)
    {
        if (!sl.rawMode)
        {
            // Line start offsets in _text (the same cumulative walk that mapped
            // lineStartXs) so each line's run spans can be located.
            var lineOffs = new int[sl.lines.Length];
            {
                var off = sl.textStartOffset;
                for (int i = 0; i < sl.lines.Length; i++) { lineOffs[i] = off; off += sl.lines[i].Length + 1; }
            }
            for (int gs = 0; gs < sl.indexed.Count;)
            {
                int ge = gs + 1;
                while (ge < sl.indexed.Count && !double.IsNaN(sl.indexed[ge].y) && !double.IsNaN(sl.indexed[ge - 1].y)
                       && Math.Abs(sl.indexed[ge].y - sl.indexed[ge - 1].y) < SameRowTolPair(sl, sl.indexed[ge - 1].y, sl.pageFs[sl.indexed[ge - 1].idx], sl.pageFs[sl.indexed[ge].idx]))
                    ge++;
                // Multi-piece rows always qualify; a single line qualifies when it
                // carries 2+ tracked runs (stream order within one emitted line can
                // interleave overlapping runs just as badly as split pieces).
                var singleLineSpans = 0;
                if (ge - gs == 1)
                {
                    int lo1 = lineOffs[sl.indexed[gs].idx], hi1 = lo1 + sl.lines[sl.indexed[gs].idx].Length;
                    foreach (var s in _pageRunSpans)
                        if (s.Offset >= lo1 && s.Offset + s.Len <= hi1) singleLineSpans++;
                }
                if (ge - gs > 1 || singleLineSpans > 1)
                {
                    int rtl = 0, ltr = 0;
                    for (int k = gs; k < ge; k++)
                        foreach (var ch in sl.indexed[k].line)
                        {
                            if (BidiReorderer.IsRtlChar(ch)) rtl++;
                            else if (char.IsLetter(ch)) ltr++;
                        }
                    if (rtl > ltr && rtl > 0)
                    {
                        MergeRtlRow(sl, gs, ref ge, lineOffs);
                    }
                    else if (_pageCellWidth > 0 && !double.IsNaN(_pageMinX) && !_pageRotDominant && ge - gs > 1)
                    {
                        MergeInterleavedRow(sl, gs, ref ge, lineOffs);
                    }
                }
                gs = ge;
            }
        }
    }

    /// <summary>Lines already in reading order go out as they are, blank rows kept; false when nothing is left to sort.</summary>
    private bool EmitLinesInOrder(SortLinesState sl)
    {
        if (!sl.needsSort)
        {
            for (int i = 1; i < sl.pageYs.Count; i++)
            {
                // A whitespace-only line is NOT skipped here: a space glyph shown in
                // its own text object at the row's baseline (a trailing space, the
                // gap of "French :" drawn as three shows) is a same-row segment the
                // row formation below must absorb — only a y-less line is spacing.
                if (double.IsNaN(sl.pageYs[i]) || double.IsNaN(sl.pageYs[i - 1])) continue;
                if (string.IsNullOrWhiteSpace(sl.lines[i]) || string.IsNullOrWhiteSpace(sl.lines[i - 1]))
                {
                    if (string.IsNullOrWhiteSpace(sl.lines[i]) && !sl.singleSpaceGlyphLine[i]) continue;
                    if (string.IsNullOrWhiteSpace(sl.lines[i - 1]) && !sl.singleSpaceGlyphLine[i - 1]) continue;
                    // ...and the glyph must seat right after the text (see the walk).
                    {
                        var bi = string.IsNullOrWhiteSpace(sl.lines[i]) ? i : i - 1;
                        var ti = bi == i ? i - 1 : i;
                        if (string.IsNullOrWhiteSpace(sl.lines[ti])) continue;   // two blank lines
                        if (!EdgeAdjacent(sl, bi, ti)) continue;
                    }
                    // A whitespace-only line is a same-row segment only on the
                    // neighbour's own baseline (see the row walk below).
                    var tolFs = sl.pageFs[i]; if (double.IsNaN(tolFs) || tolFs <= 0) tolFs = 10.0;
                    if (Math.Abs(sl.pageYs[i] - sl.pageYs[i - 1]) > SameBaselineTol * tolFs) continue;
                }
                // Line-box compatibility (the same test the row formation
                // below uses): 1-em boxes on the descender line overlapping
                // by at least half the smaller font.
                var dfA = sl.pageFs[i - 1]; if (double.IsNaN(dfA) || dfA <= 0) dfA = 10.0;
                var dfB = sl.pageFs[i]; if (double.IsNaN(dfB) || dfB <= 0) dfB = 10.0;
                var dbA = sl.pageYs[i - 1] - sl.pageDesc[i - 1] * dfA; var dbB = sl.pageYs[i] - sl.pageDesc[i] * dfB;
                if (Math.Min(dbA + dfA, dbB + dfB) - Math.Max(dbA, dbB)
                    >= 0.5 * Math.Min(dfA, dfB) - 1e-9)
                {
                    sl.hasSameYLines = true;
                    break;
                }
            }
            if (!sl.hasSameYLines && !sl.rtlMultiSpan)
            {
                // No reorder/merge needed — still insert the blank rows in place
                // (from the end backwards so earlier offsets stay valid).
                if (sl.blankFs > 0)
                {
                    var offs = new int[sl.lines.Length];
                    var off = sl.textStartOffset;
                    for (int i = 0; i < sl.lines.Length; i++) { offs[i] = off; off += sl.lines[i].Length + 1; }
                    for (int i = sl.lines.Length - 1; i >= 1; i--)
                    {
                        var b = BlankRowsFor(sl, DescLineY(sl, sl.pageYs[i - 1], i - 1), DescLineY(sl, sl.pageYs[i], i), sl.pageFs[i]);
                        if (b > 0 && offs[i] <= _text.Length)
                            _text.Insert(offs[i], string.Concat(Enumerable.Repeat("\r\n", b)));
                    }
                }
                return false;
            }
        }
        return true;
    }

    /// <summary>Pairs every text line with its recorded y, x and size, and reads the page's row pitch from them.</summary>
    private void CollectLineMetrics(SortLinesState sl)
    {
        for (int i = sl.yStartIndex; i < _lineYPositions.Count && sl.pageYs.Count < sl.lines.Length; i++)
        {
            sl.pageYs.Add(_lineYPositions[i]);
            sl.pageXs.Add(i < _lineXPositions.Count ? _lineXPositions[i] : double.NaN);
            sl.pageFs.Add(i < _lineFontSizes.Count ? _lineFontSizes[i] : double.NaN);
            sl.pageRot.Add(i < _lineIsRotated.Count && _lineIsRotated[i]);
            sl.pageDesc.Add(i < _lineDescents.Count ? _lineDescents[i] : 0.2);
        }
        while (sl.pageYs.Count < sl.lines.Length)
        {
            sl.pageYs.Add(double.NaN);
            sl.pageXs.Add(double.NaN);
            sl.pageFs.Add(double.NaN);
            sl.pageRot.Add(false);
            sl.pageDesc.Add(0.2);
        }

        sl.firstKnown = sl.pageYs.FirstOrDefault(y => !double.IsNaN(y), double.NaN);
        for (int i = 0; i < sl.pageYs.Count; i++)
        {
            if (double.IsNaN(sl.pageYs[i]))
                sl.pageYs[i] = i > 0 ? sl.pageYs[i - 1] : sl.firstKnown;
        }

        sl.needsSort = false;
        sl.rawModePre = ExtractionOptions?.FormattingMode == TextExtractionOptions.TextFormattingMode.Raw;
        for (int i = 1; i < sl.pageYs.Count; i++)
        {
            if (!double.IsNaN(sl.pageYs[i]) && !double.IsNaN(sl.pageYs[i - 1]) &&
                sl.pageYs[i] > sl.pageYs[i - 1] + 200.0) // Y jumped UP by >~3 inches — major out-of-order block
            {
                // Raw mode keeps the stream order verbatim, and a big up-jump that
                // lands well to the RIGHT is a COLUMN SWITCH in ordinary reading
                // order (two-column papers) — sorting would interleave the columns
                // row-by-row. Only a jump back up with no rightward shift marks a
                // genuinely out-of-order block (e.g. flattened fields appended
                // after the page text).
                if (sl.rawModePre && !double.IsNaN(sl.pageXs[i]) && !double.IsNaN(sl.pageXs[i - 1])
                    && sl.pageXs[i] > sl.pageXs[i - 1] + 50.0)
                    continue;
                sl.needsSort = true;
                break;
            }
        }
    }

    /// <summary>Walks the sorted lines bottom-up and numbers the rows they form, blank rows breaking a group.</summary>
    private void AssignRowGroups(SortLinesState sl)
    {
        sl.groupOf = new int[sl.indexed.Count];
        {
            var gid = 0;
            var members = new List<(double bot, double top, double fs, double y, int idx, bool blank)>();
            // Font size of the nearest text line strictly BELOW the one being
            // placed (bottom-up walk): an UNDERLINE run (only '_' and spaces)
            // has its box HEIGHT clamped to it — a fs-12 rule drawn over a
            // fs-9 table reads as a 9pt-tall band, so it underlines the row
            // below instead of capturing the header above (the
            // clamp never grows a box).
            var fsBelow = 0.0;
            for (var ii = sl.indexed.Count - 1; ii >= 0; ii--)
            {
                var (yy, idx0, ln) = sl.indexed[ii];
                var blank0 = string.IsNullOrWhiteSpace(ln);
                var ffs = sl.pageFs[idx0];
                if (double.IsNaN(ffs) || ffs <= 0) ffs = 10.0;
                var hEff = ffs;
                if (!blank0 && fsBelow > 0 && fsBelow < ffs && !sl.pageRot[idx0])
                {
                    var underline = true;
                    foreach (var ch0 in ln)
                        if (ch0 != '_' && ch0 != ' ' && ch0 != (char)13) { underline = false; break; }
                    if (underline) hEff = fsBelow;
                }
                // A minority-rotated line's box points UP from its anchor —
                // [y, y+fs] — instead of sitting on the upright descent line
                // (sideways title-block runs join rows through
                // the em above their baseline, never through a descender).
                var rot0 = sl.pageRot[idx0];
                var bot0 = rot0 ? yy : yy - sl.pageDesc[idx0] * ffs;
                var top0 = bot0 + (rot0 ? ffs : hEff);
                // A whitespace-only line is vertical spacing — UNLESS it is a space
                // glyph drawn in its own text object on the row being formed (a
                // trailing space, or the gap between "French" and ":" emitted as
                // three shows at one baseline): that is a same-row segment, and
                // breaking the group there would split the row around it.
                var joins = !sl.rawMode && !double.IsNaN(yy) && members.Count > 0;
                if (joins)
                    foreach (var m in members)
                        if (Math.Min(top0, m.top) - Math.Max(bot0, m.bot)
                            < 0.5 * Math.Min(hEff, m.fs) - 1e-9
                            // A whitespace-only line joins only a row on ITS OWN
                            // baseline (a space glyph shown as its own object beside
                            // the text); on a merely overlapping baseline it is a
                            // pad row — vertical spacing that closes the row.
                            || (blank0 && Math.Abs(yy - m.y) > SameBaselineTol * ffs))
                        { joins = false; break; }
                if (blank0 && !sl.singleSpaceGlyphLine[idx0]) joins = false;
                // ...and only when the glyph seats right after a member's text
                // (its grid column is the member's end column ±1): a single space
                // drawn columns away — a justified line's padding, a pad row — is
                // spacing, which stays as its own row.
                // (The walk is bottom-up and, on one baseline, later stream index
                // first — so the glyph may be visited before or after its text.)
                if (joins)
                {
                    var anyText = false;
                    foreach (var m in members) if (!m.blank) { anyText = true; break; }
                    if (blank0 || !anyText)
                    {
                        var adjacent = false;
                        foreach (var m in members)
                            if (m.blank != blank0 && (blank0 ? EdgeAdjacent(sl, idx0, m.idx) : EdgeAdjacent(sl, m.idx, idx0))) { adjacent = true; break; }
                        if (!adjacent) joins = false;
                    }
                }
                // A lone space glyph that joins nothing still STARTS a group (the
                // text on its baseline may be visited after it); emitted alone it
                // is the pad row it always was. Wider blanks close the row.
                var blankSpacing = blank0 && !sl.singleSpaceGlyphLine[idx0];
                if (!joins && !(members.Count == 0)) { gid++; members.Clear(); }
                sl.groupOf[ii] = gid;
                if (double.IsNaN(yy) || blankSpacing) { gid++; members.Clear(); }
                else { members.Add((bot0, top0, hEff, yy, idx0, blank0)); if (!blank0) fsBelow = ffs; }
            }
        }
    }

    /// <summary>Indexes every line by its recorded y and sorts the index top to bottom before the rows are merged.</summary>
    private void IndexLinesForSort(SortLinesState sl)
    {
        sl.lineStartXs = new double[sl.lines.Length];
        for (int i = 0; i < sl.lines.Length; i++) sl.lineStartXs[i] = double.NaN;
        {
            var startByOffset = new Dictionary<int, double>();
            foreach (var (o, x) in _pageLineStarts)
                startByOffset[o] = x;
            var off = sl.textStartOffset;
            for (int i = 0; i < sl.lines.Length; i++)
            {
                if (startByOffset.TryGetValue(off, out var sx)) sl.lineStartXs[i] = sx;
                off += sl.lines[i].Length + 1; // + '\n' the split consumed
            }
        }

        sl.indexed = new List<(double y, int idx, string line)>();
        for (int i = 0; i < sl.lines.Length; i++)
            sl.indexed.Add((sl.pageYs[i], i, sl.lines[i].TrimEnd('\r')));

        if (GridDebug)
            for (int i = 0; i < sl.indexed.Count; i++)
            {
                var t0 = sl.indexed[i].line.Trim();
                if (t0.Length > 30) t0 = t0[..30];
                Console.Error.WriteLine($"[sort] y={sl.indexed[i].y:F2} x={sl.lineStartXs[i]:F1} fs={sl.pageFs[i]:F2} desc={sl.pageDesc[i]:F3} rowX={sl.pageXs[i]:F1} '{t0}'");
            }

        // Stable sort by Y descending; lines with NaN Y keep their relative order
        sl.indexed.Sort((a, b) =>
        {
            if (double.IsNaN(a.y) && double.IsNaN(b.y)) return a.idx.CompareTo(b.idx);
            if (double.IsNaN(a.y)) return 1; // NaN goes last
            if (double.IsNaN(b.y)) return -1;
            var cmp = b.y.CompareTo(a.y); // descending Y = top first
            return cmp != 0 ? cmp : a.idx.CompareTo(b.idx); // preserve order for same Y
        });

        // RTL rows (Hebrew/Arabic): rebuild the row from geometry — the
        // row's show-runs explode to per-CHARACTER X positions (a run that was
        // reversed to logical order at decode time maps back right-to-left), the
        // characters merge in ascending X into the true VISUAL row, and one
        // Unicode-bidi pass (visual → logical) orders the result. A piece-level
        // join interleaves words whenever runs overlap in X (single letters
        // painted inside another run's span), so the merge must be per character.
        // The leftmost piece's leading grid pad moves to the LOGICAL END.
        GroupRowsByBaseline(sl);
    }

    /// <summary>Two LTR lines of one row that overlap in x are interleaved on the page's cell grid into one line.</summary>
    private void MergeInterleavedRow(SortLinesState sl, int gs, ref int ge, int[] lineOffs)
    {
        // LTR INTERLEAVING: when two LINES of this row overlap in
        // device X — a glyph of one falls inside the other's span
        // (e.g. a separately-drawn digit dropped into the gap of a
        // wide-tracked note) — JUST THOSE lines are re-laid
        // on the character grid: each word's first glyph at
        // its device column (chained, +1/3 bias), interior
        // contiguous. Non-overlapping lines on the same row (a
        // neighbouring cell to the left) keep their own drawn
        // spacing, so a lone run stays tight.
        // Each LINE of this row: does it carry runs that interleave
        // in device X (a glyph of one run inside another's span)?
        // Grid-place only those lines; a lone-run line stays tight.
        for (int k = gs; k < ge; k++)
        {
            if (!InterleaveRowLine(sl, k, gs, ge, lineOffs)) break;
        }
    }

    /// <summary>An RTL row's member lines are exploded to glyph runs and re-read right to left as one line.</summary>
    private void MergeRtlRow(SortLinesState sl, int gs, ref int ge, int[] lineOffs)
    {
        // Explode every member line's runs to (x, char, advance).
        var cells = new List<(double x, double adv, char c)>();
        double tailPadX = double.MaxValue;
        var tailPad = 0;
        for (int k = gs; k < ge; k++)
        {
            if (!ExplodeRtlLineRuns(sl, k, lineOffs, cells, ref tailPad, ref tailPadX)) break;
        }
        // Merge ascending X = the true visual row. One output space per
        // DISTINCT space glyph (co-located stacked duplicates collapse);
        // voids synthesize a space between letters/digits or beside an
        // explicit space cell (a real space glyph vouches for its gap).
        var ordered = cells.OrderBy(t => t.x).ToList();
        var vsb = new StringBuilder();
        double advSum = 0;
        var advCnt = 0;
        foreach (var t in ordered)
            if (t.c != ' ') { advSum += t.adv; advCnt++; }
        var meanAdv = advCnt > 0 ? advSum / advCnt : 6.0;
        double lastSpaceX = double.NegativeInfinity;
        for (int ci = 0; ci < ordered.Count; ci++)
        {
            var (x, adv, c) = ordered[ci];
            if (c == ' ')
            {
                if (vsb.Length == 0) continue;
                // Only NEAR-IDENTICAL positions are duplicates (stacked
                // copies of the same glyph); side-by-side space glyphs and
                // a resurrected gap space beside an explicit one all count.
                if (vsb[^1] == ' '
                    && x - lastSpaceX < Math.Max(0.25 * (adv > 0 ? adv : meanAdv), 0.3))
                    continue;
                vsb.Append(' ');
                lastSpaceX = x;
                continue;
            }
            vsb.Append(c);
            // Synthesize a word space for a geometric void with no space glyph:
            // between letters/digits (the per-run uniform advance overestimates
            // narrow punctuation — geresh, comma — and a false gap there would
            // split a word "פרג'ון"), OR beside an EXPLICIT space cell — a void
            // that abuts a real space glyph is a genuine widening
            // ("ופיתוח  –  מדעי" pads to two on each dash side).
            if (ci + 1 < ordered.Count)
            {
                var nxt = ordered[ci + 1];
                // A degenerate advance (a cum-width table whose tail reads ~0)
                // would fake a void off this glyph's true right edge.
                var effAdv = adv < 0.15 * meanAdv ? 0.6 * meanAdv : adv;
                var voidW = nxt.x - (x + effAdv);
                var letterPair = nxt.c != ' '
                    && char.IsLetterOrDigit(c) && char.IsLetterOrDigit(nxt.c);
                var spaceSide = nxt.c == ' ';
                if ((letterPair && voidW > 0.5 * meanAdv)
                    || (spaceSide && voidW > 0.45 * meanAdv))
                    vsb.Append(' ');
            }
        }
        if (GridDebug)
        {
            Console.Error.WriteLine($"[rtl] y={sl.indexed[gs].y:F2} members={ge - gs} cells={ordered.Count} visual='{vsb}'");
            foreach (var t in ordered)
                Console.Error.WriteLine($"[rtl]   x={t.x:F2} adv={t.adv:F2} '{t.c}'");
        }
        // Visual → logical via the UBA pass; the grid pad trails logically.
        // A masked line-end glyph space (see EolShowSpaceSentinel) trims
        // with the plain spaces here — the RTL rebuild's pad handling
        // owns the row's edge whitespace, exactly as before masking.
        var logical = BidiReorderer.ReorderIfNeeded(vsb.ToString().Trim(' ', EolShowSpaceSentinel));
        if (tailPad > 0 && tailPad <= 200) logical += new string(' ', tailPad);
        sl.indexed[gs] = (sl.indexed[gs].y, sl.indexed[gs].idx, logical);
        sl.lineStartXs[sl.indexed[gs].idx] = double.NaN;
        sl.indexed.RemoveRange(gs + 1, ge - gs - 1);
        ge = gs + 1;
    }

    /// <summary>Explodes one member line of an RTL row into positioned glyph runs, tracking the row's trailing pad.</summary>
    private bool ExplodeRtlLineRuns(SortLinesState sl, int k, int[] lineOffs, List<(double x, double adv, char c)> cells, ref int tailPad, ref double tailPadX)
    {
        int idx = sl.indexed[k].idx;
        var lineText = sl.indexed[k].line;
        var lx = sl.lineStartXs[idx];
        var lead = 0;
        while (lead < lineText.Length && lineText[lead] == ' ') lead++;
        if (lead < lineText.Length && !double.IsNaN(lx) && lx < tailPadX)
        {
            tailPadX = lx;   // leftmost non-blank member: its lead pad trails
            tailPad = lead;
        }
        int lo = lineOffs[idx], hi = lo + sl.lines[idx].Length;
        var fallbackAdv = _pageCellWidth > 0 ? _pageCellWidth : 6.0;
        var lineSpans = new List<RunSpan>();
        foreach (var s in _pageRunSpans)
            if (s.Offset >= lo && s.Offset + s.Len <= hi)
                lineSpans.Add(s);
        var sawSpan = lineSpans.Count > 0;
        // A span whose MEASURED width overshoots into the next span's
        // territory (bad font metrics can double an advance) would
        // interleave foreign characters mid-word in the X-merge. Clamp
        // each span's extent to the next NON-BLANK span's start and
        // rescale its per-character positions into the clamped width.
        var clampW = new Dictionary<int, double>(); // span Offset → effective width
        AppendSpanRuns(sl, lineSpans, cells, idx, lo, clampW, fallbackAdv);
        if (!sawSpan)
        {
            // Untracked line (e.g. ActualText): one left-to-right pseudo-run.
            var body = lineText.Trim(' ');
            var x0 = double.IsNaN(lx) ? 0 : lx + lead * fallbackAdv;
            for (int ci = 0; ci < body.Length; ci++)
                cells.Add((x0 + fallbackAdv * ci, fallbackAdv, body[ci]));
        }
        else
        {
            // Synthesized gap spaces live in the line TEXT between run
            // spans but have no geometry of their own — resurrect them as
            // cells interpolated across the inter-span void so the visual
            // merge keeps the document's full space count. Only across a
            // REAL void (wider than ~a third of a grid cell): a squeezed
            // word gap already covered by its explicit space glyph stays
            // single (a sub-glyph gap), a glyph-sized void widens (a wider void).
            lineSpans.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            for (int si = 0; si + 1 < lineSpans.Count; si++)
            {
                var a = lineSpans[si];
                var b = lineSpans[si + 1];
                int from = a.Offset + a.Len, to = b.Offset;
                if (to <= from) continue;
                var n = 0;
                for (int t = from; t < to; t++)
                    if (sl.lines[idx][t - lo] == ' ') n++;
                if (n == 0) continue;
                var aW = clampW.TryGetValue(a.Offset, out var acw) ? acw : a.Width;
                var aEnd = a.X + (aW > 0 && !double.IsNaN(aW) ? aW : 0);
                var gapW = b.X - aEnd;
                if (gapW <= 0.35 * fallbackAdv) continue; // thin/backward: no real void
                var step = gapW / (n + 1);
                for (int t = 1; t <= n; t++)
                    cells.Add((aEnd + step * t, step, ' '));
            }
        }
        return true;
    }

    /// <summary>Folds one member line of an interleaved row into the row's cell grid.</summary>
    private bool InterleaveRowLine(SortLinesState sl, int k, int gs, int ge, int[] lineOffs)
    {
        int idx = sl.indexed[k].idx, off = lineOffs[idx];
        var lineSpans = new List<RunSpan>();
        foreach (var s in _pageRunSpans)
            if (s.Offset >= off && s.Offset + s.Len <= off + sl.lines[idx].Length
                && s.Width > 0 && !double.IsNaN(s.Width) && !double.IsNaN(s.X))
                lineSpans.Add(s);
        // Only a genuine few-run interleave (a note plus a
        // dropped-in glyph) qualifies — a line carrying many
        // runs is a dense block (per-glyph runs, stacked cells)
        // that must NOT be re-laid by device X, which would
        // scramble it into single letters.
        if (lineSpans.Count < 2 || lineSpans.Count > 6) return true;
        var interleave = false;
        for (int a = 0; a < lineSpans.Count && !interleave; a++)
            for (int b = a + 1; b < lineSpans.Count; b++)
                if (Math.Min(lineSpans[a].X + lineSpans[a].Width, lineSpans[b].X + lineSpans[b].Width)
                    - Math.Max(lineSpans[a].X, lineSpans[b].X) > 0.5 * _pageCellWidth)
                { interleave = true; break; }
        if (!interleave) return true;
        // Split every run into WORDS (a run's own spaces are its
        // word breaks — NOT another overlapping run's spaces), each
        // word carrying its device X and its literal grid column
        // (run grid-start + char offset). Merge the words by device
        // X, then place the FIRST word at its literal column (so a
        // run's leading spaces are preserved) and CHAIN the rest by
        // device gap (+1/3 bias); interior contiguous.
        var cw = _pageCellWidth;
        var words = new List<(double x, int litCol, string text, int run)>();
        var trimC = LineStartGridCol(_pageMinX, _pageMinX);
        for (int ri = 0; ri < lineSpans.Count; ri++)
        {
            var s = lineSpans[ri];
            var runGrid = LineStartGridCol(s.X, _pageMinX) - trimC;
            var adv = s.Width / Math.Max(1, s.Len);
            int ci = 0;
            while (ci < s.Len)
            {
                var c0 = sl.lines[idx][s.Offset - off + ci];
                if (c0 == ' ' || c0 == '\r' || c0 == '\n') { ci++; continue; }
                int wStart = ci;
                var wsb = new StringBuilder();
                while (ci < s.Len)
                {
                    var c1 = sl.lines[idx][s.Offset - off + ci];
                    if (c1 == ' ') break;
                    if (c1 != '\r' && c1 != '\n') wsb.Append(c1);
                    ci++;
                }
                var wx = s.CharXs is not null ? s.X + s.CharXs[wStart] : s.X + adv * wStart;
                words.Add((wx, runGrid + wStart, wsb.ToString(), ri));
            }
        }
        words.Sort((a, b) => a.x.CompareTo(b.x));
        var sb = new StringBuilder();
        var prevCol = int.MinValue; var prevX = 0.0; var prevRun = -1; var prevLen = 0;
        var interleaved = false;
        foreach (var (wx, litCol, wtext, run) in words)
        {
            int col;
            if (prevCol == int.MinValue)
                col = litCol; // first word: literal position (run pad + leading spaces)
            else if (interleaved)
                col = prevCol + prevLen + 1; // past a foreign word: butt-join, tight
            else if (run == prevRun)
                col = prevCol + (int)Math.Floor((wx - prevX) / cw + 1.0 / 3.0); // same run: chain
            else
            {
                // a word from a DIFFERENT run drops in (the interleaving
                // glyph): chain to it, then everything after butt-joins.
                col = prevCol + (int)Math.Floor((wx - prevX) / cw + 1.0 / 3.0) + 1;
                interleaved = true;
            }
            if (col < sb.Length + 1 && sb.Length > 0) col = sb.Length + 1;
            if (col < 0) col = 0;
            while (sb.Length < col) sb.Append(' ');
            sb.Append(wtext);
            prevCol = col; prevX = wx; prevRun = run; prevLen = wtext.Length;
        }
        if (GridDebug)
            Console.Error.WriteLine($"[ltrx] y={sl.indexed[k].y:F2} runs={lineSpans.Count} words={words.Count} out='{(sb.Length > 60 ? sb.ToString(0, 60) : sb.ToString())}'");
        sl.indexed[k] = (sl.indexed[k].y, sl.indexed[k].idx, sb.ToString());
        // The rebuilt line already carries its full absolute
        // leading pad, so it must emit FIRST and let a
        // non-interleaving neighbour (a tight run to its left)
        // overlay into that pad — force it to the front of the
        // row's X-order.
        sl.pageXs[sl.indexed[k].idx] = -1e9;
        sl.lineStartXs[sl.indexed[k].idx] = -1e9;
        return true;
    }

    /// <summary>Appends one member line of the row at its column, padding to the cell grid.</summary>
    private bool EmitRowLine(SortLinesState sl, List<(double y, int idx, string line)> group, int gi, double rowCw, int rowStartLen, int firstPad)
    {
        if (gi == 0)
        {
            // The row's leftmost segment keeps its own leading grid pad.
            _text.Append(group[gi].line);
        }
        else
        {
            // A merged continuation is padded to its own grid column —
            // the same absolute floor(x/cell) frame the leading-pad
            // insertion uses, so a segment keeps one column whether it
            // was emitted on its own line or merged into a row. Only
            // when the grid frame is unavailable does the legacy
            // relative estimate (with its 6-space separator floor)
            // apply.
            var line = group[gi].line;
            var xa2 = AnchorX(sl, group[0].idx);
            var xb2 = AnchorX(sl, group[gi].idx);
            var gridTarget = rowCw > 0 && !double.IsNaN(_pageMinX) && !double.IsNaN(xb2);
            var target = 0;
            // A grid-rebuilt interleave line (sentinel X) already carries its
            // full absolute leading pad, so it overlays from column 0.
            if (gridTarget && xb2 <= -1e8)
                target = 0;
            else if (gridTarget)
                target = (xb2 < 0 ? -1 : GridColOf(xb2, _pageGridOriginX, rowCw))
                    - (_pageMinX < 0 ? -1 : GridColOf(_pageMinX, _pageGridOriginX, rowCw));
            else if (rowCw > 0 && !double.IsNaN(xa2) && !double.IsNaN(xb2))
                target = firstPad + (int)Math.Round((xb2 - xa2) / rowCw);
            var curCol = _text.Length - rowStartLen;
            if (GridDebug)
                Console.Error.WriteLine($"[merge] target={target} grid={gridTarget} cur={curCol} xa={xa2:R} xb={xb2:R} rowCw={rowCw:R} firstPad={firstPad} body='{(line.TrimStart().Length > 20 ? line.TrimStart().Substring(0, 20) : line.TrimStart())}'");
            // Strip only the line's inserted grid pad; DRAWN leading
            // space glyphs stay with the segment (an indented note
            // keeps its indent from its line-start column when it
            // merges into a row).
            string body;
            if (BuildRowSegment(sl, group, gi, line, gridTarget, target, curCol, rowStartLen, out body, out var prevSegBlank)) return true;
            if (body.Length == 0) return true;
            var bodyIsSpaceGlyph = IsSpaceGlyphSegment(body);
            var sepFloor = prevSegBlank || bodyIsSpaceGlyph ? 0 : gridTarget ? 1 : 6;
            var spaces = Math.Min(5000, Math.Max(sepFloor, target - curCol));
            // A space glyph the row must PAD to reach is layout padding
            // (a field's trailing blank drawn columns away), not the
            // word space that seats right after the text: it loses the
            // line-end protection so the trailing trim removes it with
            // its pad ("John Smith", not "John Smith      ").
            if (bodyIsSpaceGlyph && spaces > 0) body = body.Replace(EolShowSpaceSentinel, ' ');
            // The adjacent single space glyph IS the row's word space: it
            // survives the trailing trim ("experience ") via the line-end
            // sentinel, exactly like a space glyph ending a show.
            else if (bodyIsSpaceGlyph && body.Length == 1) body = EolShowSpaceSentinel.ToString();
            _text.Append(' ', spaces);
            _text.Append(body);
        }
        return true;
    }

    /// <summary>The text a row segment contributes: trimmed to its grid column, or overlapping text dropped.</summary>
    private bool BuildRowSegment(SortLinesState sl, List<(double y, int idx, string line)> group, int gi, string line, bool gridTarget, int target, int curCol, int rowStartLen, out string body, out bool prevSegBlank)
    {
        if (gridTarget)
        {
            var strip = 0;
            while (strip < line.Length && line[strip] == ' ' && strip < target) strip++;
            body = line.Substring(strip);
        }
        else
            body = line.TrimStart(' ');
        // Every segment of a row lands in one
        // row-wide character grid, so a segment whose column sits
        // INSIDE already-emitted text (the stream drew a later
        // column first) still lands at its own column. Overlay the
        // member char-wise — its own pad spaces pass over emitted
        // text, its glyphs land on blanks; any glyph collision
        // falls back to the append path.
        // A column inside the text a WHITESPACE-ONLY segment just
        // emitted is not a back-drawn column: that space is a drawn
        // glyph ("French" + " " + ":" as three shows), and the
        // segment reads after it — append, never overlay it.
        prevSegBlank = IsSpaceGlyphSegment(group[gi - 1].line);
        // (A space-glyph body never overlays either — the overlay skips
        // spaces, which would drop the glyph; it appends as the word space.)
        if (gridTarget && target < curCol && rowStartLen + target >= 0 && !prevSegBlank && !IsSpaceGlyphSegment(body))
        {
            var abs = rowStartLen + target;
            var ok = true;
            for (var bi = 0; bi < body.Length && ok; bi++)
            {
                if (body[bi] == ' ') continue;
                var pos = abs + bi;
                if (pos >= 0 && pos < _text.Length && _text[pos] != ' ') ok = false;
            }
            if (ok)
            {
                while (_text.Length < abs + body.Length) _text.Append(' ');
                for (var bi = 0; bi < body.Length; bi++)
                    if (body[bi] != ' ') _text[abs + bi] = body[bi];
                return true;
            }
        }
        // Column clamp mirrors the 5000-column grid bound
        // (oversize engineering sheets pad into the hundreds of columns).
        // No separator floor around a drawn space glyph segment: the
        // glyph IS the word space ("French" + " " + ":" reads
        // "French :"), so only the grid pad applies.
        // A segment whose whole content was grid pad / trimmed blanks
        // has nothing to seat — appending a separator for it would
        // leave a dangling run of spaces ("8/30/18      ").
        return false;
    }

    /// <summary>Clamps each span's width to its neighbours and adds the line's glyphs to the row's cells at their measured x.</summary>
    private void AppendSpanRuns(SortLinesState sl, List<RunSpan> lineSpans, List<(double x, double adv, char c)> cells, int idx, int lo, Dictionary<int, double> clampW, double fallbackAdv)
    {
        {
            var byX = new List<RunSpan>(lineSpans);
            byX.Sort((a2, b2) => a2.X.CompareTo(b2.X));
            for (int si = 0; si < byX.Count; si++)
            {
                var s2 = byX[si];
                if (!(s2.Width > 0) || double.IsNaN(s2.Width)) continue;
                var w2 = s2.Width;
                for (int sj = si + 1; sj < byX.Count; sj++)
                {
                    var nb = byX[sj];
                    var nbText = nb.Offset - lo >= 0 && nb.Offset - lo + nb.Len <= sl.lines[idx].Length
                        ? sl.lines[idx].Substring(nb.Offset - lo, nb.Len) : "";
                    if (string.IsNullOrWhiteSpace(nbText)) continue;
                    var room = nb.X - s2.X;
                    if (room > 1 && room < w2) w2 = room;
                    break;
                }
                if (w2 < s2.Width) clampW[s2.Offset] = w2;
            }
        }
        foreach (var s in lineSpans)
        {
            var effW = clampW.TryGetValue(s.Offset, out var cw) ? cw : s.Width;
            var scale = s.Width > 0 && !double.IsNaN(s.Width) && effW < s.Width
                ? effW / s.Width : 1.0;
            var adv = effW > 0 && !double.IsNaN(effW)
                ? effW / s.Len
                : fallbackAdv;
            for (int ci = 0; ci < s.Len; ci++)
            {
                var c = sl.lines[idx][s.Offset - lo + ci];
                if (c == '\r' || c == '\n') continue;
                // A reversed run's decoded char ci came from code
                // (visual) position Len-1-ci.
                var vi = s.Reversed ? s.Len - 1 - ci : ci;
                double x, cadv;
                if (s.CharXs is not null)
                {
                    x = s.X + s.CharXs[vi] * scale;
                    cadv = ((vi + 1 < s.Len ? s.CharXs[vi + 1] : s.Width) - s.CharXs[vi]) * scale;
                    if (cadv <= 0) cadv = adv;
                }
                else
                {
                    x = s.X + adv * vi;
                    cadv = adv;
                }
                cells.Add((x, cadv, c));
            }
        }
    }
}

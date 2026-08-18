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

        var yCount = _lineYPositions.Count - yStartIndex;
        if (yCount < 2) return;

        // Extract only the page's text
        var pageText = _text.ToString(textStartOffset, _text.Length - textStartOffset);
        var lines = pageText.Split('\n');

        // Build Y/X/font-size positions for this page's lines
        var pageYs = new List<double>();
        var pageXs = new List<double>();
        var pageFs = new List<double>();
        var pageRot = new List<bool>();
        var pageDesc = new List<double>();
        for (int i = yStartIndex; i < _lineYPositions.Count && pageYs.Count < lines.Length; i++)
        {
            pageYs.Add(_lineYPositions[i]);
            pageXs.Add(i < _lineXPositions.Count ? _lineXPositions[i] : double.NaN);
            pageFs.Add(i < _lineFontSizes.Count ? _lineFontSizes[i] : double.NaN);
            pageRot.Add(i < _lineIsRotated.Count && _lineIsRotated[i]);
            pageDesc.Add(i < _lineDescents.Count ? _lineDescents[i] : 0.2);
        }
        while (pageYs.Count < lines.Length)
        {
            pageYs.Add(double.NaN);
            pageXs.Add(double.NaN);
            pageFs.Add(double.NaN);
            pageRot.Add(false);
            pageDesc.Add(0.2);
        }

        // A line whose Y could not be tracked belongs WITH its neighbours, not at
        // the page end (where NaN would sort it). Forward-fill from the previous
        // line; leading unknowns take the first known Y.
        var firstKnown = pageYs.FirstOrDefault(y => !double.IsNaN(y), double.NaN);
        for (int i = 0; i < pageYs.Count; i++)
        {
            if (double.IsNaN(pageYs[i]))
                pageYs[i] = i > 0 ? pageYs[i - 1] : firstKnown;
        }

        // Check if lines are already in visual order (Y descending = top to bottom).
        bool needsSort = false;
        var rawModePre = ExtractionOptions?.FormattingMode == TextExtractionOptions.TextFormattingMode.Raw;
        for (int i = 1; i < pageYs.Count; i++)
        {
            if (!double.IsNaN(pageYs[i]) && !double.IsNaN(pageYs[i - 1]) &&
                pageYs[i] > pageYs[i - 1] + 200.0) // Y jumped UP by >~3 inches — major out-of-order block
            {
                // Raw mode keeps the stream order verbatim, and a big up-jump that
                // lands well to the RIGHT is a COLUMN SWITCH in ordinary reading
                // order (two-column papers) — sorting would interleave the columns
                // row-by-row. Only a jump back up with no rightward shift marks a
                // genuinely out-of-order block (e.g. flattened fields appended
                // after the page text).
                if (rawModePre && !double.IsNaN(pageXs[i]) && !double.IsNaN(pageXs[i - 1])
                    && pageXs[i] > pageXs[i - 1] + 50.0)
                    continue;
                needsSort = true;
                break;
            }
        }

        if (GridDebug)
        {
            Console.Error.WriteLine($"[sortpre] lines={lines.Length} yCount={yCount} needsSort={needsSort}");
            for (int i = 0; i < Math.Min(lines.Length, 70); i++)
                Console.Error.WriteLine($"[sortpre] y={pageYs[i]:F2} '{lines[i][..Math.Min(28, lines[i].Length)]}'");
        }

        // Pure-mode blank rows (the LineSplitter thresholds): a new
        // segment whose bottom clears the current line's top by more than one
        // line-height opens one empty line, by more than three line-heights a
        // second — i.e. baseline gap > 2·F → 1 blank, > 4·F → 2 (cap). F = the
        // PREVIOUS line's own font size (the "current line" whose top the new
        // segment clears); the page-dominant size only backstops untracked
        // lines. An 8pt address block with 15.1pt leading stays blank-free
        // (2·8 > 15.1) even when the page's dominant text is 7.5pt.
        var blankFs = ExtractionOptions?.FormattingMode != TextExtractionOptions.TextFormattingMode.Raw
                      && _pageCellWidth > 0
            ? (_pageDominantFs > 0 ? _pageDominantFs : _pageCellWidth / 0.6 + 2)
            : 0;
        // Blank-row gaps measure between DESCENT lines (box bottoms), not raw
        // baselines: a deep-descent big-font row reaches further down, closing
        // the gap to the next row (uniform-size rows are unaffected — the
        // descent term cancels).
        // Rotated-DOMINANT pages keep raw baselines: their per-line effective
        // sizes live in the projection frame and can't scale a descent there.
        double DescLineY(double y, int idx) =>
            double.IsNaN(y) || pageRot[idx] || _pageRotDominant ? y
            : y - pageDesc[idx] * (double.IsNaN(pageFs[idx]) || pageFs[idx] <= 0 ? 10.0 : pageFs[idx]);
        int BlankRowsFor(double prevY, double curY, double curFs = double.NaN)
        {
            if (blankFs <= 0 || double.IsNaN(prevY) || double.IsNaN(curY)) return 0;
            // The line-height that gates a blank is the ARRIVING line's own
            // font size (a 21pt heading 21.5pt below a 10pt line opens no
            // blank; a 6pt fine-print row 28.8pt below a 7.5pt line opens
            // two). Rotated pages keep the page-dominant RAW size (their
            // per-line effective sizes live in the projection frame and
            // aren't comparable to baseline gaps).
            var f = !_pageRotDominant && !double.IsNaN(curFs) && curFs > 0 ? curFs : blankFs;
            var gap = prevY - curY;
            var r = gap <= 2 * f ? 0 : gap > 4 * f ? 2 : 1;
            if (GridDebug && r > 0)
                Console.Error.WriteLine($"[blank] prevY={prevY:F2} curY={curY:F2} f={f:F2} curFs={curFs:F2} -> {r}");
            return r;
        }

        // Raw mode keeps the source stream order verbatim: no same-row column
        // merging (a wrapped table row would interleave its cells' lines) and no
        // blank-row synthesis. The big-jump sort above still applies.
        var rawMode = ExtractionOptions?.FormattingMode == TextExtractionOptions.TextFormattingMode.Raw;
        if (rawMode && !needsSort) return;

        // An IN-ORDER page keeps its stream line structure: only near-identical
        // baselines merge (co-row segments the stream happened to break). The full
        // font-relative row tolerance applies only to the geometric re-sort of an
        // OUT-OF-ORDER block (e.g. flattened field values appended after the page
        // text), where reading-order rows must be reassembled from scratch.
        double SameRowTol(double y, double fs) => needsSort
            ? RowMergeTol(y, fs)
            : Math.Min(InOrderRowTol, RowMergeTol(y, fs));
        // Row tolerance for a PAIR of lines: the larger font of the two anchors
        // the reach (a 12pt heading merges an 8pt annotation 2.7pt below it).
        double SameRowTolPair(double y, double fsA, double fsB)
        {
            // Sideways pages take the pair-max reach (a 12pt heading merging an
            // 8pt annotation 2.7pt below). Upright pages keep the anchor-font
            // rule the corpus is calibrated on — EXCEPT for strongly mixed-size
            // pairs (label/value rows: a 6pt caption above its 10pt value,
            // baselines ~4pt apart): the line bands reach by the
            // larger font, so a size ratio ≥ 1.5 takes the full band reach of
            // the larger font (past the in-order cap, which is for equal-size
            // staircases).
            if (!_pageHasRotatedText)
            {
                var lo = Math.Min(fsA, fsB); var hi = Math.Max(fsA, fsB);
                if (!(lo > 0) || double.IsNaN(lo) || hi < 1.5 * lo)
                    return SameRowTol(y, fsA);
                // Strongly mixed-size pairs (a 6pt label 3.97pt from its 10pt
                // value) band-merge with the larger font's half-height reach.
                // NOTE: the true band model is LINE BOXES
                // (segBottom/middle vs line top); baseline
                // distance + size ratio cannot reproduce it for mid-ratio
                // pairs — implementing the line-box model is the remaining work.
                return 0.5 * hi;
            }
            var fs = double.IsNaN(fsA) ? fsB : double.IsNaN(fsB) ? fsA : Math.Max(fsA, fsB);
            return SameRowTol(y, fs);
        }

        // Even if sort isn't needed, check if same-Y lines need merging
        bool hasSameYLines = false;
        if (!needsSort)
        {
            for (int i = 1; i < pageYs.Count; i++)
            {
                // Blank lines are vertical spacing, never same-row segments.
                if (string.IsNullOrWhiteSpace(lines[i]) || string.IsNullOrWhiteSpace(lines[i - 1]))
                    continue;
                if (double.IsNaN(pageYs[i]) || double.IsNaN(pageYs[i - 1])) continue;
                // Line-box compatibility (the same test the row formation
                // below uses): 1-em boxes on the descender line overlapping
                // by at least half the smaller font.
                var dfA = pageFs[i - 1]; if (double.IsNaN(dfA) || dfA <= 0) dfA = 10.0;
                var dfB = pageFs[i]; if (double.IsNaN(dfB) || dfB <= 0) dfB = 10.0;
                var dbA = pageYs[i - 1] - pageDesc[i - 1] * dfA; var dbB = pageYs[i] - pageDesc[i] * dfB;
                if (Math.Min(dbA + dfA, dbB + dfB) - Math.Max(dbA, dbB)
                    >= 0.5 * Math.Min(dfA, dfB) - 1e-9)
                {
                    hasSameYLines = true;
                    break;
                }
            }
            if (!hasSameYLines)
            {
                // No reorder/merge needed — still insert the blank rows in place
                // (from the end backwards so earlier offsets stay valid).
                if (blankFs > 0)
                {
                    var offs = new int[lines.Length];
                    var off = textStartOffset;
                    for (int i = 0; i < lines.Length; i++) { offs[i] = off; off += lines[i].Length + 1; }
                    for (int i = lines.Length - 1; i >= 1; i--)
                    {
                        var b = BlankRowsFor(DescLineY(pageYs[i - 1], i - 1), DescLineY(pageYs[i], i), pageFs[i]);
                        if (b > 0 && offs[i] <= _text.Length)
                            _text.Insert(offs[i], string.Concat(Enumerable.Repeat("\r\n", b)));
                    }
                }
                return;
            }
        }

        // Map each line back to its tracked start X (reading-axis page coordinate) so
        // the same-row merge below can pad to the right part's grid column instead of
        // a fixed separator. Lines without a tracked run keep NaN.
        var lineStartXs = new double[lines.Length];
        for (int i = 0; i < lines.Length; i++) lineStartXs[i] = double.NaN;
        {
            var startByOffset = new Dictionary<int, double>();
            foreach (var (o, x) in _pageLineStarts)
                startByOffset[o] = x;
            var off = textStartOffset;
            for (int i = 0; i < lines.Length; i++)
            {
                if (startByOffset.TryGetValue(off, out var sx)) lineStartXs[i] = sx;
                off += lines[i].Length + 1; // + '\n' the split consumed
            }
        }

        // Create (y, index, line) tuples and sort by Y descending (top of page first).
        // Lines were split on '\n' but were separated upstream by "\r\n", so each carries
        // a trailing '\r'; strip it so re-joining doesn't produce a doubled "\r\r\n" between
        // lines or a stray '\r' before a same-row column separator ("…large\r      companies").
        var indexed = new List<(double y, int idx, string line)>();
        for (int i = 0; i < lines.Length; i++)
            indexed.Add((pageYs[i], i, lines[i].TrimEnd('\r')));

        if (GridDebug)
            for (int i = 0; i < indexed.Count; i++)
            {
                var t0 = indexed[i].line.Trim();
                if (t0.Length > 30) t0 = t0[..30];
                Console.Error.WriteLine($"[sort] y={indexed[i].y:F2} x={lineStartXs[i]:F1} fs={pageFs[i]:F2} desc={pageDesc[i]:F3} rowX={pageXs[i]:F1} '{t0}'");
            }

        // Stable sort by Y descending; lines with NaN Y keep their relative order
        indexed.Sort((a, b) =>
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
        if (!rawMode)
        {
            // Line start offsets in _text (the same cumulative walk that mapped
            // lineStartXs) so each line's run spans can be located.
            var lineOffs = new int[lines.Length];
            {
                var off = textStartOffset;
                for (int i = 0; i < lines.Length; i++) { lineOffs[i] = off; off += lines[i].Length + 1; }
            }
            for (int gs = 0; gs < indexed.Count;)
            {
                int ge = gs + 1;
                while (ge < indexed.Count && !double.IsNaN(indexed[ge].y) && !double.IsNaN(indexed[ge - 1].y)
                       && Math.Abs(indexed[ge].y - indexed[ge - 1].y) < SameRowTolPair(indexed[ge - 1].y, pageFs[indexed[ge - 1].idx], pageFs[indexed[ge].idx]))
                    ge++;
                // Multi-piece rows always qualify; a single line qualifies when it
                // carries 2+ tracked runs (stream order within one emitted line can
                // interleave overlapping runs just as badly as split pieces).
                var singleLineSpans = 0;
                if (ge - gs == 1)
                {
                    int lo1 = lineOffs[indexed[gs].idx], hi1 = lo1 + lines[indexed[gs].idx].Length;
                    foreach (var s in _pageRunSpans)
                        if (s.Offset >= lo1 && s.Offset + s.Len <= hi1) singleLineSpans++;
                }
                if (ge - gs > 1 || singleLineSpans > 1)
                {
                    int rtl = 0, ltr = 0;
                    for (int k = gs; k < ge; k++)
                        foreach (var ch in indexed[k].line)
                        {
                            if (BidiReorderer.IsRtlChar(ch)) rtl++;
                            else if (char.IsLetter(ch)) ltr++;
                        }
                    if (rtl > ltr && rtl > 0)
                    {
                        // Explode every member line's runs to (x, char, advance).
                        var cells = new List<(double x, double adv, char c)>();
                        double tailPadX = double.MaxValue;
                        var tailPad = 0;
                        for (int k = gs; k < ge; k++)
                        {
                            int idx = indexed[k].idx;
                            var lineText = indexed[k].line;
                            var lx = lineStartXs[idx];
                            var lead = 0;
                            while (lead < lineText.Length && lineText[lead] == ' ') lead++;
                            if (lead < lineText.Length && !double.IsNaN(lx) && lx < tailPadX)
                            {
                                tailPadX = lx;   // leftmost non-blank member: its lead pad trails
                                tailPad = lead;
                            }
                            int lo = lineOffs[idx], hi = lo + lines[idx].Length;
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
                                        var nbText = nb.Offset - lo >= 0 && nb.Offset - lo + nb.Len <= lines[idx].Length
                                            ? lines[idx].Substring(nb.Offset - lo, nb.Len) : "";
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
                                    var c = lines[idx][s.Offset - lo + ci];
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
                                // single ("יפו 64100"), a glyph-sized void widens ("תוכנה  ופיתוח").
                                lineSpans.Sort((a, b) => a.Offset.CompareTo(b.Offset));
                                for (int si = 0; si + 1 < lineSpans.Count; si++)
                                {
                                    var a = lineSpans[si];
                                    var b = lineSpans[si + 1];
                                    int from = a.Offset + a.Len, to = b.Offset;
                                    if (to <= from) continue;
                                    var n = 0;
                                    for (int t = from; t < to; t++)
                                        if (lines[idx][t - lo] == ' ') n++;
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
                            Console.Error.WriteLine($"[rtl] y={indexed[gs].y:F2} members={ge - gs} cells={ordered.Count} visual='{vsb}'");
                            foreach (var t in ordered)
                                Console.Error.WriteLine($"[rtl]   x={t.x:F2} adv={t.adv:F2} '{t.c}'");
                        }
                        // Visual → logical via the UBA pass; the grid pad trails logically.
                        // A masked line-end glyph space (see EolShowSpaceSentinel) trims
                        // with the plain spaces here — the RTL rebuild's pad handling
                        // owns the row's edge whitespace, exactly as before masking.
                        var logical = BidiReorderer.ReorderIfNeeded(vsb.ToString().Trim(' ', EolShowSpaceSentinel));
                        if (tailPad > 0 && tailPad <= 200) logical += new string(' ', tailPad);
                        indexed[gs] = (indexed[gs].y, indexed[gs].idx, logical);
                        lineStartXs[indexed[gs].idx] = double.NaN;
                        indexed.RemoveRange(gs + 1, ge - gs - 1);
                        ge = gs + 1;
                    }
                    else if (_pageCellWidth > 0 && !double.IsNaN(_pageMinX) && !_pageRotDominant && ge - gs > 1)
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
                            int idx = indexed[k].idx, off = lineOffs[idx];
                            var lineSpans = new List<RunSpan>();
                            foreach (var s in _pageRunSpans)
                                if (s.Offset >= off && s.Offset + s.Len <= off + lines[idx].Length
                                    && s.Width > 0 && !double.IsNaN(s.Width) && !double.IsNaN(s.X))
                                    lineSpans.Add(s);
                            // Only a genuine few-run interleave (a note plus a
                            // dropped-in glyph) qualifies — a line carrying many
                            // runs is a dense block (per-glyph runs, stacked cells)
                            // that must NOT be re-laid by device X, which would
                            // scramble it into single letters.
                            if (lineSpans.Count < 2 || lineSpans.Count > 6) continue;
                            var interleave = false;
                            for (int a = 0; a < lineSpans.Count && !interleave; a++)
                                for (int b = a + 1; b < lineSpans.Count; b++)
                                    if (Math.Min(lineSpans[a].X + lineSpans[a].Width, lineSpans[b].X + lineSpans[b].Width)
                                        - Math.Max(lineSpans[a].X, lineSpans[b].X) > 0.5 * _pageCellWidth)
                                    { interleave = true; break; }
                            if (!interleave) continue;
                            // Split every run into WORDS (a run's own spaces are its
                            // word breaks — NOT another overlapping run's spaces), each
                            // word carrying its device X and its literal grid column
                            // (run grid-start + char offset). Merge the words by device
                            // X, then place the FIRST word at its literal column (so a
                            // run's leading spaces are preserved) and CHAIN the rest by
                            // device gap (+1/3 bias); interior contiguous.
                            var cw = _pageCellWidth;
                            var words = new List<(double x, int litCol, string text, int run)>();
                            var trimC = LineStartGridColTrim(_pageMinX);
                            for (int ri = 0; ri < lineSpans.Count; ri++)
                            {
                                var s = lineSpans[ri];
                                var runGrid = LineStartGridCol(s.X) - trimC;
                                var adv = s.Width / Math.Max(1, s.Len);
                                int ci = 0;
                                while (ci < s.Len)
                                {
                                    var c0 = lines[idx][s.Offset - off + ci];
                                    if (c0 == ' ' || c0 == '\r' || c0 == '\n') { ci++; continue; }
                                    int wStart = ci;
                                    var wsb = new StringBuilder();
                                    while (ci < s.Len)
                                    {
                                        var c1 = lines[idx][s.Offset - off + ci];
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
                                Console.Error.WriteLine($"[ltrx] y={indexed[k].y:F2} runs={lineSpans.Count} words={words.Count} out='{(sb.Length > 60 ? sb.ToString(0, 60) : sb.ToString())}'");
                            indexed[k] = (indexed[k].y, indexed[k].idx, sb.ToString());
                            // The rebuilt line already carries its full absolute
                            // leading pad, so it must emit FIRST and let a
                            // non-interleaving neighbour (a tight run to its left)
                            // overlay into that pad — force it to the front of the
                            // row's X-order.
                            pageXs[indexed[k].idx] = -1e9;
                            lineStartXs[indexed[k].idx] = -1e9;
                        }
                    }
                }
                gs = ge;
            }
        }

        // Replace the page portion of _text with sorted text, merging visual rows.
        // Row formation (boundaries hold to ±0.004pt):
        // each line is a 1-em box sitting on its TRUE descent line (bottom =
        // baseline − descent·fs with the line font's own descent magnitude,
        // top = bottom + fs); two lines are same-row-compatible iff their
        // boxes overlap by at least half the smaller font (inclusive). Lines
        // are walked BOTTOM-UP (ascending baseline) and a line joins the
        // forming row iff it is compatible with EVERY member (complete
        // linkage); otherwise it starts a new row. Rows then emit top-down,
        // members X-ordered. The bottom-up complete-linkage walk is what no
        // pairwise (Δ, fsA, fsB) tolerance could reproduce: an intervening
        // lower line captures its neighbour and flips a pair that would merge
        // in isolation. The per-font descent anchor is what releases a small
        // label riding above a deep-descent large-font row (the fixed 0.2
        // anchor wrongly swallowed it) — and it separates an oversized
        // underscore rule from the header above without any content test.
        var groupOf = new int[indexed.Count];
        {
            var gid = 0;
            var members = new List<(double bot, double top, double fs)>();
            // Font size of the nearest text line strictly BELOW the one being
            // placed (bottom-up walk): an UNDERLINE run (only '_' and spaces)
            // has its box HEIGHT clamped to it — a fs-12 rule drawn over a
            // fs-9 table reads as a 9pt-tall band, so it underlines the row
            // below instead of capturing the header above (the
            // clamp never grows a box).
            var fsBelow = 0.0;
            for (var ii = indexed.Count - 1; ii >= 0; ii--)
            {
                var (yy, idx0, ln) = indexed[ii];
                var blank0 = string.IsNullOrWhiteSpace(ln);
                var ffs = pageFs[idx0];
                if (double.IsNaN(ffs) || ffs <= 0) ffs = 10.0;
                var hEff = ffs;
                if (!blank0 && fsBelow > 0 && fsBelow < ffs && !pageRot[idx0])
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
                var rot0 = pageRot[idx0];
                var bot0 = rot0 ? yy : yy - pageDesc[idx0] * ffs;
                var top0 = bot0 + (rot0 ? ffs : hEff);
                var joins = !rawMode && !blank0 && !double.IsNaN(yy) && members.Count > 0;
                if (joins)
                    foreach (var m in members)
                        if (Math.Min(top0, m.top) - Math.Max(bot0, m.bot)
                            < 0.5 * Math.Min(hEff, m.fs) - 1e-9)
                        { joins = false; break; }
                if (!joins && !(members.Count == 0)) { gid++; members.Clear(); }
                groupOf[ii] = gid;
                if (blank0 || double.IsNaN(yy)) { gid++; members.Clear(); }
                else { members.Add((bot0, top0, hEff)); fsBelow = ffs; }
            }
        }
        _text.Remove(textStartOffset, _text.Length - textStartOffset);
        int gStart2 = 0;
        bool firstGroup = true;
        double prevGroupY = double.NaN;
        while (gStart2 < indexed.Count)
        {
            int gEnd2 = gStart2 + 1;
            var anchor = indexed[gStart2];
            double AnchorX(int idx2) => double.IsNaN(pageXs[idx2]) ? lineStartXs[idx2] : pageXs[idx2];
            while (gEnd2 < indexed.Count && groupOf[gEnd2] == groupOf[gStart2])
                gEnd2++;

            // Same-row segments read left-to-right: order the group by page X
            // (unknown X keeps its Y-sort position, after known Xs).
            var group = indexed.GetRange(gStart2, gEnd2 - gStart2);
            if (group.Count > 1)
                group = group.OrderBy(t => double.IsNaN(AnchorX(t.idx)) ? double.MaxValue : AnchorX(t.idx)).ToList();

            // The row's BOTTOM is the lowest member's descent line; blank rows
            // between consecutive rows gate on the bottom-to-bottom gap with
            // F = the font size of the member that defines the arriving row's
            // bottom (a wrapped note whose row also carries a smaller side
            // note reaches less far down per F unit, opening a blank its
            // uniform twin doesn't).
            var rowBottom = double.NaN;
            var rowBottomFs = double.NaN;
            if (_pageRotDominant)
            {
                // Rotated-dominant pages keep the anchor-baseline gap model
                // (projection-frame sizes don't scale descents).
                rowBottom = anchor.y;
                rowBottomFs = pageFs[anchor.idx];
            }
            else
                foreach (var t in group)
                {
                    var db0 = DescLineY(t.y, t.idx);
                    if (double.IsNaN(db0)) continue;
                    if (double.IsNaN(rowBottom) || db0 < rowBottom)
                    {
                        rowBottom = db0;
                        rowBottomFs = pageFs[t.idx];
                    }
                }
            if (!firstGroup)
            {
                _text.Append("\r\n");
                // Blank rows for the vertical gap between consecutive groups.
                var blanks = BlankRowsFor(prevGroupY, rowBottom, rowBottomFs);
                for (var b = 0; b < blanks; b++) _text.Append("\r\n");
            }
            firstGroup = false;
            if (!double.IsNaN(rowBottom)) prevGroupY = rowBottom;
            if (GridDebug)
            {
                var rowDbg = string.Join("|", group.Select(t =>
                {
                    var s0 = t.line.Trim();
                    return s0.Length > 16 ? s0[..16] : s0;
                }));
                Console.Error.WriteLine($"[row] y={anchor.y:F2} fs={pageFs[anchor.idx]:F2} n={group.Count} '{rowDbg}'");
            }

            var rowStartLen = _text.Length;
            int firstPad = 0;
            var firstLine = group[0].line;
            while (firstPad < firstLine.Length && firstLine[firstPad] == ' ') firstPad++;
            // Character width for X->column mapping within this row: prefer the
            // page grid cell; else the cell rule from the anchor font.
            var anchorFs2 = pageFs[group[0].idx];
            var rowCw = _pageCellWidth > 0
                ? _pageCellWidth
                : (!double.IsNaN(anchorFs2) && anchorFs2 > 0 ? Math.Max(1.0, 0.6 * (Math.Round(anchorFs2, MidpointRounding.AwayFromZero) - 2.0)) : 0.0);
            for (int gi = 0; gi < group.Count; gi++)
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
                    var xa2 = AnchorX(group[0].idx);
                    var xb2 = AnchorX(group[gi].idx);
                    var gridTarget = rowCw > 0 && !double.IsNaN(_pageMinX) && !double.IsNaN(xb2);
                    var target = 0;
                    // A grid-rebuilt interleave line (sentinel X) already carries its
                    // full absolute leading pad, so it overlays from column 0.
                    if (gridTarget && xb2 <= -1e8)
                        target = 0;
                    else if (gridTarget)
                        target = (xb2 < 0 ? -1 : (int)Math.Floor((xb2 - _pageGridOriginX) / rowCw - 1e-9))
                            - (_pageMinX < 0 ? -1 : (int)Math.Floor((_pageMinX - _pageGridOriginX) / rowCw));
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
                    if (gridTarget && target < curCol && rowStartLen + target >= 0)
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
                            continue;
                        }
                    }
                    // Column clamp mirrors the 5000-column grid bound
                    // (oversize engineering sheets pad into the hundreds of columns).
                    var spaces = Math.Min(5000, Math.Max(gridTarget ? 1 : 6, target - curCol));
                    _text.Append(' ', spaces);
                    _text.Append(body);
                }
            }
            gStart2 = gEnd2;
        }
    }

    /// <summary>
    /// Apply RTL reversal to a decoded string from a single Tj/TJ operator.
    /// If the string consists entirely of RTL characters and neutral punctuation/whitespace,
    /// returns the string reversed so that visual-order Hebrew/Arabic becomes logical order.
    /// Otherwise returns the string unchanged.
    /// </summary>
    private static string ApplyRtlIfPureRtl(string text) =>
        IsPureRtlRun(text) ? new string(text.ToCharArray().Reverse().ToArray()) : text;

    /// <summary>True when the run consists of RTL characters plus neutral punctuation and
    /// whitespace only (with at least one RTL char) — the condition under which
    /// <see cref="ApplyRtlIfPureRtl"/> reverses it. The test is char-class based, so it is
    /// invariant under reversal: applied to an already-reversed run it still answers
    /// "was this run reversed at decode time".</summary>
    private static bool IsPureRtlRun(string text)
    {
        if (text.Length == 0) return false;
        bool hasRtl = false;
        foreach (char c in text)
        {
            if (BidiReorderer.IsRtlChar(c))
                hasRtl = true;
            else if (!IsRtlNeutral(c))
                return false; // LTR character found
        }
        return hasRtl;
    }

    private static bool IsRtlNeutral(char c) =>
        c == ' ' || c == '\t' || c == '\n' || c == '\r'
        || (c >= '!' && c <= '/')   // !"#$%&'()*+,-./
        || (c >= ':' && c <= '@')   // :;<=>?@
        || (c >= '[' && c <= '`')   // [\]^_`
        || (c >= '{' && c <= '~');  // {|}~

    /// <summary>
    /// Extract text from all pages of a document.
    /// </summary>
    /// <summary>
    /// Extract text from a Form XObject.
    /// </summary>
    public void Visit(XForm form)
    {
        if (form is null) throw new ArgumentNullException(nameof(form));
        var streamBytes = form.DecodedBytes;
        if (streamBytes.Length == 0) return;

        // XForm has its own dict (with Resources) — use a reader from
        // the page that owns this XForm for object resolution.
        var reader = form.Reader;
        var dict = form.StreamDict;

        var textStart = _text.Length;
        var yStart = _lineYPositions.Count;
        _currentLineY = double.NaN;
        _currentLineCmTy = 0;
        _currentLineEffFs = double.NaN;
        _currentLineIsRotated = false;
        _currentLineDescent = 0.2;
        _currentLineDevY = double.NaN;
        _currentLineRowX = double.NaN;
        _rowXLineOffset = -1;
        _effectiveSearchRect = null; // form streams are not page-rotated
        // No TrimTrailingLineSpaces pass runs on a standalone form visit, so a
        // masked space would never be restored — keep masking off here.
        _maskEolShowSpaces = false;

        ExtractTextFromContentStream(streamBytes, dict, reader);
        SortLinesByY(textStart, yStart);
    }

    public void Visit(Document pdf)
    {
        var pageTexts = new List<string>();
        var isPure = ExtractionOptions?.FormattingMode
            != TextExtractionOptions.TextFormattingMode.Raw;
        foreach (var page in pdf.Pages)
        {
            _text.Clear();
            _lineYPositions.Clear();
        _lineXPositions.Clear();
        _lineFontSizes.Clear();
        _lineIsRotated.Clear();
        _lineDescents.Clear();
            Visit(page);
            var pageText = _text.ToString().Trim('\r', '\n');
            // Pure mode: pad each line to a consistent width so column
            // layout is preserved visually. Pure mode
            // does this to maintain fixed-width COLUMN alignment — so only pad
            // when this page actually shows column structure (some line needed
            // inter-run gap spaces). A single-column page (one run per line,
            // e.g. plain paragraphs) is NOT padded; blanket-padding appended
            // dozens of trailing spaces to every short line.
            if (pageText.Length > 0 && isPure && _sawIntraLineGapSpaces)
                pageText = PadLinesToFixedWidth(pageText);
            // A text-less page (e.g. image only) still contributes its empty entry,
            // so the whole-document join keeps a page separator for it — such
            // a page shows as a blank line between its neighbours.
            pageTexts.Add(pageText);
        }
        // Trailing text-less pages don't add dangling separators.
        while (pageTexts.Count > 0 && pageTexts[^1].Length == 0)
            pageTexts.RemoveAt(pageTexts.Count - 1);
        _text.Clear();
        _text.Append(string.Join("\r\n", pageTexts));
        if (pageTexts.Count > 0)
            _text.Append("\r\n");
    }

    /// <summary>
    /// Pad each line with trailing spaces to a fixed width (~80 chars).
    /// In Pure mode column layouts produce
    /// fixed-width lines for consistent visual alignment. Lines longer than
    /// the target width are left unchanged. Only pads when the page has
    /// multiple lines (single-line pages are left as-is to avoid inflating
    /// short text extractions).
    /// </summary>
    private static string PadLinesToFixedWidth(string text)
    {
        const int targetWidth = 80;
        var lines = text.Split('\n');
        // Only pad pages with multiple lines — single-line pages are short
        // text fragments that shouldn't be padded to 80 chars.
        if (lines.Length < 3) return text;

        var sb = new StringBuilder(text.Length + lines.Length * 5);
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            sb.Append(line);
            var padding = targetWidth - line.Length;
            if (padding > 0)
                sb.Append(' ', padding);
            if (i < lines.Length - 1)
                sb.Append("\r\n");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Clears the extracted text and resets the absorber state so it can be reused.
    /// </summary>
    public void Reset()
    {
        _text.Clear();
        _lineYPositions.Clear();
        _lineXPositions.Clear();
        _lineFontSizes.Clear();
        _lineIsRotated.Clear();
        _lineDescents.Clear();
        _currentLineY = double.NaN;
        _currentLineCmTy = 0;
        _currentLineEffFs = double.NaN;
        _currentLineIsRotated = false;
        _currentLineDescent = 0.2;
        _currentLineDevY = double.NaN;
        _currentLineRowX = double.NaN;
        _rowXLineOffset = -1;
    }

    /// <summary>Join a page's content streams into one buffer with newline
    /// separators, per the spec's single-logical-stream model.</summary>
    private static byte[] CombineContentStreams(List<byte[]> streams)
    {
        if (streams.Count == 1) return streams[0];
        var total = 0;
        foreach (var s in streams) total += s.Length + 1;
        var buf = new byte[total];
        var pos = 0;
        foreach (var s in streams)
        {
            Array.Copy(s, 0, buf, pos, s.Length);
            pos += s.Length;
            buf[pos++] = (byte)'\n';
        }
        return buf;
    }

    /// <summary>Check if a text position is within the page's MediaBox/CropBox.</summary>
    private bool IsWithinPageBounds(double x, double y, PdfDictionary pageDict, PdfReader reader)
    {
        if (TextSearchOptions?.LimitToPageBounds != true) return true;
        var mb = GetPageMediaBox(pageDict, reader);
        if (mb is null) return true;
        return x >= mb[0] - 1 && x <= mb[2] + 1 && y >= mb[1] - 1 && y <= mb[3] + 1;
    }

    private static double[]? GetPageMediaBox(PdfDictionary pageDict, PdfReader reader)
    {
        // Try CropBox first, then MediaBox
        var box = reader.Resolve(pageDict.Get("CropBox")) as PdfArray
               ?? reader.Resolve(pageDict.Get("MediaBox")) as PdfArray;
        if (box is null || box.Count < 4) return null;
        static double getNum(PdfObject? obj) => obj switch
        {
            Core.PdfInteger i => i.Value,
            Core.PdfReal r => r.Value,
            _ => 0
        };
        return [getNum(box[0]), getNum(box[1]), getNum(box[2]), getNum(box[3])];
    }

    /// <summary>
    /// The page's character-grid cell width for Pure-mode layout, computed by
    /// the rule <c>cell = 0.6·(F − 2)</c> where F is the
    /// font size carrying the MOST characters on the page (mode by char count;
    /// ties go to the smallest size). One cell per page, glyph-independent.
    /// Falls back to the mean glyph advance when the dominant size is too small
    /// for the formula, and to 0 when there is too little text to estimate.
    /// </summary>
    private static double EstimatePageCellWidth(List<byte[]> streams, PdfDictionary pageDict, PdfReader reader)
        => EstimatePageGrid(streams, pageDict, reader).cell;

    /// <summary>Pre-scan companion to the grid: the cell width plus the page's
    /// leftmost text X (grid origin). MinX tracks Tm/Td/cm X translation the
    /// same way the extraction loop does (scale-free approximation).</summary>
    private static (double cell, double cellCeil, double minX, double domFs, bool rotDominant) EstimatePageGrid(List<byte[]> streams, PdfDictionary pageDict, PdfReader reader, double scaleFactor = 1.0, double[]? bounds = null)
    {
        double sumW = 0; int cnt = 0;
        int rotChars = 0, uprightChars = 0;
        var rawBySize = new Dictionary<double, double>();
        var widthPerSize = new Dictionary<double, double>();
        // Pure glyph advances (kern adjustments excluded, synthesized spaces
        // not counted): diagnostic population, kept separate from the
        // kern-inclusive sums the gap heuristics were calibrated on.
        var pureWidthPerSize = new Dictionary<double, double>();
        var pureCharsPerSize = new Dictionary<double, int>();
        // The mean-advance population the cell rule averages:
        // kern-inclusive run widths WITHOUT the drawn space glyphs (their
        // advances and counts both come out), synthesized adjustment spaces
        // counted, per-run 0.6 em cap. Calibrated on three-way evidence: a
        // kern-gap French daily needs the kerns counted, a resume with drawn
        // spaces needs them excluded, a rotated CID report needs the formula
        // term to win the min().
        var avgWidthPerSize = new Dictionary<double, double>();
        var avgCharsPerSize = new Dictionary<double, int>();
        var minX = double.NaN;
        var charsPerSize = new Dictionary<double, int>();
        var pageFonts = ResolveFonts(pageDict, reader);

        // Scan one content stream, accumulating glyph advances and font-size
        // populations into the shared maps above. `recurse` gates descent into
        // Form XObjects (Do) so the extra measurement only kicks in for pages
        // whose direct content stream carries too little text — see the
        // two-pass invocation after the definition. `fonts`/`resDict` are the
        // font and resource dictionaries in scope for THIS stream (a form
        // supplies its own), and icm* is the CTM in effect at the stream's
        // start (identity for page content, the CTM at the Do for a form —
        // the form's own /Matrix is ignored, matching the extraction loop).
        void Scan(byte[] streamBytes, Dictionary<string, PdfDictionary> fonts,
            PdfDictionary resDict, double icmA, double icmB, double icmC,
            double icmD, double icmE, double icmF, int rdepth, bool recurse)
        {
            var lexer = new PdfLexer(streamBytes);
            var operands = new List<PdfObject>();
            FontMetrics? metrics = null; double fontSize = 12;
            double tlmX = 0, cmTx = icmE;
            // Baseline Y (device, approximate) and leading — only consulted for
            // the bounds check, mirroring the X tracking's level of fidelity.
            double tlmY = icmF, preTL = 0;
            // Rotated mirror of the extraction loop: for sideways text (rotation in
            // the Tm or in the CTM) the reading-axis X is the composed origin
            // projected on (a,b), Td advances scale by |(a,b)|, and the dominant
            // font size counts in DEVICE units — otherwise the pre-scan minX/cell
            // disagree with the runtime grid coordinates.
            double tmScaleX = 1.0;
            double fsScale = 1.0;
            // Horizontal scaling (Tz, percent/100): condensed text draws — and
            // measures — narrower than the font's nominal advances.
            double preHorizScale = 1.0;
            // Character/word spacing (Tc/Tw, text-space units): the
            // segment measure includes them — a negative Tc condenses every
            // advance the mean-advance cell averages.
            double preTc = 0, preTw = 0;
            bool preRot = false;
            double cmA = icmA, cmB = icmB, cmC = icmC, cmD = icmD, cmE = icmE, cmF = icmF;
            var cmFullStack = new Stack<(double a, double b, double c, double d, double e, double f)>();
            var cmStack = new Stack<double>();
            void SeeShowX()
            {
                // Rotated keeps the projection+cmTx frame (mirrors the runtime);
                // upright tlmX is already the composed device X.
                var x = preRot ? tlmX + cmTx : tlmX;
                // Text drawn at negative X (template/title-block junk on
                // engineering sheets with a shifted MediaBox) can't occupy a
                // grid column and never anchors the grid origin.
                if (x < 0) return;
                if (double.IsNaN(minX) || x < minX) minX = x;
            }
            // True when the current show position falls outside the measuring
            // window (page bounds under LimitToPageBounds) — its glyphs are
            // clipped from the output, so they must not vote in the grid.
            bool ShowOutOfBounds()
            {
                if (bounds is null) return false;
                var x = preRot ? tlmX + cmTx : tlmX;
                if (x < bounds[0] - 1 || x > bounds[2] + 1) return true;
                return !preRot && (tlmY < bounds[1] - 1 || tlmY > bounds[3] + 1);
            }
            while (true)
            {
                var tok = lexer.NextToken();
                if (tok.Kind == TokenKind.Eof) break;
                switch (tok.Kind)
                {
                    case TokenKind.Integer: operands.Add(new Core.PdfInteger(tok.IntValue)); break;
                    case TokenKind.Real: operands.Add(new Core.PdfReal(tok.RealValue)); break;
                    case TokenKind.LiteralString: operands.Add(new Core.PdfString(tok.BytesValue!)); break;
                    case TokenKind.HexString: operands.Add(new Core.PdfString(tok.BytesValue!, isHex: true)); break;
                    case TokenKind.Name: operands.Add(new Core.PdfName(tok.StringValue!)); break;
                    case TokenKind.ArrayStart: operands.Add(ParseContentArray(lexer)); break;
                    case TokenKind.Keyword:
                        var op = tok.StringValue!;
                        if (op == "Tf")
                        {
                            if (operands.Count >= 2 && operands[0] is Core.PdfName fn
                                && fonts.TryGetValue(fn.Value, out var fdict))
                            {
                                try { metrics = FontMetrics.FromFontDict(fdict, reader); } catch { metrics = null; }
                                fontSize = Math.Abs(GetNumber(operands[1]));
                            }
                        }
                        else if (op == "BT")
                        {
                            fsScale = Math.Sqrt(cmC * cmC + cmD * cmD);
                            if (fsScale < 0.001) fsScale = 1.0;
                            var nab = Math.Sqrt(cmA * cmA + cmB * cmB);
                            if (nab < 0.001) nab = 1.0;
                            preRot = Math.Abs(cmB) > 0.001 && Math.Abs(cmD) < 0.1 * Math.Abs(cmB);
                            tmScaleX = nab;
                            tlmX = preRot ? (cmE * cmA + cmF * cmB) / nab : cmE;
                            tlmY = cmF;
                        }
                        else if (op == "Tm" && operands.Count >= 6)
                        {
                            var mA = GetNumber(operands[0]); var mB = GetNumber(operands[1]);
                            var mC = GetNumber(operands[2]); var mD = GetNumber(operands[3]);
                            var mE = GetNumber(operands[4]); var mF = GetNumber(operands[5]);
                            // Compose with the CTM linear part (mirrors the extraction loop).
                            var cEa = mA * cmA + mB * cmC; var cEb = mA * cmB + mB * cmD;
                            var cEc = mC * cmA + mD * cmC; var cEd = mC * cmB + mD * cmD;
                            var cEe = mE * cmA + mF * cmC + cmE; var cEf = mE * cmB + mF * cmD + cmF;
                            fsScale = Math.Sqrt(cEc * cEc + cEd * cEd);
                            if (fsScale < 0.001) fsScale = 1.0;
                            var nab2 = Math.Sqrt(cEa * cEa + cEb * cEb);
                            if (nab2 < 0.001) nab2 = 1.0;
                            preRot = Math.Abs(cEb) > 0.001 && Math.Abs(cEd) < 0.1 * Math.Abs(cEb);
                            tmScaleX = nab2;
                            tlmX = preRot ? (cEe * cEa + cEf * cEb) / nab2 : cEe;
                            tlmY = cEf;
                        }
                        else if ((op == "Td" || op == "TD") && operands.Count >= 2)
                        {
                            tlmX += GetNumber(operands[0]) * tmScaleX;
                            tlmY += GetNumber(operands[1]) * fsScale;
                            if (op == "TD") preTL = -GetNumber(operands[1]) * fsScale;
                        }
                        else if (op == "TL" && operands.Count >= 1) { preTL = GetNumber(operands[0]) * fsScale; }
                        else if (op == "T*") { tlmY -= preTL; }
                        else if (op == "Tz" && operands.Count >= 1)
                        {
                            var hs = GetNumber(operands[0]) / 100.0;
                            if (hs > 0.01 && hs < 100) preHorizScale = hs;
                        }
                        else if (op == "Tc" && operands.Count >= 1) { preTc = GetNumber(operands[0]); }
                        else if (op == "Tw" && operands.Count >= 1) { preTw = GetNumber(operands[0]); }
                        else if (op == "q") { cmStack.Push(cmTx); cmFullStack.Push((cmA, cmB, cmC, cmD, cmE, cmF)); }
                        else if (op == "Q")
                        {
                            if (cmStack.Count > 0) cmTx = cmStack.Pop();
                            if (cmFullStack.Count > 0) (cmA, cmB, cmC, cmD, cmE, cmF) = cmFullStack.Pop();
                        }
                        else if (op == "cm" && operands.Count >= 6)
                        {
                            cmTx += GetNumber(operands[4]);
                            var na = GetNumber(operands[0]); var nb = GetNumber(operands[1]);
                            var nc = GetNumber(operands[2]); var nd = GetNumber(operands[3]);
                            var ne = GetNumber(operands[4]); var nf = GetNumber(operands[5]);
                            var a2 = na * cmA + nb * cmC; var b2 = na * cmB + nb * cmD;
                            var c2 = nc * cmA + nd * cmC; var d2 = nc * cmB + nd * cmD;
                            var e2 = ne * cmA + nf * cmC + cmE; var f2 = ne * cmB + nf * cmD + cmF;
                            cmA = a2; cmB = b2; cmC = c2; cmD = d2; cmE = e2; cmF = f2;
                        }
                        else if (op == "BI") { SkipInlineImage(lexer); }
                        else if (op == "Do" && recurse && rdepth < 6
                            && operands.Count >= 1 && operands[0] is Core.PdfName doName)
                        {
                            // A page can draw all its text inside a Form XObject
                            // (a shifted-MediaBox wrapper); measure that text too so
                            // the grid is sized instead of falling back to gap
                            // spacing. Recurse with the form's own fonts and the CTM
                            // in effect at the Do (form /Matrix ignored, as in the
                            // extraction loop).
                            var xobjs = ResolveXObjects(resDict, reader);
                            var xstr = xobjs is not null ? reader.ResolveStream(xobjs.Get(doName.Value)) : null;
                            if (xstr is not null && reader.ResolveName(xstr.Dict, "Subtype") == "Form")
                            {
                                var xbytes = reader.DecodeStream(xstr);
                                var formFonts = ResolveFonts(xstr.Dict, reader);
                                Scan(xbytes, formFonts, xstr.Dict, cmA, cmB, cmC, cmD, cmE, cmF, rdepth + 1, recurse);
                            }
                        }
                        else if (op == "Tj" || op == "'" || op == "\"")
                        {
                            if (op != "Tj") tlmY -= preTL; // ' and " imply T*
                            var s = operands.LastOrDefault(o => o is Core.PdfString) as Core.PdfString;
                            if (s is not null && metrics is not null && !ShowOutOfBounds())
                            {
                                SeeShowX();
                                // Grid buckets CEIL the effective size BEFORE aggregation
                                // (9.2pt and 9.7pt text pools into one 10pt
                                // bucket; 11.01pt grids as 12pt). Advances still measure at
                                // the true size.
                                var fsTrue = fontSize * fsScale;
                                // Round before ceiling: matrix-composition float dirt
                                // (12.0000001) must not bump a whole bucket.
                                var fsDev = Math.Ceiling(Math.Round(fsTrue, 3));
                                if (!rawBySize.TryGetValue(fsDev, out var rw) || fsTrue < rw) rawBySize[fsDev] = fsTrue;
                                // Advances scale by the ADVANCE-axis norm — on rotated pages a
                                // run's horizontal stretch is independent of its font size.
                                var fsAdv = fontSize * (preRot ? tmScaleX : fsScale);
                                var w1 = metrics.MeasureString(s.Value, fsAdv) * preHorizScale;
                                sumW += w1;
                                var g = GlyphCount(s.Value.Length, metrics);
                                cnt += g;
                                if (preRot) rotChars += g; else uprightChars += g;
                                charsPerSize[fsDev] = charsPerSize.GetValueOrDefault(fsDev) + g;
                                widthPerSize[fsDev] = widthPerSize.GetValueOrDefault(fsDev)
                                    + Math.Min(w1, 0.6 * fsTrue * g);
                                pureCharsPerSize[fsDev] = pureCharsPerSize.GetValueOrDefault(fsDev) + g;
                                pureWidthPerSize[fsDev] = pureWidthPerSize.GetValueOrDefault(fsDev)
                                    + Math.Min(w1, 0.6 * fsTrue * g);
                                var (nsp, wsp) = DrawnSpaces(s.Value, metrics, fsAdv, preHorizScale);
                                var tcW = (preTc * g + preTw * nsp) * (preRot ? tmScaleX : fsScale) * preHorizScale;
                                avgCharsPerSize[fsDev] = avgCharsPerSize.GetValueOrDefault(fsDev) + g;
                                avgWidthPerSize[fsDev] = avgWidthPerSize.GetValueOrDefault(fsDev)
                                    + Math.Min(Math.Max(w1 + tcW, 0), 0.6 * fsTrue * g);
                            }
                        }
                        else if (op == "TJ" && operands.Count >= 1 && operands[0] is Core.PdfArray arr
                            && !ShowOutOfBounds())
                        {
                            var sawString = false;
                            // The estimate averages NET run widths — glyph advances plus
                            // the array's kern adjustments — over the run's PHYSICAL text
                            // length, which includes the word spaces its kern rule will
                            // synthesize (an adjustment at word depth becomes a char).
                            double arrW = 0; var arrG = 0; double arrFsTrue = 0, arrFsDev = 0;
                            double arrWPure = 0; var arrSp = 0; double arrSpW = 0;
                            // Mean-advance numerator: glyph widths plus kern adjustments,
                            // EXCLUDING large positive ones — a positive adjustment past
                            // ~0.1 em is a backward pen JUMP (RTL layout), not kerning, and
                            // would cancel real ink out of the average, collapsing the cell.
                            // Small positive tightening kerns stay in the sum.
                            double arrWAvg = 0;
                            var arrMulti = false; var arrDeep = 0; var arrAdjCnt = 0; var arrSynth = 0;
                            // Word-space synthesis chars for the MEAN-ADVANCE population:
                            // a deep kern only reads as a word space when it does NOT
                            // adjoin a drawn space glyph (justified text kerns beside its
                            // real spaces; those gaps are already counted by the glyph).
                            var arrSynthAvg = 0; var pendingSynth = 0; var prevEndsSpace = false;
                            foreach (var it in arr)
                            {
                                if (it is Core.PdfString ps && metrics is not null)
                                {
                                    sawString = true;
                                    arrFsTrue = fontSize * fsScale;
                                    arrFsDev = Math.Ceiling(Math.Round(arrFsTrue, 3));
                                    if (!rawBySize.TryGetValue(arrFsDev, out var rw2) || arrFsTrue < rw2) rawBySize[arrFsDev] = arrFsTrue;
                                    // Advance-axis norm (see the Tj note).
                                    var arrFsAdv = fontSize * (preRot ? tmScaleX : fsScale);
                                    var wPiece = metrics.MeasureString(ps.Value, arrFsAdv) * preHorizScale;
                                    arrW += wPiece;
                                    arrWPure += wPiece;
                                    arrWAvg += wPiece;
                                    var g2 = GlyphCount(ps.Value.Length, metrics);
                                    arrG += g2;
                                    var (nsp2, wsp2) = DrawnSpaces(ps.Value, metrics, arrFsAdv, preHorizScale);
                                    arrSp += nsp2; arrSpW += wsp2;
                                    var simple = !metrics.IsCid && ps.Value.Length > 0;
                                    if (pendingSynth > 0 && !(simple && ps.Value[0] == 0x20))
                                        arrSynthAvg += pendingSynth;
                                    pendingSynth = 0;
                                    prevEndsSpace = simple && ps.Value[^1] == 0x20;
                                    if (g2 >= 2) arrMulti = true;
                                }
                                else if (it is not Core.PdfString && metrics is not null)
                                {
                                    var adj = GetNumber(it);
                                    arrW -= adj * fontSize * (preRot ? tmScaleX : fsScale) * preHorizScale / 1000.0;
                                    if (adj < 100)
                                        arrWAvg -= adj * fontSize * (preRot ? tmScaleX : fsScale) * preHorizScale / 1000.0;
                                    arrAdjCnt++;
                                    if (adj <= -130) arrDeep++;
                                    if (adj < -190 || (arrMulti && adj <= -130))
                                    {
                                        arrSynth++;
                                        if (!prevEndsSpace) pendingSynth++;
                                    }
                                }
                            }
                            if (sawString)
                            {
                                // Positioning arrays (word-depth kerns are the norm) don't
                                // synthesize; mirror the runtime rule's shape.
                                if (!arrMulti && arrAdjCnt >= 3 && arrDeep * 2 >= arrAdjCnt) { arrSynth = 0; arrSynthAvg = 0; }
                                if (arrW < 0) arrW = 0;
                                var chars2 = arrG + arrSynth;
                                sumW += arrW;
                                cnt += chars2;
                                if (preRot) rotChars += arrG; else uprightChars += arrG;
                                charsPerSize[arrFsDev] = charsPerSize.GetValueOrDefault(arrFsDev) + chars2;
                                widthPerSize[arrFsDev] = widthPerSize.GetValueOrDefault(arrFsDev)
                                    + Math.Min(arrW, 0.6 * arrFsTrue * chars2);
                                pureCharsPerSize[arrFsDev] = pureCharsPerSize.GetValueOrDefault(arrFsDev) + arrG;
                                pureWidthPerSize[arrFsDev] = pureWidthPerSize.GetValueOrDefault(arrFsDev)
                                    + Math.Min(arrWPure, 0.6 * arrFsTrue * arrG);
                                var avgChars = arrG + arrSynthAvg;
                                var arrTcW = (preTc * arrG + preTw * arrSp) * (preRot ? tmScaleX : fsScale) * preHorizScale;
                                avgCharsPerSize[arrFsDev] = avgCharsPerSize.GetValueOrDefault(arrFsDev) + avgChars;
                                avgWidthPerSize[arrFsDev] = avgWidthPerSize.GetValueOrDefault(arrFsDev)
                                    + Math.Min(Math.Max(arrWAvg + arrTcW, 0), 0.6 * arrFsTrue * avgChars);
                                SeeShowX();
                            }
                        }
                        operands.Clear();
                        break;
                    default: operands.Clear(); break;
                }
            }
        }

        // First pass: the page's own content streams only (unchanged behaviour).
        foreach (var streamBytes in streams)
            Scan(streamBytes, pageFonts, pageDict, 1, 0, 0, 1, 0, 0, 0, recurse: false);

        // Rescue pass: a page whose direct stream carries almost no text draws it
        // through Form XObjects. Re-measure descending into those forms so the grid
        // gets sized (otherwise cell = 0 and Pure spacing falls back to the coarser
        // gap heuristic). Only mixed-content pages with real direct text (cnt >= 8)
        // keep the original, calibration-preserving estimate.
        if (cnt < 8)
        {
            sumW = 0; cnt = 0; rotChars = 0; uprightChars = 0; minX = double.NaN;
            rawBySize.Clear(); widthPerSize.Clear(); pureWidthPerSize.Clear();
            pureCharsPerSize.Clear(); avgWidthPerSize.Clear(); avgCharsPerSize.Clear();
            charsPerSize.Clear();
            foreach (var streamBytes in streams)
                Scan(streamBytes, pageFonts, pageDict, 1, 0, 0, 1, 0, 0, 0, recurse: true);
        }

        if (cnt < 8) return (0, 0, minX, 0, rotChars > uprightChars);

        // Dominant font size: most characters; tie → smallest size.
        double domSize = 0; var domCount = -1;
        foreach (var kv in charsPerSize)
        {
            if (kv.Value > domCount || (kv.Value == domCount && kv.Key < domSize))
            {
                domSize = kv.Key; domCount = kv.Value;
            }
        }
        // Calibrated rule (22 controlled trials): the grid cell
        // is scaleFactor · 0.6·(F−2) — F = the ceiled-size bucket holding the
        // most characters (sizes CEIL to integer buckets BEFORE the counts
        // aggregate: an 8.04pt report grids at the 9-bucket cell 4.2). There
        // is NO mean-advance branch on this path. Only the explicit AUTO mode
        // (ScaleFactor = 0) sets the cell to the page's capped mean glyph
        // advance: kern-inclusive run widths (backward jumps excluded), Tz/Tc
        // applied, drawn spaces included, adjacency-aware synthesized spaces,
        // per-run cap 0.6·fsTrue — measured over the dominant bucket only.
        // Blank-row thresholds still key on the RAW dominant size (line
        // heights are untransformed).
        if (domSize > 2.5)
        {
            var rawDom = rawBySize.TryGetValue(domSize, out var rv) ? rv : domSize;
            var ac = avgCharsPerSize.GetValueOrDefault(domSize);
            var aw = avgWidthPerSize.GetValueOrDefault(domSize);
            var sf = scaleFactor > 0 ? scaleFactor : 1.0;
            var cell = sf * 0.6 * (domSize - 2);
            if (scaleFactor == 0 && ac > 0) cell = aw / ac;
            if (GridDebug)
            {
                var dc = charsPerSize.GetValueOrDefault(domSize);
                var dw = widthPerSize.GetValueOrDefault(domSize);
                var pc = pureCharsPerSize.GetValueOrDefault(domSize);
                var pw = pureWidthPerSize.GetValueOrDefault(domSize);
                Console.Error.WriteLine($"[cell] dom={domSize} raw={rawDom:F2} chars={dc} width={dw:F1} "
                    + $"avg={(dc > 0 ? dw / dc : 0):F3} pureAvg={(pc > 0 ? pw / pc : 0):F3} "
                    + $"nsAvg={(ac > 0 ? aw / ac : 0):F3} cell={cell:F3} "
                    + $"legacy={0.6 * (rawDom - 2):F3} legacyCeil={0.6 * (domSize - 2):F3}");
                foreach (var kv in charsPerSize)
                    Console.Error.WriteLine($"[cell]   bucket={kv.Key} chars={kv.Value} width={widthPerSize.GetValueOrDefault(kv.Key):F1} avg={(kv.Value > 0 ? widthPerSize.GetValueOrDefault(kv.Key) / kv.Value : 0):F3} pureAvg={(pureCharsPerSize.GetValueOrDefault(kv.Key) > 0 ? pureWidthPerSize.GetValueOrDefault(kv.Key) / pureCharsPerSize.GetValueOrDefault(kv.Key) : 0):F3}");
            }
            return (cell, sf * 0.6 * (domSize - 2), minX, rawDom, rotChars > uprightChars);
        }
        return (sumW / cnt, sumW / cnt, minX, 0, rotChars > uprightChars);
    }

    /// <summary>Approximate glyph count from a show-string's byte length: 2-byte codes for a
    /// composite (CID/Identity-H) font, one byte per glyph otherwise. Keeps the mean-advance
    /// estimate from halving the cell width on CID pages.</summary>
    private static int GlyphCount(int byteLen, FontMetrics metrics)
        => metrics.IsCid ? (byteLen + 1) / 2 : byteLen;

    /// <summary>Count and measure the drawn SPACE glyphs of a show string (simple fonts:
    /// byte 0x20; composite fonts are left alone — their space CID isn't identifiable
    /// without decoding). The mean-advance cell population excludes them.</summary>
    private static (int count, double width) DrawnSpaces(
        byte[] bytes, FontMetrics metrics, double fsAdv, double horizScale)
    {
        if (metrics.IsCid) return (0, 0);
        var n = 0;
        foreach (var b in bytes)
            if (b == 0x20) n++;
        if (n == 0) return (0, 0);
        return (n, n * metrics.GetWidth(0x20) * fsAdv / 1000.0 * horizScale);
    }
}

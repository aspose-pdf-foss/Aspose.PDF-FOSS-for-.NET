using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
    /// <summary>
    /// Append the glyphs of one show string that fall within the search rectangle, in
    /// page space, to <paramref name="sb"/>. The pen (<paramref name="penText"/>, in the
    /// unscaled text-space units the absorber tracks) advances over every glyph whether
    /// or not it is visible, so positioning of later runs is unaffected. X is clipped per
    /// glyph: a glyph contributes only when its whole advance box lies within
    /// [LLX, URX]. Y is filtered at the line level by the caller before this runs.
    /// </summary>
    /// <remarks>
    /// The absorber accumulates Td/TD advances unscaled but tracks the text-matrix X scale
    /// in <paramref name="tmScaleX"/> and the page-space line origin in <paramref name="tmOriginX"/>,
    /// so a text-space pen X maps to page space as
    /// tmOriginX + (penText - tmOriginX) * tmScaleX + localCmTx.
    /// </remarks>
    private static void AppendClippedRun(StringBuilder sb, byte[] bytes,
        Dictionary<int, string>? toUnicode, PdfDictionary? fontDict, PdfReader reader,
        bool useFontEngine, FontMetrics? metrics, double fontSize, double horizScale,
        Rectangle searchRect, double tmOriginX, double tmScaleX, double localCmTx,
        double cmScaleX, ref double penText, double charSpacing, double wordSpacing,
        out double keptStartPen, bool blankClipped = false, bool dropLeadingSpaces = false)
    {
        // Text-space pen of the first SURVIVING glyph: a left-clipped run's
        // grid position starts there, not at the run's off-page origin.
        // (Not reported in blank mode — the run keeps its original position.)
        keptStartPen = double.NaN;
        const double eps = 0.05;
        var isCid = metrics?.IsCid ?? (fontDict?.GetName("Subtype") == "Type0");
        var step = isCid ? 2 : 1;
        for (var i = 0; i + step - 1 < bytes.Length; i += step)
        {
            var code = isCid ? ((bytes[i] << 8) | bytes[i + 1]) : bytes[i];
            var seg = isCid ? new[] { bytes[i], bytes[i + 1] } : new[] { bytes[i] };
            var glyph = NormalizeDecoded(DecodeString(seg, toUnicode, fontDict, reader, useFontEngine), foldNbsp: false);
            // Advance = glyph width + Tc (+ Tw on the space code), matching the
            // main extraction pen — clip positions drift without them.
            var w = ((metrics is not null
                ? metrics.GetWidth(code) * fontSize / 1000.0
                : fontSize * 0.5 * System.Math.Max(1, glyph.Length))
                + charSpacing + (!isCid && code == 32 ? wordSpacing : 0)) * horizScale;
            // Device X = CTM linear scale × (Tm-composed text X) + CTM translation.
            // Content nested in a scaled Form XObject (e.g. resized page content
            // invoked via "0.6 0 0 0.6 tx ty cm /Fm Do") must fold the cm scale in,
            // or every glyph past (URX − cmTx) of UNSCALED text space gets clipped.
            var scale = System.Math.Abs(cmScaleX) > 1e-9 ? cmScaleX : 1.0;
            var e1 = scale * (tmOriginX + (penText - tmOriginX) * tmScaleX) + localCmTx;
            var e2 = scale * (tmOriginX + (penText + w - tmOriginX) * tmScaleX) + localCmTx;
            var pageLeft = System.Math.Min(e1, e2);
            var pageRight = System.Math.Max(e1, e2);
            if (pageLeft >= searchRect.LLX - eps && pageRight <= searchRect.URX + eps)
            {
                // A rectangle window re-anchors a clipped line at its first
                // surviving NON-SPACE glyph: drawn leading spaces entering the
                // window are dropped (a window over "    24.08.2026" returns
                // "24.08.2026", while synthesized grid pads are
                // added later and survive).
                if (dropLeadingSpaces && sb.Length == 0 && glyph.Length > 0
                    && glyph.Trim().Length == 0)
                {
                    penText += w;
                    continue;
                }
                if (!blankClipped && double.IsNaN(keptStartPen)) keptStartPen = penText;
                sb.Append(glyph);
            }
            else if (blankClipped)
            {
                sb.Append(' ', Math.Max(1, glyph.Length));
            }
            penText += w;
        }
    }

    // Compute number of spaces to emit for an inter-run gap.
    // Raw mode always emits at most 1 space (no visual formatting reconstruction).
    // Pure mode emits proportional spaces so column layout is preserved:
    //   count ≈ round(gap / spaceWidth), where spaceWidth is the typical space glyph width
    //   (~0.25 * fontSize for most Latin fonts). Clamped to avoid runaway widths.
    // Returns 0 when gap is below the threshold (no space should be inserted).
    /// <summary>
    /// Keep the Pure-mode grid origin current: find the start of the line being built
    /// (text after the last newline) and, when it changes, reset the grid so the first run
    /// of the new line anchors column 0. Called before spacing so <see cref="ColumnSpaces"/>
    /// measures from the correct line origin.
    /// </summary>
    private void TrackLineStart(double runPageX, bool whitespaceOnly = false)
    {
        int ls = _text.Length;
        while (ls > 0 && _text[ls - 1] != '\n') ls--;
        if (ls != _lineStartTextOffset) { _lineStartTextOffset = ls; _lineStartPageX = double.NaN; }
        // Whitespace-only runs are grid citizens like any other: they anchor
        // the line and their glyphs fill their own columns (the leading-space
        // extraChars rule keeps the column accounting consistent).
        // Pure-pad lines are emitted for them.
        if (double.IsNaN(_lineStartPageX))
        {
            _lineStartPageX = runPageX;
            // Remember every line's start offset + X so the page pass can pad
            // leading grid columns from the page-absolute origin (minX).
            _pageLineStarts.Add((ls, runPageX));
        }
        else if (runPageX < _lineStartPageX)
        {
            // The line's leading column reflects its LEFTMOST run: streams often
            // draw a row's trailing space fragment (far right) before the row
            // text, and anchoring on that first-seen X would pad wildly.
            _lineStartPageX = runPageX;
            for (var i = _pageLineStarts.Count - 1; i >= 0; i--)
            {
                if (_pageLineStarts[i].offset != ls) continue;
                _pageLineStarts[i] = (ls, runPageX);
                break;
            }
        }
    }

    // Per-page span of every appended show-run: [Offset, Offset+Len) in _text, the
    // run's page-space start X and rendered width, whether ApplyRtlIfPureRtl reversed
    // it at decode time, and (when the code↔char mapping is 1:1) each character's
    // page-space X offset from the run start, in CODE (visual) order. Input to the
    // RTL row re-assembly, which merges rows per character by X.
    private readonly record struct RunSpan(int Offset, int Len, double X, double Width, bool Reversed, double[]? CharXs);

    /// <summary>Per-code start offsets (page units, relative to the run's X) for a show
    /// string, in code order. Null when no metrics are available or the decoded length
    /// differs from the code count (ligatures, multi-char mappings, clipped runs).</summary>
    private static double[]? BuildCharXs(byte[] bytes, FontMetrics? metrics, double fontSize,
        double scale, int decodedLen, double charSpacing = 0, double wordSpacing = 0)
    {
        if (metrics is null) return null;
        var codeCount = metrics.IsCid ? bytes.Length / 2 : bytes.Length;
        if (codeCount != decodedLen || codeCount == 0) return null;
        var rel = new double[codeCount];
        double pen = 0;
        if (metrics.IsCid)
        {
            for (int i = 0, k = 0; i + 1 < bytes.Length; i += 2, k++)
            {
                rel[k] = pen * scale;
                pen += metrics.GetWidth((bytes[i] << 8) | bytes[i + 1]) * fontSize / 1000.0
                       + charSpacing;
            }
        }
        else
        {
            // Per-glyph advance carries Tc (and Tw on byte 32) exactly like the
            // rendered pen — a Tw-inflated in-string space then spans its true
            // multi-column gap.
            for (var i = 0; i < bytes.Length; i++)
            {
                rel[i] = pen * scale;
                pen += metrics.GetWidth(bytes[i]) * fontSize / 1000.0
                       + charSpacing + (bytes[i] == 32 ? wordSpacing : 0);
            }
        }
        return rel;
    }

    /// <summary>Map a viewer-space rectangle to media space by undoing the page's
    /// /Rotate. Viewer space for /Rotate 90 shows the media rotated clockwise, so
    /// (xv, yv) → (W − yv, xv) etc., with W/H the media box width/height.</summary>
    private static Rectangle? MapViewerRectToMedia(Rectangle? rect, Page page)
    {
        if (rect is null) return null;
        var rotate = ((page.RotateDegrees % 360) + 360) % 360;
        if (rotate == 0) return rect;
        var mb = page.MediaBox;
        var w = mb?.Width ?? 612;
        var h = mb?.Height ?? 792;
        double llx, lly, urx, ury;
        switch (rotate)
        {
            case 90:
                llx = w - rect.URY; lly = rect.LLX;
                urx = w - rect.LLY; ury = rect.URX;
                break;
            case 180:
                llx = w - rect.URX; lly = h - rect.URY;
                urx = w - rect.LLX; ury = h - rect.LLY;
                break;
            case 270:
                llx = rect.LLY; lly = h - rect.URX;
                urx = rect.URY; ury = h - rect.LLX;
                break;
            default:
                return rect;
        }
        return new Rectangle(llx, lly, urx, ury);
    }

    /// <summary>
    /// Pure-mode leading columns: each page lays out on a character
    /// grid anchored at the page's leftmost text X, so a line starting to the
    /// right of that origin gets round((x − minX) / cell) leading spaces.
    /// Runs after the page streams (before line sorting), inserting from the
    /// last line backwards so recorded offsets stay valid.
    /// </summary>
    /// <summary>Absolute grid column of a page X: the grid is anchored at
    /// the page's left edge (MediaBox LLX; x = 0 for ordinary pages) with
    /// lines every cell width; pad = col(X) - col(minX), no phase, no dx
    /// division.</summary>
    // The grid is WALKED, not divided: column n ends at the accumulated sum
    // s(n) = (((origin + cell) + cell) + …), which drifts from origin + n·cell by
    // the rounding of every addition. That drift is exactly what decides an x
    // landing on a nominal multiple - a machine-set column stop - so the ladder
    // has to be built the same way rather than divided through. Measured over
    // 12 944 positions in five font sizes and three fonts: the dividing form
    // misplaces 137 of them, the ladder none.
    private int GridCol(double x) => GridColOf(x, _pageGridOriginX, _pageCellWidth);

    // Past this many columns the ladder stops growing and the nominal division
    // answers instead: no page grid runs that wide (the pads themselves cap at
    // _maxCols), and the drift is far below one column there anyway.
    private const int GridLadderMax = 65536;

    internal static int GridColOf(double x, double origin, double cell)
    {
        if (!(cell > 0)) return 0;
        // Left of the origin there are no accumulated stops to walk; the pure
        // grid clamps everything at negative X to one column left regardless.
        if (x < origin) return (int)Math.Floor((x - origin) / cell);
        if (_gridStops is null || _gridStopsCell != cell || _gridStopsOrigin != origin)
        {
            _gridStops = new double[256];
            _gridStopsOrigin = origin;
            _gridStopsCell = cell;
            _gridStopsCount = 0;
        }
        var stops = _gridStops;
        if (_gridStopsCount == 0 || stops[_gridStopsCount - 1] <= x)
        {
            var s = _gridStopsCount == 0 ? origin : stops[_gridStopsCount - 1];
            while (s <= x && _gridStopsCount < GridLadderMax)
            {
                s += cell;
                if (_gridStopsCount == stops.Length)
                {
                    var grown = new double[Math.Min(GridLadderMax, stops.Length * 2)];
                    Array.Copy(stops, grown, stops.Length);
                    _gridStops = stops = grown;
                }
                stops[_gridStopsCount++] = s;
            }
            if (_gridStopsCount >= GridLadderMax && stops[_gridStopsCount - 1] <= x)
                return (int)Math.Floor((x - origin) / cell);
        }
        // Column = how many stops the position has passed.
        int lo = 0, hi = _gridStopsCount;
        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
            if (stops[mid] <= x) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    // Grid column of a LINE START: text left of x = 0 (invisible zone markers,
    // shifted-MediaBox title blocks) reads as column −1 regardless of magnitude
    // (x = −3 and x = −300 both land one column left of the
    // grid). Such a line zeroes the trim at −1, shifting the whole page right
    // by one column.
    /// <remarks>The collapse treats a negative start as an OUTLIER, which is what it is
    /// on an upright page. A ROTATION-DOMINANT page is different: its whole coordinate
    /// system runs negative, and collapsing every line to -1 leaves them all at column 0,
    /// so the page loses its columns and extracts with no leading indent at all. The real
    /// grid column therefore applies exactly when the page is rotation-dominant AND its
    /// origin is itself negative; an upright page keeps the outlier rule, whose absence
    /// pads it by tens of thousands of phantom columns (98 850 chars becomes
    /// 128 319 and its asserted offsets move).</remarks>
    private int LineStartGridCol(double x, double minX) =>
        x < 0 && !(_pageRotDominant && minX < 0) ? -1 : GridCol(x);

    private void InsertLeadingGridSpaces(int pageTextStart)
    {
        if (_pageCellWidth <= 0 || _pageLineStarts.Count == 0) return;
        // The grid trim is the OBSERVED minimum over the page's emitted lines
        // (the grid is trimmed by the smallest leading-space count of
        // any produced line). The pre-scan minX can sit left of every emitted
        // line — clipped or invisible runs that never extract — and trimming
        // by it pads phantom leading columns onto the whole page.
        // Under a rectangle window only line starts INSIDE the window's X band
        // participate: a start recorded for a run whose glyphs were all clipped
        // away sits at an out-of-band x and would both skew the trim and pad
        // lines that no longer hold that content.
        var padWin = _effectiveSearchRect ?? TextSearchOptions?.Rectangle;
        bool InBand(double x) => padWin is null || (x >= padWin.LLX - 1 && x <= padWin.URX + 1);
        var minX = double.MaxValue;
        foreach (var (off, x) in _pageLineStarts)
            if (off >= pageTextStart && InBand(x) && x < minX) minX = x;
        if (minX == double.MaxValue) return;
        // Keep the merge/target math on the same trim.
        _pageMinX = minX;


        if (GridDebug)
        {
            Console.Error.WriteLine($"[grid] cell={_pageCellWidth:R} minX={minX:F2} lines={_pageLineStarts.Count} rotDom={_pageRotDominant}");
            foreach (var (off, x) in _pageLineStarts)
            {
                var end = Math.Min(_text.Length, off + 30);
                var snippet = _text.ToString(off, Math.Max(0, end - off)).Replace("\r", "").Replace("\n", "");
                Console.Error.WriteLine($"[grid]   off={off} x={x:F2} n={LineStartGridCol(x, minX) - LineStartGridCol(minX, minX)} '{snippet}'");
            }
        }

        for (var i = _pageLineStarts.Count - 1; i >= 0; i--)
        {
            var (off, x) = _pageLineStarts[i];
            if (off < pageTextStart || off > _text.Length) continue;
            if (!InBand(x)) continue;
            // Absolute grid: pad = floor(x/cell) − floor(minX/cell)
            // — boundaries at k·cell anchored at the page's left edge, floor
            // quantisation, no rounding phase (see the puremode-grid spec note).
            var n = LineStartGridCol(x, minX) - LineStartGridCol(minX, minX);
            if (n <= 0) continue;
            if (n > 5000) n = 5000; // grid bound (_maxCols)
            _text.Insert(off, new string(' ', n));
            // Keep the recorded offsets valid — SortLinesByY maps them back to
            // lines for the grid-aware same-row merge.
            for (var j = 0; j < _pageLineStarts.Count; j++)
                if (_pageLineStarts[j].offset > off)
                    _pageLineStarts[j] = (_pageLineStarts[j].offset + n, _pageLineStarts[j].x);
            // Run spans shift too; a span starting AT the insertion point moves right
            // (the pad is inserted before it).
            for (var j = 0; j < _pageRunSpans.Count; j++)
                if (_pageRunSpans[j].Offset >= off)
                    _pageRunSpans[j] = _pageRunSpans[j] with { Offset = _pageRunSpans[j].Offset + n };
        }
    }

    /// <summary>
    /// Number of spaces to pad before a run under the Pure-mode character grid: the run is
    /// placed at absolute column round((runPageX − lineStartX) / cellWidth), and we pad from
    /// the number of characters already emitted on the line. A real gap always yields at
    /// least one space; below the word-gap threshold the run is adjacent (no space).
    /// </summary>
    /// <summary>True when the current output line already carries fullwidth
    /// (CJK) glyphs — the one case where the emitted character count falls
    /// behind the device column (each glyph covers ~2 grid cells).</summary>
    /// <summary>True when the current output tail (last ~8 non-space chars)
    /// carries an RTL character — the same lookback the TJ backjump rule uses.</summary>
    private bool RecentTextIsRtl()
    {
        var seen = 0;
        for (var i = _text.Length - 1; i >= 0 && seen < 8; i--)
        {
            var c = _text[i];
            if (c == '\n' || c == '\r') return false;
            if (c == ' ') continue;
            if (BidiReorderer.IsRtlChar(c)) return true;
            seen++;
        }
        return false;
    }

    private int ColumnSpaces(double gap, double threshold, double runPageX, int extraChars = 0,
        int minPad = 1)
    {
        if (gap <= threshold) return 0;
        int targetCol, outputCol;
        if (!double.IsNaN(_pageMinX))
        {
            // Page-absolute grid, quantised by floor against boundaries at k·cell
            // (col = floor(x/cell) − floor(minX/cell), no
            // rounding phase) — the same mapping the leading-pad insertion uses,
            // so target and output columns share one frame.
            targetCol = LineStartGridCol(runPageX, _pageMinX) - LineStartGridCol(_pageMinX, _pageMinX);
            var leadCols = LineStartGridCol(_lineStartPageX, _pageMinX) - LineStartGridCol(_pageMinX, _pageMinX);
            if (leadCols < 0) leadCols = 0;
            outputCol = leadCols + (_text.Length - _lineStartTextOffset) + extraChars;
        }
        else
        {
            targetCol = (int)Math.Round((runPageX - _lineStartPageX) / _pageCellWidth);
            outputCol = _text.Length - _lineStartTextOffset + extraChars;
        }
        if (GridDebug)
            Console.Error.WriteLine($"[cols] target={targetCol} output={outputCol} cell={_pageCellWidth:R} runPageX={runPageX:F1} lineStartX={_lineStartPageX:F1} minX={_pageMinX:F1}");
        int spaces = targetCol - outputCol;
        if (spaces < minPad) spaces = minPad;
        if (spaces > 5000) spaces = 5000; // grid bound (_maxCols)
        return spaces;
    }

    private int ComputeSpaceCount(double gap, double threshold, double fontSize)
    {
        // MemorySaving: ONE separator space per run boundary
        // whose pen jumped — forward column gap or BACKWARD overlap alike (a table
        // cell's unclipped text overruns its neighbour, and the next cell still
        // reads as a separate word).
        if (ExtractionOptions?.FormattingMode == TextExtractionOptions.TextFormattingMode.MemorySaving)
            return Math.Abs(gap) > threshold ? 1 : 0;
        if (gap <= threshold) return 0;
        if (ExtractionOptions?.FormattingMode != TextExtractionOptions.TextFormattingMode.Raw)
        {
            // Pure mode: one space per ~0.217 * fontSize of gap width
            // (the Pure-mode column-spacing rule).
            var spaceWidth = Math.Max(fontSize * 0.217, 0.5);
            var count = (int)Math.Round(gap / spaceWidth);
            if (count < 1) count = 1;
            if (count > 40) count = 40;
            return count;
        }
        return 1;
    }
}

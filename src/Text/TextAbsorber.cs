using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

/// <summary>
/// Extracts text from PDF pages by parsing content streams.
/// </summary>
public sealed partial class TextAbsorber
{
    private static readonly bool GridDebug =
        Environment.GetEnvironmentVariable("ASPOSE_FOSS_GRIDDEBUG") == "1";

    private readonly StringBuilder _text = new();

    // Track Y positions for each text line to enable visual-order sorting.
    // Each entry corresponds to a line boundary in _text.
    private readonly List<double> _lineYPositions = new();

    // Per-line page-space start X (leftmost run) and effective font size (Tf size ×
    // text-matrix scale × CTM scale), recorded in lockstep with _lineYPositions.
    // X orders same-row segments left-to-right when lines merge; the effective font
    // size scales the row-merge tolerance (a label/value pair whose baselines differ
    // by less than ~half the smaller font share a visual row).
    private readonly List<double> _lineXPositions = new();

    private readonly List<double> _lineFontSizes = new();

    // Whether every run on the line so far was a minority-rotated run (upright-
    // dominant page). Such lines take an UPWARD box [y, y+fs] in row formation
    // instead of the upright descender box.
    private readonly List<bool> _lineIsRotated = new();

    private bool _currentLineIsRotated;

    // Per-line font descent MAGNITUDE as a fraction of the font size (0.2 when
    // unknown). The row-formation line box is anchored on the line's TRUE
    // descent line — bottom = baseline − descent·fs — not a fixed 0.2 em
    // (holds to ±0.004pt across Helvetica/Times/Courier and a
    // descriptor-override fixture; a deep-descent font drops its box enough
    // to release a small label riding above a large-font row).
    private readonly List<double> _lineDescents = new();

    private double _currentLineDescent = 0.2;

    // Device-space baseline Y of the line's first minority-rotated run: row
    // placement for such lines works in the page frame (box [y, y+fs] up from
    // the anchor), not in the rotated-projection frame the internal line
    // tracking uses.
    private double _currentLineDevY = double.NaN;

    private double _currentLineEffFs = double.NaN;

    // Leftmost PAGE-space X of the line being built (full axis-aligned CTM applied —
    // scale AND translation, unlike the grid's _lineStartPageX which is calibrated to
    // translation-only). _rowXLineOffset detects line changes like TrackLineStart.
    private double _currentLineRowX = double.NaN;

    private int _rowXLineOffset = -1;

    /// <summary>Track the current line's leftmost page-space X for row ordering.
    /// Call before appending the run's text.</summary>
    private void TrackRowX(double rowX)
    {
        if (double.IsNaN(rowX)) return;
        int ls = _text.Length;
        while (ls > 0 && _text[ls - 1] != '\n') ls--;
        if (ls != _rowXLineOffset) { _rowXLineOffset = ls; _currentLineRowX = rowX; }
        else if (double.IsNaN(_currentLineRowX) || rowX < _currentLineRowX) _currentLineRowX = rowX;
    }

    private double _currentLineY = double.NaN;

    // Page-space Y offset (accumulated CTM translation, localCmTy) in effect when the
    // current line's Y was established. _currentLineY is tracked in TEXT space (so it can
    // be compared against text-space refs for line-break decisions), but line SORTING needs
    // absolute page-space Y — so RecordLineY stores (_currentLineY + _currentLineCmTy).
    // Without this, text drawn inside a translated Form XObject (e.g. a widget appearance
    // placed by a `cm` translation) sorts by its local Tm Y instead of its page position.
    private double _currentLineCmTy;

    // Counts text-showing operators (Tj/TJ/'/") seen while extracting the current
    // page (including nested Form XObjects). Used to emit the "no text operators"
    // diagnostic when TextSearchOptions.LogTextExtractionErrors is enabled.
    private int _textShowingOpCount;

    // ── Pure-mode character-grid layout ─────────────────────────────────────
    // Pure mode lays runs out on a per-page character grid (pdftotext -layout style)
    // rather than emitting round(gap / spaceWidth) spaces per inter-run gap. Each run is
    // placed at an absolute character column = round((runPageX − lineStartX) / cellWidth),
    // and spaces pad from the current output column. This produces
    // fixed-column spacing (a long label eats its column budget → 1 space; a short label →
    // many spaces up to the shared value column) where a per-gap divisor cannot.
    private double _pageCellWidth;       // page grid cell width (≈ mean glyph advance); 0 = disabled

    // Ceiled dominant device font size from the page pre-scan (blank-row thresholds).
    private double _pageDominantFs;

    // Grid origin: the page's MediaBox left edge. The absolute column grid is
    // anchored at the PAGE edge, not at coordinate 0 — a shifted MediaBox
    // (engineering sheets placing the origin mid-page) shifts every column
    // boundary with it. 0 for ordinary pages.
    private double _pageGridOriginX;

    // True when the page's text is predominantly sideways (rotation in the
    // composed matrix). The grid then lives in the READING frame (projected
    // coordinates). On an upright-dominant page a minority rotated run (edge
    // annotations, vertical form labels) instead grids at its DEVICE x — the
    // horizontal position of its vertical baseline.
    private bool _pageRotDominant;

    // True when this page inserted inter-run gap spaces on some line — the signal
    // that it has a column layout worth padding to a fixed width (see Visit(Document)).
    private bool _sawIntraLineGapSpaces;

    private double _lineStartPageX = double.NaN; // page-space X of the current line's first run

    private int _lineStartTextOffset;    // _text offset where the current line's text begins

    // ── hOCR searchable-overlay reconstruction ──────────────────────────────
    // A page produced by Document.Convert(hOCR) carries every recognised word as its
    // own invisible (Tr 3) BT/Td/Tj block. The streaming extractor would emit one word
    // per line; instead we collect those invisible runs and rebuild the page on a
    // page-absolute character grid (leading + column spacing + blank rows), matching
    // the expected Pure-mode OCR layout. Only engaged in Pure mode when the
    // page is dominated by such runs (see RebuildOcrOverlayPage), so normal PDFs are
    // untouched.
    private readonly List<(string text, double x, double y, double fs, double width)> _ocrRuns = new();

    private bool _collectOcrRuns;

    // Runs collected from Type3 /ActualText spans (Figma exports): the page grid
    // rebuild also engages when THESE dominate the page's show ops.
    private int _type3SpanRuns;

    // Type3 /Widths cache per font dict: FirstChar + per-code advances scaled by
    // FontMatrix[0] into text space (identity matrix for Figma's fonts, 0.001 for
    // classic Type3). FontMetrics has no Type3 support, and the 0.5-em fallback
    // overestimates these runs so badly the grid rebuild glued sidebar columns.
    private readonly Dictionary<PdfDictionary, (int first, double[] w)?> _type3Widths =
        new(ReferenceEqualityComparer.Instance);

    private double Type3Advance(byte[] codes, PdfDictionary fontDict, PdfReader reader, double fontSize)
    {
        if (!_type3Widths.TryGetValue(fontDict, out var entry))
        {
            entry = null;
            if (reader.Resolve(fontDict.Get("Widths")) is PdfArray wa && wa.Count > 0)
            {
                var first = reader.Resolve(fontDict.Get("FirstChar")) is PdfInteger fi ? (int)fi.Value : 0;
                var scale = 1.0;
                if (reader.Resolve(fontDict.Get("FontMatrix")) is PdfArray fm && fm.Count == 6
                    && GetNumber(fm[0]) != 0)
                    scale = GetNumber(fm[0]);
                var w = new double[wa.Count];
                for (var i = 0; i < wa.Count; i++) w[i] = GetNumber(wa[i]) * scale;
                entry = (first, w);
            }
            _type3Widths[fontDict] = entry;
        }
        if (entry is not { } e) return -1;
        double adv = 0;
        foreach (var b in codes)
        {
            var idx = b - e.first;
            adv += idx >= 0 && idx < e.w.Length ? e.w[idx] : 0.5;
        }
        return adv * fontSize;
    }

    private bool _anyReconstructed;       // a page was rebuilt → preserve its leading spaces

    // ── document tail / line-end reconstruction (TextFragmentAbsorber replay) ──
    // The public Text getter trims the trailing newline sentinels, but
    // TextFragmentAbsorber.Text keeps one "\r\n" per textless line
    // advance after the document's last glyph (a source text ending "…\r\n\r\n"
    // round-trips through T* T* back to "…\r\n\r\n"). Count those advances here
    // so the fragment absorber's replay can re-append them after the trim.
    internal int TrailingBlankRows;

    private int _breaksAfterLastGlyph;    // streaming line breaks since the last show op

    private int _lastBreakPos = -1;       // _text.Length right after the last streaming break

    private int _lastShowStart = -1;      // span of the most recent show-derived append

    private int _lastShowEnd = -1;

    // The most recent show-derived append drew a TAB glyph (flattened to a space
    // in the output): the following inter-run gap is a column hole even though
    // the text tail is already a space.
    private bool _prevShowHadTab;

    // Pure mode trims every space run sitting before a line break (grid pads,
    // gap-synthesised separators) — but a space GLYPH drawn inside a show run
    // that also has visible glyphs is real content that is kept
    // ("Links \r\n"). At break time such spaces are masked with a private-use
    // sentinel so TrimTrailingLineSpaces passes over them, then restored.
    // All-space shows (a lone space fragment at the row's far right) stay
    // trimmed.
    private bool _maskEolShowSpaces;

    private const char EolShowSpaceSentinel = '';

    /// <summary>Append show-derived text (decoded glyphs / ActualText), tracking
    /// its span for the line-end glyph-space and trailing-blank-row bookkeeping.</summary>
    private void AppendShowText(string s)
    {
        if (s.Length == 0) return;
        // Contiguous show appends (one line assembled from several ops) count as
        // one span; anything synthesised in between (gap spaces, a break) starts
        // a fresh span at the current tail.
        if (_lastShowEnd != _text.Length) _lastShowStart = _text.Length;
        // A drawn TAB glyph (a ToUnicode 0009 destination) is a column stop, not a
        // word space: it flattens to a space in the output, and the flag lets the
        // next run's gap logic pad past the trailing-space suppressor — the tab
        // marks the hole as structural.
        _prevShowHadTab = s.IndexOf('\t') >= 0;
        if (_prevShowHadTab) s = s.Replace('\t', ' ');
        _text.Append(s);
        _lastShowEnd = _text.Length;
    }

    /// <summary>Append a streaming line break ("\r\n" from Td/TD/T*/Tm/'/" positioning),
    /// masking a real glyph space at the finished line's tail and keeping the
    /// count of breaks that follow the page's last glyph.</summary>
    private void AppendStreamBreak()
    {
        if (_lastShowEnd > _lastBreakPos) _breaksAfterLastGlyph = 0;
        MaskTrailingShowSpaces();
        _text.Append("\r\n");
        _breaksAfterLastGlyph++;
        _lastBreakPos = _text.Length;
    }

    private void MaskTrailingShowSpaces()
    {
        if (!_maskEolShowSpaces) return;
        // The buffer tail must be the most recent show's own text — anything
        // appended after it (gap spaces, a break) means no glyph space is at risk.
        if (_lastShowEnd != _text.Length || _lastShowStart < 0) return;
        var i = _text.Length;
        while (i > _lastShowStart && _text[i - 1] == ' ') i--;
        if (i == _text.Length) return;      // no trailing space in the show
        if (i == _lastShowStart) return;    // all-space show: trimmed
        // Only a SINGLE trailing space glyph is typographic content that is
        // kept ("Links \r\n"); a run of them is drawn layout padding
        // trimmed like the synthesised pads (a table row ending "€     " loses
        // all five).
        if (_text.Length - i != 1) return;
        _text[i] = EolShowSpaceSentinel;
    }

    private void RestoreEolShowSpaces(int textStart)
    {
        for (var i = textStart; i < _text.Length; i++)
            if (_text[i] == EolShowSpaceSentinel) _text[i] = ' ';
    }

    /// <summary>
    /// The extracted text after calling Visit(). Full Trim() is applied to
    /// strip both trailing newline sentinels emitted by the extraction loop
    /// and any spurious trailing spaces from gap-detection between BT/ET blocks.
    /// </summary>
    // A reconstructed OCR page carries meaningful leading spaces on its first line
    // (page-absolute grid), so only trailing newlines are stripped in that case.
    // Raw mode trims leading whitespace (no empty feeds above
    // a clipped rectangle) but keeps the source stream's own TRAILING space
    // glyphs — a document whose last show is a space extracts with it kept.
    public string Text => _anyReconstructed
        ? _text.ToString().TrimEnd('\r', '\n')
        : ExtractionOptions?.FormattingMode == TextExtractionOptions.TextFormattingMode.Raw
            ? _text.ToString().TrimStart().TrimEnd('\r', '\n')
            // A rectangle-clipped window keeps the source's own trailing space
            // glyphs on the final line (the windowed output ends
            // with them); only line sentinels are stripped.
            : TextSearchOptions?.Rectangle is not null
                ? _text.ToString().TrimStart().TrimEnd('\r', '\n')
                // Pure keeps a first line's leading grid pad (a page whose top
                // row starts deep in the grid pads to its column, like every
                // other line); only line sentinels trim.
                : _text.ToString().Trim('\r', '\n');

    /// <summary>
    /// The extracted text with only trailing \r\n stripped (preserving trailing
    /// spaces from the source glyph stream). Used by LowCode extractors that
    /// need exact byte parity with the content stream.
    /// </summary>
    internal string RawText => _text.ToString().TrimEnd('\r', '\n');

    /// <summary>
    /// Gets or sets the text extraction options.
    /// </summary>
    public TextExtractionOptions ExtractionOptions { get; set; } = new TextExtractionOptions();

    /// <summary>
    /// Gets or sets the text search options used during extraction.
    /// </summary>
    public TextSearchOptions TextSearchOptions { get; set; } = new TextSearchOptions();

    /// <summary>
    /// Initializes a new TextAbsorber with default settings.
    /// </summary>
    public TextAbsorber() { }

    /// <summary>
    /// Initializes a new TextAbsorber with the specified extraction options.
    /// </summary>
    public TextAbsorber(TextExtractionOptions extractionOptions)
    {
        ExtractionOptions = extractionOptions ?? new TextExtractionOptions();
        TextSearchOptions.IgnoreResourceFontErrors |= ExtractionOptions.IgnoreResourceFontErrors;
    }

    /// <summary>Initializes with text-search options.</summary>
    public TextAbsorber(TextSearchOptions textSearchOptions)
    {
        TextSearchOptions = textSearchOptions ?? new TextSearchOptions();
    }

    /// <summary>Initializes with both extraction and search options.</summary>
    public TextAbsorber(TextExtractionOptions extractionOptions, TextSearchOptions textSearchOptions)
    {
        ExtractionOptions = extractionOptions ?? new TextExtractionOptions();
        TextSearchOptions = textSearchOptions ?? new TextSearchOptions();
        TextSearchOptions.IgnoreResourceFontErrors |= ExtractionOptions.IgnoreResourceFontErrors;
    }

    /// <summary>Errors recorded during extraction.</summary>
    public List<TextExtractionError> Errors { get; } = new();

    /// <summary>Whether any extraction error was recorded.</summary>
    public bool HasErrors => Errors.Count > 0;

    /// <summary>
    /// Extract text from a single page.
    /// </summary>
    public void Visit(Page page)
    {
        var reader = GetReader(page);
        var contentStreams = GetContentStreams(page, reader);

        // Pages joined through repeated Visit calls separate with a line
        // break — page N+1's first row never continues page N's last row.
        if (_text.Length > 0 && _text[^1] != '\n')
            _text.Append("\r\n");

        // Track starting positions for this page
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
        _textShowingOpCount = 0;
        _currentPageNumber = page.Number;

        // Pure mode: size the per-page character grid up front (see the column-model note
        // on the fields). Raw and MemorySaving modes keep the single-space-per-gap
        // behaviour (cellWidth 0) — MemorySaving output separates
        // column-gapped runs with ONE space, never grid pads.
        var pureLayout = ExtractionOptions?.FormattingMode is not TextExtractionOptions.TextFormattingMode.Raw
            and not TextExtractionOptions.TextFormattingMode.MemorySaving;
        // Line-end glyph-space masking only applies where TrimTrailingLineSpaces
        // runs (full-page Pure); Raw keeps whitespace verbatim and rect-clipped
        // extraction keeps clipped-run edges untouched.
        _maskEolShowSpaces = pureLayout && TextSearchOptions?.Rectangle is null;
        _breaksAfterLastGlyph = 0;
        _lastBreakPos = _text.Length;
        _lastShowStart = -1;
        _lastShowEnd = -1;
        _pageHeightForRows = page.Height;
        _pageHasRotatedText = false;
        // The grid anchors at coordinate x = 0 regardless of the MediaBox
        // (a shifted MediaBox does not move the column
        // boundaries); content at negative X can't occupy a column at all
        // and is dropped from Pure output (see the show-op guard).
        _pageGridOriginX = 0;
        (_pageCellWidth, _pageCellCeil, _pageMinX, _pageDominantFs, _pageRotDominant) = pureLayout
            ? EstimatePageGrid(contentStreams, page.Dict, reader,
                ExtractionOptions?.ScaleFactor ?? 1.0)
            : (0, 0, double.NaN, 0, false);
        // A clip rectangle re-anchors extraction to the window, not the page:
        // page-absolute columns/leading pads don't apply (the
        // rect-clipped output starts lines at the window edge).
        if (TextSearchOptions?.Rectangle is not null)
        {
            _pageMinX = double.NaN;
            // Rect-clipped extraction uses the exact ceiled-bucket cell (see
            // EstimatePageGrid note).
            if (_pageCellCeil > 0) _pageCellWidth = _pageCellCeil;
        }
        // The caller's rectangle is in VIEWER coordinates (the page as displayed,
        // after /Rotate); content-stream positions are in media coordinates. Map
        // the window through the inverse page rotation so the filters compare
        // like with like.
        _effectiveSearchRect = MapViewerRectToMedia(TextSearchOptions?.Rectangle, page);
        _lineStartPageX = double.NaN;
        _lineStartTextOffset = _text.Length;
        _sawIntraLineGapSpaces = false;
        _collectOcrRuns = pureLayout;
        _ocrRuns.Clear();
        _type3SpanRuns = 0;
        _pageLineStarts.Clear();
        _pageRunSpans.Clear();

        // A page's content streams concatenate into ONE logical stream sharing one
        // graphics/text state (ISO 32000-1 §7.8.2). Parsing them separately reset the
        // whole text matrix at every boundary — a producer that splits mid-text-object
        // (Acrobat touch-up) had its post-boundary lines tracked at raw Td offsets
        // (tmY ≈ −1.2 instead of the page Y), so the search-rectangle and page-bounds
        // filters dropped them. Join with newline separators and parse once.
        var combined = CombineContentStreams(contentStreams);
        ExtractTextFromContentStream(combined, page.Dict, reader);

        // A page that is an hOCR searchable overlay (many invisible single-word blocks)
        // is rebuilt on a page-absolute grid instead of the streaming per-word lines.
        if (_collectOcrRuns && RebuildOcrOverlayPage(textStart))
        {
            // The rebuild replaced the streamed text (with any masked spaces in it);
            // its grid rows have no trailing textless advances to replay.
            TrailingBlankRows = 0;
            return;
        }

        // Pure-mode leading columns from the page-absolute grid origin
        // (not for rect-clipped extraction — see the _pageMinX note above).
        if (pureLayout && TextSearchOptions?.Rectangle is null)
            InsertLeadingGridSpaces(textStart);

        // The page end closes the final line the way a streaming break would:
        // a single trailing space GLYPH in its show is typographic content and
        // gets the same sentinel protection from the trailing trim below.
        MaskTrailingShowSpaces();

        // Sort this page's text lines by visual order (Y coordinate, top to bottom)
        SortLinesByY(textStart, yStart);

        // Pure mode lays lines on the character grid but never leaves padding at a
        // line's right edge: Pure output has no trailing spaces on
        // any line (a trailing space fragment drawn at the row's far right would
        // otherwise leave one). Raw mode keeps source whitespace verbatim, and
        // rect-clipped extraction keeps clipped-run edges (the windowed
        // output ends lines with the source spaces).
        if (pureLayout && TextSearchOptions?.Rectangle is null)
            TrimTrailingLineSpaces(textStart);
        // Unmask the real glyph spaces the trim was steered around, and record
        // the page's trailing textless advances (a show op after the last break
        // means the page ends in glyphs — nothing to replay).
        RestoreEolShowSpaces(textStart);
        TrailingBlankRows = _lastShowEnd < 0 || _lastShowEnd > _lastBreakPos
            ? 0 : _breaksAfterLastGlyph;

        // Diagnostic: a page that draws only images/graphics has no text-showing
        // operators. When the caller opted into error logging, surface this as a
        // recorded extraction error.
        if ((TextSearchOptions?.LogTextExtractionErrors ?? false) && _textShowingOpCount == 0)
        {
            const string msg = "Text showing operators aren't found on the page.";
            Errors.Add(new TextExtractionError
            {
                PageIndex = page.Number,
                Message = msg,
                Description = msg,
                Summary = msg,
                Location = new TextExtractionErrorLocation { PageNumber = page.Number },
            });
        }
    }

    // Glue threshold: two words are joined with no space when the gap between the
    // previous word's rendered advance end and this word's start is under 0.4 of a
    // space glyph's width at this font size (a wide width-fit word can overshoot its
    // box into the next — they render touching). Font-relative,
    // so the same 1.14pt gap glues at fs13 but keeps a space at fs7.
    private const double OcrGlueSpaceFraction = 0.4;

    private const double HelveticaSpaceEm = 0.278; // space advance, em units

    /// <summary>
    /// Rebuild an hOCR searchable-overlay page from its collected invisible runs onto a
    /// page-absolute character grid: line grouping by baseline, leading + column spacing
    /// (cell = 0.6·(F−2), F = dominant-by-char font size, origin = leftmost run), glyph
    /// glue on rendered overlap, and blank rows for vertical gaps
    /// (blanks = min(2, ceil(gap / (2·fontSize)) − 1)). Returns false — leaving the
    /// streaming output intact — unless the page is clearly such an overlay.
    /// </summary>
    private bool RebuildOcrOverlayPage(int textStart)
    {
        // Only an OCR searchable overlay qualifies: many runs, and the invisible runs
        // must be essentially ALL of the page's text (a normal page with a little
        // invisible text must keep its streamed, visible-text output).
        if (GridDebug)
            Console.Error.WriteLine($"[ocr] runs={_ocrRuns.Count} shows={_textShowingOpCount} textLen={_text.Length - textStart}");
        if (_ocrRuns.Count < 25) return false;
        if (_ocrRuns.Count < 0.9 * Math.Max(_ocrRuns.Count, _textShowingOpCount)) return false;

        // Group runs into visual lines. Runs are taken top-to-bottom; one joins the
        // current line when its baseline is within a font-relative tolerance of the
        // line's top run — so a giant glyph (a "/" many times the text size) still
        // joins its line, while the next text line, a full leading below, does not.
        var ordered = new List<(string text, double x, double y, double fs, double width)>(_ocrRuns);
        ordered.Sort((a, b) =>
        {
            var cy = b.y.CompareTo(a.y); // top of page first
            return cy != 0 ? cy : a.x.CompareTo(b.x);
        });
        var lines = new List<List<(string text, double x, double fs, double width, double y)>>();
        foreach (var r in ordered)
        {
            if (lines.Count > 0)
            {
                // Tolerance scales with the INCOMING run's own font: a big glyph (a "/")
                // reaches up to join a small-text line, but a small word will not reach up
                // to a big-font line above it (which would merge two distinct rows).
                var cur = lines[^1];
                if (cur[0].y - r.y < 0.4 * r.fs)
                {
                    cur.Add((r.text, r.x, r.fs, r.width, r.y));
                    continue;
                }
            }
            lines.Add(new List<(string, double, double, double, double)> { (r.text, r.x, r.fs, r.width, r.y) });
        }
        if (lines.Count < 3) return false;

        // Per line: baseline = median glyph baseline; bottom = deepest glyph (lowest y).
        var baseline = new double[lines.Count];
        var bottom = new double[lines.Count];
        for (int li = 0; li < lines.Count; li++)
        {
            var ys = new List<double>(lines[li].Count);
            foreach (var w in lines[li]) ys.Add(w.y);
            ys.Sort();
            baseline[li] = ys[ys.Count / 2];
            bottom[li] = ys[0]; // smallest page-space y = deepest point
        }

        // Grid geometry: cell from the dominant-by-char font size; origin at leftmost run.
        double minX = double.MaxValue;
        var charByFs = new Dictionary<int, int>();
        foreach (var r in _ocrRuns)
        {
            if (r.x < minX) minX = r.x;
            int f = (int)Math.Round(r.fs);
            charByFs.TryGetValue(f, out var c);
            charByFs[f] = c + r.text.Length;
        }
        int fdom = 0, bestChars = -1;
        foreach (var kv in charByFs)
            if (kv.Value > bestChars || (kv.Value == bestChars && kv.Key < fdom))
            { bestChars = kv.Value; fdom = kv.Key; }
        double cell = 0.6 * (fdom - 2);
        if (cell <= 0) return false;

        var sb = new StringBuilder();
        for (int li = 0; li < lines.Count; li++)
        {
            var ws = new List<(string text, double x, double fs, double width)>(lines[li].Count);
            foreach (var w in lines[li]) ws.Add((w.text, w.x, w.fs, w.width));
            ws.Sort((a, b) => a.x.CompareTo(b.x));
            if (li > 0)
            {
                // Vertical gap is measured from the PREVIOUS line's bottom (deepest glyph)
                // to THIS line's baseline — an asymmetry that lets a tall descender (a "/")
                // push the following line apart without affecting the line above it. Blank
                // rows scale with the smallest baseline-sitting font on this line (unit =
                // 2× that size), capped at 2; dashes are centred, not baseline glyphs.
                double gap = bottom[li - 1] - baseline[li];
                double fsMin = double.MaxValue;
                foreach (var w in ws)
                    if (w.fs > 0 && w.fs < fsMin && !IsDashOnly(w.text)) fsMin = w.fs;
                if (fsMin == double.MaxValue)
                    foreach (var w in ws) if (w.fs > 0 && w.fs < fsMin) fsMin = w.fs;
                int blanks = 0;
                if (fsMin is > 0 and < double.MaxValue)
                    blanks = Math.Min(2, Math.Max(0, (int)Math.Ceiling(gap / (2.0 * fsMin)) - 1));
                for (int k = 0; k <= blanks; k++) sb.Append("\r\n");
            }
            RenderOcrLine(sb, ws, minX, cell);
        }

        // Replace this page's streamed segment with the reconstruction (keeping any
        // earlier pages' text intact for multi-page documents).
        _text.Remove(textStart, _text.Length - textStart);
        _text.Append(sb);
        _anyReconstructed = true;
        return true;
    }

    /// <summary>Emit one grid line: leading spaces to the first run's column, then each
    /// run padded to its column (or glued when the previous run's rendered advance
    /// overlaps it).</summary>
    private static void RenderOcrLine(StringBuilder sb, List<(string text, double x, double fs, double width)> ws,
        double minX, double cell)
    {
        int outlen = 0;
        double prevPen = double.NaN;
        for (int i = 0; i < ws.Count; i++)
        {
            var (text, x, fs, width) = ws[i];
            int target = (int)Math.Round((x - minX) / cell - 0.32);
            int sp;
            if (i == 0)
                sp = Math.Max(0, target);
            else if (x - prevPen < OcrGlueSpaceFraction * HelveticaSpaceEm * fs)
                sp = 0;
            else
                sp = Math.Max(1, target - outlen);
            for (int s = 0; s < sp; s++) sb.Append(' ');
            sb.Append(text);
            outlen += sp + text.Length;
            prevPen = x + width;
        }
    }

    /// <summary>True when the run is only dash/hyphen characters (vertically centred
    /// glyphs that don't establish a text baseline).</summary>
    private static bool IsDashOnly(string s)
    {
        if (s.Length == 0) return false;
        foreach (var c in s)
            if (c is not ('-' or '‐' or '‑' or '‒' or '–' or '—'))
                return false;
        return true;
    }

    /// <summary>
    /// Record the Y position of the current line (before emitting a newline).
    /// </summary>
    private void RecordLineY()
    {
        // Always record — an unknown Y goes in as NaN so the line↔Y pairing in
        // SortLinesByY stays 1:1 with emitted newlines (a skipped entry used to
        // shift every later line onto its neighbour's Y).
        _lineYPositions.Add(_currentLineIsRotated && !double.IsNaN(_currentLineDevY)
            ? _currentLineDevY
            : double.IsNaN(_currentLineY) ? double.NaN : _currentLineY + _currentLineCmTy);
        _currentLineDevY = double.NaN;
        // The finished line's start X is only valid when the row tracker actually
        // saw a run on THIS line (offsets match); otherwise it belongs to an older line.
        int lsRec = _text.Length;
        while (lsRec > 0 && _text[lsRec - 1] != '\n') lsRec--;
        _lineXPositions.Add(lsRec == _rowXLineOffset ? _currentLineRowX : double.NaN);
        _lineFontSizes.Add(_currentLineEffFs);
        _lineIsRotated.Add(_currentLineIsRotated);
        _lineDescents.Add(_currentLineDescent);
    }

    /// <summary>Remove space runs sitting immediately before a line break (and at the very
    /// end of the page's segment), from <paramref name="textStart"/> onward. Backward walk
    /// so removals don't disturb positions still to visit.</summary>
    private void TrimTrailingLineSpaces(int textStart)
    {
        // RTL lines keep their pad at the LOGICAL END instead: the visual-left
        // grid pad (leading in LTR emission order) belongs after the last logical
        // word, so rotate a leading space run to the tail rather than trimming.
        void HandleLine(int lineStart, int end)
        {
            var s = end;
            while (s > lineStart && _text[s - 1] == ' ') s--;
            // A line of ONLY spaces is a pad line (placeholder space glyphs
            // laid out on the grid) — it is kept at full width;
            // only lines with visible content trim their right edge.
            if (s == lineStart && end > lineStart) return;
            int rtl = 0, ltr = 0;
            for (var k = lineStart; k < s; k++)
            {
                var ch = _text[k];
                if (BidiReorderer.IsRtlChar(ch)) rtl++;
                else if (char.IsLetter(ch)) ltr++;
            }
            if (rtl > ltr && rtl > 0)
            {
                var lead = 0;
                while (lineStart + lead < s && _text[lineStart + lead] == ' ') lead++;
                if (lead > 0)
                {
                    _text.Remove(lineStart, lead);
                    _text.Insert(end - lead, new string(' ', lead));
                }
                return; // keep the (rotated + existing) trailing pad
            }
            if (s < end) _text.Remove(s, end - s);
        }

        // Line start offsets, walked backwards so removals keep earlier offsets valid.
        var end0 = _text.Length;
        for (var pos = _text.Length - 1; pos >= textStart; pos--)
        {
            if (_text[pos] != '\n') continue;
            var lineEnd = end0;
            HandleLine(pos + 1, lineEnd);
            end0 = pos > textStart && _text[pos - 1] == '\r' ? pos - 1 : pos;
            pos = end0; // resume before this line's terminator
        }
        HandleLine(textStart, end0);
    }

    /// <summary>Adjustment added to the raw text-space line Y so that RecordLineY stores a
    /// sortable page-space Y. Inside a Form XObject (depth > 0) it is the accumulated CTM Y
    /// translation (see the Tm-handler note). At page level it is normally 0 — byte-identical
    /// to the plain extraction path — except under a FLIPPED page CTM ("1 0 0 -1 0 H cm",
    /// text-space Y growing downward), where raw Ys would sort bottom-up: there the stored
    /// sum becomes the device Y (cmD·rawY + cmTy).</summary>
    private static double LineCmAdjust(int depth, double cmD, double cmTy, double rawY)
    {
        // Inside a Form XObject (depth > 0) only the accumulated CTM TRANSLATION
        // applies — widget appearances are placed by translate-only cms, and a
        // scaled inner cm (fit-to-rect appearance) must not warp the recorded Y
        // (flattened-field values would sort against wrong baselines).
        if (depth > 0) return cmTy;
        // Page level: store the DEVICE Y cmD*rawY + cmTy (as an adjustment added
        // to rawY). Identity CTM keeps this at cmTy so a page that positions each
        // paragraph block via a cm translation (generated docs) still separates
        // its blocks' lines.
        if (double.IsNaN(rawY)) return cmTy;
        return (cmD - 1.0) * rawY + cmTy;
    }

    /// <summary>
    /// Sort recently extracted text lines by Y coordinate (top-to-bottom visual order).
    /// Only sorts text added after startOffset.
    /// </summary>
    // In-order pages merge only near-identical baselines (stream-broken co-row
    // segments); rows drawn a few points apart keep their own lines, preserving
    // the stream-shaped output (e.g. label-above-value report layouts).
    private const double InOrderRowTol = 1.0;

    /// <summary>Row-merge tolerance: the legacy 1%-of-Y rule CAPPED at half the ANCHOR
    /// line's effective font size — a row reaches at most half its (topmost segment's)
    /// font size downward. The font cap only ever TIGHTENS the legacy rule (small print
    /// merges less), never widens it.</summary>
    private double RowMergeTol(double y, double anchorFs)
    {
        // The percentage term normally measures against the line's own |y|; on a
        // page with SIDEWAYS text the projected line coordinates run near zero,
        // where 1%*|y| collapses below real inter-baseline distances (mixed-size
        // header rows sit ~2.7pt apart and still merge) - use
        // the page height there instead.
        var extent = _pageHasRotatedText && _pageHeightForRows > 0 ? _pageHeightForRows : Math.Abs(y);
        var legacy = Math.Max(2.0, extent * 0.01);
        if (!double.IsNaN(anchorFs) && anchorFs > 0) return Math.Min(legacy, 0.5 * anchorFs);
        return legacy;
    }

    private double _pageHeightForRows;

    private double _pageCellCeil;

    private bool _pageHasRotatedText;
}

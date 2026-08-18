using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

/// <summary>
/// Searches for text fragments on PDF pages, optionally matching a search phrase.
/// </summary>
public sealed partial class TextFragmentAbsorber
{
    private static readonly bool SearchDebug =
        Environment.GetEnvironmentVariable("ASPOSE_FOSS_SEARCHDEBUG") == "1";

    private string? _searchPhrase;

    private bool _isRegex;

    private bool _caseSensitive = true;

    private bool _wholeWord;

    private readonly TextFragmentCollection _fragments = new();

    private TextSearchOptions? _textSearchOptions;

    // Sources visited in absorb-all mode (empty search phrase). The public API
    // requires that TextFragmentAbsorber.Text equals TextAbsorber.Text for the
    // same document and formatting mode (Pure vs Raw), which the fragment
    // concatenation path cannot reproduce (it lacks Pure-mode column/grid
    // spacing and the shared row-reconstruction). When Text is read in this
    // mode we replay the visited sources through a TextAbsorber and return its
    // output. Captured lazily so the (dominant) search-phrase path pays nothing.
    private readonly List<Page> _absorbAllPages = new();

    private readonly List<XForm> _absorbAllForms = new();

    // Whole-document absorb-all: TextAbsorber.Visit(Document) applies cross-page
    // Pure-mode line-width padding that a per-page replay does not, so when the
    // fragment absorber was itself driven by Visit(Document) we must delegate at
    // the document level to reproduce TextAbsorber.Text exactly.
    private Document? _absorbAllDocument;

    /// <summary>
    /// Create an absorber that collects all text fragments.
    /// </summary>
    public TextFragmentAbsorber()
    {
    }

    /// <summary>
    /// Create an absorber that searches for a specific phrase.
    /// </summary>
    public TextFragmentAbsorber(string searchPhrase, bool isRegex = false)
    {
        _searchPhrase = searchPhrase;
        _isRegex = isRegex;
    }

    /// <summary>Create an absorber for a single literal phrase.</summary>
    public TextFragmentAbsorber(string phrase)
    {
        _searchPhrase = phrase;
        _isRegex = false;
    }

    /// <summary>Create an absorber for a phrase with edit options.</summary>
    public TextFragmentAbsorber(string phrase, TextEditOptions textEditOptions)
    {
        _searchPhrase = phrase;
        _textEditOptions = textEditOptions;
    }

    /// <summary>
    /// Create an absorber that searches for a specific phrase with search options.
    /// </summary>
    public TextFragmentAbsorber(string phrase, TextSearchOptions textSearchOptions)
    {
        _searchPhrase = phrase;
        _isRegex = textSearchOptions.IsRegularExpression;
        _caseSensitive = textSearchOptions.CaseSensitive;
        _wholeWord = textSearchOptions.WholeWord;
        _textSearchOptions = textSearchOptions;
    }

    /// <summary>Create an absorber for a phrase with search + edit options.</summary>
    public TextFragmentAbsorber(string phrase, TextSearchOptions textSearchOptions, TextEditOptions textEditOptions)
        : this(phrase, textSearchOptions)
    {
        _textEditOptions = textEditOptions;
    }

    /// <summary>Create an absorber from a regex with edit options.</summary>
    public TextFragmentAbsorber(System.Text.RegularExpressions.Regex regex, TextEditOptions textEditOptions)
        : this(regex)
    {
        _textEditOptions = textEditOptions;
    }

    /// <summary>Create an absorber from a regex with search options.</summary>
    public TextFragmentAbsorber(System.Text.RegularExpressions.Regex regex, TextSearchOptions textSearchOptions)
        : this(regex)
    {
        _textSearchOptions = textSearchOptions;
    }

    /// <summary>Create an absorber from an array of regexes; each compiles into its own RegexResults entry.</summary>
    public TextFragmentAbsorber(System.Text.RegularExpressions.Regex[] regexes, TextSearchOptions textSearchOptions)
    {
        if (regexes is null || regexes.Length == 0)
            throw new ArgumentException("At least one regex is required.", nameof(regexes));
        _regexes = regexes;
        _searchPhrase = regexes[0].ToString();
        _isRegex = true;
        _textSearchOptions = textSearchOptions;
    }

    private System.Text.RegularExpressions.Regex[]? _regexes;

    /// <summary>
    /// Create an absorber that searches using a precompiled .NET <see cref="Regex"/>.
    /// The regex's pattern becomes the search phrase; <see cref="RegexOptions.IgnoreCase"/>
    /// flips <c>CaseSensitive</c> off.
    /// </summary>
    public TextFragmentAbsorber(System.Text.RegularExpressions.Regex regex)
    {
        if (regex is null) throw new ArgumentNullException(nameof(regex));
        _searchPhrase = regex.ToString();
        _isRegex = true;
        _caseSensitive = (regex.Options & System.Text.RegularExpressions.RegexOptions.IgnoreCase) == 0;
    }

    /// <summary>Create an absorber configured with the given edit options.</summary>
    public TextFragmentAbsorber(TextEditOptions textEditOptions)
    {
        _textEditOptions = textEditOptions;
    }

    private TextEditOptions? _textEditOptions;

    /// <summary>Edit options applied during text replacement / font substitution.</summary>
    public TextEditOptions TextEditOptions
    {
        get => _textEditOptions ??= new TextEditOptions(TextEditOptions.LanguageTransformation.Default);
        set => _textEditOptions = value;
    }

    /// <summary>Found text fragments. 1-based indexer matching the public API.</summary>
    public TextFragmentCollection TextFragments
    {
        get
        {
            var frags = _fragmentsOverride ?? _fragments;
            // Carry the absorber's edit options onto every fragment (only when the
            // caller supplied options explicitly) so a later `fragment.Text = ...`
            // honors NoCharacterBehavior regardless of which extraction path built it.
            if (_textEditOptions is not null)
                foreach (TextFragment f in frags)
                    f.TextEditOptions = _textEditOptions;
            return frags;
        }
        set => _fragmentsOverride = value;
    }

    private TextFragmentCollection? _fragmentsOverride;

    /// <summary>Textless line advances after the last glyph of the last visited page —
    /// the document's trailing blank lines, replayed by <see cref="Text"/>.</summary>
    private int _trailingLineBreaks;

    /// <summary>Diagnostics emitted while extracting text. Empty unless extraction reports an error.</summary>
    public List<TextExtractionError> Errors { get; } = new();

    /// <summary>True when <see cref="Errors"/> has at least one entry.</summary>
    public bool HasErrors => Errors.Count > 0;

    /// <summary>Per-regex fragment groups when constructed via the <c>Regex[]</c> ctor; empty otherwise.</summary>
    public Dictionary<System.Text.RegularExpressions.Regex, TextFragmentCollection> RegexResults { get; } = new();

    /// <summary>Gets or sets the search phrase. Setting clears previous results.</summary>
    public string? Phrase
    {
        get => _searchPhrase;
        set { _searchPhrase = value; _fragments.Clear(); _absorbAllPages.Clear(); _absorbAllForms.Clear(); _absorbAllDocument = null; }
    }

    /// <summary>
    /// Gets the concatenated text of all found fragments.
    /// </summary>
    public string Text
    {
        get
        {
            // Absorb-all mode: the extracted text must match TextAbsorber.Text
            // for the same formatting mode (the public contract the two absorbers
            // share — see ExtractText_AbsorbersComparation). Replay the visited
            // sources through a TextAbsorber configured with the same options
            // rather than concatenating fragments (which has no Pure-mode grid
            // spacing and reconstructs rows differently).
            if (string.IsNullOrEmpty(_searchPhrase) &&
                (_absorbAllDocument is not null || _absorbAllPages.Count > 0 || _absorbAllForms.Count > 0))
            {
                // Exclusion areas cut runs into positional pieces; the aggregated
                // text is no longer a contiguous reading order, so the absorb-all
                // Text contract is an empty string (fragments carry the content).
                if (_textSearchOptions?.ExcludeRectangles is { Length: > 0 })
                    return string.Empty;
                var ta = new TextAbsorber(
                    ExtractionOptions ?? new TextExtractionOptions(),
                    _textSearchOptions ?? new TextSearchOptions());
                if (_absorbAllDocument is not null)
                {
                    // Document-level replay reproduces TextAbsorber.Visit(Document)'s
                    // cross-page Pure-mode padding exactly.
                    ta.Visit(_absorbAllDocument);
                }
                else
                {
                    foreach (var page in _absorbAllPages) ta.Visit(page);
                }
                foreach (var form in _absorbAllForms) ta.Visit(form);
                var replayed = ta.Text;
                // The TextAbsorber trim also strips the document's REAL trailing
                // blank lines (textless T*/Td advances after the last glyph); the
                // fragment-absorber text keeps them — a generated PDF
                // whose source text ended "…\r\n\r\n" round-trips exactly. Re-append
                // one line break per trailing advance of the last visited page.
                if (replayed.Length > 0)
                    for (var k = 0; k < ta.TrailingBlankRows; k++)
                        replayed += "\r\n";
                return replayed;
            }

            var sb = new StringBuilder();
            double? prevY = null;
            double prevFs = 0;
            foreach (var frag in _fragments)
            {
                // Separate fragments that sit on different visual lines with a CR/LF,
                // the extracted-text line structure (consecutive same-line
                // fragments stay concatenated). A fragment's baseline Y is compared against the
                // previous one; a change beyond a small tolerance starts a new line. Fragments
                // without a resolved position don't force a break.
                var y = frag.PositionOrNull?.YIndent;
                if (prevY is { } py && y is { } cy && Math.Abs(py - cy) > 2.0)
                {
                    // BLANK source lines advance the baseline without painting anything, so a
                    // downward jump of k line-heights means k-1 empty lines were skipped —
                    // each is reproduced as its own CR/LF. Line height is taken as 1.2×
                    // the larger neighbouring font size; the +0.3 bias keeps ordinary paragraph
                    // spacing (up to ~1.7×) at a single break. Upward jumps (column/page flow)
                    // stay a single break.
                    var breaks = 1;
                    var fs = Math.Max(prevFs, frag.TextState?.FontSize ?? 0);
                    if (py - cy > 0 && fs > 0)
                        breaks = Math.Max(1, (int)Math.Floor((py - cy) / (1.2 * fs) + 0.3));
                    for (var k = 0; k < breaks; k++)
                        sb.Append("\r\n");
                }
                sb.Append(frag.Text);
                if (y is { } ny) prevY = ny;
                var curFs = frag.TextState?.FontSize ?? 0;
                if (curFs > 0) prevFs = curFs;
            }
            // Trailing blank lines have no fragment; replay the textless line advances
            // recorded after the last glyph (see BuildAllFragmentsFromRuns).
            if (sb.Length > 0)
                for (var k = 0; k < _trailingLineBreaks; k++)
                    sb.Append("\r\n");
            return sb.ToString();
        }
    }

    /// <summary>
    /// Gets or sets the text extraction options.
    /// </summary>
    public TextExtractionOptions ExtractionOptions { get; set; } = new TextExtractionOptions();

    /// <summary>
    /// Gets or sets the text search options. Can be set after construction to configure
    /// rectangle-bounded search and other options.
    /// </summary>
    public TextSearchOptions TextSearchOptions
    {
        get => _textSearchOptions ??= new TextSearchOptions();
        set
        {
            _textSearchOptions = value;
            if (value is not null)
            {
                // Preserve regex mode set by a Regex/(string,isRegex:true) constructor:
                // assigning options whose IsRegularExpression is the default false must
                // not silently demote a regex absorber to literal matching (which would
                // then search for the regex pattern text verbatim and find nothing).
                _isRegex = _isRegex || value.IsRegularExpression;
                _caseSensitive = value.CaseSensitive;
                _wholeWord = value.WholeWord;
            }
        }
    }

    /// <summary>
    /// Gets or sets text replace options for controlling replacement behavior.
    /// The absorber's default action is ShiftRestOfLine:
    /// replacing a match with shorter/longer text shifts the
    /// rest of the line by the width delta. Callers opt OUT of the shift by
    /// setting ReplaceAdjustment.None explicitly.
    /// </summary>
    public TextReplaceOptions TextReplaceOptions { get; set; } =

        new(TextReplaceOptions.ReplaceAdjustment.ShiftRestOfLine);

    /// <summary>
    /// Visit a page and extract/search text fragments.
    /// </summary>
    public void Visit(Page page)
    {
        // Don't clear — public API accumulates results across multiple Accept() calls.
        // Call Visit(Document) for a fresh search, which does clear.
        VisitInternal(page);
    }

    internal void VisitInternal(Page page, bool tolerantFonts = false,
        HashSet<object>? seenForms = null)
    {
        var reader = page.Reader;
        var contentStreams = GetContentStreams(page.Dict, reader);
        // Absorb-all mode dedups repeated Form XObject references per absorber run
        // (a document walk passes one set for all pages; a lone page visit is its
        // own run). Phrase search keeps every occurrence.
        if (seenForms is null && string.IsNullOrEmpty(_searchPhrase))
            seenForms = new HashSet<object>(ReferenceEqualityComparer.Instance);
        // Font keys named by Tf but absent from Resources, reported to the page
        // notification log when the document enables it.
        var missingFontKeys = page.Reader?.OwnerDocument?.EnableNotificationLogging == true
            ? new List<string>() : null;

        var rawFragments = new List<RawTextRun>();
        // Collect filled rects when the caller asked for graphics-related results, or when
        // ToAttemptGetUnderlineFromSource is set (so source underlines can be captured and,
        // if the fragment's underline is later toggled off, removed at save time).
        // Always collect fill rects: strikeout detection runs by default (no option).
        // In default mode only thin decoration-candidate rects are kept (see ExtractRuns)
        // so the extra bookkeeping stays cheap; the background/underline consumers below
        // remain gated behind their options.
        var fillRects = new List<RawFillRect>();
        // Occlusion candidates for hidden-text detection (always on; rect-only paths).
        var coverRects = new List<RawCoverRect>();

        // Apply page rotation CTM so fragment coordinates are in the viewer's
        // natural coordinate system (same as the public API behaviour).
        var rotCtm = PageRotationCtm(page);

        // Per PDF spec, a page's content streams are logically a single concatenated stream —
        // text state and graphics state must persist across them. Concatenate with a space
        // separator to prevent token adjacency.
        if (contentStreams.Count == 1)
        {
            ExtractRuns(contentStreams[0], page.Dict, reader, rawFragments, inheritedCtm: rotCtm, fillRects: fillRects, useFontEngineEncoding: _textSearchOptions?.UseFontEngineEncoding ?? false, keepAllFillRects: (_textSearchOptions?.SearchForTextRelatedGraphics ?? true) || (_textEditOptions?.ToAttemptGetUnderlineFromSource ?? false), coverRects: coverRects, strictFonts: !tolerantFonts && !(_textSearchOptions?.IgnoreResourceFontErrors ?? false), seenForms: seenForms, missingFontKeys: missingFontKeys);
        }
        else if (contentStreams.Count > 1)
        {
            var totalLen = 0;
            foreach (var s in contentStreams) totalLen += s.Length + 1;
            var combined = new byte[totalLen];
            int off = 0;
            foreach (var s in contentStreams)
            {
                Buffer.BlockCopy(s, 0, combined, off, s.Length);
                off += s.Length;
                combined[off++] = (byte)'\n';
            }
            ExtractRuns(combined, page.Dict, reader, rawFragments, inheritedCtm: rotCtm, fillRects: fillRects, useFontEngineEncoding: _textSearchOptions?.UseFontEngineEncoding ?? false, keepAllFillRects: (_textSearchOptions?.SearchForTextRelatedGraphics ?? true) || (_textEditOptions?.ToAttemptGetUnderlineFromSource ?? false), coverRects: coverRects, strictFonts: !tolerantFonts && !(_textSearchOptions?.IgnoreResourceFontErrors ?? false), seenForms: seenForms, missingFontKeys: missingFontKeys);
        }

        if (missingFontKeys is { Count: > 0 })
            foreach (var key in missingFontKeys)
                page.NotificationLog +=
                    $"Document error: Font key {key} is absent in page Resources\r\n";

        var searchRect = _textSearchOptions?.Rectangle;

        if (string.IsNullOrEmpty(_searchPhrase)) // empty phrase = absorb all
        {
            _absorbAllPages.Add(page);
            BuildAllFragmentsFromRuns(rawFragments, searchRect, sourcePage: page,
                sourceForm: null, pageIndex: page.Index, fillRects: fillRects, coverRects: coverRects);
        }
        else
        {
            // Search for the phrase in concatenated text, then map matches
            // back to source runs for bounding rectangles
            BuildSearchFragments(rawFragments, page.Index, page, fillRects: fillRects);

            // Apply rectangle filter if set — a fragment is kept when its bounding box overlaps the
            // search rect (a search box that clips only the ascender band of a
            // run still finds it), falling back to start-position containment when no bbox is known.
            if (searchRect is not null && !searchRect.IsEmpty)
            {
                for (var i = _fragments.Count - 1; i >= 0; i--)
                {
                    if (!FragmentInSearchRect(searchRect, _fragments.GetInternal(i)))
                    {
                        // Only remove fragments added during this Visit call (they have matching pageIndex)
                        if (_fragments.GetInternal(i).PageIndex == page.Index)
                            _fragments.RemoveAt(i);
                    }
                }
            }
        }

        // Also search the text drawn inside annotation appearance streams
        // (form fields, FreeText, stamps, …) when the caller opts in.
        if (_textSearchOptions?.SearchInAnnotations ?? false)
        {
            foreach (var annotation in page.Annotations)
            {
                var appearance = annotation?.NormalAppearance;
                if (appearance is not null) Visit(appearance);
            }
        }
    }

    /// <summary>
    /// Search the content stream of a Form XObject for text fragments. The
    /// form's own /Resources dict supplies fonts; fragments are produced with
    /// <see cref="TextFragment.Page"/>=null and <see cref="TextFragment.Form"/>
    /// set to <paramref name="form"/>. When a search phrase is set the
    /// fragments are filtered by phrase match using the same logic as
    /// <see cref="Visit(Page)"/>.
    /// </summary>
    public void Visit(XForm xForm)
    {
        var form = xForm;
        if (form is null) throw new ArgumentNullException(nameof(xForm));
        var streamBytes = form.DecodedBytes;
        if (streamBytes.Length == 0) return;

        var reader = form.Reader;
        var dict = form.StreamDict;

        var rawFragments = new List<RawTextRun>();
        // Always collect fill rects: strikeout detection runs by default (no option).
        // In default mode only thin decoration-candidate rects are kept (see ExtractRuns)
        // so the extra bookkeeping stays cheap; the background/underline consumers below
        // remain gated behind their options.
        var fillRects = new List<RawFillRect>();
        ExtractRuns(streamBytes, dict, reader, rawFragments, fillRects: fillRects, useFontEngineEncoding: _textSearchOptions?.UseFontEngineEncoding ?? false, keepAllFillRects: (_textSearchOptions?.SearchForTextRelatedGraphics ?? true) || (_textEditOptions?.ToAttemptGetUnderlineFromSource ?? false));

        var searchRect = _textSearchOptions?.Rectangle;

        if (string.IsNullOrEmpty(_searchPhrase)) // empty phrase = absorb all
        {
            _absorbAllForms.Add(form);
            BuildAllFragmentsFromRuns(rawFragments, searchRect, sourcePage: null,
                sourceForm: form, pageIndex: 0, fillRects: fillRects);
        }
        else
        {
            BuildSearchFragments(rawFragments, pageIndex: 0, sourcePage: null, sourceForm: form, fillRects: fillRects);
            if (searchRect is not null && !searchRect.IsEmpty)
            {
                for (var i = _fragments.Count - 1; i >= 0; i--)
                {
                    if (!FragmentInSearchRect(searchRect, _fragments.GetInternal(i)))
                    {
                        if (_fragments.GetInternal(i).PageIndex == 0)
                            _fragments.RemoveAt(i);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Convert an extracted set of raw runs into TextFragment objects (the
    /// no-phrase path). Shared between <see cref="Visit(Page)"/> and
    /// <see cref="Visit(XForm)"/>.
    /// </summary>

    /// <summary>Split runs whose glyphs sit further apart than a column-gap threshold
    /// (Tc-spread table layouts draw several columns in ONE show op). Pieces keep
    /// per-char geometry (rebased) and the continuations carry GapSplit so the
    /// search text gets exactly one boundary space between them.</summary>
    private static void SplitRunsAtCharGaps(List<RawTextRun> runs)
    {
        for (var ri = 0; ri < runs.Count; ri++)
        {
            var run = runs[ri];
            var cum = run.CharCumWidths;
            var ends = run.CharEndPositions;
            var n = run.Text.Length;
            if (cum is null || ends is null || n < 2) continue;
            if (cum.Length < n || ends.Length < n) continue;
            var tmN = Math.Sqrt(run.TmA * run.TmA + run.TmB * run.TmB);
            var det = Math.Abs(run.Ctm.A * run.Ctm.D - run.Ctm.B * run.Ctm.C);
            var dev = (tmN > 1e-9 ? tmN : 1.0) * (det > 1e-12 ? Math.Sqrt(det) : 1.0) * run.HScaling;
            var effFs = run.FontSize * dev;
            // Column hops only: measured column spreads run 10-14em; everything
            // below ~4em (justified spaces, tracked titles, glued abbreviations)
            // stays inside the run.
            var threshold = Math.Max(24.0, 4.0 * effFs);
            List<int>? cuts = null;
            var wideGaps = 0;
            double minWide = double.MaxValue, maxWide = 0;
            for (var i = 0; i + 1 < n; i++)
            {
                // CharEndPositions fold Tc (and Tw for spaces) into the char's
                // advance; add them back so the INK gap between glyphs is measured.
                var pad = run.CharSpacing + (run.Text[i] == ' ' ? run.WordSpacing : 0.0);
                // A glyph whose measured ink width is ~0 wasn't really measured
                // (missing widths/exotic cmap) — its "gap" is the raw advance and
                // means nothing; never cut there.
                var inkW = ends[i] - cum[i] - pad;
                if (inkW <= 0.01) continue;
                var gap = (cum[i + 1] - ends[i] + pad) * dev;
                var spaceAdj = run.Text[i] == ' ' || run.Text[i + 1] == ' ';
                // A run is fragmented only when the
                // column spread is carried by SPACING OPERATORS (Tc/Tw push pairs
                // apart and kerns pull token interiors back together — a table
                // row). Kern-only spreads (monospace manifests, glued headers)
                // stay whole even at 100pt gaps. An explicit space glyph already
                // separates words, so no cut lands next to one.
                var spacingCarried = (Math.Abs(run.CharSpacing) + Math.Abs(run.WordSpacing)) * dev >= 1.0;
                if (spaceAdj) continue;
                if (spacingCarried && gap >= threshold)
                {
                    (cuts ??= new List<int>()).Add(i + 1);
                    wideGaps++;
                    if (gap < minWide) minWide = gap;
                    if (gap > maxWide) maxWide = gap;
                }
            }
            if (cuts is null) continue;
            // Letter-tracked display text gaps EVERY glyph pair by a similar
            // amount — that is styling, not columns. Columns show up as a MIX of
            // tight pairs and wide hops. Only split mixed runs.
            if (wideGaps == n - 1 && maxWide < minWide * 1.5) continue;

            cuts.Add(n);
            var pieces = new List<RawTextRun>(cuts.Count);
            var start = 0;
            foreach (var cut in cuts)
            {
                var len = cut - start;
                if (len <= 0) { start = cut; continue; }
                var baseAdv = cum[start];
                var subCum = new double[len];
                var subEnds = new double[len];
                for (var k = 0; k < len; k++)
                {
                    subCum[k] = cum[start + k] - baseAdv;
                    subEnds[k] = ends[start + k] - baseAdv;
                }
                // The last char's recorded end folds its trailing Tc/Tw pad in;
                // the piece's INK width must not carry it (rects would span the
                // column gap to the next piece).
                var lastPad = run.CharSpacing
                    + (run.Text[cut - 1] == ' ' ? run.WordSpacing : 0.0);
                var pieceWidth = Math.Max(0, subEnds[len - 1] - lastPad);
                // Trim the pad off the recorded end too, so match rectangles
                // measure the ink, not the column hop to the next piece.
                subEnds[len - 1] = pieceWidth;
                pieces.Add(run with
                {
                    Text = run.Text.Substring(start, len),
                    X = run.X + run.TmA * baseAdv,
                    Y = run.Y + run.TmB * baseAdv,
                    Width = pieceWidth,
                    CharCumWidths = subCum,
                    CharEndPositions = subEnds,
                    GapSplit = start > 0,
                });
                start = cut;
            }
            if (pieces.Count > 1)
            {
                runs.RemoveAt(ri);
                runs.InsertRange(ri, pieces);
                ri += pieces.Count - 1;
            }
        }
    }

    /// <summary>Split runs against <see cref="TextSearchOptions.ExcludeRectangles"/>:
    /// characters inside an excluded area are dropped and the remaining glyphs
    /// continue as separate pieces, so each kept stretch surfaces as its own
    /// fragment. A character is excluded when its glyph box overlaps the
    /// rectangle by more than half a point in BOTH axes — a rectangle that
    /// merely touches a line's band (or a character edge) leaves it alone.</summary>
    private static void SplitRunsByExcludeRects(List<RawTextRun> runs, Rectangle[] excludeRects)
    {
        for (var ri = 0; ri < runs.Count; ri++)
        {
            var run = runs[ri];
            var n = run.Text.Length;
            if (n == 0 || run.Text == "\r\n") continue;

            // Vertical glyph band in page space (descent..ascent along the Tm up-axis).
            var fs = run.FontSize;
            double descentOffset = 0, ascentHeight = fs;
            if (run.Metrics is not null && run.Metrics.Descent != 0)
                descentOffset = run.Metrics.Descent * fs / 1000.0;
            if (run.Metrics is not null && run.Metrics.Ascent > 0)
                ascentHeight = run.Metrics.Ascent * fs / 1000.0;
            var (_, by1) = ApplyCtm(run.X + run.TmC * descentOffset, run.Y + run.TmD * descentOffset, run.Ctm);
            var (_, by2) = ApplyCtm(run.X + run.TmC * ascentHeight, run.Y + run.TmD * ascentHeight, run.Ctm);
            var bandLly = Math.Min(by1, by2);
            var bandUry = Math.Max(by1, by2);
            var bandH = bandUry - bandLly;

            const double tol = 0.5;
            var dbg = Environment.GetEnvironmentVariable("ASPOSE_FOSS_EXCLDEBUG") == "1";
            var active = new List<Rectangle>();
            foreach (var er in excludeRects)
            {
                if (er is null || er.IsEmpty) continue;
                var overlapV = Math.Min(bandUry, er.URY) - Math.Max(bandLly, er.LLY);
                if (overlapV > tol) active.Add(er);
            }
            if (dbg) Console.Error.WriteLine($"[excl] run '{(run.Text.Length > 24 ? run.Text.Substring(0, 24) : run.Text)}' X={run.X:0.#} Y={run.Y:0.#} band={bandLly:0.#}..{bandUry:0.#} active={active.Count} cum={(run.CharCumWidths?.Length.ToString() ?? "null")} ends={(run.CharEndPositions?.Length.ToString() ?? "null")} n={n}");
            if (active.Count == 0) continue;

            // Per-character page-space X positions (cumulative advances include Tc/Tw).
            var cum = run.CharCumWidths;
            var ends = run.CharEndPositions;
            if (ends is not null && ends.Length < n) ends = null;
            var charX = new double[n + 1];
            var haveCum = cum is not null && cum.Length > n;
            if (cum is not null && cum.Length > n)
            {
                for (var i = 0; i <= n; i++)
                {
                    var (px, _) = ApplyCtm(run.X + run.TmA * cum[i] * run.HScaling,
                        run.Y + run.TmB * cum[i] * run.HScaling, run.Ctm);
                    charX[i] = px;
                }
            }
            else
            {
                var totalW = run.Width > 0 ? run.Width : EstimateWidth(run.Text, fs);
                for (var i = 0; i <= n; i++)
                {
                    var cw = totalW * i / n;
                    var (px, _) = ApplyCtm(run.X + run.TmA * cw * run.HScaling,
                        run.Y + run.TmB * cw * run.HScaling, run.Ctm);
                    charX[i] = px;
                }
            }

            var excluded = new bool[n];
            var any = false;
            for (var i = 0; i < n; i++)
            {
                // A character belongs to the excluded area when its END lands
                // inside the rectangle — a glyph merely straddling the RIGHT edge
                // stays with the text after the area, while one straddling the
                // left edge (its end inside) is consumed by the area.
                var cend = Math.Max(charX[i], charX[i + 1]);
                foreach (var er in active)
                {
                    if (cend > er.LLX + tol && cend <= er.URX + tol) { excluded[i] = true; any = true; break; }
                }
            }
            if (!any) continue;

            // Without per-char advances the pieces cannot be re-based; drop the run
            // only when everything is excluded, otherwise keep it whole.
            if (!haveCum)
            {
                var all = true;
                for (var i = 0; i < n; i++) if (!excluded[i]) { all = false; break; }
                if (all) { runs.RemoveAt(ri); ri--; }
                continue;
            }

            var pieces = new List<RawTextRun>();
            var start = -1;
            for (var i = 0; i <= n; i++)
            {
                var keep = i < n && !excluded[i];
                if (keep && start < 0) start = i;
                if (!keep && start >= 0)
                {
                    var len = i - start;
                    var baseAdv = cum![start];
                    var subCum = new double[len];
                    var subEnds = new double[len];
                    for (var k = 0; k < len; k++)
                    {
                        subCum[k] = cum[start + k] - baseAdv;
                        // No recorded ink ends: the next char's start is the end.
                        subEnds[k] = ends is not null
                            ? ends[start + k] - baseAdv
                            : cum[start + k + 1] - baseAdv;
                    }
                    var lastPad = run.CharSpacing
                        + (run.Text[i - 1] == ' ' ? run.WordSpacing : 0.0);
                    var pieceWidth = Math.Max(0, subEnds[len - 1] - lastPad);
                    subEnds[len - 1] = pieceWidth;
                    pieces.Add(run with
                    {
                        Text = run.Text.Substring(start, len),
                        X = run.X + run.TmA * baseAdv,
                        Y = run.Y + run.TmB * baseAdv,
                        Width = pieceWidth,
                        CharCumWidths = subCum,
                        CharEndPositions = ends is not null ? subEnds : null,
                        GapSplit = start > 0,
                    });
                    start = -1;
                }
            }
            runs.RemoveAt(ri);
            runs.InsertRange(ri, pieces);
            ri += pieces.Count - 1;
        }
    }

    /// <summary>
    /// Marks runs whose glyph box a LATER text run's ink covers
    /// (stacked duplicate draws report every copy but the last as Invisible —
    /// the occluder needn't match text, font or colour; coverage above ~55% of the
    /// victim's area hides it). Candidates are found through a coarse position
    /// grid, so only occluders drawn at (near-)the-same spot are considered — the
    /// duplicate-stack shape this rule exists for; a large body of text far from
    /// the victim's centre never scans the whole page.
    /// </summary>
    private static (bool[] Occluded, bool[] ClippedAway, double[] BoxArea) ComputeLaterInkOcclusion(List<RawTextRun> runs)
    {
        var n = runs.Count;
        var occluded = new bool[n];
        var clippedAway = new bool[n];
        var boxArea = new double[n];
        if (n < 2) return (occluded, clippedAway, boxArea);

        // Device-space glyph boxes (baseline-anchored approximation: −0.2 em
        // descent to +0.7 em cap height along the Tm up-axis).
        var boxes = new (double Llx, double Lly, double Urx, double Ury, bool Ink)[n];
        for (var i = 0; i < n; i++)
        {
            var run = runs[i];
            if (run.Text.Length == 0 || run.Text == "\r\n" || string.IsNullOrWhiteSpace(run.Text))
            {
                boxes[i] = (0, 0, -1, -1, false);
                continue;
            }
            var fs = run.FontSize;
            var w = run.Width * run.HScaling;
            var x0 = run.X + run.TmC * (-0.2 * fs);
            var y0 = run.Y + run.TmD * (-0.2 * fs);
            var x1 = run.X + run.TmA * w + run.TmC * (0.7 * fs);
            var y1 = run.Y + run.TmB * w + run.TmD * (0.7 * fs);
            var (dx0, dy0) = ApplyCtm(x0, y0, run.Ctm);
            var (dx1, dy1) = ApplyCtm(x1, y1, run.Ctm);
            var llx = Math.Min(dx0, dx1);
            var lly = Math.Min(dy0, dy1);
            var urx = Math.Max(dx0, dx1);
            var ury = Math.Max(dy0, dy1);
            // Ink = this run actually paints: fill/stroke modes only (Tr 3 and
            // the clip-only mode 7 mark nothing), and not clipped away.
            var ink = run.RenderingMode != 3 && run.RenderingMode != 7;
            if (run.ClipRect is { } rc)
            {
                var cx = Math.Min(urx, rc.Urx) - Math.Max(llx, rc.Llx);
                var cy = Math.Min(ury, rc.Ury) - Math.Max(lly, rc.Lly);
                if (cx <= 0.05 || cy <= 0.05) { ink = false; clippedAway[i] = true; }
            }
            boxes[i] = (llx, lly, urx, ury, ink);
            boxArea[i] = Math.Max(0, urx - llx) * Math.Max(0, ury - lly);
        }

        // Coarse grid over box centres: only same-cell (±1) pairs are compared.
        var grid = new Dictionary<(int gx, int gy), List<int>>();
        const double cell = 8.0;
        for (var i = 0; i < n; i++)
        {
            if (boxes[i].Urx < boxes[i].Llx) continue;
            var key = ((int)Math.Floor((boxes[i].Llx + boxes[i].Urx) / 2 / cell),
                       (int)Math.Floor((boxes[i].Lly + boxes[i].Ury) / 2 / cell));
            if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
            list.Add(i);
        }

        foreach (var (key, list) in grid)
        {
            foreach (var i in list)
            {
                var vb = boxes[i];
                if (vb.Urx < vb.Llx) continue;
                var area = (vb.Urx - vb.Llx) * (vb.Ury - vb.Lly);
                if (area <= 0.01) continue;
                for (var dgx = -1; dgx <= 1 && !occluded[i]; dgx++)
                for (var dgy = -1; dgy <= 1 && !occluded[i]; dgy++)
                {
                    if (!grid.TryGetValue((key.gx + dgx, key.gy + dgy), out var cand)) continue;
                    foreach (var j in cand)
                    {
                        if (j <= i || !boxes[j].Ink) continue; // only LATER ink occludes
                        var ob = boxes[j];
                        var ix = Math.Min(vb.Urx, ob.Urx) - Math.Max(vb.Llx, ob.Llx);
                        var iy = Math.Min(vb.Ury, ob.Ury) - Math.Max(vb.Lly, ob.Lly);
                        if (ix <= 0 || iy <= 0) continue;
                        if (ix * iy > area * 0.55) { occluded[i] = true; break; }
                    }
                }
            }
        }
        return (occluded, clippedAway, boxArea);
    }
}

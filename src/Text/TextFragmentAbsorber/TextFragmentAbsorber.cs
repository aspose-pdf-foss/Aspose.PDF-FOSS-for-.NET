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
        // The regex's own IgnoreCase survives a later TextSearchOptions assignment:
        // options carry their default CaseSensitive=true, which must not silently
        // re-sensitise a Regex(IgnoreCase) search (matching is
        // case-insensitive when EITHER asks for it).
        _regexIgnoreCase = !_caseSensitive;
    }

    /// <summary>True when a Regex-based ctor carried RegexOptions.IgnoreCase —
    /// case-insensitive matching then applies whatever the search options say.</summary>
    private bool _regexIgnoreCase;

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
        // A repeated Form XObject reference is deduped per absorber run (a document walk
        // passes one set for all pages; a lone page visit is its own run). This holds for
        // a phrase search too: the matches inside a shared form all address the SAME
        // bytes, so reporting one per referencing page would hand the caller N handles
        // onto one piece of text.
        seenForms ??= new HashSet<object>(ReferenceEqualityComparer.Instance);
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

    /// <summary>
    /// How many of a run's leading characters the horizontal clip test is
    /// measured against.
    /// </summary>
    /// <remarks>
    /// Trailing spaces are no part of the tested box: "Probe " tolerates exactly one
    /// space advance more than "Probe" past a clip's right edge, "Probe  " two. LEADING
    /// spaces shorten it from the OTHER end - as many characters are dropped
    /// off the END as the run has leading spaces, so " Probe" is tested as " Prob"
    /// (28.68 pt of Helvetica-12, not 35.352) and "  Probe" as "  Pro". Measured on
    /// twenty-odd strings, both edges agreeing on the same width and character count
    /// (" AA" and " PW" test alike at 11.34 pt; "  W" and " ." both collapse to the
    /// leading space alone). An all-space run keeps its whole box.
    /// </remarks>
    private static (int Keep, int Count) TestedCharCount(string text)
    {
        var end = text.Length;
        while (end > 0 && text[end - 1] == ' ') end--;
        // An all-space run keeps its whole box, and the right edge's slack is a tenth
        // of that whole box: four spaces tolerate 1.3344 pt past the clip, not the
        // 0.3336 pt a per-character average would give.
        if (end == 0) return (text.Length, 1);
        var lead = 0;
        while (lead < end && text[lead] == ' ') lead++;
        var keep = end - lead;
        return (keep, keep);
    }

    /// <summary>Does the run advance along +x on the page (so its last glyph sits at URX)?</summary>
    private static bool TextReadsLeftToRight(RawTextRun run)
    {
        var tdx = run.Ctm.A * run.TmA + run.Ctm.C * run.TmB;
        var tdy = run.Ctm.B * run.TmA + run.Ctm.D * run.TmB;
        return tdx > 0 && Math.Abs(tdy) < 1e-6;
    }

    /// <summary>
    /// The run's reported line box in page space - bottom at baseline + descent,
    /// 1.1 x FontSize tall, the same box <c>BuildFragments</c> hands out. The clip
    /// verdict is that box's, not the ink box <see cref="ComputeLaterInkOcclusion"/>
    /// compares for coverage.
    /// </summary>
    private static (double Llx, double Lly, double Urx, double Ury) RunLineBox(RawTextRun run)
    {
        // coreFaceDescent: TRUE - this box is the clip verdict's, and the clip
        // applies to the FACE's own line box even when the font dict declares no descent.
        var (descentOffset, ascentHeight) = ComputeDescentAscent(run, coreFaceDescent: true);
        var w = (run.Width > 0 ? run.Width : EstimateWidth(run.Text, run.FontSize)) * run.HScaling;
        if (run.Width > 0 && run.Text.Length > 0)
            w -= (run.CharSpacing + (run.Text[^1] == ' ' ? run.WordSpacing : 0)) * run.HScaling;
        var (x1, y1) = ApplyCtm(run.X + run.TmC * descentOffset,
                                run.Y + run.TmD * descentOffset, run.Ctm);
        var (x2, y2) = ApplyCtm(run.X + run.TmA * w + run.TmC * ascentHeight,
                                run.Y + run.TmB * w + run.TmD * ascentHeight, run.Ctm);
        return (Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));
    }

    private static (bool[] Occluded, bool[] ClippedAway, double[] BoxArea) ComputeLaterInkOcclusion(List<RawTextRun> runs)
    {
        var n = runs.Count;
        var occluded = new bool[n];
        var clippedAway = new bool[n];
        var boxArea = new double[n];
        if (n < 2) return (occluded, clippedAway, boxArea);

        // Device-space glyph boxes (baseline-anchored approximation, see the
        // Occlusion* constants above).
        var boxes = new (double Llx, double Lly, double Urx, double Ury, bool Ink)[n];
        for (var i = 0; i < n; i++)
        {
            var run = runs[i];
            // The clip verdict is the FACE's line box's, and it stands for a
            // whitespace-only run too: a space paints nothing, but it is
            // reported Invisible when the clip cuts its box away exactly as a
            // run of letters is.
            if (run.ClipRect is { } clipRc && run.Text.Length > 0 && run.Text != "\r\n")
            {
                var lb = RunLineBox(run);
                if (IsHiddenByClip(run, run.Text, lb.Llx, lb.Lly, lb.Urx, lb.Ury, clipRc))
                    clippedAway[i] = true;
            }
            if (run.Text.Length == 0 || run.Text == "\r\n")
            {
                boxes[i] = (0, 0, -1, -1, false);
                continue;
            }
            var fs = run.FontSize;
            var w = run.Width * run.HScaling;
            var x0 = run.X + run.TmC * (-OcclusionBoxDescentEm * fs);
            var y0 = run.Y + run.TmD * (-OcclusionBoxDescentEm * fs);
            var x1 = run.X + run.TmA * w + run.TmC * (OcclusionBoxCapHeightEm * fs);
            var y1 = run.Y + run.TmB * w + run.TmD * (OcclusionBoxCapHeightEm * fs);
            var (dx0, dy0) = ApplyCtm(x0, y0, run.Ctm);
            var (dx1, dy1) = ApplyCtm(x1, y1, run.Ctm);
            var llx = Math.Min(dx0, dx1);
            var lly = Math.Min(dy0, dy1);
            var urx = Math.Max(dx0, dx1);
            var ury = Math.Max(dy0, dy1);
            // Ink = this run actually paints: fill/stroke modes only (Tr 3 and
            // the clip-only mode 7 mark nothing), not clipped away, and not
            // whitespace — a space paints nothing and can hide no one. It still
            // keeps its box: like the clip verdict above, a space is reported
            // Invisible when LATER ink overpaints where it stands (a footer
            // glyph drawn across a stray space run covers it like any letter).
            var ink = run.RenderingMode != 3 && run.RenderingMode != 7 && !clippedAway[i]
                && !string.IsNullOrWhiteSpace(run.Text);
            boxes[i] = (llx, lly, urx, ury, ink);
            boxArea[i] = Math.Max(0, urx - llx) * Math.Max(0, ury - lly);
        }

        // Coarse grid: OCCLUDERS register in every cell their box spans — a long run
        // hides a short one whose centre sits far from its own (a footer line drawn
        // across a stray space run) — while each victim looks up only its centre
        // cell ±1. Registering by centre missed exactly that pair.
        var grid = new Dictionary<(int gx, int gy), List<int>>();
        const double cell = OcclusionGridCellPt;
        for (var j = 0; j < n; j++)
        {
            if (!boxes[j].Ink || boxes[j].Urx < boxes[j].Llx) continue;
            var gx0 = (int)Math.Floor(boxes[j].Llx / cell);
            var gx1 = (int)Math.Floor(boxes[j].Urx / cell);
            var gy0 = (int)Math.Floor(boxes[j].Lly / cell);
            var gy1 = (int)Math.Floor(boxes[j].Ury / cell);
            if ((long)(gx1 - gx0 + 1) * (gy1 - gy0 + 1) > OcclusionOccluderCellCap) continue;
            for (var gx = gx0; gx <= gx1; gx++)
                for (var gy = gy0; gy <= gy1; gy++)
                {
                    if (!grid.TryGetValue((gx, gy), out var list)) grid[(gx, gy)] = list = new List<int>();
                    list.Add(j);
                }
        }

        for (var i = 0; i < n; i++)
        {
            var vb = boxes[i];
            if (vb.Urx < vb.Llx) continue;
            var area = (vb.Urx - vb.Llx) * (vb.Ury - vb.Lly);
            if (area <= OcclusionMinBoxArea) continue;
            var key = ((int)Math.Floor((vb.Llx + vb.Urx) / 2 / cell),
                       (int)Math.Floor((vb.Lly + vb.Ury) / 2 / cell));
            for (var dgx = -1; dgx <= 1 && !occluded[i]; dgx++)
            for (var dgy = -1; dgy <= 1 && !occluded[i]; dgy++)
            {
                if (!grid.TryGetValue((key.Item1 + dgx, key.Item2 + dgy), out var cand)) continue;
                foreach (var j in cand)
                {
                    if (j <= i) continue; // only LATER ink occludes
                    var ob = boxes[j];
                    var ix = Math.Min(vb.Urx, ob.Urx) - Math.Max(vb.Llx, ob.Llx);
                    var iy = Math.Min(vb.Ury, ob.Ury) - Math.Max(vb.Lly, ob.Lly);
                    if (ix <= 0 || iy <= 0) continue;
                    if (ix * iy > area * OcclusionCoverageFraction) { occluded[i] = true; break; }
                }
            }
        }
        return (occluded, clippedAway, boxArea);
    }
}

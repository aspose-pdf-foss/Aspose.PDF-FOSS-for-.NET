using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

/// <summary>
/// Searches for text fragments on PDF pages, optionally matching a search phrase.
/// </summary>
public sealed class TextFragmentAbsorber
{
    private string? _searchPhrase;
    private bool _isRegex;
    private bool _caseSensitive = true;
    private bool _wholeWord;
    private readonly TextFragmentCollection _fragments = new();
    private TextSearchOptions? _textSearchOptions;

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
        set { _searchPhrase = value; _fragments.Clear(); }
    }

    /// <summary>
    /// Gets the concatenated text of all found fragments.
    /// </summary>
    public string Text
    {
        get
        {
            var sb = new StringBuilder();
            foreach (var frag in _fragments)
                sb.Append(frag.Text);
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
    /// </summary>
    public TextReplaceOptions TextReplaceOptions { get; set; } = new();

    /// <summary>
    /// Visit a page and extract/search text fragments.
    /// </summary>
    public void Visit(Page page)
    {
        // Don't clear — public API accumulates results across multiple Accept() calls.
        // Call Visit(Document) for a fresh search, which does clear.
        VisitInternal(page);
    }

    internal void VisitInternal(Page page)
    {
        var reader = page.Reader;
        var contentStreams = GetContentStreams(page.Dict, reader);

        var rawFragments = new List<RawTextRun>();
        // Only collect filled rects when the caller has asked for graphics-related results.
        var fillRects = (_textSearchOptions?.SearchForTextRelatedGraphics ?? false)
            ? new List<RawFillRect>()
            : null;

        // Apply page rotation CTM so fragment coordinates are in the viewer's
        // natural coordinate system (same as the public API behaviour).
        var rotCtm = PageRotationCtm(page);

        // Per PDF spec, a page's content streams are logically a single concatenated stream —
        // text state and graphics state must persist across them. Concatenate with a space
        // separator to prevent token adjacency.
        if (contentStreams.Count == 1)
        {
            ExtractRuns(contentStreams[0], page.Dict, reader, rawFragments, inheritedCtm: rotCtm, fillRects: fillRects, useFontEngineEncoding: _textSearchOptions?.UseFontEngineEncoding ?? false);
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
            ExtractRuns(combined, page.Dict, reader, rawFragments, inheritedCtm: rotCtm, fillRects: fillRects, useFontEngineEncoding: _textSearchOptions?.UseFontEngineEncoding ?? false);
        }

        // DEBUG

        var searchRect = _textSearchOptions?.Rectangle;

        if (_searchPhrase is null)
        {
            BuildAllFragmentsFromRuns(rawFragments, searchRect, sourcePage: page,
                sourceForm: null, pageIndex: page.Index, fillRects: fillRects);
        }
        else
        {
            // Search for the phrase in concatenated text, then map matches
            // back to source runs for bounding rectangles
            BuildSearchFragments(rawFragments, page.Index, page, fillRects: fillRects);

            // Apply rectangle filter if set — use start-position containment, not bbox overlap.
            if (searchRect is not null && !searchRect.IsEmpty)
            {
                for (var i = _fragments.Count - 1; i >= 0; i--)
                {
                    var pos = _fragments.GetInternal(i).Position;
                    if (pos is null || !RectangleContainsPoint(searchRect, pos.XIndent, pos.YIndent))
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
        var fillRects = (_textSearchOptions?.SearchForTextRelatedGraphics ?? false)
            ? new List<RawFillRect>()
            : null;
        ExtractRuns(streamBytes, dict, reader, rawFragments, fillRects: fillRects, useFontEngineEncoding: _textSearchOptions?.UseFontEngineEncoding ?? false);

        var searchRect = _textSearchOptions?.Rectangle;

        if (_searchPhrase is null)
        {
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
                    var pos = _fragments.GetInternal(i).Position;
                    if (pos is null || !RectangleContainsPoint(searchRect, pos.XIndent, pos.YIndent))
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
    private void BuildAllFragmentsFromRuns(List<RawTextRun> rawFragments, Rectangle? searchRect,
        Page? sourcePage, XForm? sourceForm, int pageIndex, List<RawFillRect>? fillRects = null)
    {
        foreach (var run in rawFragments)
        {
            if (run.Text == "\r\n") continue;
            var tmScale = Math.Sqrt(run.TmC * run.TmC + run.TmD * run.TmD);
            var effectiveFontSize = tmScale > 0.001 && Math.Abs(tmScale - 1.0) > 0.001
                ? run.FontSize * tmScale
                : run.FontSize;
            var textState = new TextState
            {
                FontSize = (float)effectiveFontSize,
                FontName = run.FontName,
                RenderingMode = (Aspose.Pdf.Text.TextRenderingMode)run.RenderingMode,
                IsBold = run.IsBold,
                IsItalic = run.IsItalic,
                Font = run.FontInfoObj ?? FontInfo.DefaultHelvetica,
                TextRise = run.TextRise,
                IsSuperscript = run.TextRise > 0,
                IsSubscript = run.TextRise < 0,
            };
            textState.SetCapturedForegroundColor(run.FillColor ?? Color.Black);
            textState.StrokingColor = run.StrokingColor;
            var width = run.Width > 0 ? run.Width : EstimateWidth(run.Text, run.FontSize);
            var height = run.FontSize;

            double descentOffset = 0;
            double ascentHeight = height;
            if (run.Metrics is not null && run.Metrics.Descent != 0)
                descentOffset = run.Metrics.Descent * run.FontSize / 1000.0;
            if (run.Metrics is not null && run.Metrics.Ascent > 0)
                ascentHeight = run.Metrics.Ascent * run.FontSize / 1000.0;

            var rectStartX = run.X + run.TmC * descentOffset;
            var rectStartY = run.Y + run.TmD * descentOffset;
            var (rx1, ry1) = ApplyCtm(rectStartX, rectStartY, run.Ctm);
            var endX = run.X + run.TmA * width + run.TmC * ascentHeight;
            var endY = run.Y + run.TmB * width + run.TmD * ascentHeight;
            var (rx2, ry2) = ApplyCtm(endX, endY, run.Ctm);

            var llx = Math.Min(rx1, rx2);
            var lly = Math.Min(ry1, ry2);
            var urx = Math.Max(rx1, rx2);
            var ury = Math.Max(ry1, ry2);

            var rect = new Rectangle(llx, lly, urx, ury);
            var px = rx1;
            var py = ry1;

            if (searchRect is not null && !searchRect.IsEmpty)
            {
                var leftTolerance = 2.0 * height;
                if (!(px >= searchRect.LLX - leftTolerance && px <= searchRect.URX
                      && py >= searchRect.LLY && py <= searchRect.URY))
                    continue;
            }

            var clipText = run.Text;
            var clipX = run.X;
            var clipWidth = width;
            if (searchRect is not null && run.Metrics is not null)
            {
                ClipRunToRect(run, searchRect, ref clipText, ref clipX, ref clipWidth);
                if (clipText.Length == 0) continue;
                var cRectStartX = clipX + run.TmC * descentOffset;
                var cRectStartY = run.Y + run.TmD * descentOffset;
                var (crx1, cry1) = ApplyCtm(cRectStartX, cRectStartY, run.Ctm);
                var cEndX = clipX + run.TmA * clipWidth + run.TmC * ascentHeight;
                var cEndY = run.Y + run.TmB * clipWidth + run.TmD * ascentHeight;
                var (crx2, cry2) = ApplyCtm(cEndX, cEndY, run.Ctm);
                llx = Math.Min(crx1, crx2);
                lly = Math.Min(cry1, cry2);
                urx = Math.Max(crx1, crx2);
                ury = Math.Max(cry1, cry2);
                rect = new Rectangle(llx, lly, urx, ury);
                px = crx1;
                py = cry1;
            }

            var tdx = run.Ctm.A * run.TmA + run.Ctm.C * run.TmB;
            var tdy = run.Ctm.B * run.TmA + run.Ctm.D * run.TmB;

            var rotDeg = RotationFromDirection(tdx, tdy);
            if (rotDeg.HasValue) textState.Rotation = rotDeg.Value;

            // SearchForTextRelatedGraphics: when a fill rect collected from the content stream
            // contains the fragment's text origin, copy its color to the TextState as the
            // background. Search the most recently emitted rect first — later draw order wins
            // for overlapping rects, matching the visible z-order on the page.
            // Assigning to the TextState's backing field directly avoids triggering the
            // save-time rect-injection registration that the public setter performs.
            if (fillRects is not null)
            {
                for (var i = fillRects.Count - 1; i >= 0; i--)
                {
                    var fr = fillRects[i];
                    if (px >= fr.Llx && px <= fr.Urx && py >= fr.Lly && py <= fr.Ury)
                    {
                        textState.SetCapturedBackgroundColor(fr.FillColor);
                        break;
                    }
                }

                var (_, baselineY) = ApplyCtm(run.X, run.Y, run.Ctm);
                if (DetectUnderlineRect(rect, baselineY, effectiveFontSize, fillRects))
                    textState.SetCapturedUnderline(true);
            }

            var frag = new TextFragment(clipText, rect, textState)
            {
                PageIndex = pageIndex,
                Position = new Position(px, py),
                SourcePage = sourcePage,
                Form = sourceForm,
                TextDirX = tdx,
                TextDirY = tdy,
                ExtractionCtm = new Aspose.Pdf.Matrix(run.Ctm.A, run.Ctm.B, run.Ctm.C, run.Ctm.D, run.Ctm.E, run.Ctm.F),
                ReplaceOptions = TextReplaceOptions,
            };
            if (frag.Segments.Count > 0)
            {
                frag.Segments[1].EndCharIndex = clipText.Length - 1;
                // Per-character layout for the whole-run segment. The unclipped case
                // (the common one) maps one character per glyph exactly from the run
                // start; PopulateCharacters bounds the range to the run text length.
                PopulateCharacters(frag.Segments[1], run, 0, clipText.Length - 1);
            }
            _fragments.Add(frag);
        }

        DetectSuperSubscript(_fragments);
    }

    /// <summary>
    /// Visit all pages of a document.
    /// </summary>
    public void Visit(Document pdf)
    {
        var document = pdf;
        _fragments.Clear();

        if (_searchPhrase is null)
        {
            // No search phrase — just extract all fragments page by page
            foreach (var page in document.Pages)
                VisitInternal(page);
            return;
        }

        // Extract runs from all pages first
        var allPageRuns = new List<(Page page, List<RawTextRun> runs)>();
        foreach (var page in document.Pages)
        {
            var reader = page.Reader;
            var contentStreams = GetContentStreams(page.Dict, reader);
            var rawFragments = new List<RawTextRun>();
            var rotCtm = PageRotationCtm(page);
            foreach (var stream in contentStreams)
                ExtractRuns(stream, page.Dict, reader, rawFragments, inheritedCtm: rotCtm, useFontEngineEncoding: _textSearchOptions?.UseFontEngineEncoding ?? false);
            allPageRuns.Add((page, rawFragments));
        }

        // Try per-page search first (most common case)
        foreach (var (page, runs) in allPageRuns)
            BuildSearchFragments(runs, page.Index, page);

        // If per-page search found results, we're done
        if (_fragments.Count > 0) return;

        // No per-page matches — try cross-page search
        BuildCrossPageSearchFragments(allPageRuns);
    }

    /// <summary>
    /// Search for text across page boundaries by concatenating text from all pages.
    /// </summary>
    private void BuildCrossPageSearchFragments(List<(Page page, List<RawTextRun> runs)> allPageRuns)
    {
        // Concatenate text from all pages with \r\n between pages
        var fullText = new StringBuilder();
        // Track: for each char position, which page and which run within that page
        var charMap = new List<(int pageIdx, int runIdx)>();
        var pageRunStartChars = new List<List<int>>(); // per page, per run: start char index

        for (int pi = 0; pi < allPageRuns.Count; pi++)
        {
            var (page, runs) = allPageRuns[pi];
            var runStarts = new List<int>();
            pageRunStartChars.Add(runStarts);

            // Insert page separator (except before first page)
            if (pi > 0 && fullText.Length > 0)
            {
                fullText.Append("\r\n");
                charMap.Add((-1, -1)); // \r
                charMap.Add((-1, -1)); // \n
            }

            for (int ri = 0; ri < runs.Count; ri++)
            {
                // Space insertion between runs on the same line
                if (ri > 0 && runs[ri].Text != "\r\n" && runs[ri - 1].Text != "\r\n")
                {
                    var prev = runs[ri - 1];
                    var deltaY = Math.Abs(runs[ri].Y - prev.Y);
                    if (deltaY < 2.0)
                    {
                        var prevEndX = prev.X + (prev.Width > 0 ? prev.Width : EstimateWidth(prev.Text, prev.FontSize));
                        var gap = runs[ri].X - prevEndX;
                        var fontSize = runs[ri].FontSize > 0 ? runs[ri].FontSize : 12.0;
                        var spaceThreshold = fontSize * 0.2;
                        var maxGap = fontSize * 3.0;
                        var lastChar = fullText.Length > 0 ? fullText[^1] : '\0';
                        var nextChar = runs[ri].Text.Length > 0 ? runs[ri].Text[0] : '\0';
                        // Require a real gap and avoid spacing inside letter-spaced words,
                        // where EVERY run is a single character. The earlier `both runs >= 2
                        // chars` rule was too strict: it also dropped the space at a word↔
                        // single-char-token boundary (e.g. "level" -> "1"), so a phrase search
                        // for "Heading level 1" failed to match the extracted "Heading level1".
                        // Suppress the space only when BOTH sides are single
                        // characters (the genuine letter-spacing case).
                        if (gap > spaceThreshold && gap <= maxGap && fullText.Length > 0
                            && lastChar != ' ' && lastChar != '\n' && nextChar != ' '
                            && (prev.Text.Length >= 2 || runs[ri].Text.Length >= 2))
                        {
                            charMap.Add((pi, ri - 1));
                            fullText.Append(' ');
                        }
                    }
                }

                runStarts.Add(charMap.Count);
                var text = runs[ri].Text;
                // Keep newlines for regex
                foreach (var _ in text)
                    charMap.Add((pi, ri));
                fullText.Append(text);
            }
        }

        var concatenated = fullText.ToString();
        concatenated = NormalizeArabicPresentationForms(concatenated);

        var matches = BuildMatches(concatenated);

        foreach (Match match in matches)
        {
            if (match.Length == 0) continue;

            var startIdx = match.Index;
            var endIdx = match.Index + match.Length - 1;
            if (startIdx >= charMap.Count || endIdx >= charMap.Count) continue;

            // Find the first valid page/run for the match start
            var (startPageIdx, startRunIdx) = charMap[startIdx];
            // Skip separators
            while (startPageIdx < 0 && startIdx <= endIdx)
            {
                startIdx++;
                if (startIdx < charMap.Count) (startPageIdx, startRunIdx) = charMap[startIdx];
            }
            if (startPageIdx < 0) continue;

            var startPage = allPageRuns[startPageIdx].page;
            var startRuns = allPageRuns[startPageIdx].runs;
            if (startRunIdx < 0 || startRunIdx >= startRuns.Count) continue;
            var firstRun = startRuns[startRunIdx];

            // Position from first run
            var (posX, posY) = ApplyCtm(firstRun.X, firstRun.Y, firstRun.Ctm);

            // Effective font size
            var tmScale = Math.Sqrt(firstRun.TmC * firstRun.TmC + firstRun.TmD * firstRun.TmD);
            var effectiveFs = tmScale > 0.001 && Math.Abs(tmScale - 1.0) > 0.001
                ? firstRun.FontSize * tmScale : firstRun.FontSize;

            var textState = new TextState
            {
                FontSize = (float)effectiveFs,
                FontName = firstRun.FontName,
                RenderingMode = (Aspose.Pdf.Text.TextRenderingMode)firstRun.RenderingMode,
                IsBold = firstRun.IsBold,
                IsItalic = firstRun.IsItalic,
                Font = firstRun.FontInfoObj ?? FontInfo.DefaultHelvetica,
                TextRise = firstRun.TextRise,
                IsSuperscript = firstRun.TextRise > 0,
                IsSubscript = firstRun.TextRise < 0,
            };
            textState.SetCapturedForegroundColor(firstRun.FillColor ?? Color.Black);
            textState.StrokingColor = firstRun.StrokingColor;

            // Simple bounding rect from first run
            var w = firstRun.Width > 0 ? firstRun.Width : EstimateWidth(firstRun.Text, firstRun.FontSize);
            var h = firstRun.FontSize;
            var (px2, py2) = ApplyCtm(firstRun.X + w, firstRun.Y + h, firstRun.Ctm);
            var rect = new Rectangle(
                Math.Min(posX, px2), Math.Min(posY, py2),
                Math.Max(posX, px2), Math.Max(posY, py2));

            var fragment = new TextFragment(match.Value, rect, textState)
            {
                PageIndex = startPage.Index,
                Position = new Position(posX, posY),
                SourcePage = startPage,
                ExtractionCtm = new Aspose.Pdf.Matrix(firstRun.Ctm.A, firstRun.Ctm.B, firstRun.Ctm.C, firstRun.Ctm.D, firstRun.Ctm.E, firstRun.Ctm.F),
            };

            _fragments.Add(fragment);
        }
    }

    /// <summary>
    /// Maps character offsets in concatenated text back to source RawTextRun entries
    /// to compute bounding rectangles for search matches.
    /// Three phases: (1) build char→run index, (2) find regex/phrase matches, (3) build fragments.
    /// </summary>
    // Detect a horizontal underline drawn as a thin filled rectangle just below the
    // fragment's baseline. Used by SearchForTextRelatedGraphics. PDF producers commonly
    // emit underlines as `x y w h re f*` after the Tj/TJ that placed the text — these
    // rects are short, just below the baseline, and span (approximately) the run's width.
    private static bool DetectUnderlineRect(Rectangle rect, double baselineY,
        double fontSize, List<RawFillRect> fillRects)
    {
        if (fillRects.Count == 0) return false;
        var fragWidth = rect.URX - rect.LLX;
        if (fragWidth <= 0) return false;
        var maxThickness = Math.Max(1.5, 0.15 * fontSize);
        var maxGap = Math.Max(2.5, 0.4 * fontSize);
        for (var i = fillRects.Count - 1; i >= 0; i--)
        {
            var fr = fillRects[i];
            var h = fr.Ury - fr.Lly;
            if (h > maxThickness) continue;
            if (fr.Ury > baselineY + 0.5) continue;
            if (fr.Ury < baselineY - maxGap) continue;
            var ox = Math.Max(0, Math.Min(rect.URX, fr.Urx) - Math.Max(rect.LLX, fr.Llx));
            if (ox * 2 < fragWidth) continue;
            return true;
        }
        return false;
    }

    /// <summary>Text rotation in degrees from the baseline direction vector (the
    /// text-space x-axis mapped through the text matrix and CTM), measured CCW
    /// from the page x-axis and normalised to [0, 360). Axis-aligned text yields
    /// exactly 0/90/180/270; arbitrary text matrices report their true angle.
    /// Returns null for a degenerate (zero-length) direction.</summary>
    private static double? RotationFromDirection(double tdx, double tdy)
    {
        if (Math.Abs(tdx) <= 1e-9 && Math.Abs(tdy) <= 1e-9) return null;
        var rot = Math.Atan2(tdy, tdx) * 180.0 / Math.PI;
        if (rot < 0) rot += 360.0;
        var snapped = Math.Round(rot);
        if (Math.Abs(rot - snapped) < 1e-6) rot = snapped >= 360 ? 0 : snapped;
        return rot;
    }

    private void BuildSearchFragments(List<RawTextRun> rawFragments, int pageIndex,
        Page? sourcePage = null, XForm? sourceForm = null, List<RawFillRect>? fillRects = null)
    {
        // Phase 1: Build the concatenated text and character-to-run mapping
        var (concatenated, charToRun, runStartChar, bidiPerm) = BuildConcatenatedText(rawFragments);

        // Phase 2: Find matches in the concatenated text
        var matches = BuildMatches(concatenated);

        // Phase 3: For each match, build a TextFragment with position, rect, and segments
        foreach (Match match in matches)
        {
            if (match.Length == 0) continue;

            // Map match indices back through bidi permutation if reordering was applied
            var startCharIdx = bidiPerm is not null ? bidiPerm[match.Index] : match.Index;
            var endCharIdx = bidiPerm is not null
                ? bidiPerm[match.Index + match.Length - 1]
                : match.Index + match.Length - 1;
            if (startCharIdx > endCharIdx)
                (startCharIdx, endCharIdx) = (endCharIdx, startCharIdx);

            if (startCharIdx >= charToRun.Count || endCharIdx >= charToRun.Count)
            {
                _fragments.Add(new TextFragment(match.Value) { PageIndex = pageIndex, SourcePage = sourcePage, Form = sourceForm });
                continue;
            }

            var firstRunIdx = charToRun[startCharIdx];
            var lastRunIdx = charToRun[endCharIdx];

            // Compute bounding rectangle spanning all involved runs
            var rect = ComputeMatchBounds(rawFragments, runStartChar,
                firstRunIdx, lastRunIdx, startCharIdx, endCharIdx);

            // Compute position, text state, and trailing Tc for the fragment
            var (posX, posY) = ComputeMatchPosition(rawFragments[firstRunIdx],
                startCharIdx - runStartChar[firstRunIdx]);
            var firstRun = rawFragments[firstRunIdx];
            var textState = BuildTextState(firstRun);
            var trailingTc = ComputeTrailingTc(rawFragments, runStartChar, lastRunIdx, endCharIdx);

            // Text direction in page space
            var sTdx = firstRun.Ctm.A * firstRun.TmA + firstRun.Ctm.C * firstRun.TmB;
            var sTdy = firstRun.Ctm.B * firstRun.TmA + firstRun.Ctm.D * firstRun.TmB;
            var sRot = RotationFromDirection(sTdx, sTdy);
            if (sRot.HasValue) textState.Rotation = sRot.Value;

            var fragment = new TextFragment(match.Value, rect, textState)
            {
                PageIndex = pageIndex,
                Position = new Position(posX, posY),
                SourcePage = sourcePage,
                Form = sourceForm,
                TextDirX = sTdx, TextDirY = sTdy,
                ExtractionCtm = new Aspose.Pdf.Matrix(firstRun.Ctm.A, firstRun.Ctm.B,
                    firstRun.Ctm.C, firstRun.Ctm.D, firstRun.Ctm.E, firstRun.Ctm.F),
                TrailingTcPageSpace = trailingTc,
                ReplaceOptions = TextReplaceOptions,
            };

            if (fillRects is not null)
            {
                var (_, baselineY) = ApplyCtm(firstRun.X, firstRun.Y, firstRun.Ctm);
                if (DetectUnderlineRect(rect, baselineY, textState.FontSize, fillRects))
                    textState.SetCapturedUnderline(true);
            }

            // Build per-run segments with position and rectangle
            BuildFragmentSegments(fragment, rawFragments, runStartChar,
                firstRunIdx, lastRunIdx, startCharIdx, endCharIdx);

            _fragments.Add(fragment);
        }
    }

    /// <summary>
    /// Concatenates text from raw runs into a single searchable string, inserting
    /// spaces at detected word gaps, removing false newlines at BT/ET boundaries,
    /// and applying bidi reordering + Arabic normalization for phrase search.
    /// </summary>
    private (string text, List<int> charToRun, int[] runStartChar, int[]? bidiPerm)
        BuildConcatenatedText(List<RawTextRun> rawFragments)
    {
        var fullText = new StringBuilder();
        var charToRun = new List<int>();
        var runStartChar = new int[rawFragments.Count];

        // IgnoreShadowText: drop drop-shadow duplicates. A shadow glyph is the SAME character
        // drawn again at a near-overlapping position (a small offset, far less than a glyph
        // advance) — e.g. "Construction" rendered as runs C,C,o,o,n,n,… where each second copy
        // sits ~0.06·fontSize away. Skip a run that repeats the last kept run's text within a
        // fraction of the visual font size, so the search sees "Construction" not
        // "CCoonnssttrruuccttiioonn".
        bool ignoreShadow = _textSearchOptions?.IgnoreShadowText ?? false;
        string? lastKeptText = null; double lastKeptX = 0, lastKeptY = 0;

        for (var i = 0; i < rawFragments.Count; i++)
        {
            if (ignoreShadow)
            {
                var cur = rawFragments[i];
                // A shadow copy of a space is also dropped: position-based matching (overlapping X)
                // distinguishes it from a real inter-word space, which is a full advance away.
                if (cur.Text != "\r\n" && cur.Text.Length > 0
                    && lastKeptText == cur.Text)
                {
                    double effFs = (cur.FontSize > 0 ? cur.FontSize : 1.0) * (Math.Abs(cur.TmA) > 0 ? Math.Abs(cur.TmA) : 12.0);
                    double tol = Math.Max(1.0, 0.22 * effFs);
                    if (Math.Abs(cur.X - lastKeptX) < tol && Math.Abs(cur.Y - lastKeptY) < tol)
                    {
                        // Drop the \r\n sentinel(s) sitting between the kept glyph and this shadow
                        // copy — they only separated a glyph from its own shadow (each glyph is its
                        // own BT/ET), not real content. Otherwise an orphan \r\n can survive inside a
                        // word (e.g. "Constructio\r\nn") and break the match.
                        while (fullText.Length > 0 && (fullText[^1] == '\r' || fullText[^1] == '\n'))
                        {
                            fullText.Length--;
                            charToRun.RemoveAt(charToRun.Count - 1);
                        }
                        runStartChar[i] = charToRun.Count; // shadow duplicate — emit no characters
                        continue;
                    }
                }
            }
            // Detect horizontal gaps between consecutive runs on the same line.
            // Skip \r\n sentinels to find the real previous run — BT/ET boundaries
            // inject \r\n but runs in adjacent BT blocks at the same Y are same-line text.
            if (i > 0 && rawFragments[i].Text != "\r\n")
            {
                int prevIdx = i - 1;
                while (prevIdx >= 0 && rawFragments[prevIdx].Text == "\r\n") prevIdx--;
                if (prevIdx < 0) goto skipSpaceInsert;
                var prev = rawFragments[prevIdx];
                var deltaY = Math.Abs(rawFragments[i].Y - prev.Y);
                if (deltaY < 2.0) // same line
                {
                    // Remove \r\n sentinels between prevIdx and i on the same line —
                    // they were BT/ET boundary artifacts, not real line breaks.
                    if (prevIdx < i - 1)
                    {
                        while (fullText.Length > 0 && (fullText[^1] == '\r' || fullText[^1] == '\n'))
                        {
                            fullText.Length--;
                            charToRun.RemoveAt(charToRun.Count - 1);
                        }
                    }

                    // Insert space if there's a word-sized or column-sized gap
                    var prevEndX = prev.X + (prev.Width > 0 ? prev.Width : EstimateWidth(prev.Text, prev.FontSize));
                    var gap = rawFragments[i].X - prevEndX;
                    var fontSize = rawFragments[i].FontSize > 0 ? rawFragments[i].FontSize : 12.0;
                    var tmScaleX = Math.Abs(rawFragments[i].TmA) > 0 ? Math.Abs(rawFragments[i].TmA) : 1.0;
                    var effFontSize = fontSize * tmScaleX;
                    var lastChar = fullText.Length > 0 ? fullText[^1] : '\0';
                    var nextChar = rawFragments[i].Text.Length > 0 ? rawFragments[i].Text[0] : '\0';
                    bool noPriorSpace = fullText.Length > 0 && lastChar != ' ' && lastChar != '\n' && nextChar != ' ';
                    // Suppress the word-gap space only inside letter-spaced words, where BOTH
                    // sides are single characters. Requiring both runs >= 2 chars also dropped
                    // the space at a word -> single-char-token boundary (e.g. "level" -> "1"),
                    // so a phrase search for "Heading level 1" missed the extracted
                    // "Heading level1".
                    bool insertByWordGap = gap > effFontSize * 0.2 && gap <= effFontSize * 3.0
                        && (prev.Text.Length >= 2 || rawFragments[i].Text.Length >= 2);
                    bool insertByColumnGap = gap > effFontSize * 40.0;
                    // Rotated text (|TmB| > |TmA|, e.g. vertical labels rotated ~90°) advances
                    // along Y, not X, so the X-based `gap` above is meaningless (often negative).
                    // For such runs the cross-axis is X: two runs sharing a baseline (deltaY≈0)
                    // but at clearly different X are distinct columns/labels (e.g. CAD grid
                    // markers "A","B","C" rotated and spread across the sheet), not one word.
                    // Insert a separator so a regex word boundary \b can form between them.
                    // Horizontal text (|TmA| >= |TmB|, incl. curved/kerned words) is unaffected.
                    bool isRotated = Math.Abs(rawFragments[i].TmB) > Math.Abs(rawFragments[i].TmA)
                        && Math.Abs(prev.TmB) > Math.Abs(prev.TmA);
                    var rotScale = Math.Sqrt(rawFragments[i].TmA * rawFragments[i].TmA
                        + rawFragments[i].TmB * rawFragments[i].TmB);
                    var effRotFont = (fontSize > 0 ? fontSize : 12.0) * (rotScale > 0 ? rotScale : 1.0);
                    bool insertByRotatedColumn = isRotated
                        && Math.Abs(rawFragments[i].X - prev.X) > effRotFont * 0.5;
                    if (noPriorSpace && (insertByWordGap || insertByColumnGap || insertByRotatedColumn))
                    {
                        charToRun.Add(prevIdx);
                        fullText.Append(' ');
                    }
                }
            }
            skipSpaceInsert:

            runStartChar[i] = charToRun.Count;
            var text = rawFragments[i].Text;

            // Newline sentinels: skip for phrase search (so cross-line phrases match),
            // keep for regex search (so \r\n patterns work).
            var effectiveIsRegex = _isRegex || (_textSearchOptions?.IsRegularExpression ?? false);
            if (text == "\r\n" && !effectiveIsRegex) continue;

            foreach (var _ in text)
                charToRun.Add(i);
            fullText.Append(text);

            // Track the last kept (appended) non-sentinel run for shadow de-duplication.
            if (ignoreShadow && text != "\r\n")
            {
                lastKeptText = rawFragments[i].Text;
                lastKeptX = rawFragments[i].X;
                lastKeptY = rawFragments[i].Y;
            }
        }

        var concatenated = fullText.ToString();

        // Apply bidi reordering for non-regex search — regex patterns expect logical order.
        int[]? bidiPerm = null;
        var isRegex = _isRegex || (_textSearchOptions?.IsRegularExpression ?? false);
        if (!isRegex)
            concatenated = BidiReorderer.ReorderIfNeeded(concatenated, out bidiPerm);

        // Normalize Arabic Presentation Forms to base Unicode characters
        concatenated = NormalizeArabicPresentationForms(concatenated);

        return (concatenated, charToRun, runStartChar, bidiPerm);
    }

    /// <summary>
    /// Computes the bounding rectangle for a search match spanning runs [firstRunIdx.lastRunIdx].
    /// Handles within-run offsets for partial first/last runs, descent/ascent, and text matrix.
    /// </summary>
    private static Rectangle ComputeMatchBounds(List<RawTextRun> rawFragments, int[] runStartChar,
        int firstRunIdx, int lastRunIdx, int startCharIdx, int endCharIdx)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        for (var ri = firstRunIdx; ri <= lastRunIdx; ri++)
        {
            var run = rawFragments[ri];
            var w = run.Width > 0 ? run.Width : EstimateWidth(run.Text, run.FontSize);

            // Compute descent/ascent offsets for rectangle corners.
            // Standard-14 fonts may omit FontDescriptor; fall back to AFM reference values
            // so the rectangle LLY isn't effectively zero.
            var (descentOff, ascentH) = ComputeDescentAscent(run);

            // For the first run, advance past the prefix to the match start position
            double runStartX = run.X, runStartY = run.Y;
            if (ri == firstRunIdx)
            {
                var offsetInRun = startCharIdx - runStartChar[ri];
                if (offsetInRun > 0 && offsetInRun < run.Text.Length)
                {
                    var prefixWidth = MeasureRunPrefix(run, offsetInRun);
                    runStartX = run.X + run.TmA * prefixWidth * run.HScaling;
                    runStartY = run.Y + run.TmB * prefixWidth * run.HScaling;
                    w -= prefixWidth;
                }
            }

            // For the last run, trim width to end of match
            if (ri == lastRunIdx)
                w = MeasureMatchWidthInRun(run, runStartChar[ri], startCharIdx, endCharIdx, ri == firstRunIdx);

            // Map to page space through text matrix + CTM
            var scaledW = w * run.HScaling;
            var (px, py) = ApplyCtm(runStartX + run.TmC * descentOff,
                                     runStartY + run.TmD * descentOff, run.Ctm);
            var (px2, py2) = ApplyCtm(runStartX + run.TmA * scaledW + run.TmC * ascentH,
                                       runStartY + run.TmB * scaledW + run.TmD * ascentH, run.Ctm);
            minX = Math.Min(minX, Math.Min(px, px2));
            minY = Math.Min(minY, Math.Min(py, py2));
            maxX = Math.Max(maxX, Math.Max(px, px2));
            maxY = Math.Max(maxY, Math.Max(py, py2));
        }

        return new Rectangle(minX, minY, maxX, maxY);
    }

    /// <summary>
    /// Computes the descent and ascent offsets used by the phrase-search rect
    /// calc (<see cref="ComputeMatchBounds"/>). The reference implementation
    /// uses <c>URY = baseline + (1.1 × FontSize + descentOff)</c> as a floor —
    /// a 10% padding above <c>FontSize</c> with the bottom edge at
    /// <c>baseline + descent</c>. When the font's own ascent metric implies a
    /// taller rect (typical for fonts with large <c>usWinAscent</c> from the
    /// embedded TrueType), keep the metric-driven height instead. So
    /// <c>ascentH = max(metric.Ascent × FontSize / 1000, 1.1 × FontSize +
    /// descentOff)</c>.
    /// </summary>
    private static (double descentOff, double ascentH) ComputeDescentAscent(RawTextRun run)
    {
        double effectiveDescent = 0;
        if (run.Metrics is not null && run.Metrics.Descent != 0)
            effectiveDescent = run.Metrics.Descent;
        else if (!string.IsNullOrEmpty(run.FontName))
            effectiveDescent = Standard14Fonts.GetDescent(run.FontName!);

        double descentOff = effectiveDescent * run.FontSize / 1000.0;
        double ascentH = run.FontSize * 1.1 + descentOff;
        // When the font has a real ascent metric (descriptor or embedded font OS/2
        // larger than 1.1 × em), use it instead of the canonical floor — fonts like
        // CourierNewPSMT report usWinAscent ratios > 1.5 × em, and tests written
        // against them assume the metric-driven height. The fallback to FontSize
        // when Metrics.Ascent==0 is intentionally NOT used here: that fallback
        // gave a misleading height for Standard14 phrase searches without a
        // descriptor (e.g. Helvetica-named runs whose absorber rect should match
        // the canonical 1.1 × FontSize).
        if (run.Metrics is not null && run.Metrics.Ascent > 0)
        {
            double metricBased = run.Metrics.Ascent * run.FontSize / 1000.0;
            if (metricBased > ascentH) ascentH = metricBased;
        }
        return (descentOff, ascentH);
    }

    /// <summary>
    /// Measures the width of a prefix (first N characters) within a run.
    /// Uses CharCumWidths when available (exact TJ advances), then font metrics, then proportional.
    /// </summary>
    private static double MeasureRunPrefix(RawTextRun run, int offsetInRun)
    {
        if (run.CharCumWidths is not null && offsetInRun < run.CharCumWidths.Length)
            return run.CharCumWidths[offsetInRun];
        if (run.Metrics is not null)
            return run.Metrics.MeasureString(run.Text[..offsetInRun], run.FontSize);
        var totalW = run.Width > 0 ? run.Width : EstimateWidth(run.Text, run.FontSize);
        return (offsetInRun / (double)run.Text.Length) * totalW;
    }

    /// <summary>
    /// Measures the width of the matched portion within the last run of a match.
    /// Uses CharCumWidths/CharEndPositions for accuracy, falls back to proportional.
    /// CharEndPositions are preferred because they exclude compensation kerning
    /// between the matched region and post-match characters.
    /// </summary>
    private static double MeasureMatchWidthInRun(RawTextRun run, int runStart,
        int startCharIdx, int endCharIdx, bool isAlsoFirstRun)
    {
        var matchEnd = endCharIdx - runStart + 1;
        var offsetStart = isAlsoFirstRun ? startCharIdx - runStart : 0;
        if (matchEnd > run.Text.Length)
            return run.Width > 0 ? run.Width : EstimateWidth(run.Text, run.FontSize);

        var totalRunW = run.Width > 0 ? run.Width : EstimateWidth(run.Text, run.FontSize);
        if (run.CharCumWidths is not null && offsetStart < run.CharCumWidths.Length)
        {
            var startW = run.CharCumWidths[offsetStart];
            double endW;
            if (matchEnd - 1 >= 0 && run.CharEndPositions is not null
                && matchEnd - 1 < run.CharEndPositions.Length)
                endW = run.CharEndPositions[matchEnd - 1];
            else
                endW = matchEnd < run.CharCumWidths.Length ? run.CharCumWidths[matchEnd] : totalRunW;
            return endW - startW;
        }
        // Proportional fallback — avoids MeasureString(string) encoding issues
        return ((matchEnd - offsetStart) / (double)run.Text.Length) * totalRunW;
    }

    /// <summary>
    /// Computes the page-space position of a match start within its first run.
    /// Applies within-run prefix offset, text matrix, descent, and CTM.
    /// </summary>
    private static (double x, double y) ComputeMatchPosition(RawTextRun firstRun, int offsetInRun)
    {
        double matchStartX = firstRun.X, matchStartY = firstRun.Y;
        if (offsetInRun > 0 && offsetInRun < firstRun.Text.Length)
        {
            var prefixW = MeasureRunPrefix(firstRun, offsetInRun);
            matchStartX = firstRun.X + firstRun.TmA * prefixW * firstRun.HScaling;
            matchStartY = firstRun.Y + firstRun.TmB * prefixW * firstRun.HScaling;
        }
        // Apply descent offset (bottom-left of text rect, matching per-run path).
        double posDescentOff = 0;
        if (firstRun.Metrics is not null && firstRun.Metrics.Descent != 0)
            posDescentOff = firstRun.Metrics.Descent * firstRun.FontSize / 1000.0;
        else if (Math.Abs(firstRun.TmB) > Math.Abs(firstRun.TmA))
            // Rotated run with no descriptor descent: fall back to the Standard-14 metric
            // (same as the rectangle path) so the baseline→descent offset is applied along
            // the rotated baseline. Without it the fragment Position is off by ~descent.
            // Gated to rotated runs to leave the (verified) horizontal-text positions intact.
            (posDescentOff, _) = ComputeDescentAscent(firstRun);
        return ApplyCtm(matchStartX + firstRun.TmC * posDescentOff,
                        matchStartY + firstRun.TmD * posDescentOff, firstRun.Ctm);
    }

    /// <summary>Builds a TextState from the first run's font properties.</summary>
    private static TextState BuildTextState(RawTextRun run)
    {
        var tmScale = Math.Sqrt(run.TmC * run.TmC + run.TmD * run.TmD);
        var effectiveFs = tmScale > 0.001 && Math.Abs(tmScale - 1.0) > 0.001
            ? run.FontSize * tmScale : run.FontSize;
        var ts = new TextState
        {
            FontSize = (float)effectiveFs,
            FontName = run.FontName,
            RenderingMode = (Aspose.Pdf.Text.TextRenderingMode)run.RenderingMode,
            IsBold = run.IsBold,
            IsItalic = run.IsItalic,
            Font = run.FontInfoObj ?? FontInfo.DefaultHelvetica,
            TextRise = run.TextRise,
            IsSuperscript = run.TextRise > 0,
            IsSubscript = run.TextRise < 0,
        };
        ts.SetCapturedForegroundColor(run.FillColor ?? Color.Black);
        ts.StrokingColor = run.StrokingColor;
        return ts;
    }

    /// <summary>
    /// Computes the trailing Tc/spacing contribution at the end of the last matched run.
    /// This value is subtracted from bg rect width so it covers only visible text.
    /// </summary>
    private static double ComputeTrailingTc(List<RawTextRun> rawFragments, int[] runStartChar,
        int lastRunIdx, int endCharIdx)
    {
        var lastRun = rawFragments[lastRunIdx];
        var matchEndInRun = endCharIdx - runStartChar[lastRunIdx] + 1;
        if (matchEndInRun >= 2
            && lastRun.CharCumWidths is not null && matchEndInRun < lastRun.CharCumWidths.Length
            && lastRun.Metrics is not null)
        {
            var lastCharAdvance = lastRun.CharCumWidths[matchEndInRun] - lastRun.CharCumWidths[matchEndInRun - 1];
            var lastCharText = lastRun.Text[(matchEndInRun - 1)..matchEndInRun];
            var lastGlyphW = lastRun.Metrics.MeasureString(lastCharText, lastRun.FontSize);
            var tcUnscaled = lastCharAdvance - lastGlyphW;
            if (tcUnscaled > 0.01)
                return tcUnscaled * lastRun.HScaling * Math.Abs(lastRun.TmA);
        }
        return 0;
    }

    /// <summary>
    /// Builds per-source-run TextSegments for a fragment, each with accurate
    /// position, rectangle, and text state derived from its source run.
    /// </summary>
    private static void BuildFragmentSegments(TextFragment fragment, List<RawTextRun> rawFragments,
        int[] runStartChar, int firstRunIdx, int lastRunIdx, int startCharIdx, int endCharIdx)
    {
        fragment.Segments.Clear();
        for (var ri = firstRunIdx; ri <= lastRunIdx; ri++)
        {
            var run = rawFragments[ri];
            if (run.Text == "\r\n") continue; // skip newline sentinels

            // Determine the portion of this run that is part of the match
            var runStart = runStartChar[ri];
            var segStartInRun = (ri == firstRunIdx) ? startCharIdx - runStart : 0;
            var segEndInRun = (ri == lastRunIdx) ? endCharIdx - runStart : run.Text.Length - 1;
            if (segStartInRun < 0) segStartInRun = 0;
            if (segEndInRun >= run.Text.Length) segEndInRun = run.Text.Length - 1;
            if (segEndInRun < segStartInRun) continue;

            var segText = run.Text.Substring(segStartInRun, segEndInRun - segStartInRun + 1);
            var seg = BuildSegment(run, segText, segStartInRun, segEndInRun, ri);

            // Compute segment position with within-run offset and descent
            seg.Position = ComputeSegmentPosition(run, segStartInRun);

            // Compute segment bounding rectangle
            seg.Rectangle = ComputeSegmentRectangle(run, segText, segStartInRun, segEndInRun);

            // Populate per-character layout (position + glyph rectangle).
            PopulateCharacters(seg, run, segStartInRun, segEndInRun);

            fragment.Segments.Add(seg);
        }
        if (fragment.Segments.Count == 0)
            fragment.Segments.Add(new TextSegment(fragment.Text));
    }

    /// <summary>Creates a TextSegment from a run with text state properties.</summary>
    private static TextSegment BuildSegment(RawTextRun run, string text,
        int startInRun, int endInRun, int runIndex)
    {
        var tmScale = Math.Sqrt(run.TmC * run.TmC + run.TmD * run.TmD);
        var effectiveFs = tmScale > 0.001 && Math.Abs(tmScale - 1.0) > 0.001
            ? run.FontSize * tmScale : run.FontSize;
        var seg = new TextSegment(text)
        {
            StartCharIndex = startInRun,
            EndCharIndex = endInRun,
            SourceRunIndex = runIndex,
        };
        seg.TextState.FontSize = (float)effectiveFs;
        seg.TextState.RawFontSize = (float)run.FontSize;
        seg.TextState.TmD = run.TmD;
        seg.TextState.FontName = run.FontName;
        seg.TextState.RenderingMode = (Aspose.Pdf.Text.TextRenderingMode)run.RenderingMode;
        seg.TextState.StrokingColor = run.StrokingColor;
        seg.TextState.IsBold = run.IsBold;
        seg.TextState.IsItalic = run.IsItalic;
        seg.TextState.Font = run.FontInfoObj ?? FontInfo.DefaultHelvetica;
        seg.TextState.TextRise = run.TextRise;
        seg.TextState.IsSuperscript = run.TextRise > 0;
        seg.TextState.IsSubscript = run.TextRise < 0;
        seg.TextState.OwnerSegment = seg;
        return seg;
    }

    /// <summary>Fills <see cref="TextSegment.Characters"/> with one entry per
    /// character in the segment, each carrying the character's page-space position
    /// and glyph bounding rectangle. Reuses the segment position/rectangle math
    /// applied to a single-character range.</summary>
    /// <summary>
    /// Some embedded/subset fonts can't measure individual glyphs — per-character
    /// advance comes back as 0 even though the run's total width is correct — which
    /// collapses the cumulative-width array to <c>[0,…,0,total]</c>. That would place
    /// every character but the last at the run origin (breaking per-char
    /// <see cref="CharInfo.Rectangle"/> and, in turn, marked-text extraction). When
    /// that degenerate shape is detected, distribute the total width evenly across
    /// the characters. No-op for well-formed arrays.
    /// </summary>
    private static void NormalizeDegenerateCumWidths(double[]? cum)
    {
        if (cum is not { Length: > 2 }) return;
        var total = cum[cum.Length - 1];
        if (total <= 0) return;
        var degenerate = false;
        for (var i = 1; i < cum.Length - 1; i++)
            if (cum[i] <= 0) { degenerate = true; break; }
        if (!degenerate) return;
        var n = cum.Length - 1;
        for (var i = 0; i <= n; i++) cum[i] = total * i / n;
    }

    private static void PopulateCharacters(TextSegment seg, RawTextRun run,
        int segStartInRun, int segEndInRun)
    {
        seg.Characters.Clear();
        for (var ci = segStartInRun; ci <= segEndInRun && ci < run.Text.Length; ci++)
        {
            var charText = run.Text.Substring(ci, 1);
            var pos = ComputeSegmentPosition(run, ci);
            var rect = ComputeSegmentRectangle(run, charText, ci, ci);
            seg.Characters.Add(new CharInfo(pos, rect));
        }
    }

    /// <summary>Computes a segment's page-space position from its run and within-run offset.</summary>
    private static Position ComputeSegmentPosition(RawTextRun run, int segStartInRun)
    {
        double segX = run.X, segY = run.Y;
        if (segStartInRun > 0 && segStartInRun < run.Text.Length)
        {
            var prefW = MeasureRunPrefix(run, segStartInRun);
            segX = run.X + run.TmA * prefW * run.HScaling;
            segY = run.Y + run.TmB * prefW * run.HScaling;
        }
        // Apply descent offset — fall back to Standard-14 AFM descent
        double segDescentOff = 0;
        double effectiveDescent = 0;
        if (run.Metrics is not null && run.Metrics.Descent != 0)
            effectiveDescent = run.Metrics.Descent;
        else if (!string.IsNullOrEmpty(run.FontName))
            effectiveDescent = Standard14Fonts.GetDescent(run.FontName!);
        if (effectiveDescent != 0)
            segDescentOff = effectiveDescent * run.FontSize / 1000.0;
        var (px, py) = ApplyCtm(segX + run.TmC * segDescentOff,
                                 segY + run.TmD * segDescentOff, run.Ctm);
        return new Position(px, py);
    }

    /// <summary>Computes a segment's bounding rectangle from its run, text, and character range.</summary>
    private static Rectangle ComputeSegmentRectangle(RawTextRun run, string segText,
        int segStartInRun, int segEndInRun)
    {
        double segW;
        if (run.CharCumWidths is not null)
        {
            var segEndPos = Math.Min(segEndInRun + 1, run.CharCumWidths.Length - 1);
            segW = run.CharCumWidths[segEndPos]
                 - (segStartInRun < run.CharCumWidths.Length ? run.CharCumWidths[segStartInRun] : 0);
        }
        else if (run.Metrics is not null)
            segW = run.Metrics.MeasureString(segText, run.FontSize);
        else
            segW = EstimateWidth(segText, run.FontSize);

        double segAscentH = run.FontSize;
        if (run.Metrics is not null && run.Metrics.Ascent > 0)
            segAscentH = run.Metrics.Ascent * run.FontSize / 1000.0;
        var (descentOff, _) = ComputeDescentAscent(run);

        double segX = run.X, segY = run.Y;
        if (segStartInRun > 0 && segStartInRun < run.Text.Length)
        {
            var prefW = MeasureRunPrefix(run, segStartInRun);
            segX = run.X + run.TmA * prefW * run.HScaling;
            segY = run.Y + run.TmB * prefW * run.HScaling;
        }
        var scaledSegW = segW * run.HScaling;
        var (x1, y1) = ApplyCtm(segX + run.TmC * descentOff, segY + run.TmD * descentOff, run.Ctm);
        var (x2, y2) = ApplyCtm(segX + run.TmA * scaledSegW + run.TmC * segAscentH,
                                 segY + run.TmB * scaledSegW + run.TmD * segAscentH, run.Ctm);
        return new Rectangle(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));
    }

    private MatchCollection BuildMatches(string text)
    {
        // Check TextSearchOptions at search time (may have been set after construction)
        var isRegex = _isRegex || (_textSearchOptions?.IsRegularExpression ?? false);
        var caseSensitive = _textSearchOptions is not null ? _textSearchOptions.CaseSensitive : _caseSensitive;
        var wholeWord = _wholeWord || (_textSearchOptions?.WholeWord ?? false);

        var phrase = NormalizeArabicPresentationForms(_searchPhrase!);
        // For non-regex search, strip trailing \r that may come from splitting \r\n text by \n.
        // Newline sentinels are excluded from concatenated text in phrase mode, so trailing
        // \r would cause a false mismatch.
        if (!isRegex)
            phrase = phrase.TrimEnd('\r');
        var pattern = isRegex ? phrase : Regex.Escape(phrase);
        if (wholeWord)
            pattern = @"\b" + pattern + @"\b";
        var options = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
        // Enable multiline so ^ and $ match at line boundaries, not just string start/end.
        // This matches the .NET the public API behavior for regex text search.
        if (isRegex)
            options |= RegexOptions.Multiline;
        // Apply the global RegexManager settings: NonBacktracking guarantees linear-time
        // matching, and MatchTimeout bounds runaway (catastrophic-backtracking) patterns.
        if (RegexManager.NonBacktracking)
            options |= RegexOptions.NonBacktracking;
        return new Regex(pattern, options, RegexManager.MatchTimeout).Matches(text);
    }

    /// <summary>
    /// A simple 3x2 affine matrix (a, b, c, d, e, f) for CTM tracking.
    /// Represents the transformation: [a b 0; c d 0; e f 1]
    /// </summary>
    private readonly record struct Matrix(double A, double B, double C, double D, double E, double F)
    {
        public static readonly Matrix Identity = new(1, 0, 0, 1, 0, 0);

        /// <summary>
        /// Multiply this matrix by another: this * other
        /// </summary>
        public Matrix Multiply(Matrix other)
        {
            return new Matrix(
                A * other.A + B * other.C,
                A * other.B + B * other.D,
                C * other.A + D * other.C,
                C * other.B + D * other.D,
                E * other.A + F * other.C + other.E,
                E * other.B + F * other.D + other.F
            );
        }
    }

    /// <summary>
    /// Apply a CTM matrix to a point.
    /// </summary>
    private static (double x, double y) ApplyCtm(double x, double y, Matrix ctm)
    {
        var tx = ctm.A * x + ctm.C * y + ctm.E;
        var ty = ctm.B * x + ctm.D * y + ctm.F;
        return (tx, ty);
    }

    /// <summary>
    /// Compute the page-rotation CTM for a page, matching the TypeScript
    /// <c>pageRotationCtm</c> function.  Returns null for Rotate=0/unset.
    /// </summary>
    private static Matrix? PageRotationCtm(Page page)
    {
        var rotate = ((page.RotateDegrees % 360) + 360) % 360;
        if (rotate == 0) return null;
        var mb = page.MediaBox;
        var w = mb.URX - mb.LLX;
        var h = mb.URY - mb.LLY;
        return rotate switch
        {
            90  => new Matrix( 0, -1,  1,  0,  0, w),
            180 => new Matrix(-1,  0,  0, -1,  w, h),
            270 => new Matrix( 0,  1, -1,  0,  h, 0),
            _   => null,
        };
    }

    /// <summary>
    /// Check if two rectangles overlap (share any area).
    /// </summary>
    private static bool RectanglesOverlap(Rectangle a, Rectangle b)
    {
        // A fragment is included when its vertical center falls within the search rectangle's
        // Y bounds AND it overlaps with the X bounds. This prevents counting fragments whose
        // baseline just clips the rectangle edge.
        var aCenterY = (a.LLY + a.URY) / 2.0;
        if (aCenterY < b.LLY || aCenterY > b.URY) return false;
        if (a.URX < b.LLX || a.LLX > b.URX) return false;
        return true;
    }

    /// <summary>
    /// Check if the given point is contained within (or on the boundary of) a rectangle.
    /// </summary>
    private static bool RectangleContainsPoint(Rectangle rect, double x, double y)
        => x >= rect.LLX && x <= rect.URX && y >= rect.LLY && y <= rect.URY;

    /// <summary>
    /// Clip a text run to fit within a search rectangle (horizontal text only).
    /// Trims characters from left/right whose page-space X falls outside the rect.
    /// Uses CharCumWidths (which include Tc/Tw) for accurate character positions.
    /// </summary>
    private static void ClipRunToRect(RawTextRun run, Rectangle searchRect,
        ref string text, ref double startX, ref double width)
    {
        if (text.Length == 0) return;

        // Build per-character page-space X positions using CharCumWidths (includes Tc/Tw).
        // Fall back to glyph-only widths when CumWidths not available.
        var charPageX = new double[text.Length + 1];
        if (run.CharCumWidths is not null && run.CharCumWidths.Length > text.Length)
        {
            for (int i = 0; i <= text.Length; i++)
            {
                var cumW = run.CharCumWidths[i];
                var (px, _) = ApplyCtm(run.X + run.TmA * cumW * run.HScaling,
                    run.Y + run.TmB * cumW * run.HScaling, run.Ctm);
                charPageX[i] = px;
            }
        }
        else
        {
            // No per-char cumulative widths: distribute total run width proportionally.
            // MeasureString(string) can return wrong widths for custom-encoded fonts,
            // but run.Width (computed from MeasureString(bytes)) is accurate.
            var totalW = run.Width > 0 ? run.Width : EstimateWidth(text, run.FontSize);
            for (int i = 0; i <= text.Length; i++)
            {
                var cumW = totalW * i / text.Length;
                var (px, _) = ApplyCtm(run.X + run.TmA * cumW * run.HScaling,
                    run.Y + run.TmB * cumW * run.HScaling, run.Ctm);
                charPageX[i] = px;
            }
        }

        // Use tight tolerance for left clip (include chars AT or after rect.LLX)
        // and loose tolerance for right clip.
        var rightTol = 0.5;

        // Find first character that starts within or near the rect left edge.
        // Include characters whose midpoint is within the rect (more than half
        // of the glyph is visible).
        int clipStart = 0;
        for (int i = 0; i < text.Length; i++)
        {
            var charMid = (charPageX[i] + charPageX[i + 1]) * 0.5;
            if (charMid >= searchRect.LLX)
            {
                clipStart = i;
                break;
            }
            clipStart = i + 1;
        }

        // Find last character whose END position is within the rect right edge
        int clipEnd = text.Length;
        for (int i = text.Length - 1; i >= clipStart; i--)
        {
            if (charPageX[i + 1] <= searchRect.URX + rightTol)
            {
                clipEnd = i + 1;
                break;
            }
        }


        if (clipStart >= clipEnd)
        {
            text = "";
            return;
        }
        if (clipStart == 0 && clipEnd == text.Length)
            return; // no clipping needed

        // Use CumWidths for the prefix offset and clipped width
        double prefAdv, clipAdv;
        if (run.CharCumWidths is not null && run.CharCumWidths.Length > text.Length)
        {
            prefAdv = run.CharCumWidths[clipStart];
            clipAdv = run.CharCumWidths[clipEnd] - run.CharCumWidths[clipStart];
        }
        else
        {
            // Proportional distribution from total run width.
            // text is already clipped; run.Text has the original full text.
            var totalW = run.Width > 0 ? run.Width : EstimateWidth(run.Text, run.FontSize);
            prefAdv = totalW * clipStart / run.Text.Length;
            clipAdv = totalW * text.Length / run.Text.Length;
        }

        text = text[clipStart..clipEnd];
        startX = run.X + run.TmA * prefAdv * run.HScaling;
        width = clipAdv;
    }

    private static double EstimateWidth(string text, double fontSize)
    {
        return text.Length * fontSize * 0.5;
    }

    private static void ExtractRuns(byte[] streamBytes, PdfDictionary resourceDict,
        PdfReader reader, List<RawTextRun> result, int depth = 0,
        Matrix? inheritedCtm = null, List<RawFillRect>? fillRects = null,
        bool useFontEngineEncoding = false)
    {
        if (depth > 10) return; // prevent infinite recursion
        var fonts = ResolveFonts(resourceDict, reader);
        var lexer = new PdfLexer(streamBytes);
        var operands = new List<PdfObject>();
        string? currentFontName = null;
        Dictionary<int, string>? toUnicode = null;
        PdfDictionary? fontDict = null;
        FontMetrics? metrics = null;
        Font? currentFontInfo = null;
        double fontSize = 12;
        double tx = 0, ty = 0;
        double txLine = 0, tyLine = 0; // Line matrix start (e,f of Tm; updated by Td/TD/T*)
        double leading = 0; // Text leading for T*, ', "
        double charSpacing = 0; // Tc operator
        double wordSpacing = 0; // Tw operator
        double hScaling = 1.0; // Tz operator (percentage / 100)
        double textRise = 0; // Ts operator (superscript/subscript offset)
        // Text matrix components (a, b, c, d) — updated by Tm.
        // Needed to correctly scale Td/TD/T* displacements (values are in unscaled text space).
        double tmA = 1.0, tmB = 0.0, tmC = 0.0, tmD = 1.0;

        // CTM stack for q/Q/cm operators; inherit from parent context if provided
        var ctm = inheritedCtm ?? Matrix.Identity;
        var ctmStack = new Stack<Matrix>();

        // Graphics state stack — save/restore text state across q/Q.
        // Per PDF spec, the graphics state includes text parameters. We save the simple
        // scalar parameters here (leading, spacing, scaling); font/font-size changes
        // within q/Q blocks are generally followed by a Tf that resets them, so we
        // don't try to restore font dict/metrics. Nonstroking color is part of the
        // graphics state and must be saved/restored alongside text params.
        var gsStack = new Stack<(double leading, double charSpacing, double wordSpacing,
            double hScaling, double textRise, int renderMode, Color fillColor, Color? strokeColor)>();

        // Nonstroking (fill) color tracking for SearchForTextRelatedGraphics.
        // Updated by g/rg/k/sc/scn; saved/restored on q/Q.
        var currentFillColor = Color.Black;

        // Stroking color tracking — captured onto each run's StrokingColor so a
        // round-tripped TextState.StrokingColor survives. Updated by G/RG/K/SC/SCN.
        Color? currentStrokeColor = null;

        // Pending path fragments since the last path-painting operator.
        // We only classify the path as a "rectangle fill" when it contains
        // at least one re and no other path-construction operator (m/l/c/v/y/h).
        // CTM is captured at the time of re so a subsequent cm doesn't shift the rect.
        var pendingPathRects = new List<(double x, double y, double w, double h, Matrix ctmAtRe)>();
        var currentPathHasNonRect = false;

        // Text rendering mode (0=fill, 3=invisible, etc.)
        int renderMode = 0;

        // Font style flags (resolved from font descriptor or BaseFont name)
        bool currentIsBold = false;
        bool currentIsItalic = false;
        // Font-intrinsic bold state (from descriptor/name), separate from Tr-based bold
        bool fontIsBold = false;

        // Track the Y position of the last actually-emitted text run.
        // Used by the Tm handler to avoid false "\n" sentinels when BT resets ty=0
        // but the next text block is on the same visual line as the previous one.
        double lastEmittedY = double.NaN;

        while (true)
        {
            var token = lexer.NextToken();
            if (token.Kind == TokenKind.Eof) break;

            switch (token.Kind)
            {
                case TokenKind.Integer:
                    operands.Add(new PdfInteger(token.IntValue));
                    break;
                case TokenKind.Real:
                    operands.Add(new PdfReal(token.RealValue));
                    break;
                case TokenKind.LiteralString:
                    operands.Add(new PdfString(token.BytesValue!));
                    break;
                case TokenKind.HexString:
                    operands.Add(new PdfString(token.BytesValue!, isHex: true));
                    break;
                case TokenKind.Name:
                    operands.Add(new PdfName(token.StringValue!));
                    break;
                case TokenKind.ArrayStart:
                {
                    var arr = ParseArray(lexer);
                    operands.Add(arr);
                    break;
                }
                case TokenKind.Keyword:
                {
                    var op = token.StringValue!;
                    switch (op)
                    {
                        // ── Graphics state: CTM tracking ──
                        case "q":
                            ctmStack.Push(ctm);
                            // Save text state parameters (part of the graphics state per PDF spec).
                            gsStack.Push((leading, charSpacing, wordSpacing, hScaling, textRise, renderMode, currentFillColor, currentStrokeColor));
                            break;
                        case "Q":
                            if (ctmStack.Count > 0)
                                ctm = ctmStack.Pop();
                            if (gsStack.Count > 0)
                            {
                                var saved = gsStack.Pop();
                                leading = saved.leading;
                                charSpacing = saved.charSpacing;
                                wordSpacing = saved.wordSpacing;
                                hScaling = saved.hScaling;
                                textRise = saved.textRise;
                                renderMode = saved.renderMode;
                                currentFillColor = saved.fillColor;
                                currentStrokeColor = saved.strokeColor;
                            }
                            break;
                        case "cm":
                            if (operands.Count >= 6)
                            {
                                var m = new Matrix(
                                    GetNum(operands[0]), GetNum(operands[1]),
                                    GetNum(operands[2]), GetNum(operands[3]),
                                    GetNum(operands[4]), GetNum(operands[5]));
                                ctm = m.Multiply(ctm);
                            }
                            break;

                        // ── Nonstroking (fill) color operators ──
                        // Tracked unconditionally so each fragment's ForegroundColor
                        // reflects the glyph fill colour in effect when it was drawn,
                        // not only under SearchForTextRelatedGraphics.
                        case "g":
                            if (operands.Count >= 1)
                                currentFillColor = Color.FromGray(GetNum(operands[0]));
                            break;
                        case "rg":
                            if (operands.Count >= 3)
                                currentFillColor = Color.FromRgb(
                                    GetNum(operands[0]), GetNum(operands[1]), GetNum(operands[2]));
                            break;
                        case "k":
                            if (operands.Count >= 4)
                                currentFillColor = Color.FromCmyk(
                                    GetNum(operands[0]), GetNum(operands[1]),
                                    GetNum(operands[2]), GetNum(operands[3]));
                            break;
                        case "sc":
                        case "scn":
                            // Pick by operand count: 1=gray, 3=rgb, 4=cmyk.
                            // Pattern color spaces (where scn takes a /Name) fall through unchanged.
                            if (operands.Count == 1 && operands[0] is not PdfName)
                                currentFillColor = Color.FromGray(GetNum(operands[0]));
                            else if (operands.Count == 3)
                                currentFillColor = Color.FromRgb(
                                    GetNum(operands[0]), GetNum(operands[1]), GetNum(operands[2]));
                            else if (operands.Count == 4)
                                currentFillColor = Color.FromCmyk(
                                    GetNum(operands[0]), GetNum(operands[1]),
                                    GetNum(operands[2]), GetNum(operands[3]));
                            break;

                        // ── Stroking color operators ──
                        // Captured unconditionally onto each run's StrokingColor so a
                        // round-tripped TextState.StrokingColor (e.g. stroked text) survives.
                        case "G":
                            if (operands.Count >= 1)
                                currentStrokeColor = Color.FromGray(GetNum(operands[0]));
                            break;
                        case "RG":
                            if (operands.Count >= 3)
                                currentStrokeColor = Color.FromRgb(
                                    GetNum(operands[0]), GetNum(operands[1]), GetNum(operands[2]));
                            break;
                        case "K":
                            if (operands.Count >= 4)
                                currentStrokeColor = Color.FromCmyk(
                                    GetNum(operands[0]), GetNum(operands[1]),
                                    GetNum(operands[2]), GetNum(operands[3]));
                            break;
                        case "SC":
                        case "SCN":
                            if (operands.Count == 1 && operands[0] is not PdfName)
                                currentStrokeColor = Color.FromGray(GetNum(operands[0]));
                            else if (operands.Count == 3)
                                currentStrokeColor = Color.FromRgb(
                                    GetNum(operands[0]), GetNum(operands[1]), GetNum(operands[2]));
                            else if (operands.Count == 4)
                                currentStrokeColor = Color.FromCmyk(
                                    GetNum(operands[0]), GetNum(operands[1]),
                                    GetNum(operands[2]), GetNum(operands[3]));
                            break;

                        // ── Path construction ──
                        case "re":
                            if (fillRects is not null && operands.Count >= 4)
                                pendingPathRects.Add((
                                    GetNum(operands[0]), GetNum(operands[1]),
                                    GetNum(operands[2]), GetNum(operands[3]), ctm));
                            break;
                        case "m":
                        case "l":
                        case "c":
                        case "v":
                        case "y":
                        case "h":
                            if (fillRects is not null) currentPathHasNonRect = true;
                            break;

                        // ── Path painting: emit pending rects as fill rects. ──
                        // f/F/f*/B/b/B*/b* paint the current path (with fill); n/S/s do not fill.
                        case "f":
                        case "F":
                        case "f*":
                        case "B":
                        case "B*":
                        case "b":
                        case "b*":
                            if (fillRects is not null && !currentPathHasNonRect && pendingPathRects.Count > 0)
                            {
                                foreach (var (x, y, w, h, ctmAtRe) in pendingPathRects)
                                {
                                    // Transform the four corners by the CTM at the time of re,
                                    // then take the axis-aligned bounding box (handles rotation).
                                    var (x1, y1) = ApplyCtm(x, y, ctmAtRe);
                                    var (x2, y2) = ApplyCtm(x + w, y, ctmAtRe);
                                    var (x3, y3) = ApplyCtm(x + w, y + h, ctmAtRe);
                                    var (x4, y4) = ApplyCtm(x, y + h, ctmAtRe);
                                    var llx = Math.Min(Math.Min(x1, x2), Math.Min(x3, x4));
                                    var lly = Math.Min(Math.Min(y1, y2), Math.Min(y3, y4));
                                    var urx = Math.Max(Math.Max(x1, x2), Math.Max(x3, x4));
                                    var ury = Math.Max(Math.Max(y1, y2), Math.Max(y3, y4));
                                    fillRects.Add(new RawFillRect(llx, lly, urx, ury, currentFillColor));
                                }
                            }
                            pendingPathRects.Clear();
                            currentPathHasNonRect = false;
                            break;
                        case "n":
                        case "S":
                        case "s":
                            pendingPathRects.Clear();
                            currentPathHasNonRect = false;
                            break;

                        // ── Text block delimiters ──
                        case "BT":
                            // PDF spec: BT resets the text matrix and text line matrix to identity.
                            // Reset text position and matrix components so subsequent Td/TD/Tm start fresh.
                            // Do NOT reset lastEmittedY — it tracks cross-BT-block Y position
                            // to prevent spurious newline sentinels between adjacent BT blocks.
                            tx = txLine = 0;
                            ty = tyLine = 0;
                            tmA = 1.0; tmB = 0.0; tmC = 0.0; tmD = 1.0;
                            break;

                        // ── Text state operators ──
                        case "Tf":
                            if (operands.Count >= 2 && operands[0] is PdfName fn)
                            {
                                currentFontName = fn.Value;
                                fontSize = GetNum(operands[1]);
                                currentIsBold = false;
                                fontIsBold = false;
                                currentIsItalic = false;
                                if (fonts.TryGetValue(currentFontName, out var fd))
                                {
                                    fontDict = fd;
                                    // Prefer BaseFont name (e.g. "ArialMT") over resource key (e.g. "TT2")
                                    var baseFontName = fd.GetName("BaseFont");
                                    if (baseFontName is not null)
                                        currentFontName = baseFontName;
                                    // UseFontEngineEncoding: ignore /ToUnicode and decode via
                                    // the font program's own encoding/cmap instead (recovers
                                    // text when the ToUnicode map is wrong or absent).
                                    toUnicode = useFontEngineEncoding
                                        ? null
                                        : TextAbsorber.ParseToUnicodeFromDict(fd, reader);
                                    metrics = FontMetrics.FromFontDict(fd, reader);

                                    // Create FontInfo from the resolved font dictionary
                                    currentFontInfo = new Font(fn.Value, fd, reader);

                                    // Resolve bold/italic from font descriptor flags
                                    var descriptor = reader.ResolveDict(fd.Get("FontDescriptor"));
                                    if (descriptor is not null)
                                    {
                                        var flagsVal = (int)descriptor.GetInt("Flags");
                                        currentIsItalic = (flagsVal & 64) != 0;
                                        currentIsBold = (flagsVal & (1 << 18)) != 0;
                                    }
                                    // Also check BaseFont name for bold/italic hints
                                    if (baseFontName is not null)
                                    {
                                        var upper = baseFontName.ToUpperInvariant();
                                        if (!currentIsBold && (upper.Contains("BOLD") || upper.Contains(",BOLD")))
                                            currentIsBold = true;
                                        if (!currentIsItalic && (upper.Contains("ITALIC") || upper.Contains("OBLIQUE") || upper.Contains(",ITALIC")))
                                            currentIsItalic = true;
                                    }
                                    fontIsBold = currentIsBold;
                                    // Apply Tr-based bold if current render mode is fill+stroke
                                    if (renderMode == 2)
                                        currentIsBold = true;
                                }
                            }
                            break;
                        case "TL":
                            if (operands.Count >= 1)
                                leading = GetNum(operands[0]);
                            break;
                        case "Tr":
                            if (operands.Count >= 1)
                            {
                                renderMode = (int)GetNum(operands[0]);
                                // Rendering mode 2 (fill+stroke) visually simulates bold text.
                                // Restore font-intrinsic bold when mode changes away from 2.
                                currentIsBold = renderMode == 2 || fontIsBold;
                            }
                            break;
                        case "Tc":
                            if (operands.Count >= 1)
                                charSpacing = GetNum(operands[0]);
                            break;
                        case "Tw":
                            if (operands.Count >= 1)
                                wordSpacing = GetNum(operands[0]);
                            break;
                        case "Tz":
                            if (operands.Count >= 1)
                                hScaling = GetNum(operands[0]) / 100.0;
                            break;
                        case "Ts":
                            if (operands.Count >= 1)
                                textRise = GetNum(operands[0]);
                            break;

                        // ── Text positioning operators ──
                        case "Td":
                            if (operands.Count >= 2)
                            {
                                var tdxVal = GetNum(operands[0]);
                                var tdyVal = GetNum(operands[1]);
                                // Td values are in unscaled text space; apply the text matrix to convert
                                // to content-stream space: new_line = Tm(a,b,c,d) × (tdx, tdy) + old_line.
                                txLine = tmA * tdxVal + tmC * tdyVal + txLine;
                                tyLine = tmB * tdxVal + tmD * tdyVal + tyLine;
                                tx = txLine;
                                ty = tyLine;
                                // Insert newline sentinel for significant vertical displacement.
                                var pageDisp = Math.Abs(tmB * tdxVal + tmD * tdyVal);
                                if (pageDisp > 0.5 && result.Count > 0 && result[^1].Text != "\r\n")
                                    result.Add(new RawTextRun("\r\n", tx, ty, fontSize, currentFontName, 0, ctm));
                            }
                            break;
                        case "TD":
                            if (operands.Count >= 2)
                            {
                                var tdxD = GetNum(operands[0]);
                                var tdyD = GetNum(operands[1]);
                                txLine = tmA * tdxD + tmC * tdyD + txLine;
                                tyLine = tmB * tdxD + tmD * tdyD + tyLine;
                                tx = txLine;
                                ty = tyLine;
                                leading = -tdyD; // TD sets TL = -ty (in unscaled text space)
                                var pageDispD = Math.Abs(tmB * tdxD + tmD * tdyD);
                                if (pageDispD > 0.5 && result.Count > 0 && result[^1].Text != "\r\n")
                                    result.Add(new RawTextRun("\r\n", tx, ty, fontSize, currentFontName, 0, ctm));
                            }
                            break;
                        case "Tm":
                            if (operands.Count >= 6)
                            {
                                var newTmTx = GetNum(operands[4]);
                                var newTmTy = GetNum(operands[5]);
                                // Track all Tm components so Td/TD/T* can scale displacements correctly.
                                tmA = GetNum(operands[0]);
                                tmB = GetNum(operands[1]);
                                tmC = GetNum(operands[2]);
                                tmD = GetNum(operands[3]); // raw value; use Math.Abs where needed for thresholds
                                // Emit newline sentinel when Tm repositions to a different Y line.
                                // Compare against lastEmittedY (not ty) so that BT resets (ty=0)
                                // don't cause false newlines when consecutive BT blocks are on the same line.
                                var tmRefY = !double.IsNaN(lastEmittedY) ? lastEmittedY : ty;
                                if (Math.Abs(newTmTy - tmRefY) > Math.Max(1.0, fontSize * 0.3)
                                    && result.Count > 0 && result[^1].Text != "\r\n")
                                    result.Add(new RawTextRun("\r\n", newTmTx, newTmTy, fontSize, currentFontName, 0, ctm));
                                tx = txLine = newTmTx;
                                ty = tyLine = newTmTy;
                            }
                            break;
                        case "T*":
                            // T* = Td(0, -TL): move to the start of the next line.
                            // Apply the text matrix scale to the leading displacement.
                            txLine = tmA * 0 + tmC * (-leading) + txLine;
                            tyLine = tmB * 0 + tmD * (-leading) + tyLine;
                            tx = txLine;
                            ty = tyLine;
                            {
                                var pageDispStar = Math.Abs(Math.Abs(tmD) * leading);
                                if (pageDispStar > 0.5 && result.Count > 0 && result[^1].Text != "\r\n")
                                    result.Add(new RawTextRun("\r\n", tx, ty, fontSize, currentFontName, 0, ctm));
                            }
                            break;

                        // ── Text showing operators ──
                        case "Tj":
                            if (operands.Count >= 1 && operands[0] is PdfString s)
                            {
                                var text = DecodeBytes(s.Value, toUnicode, fontDict, reader, useFontEngineEncoding);
                                var rawWidth = metrics?.MeasureStringExact(s.Value, fontSize) ?? 0;
                                var numChars = text.Length;
                                var numSpaces = text.Count(c => c == ' ');
                                var unscaledWidth = rawWidth + charSpacing * numChars + wordSpacing * numSpaces;
                                var scaledWidth = unscaledWidth * hScaling;
                                // Build per-character cumulative widths from byte-level
                                // metrics so segment positioning is consistent with how
                                // tx is advanced. Without this, MeasureString(string)
                                // may give different results than MeasureString(bytes)
                                // for fonts with custom encodings or differing glyph
                                // indices, causing segment X offsets to drift.
                                double[]? tjCharCumWidths = null;
                                if (metrics is not null && text.Length == s.Value.Length)
                                {
                                    // n+1 entries: cumWidths[i] = advance to start of char i;
                                    // cumWidths[n] = total advance past last char (incl. trailing Tc).
                                    var cumWidths = new double[text.Length + 1];
                                    double cumW = 0;
                                    for (var ci = 0; ci < s.Value.Length; ci++)
                                    {
                                        cumWidths[ci] = cumW;
                                        var charW = metrics.MeasureStringExact(
                                            s.Value[ci..(ci + 1)], fontSize);
                                        var isSpace = ci < text.Length && text[ci] == ' ';
                                        cumW += charW + charSpacing
                                            + (isSpace ? wordSpacing : 0);
                                    }
                                    cumWidths[text.Length] = cumW;
                                    tjCharCumWidths = cumWidths;
                                }
                                else if (metrics is not null && text.Length > 0
                                    && s.Value.Length == text.Length * 2)
                                {
                                    // CID font: 2 bytes per character
                                    var cumWidths = new double[text.Length + 1];
                                    double cumW = 0;
                                    for (var ci = 0; ci < text.Length; ci++)
                                    {
                                        cumWidths[ci] = cumW;
                                        var charW = metrics.MeasureStringExact(
                                            s.Value[(ci * 2)..(ci * 2 + 2)], fontSize);
                                        cumW += charW + charSpacing
                                            + (text[ci] == ' ' ? wordSpacing : 0);
                                    }
                                    cumWidths[text.Length] = cumW;
                                    tjCharCumWidths = cumWidths;
                                }
                                else if (metrics is not null && text.Length > 0
                                    && s.Value.Length != text.Length)
                                {
                                    // Other encoding mismatch: distribute proportionally
                                    // from byte-level measured width
                                    var cumWidths = new double[text.Length + 1];
                                    for (var ci = 0; ci <= text.Length; ci++)
                                        cumWidths[ci] = unscaledWidth * ci / text.Length;
                                    tjCharCumWidths = cumWidths;
                                }

                                NormalizeDegenerateCumWidths(tjCharCumWidths);
                                // RawTextRun.Width stores unscaled width (CTM handles visual scaling)
                                result.Add(new RawTextRun(text, tx, ty, fontSize, currentFontName, unscaledWidth, ctm, metrics,
                                    TmA: tmA, TmB: tmB, TmC: tmC, TmD: tmD,
                                    CharCumWidths: tjCharCumWidths,
                                    RenderingMode: renderMode,
                                    IsBold: currentIsBold, IsItalic: currentIsItalic, FontInfoObj: currentFontInfo,
                                    HScaling: hScaling,
                                    TextRise: textRise,
                                    FillColor: currentFillColor, StrokingColor: currentStrokeColor));
                                lastEmittedY = ty;
                                // Advance position uses scaled width
                                tx += tmA * scaledWidth;
                                ty += tmB * scaledWidth;
                            }
                            break;
                        case "TJ":
                            if (operands.Count >= 1 && operands[0] is PdfArray arr)
                            {
                                var sb = new StringBuilder();
                                double tjWidth = 0;
                                double tjWidthUnscaled = 0; // same as tjWidth but without hScaling
                                int lastStrLen = 0; // decoded length of last PdfString element
                                // Track per-character cumulative advance widths WITHOUT hScaling.
                                // Rectangle width should not include Tz scaling — CTM handles
                                // the visual scaling. This matches .NET behavior.
                                var charCumWidthsList = new List<double>();
                                // Parallel list: position just AFTER each character's own glyph
                                // advance, BEFORE any TJ kerning that follows.  Fragment-width
                                // computation uses this for the match's final character so that
                                // compensation kernings sitting between the matched region and
                                // subsequent runs don't inflate the fragment's rectangle.
                                var charEndPositionsList = new List<double>();

                                for (int tjIdx = 0; tjIdx < arr.Count; tjIdx++)
                                {
                                    var item = arr[tjIdx];
                                    if (item is PdfString ps)
                                    {
                                        var decoded = DecodeBytes(ps.Value, toUnicode, fontDict, reader, useFontEngineEncoding);
                                        lastStrLen = decoded.Length;
                                        // Build per-character cumulative widths from byte-level metrics
                                        // so that TJ kerning before/between segments is correctly tracked.
                                        double segAdvance = 0;
                                        if (metrics is not null)
                                        {
                                            // Detect CID font: 2 bytes per character.
                                            int byteLen = (ps.Value.Length > 0 && decoded.Length > 0
                                                && ps.Value.Length == decoded.Length * 2) ? 2 : 1;
                                            for (var ci = 0; ci < ps.Value.Length; )
                                            {
                                                charCumWidthsList.Add(tjWidthUnscaled + segAdvance);
                                                var bl = Math.Min(byteLen, ps.Value.Length - ci);
                                                var charW = metrics.MeasureStringExact(ps.Value[ci..(ci + bl)], fontSize);
                                                var charIdx = byteLen == 2 ? ci / 2 : ci;
                                                var isSpace = charIdx < decoded.Length && decoded[charIdx] == ' ';
                                                var advance = charW + charSpacing + (isSpace ? wordSpacing : 0);
                                                segAdvance += advance;
                                                charEndPositionsList.Add(tjWidthUnscaled + segAdvance);
                                                ci += bl;
                                            }
                                        }
                                        else
                                        {
                                            // No metrics: distribute total width proportionally
                                            for (var ci = 0; ci < decoded.Length; ci++)
                                            {
                                                charCumWidthsList.Add(tjWidthUnscaled + segAdvance);
                                                charEndPositionsList.Add(tjWidthUnscaled + segAdvance);
                                            }
                                        }
                                        sb.Append(decoded);
                                        var segW = metrics?.MeasureStringExact(ps.Value, fontSize) ?? 0;
                                        var segSpaces = decoded.Count(c => c == ' ');
                                        var unscaledAdvance = segW + charSpacing * decoded.Length + wordSpacing * segSpaces;
                                        tjWidth += unscaledAdvance * hScaling;
                                        tjWidthUnscaled += unscaledAdvance;
                                    }
                                    else
                                    {
                                        // Kerning adjustment: value in thousandths of text space unit
                                        // Negative values move right, positive move left
                                        var adj = GetNum(item);
                                        var kernPt = -adj * fontSize / 1000.0;
                                        tjWidth += kernPt * hScaling;
                                        tjWidthUnscaled += kernPt;

                                        // Insert space for large negative adjustments (word breaks).
                                        // Threshold -190 matches TextAbsorber. The lastStrLen != 1
                                        // check prevents false spaces in single-char TJ arrays
                                        // (character spacing / decorative tracking). Also skip
                                        // synthetic-space insertion when the next PdfString decodes
                                        // to a leading space — the real space covers the word
                                        // boundary and a synthetic one would double it. We must
                                        // DECODE (not compare raw bytes) because CID/Type0 fonts
                                        // map non-0x20 bytes to the space glyph.
                                        bool nextStartsWithSpace = false;
                                        if (tjIdx + 1 < arr.Count && arr[tjIdx + 1] is PdfString peekStr)
                                        {
                                            var peekDecoded = DecodeBytes(peekStr.Value, toUnicode, fontDict, reader, useFontEngineEncoding);
                                            nextStartsWithSpace = peekDecoded.Length > 0 && peekDecoded[0] == ' ';
                                        }
                                        if (adj < -190 && lastStrLen != 1 && (sb.Length == 0 || sb[^1] != ' ')
                                            && !nextStartsWithSpace)
                                        {
                                            sb.Append(' ');
                                            charCumWidthsList.Add(tjWidthUnscaled); // space inserted at current position
                                            charEndPositionsList.Add(tjWidthUnscaled);
                                        }
                                    }
                                }
                                // Add n+1 entry (total width) for trailing Tc detection and clipping.
                                if (charCumWidthsList.Count == sb.Length)
                                    charCumWidthsList.Add(tjWidthUnscaled);
                                var charCumWidths = charCumWidthsList.Count == sb.Length + 1
                                    ? charCumWidthsList.ToArray() : null;
                                NormalizeDegenerateCumWidths(charCumWidths);
                                var charEndPositions = charEndPositionsList.Count == sb.Length
                                    ? charEndPositionsList.ToArray() : null;
                                // Use unscaled width for rectangle computation (CTM handles visual scaling)
                                result.Add(new RawTextRun(sb.ToString(), tx, ty, fontSize, currentFontName, tjWidthUnscaled, ctm, metrics,
                                    TmA: tmA, TmB: tmB, TmC: tmC, TmD: tmD, CharCumWidths: charCumWidths,
                                    CharEndPositions: charEndPositions, RenderingMode: renderMode,
                                    IsBold: currentIsBold, IsItalic: currentIsItalic, FontInfoObj: currentFontInfo,
                                    HScaling: hScaling, TextRise: textRise, FillColor: currentFillColor, StrokingColor: currentStrokeColor));
                                lastEmittedY = ty;
                                // Advance position through text matrix (for rotated text tmB≠0 advances Y)
                                tx += tmA * tjWidth;
                                ty += tmB * tjWidth;
                            }
                            break;
                        case "'":
                            // Move to next line (T* equivalent), then show text
                            txLine = tmA * 0 + tmC * (-leading) + txLine;
                            tyLine = tmB * 0 + tmD * (-leading) + tyLine;
                            tx = txLine; ty = tyLine;
                            if (result.Count > 0 && result[^1].Text != "\r\n")
                                result.Add(new RawTextRun("\r\n", tx, ty, fontSize, currentFontName, 0, ctm));
                            if (operands.Count >= 1 && operands[0] is PdfString s2)
                            {
                                var text2 = DecodeBytes(s2.Value, toUnicode, fontDict, reader, useFontEngineEncoding);
                                var rawW2 = metrics?.MeasureString(s2.Value, fontSize) ?? 0;
                                var nSp2 = text2.Count(c => c == ' ');
                                var unscW2 = rawW2 + charSpacing * text2.Length + wordSpacing * nSp2;
                                var w2 = unscW2 * hScaling;
                                result.Add(new RawTextRun(text2, tx, ty, fontSize, currentFontName, unscW2, ctm, metrics,
                                    TmA: tmA, TmB: tmB, TmC: tmC, TmD: tmD, RenderingMode: renderMode,
                                    IsBold: currentIsBold, IsItalic: currentIsItalic, FontInfoObj: currentFontInfo,
                                    HScaling: hScaling, TextRise: textRise, FillColor: currentFillColor, StrokingColor: currentStrokeColor));
                                tx += tmA * w2;
                                ty += tmB * w2;
                            }
                            break;
                        case "\"":
                            // Set word spacing, char spacing, move to next line, show text
                            if (operands.Count >= 3)
                            {
                                wordSpacing = GetNum(operands[0]);
                                charSpacing = GetNum(operands[1]);
                            }
                            txLine = tmA * 0 + tmC * (-leading) + txLine;
                            tyLine = tmB * 0 + tmD * (-leading) + tyLine;
                            tx = txLine; ty = tyLine;
                            if (result.Count > 0 && result[^1].Text != "\r\n")
                                result.Add(new RawTextRun("\r\n", tx, ty, fontSize, currentFontName, 0, ctm));
                            if (operands.Count >= 3 && operands[2] is PdfString s3)
                            {
                                var text3 = DecodeBytes(s3.Value, toUnicode, fontDict, reader, useFontEngineEncoding);
                                var rawW3 = metrics?.MeasureString(s3.Value, fontSize) ?? 0;
                                var nSp3 = text3.Count(c => c == ' ');
                                var unscW3 = rawW3 + charSpacing * text3.Length + wordSpacing * nSp3;
                                var w3 = unscW3 * hScaling;
                                result.Add(new RawTextRun(text3, tx, ty, fontSize, currentFontName, unscW3, ctm, metrics,
                                    TmA: tmA, TmB: tmB, TmC: tmC, TmD: tmD, RenderingMode: renderMode,
                                    IsBold: currentIsBold, IsItalic: currentIsItalic, FontInfoObj: currentFontInfo,
                                    HScaling: hScaling, TextRise: textRise, FillColor: currentFillColor, StrokingColor: currentStrokeColor));
                                tx += tmA * w3;
                                ty += tmB * w3;
                            }
                            break;

                        // ── Inline image — skip binary data ──
                        case "BI":
                            SkipInlineImage(lexer);
                            operands.Clear();
                            continue;

                        // ── XObject invocation ──
                        case "Do":
                            if (operands.Count >= 1 && operands[0] is PdfName xobjName)
                            {
                                var xobjects = TextAbsorber.ResolveXObjects(resourceDict, reader);
                                if (xobjects is not null)
                                {
                                    var xobjStream = reader.ResolveStream(xobjects.Get(xobjName.Value));
                                    if (xobjStream is not null &&
                                        xobjStream.Dict.GetName("Subtype") == "Form")
                                    {
                                        var xobjBytes = reader.DecodeStream(xobjStream);
                                        var xobjDict = xobjStream.Dict;

                                        // Compute the CTM for the XObject: current CTM × form's own /Matrix
                                        var xobjCtm = ctm;
                                        var matrixArr = xobjDict.Get("Matrix") as PdfArray;
                                        if (matrixArr is { Count: >= 6 })
                                        {
                                            var fm = new Matrix(
                                                GetNum(matrixArr[0]), GetNum(matrixArr[1]),
                                                GetNum(matrixArr[2]), GetNum(matrixArr[3]),
                                                GetNum(matrixArr[4]), GetNum(matrixArr[5]));
                                            xobjCtm = fm.Multiply(ctm);
                                        }

                                        ExtractRuns(xobjBytes, xobjDict, reader, result, depth + 1, xobjCtm, fillRects, useFontEngineEncoding);
                                    }
                                }
                            }
                            break;
                    }
                    operands.Clear();
                    break;
                }
                default:
                    operands.Clear();
                    break;
            }
        }
    }

    private static string DecodeBytes(byte[] bytes, Dictionary<int, string>? toUnicode,
        PdfDictionary? fontDict, PdfReader reader, bool useFontEngineEncoding = false)
    {
        // Delegate to TextAbsorber for consistent encoding handling
        return TextAbsorber.DecodeStringPublic(bytes, toUnicode, fontDict, reader, useFontEngineEncoding);
    }

    private static Dictionary<string, PdfDictionary> ResolveFonts(PdfDictionary pageDict, PdfReader reader)
        => TextAbsorber.ResolveFonts(pageDict, reader);

    private static List<byte[]> GetContentStreams(PdfDictionary pageDict, PdfReader reader)
    {
        var result = new List<byte[]>();
        var obj = reader.Resolve(pageDict.Get("Contents"));
        if (obj is PdfStream stream)
            result.Add(reader.DecodeStream(stream));
        else if (obj is PdfArray arr)
        {
            foreach (var item in arr)
            {
                var s = reader.ResolveStream(item);
                if (s is not null) result.Add(reader.DecodeStream(s));
            }
        }
        return result;
    }

    /// <summary>Skip inline image data (BI . ID &lt;data&gt; EI) per PDF spec §8.9.7.</summary>
    private static void SkipInlineImage(PdfLexer lexer) => TextAbsorber.SkipInlineImage(lexer);

    private static PdfArray ParseArray(PdfLexer lexer)
    {
        var arr = new PdfArray();
        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.ArrayEnd || t.Kind == TokenKind.Eof) break;
            switch (t.Kind)
            {
                case TokenKind.Integer: arr.Add(new PdfInteger(t.IntValue)); break;
                case TokenKind.Real: arr.Add(new PdfReal(t.RealValue)); break;
                case TokenKind.LiteralString: arr.Add(new PdfString(t.BytesValue!)); break;
                case TokenKind.HexString: arr.Add(new PdfString(t.BytesValue!, isHex: true)); break;
                case TokenKind.Name: arr.Add(new PdfName(t.StringValue!)); break;
            }
        }
        return arr;
    }

    private static double GetNum(PdfObject obj) => obj switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0,
    };

    /// <param name="CharCumWidths">
    /// Per-character cumulative advance widths (text-space).
    /// CharCumWidths[i] = total advance from run start to the START of character i.
    /// Accounts for TJ kerning adjustments.  Null if not tracked (use proportional fallback).
    /// </param>
    /// <summary>
    /// A filled rectangle painted by the content stream — collected when
    /// <see cref="TextSearchOptions.SearchForTextRelatedGraphics"/> is enabled
    /// so that a fragment whose origin falls inside one of these rects can be
    /// reported with the rect's fill color as <c>TextState.BackgroundColor</c>.
    /// Coordinates are in the same absorber-space as <see cref="RawTextRun"/>
    /// (post-CTM, post-page-rotation).
    /// </summary>
    private readonly record struct RawFillRect(double Llx, double Lly, double Urx, double Ury, Color FillColor);

    private readonly record struct RawTextRun(string Text, double X, double Y, double FontSize,
        string? FontName, double Width, Matrix Ctm, FontMetrics? Metrics = null,
        double TmA = 1.0, double TmB = 0.0, double TmC = 0.0, double TmD = 1.0,
        double[]? CharCumWidths = null, int RenderingMode = 0,
        bool IsBold = false, bool IsItalic = false,
        Font? FontInfoObj = null, double TextRise = 0.0,
        double HScaling = 1.0,
        // Parallel to CharCumWidths: the position just AFTER each character's
        // own glyph advance, BEFORE any subsequent TJ kerning. Used by the
        // fragment-width computation so compensation kernings emitted between
        // the match and post-match text don't inflate rectangle widths.
        double[]? CharEndPositions = null,
        // Fill colour in effect when this run was drawn (PDF default black).
        // Captured onto the fragment's TextState.ForegroundColor during assembly.
        Color? FillColor = null,
        // Stroking colour in effect when this run was drawn (null = default).
        // Captured onto the fragment's TextState.StrokingColor during assembly.
        Color? StrokingColor = null);

    /// <summary>
    /// Detect superscript/subscript fragments heuristically by comparing each fragment
    /// with its neighbors. A fragment is super/subscript if its font size is significantly
    /// smaller than neighbors on the same visual line and its Y position is shifted.
    /// </summary>
    private static void DetectSuperSubscript(TextFragmentCollection fragments)
    {
        if (fragments.Count < 2) return;

        for (int i = 1; i <= fragments.Count; i++)
        {
            var frag = fragments[i];
            // Skip if already detected via Ts operator
            if (frag.TextState.TextRise != 0) continue;

            if (frag.Position is null) continue;
            var fs = frag.TextState.FontSize;
            var y = frag.Position.YIndent;

            // Find the dominant (normal-sized) font size and Y from neighbors
            // that are horizontally adjacent (within the same visual line).
            double neighborFs = 0;
            double neighborY = double.NaN;

            // Check previous neighbor
            if (i > 1)
            {
                var prev = fragments[i - 1];
                if (prev.Position is not null && IsHorizontalNeighbor(frag, prev))
                {
                    neighborFs = prev.TextState.FontSize;
                    neighborY = prev.Position.YIndent;
                }
            }
            // Check next neighbor if no previous or previous was also small
            if (neighborFs <= fs && i < fragments.Count)
            {
                var next = fragments[i + 1];
                if (next.Position is not null && IsHorizontalNeighbor(frag, next))
                {
                    neighborFs = next.TextState.FontSize;
                    neighborY = next.Position.YIndent;
                }
            }

            if (neighborFs <= 0 || double.IsNaN(neighborY)) continue;

            // Super/subscript heuristic constraints:
            // 1. Fragment text must be short (≤5 chars) — real super/sub are brief
            // 2. Font must be significantly smaller (at most ~70% of neighbor size)
            if (frag.Text.Length > 5) continue;
            if (fs >= neighborFs * 0.7) continue;

            var yDiff = y - neighborY;
            var absYDiff = Math.Abs(yDiff);
            // Superscript: smaller font + Y is significantly higher (≥30% of neighbor font).
            // Subscript: smaller font + Y is at/near the same baseline (within 5% of neighbor font)
            // or below. In-between shifts (5-30%) are ambiguous — not marked.
            if (yDiff > neighborFs * 0.3)
            {
                frag.TextState.IsSuperscript = true;
            }
            else if (absYDiff < neighborFs * 0.05 || yDiff < -neighborFs * 0.05)
            {
                // Same baseline or below — subscript
                frag.TextState.IsSubscript = true;
            }
        }
    }

    /// <summary>Check if two fragments are on the same visual line (close Y, close X).
    /// Callers must ensure both Position values are non-null.</summary>
    private static bool IsHorizontalNeighbor(TextFragment a, TextFragment b)
    {
        // Y positions must be close (within the larger font size)
        var maxFs = Math.Max(a.TextState.FontSize, b.TextState.FontSize);
        var yDiff = Math.Abs(a.Position!.YIndent - b.Position!.YIndent);
        if (yDiff > maxFs) return false;

        // X positions should be reasonably close (fragments on the same line)
        var xDist = Math.Abs(a.Position.XIndent - b.Position.XIndent);
        return xDist < 500; // generous threshold for same-line proximity
    }

    /// <summary>
    /// Normalize Arabic Presentation Forms (U+FB50–U+FDFF, U+FE70–U+FEFF) to their
    /// base Unicode Arabic characters using NFKD decomposition.
    /// This allows text search to match regardless of whether the PDF uses
    /// presentation forms or standard Arabic codepoints.
    /// </summary>
    private static string NormalizeArabicPresentationForms(string text)
    {
        // Fast path: check if text contains any Arabic presentation form characters
        bool hasPresentationForms = false;
        foreach (var ch in text)
        {
            if ((ch >= '\uFB50' && ch <= '\uFDFF') || (ch >= '\uFE70' && ch <= '\uFEFF'))
            {
                hasPresentationForms = true;
                break;
            }
        }
        if (!hasPresentationForms) return text;

        // NFKD decomposition maps presentation forms to base characters
        return text.Normalize(System.Text.NormalizationForm.FormKD);
    }

    // ── Aspose.PDF for .NET-shape additions ───────────────────────────────

    /// <summary>Apply the supplied font to every absorbed fragment.</summary>
    public void ApplyForAllFragments(Font font)
    {
        if (font is null) return;
        foreach (var frag in _fragments)
        {
            if (frag.TextState is not null) frag.TextState.Font = font;
        }
    }

    /// <summary>Apply the supplied font + size to every absorbed fragment.</summary>
    public void ApplyForAllFragments(Font font, float fontSize)
    {
        if (font is null) return;
        foreach (var frag in _fragments)
        {
            if (frag.TextState is not null)
            {
                frag.TextState.Font = font;
                frag.TextState.FontSize = fontSize;
            }
        }
    }

    /// <summary>Apply the supplied font size to every absorbed fragment.</summary>
    public void ApplyForAllFragments(float fontSize)
    {
        foreach (var frag in _fragments)
        {
            if (frag.TextState is not null) frag.TextState.FontSize = fontSize;
        }
    }

    /// <summary>Replace every fragment's text with the empty string across every page in the document.</summary>
    public void RemoveAllText(Aspose.Pdf.Document document)
    {
        if (document is null) return;
        foreach (var page in document.Pages)
            RemoveAllText(page);
    }

    /// <summary>Replace every fragment's text with the empty string on the given page.</summary>
    public void RemoveAllText(Aspose.Pdf.Page page)
    {
        if (page is null) return;
        Visit(page);
        foreach (var frag in _fragments)
            frag.Text = string.Empty;
        _fragments.Clear();
    }

    /// <summary>Replace every fragment's text with the empty string on the given page, restricted to <paramref name="rect"/>.</summary>
    public void RemoveAllText(Aspose.Pdf.Page page, Aspose.Pdf.Rectangle rect)
    {
        if (page is null) return;
        var prevSearch = _textSearchOptions;
        try
        {
            _textSearchOptions = new TextSearchOptions(rect);
            RemoveAllText(page);
        }
        finally
        {
            _textSearchOptions = prevSearch;
        }
    }

    /// <summary>Clear absorbed fragments, errors, and per-regex results.</summary>
    public void Reset()
    {
        _fragments.Clear();
        Errors.Clear();
        RegexResults.Clear();
    }
}

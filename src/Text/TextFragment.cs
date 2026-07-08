namespace Aspose.Pdf.Text;

/// <summary>
/// A single segment within a <see cref="TextFragment"/>.
/// A fragment may be composed of one or more segments that share the same text state.
/// </summary>
public sealed class TextSegment
{
    private string _text;

    public TextSegment() : this(string.Empty) { }

    public TextSegment(string text)
    {
        _text = text;
        TextState = new TextState();
    }

    /// <summary>The text of this segment.</summary>
    public string Text
    {
        get => _text;
        set
        {
            _text = value;
            Owner?.RefreshTextFromSegments();
        }
    }

    /// <summary>The position of this segment on the page.</summary>
    public Position? Position { get; set; }

    public Position? BaselinePosition { get; set; }

    /// <summary>The text state (font, size, colour, etc.) for this segment.</summary>
    public TextState TextState { get; set; }

    /// <summary>
    /// The starting character index of this segment within the source text run on the page.
    /// </summary>
    public int StartCharIndex { get; internal set; }

    /// <summary>
    /// The ending character index of this segment within the source text run on the page.
    /// </summary>
    public int EndCharIndex { get; internal set; }

    /// <summary>Index of the source text run (Tj/TJ operator) this segment came from.</summary>
    internal int SourceRunIndex { get; set; }

    /// <summary>Back-reference to the owning TextFragment (set by the collection).</summary>
    internal TextFragment? Owner { get; set; }

    private Rectangle? _rectangle;

    /// <summary>
    /// The bounding rectangle of this segment. For a segment placed by an
    /// absorb/layout pass this is its page bounds; for a standalone
    /// (just-constructed) segment it is measured on demand from the text and the
    /// TextState font metrics — origin (0,0), width = the text advance, height =
    /// the font size — so callers can size content (e.g. table cells) before layout.
    /// </summary>
    public Rectangle? Rectangle
    {
        get
        {
            if (_rectangle is not null) return _rectangle;
            if (string.IsNullOrEmpty(Text) || TextState is null) return null;
            var width = TextState.MeasureString(Text);
            return new Rectangle(0, 0, width, TextState.FontSize);
        }
        internal set => _rectangle = value;
    }

    /// <summary>Per-character layout information for this segment: one
    /// <see cref="CharInfo"/> per character, in text order, populated when the
    /// segment is produced by a text absorber.</summary>
    public CharInfoCollection Characters { get; } = new CharInfoCollection();

    /// <summary>Optional hyperlink associated with this segment.</summary>
    public Hyperlink? Hyperlink { get; set; }

    /// <summary>HTML-encode a string by replacing &amp;, &lt;, &gt;, &quot;, and &apos;
    /// with their entity references. Helper used during HTML emission.</summary>
    public string MyHtmlEncode(string value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\'': sb.Append("&apos;"); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }

    private TextEditOptions? _textEditOptions;

    /// <summary>Edit options applied during text replacement / font substitution.</summary>
    public TextEditOptions TextEditOptions
    {
        get => _textEditOptions ??= new TextEditOptions(TextEditOptions.LanguageTransformation.Default);
        set => _textEditOptions = value;
    }

    /// <summary>Physical (page-space) view of this segment: exposes its on-page start
    /// X via <c>TextState.TextXIndent</c> and per-range advance measurement via
    /// <see cref="PhysicalTextSegment.MeasureSegment(int, int, bool)"/>.</summary>
    public PhysicalTextSegment PhysicalSegment => new(this);
}

/// <summary>
/// Physical (page-space) projection of an absorbed <see cref="TextSegment"/>.
/// </summary>
public sealed class PhysicalTextSegment
{
    private readonly TextSegment _segment;

    internal PhysicalTextSegment(TextSegment segment) => _segment = segment;

    /// <summary>The segment's text state; its <see cref="TextState.TextXIndent"/> carries
    /// the segment's on-page start X.</summary>
    public TextState TextState
    {
        get
        {
            var ts = _segment.TextState;
            ts.TextXIndent = (float)(_segment.Position?.XIndent
                ?? _segment.Rectangle?.LLX ?? 0);
            return ts;
        }
    }

    /// <summary>Measure the page-space advance of the character range
    /// [<paramref name="from"/>..<paramref name="to"/>] (inclusive) of the segment text.
    /// The full range returns the segment's absorbed width exactly; partial ranges are
    /// apportioned by font advance.</summary>
    public double MeasureSegment(int from, int to, bool includeTrailingSpaces)
    {
        _ = includeTrailingSpaces;
        var text = _segment.Text ?? string.Empty;
        if (text.Length == 0) return 0;
        from = Math.Max(0, from);
        to = Math.Min(text.Length - 1, Math.Max(from, to));

        var fullWidth = _segment.Rectangle?.Width ?? 0;
        if (from == 0 && to == text.Length - 1) return fullWidth;

        // Partial range: prefer the absorber's per-character boxes; else share the
        // absorbed width by the font-advance ratio of the sub-range.
        if (_segment.Characters.Count == text.Length)
        {
            double w = 0;
            for (var i = from; i <= to; i++)
                w += _segment.Characters[i + 1].Rectangle.Width;
            return w;
        }
        var sub = text.Substring(from, to - from + 1);
        var subAdvance = _segment.TextState.MeasureString(sub);
        var allAdvance = _segment.TextState.MeasureString(text);
        return allAdvance > 0 ? fullWidth * subAdvance / allAdvance
             : fullWidth * (to - from + 1) / (double)text.Length;
    }
}

/// <summary>Per-character layout information (glyph rectangle + page position).
/// Empty in FOSS — character-level metrics aren't exposed.</summary>
public sealed class CharInfo
{
    internal CharInfo(Position position, Aspose.Pdf.Rectangle rectangle)
    {
        Position = position;
        Rectangle = rectangle;
    }

    /// <summary>Glyph position on the page.</summary>
    public Position Position { get; }

    /// <summary>Glyph bounding rectangle on the page.</summary>
    public Aspose.Pdf.Rectangle Rectangle { get; }
}

/// <summary>Collection of <see cref="CharInfo"/> entries — supports the public surface used by
/// TextSegment.Characters but stays empty by default.</summary>
public sealed class CharInfoCollection : System.Collections.Generic.IEnumerable<CharInfo>
{
    private readonly System.Collections.Generic.List<CharInfo> _items = new();

    public int Count => _items.Count;
    public bool IsReadOnly => false;
    public bool IsSynchronized => false;
    public object SyncRoot { get; } = new();

    /// <summary>1-based accessor for the character at the given position.</summary>
    public CharInfo this[int index] => _items[index - 1];

    public void Add(CharInfo item)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));
        _items.Add(item);
    }

    public void Clear() => _items.Clear();
    public bool Contains(CharInfo item) => _items.Contains(item);
    public void CopyTo(CharInfo[] array, int index) => _items.CopyTo(array, index);
    public bool Remove(CharInfo item) => item is not null && _items.Remove(item);
    public System.Collections.Generic.IEnumerator<CharInfo> GetEnumerator() => _items.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// A 1-indexed collection of <see cref="TextSegment"/> objects belonging to a <see cref="TextFragment"/>.
/// </summary>
public sealed class TextSegmentCollection : System.Collections.Generic.IEnumerable<TextSegment>
{
    private readonly System.Collections.Generic.List<TextSegment> _segments = new();

    /// <summary>Number of segments.</summary>
    public int Count => _segments.Count;

    /// <summary>1-based indexer (index 1 returns the first segment).</summary>
    public TextSegment this[int index]
    {
        get
        {
            if (index < 1 || index > _segments.Count)
                throw new IndexOutOfRangeException($"Index {index} out of range [1, {_segments.Count}].");
            return _segments[index - 1];
        }
    }

    /// <summary>Back-reference to the owning TextFragment.</summary>
    internal TextFragment? Owner { get; set; }

    public bool IsReadOnly => false;
    public bool IsSynchronized => false;
    public object SyncRoot { get; } = new();

    public void Add(TextSegment segment)
    {
        if (segment is null) throw new ArgumentNullException(nameof(segment));
        segment.Owner = Owner;
        _segments.Add(segment);
        Owner?.RefreshTextFromSegments();
    }

    public bool Contains(TextSegment item) => _segments.Contains(item);

    public void CopyTo(TextSegment[] array, int index) => _segments.CopyTo(array, index);

    public bool Remove(TextSegment item)
    {
        if (item is null) return false;
        var removed = _segments.Remove(item);
        if (removed)
        {
            item.Owner = null;
            Owner?.RefreshTextFromSegments();
        }
        return removed;
    }

    public void Clear()
    {
        foreach (var seg in _segments) seg.Owner = null;
        _segments.Clear();
        Owner?.RefreshTextFromSegments();
    }

    public System.Collections.Generic.IEnumerator<TextSegment> GetEnumerator() => _segments.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _segments.GetEnumerator();
}

/// <summary>
/// Represents a fragment of text on a page with position and style information.
/// </summary>
public class TextFragment : BaseParagraph
{
    private string _text;
    private readonly TextSegmentCollection _segments;

    /// <summary>
    /// Create a text fragment with tab stops. Text is set via Segments.
    /// </summary>
    /// <summary>Create an empty text fragment.</summary>
    public TextFragment() : this("") { }

    public TextFragment(TabStops tabStops) : this("")
    {
        TabStops = tabStops;
    }

    /// <summary>Create a text fragment with the given <paramref name="text"/>.
    /// Standalone single-arg ctor (Aspose.Pdf reflection signature).</summary>
    public TextFragment(string text) : this(text, rectangle: null, textState: null) { }

    /// <summary>Create a text fragment with the given <paramref name="text"/>
    /// and <paramref name="tabStops"/> resolution table.</summary>
    public TextFragment(string text, TabStops tabStops) : this(text, rectangle: null, textState: null)
    {
        TabStops = tabStops;
    }

    /// <summary>Tab stops for this fragment (used with #$TAB markers in text).</summary>
    public TabStops? TabStops { get; set; }

    private TextEditOptions? _textEditOptions;

    /// <summary>Edit options applied during text replacement / font substitution.</summary>
    public TextEditOptions TextEditOptions
    {
        get => _textEditOptions ??= new TextEditOptions(TextEditOptions.LanguageTransformation.Default);
        set => _textEditOptions = value;
    }

    /// <summary>True when the caller explicitly opted into
    /// <see cref="TextEditOptions.NoCharacterAction.ReplaceFonts"/> — the generator
    /// then substitutes a glyph-covering face at layout time. Reads the backing
    /// field so the check never instantiates default options.</summary>
    internal bool HasExplicitReplaceFonts =>
        _textEditOptions is { NoCharacterBehaviorExplicit: true,
            NoCharacterBehavior: TextEditOptions.NoCharacterAction.ReplaceFonts };

    /// <summary>
    /// When true, this text fragment renders on the same line as the previous
    /// in-line paragraph (see docs.aspose.com/pdf/net/add-text-to-pdf-file
    /// "inline paragraphs"). Currently a state flag — layout wiring follows
    /// once Image / inline-flow rendering is implemented.
    /// </summary>
    public new bool IsInLineParagraph { get; set; }

    public new bool IsInNewPage { get; set; }

    public new VerticalAlignment VerticalAlignment { get; set; } = VerticalAlignment.Top;

    public TextFragment(string text, Rectangle? rectangle = null, TextState? textState = null)
    {
        _text = text;
        Rectangle = rectangle;
        TextState = new TextFragmentState(this);
        if (textState is not null) TextState.ApplyChangesFrom(textState);
        _segments = new TextSegmentCollection { Owner = this };
        var seg = new TextSegment(text);
        seg.TextState.FontSize = TextState.FontSize;
        seg.TextState.FontName = TextState.FontName;
        seg.TextState.CharacterSpacing = TextState.CharacterSpacing;
        seg.TextState.WordSpacing = TextState.WordSpacing;
        seg.TextState.HorizontalScaling = TextState.HorizontalScaling;
        seg.TextState.IsBold = TextState.IsBold;
        seg.TextState.IsItalic = TextState.IsItalic;
        seg.TextState.RenderingMode = TextState.RenderingMode;
        seg.TextState.Font = TextState.Font;
        seg.Owner = this;
        _segments.Add(seg);
    }

    /// <summary>
    /// The text content. Setting this property replaces the text in the PDF content stream
    /// when a source page reference is available (i.e. when the fragment was obtained from
    /// a TextFragmentAbsorber).
    /// </summary>
    public string Text
    {
        get => _text;
        set
        {
            // Identical text is normally a no-op, but a post-replace reflow
            // (ReplaceOptions.Rectangle set) can still need to run — e.g.
            // ScaleToFill re-fits the SAME text to a resized rectangle.
            if (string.Equals(_text, value, StringComparison.Ordinal)
                && _replaceOptions?.Rectangle is null)
                return;

            // NoCharacterAction.ThrowException (opt-in): reject replacement text the
            // fragment's font can't represent, before mutating anything.
            ThrowIfFontLacksGlyph(value);

            var oldText = _text;

            // Post-replace reflow: when a target rectangle is supplied via
            // ReplaceOptions.Rectangle, re-wrap the replacement text into that
            // rectangle (with optional font-size fit) instead of doing an
            // in-place operator swap. Only for page-level, non-CID fragments —
            // CID/Type0 (CJK/Arabic) reflow needs shaping the layout engine
            // doesn't model. TryReflowIntoRectangle sets _text/_segments/_rectangle.
            if (_replaceOptions?.Rectangle is { } reflowRect && SourcePage is not null
                && TextState.Font is not null && TextState.FontSize > 0
                && TryReflowIntoRectangle(oldText, value, reflowRect))
            {
                return;
            }

            // WholeWordsHyphenation: re-wrap the whole CONTAINING paragraph so text flows up
            // to close the gap the (usually shorter) replacement leaves, giving a
            // continuous re-flow. Done once per paragraph — the first fragment whose search
            // text is found reflows every occurrence in the paragraph; sibling fragments then
            // find nothing to replace and no-op.
            if (SourcePage is not null && TextState.Font is not null && TextState.FontSize > 0
                && _replaceOptions?.ReplaceAdjustmentAction == TextReplaceOptions.ReplaceAdjustment.WholeWordsHyphenation
                && TryReflowParagraph(SourcePage, oldText, value))
            {
                _text = value;
                return;
            }

            _text = value;

            string? switchedFontFamily = null;
            if (SourcePage is not null)
            {
                // Font-switch a replacement whose glyphs are absent from the source
                // embedded subset to a fallback (source family / Times) so it renders,
                // splitting the run and re-anchoring following text at its original
                // absolute Tm so downstream positions are preserved.
                TextReplacer.ResetSwitchedFont();
                // NoCharacterAction.ReplaceAnyway means "force the bytes into the ORIGINAL
                // font, don't substitute" — so the subset-glyph fallback (and its font
                // report) is disabled for that mode; the default mode substitutes and
                // reports the fallback face.
                bool replaceAnyway = _textEditOptions is { NoCharacterBehaviorExplicit: true } teo0
                    && teo0.NoCharacterBehavior == TextEditOptions.NoCharacterAction.ReplaceAnyway;
                bool allowFallback = !replaceAnyway;
                // Fragment-level replace re-flows the rest of the line (the reference
                // engine shifts following same-line text by the width delta when the
                // replacement is shorter/longer than the match) — EXCEPT under
                // ReplaceAdjustment.None, whose contract is that surrounding text
                // keeps its exact position regardless of the width change.
                bool reflowLine = _replaceOptions?.ReplaceAdjustmentAction
                    != TextReplaceOptions.ReplaceAdjustment.None;
                var replacer = new TextReplacer { AllowSubsetGlyphFallback = allowFallback, ReflowLineOnReplace = reflowLine, AnchorTrailingOnReplace = !reflowLine };
                // Scope to this fragment's page-space Y so iterating
                // fragments[i].Text in a loop replaces only the operator that
                // produced this fragment, not every matching occurrence on the
                // page. Without this, the replacement string accumulates across
                // iterations: setting fragment 1 first replaces all occurrences,
                // then fragment 2's setter re-matches inside the just-replaced
                // text and appends another copy of the replacement, etc.
                //
                // The replacer compares against the Tm-origin (baseline) Y.
                // Position.YIndent tracks the baseline only loosely — depending on the
                // absorber path it is the rect bottom (descent below baseline) or the
                // baseline itself, and BaselinePosition's descent correction can itself
                // be wrong when FontSize carries an unscaled Tf value (page scale in the
                // CTM). So try BOTH candidate Ys, baseline-corrected first.
                var targetYs = new List<double?>();
                if (_position is { } pos)
                {
                    var baseY = (BaselinePosition ?? pos).YIndent;
                    targetYs.Add(baseY);
                    if (Math.Abs(pos.YIndent - baseY) > 0.01)
                        targetYs.Add(pos.YIndent);
                }
                else
                    targetYs.Add(null);
                // Absorber fragments map to whole show-operators, so per candidate Y
                // first look for an operator whose ENTIRE text equals the fragment.
                // This pins the edit to the fragment's own operator when the same words
                // also occur INSIDE a longer operator on the same line (emptying a
                // "Lorem Ipsum" heading must not eat the mid-sentence "Lorem Ipsum" of
                // a sibling fragment, and a whitespace-only fragment must not match the
                // spaces inside every neighbouring operator). Substring matching runs
                // as a second sweep — except for whitespace-only text, where it would
                // only cause the space-eating collapse the whole-op pass prevents.
                foreach (var ty in targetYs)
                {
                    replacer.TargetY = ty;
                    replacer.MatchWholeOperator = true;
                    replacer.Replace(SourcePage, oldText, value);
                    if (replacer.ReplacementCount > 0) break;
                }
                if (replacer.ReplacementCount == 0 && oldText.Trim().Length > 0)
                {
                    foreach (var ty in targetYs)
                    {
                        replacer.TargetY = ty;
                        replacer.MatchWholeOperator = false;
                        replacer.Replace(SourcePage, oldText, value);
                        if (replacer.ReplacementCount > 0) break;
                    }
                }

                // Fallback: if simple replace found nothing, try cross-operator replacement
                // (handles text split across TJ/Tj operators). Not gated on segment count:
                // the absorber COALESCES a one-character-per-operator producer into a single
                // segment, so a single-segment fragment can still span many operators.
                if (replacer.ReplacementCount == 0 && (_segments.Count > 1 || oldText.Length > 1))
                {
                    var crossReplacer = new TextReplacer { AllowSubsetGlyphFallback = allowFallback, ReflowLineOnReplace = reflowLine, AnchorTrailingOnReplace = !reflowLine };
                    foreach (var ty in targetYs)
                    {
                        crossReplacer.TargetY = ty;
                        crossReplacer.ReplaceWithCrossOperator(SourcePage, oldText, value);
                        if (crossReplacer.ReplacementCount > 0) break;
                    }
                    if (crossReplacer.ReplacementCount > 0)
                        replacer = crossReplacer; // use cross result
                }

                // Fallback: if the combined text wasn't found AND the fragment uses a CID
                // font (common for Arabic/CJK where text spans multiple content stream
                // operators), replace segment-by-segment. The first non-trivial segment
                // receives the new text; remaining segments are cleared.
                if (replacer.ReplacementCount == 0 && _segments.Count > 1 && IsCidFontFragment())
                {
                    var firstSeg = true;
                    foreach (var s in _segments)
                    {
                        if (string.IsNullOrWhiteSpace(s.Text)) continue;
                        var segReplacer = new TextReplacer();
                        segReplacer.Replace(SourcePage, s.Text, firstSeg ? value : "");
                        if (segReplacer.ReplacementCount > 0) firstSeg = false;
                    }
                }

                // Deletion-only last resort: a fragment with EDGE whitespace (" .") can
                // join a space operator and a glyph operator that no single-op match —
                // whole or substring — covers, and a sibling fragment's earlier delete
                // may already have consumed the glyph op. Drop each whitespace/non-ws
                // token as its own whole operator so the space op doesn't survive as a
                // non-empty invisible remnant.
                if (replacer.ReplacementCount == 0 && value.Length == 0
                    && oldText.Length > 0 && oldText.Trim() != oldText)
                {
                    var ti = 0;
                    while (ti < oldText.Length)
                    {
                        var ws = char.IsWhiteSpace(oldText[ti]);
                        var tj = ti;
                        while (tj < oldText.Length && char.IsWhiteSpace(oldText[tj]) == ws) tj++;
                        var tokReplacer = new TextReplacer { MatchWholeOperator = true };
                        foreach (var ty in targetYs)
                        {
                            tokReplacer.TargetY = ty;
                            tokReplacer.Replace(SourcePage, oldText.Substring(ti, tj - ti), "");
                            if (tokReplacer.ReplacementCount > 0) break;
                        }
                        ti = tj;
                    }
                }

                // If the replacement's glyphs were absent from the source subset and the
                // run was substituted in a fallback face, surface that font on the
                // fragment (default no-character behaviour reports the substituted font).
                switchedFontFamily = replacer.SwitchedFontFamily;
            }
            else if (Form is not null)
            {
                // The fragment was extracted via TextFragmentAbsorber.Visit(XForm), so
                // its producing operator lives in the Form XObject's content stream
                // (SourcePage is null). Edit that stream rather than no-op.
                new TextReplacer().Replace(Form, oldText, value);
            }

            // Reset segments to a single segment with the new text
            _segments.Clear();
            var seg = new TextSegment(value);
            seg.TextState.FontSize = TextState.FontSize;
            seg.TextState.FontName = TextState.FontName;
            seg.TextState.Font = TextState.Font;
            seg.Owner = this;
            // Inherit position from the fragment so segment-level
            // BackgroundColor / per-segment effects can compute rect bounds.
            // Use PositionOrNull, not the Position getter: the getter now
            // auto-materialises a (0,0) stand-in for an unpositioned fragment,
            // and copying that into the segment would make the flow paginator
            // treat the fragment as explicitly positioned (per-segment Position
            // set) and decline to flow it.
            seg.Position = PositionOrNull;
            // Wire OwnerSegment so subsequent TextState.ForegroundColor /
            // BackgroundColor / FontSize setters propagate to the page's
            // content stream (TextStateModifier looks up via OwnerSegment.Owner.SourcePage).
            seg.TextState.OwnerSegment = seg;
            _segments.Add(seg);

            // Under ReplaceAdjustment.None and ShiftRestOfLine the visible width of
            // the fragment shrinks to the replacement's natural advance — whether the
            // surrounding glyphs keep their positions (None: TJ compensation kerns) or
            // close up behind it (ShiftRestOfLine), the fragment itself spans only the
            // new text, so its rectangle must reflect the new width. The wholesale
            // re-wrap modes (WholeWordsHyphenation etc.) recompute geometry themselves.
            if (_rectangle is not null
                && ReplaceOptions?.ReplaceAdjustmentAction is TextReplaceOptions.ReplaceAdjustment.None
                    or TextReplaceOptions.ReplaceAdjustment.ShiftRestOfLine
                && TextState.Font is { } font && TextState.FontSize > 0)
            {
                double newWidth;
                try { newWidth = font.MeasureString(value, TextState.FontSize); }
                catch { newWidth = -1; }
                if (newWidth >= 0)
                {
                    _rectangle = new Rectangle(_rectangle.LLX, _rectangle.LLY,
                        _rectangle.LLX + newWidth, _rectangle.URY);
                }
            }

            // ToAttemptGetUnderlineFromSource: source decorations follow the replacement.
            // Splice the captured underline/highlight rectangles out of the content and
            // register standard injection so new ones are drawn at the replacement's
            // advance (the source rects are sized to the OLD text and would otherwise
            // keep rendering under/behind the new, differently-sized text). Runs BEFORE
            // the reported-font switch below so the advance is measured against the
            // ORIGINAL face (with its style), not the switched family name.
            if (SourcePage is not null &&
                (CapturedUnderlineSources is { Count: > 0 } || CapturedBackgroundSources is { Count: > 0 }))
            {
                SourcePage.RegisterUnderlineRemoval(this);

                // Re-size the fragment rectangle to the replacement's natural advance so
                // the injected decoration spans the new text, not the old.
                if (_rectangle is not null && TextState.Font is { } decFont && TextState.FontSize > 0)
                {
                    var advance = MeasureReplacementAdvance(decFont, value, TextState.FontSize);
                    if (advance >= 0)
                        _rectangle = new Rectangle(_rectangle.LLX, _rectangle.LLY,
                            _rectangle.LLX + advance, _rectangle.URY);
                }

                if (CapturedUnderlineSources is { Count: > 0 })
                    SourcePage.RegisterUnderlineFragment(this);
                if (CapturedBackgroundSources is { Count: > 0 })
                {
                    if (TextState.BackgroundColor is null && CapturedBackgroundColor is { } cbc)
                        TextState.SetCapturedBackgroundColor(cbc);
                    SourcePage.RegisterBgColorFragment(this);
                }
            }

            // Report the substituted fallback face on the fragment's TextState.Font
            // (the byte-level replacer already switched the glyphs in the stream; this
            // only updates what the fragment REPORTS, side-effect-free — no re-embed
            // or content rewrite, so following text is not shifted).
            if (switchedFontFamily is not null)
                TextState.SetReportedFont(switchedFontFamily);
        }
    }

    /// <summary>
    /// Remove this fragment's text from the page content stream. Used when a
    /// fragment is removed from an absorber's result collection: the producing
    /// text-showing operator is dropped so the next save no longer renders it.
    /// Matches the operator whose entire shown text equals this fragment's text
    /// (scoped to the fragment's page-space Y) so deleting a short fragment such
    /// as "$" does not corrupt a longer one such as "$ 200.00" on the same row.
    /// </summary>
    internal void DeleteFromContent()
    {
        if (SourcePage is null || string.IsNullOrEmpty(_text))
            return;

        var replacer = new TextReplacer { MatchWholeOperator = true };
        if (_position is { } pos)
            replacer.TargetY = pos.YIndent;
        replacer.Replace(SourcePage, _text, string.Empty);

        // Fall back to substring replacement for fragments whose text spans only
        // part of an operator (or multiple operators) and so was not removed by
        // the exact whole-operator pass.
        if (replacer.ReplacementCount == 0)
        {
            var fallback = new TextReplacer();
            if (_position is { } pos2)
                fallback.TargetY = pos2.YIndent;
            fallback.Replace(SourcePage, _text, string.Empty);
        }

        _text = string.Empty;
        _segments.Clear();
    }

    /// <summary>
    /// Remove this fragment's text from the page for redaction: like
    /// <see cref="DeleteFromContent"/> but width-preserving — a fully-deleted show
    /// operator leaves a glyph-less advance instead of being dropped, so text after
    /// it on the same line keeps its position (no reflow). Scoped to the fragment's
    /// page-space Y so only this occurrence is removed.
    /// </summary>
    internal void RedactFromContent()
    {
        if (SourcePage is null || string.IsNullOrEmpty(_text))
            return;

        var replacer = new TextReplacer { MatchWholeOperator = true, PreserveAdvanceOnDelete = true };
        if (_position is { } pos)
            replacer.TargetY = pos.YIndent;
        replacer.Replace(SourcePage, _text, string.Empty);

        if (replacer.ReplacementCount == 0)
        {
            var fallback = new TextReplacer { PreserveAdvanceOnDelete = true };
            if (_position is { } pos2)
                fallback.TargetY = pos2.YIndent;
            fallback.Replace(SourcePage, _text, string.Empty);
        }

        _text = string.Empty;
        _segments.Clear();
    }

    /// <summary>
    /// Reflow <paramref name="newText"/> into <paramref name="rect"/>: word-wrap
    /// to the rectangle width, optionally fit the font size
    /// (<see cref="TextReplaceOptions.FontSizeAdjustment"/>), delete the original
    /// paragraph operators, and write the wrapped lines top-anchored inside the
    /// rectangle. Updates <see cref="_text"/>, <see cref="_segments"/> and
    /// <see cref="_rectangle"/> to the laid-out block. Returns false (leaving the
    /// caller to fall back to the in-place swap) when the geometry is unusable.
    /// </summary>
    private bool TryReflowIntoRectangle(string oldText, string newText, Rectangle rect)
    {
        var page = SourcePage!;
        var font = TextState.Font!;
        double baseFs = TextState.FontSize;
        if (rect.Width <= 1 || rect.Height <= 1 || string.IsNullOrEmpty(newText)) return false;

        // Derive the line pitch (baseline-to-baseline) from the fragment's current
        // multi-line layout so the reflowed block keeps the same leading.
        double leadingRatio = 1.2;
        if (_segments.Count >= 2 && _segments[1].Position is { } b1 && _segments[2].Position is { } b2)
        {
            var l = (b1.YIndent - b2.YIndent) / baseFs;
            if (l > 0.5 && l < 3.0) leadingRatio = l;
        }

        double wrapWidth = rect.Width;
        var fit = _replaceOptions?.FontSizeAdjustmentAction ?? TextReplaceOptions.FontSizeAdjustment.None;

        double fs = baseFs;
        if (fit is TextReplaceOptions.FontSizeAdjustment.ShrinkToFit
                or TextReplaceOptions.FontSizeAdjustment.Decrease)
            fs = FitFontSize(newText, font, wrapWidth, rect.Height, 1.0, baseFs, leadingRatio);
        else if (fit is TextReplaceOptions.FontSizeAdjustment.ScaleToFill
                or TextReplaceOptions.FontSizeAdjustment.Increase)
            fs = FitFontSize(newText, font, wrapWidth, rect.Height, baseFs, 400.0, leadingRatio);

        var lines = WrapToWidth(newText, font, fs, wrapWidth);
        if (lines.Count == 0) return false;

        double leading = leadingRatio * fs;
        // Anchor the block so its re-absorbed top matches rect.URY. The wrapped
        // lines are written through TextBuilder, which maps a non-embedded font
        // to a Standard-14 face; TextFragmentAbsorber then reconstructs that
        // run's box as URY = baseline + (1.1·fs + descentOff) and
        // LLY = baseline + descentOff (descentOff negative). Use the WRITTEN
        // font's descent (not the original run's ascent) so the anchor lines up.
        var writtenFontName = TextBuilder.MapToStandard14Public(TextState);
        double descentOff = font.SourceFontData is null
            ? Standard14Fonts.GetDescent(writtenFontName) * fs / 1000.0
            : (font.GetMetrics()?.Descent ?? -212) * fs / 1000.0;
        double ascentH = fs * 1.1 + descentOff;
        double firstBaseline = rect.URY - ascentH;

        // Remove the original paragraph: delete each source line operator at its
        // own baseline Y so a repeated substring elsewhere on the page is untouched.
        DeleteReflowSource(page, oldText);

        // Write the wrapped lines as positioned fragments.
        var tb = new TextBuilder(page);
        double maxW = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            var lf = new TextFragment(lines[i]);
            lf.TextState.Font = font;
            lf.TextState.FontName = TextState.FontName;
            lf.TextState.FontSize = (float)fs;
            lf.TextState.IsBold = TextState.IsBold;
            lf.TextState.IsItalic = TextState.IsItalic;
            if (TextState.ForegroundColor is { } fg) lf.TextState.ForegroundColor = fg;
            lf.Position = new Position(rect.LLX, firstBaseline - i * leading);
            tb.AppendText(lf);
            double w;
            try { w = font.MeasureString(lines[i], fs); } catch { w = lines[i].Length * fs * 0.5; }
            if (w > maxW) maxW = w;
        }

        // The delete (SetContentStream) + line writes (AddContentStream) edited the
        // raw /Contents; drop any materialised typed-operator view so a later
        // page.Contents use (and save) re-reads the reflowed content.
        page.ResetContentsCache();

        // Update this fragment to the laid-out block (matching the absorber's
        // baseline+descentOff floor and baseline+ascentH top).
        double lastBaseline = firstBaseline - (lines.Count - 1) * leading;
        _rectangle = new Rectangle(rect.LLX, lastBaseline + descentOff, rect.LLX + maxW, rect.URY);
        _text = newText;
        _segments.Clear();
        for (var i = 0; i < lines.Count; i++)
        {
            var seg = new TextSegment(lines[i]);
            seg.TextState.FontSize = (float)fs;
            seg.TextState.FontName = TextState.FontName;
            seg.TextState.Font = font;
            seg.Owner = this;
            seg.Position = new Position(rect.LLX, firstBaseline - i * leading);
            seg.TextState.OwnerSegment = seg;
            _segments.Add(seg);
        }
        return true;
    }

    /// <summary>Delete the pre-reflow paragraph text. Removes each current
    /// segment's line at its own baseline Y (falls back to a page-wide delete of
    /// the joined text when segment positions are unavailable).</summary>
    private int DeleteReflowSource(Page page, string oldText)
    {
        var deleted = 0;
        foreach (var seg in _segments)
        {
            if (string.IsNullOrEmpty(seg.Text)) continue;
            var r = new TextReplacer();
            if (seg.Position is { } sp) r.TargetY = sp.YIndent;
            r.Replace(page, seg.Text, string.Empty);
            deleted += r.ReplacementCount;
        }
        if (deleted == 0 && !string.IsNullOrEmpty(oldText))
        {
            var r = new TextReplacer();
            if (_position is { } pos) r.TargetY = pos.YIndent;
            r.ReplaceWithCrossOperator(page, oldText, string.Empty);
            deleted += r.ReplacementCount;
        }
        return deleted;
    }

    /// <summary>
    /// Re-wrap the whole paragraph that contains this fragment after a replacement, so
    /// following words flow up to close the gap a shorter replacement leaves — matching
    /// the WholeWordsHyphenation re-flow. Groups the contiguous, same-left-margin
    /// lines around this fragment into a paragraph, applies the replacement to EVERY
    /// occurrence in it, greedy-wraps the result to the paragraph's width, and re-emits the
    /// lines at the original baseline grid (in the paragraph's dominant font). Returns false
    /// when the search text isn't in the detected paragraph (so sibling fragments no-op).
    /// </summary>
    private bool TryReflowParagraph(Page page, string oldText, string newText)
    {
        if (_position is not { } myPos || TextState.Font is not { } myFont || TextState.FontSize <= 0)
            return false;
        double fs = TextState.FontSize;

        var abs = new TextFragmentAbsorber(".+", new TextSearchOptions(true));
        // The line fragments absorbed here are deleted in place (see below); pin
        // ReplaceAdjustment.None so the deletion never shifts other same-line
        // content, independent of the absorber's ShiftRestOfLine default.
        abs.TextReplaceOptions = new TextReplaceOptions(TextReplaceOptions.ReplaceAdjustment.None);
        page.Accept(abs);
        // Precompute geometry so Position (non-null after this filter) isn't re-dereferenced.
        var lines0 = new System.Collections.Generic.List<(TextFragment f, double y, double lx, double rx)>();
        foreach (TextFragment f in abs.TextFragments)
        {
            var p = f.PositionOrNull;
            if (p is null) continue;
            if (string.IsNullOrWhiteSpace(f.Text)) continue;
            var rect = f.Rectangle;
            if (rect is null) continue;
            lines0.Add((f, p.YIndent, rect.LLX, rect.URX));
        }
        if (lines0.Count == 0) return false;
        // Top-to-bottom (PDF Y grows upward, so higher YIndent = higher on page).
        lines0.Sort((a, b) => b.y.CompareTo(a.y));

        // Find the re-absorbed line that CONTAINS this fragment. The fragment's own
        // LLX is the X of the matched token, which may sit mid-line (e.g. "{{Name}}"
        // embedded in flowing text), so match by Y proximity plus X-within-[lx,rx]
        // rather than assuming the fragment starts at the line's left margin.
        double myLLX = _rectangle!.LLX;
        int myIdx = -1; double best = fs;
        for (int i = 0; i < lines0.Count; i++)
        {
            double dy = System.Math.Abs(lines0[i].y - myPos.YIndent);
            bool xin = myLLX >= lines0[i].lx - 5 && myLLX <= lines0[i].rx + 5;
            if (dy <= best && xin && dy < fs) { best = dy; myIdx = i; }
        }
        if (myIdx < 0) return false;
        double leftX = lines0[myIdx].lx;

        // Grow the paragraph up/down over contiguous same-left-margin lines (one line pitch apart).
        // A line is only merged if it shares the left margin AND is close in font SIZE: a bigger
        // heading (e.g. a 24pt bold title above 12pt body, same left margin) is a SEPARATE
        // paragraph, so merging it would collapse it to body size on reflow. Same-size paragraphs
        // (the common case) are unaffected.
        const double xtol = 3.0;
        // IgnoreParagraphs = continuous-flow reflow: the replacement flows through the WHOLE text
        // block, ignoring paragraph boundaries. Grow across all contiguous same-size lines
        // regardless of left-margin changes so the entire block reflows as one unit and cascades
        // down naturally (no separate push-down of trailing paragraphs needed). Default mode keeps
        // the strict same-left-margin grow.
        bool ignorePara = _replaceOptions?.IgnoreParagraphs ?? false;
        double paraFs = lines0[myIdx].f.TextState.FontSize;
        if (paraFs <= 0) paraFs = fs;
        bool SizeCompatible(double lineFs) =>
            lineFs <= 0 || (lineFs <= paraFs * 1.35 && lineFs >= paraFs / 1.35);
        bool XCompatible(double lx) => ignorePara || System.Math.Abs(lx - leftX) <= xtol;
        int lo = myIdx, hi = myIdx;
        while (lo - 1 >= 0)
        {
            double gap = lines0[lo - 1].y - lines0[lo].y;
            if (XCompatible(lines0[lo - 1].lx) && gap > 0 && gap < 3 * fs
                && SizeCompatible(lines0[lo - 1].f.TextState.FontSize)) lo--;
            else break;
        }
        while (hi + 1 < lines0.Count)
        {
            double gap = lines0[hi].y - lines0[hi + 1].y;
            if (XCompatible(lines0[hi + 1].lx) && gap > 0 && gap < 3 * fs
                && SizeCompatible(lines0[hi + 1].f.TextState.FontSize)) hi++;
            else break;
        }

        var paraLines = lines0.GetRange(lo, hi - lo + 1);
        // Continuous flow anchors the re-emitted block at the flow's leftmost x.
        if (ignorePara)
            foreach (var l in paraLines) if (l.lx < leftX) leftX = l.lx;
        // Replace PER LINE (mirroring the per-fragment absorber), then reunite — an occurrence
        // split across a line break isn't a single-line match and is left intact (a
        // per-fragment replace also misses line-straddling occurrences).
        var origParts = new System.Collections.Generic.List<string>();
        var newParts = new System.Collections.Generic.List<string>();
        foreach (var l in paraLines)
        {
            var t = l.f.Text.Trim();
            origParts.Add(t);
            newParts.Add(t.Replace(oldText, newText, System.StringComparison.Ordinal));
        }
        var origText = string.Join(" ", origParts);
        var replaced = string.Join(" ", newParts);
        // Whole-paragraph replacement: when the matched fragment IS the entire paragraph
        // (oldText spans every line, e.g. a paragraph->paragraph+paragraph replace), no
        // single-line Replace fires, so replaced==origText. Detect that by comparing the
        // paragraph body to oldText ignoring all whitespace (robust to reconstruction
        // spacing differences) and re-wrap the replacement directly. Otherwise there is no
        // within-line occurrence in this paragraph and sibling fragments must no-op.
        bool wholePara = false;
        if (replaced == origText)
        {
            static string Squash(string s) =>
                System.Text.RegularExpressions.Regex.Replace(s, @"\s+", string.Empty);
            if (Squash(oldText) == Squash(origText)) { replaced = newText; wholePara = true; }
            else return false;
        }

        // Mid-token replacement (default flow): cascade from the MATCH position, matching
        // the reference — the paragraph lines above the match and the match line's prefix
        // stay untouched; text from the match onward re-packs onto the EXISTING baselines.
        // When the cascade can't handle the page's structure (CID font, cross-run match,
        // glyphs missing from the subset…) FALL THROUGH to the whole-paragraph re-wrap
        // below — bailing out entirely would leave the plain in-place replace to grow the
        // line past the page edge.
        if (!wholePara && !ignorePara
            && CascadeFromMatch(page, paraLines, myIdx - lo, myLLX, oldText, newText))
            return true;

        double rightX = 0;
        foreach (var l in paraLines) if (l.rx > rightX) rightX = l.rx;
        // Continuous-flow (IgnoreParagraphs): page-bound the wrap width. A previous longer
        // replacement can leave an over-wide unbreakable-token line, and re-absorbing that inflated
        // max-URX would compound the overflow. Cap the right border at the page's usable right edge
        // (mirror the left inset) so the flow wraps within the page instead of running off it.
        var pageRect = page.Rect;
        if (ignorePara && pageRect is not null)
        {
            double leftInset = leftX - pageRect.LLX;
            double pageRight = pageRect.URX - (leftInset > 0 ? leftInset : 0);
            if (pageRight > leftX + 10 && rightX > pageRight) rightX = pageRight;
        }
        // RightAdjustment extends the wrap border to the right so a longer replacement
        // re-flows into more lines against the widened margin. It applies only to the
        // mid-line-token reflow; a whole-paragraph replace re-wraps to the paragraph's own
        // width and ignores RightAdjustment.
        double rightAdjust = wholePara ? 0 : (_replaceOptions?.RightAdjustment ?? 0);
        double width = (rightX - leftX) + rightAdjust;
        if (width < 10) return false;

        // Re-flow in the paragraph's dominant font (the fragment carrying the most text),
        // so a lone bold word doesn't bold the whole paragraph and vice-versa.
        var domLine = paraLines[0].f;
        foreach (var l in paraLines) if (l.f.Text.Length > domLine.Text.Length) domLine = l.f;
        var domFont = domLine.TextState.Font ?? myFont;
        var domName = domLine.TextState.FontName ?? TextState.FontName;
        float domSize = domLine.TextState.FontSize > 0 ? domLine.TextState.FontSize : (float)fs;

        System.Collections.Generic.List<string> wrapped;
        if (wholePara)
        {
            // Shrink the font until the (larger) replacement fits the ORIGINAL rectangle,
            // HOLDING the line count measured at the original size, then re-wrap at the
            // fitted size. Compute the fit from the un-mutated original size (the fresh
            // re-absorb's, not THIS fragment's TextState which a caller may have already
            // shrunk via IsFitRectangle) and the original rectangle, so the result is
            // independent of the caller's font-size loop. Measure with a trailing space per
            // line: reserving one space width past each wrapped line breaks lines slightly
            // earlier and keeps the wrapped lines re-searchable across the line breaks.
            double origSize = domSize;
            double rectH = _rectangle!.Height;
            int nFit = WrapToWidth(replaced, domFont, origSize, width, trailingSpace: true).Count;
            if (nFit < 1) nFit = 1;
            double fitFs = origSize;
            while (fitFs > 1.0 && nFit * 1.2 * fitFs > rectH) fitFs -= 0.5;
            domSize = (float)fitFs;
            wrapped = WrapToWidth(replaced, domFont, fitFs, width, trailingSpace: true);
            // A LONGER whole-paragraph replacement flows into the ORIGINAL paragraph's line
            // grid (same baseline count). A greedy wrap at the full width under-fills (packs
            // one extra line's worth of text per line), so narrow the wrap width until the
            // line count reaches the original's. The exact per-line breaks produced by this
            // greedy wrapper still differ from an optimal/balanced line-breaker (so the flow,
            // and the block's URX, is a fidelity gap), but the line count — hence the segment
            // and baseline grid — matches.
            if (replaced.Length > origText.Length)
            {
                int targetLines = paraLines.Count;
                double renderW = width;
                int guard = 0;
                while (wrapped.Count < targetLines && renderW > 20 && guard++ < 800)
                {
                    renderW -= 0.5;
                    wrapped = WrapToWidth(replaced, domFont, fitFs, renderW, trailingSpace: true);
                }
            }
        }
        else
        {
            wrapped = WrapToWidth(replaced, domFont, domSize, width, allowCharBreak: ignorePara);
        }
        if (wrapped.Count == 0) return false;

        var baselines = new System.Collections.Generic.List<double>();
        foreach (var l in paraLines) baselines.Add(l.y);
        double pitch = baselines.Count >= 2 ? baselines[0] - baselines[1] : 1.2 * domSize;
        if (pitch <= 0) pitch = 1.2 * domSize;

        foreach (var l in paraLines)
        {
            // The re-absorbed line fragments have ReplaceAdjustment.None (fresh absorber),
            // so this deletes in place via the normal replace machinery without recursing
            // back into paragraph reflow.
            try { l.f.Text = string.Empty; } catch { }
        }

        var tb = new TextBuilder(page);
        var laidOut = new System.Collections.Generic.List<(string text, double baseline, double width)>();
        double maxLineW = 0;
        for (int i = 0; i < wrapped.Count; i++)
        {
            double by = i < baselines.Count ? baselines[i] : baselines[^1] - (i - baselines.Count + 1) * pitch;
            var frag = new TextFragment(wrapped[i]);
            frag.TextState.Font = domFont;
            if (!string.IsNullOrEmpty(domName)) frag.TextState.FontName = domName;
            frag.TextState.FontSize = domSize;
            if (TextState.ForegroundColor is { } fg) frag.TextState.ForegroundColor = fg;
            frag.Position = new Position(leftX, by);
            tb.AppendText(frag);
            double lw;
            try { lw = domFont.MeasureString(wrapped[i], domSize); } catch { lw = wrapped[i].Length * domSize * 0.5; }
            laidOut.Add((wrapped[i], by, lw));
            if (lw > maxLineW) maxLineW = lw;
        }
        page.ResetContentsCache();

        // A whole-paragraph replace re-points THIS fragment at the laid-out block so a caller
        // that reads fragment.Segments / fragment.Rectangle after the assignment (e.g. to add
        // a per-segment underline or a per-fragment highlight) sees the reflowed geometry. Box
        // mirrors the absorber: LLY = baseline, URY = baseline + 1.1*fs; URX = widest line.
        if (wholePara && laidOut.Count > 0)
        {
            double firstBaseline = laidOut[0].baseline;
            double lastBaseline = laidOut[^1].baseline;
            double ascentH = 1.1 * domSize;
            _rectangle = new Rectangle(leftX, lastBaseline, leftX + maxLineW, firstBaseline + ascentH);
            _text = newText;
            _segments.Clear();
            foreach (var ln in laidOut)
            {
                var seg = new TextSegment(ln.text);
                seg.TextState.FontSize = domSize;
                if (!string.IsNullOrEmpty(domName)) seg.TextState.FontName = domName;
                seg.TextState.Font = domFont;
                seg.Owner = this;
                seg.Position = new Position(leftX, ln.baseline);
                seg.TextState.OwnerSegment = seg;
                _segments.Add(seg);
            }
        }
        return true;
    }

    /// <summary>Reference-parity reflow for a mid-line token replacement
    /// (WholeWordsHyphenation): everything BEFORE the match stays untouched; the
    /// replacement plus all following paragraph text re-packs greedily from the match
    /// position onto the paragraph's EXISTING baselines (baselines never move; trailing
    /// lines that empty out stay empty). Works at run granularity — each source segment
    /// is one text-showing operator, deleted by whole-operator match at its page-space
    /// position and re-emitted as packed lines.</summary>
    private bool CascadeFromMatch(Page page,
        System.Collections.Generic.List<(TextFragment f, double y, double lx, double rx)> paraLines,
        int matchLine, double myLLX, string oldText, string newText)
    {
        if (matchLine < 0 || matchLine >= paraLines.Count) return false;

        // Reference-parity path first: MOVE the original runs (keeping their bytes,
        // fonts, kerning and per-run Tc) and rewrite only the matched operator,
        // re-encoded in its own font. Positions then match the reference to hundredths
        // of a point. Falls back to the coarser delete-and-re-emit below when the page
        // structure defeats it (CID font, replacement glyphs missing from the subset,
        // match not carried by a single run).
        {
            var rlines = new System.Collections.Generic.List<(double y, double lx, double rx)>();
            for (int li = matchLine; li < paraLines.Count; li++)
                rlines.Add((paraLines[li].y, paraLines[li].lx, paraLines[li].rx));
            double pLeft = double.MaxValue, maxRx = 0;
            foreach (var l in paraLines)
            {
                if (l.lx < pLeft) pLeft = l.lx;
                if (l.rx > maxRx) maxRx = l.rx;
            }
            double rPitch = rlines.Count >= 2 ? rlines[0].y - rlines[1].y : 0;
            if (rPitch <= 0) rPitch = 1.2 * (TextState.FontSize > 0 ? TextState.FontSize : 10);
            // The reference wraps at a right margin mirroring the paragraph's left inset
            // (MediaBox width − left X); never tighter than the paragraph's own extent.
            double mediaW = page.MediaBox is { } mbx ? mbx.URX - mbx.LLX : 0;
            double rMargin = System.Math.Max(mediaW - pLeft, maxRx);
            var mover = new TextReplacer();
            if (mover.ReflowFromMatch(page, oldText, newText, myLLX, rlines, pLeft, rMargin, rPitch))
            {
                page.ResetContentsCache();
                return true;
            }
        }

        // Effective (page-space) font scale: producers that draw each run in its own
        // q/cm/BT..ET/Q block size text via Tm with the CTM shrinking it back; measuring
        // or re-emitting at the raw Tm size would be wrong by the CTM factor.
        double ctmScale = 1.0;
        if (ExtractionCtm is { } ectm)
        {
            var det = System.Math.Abs(ectm.A * ectm.D - ectm.B * ectm.C);
            if (det > 1e-9) ctmScale = System.Math.Sqrt(det);
        }

        // Collect the segments to re-flow, each one source run: on the match line those
        // at/after the match X, on the following paragraph lines all of them.
        var moved = new System.Collections.Generic.List<(TextSegment seg, double x, double y)>();
        for (int li = matchLine; li < paraLines.Count; li++)
        {
            foreach (var seg in paraLines[li].f.Segments)
            {
                if (seg.Position is not { } sp) continue;
                if (string.IsNullOrEmpty(seg.Text)) continue;
                if (li == matchLine && sp.XIndent < myLLX - 0.5) continue; // prefix stays
                moved.Add((seg, sp.XIndent, sp.YIndent));
            }
        }
        if (moved.Count == 0) return false;
        moved.Sort((a, b) => b.y != a.y ? b.y.CompareTo(a.y) : a.x.CompareTo(b.x));

        // The first moved segment must carry the matched token (a match hidden mid-run
        // with a prefix inside the same run is left to the in-place replace path).
        var head = moved[0].seg.Text;
        int occ = head.IndexOf(oldText, System.StringComparison.Ordinal);
        if (occ < 0) return false;

        // Combined text from the match onward. Same-line neighbours concatenate verbatim
        // (their spacing rides in the runs); a line break is a word boundary. NBSPs fold
        // to plain spaces — producers that pad word gaps with U+00A0 would otherwise glue
        // the NBSP onto the next word through the space-split below, and the re-emitted
        // line would never phrase-match a plain-space search.
        var sb = new System.Text.StringBuilder();
        sb.Append(head.Replace(oldText, newText, System.StringComparison.Ordinal));
        for (int i = 1; i < moved.Count; i++)
        {
            bool lineBreak = System.Math.Abs(moved[i].y - moved[i - 1].y) > 0.75;
            if (lineBreak && sb.Length > 0 && sb[^1] != ' ' && !moved[i].seg.Text.StartsWith(" "))
                sb.Append(' ');
            sb.Append(moved[i].seg.Text);
        }
        sb.Replace('\u00A0', ' ');

        // Measure/emit in the paragraph's dominant face at the effective size.
        var domSeg = moved[0].seg;
        foreach (var m in moved)
            if (m.seg.Text.Trim().Length > domSeg.Text.Trim().Length) domSeg = m.seg;
        var font = domSeg.TextState.Font ?? TextState.Font;
        double rawFs = domSeg.TextState.FontSize > 0 ? domSeg.TextState.FontSize : TextState.FontSize;
        double effFs = rawFs * ctmScale;
        if (font is null || effFs <= 0.5) return false;
        // Prefer the SYSTEM face of the same family for measuring and re-emission. The
        // source font is typically an embedded SUBSET whose width table is keyed by its
        // custom byte codes, so measuring Unicode text against it mis-indexes the widths;
        // the system face carries the true advances (the reference measures its reflow
        // with these), and embedding it makes the absorber read the same metrics back, so
        // the re-emitted words land at reference-parity positions.
        var faceName = font.FontName ?? string.Empty;
        int subsetPlus = faceName.IndexOf('+');
        if (subsetPlus >= 0 && subsetPlus + 1 < faceName.Length)
            faceName = faceName[(subsetPlus + 1)..];
        int styleComma = faceName.IndexOf(',');
        if (styleComma > 0) faceName = faceName[..styleComma];
        if (faceName.Length > 0
            && FontRepository.FindFont(faceName, ignoreCase: true) is { } sysFont)
            font = sysFont;

        double leftX = double.MaxValue, rightX = 0;
        for (int li = matchLine; li < paraLines.Count; li++)
        {
            if (paraLines[li].lx < leftX) leftX = paraLines[li].lx;
            if (paraLines[li].rx > rightX) rightX = paraLines[li].rx;
        }
        if (rightX - leftX < 10 || rightX <= myLLX + 5) return false;

        // Greedy pack: first line from the match X, continuation lines from the left margin.
        var words = sb.ToString().Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return false;
        double SpaceW() { try { return font.MeasureString(" ", effFs); } catch { return effFs * 0.25; } }
        double WordW(string w) { try { return font.MeasureString(w, effFs); } catch { return w.Length * effFs * 0.5; } }
        var packed = new System.Collections.Generic.List<string>();
        var cur = new System.Text.StringBuilder();
        double curX = myLLX, curW = 0, spaceW = SpaceW();
        foreach (var w in words)
        {
            double ww = WordW(w);
            double trial = cur.Length == 0 ? ww : curW + spaceW + ww;
            if (curX + trial <= rightX + 0.5 || cur.Length == 0)
            {
                if (cur.Length > 0) cur.Append(' ');
                cur.Append(w);
                curW = trial;
            }
            else
            {
                packed.Add(cur.ToString());
                cur.Clear(); cur.Append(w);
                curW = ww; curX = leftX;
            }
        }
        if (cur.Length > 0) packed.Add(cur.ToString());

        // Existing baselines from the match line down; extend below by the pitch if the
        // packed text needs more lines than the paragraph had.
        var baselines = new System.Collections.Generic.List<double>();
        for (int li = matchLine; li < paraLines.Count; li++) baselines.Add(paraLines[li].y);
        double pitch = baselines.Count >= 2 ? baselines[0] - baselines[1] : 1.2 * effFs;
        if (pitch <= 0) pitch = 1.2 * effFs;

        // Delete the source runs by REGION, one line at a time, at operator granularity:
        // producers that draw one word (or one bare space) per operator defeat text-keyed
        // deletion — the absorber's coalesced segment text (with synthesized gap spaces)
        // never equals any single operator's decode. Every text operator starting inside
        // the line's X-span goes; the match line is cleared only from the match X on, so
        // its prefix stays put.
        for (int li = matchLine; li < paraLines.Count; li++)
        {
            double xmin = (li == matchLine ? myLLX : paraLines[li].lx) - 0.5;
            double xmax = paraLines[li].rx + 1.0;
            if (xmax <= xmin) continue;
            var del = new TextReplacer
            {
                MatchAnyOperator = true,
                TargetY = paraLines[li].y,
                TargetX = (xmin + xmax) / 2,
                TargetXTolerance = (xmax - xmin) / 2,
            };
            del.Replace(page, string.Empty, string.Empty);
        }

        // Re-emit the packed lines.
        var tb = new TextBuilder(page);
        for (int i = 0; i < packed.Count; i++)
        {
            double by = i < baselines.Count ? baselines[i] : baselines[^1] - (i - baselines.Count + 1) * pitch;
            var frag = new TextFragment(packed[i]);
            frag.TextState.Font = font;
            if (domSeg.TextState.FontName is { Length: > 0 } fn) frag.TextState.FontName = fn;
            frag.TextState.FontSize = (float)effFs;
            if (TextState.ForegroundColor is { } fg) frag.TextState.ForegroundColor = fg;
            frag.Position = new Position(i == 0 ? myLLX : leftX, by);
            tb.AppendText(frag);
        }
        page.ResetContentsCache();
        return true;
    }

    /// <summary>Greedy word-wrap of <paramref name="text"/> to <paramref name="maxWidth"/>
    /// using the font's real advance metrics at <paramref name="fs"/>. When
    /// <paramref name="trailingSpace"/> is set, each candidate line is measured WITH a
    /// trailing space (reserving one space width past each line, so lines break slightly
    /// earlier) and every completed (non-final) line keeps that trailing space — this keeps
    /// the wrapped lines re-searchable across the breaks.</summary>
    private static double MeasureOrEstimate(FontInfo font, string s, double fs, bool trailingSpace)
    {
        var m = trailingSpace ? s + " " : s;
        try { return font.MeasureString(m, fs); } catch { return m.Length * fs * 0.5; }
    }

    private static System.Collections.Generic.List<string> WrapToWidth(string text, FontInfo font, double fs, double maxWidth, bool trailingSpace = false, bool allowCharBreak = false)
    {
        var lines = new System.Collections.Generic.List<string>();
        var words = text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Split(' ');
        var cur = new System.Text.StringBuilder();
        foreach (var word in words)
        {
            if (word.Length == 0) continue;
            var trial = cur.Length == 0 ? word : cur + " " + word;
            if (MeasureOrEstimate(font, trial, fs, trailingSpace) <= maxWidth)
            {
                if (cur.Length > 0) cur.Append(' ');
                cur.Append(word);
                continue;
            }
            // The word doesn't fit the current line. Flush the line, then place the word fresh.
            if (cur.Length > 0)
            {
                lines.Add(trailingSpace ? cur.ToString() + " " : cur.ToString());
                cur.Clear();
            }
            if (!allowCharBreak || MeasureOrEstimate(font, word, fs, trailingSpace) <= maxWidth)
            {
                // Fits on its own line (or char-break disabled: keep the original behaviour of a
                // lone over-wide word occupying its own line).
                cur.Append(word);
            }
            else
            {
                // A single word wider than the line (an unbreakable long token, e.g. a no-space
                // replacement): character-break it so it stays within the page instead of running
                // off the right edge. Emit as many leading characters as fit per line.
                int start = 0;
                while (start < word.Length)
                {
                    int take = 1;
                    while (start + take < word.Length &&
                           MeasureOrEstimate(font, word.Substring(start, take + 1), fs, trailingSpace) <= maxWidth) take++;
                    string chunk = word.Substring(start, take);
                    start += take;
                    if (start < word.Length) lines.Add(trailingSpace ? chunk + " " : chunk);
                    else cur.Append(chunk); // last chunk continues the current line
                }
            }
        }
        if (cur.Length > 0) lines.Add(cur.ToString());
        return lines;
    }

    /// <summary>Binary-search the largest font size in [<paramref name="lo"/>,
    /// <paramref name="hi"/>] whose wrapped block height fits
    /// <paramref name="targetHeight"/>. Block height =
    /// (lines-1)·leadingRatio·fs + 1.1·fs (the absorber's ascentH−descentOff).</summary>
    private static double FitFontSize(string text, FontInfo font, double wrapWidth,
        double targetHeight, double lo, double hi, double leadingRatio)
    {
        double BlockHeight(double fs)
        {
            var n = WrapToWidth(text, font, fs, wrapWidth).Count;
            return (n - 1) * leadingRatio * fs + 1.1 * fs;
        }
        for (var it = 0; it < 48; it++)
        {
            var mid = (lo + hi) / 2;
            if (BlockHeight(mid) <= targetHeight) lo = mid; else hi = mid;
        }
        return lo;
    }

    /// <summary>When the fragment's edit options explicitly select
    /// <see cref="TextEditOptions.NoCharacterAction.ThrowException"/>, throw
    /// <see cref="InvalidOperationException"/> if the new text contains a character
    /// the fragment's font cannot represent.</summary>
    private void ThrowIfFontLacksGlyph(string newText)
    {
        if (_textEditOptions is not { NoCharacterBehaviorExplicit: true } teo
            || teo.NoCharacterBehavior != TextEditOptions.NoCharacterAction.ThrowException
            || TextState.Font is not { } font
            || string.IsNullOrEmpty(newText))
            return;

        foreach (var ch in newText)
            if (!font.CanRepresent(ch))
                throw new InvalidOperationException(
                    $"Font '{font.FontName}' does not contain a glyph for character " +
                    $"'{ch}' (U+{(int)ch:X4}).");
    }

    /// <summary>
    /// Replace options inherited from the producing TextFragmentAbsorber. Drives
    /// the rect-recompute behavior in the <see cref="Text"/> setter — when set
    /// to <see cref="TextReplaceOptions.ReplaceAdjustment.None"/>, the
    /// fragment's <see cref="Rectangle"/> shrinks to the replacement text's
    /// advance width.
    /// </summary>
    private TextReplaceOptions? _replaceOptions;

    /// <summary>Text-replacement options used by TextFragmentAbsorber
    /// replace paths. Lazy-initialised on first access so callers can
    /// mutate properties without setting a fresh instance first.</summary>
    public TextReplaceOptions ReplaceOptions
    {
        get => _replaceOptions ??= new TextReplaceOptions(TextReplaceOptions.ReplaceAdjustment.None);
        internal set => _replaceOptions = value;
    }

    /// <summary>
    /// The page this fragment was extracted from. When set, modifying <see cref="Text"/>
    /// will update the PDF content stream.
    /// </summary>
    internal Page? SourcePage { get; set; }

    /// <summary>The page this fragment belongs to (public alias for SourcePage).</summary>
    public Page? Page => SourcePage;

    /// <summary>Raw <c>re</c> operands (X, Y, Width, Height) of underline rectangles found
    /// in the source content beneath this fragment, captured when the absorber runs with
    /// <see cref="TextEditOptions.ToAttemptGetUnderlineFromSource"/>. If the fragment's
    /// underline is later toggled off, these locate the exact rectangle operators to splice
    /// out of the page content stream at save time.</summary>
    internal System.Collections.Generic.List<(double X, double Y, double W, double H)>? CapturedUnderlineSources;

    /// <summary>Records a captured source underline rectangle and marks this fragment and all
    /// its segments as underlined (without registering save-time underline injection).</summary>
    internal void MarkCapturedUnderlineSource(double x, double y, double w, double h)
    {
        (CapturedUnderlineSources ??= new()).Add((x, y, w, h));
        TextState.SetCapturedUnderline(true);
        if (_segments is not null)
            foreach (var s in _segments)
                s.TextState?.SetCapturedUnderline(true);
    }

    /// <summary>Raw <c>re</c> operands of background (highlight) rectangles drawn in the
    /// source content behind this fragment, captured when the absorber runs with
    /// <see cref="TextEditOptions.ToAttemptGetUnderlineFromSource"/>. When the fragment's
    /// text is replaced, these locate the old highlight to splice out so a new one can be
    /// drawn at the replacement's advance.</summary>
    internal System.Collections.Generic.List<(double X, double Y, double W, double H)>? CapturedBackgroundSources;

    /// <summary>Fill colour of the captured source background, re-used when the highlight
    /// is re-drawn for replaced text.</summary>
    internal Color? CapturedBackgroundColor;

    /// <summary>Records a captured source background (highlight) rectangle without
    /// registering save-time background injection.</summary>
    internal void MarkCapturedBackgroundSource(double x, double y, double w, double h, Color? color)
    {
        (CapturedBackgroundSources ??= new()).Add((x, y, w, h));
        CapturedBackgroundColor ??= color;
    }

    /// <summary>Measure the replacement text's advance for decoration sizing. A subset
    /// font carries widths only for its own glyphs, so when any replacement character
    /// lacks an explicit width the embedded metrics would degrade to default (1 em)
    /// widths — fall back to the real system face of the same family/style, which is
    /// what the replaced text renders in after the subset-glyph font switch.</summary>
    private static double MeasureReplacementAdvance(FontInfo font, string text, double fontSize)
    {
        var covered = true;
        try
        {
            var m = font.Metrics;
            foreach (var ch in text)
            {
                var code = m.IsCid ? ch : (ch < 256 ? ch : '?');
                if (!m.HasExplicitWidth(code)) { covered = false; break; }
            }
        }
        catch { covered = true; }
        if (!covered && font.FontName is { Length: > 0 } name)
        {
            try
            {
                // Measure with the system face's raw TTF metrics (FontData.MeasureString);
                // Font.MeasureString would consult the synthetic font dict, which carries
                // no widths for a repository-resolved face.
                var real = FontRepository.FindFont(name, ignoreCase: true);
                if (real?.SourceFontData is { } fd)
                {
                    var w = fd.MeasureString(text, fontSize);
                    if (w > 0) return w;
                }
            }
            catch { }
        }
        try { return font.MeasureString(text, fontSize); }
        catch { return -1; }
    }

    /// <summary>The text as last written to the content stream by TextBuilder.</summary>
    internal string? LastWrittenText { get; set; }

    /// <summary>The XForm this fragment was extracted from, or null for page-level fragments.</summary>
    public XForm? Form { get; internal set; }

    /// <summary>
    /// Optional footnote attached to this fragment. Stored only; the
    /// layout engine does not currently render footnote references or
    /// the page-bottom note text.
    /// </summary>
    public Note? FootNote { get; set; }

    /// <summary>
    /// Horizontal alignment used when this fragment is laid out as a
    /// paragraph (added to <c>page.Paragraphs</c>). Stored on the
    /// fragment so callers can set alignment without touching
    /// <see cref="TextState"/>; the layout engine reads this on save.
    /// </summary>
    public new HorizontalAlignment HorizontalAlignment { get; set; } = HorizontalAlignment.Left;

    /// <summary>
    /// Check if this fragment uses a CID/Type0 font (Arabic, CJK, etc.).
    /// CID fonts store text in visual order with multi-byte character codes,
    /// requiring segment-by-segment replacement when the combined text isn't
    /// found in a single content stream operator.
    /// </summary>
    private bool IsCidFontFragment()
    {
        // Check font metadata first
        foreach (var seg in _segments)
        {
            if (seg.TextState?.Font?.IsCid == true) return true;
        }
        // Fallback: detect by Arabic/CJK presentation forms in the text
        foreach (var seg in _segments)
        {
            foreach (var ch in seg.Text)
            {
                if ((ch >= '\uFB50' && ch <= '\uFDFF') || (ch >= '\uFE70' && ch <= '\uFEFF') ||
                    (ch >= '\u3000' && ch <= '\u9FFF'))
                    return true;
            }
        }
        return false;
    }

    /// <summary>The bounding rectangle of this text fragment on the page.</summary>
    public Rectangle? Rectangle
    {
        get
        {
            if (_rectangle != null) return _rectangle;
            // Estimate rectangle for newly created fragments from font metrics.
            // Used before the fragment is placed on a page. An empty fragment
            // still yields a (zero-width) rectangle, matching Aspose.PDF for
            // .NET, whose Rectangle is never null for a constructed fragment.
            {
                double fontSize = TextState.FontSize;
                double width = 0;
                if (Text != null && Text.Length > 0)
                {
                    var font = TextState.Font;
                    if (font != null)
                    {
                        try { width = font.MeasureString(Text, fontSize); }
                        catch { width = Text.Length * fontSize * 0.5; }
                    }
                    else
                        width = Text.Length * fontSize * 0.5;
                }

                double height = fontSize * 1.2; // approximate line height
                double x = PositionOrNull?.XIndent ?? 0;
                double y = PositionOrNull?.YIndent ?? 0;
                return new Rectangle(x, y - height * 0.2, x + width, y + height * 0.8);
            }
        }
        internal set => _rectangle = value;
    }
    private Rectangle? _rectangle;

    /// <summary>The text position.</summary>
    public Position? Position
    {
        // Never return null: hand back a lazily-created (0,0) Position so callers can
        // write `fragment.Position.XIndent = …` on a freshly-constructed fragment
        // without a NullReferenceException (matches the Aspose.Pdf surface).
        // IMPORTANT: the auto-Position is kept in a SEPARATE field and is NOT written
        // into _position, so field-based readers (Rectangle, BaselinePosition) still
        // see "no position" for an unpositioned fragment. It also starts Touched==false,
        // so merely reading Position does not make the fragment count as explicitly
        // positioned — only writing XIndent/YIndent (or the setter) does. See
        // HasExplicitPosition.
        get => _position ?? (_autoPosition ??= new Position(0, 0));
        set
        {
            _positionExplicit = value is not null;
            var oldPos = _position;
            _position = value;
            // Reposition all segments relative to the new position.
            // Preserve their relative offsets from the fragment's previous position.
            if (value is not null && _segments.Count > 0)
            {
                // Determine reference position: use old fragment position, or first segment's position
                var refPos = oldPos ?? _segments[1].Position;
                if (refPos is not null)
                {
                    var dx = value.XIndent - refPos.XIndent;
                    var dy = value.YIndent - refPos.YIndent;
                    foreach (var seg in _segments)
                    {
                        if (seg.Position is not null)
                            seg.Position = new Position(seg.Position.XIndent + dx, seg.Position.YIndent + dy);
                    }
                }
                else
                {
                    _segments[1].Position = value;
                }
            }
        }
    }
    private Position? _position;
    // Lazily-created stand-in returned by the Position getter when no real position
    // is set; never stored in _position (so field-based readers stay null-correct).
    private Position? _autoPosition;
    private bool _positionExplicit;

    /// <summary>Whether this fragment has a caller-specified position — set via the
    /// <see cref="Position"/> setter (absorber / generator) or by writing
    /// <c>Position.XIndent</c>/<c>YIndent</c> on the auto-materialised Position.
    /// Distinct from "<c>Position != null</c>" because the getter now never returns
    /// null; consumers that previously branched on null use this instead so a fragment
    /// that merely had its Position read still flows / derives geometry as before.</summary>
    internal bool HasExplicitPosition
        => _positionExplicit || (_position?.Touched ?? false) || (_autoPosition?.Touched ?? false);

    /// <summary>The real position, or null when none was set — i.e. the pre-refactor
    /// semantics of the <see cref="Position"/> getter (which now never returns null).
    /// Cross-instance readers that fall back to Rectangle/owner when unpositioned use
    /// this so the auto-materialised (0,0) stand-in doesn't suppress the fallback.
    /// A touched auto-Position (the caller did <c>Position.XIndent = …</c> on a fresh
    /// fragment) counts as a real position and is surfaced here too.</summary>
    internal Position? PositionOrNull
        => _position ?? (_autoPosition is { Touched: true } ? _autoPosition : null);

    /// <summary>
    /// The text baseline position. Position includes descent offset (bottom of text rect);
    /// BaselinePosition is the actual text baseline (higher by |descent|).
    /// </summary>
    public Position? BaselinePosition
    {
        get
        {
            // No baseline for an unpositioned fragment. PositionOrNull is null unless a
            // real position was set (or the auto-Position was touched), so the auto
            // (0,0) stand-in doesn't fabricate a baseline.
            var p = PositionOrNull;
            if (p is null) return null;
            // Compute descent from font metrics
            double descent = 0;
            var font = TextState.Font;
            var metrics = font?.GetMetrics();
            var fs = TextState.FontSize;
            if (metrics is not null && metrics.Descent != 0)
                descent = metrics.Descent * fs / 1000.0; // negative value
            return new Position(p.XIndent, p.YIndent - descent);
        }
        set
        {
            if (value is null) { _position = null; _positionExplicit = false; return; }
            // Reverse: add descent to get Position from BaselinePosition
            double descent = 0;
            var font = TextState.Font;
            var metrics = font?.GetMetrics();
            var fs = TextState.FontSize;
            if (metrics is not null && metrics.Descent != 0)
                descent = metrics.Descent * fs / 1000.0;
            _position = new Position(value.XIndent, value.YIndent + descent);
            _positionExplicit = true;
        }
    }

    /// <summary>Margin information for layout when used as a paragraph element.</summary>
    public new MarginInfo Margin { get; set; } = new();

    /// <summary>Text state (font, size, color). Fragment-typed wrapper
    /// around the underlying <see cref="Aspose.Pdf.Text.TextState"/> so
    /// callers can reach <see cref="TextFragmentState.Font"/> as
    /// <see cref="Font"/> and the fragment-only members
    /// (DrawTextRectangleBorder, TabStops, IsFitRectangle).</summary>
    public TextFragmentState TextState { get; }

    /// <summary>The page index (0-based) this fragment was found on.</summary>
    public int PageIndex { get; internal set; }

    /// <summary>
    /// Text direction in page space — the reading-direction unit vector
    /// transformed by both the text matrix and the CTM.
    /// For horizontal LTR text: (1, 0). For 90° rotated vertical text: (0, ±1).
    /// </summary>
    internal double TextDirX { get; set; } = 1;
    internal double TextDirY { get; set; }

    /// <summary>
    /// Trailing character spacing (Tc * HScaling * TmA) in page space, subtracted
    /// from Rectangle.Width to get the visual glyph-only width for bg rect rendering.
    /// </summary>
    internal double TrailingTcPageSpace { get; set; }

    /// <summary>
    /// The CTM that was active when this fragment was extracted.
    /// Used to transform page-space coordinates back to content-stream space
    /// when injecting background/underline rectangles.
    /// </summary>
    internal Matrix? ExtractionCtm { get; set; }

    /// <summary>Shortcut for TextState.FontSize.</summary>
    public double FontSize => TextState.FontSize;

    /// <summary>
    /// The collection of text segments that make up this fragment (1-indexed).
    /// A newly constructed fragment always contains at least one segment.
    /// Setter replaces every segment with the supplied collection's items.
    /// </summary>
    public TextSegmentCollection Segments
    {
        get => _segments;
        set
        {
            // Mutate-in-place rather than rebinding so the existing Owner
            // back-reference stays intact.
            _segments.Clear();
            if (value is null) return;
            foreach (var s in value) _segments.Add(s);
            RefreshTextFromSegments();
        }
    }

    /// <summary>
    /// Isolate the character range <c>[startIndex, startIndex+length)</c>
    /// (<paramref name="startIndex"/> is a 0-based character offset into the
    /// fragment's text) into its own <see cref="TextSegment"/>(s): the
    /// covering segment is split into up to three pieces (before / isolated /
    /// after), each inheriting the original segment's <see cref="TextState"/>,
    /// and the fragment's <see cref="Segments"/> collection is rebuilt to
    /// reflect the split. Returns the isolated (middle) segments so a caller
    /// can restyle just that range, e.g. recolour "95" inside "Windows 95 ".
    /// </summary>
    public TextSegmentCollection IsolateTextSegments(int startIndex, int length)
    {
        var result = new TextSegmentCollection();
        if (length <= 0 || startIndex < 0) return result;

        var rangeStart = startIndex;
        var rangeEnd = startIndex + length;
        var rebuilt = new List<TextSegment>();
        var cursor = 0;
        foreach (var seg in _segments)
        {
            var text = seg.Text ?? string.Empty;
            var segStart = cursor;
            var segEnd = cursor + text.Length;
            cursor = segEnd;

            // No overlap with the isolation range — keep the segment intact.
            if (segEnd <= rangeStart || segStart >= rangeEnd)
            {
                rebuilt.Add(seg);
                continue;
            }

            // Overlap, expressed in this segment's local coordinates.
            var localStart = Math.Max(rangeStart, segStart) - segStart;
            var localEnd = Math.Min(rangeEnd, segEnd) - segStart;

            if (localStart > 0)
                rebuilt.Add(CloneSegmentText(seg, text.Substring(0, localStart)));

            var isolated = CloneSegmentText(seg, text.Substring(localStart, localEnd - localStart));
            rebuilt.Add(isolated);
            result.Add(isolated);

            if (localEnd < text.Length)
                rebuilt.Add(CloneSegmentText(seg, text.Substring(localEnd)));
        }

        _segments.Clear();
        foreach (var s in rebuilt) _segments.Add(s);
        RefreshTextFromSegments();
        return result;
    }

    /// <summary>New <see cref="TextSegment"/> carrying <paramref name="text"/>
    /// with a copy of <paramref name="src"/>'s text state.</summary>
    private static TextSegment CloneSegmentText(TextSegment src, string text)
    {
        var s = new TextSegment(text);
        s.TextState.ApplyChangesFrom(src.TextState);
        return s;
    }

    /// <summary>Shallow copy of the fragment (text + state). Segments are
    /// regenerated from the cloned text.</summary>
    public override object Clone()
    {
        var copy = new TextFragment(_text, Rectangle, textState: null);
        copy.TextState.ApplyChangesFrom(TextState);
        copy.TabStops = TabStops;
        copy.IsInLineParagraph = IsInLineParagraph;
        copy.IsInNewPage = IsInNewPage;
        copy.VerticalAlignment = VerticalAlignment;
        copy.WrapLinesCount = WrapLinesCount;
        return copy;
    }

    /// <summary>Clone the fragment AND its segments. The cloned fragment
    /// has fresh segment instances that mirror the source's text+state.</summary>
    public object CloneWithSegments()
    {
        var copy = (TextFragment)Clone();
        copy._segments.Clear();
        foreach (var s in _segments)
        {
            var fresh = new TextSegment(s.Text);
            fresh.TextState.ApplyChangesFrom(s.TextState);
            copy._segments.Add(fresh);
        }
        copy.RefreshTextFromSegments();
        return copy;
    }

    /// <summary>Hyperlink applied to this fragment. Set-only on the
    /// Aspose.Pdf surface; the underlying hyperlink action is wired into
    /// the page's annotation stream at save time (stored only in this build).</summary>
    public new Hyperlink Hyperlink
    {
        set
        {
            _hyperlink = value;
            // Register for save-time link-annotation emission when set on a fragment
            // obtained via TextFragmentAbsorber (the generator path emits links during
            // layout, but absorber-edited fragments need an explicit save-time pass).
            if (value is not null) SourcePage?.RegisterHyperlinkFragment(this);
        }
    }
    private Hyperlink? _hyperlink;

    /// <summary>Internal read access to the hyperlink set via <see cref="Hyperlink"/>,
    /// used by the page layout pass to emit the corresponding link annotation.</summary>
    internal Hyperlink? HyperlinkValue => _hyperlink;

    /// <summary>Endnote attached to this fragment. Stored only.</summary>
    public Note? EndNote { get; set; }

    /// <summary>Number of wrapped lines computed during layout.
    /// 0 until layout runs.</summary>
    public int WrapLinesCount { get; set; }

    internal void RefreshTextFromSegments()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var seg in _segments)
            sb.Append(seg.Text);
        _text = sb.ToString();
    }
}

/// <summary>
/// Represents a position on a page.
/// </summary>
/// <summary>Font style flags matching the public API.</summary>
[Flags]
public enum FontStyles
{
    Regular = 0,
    Bold = 1,
    Italic = 2,
}

public sealed class Position
{
    public Position(double xIndent, double yIndent)
    {
        // Assign the backing fields directly so construction does not set Touched —
        // only a later property write counts as the caller "setting" the position.
        _xIndent = xIndent;
        _yIndent = yIndent;
    }

    private double _xIndent;
    private double _yIndent;

    /// <summary>True once <see cref="XIndent"/>/<see cref="YIndent"/> has been
    /// written through a property setter (not via the constructor). Lets the owning
    /// <see cref="TextFragment"/> distinguish a position the caller explicitly set —
    /// e.g. <c>fragment.Position.XIndent = …</c> on a fresh fragment — from one that
    /// was merely auto-created when the (never-null) Position getter was read.</summary>
    internal bool Touched { get; private set; }

    public double XIndent { get => _xIndent; set { _xIndent = value; Touched = true; } }
    public double YIndent { get => _yIndent; set { _yIndent = value; Touched = true; } }

    public override bool Equals(object? obj)
        => obj is Position other
           && Math.Abs(XIndent - other.XIndent) < 0.001
           && Math.Abs(YIndent - other.YIndent) < 0.001;

    public override int GetHashCode()
        => HashCode.Combine(Math.Round(XIndent, 2), Math.Round(YIndent, 2));

    public override string ToString() => $"Position({XIndent:F3}, {YIndent:F3})";
}

/// <summary>
/// Text formatting state.
/// </summary>
public class TextState
{
    /// <summary>Default tab-stop width in PDF points (56 pt ≈ 0.78 in,
    /// matches Adobe's default tab spacing). Declared as an instance
    /// field to match the non-static field reflection signature of Aspose.Pdf.</summary>
    public float TabstopDefaultValue = 56f;

    public TextState() { }

    public TextState(double fontSize) { FontSize = (float)fontSize; }

    public TextState(string fontFamily) { FontName = fontFamily; }

    public TextState(string fontFamily, double fontSize)
    {
        FontName = fontFamily;
        FontSize = (float)fontSize;
    }

    public TextState(string fontFamily, bool bold, bool italic)
    {
        // Keep the family name clean and carry the requested style as flags. The styled
        // base-font name (e.g. "Times" + Bold → Times-Bold, "Courier" + Italic →
        // Courier-Oblique) is resolved from FontName + FontStyle at the point the font is
        // applied, so the standard-14 oblique/italic spelling differences are handled in
        // one place instead of being baked into the name here.
        FontName = fontFamily;
        IsBold = bold;
        IsItalic = italic;
    }

    public TextState(System.Drawing.Color foregroundColor)
    {
        ForegroundColor = Color.FromRgb(foregroundColor);
    }

    public TextState(System.Drawing.Color foregroundColor, double fontSize)
    {
        ForegroundColor = Color.FromRgb(foregroundColor);
        FontSize = (float)fontSize;
    }

    public string? FontName { get; set; }

    /// <summary>On-page start X (points) of the text this state belongs to.
    /// Populated for states surfaced through <see cref="TextSegment.PhysicalSegment"/>.</summary>
    public float TextXIndent { get; internal set; }

    /// <summary>Text height in points: (Ascent + |Descent|) · FontSize / 1000 from
    /// the font's descriptor metrics. Falls back to the bare font size when no
    /// descriptor metrics are available.</summary>
    public float TextHeight
    {
        get
        {
            var m = Font?.GetMetrics();
            if (m is not null && m.Ascent > 0 && m.Descent != 0)
                return (float)((m.Ascent + Math.Abs(m.Descent)) * FontSize / 1000.0);
            return FontSize;
        }
    }

    private double _fontSize = 10;

    public float FontSize
    {
        get => (float)_fontSize;
        set
        {
            // A non-finite size is rejected outright. Zero and negative stay
            // legal: real documents carry Tf 0 (hidden OCR text) and negative
            // sizes (vertically mirrored text), and the absorbers surface
            // those parsed values through this same setter.
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentException("Incorrect font size value");
            if (Math.Abs(_fontSize - value) < 0.0001) return;
            var oldSize = _fontSize;
            _fontSize = value;
            // If this state belongs to a fragment from a page, update the content stream
            ApplyFontSizeChange(oldSize, value);
        }
    }

    /// <summary>Raw font size from the Tf operator (before text matrix scaling).</summary>
    internal float RawFontSize { get; set; }

    /// <summary>Text matrix D component (vertical scale) for height computation.</summary>
    internal double TmD { get; set; } = 1.0;

    /// <summary>Owner segment — needed to walk back to the source page for content stream updates.</summary>
    internal TextSegment? OwnerSegment { get; set; }

    /// <summary>Owner fragment — for fragment-level TextState, allows registration for save-time effects.</summary>
    internal TextFragment? OwnerFragment { get; set; }

    private void ApplyFontSizeChange(double oldSize, double newSize)
    {
        // Segment-level state reaches its page via the owning fragment; a
        // fragment-level state (TextFragmentState) only has OwnerFragment —
        // fall back to it so absorbed fragments write the new size through
        // to the page content stream too.
        var page = OwnerSegment?.Owner?.SourcePage ?? OwnerFragment?.SourcePage;
        if (page is null) return;
        var text = OwnerSegment?.Text ?? OwnerFragment?.Text;
        if (string.IsNullOrEmpty(text)) return;
        var modifier = new TextStateModifier();
        // Segment-level resize keeps the historical semantics (patch the
        // covering Tf even when it also governs neighbouring shows — a
        // sub-run resize resizes its whole run). Fragment-level resize is
        // collateral-free: it only rewrites when the covering Tf runs are
        // wholly inside the fragment's text, else it leaves the stream alone.
        modifier.ModifyFontSize(page, text, oldSize, newSize,
            allowCollateral: OwnerSegment is not null);
        // Keep the fragment's segment states in sync without re-triggering
        // a second content-stream rewrite per segment.
        if (OwnerSegment is null && OwnerFragment is not null)
            foreach (var seg in OwnerFragment.Segments)
                seg.TextState.SetFontSizeQuiet(newSize);
    }

    /// <summary>Set the stored font size without the content-stream
    /// write-back side effect (used to sync segment states after a
    /// fragment-level change already rewrote the stream).</summary>
    internal void SetFontSizeQuiet(double value) => _fontSize = value;

    private Color? _foregroundColor;
    public Color? ForegroundColor
    {
        get => _foregroundColor;
        set
        {
            _foregroundColor = value;
            if (value is null) return;
            // Mirror the FontSize/BackgroundColor side-effects: when this
            // TextState belongs to a segment from a page, propagate the new
            // fill colour to the content stream by injecting an `R G B rg`
            // before the segment's Tj/TJ operator. Pass the segment's Y so
            // the same text on multiple lines doesn't all get coloured by
            // a single setter call.
            var page = OwnerSegment?.Owner?.SourcePage;
            var text = OwnerSegment?.Text;
            if (page is null || string.IsNullOrEmpty(text)) return;
            var modifier = new TextStateModifier();
            modifier.ModifyForegroundColor(page, text, value,
                OwnerSegment?.Position?.YIndent ?? OwnerSegment?.Owner?.PositionOrNull?.YIndent);
        }
    }

    /// <summary>Assigns the captured foreground color from absorber graphics-state
    /// tracking without triggering content-stream injection. Used by
    /// TextFragmentAbsorber when reading existing text colour during extraction.</summary>
    internal void SetCapturedForegroundColor(Color? color) => _foregroundColor = color;

    /// <summary>Stroking (outline) color of the text. Used together with
    /// a non-zero <see cref="RenderingMode"/> (1 = stroke, 2 = fill+stroke).</summary>
    public Color? StrokingColor { get; set; }

    /// <summary>Whether text positioning treats Y as the baseline or the descender.
    /// Default <see cref="CoordinateOrigin.Descender"/> matches Aspose.PDF behaviour.</summary>
    public CoordinateOrigin CoordinateOrigin { get; set; } = CoordinateOrigin.Descender;

    private Color? _backgroundColor;
    public Color? BackgroundColor
    {
        get => _backgroundColor;
        set
        {
            _backgroundColor = value;
            // When BackgroundColor is set on a segment obtained via TextFragmentAbsorber,
            // register the owning fragment for rectangle injection during save.
            if (value is not null)
            {
                // Segment-level: register via segment's owner fragment
                if (OwnerSegment?.Owner?.SourcePage is not null)
                    OwnerSegment.Owner.SourcePage.RegisterBgColorFragment(OwnerSegment.Owner);
                // Fragment-level TextState: register the fragment directly
                else if (OwnerFragment?.SourcePage is not null)
                    OwnerFragment.SourcePage.RegisterBgColorFragment(OwnerFragment);
            }
        }
    }

    /// <summary>Assigns the captured background color from absorber graphics-state
    /// tracking without triggering rect-injection registration. Used by
    /// TextFragmentAbsorber when SearchForTextRelatedGraphics is enabled.</summary>
    internal void SetCapturedBackgroundColor(Color? color) => _backgroundColor = color;

    /// <summary>Whether the font is bold.</summary>
    public bool IsBold { get; set; }

    /// <summary>Whether the font is italic.</summary>
    public bool IsItalic { get; set; }

    /// <summary>Font style flags (Bold, Italic, etc.).</summary>
    public FontStyles FontStyle
    {
        get
        {
            var s = FontStyles.Regular;
            if (IsBold) s |= FontStyles.Bold;
            if (IsItalic) s |= FontStyles.Italic;
            return s;
        }
        set
        {
            IsBold = (value & FontStyles.Bold) != 0;
            IsItalic = (value & FontStyles.Italic) != 0;
        }
    }

    /// <summary>Whether the text is underlined (alias for <see cref="IsUnderline"/>).</summary>
    public bool Underline
    {
        get => _isUnderline;
        set
        {
            _isUnderline = value;
            // Register the owning fragment for underline-rect injection during save.
            // Try segment ownership first (segment-level TextState), then fragment ownership.
            var frag = OwnerSegment?.Owner ?? OwnerFragment;
            if (value)
            {
                frag?.SourcePage?.RegisterUnderlineFragment(frag);
            }
            // Turning underline off on a fragment whose source underline was captured
            // (ToAttemptGetUnderlineFromSource): register it so the source rectangle is
            // spliced out of the content stream at save time.
            else if (frag?.CapturedUnderlineSources is { Count: > 0 })
            {
                frag.SourcePage?.RegisterUnderlineRemoval(frag);
            }
        }
    }

    private bool _isUnderline;

    /// <summary>Whether the text is underlined.</summary>
    public bool IsUnderline
    {
        get => _isUnderline;
        set => Underline = value; // delegate to the registering setter
    }

    /// <summary>Assigns the captured underline state from absorber graphics-state
    /// tracking without triggering save-time rect-injection. Used by
    /// TextFragmentAbsorber when SearchForTextRelatedGraphics is enabled.</summary>
    internal void SetCapturedUnderline(bool value) => _isUnderline = value;

    /// <summary>Assigns the captured strikeout state from absorber graphics-state
    /// tracking without triggering the save-time strikeout-fragment registration
    /// that the public <see cref="IsStrikeOut"/> setter performs.</summary>
    internal void SetCapturedStrikeOut(bool value) => _isStrikeOut = value;

    private bool _isStrikeOut;

    /// <summary>Whether the text has strikethrough.</summary>
    public bool IsStrikeOut
    {
        get => _isStrikeOut;
        set
        {
            _isStrikeOut = value;
            if (value)
            {
                var frag = OwnerSegment?.Owner ?? OwnerFragment;
                frag?.SourcePage?.RegisterStrikeOutFragment(frag);
            }
        }
    }

    /// <summary>Alias for <see cref="IsStrikeOut"/>.</summary>
    public bool StrikeOut
    {
        get => IsStrikeOut;
        set => IsStrikeOut = value;
    }

    /// <summary>Whether the text is superscript.</summary>
    public bool IsSuperscript { get; set; }

    /// <summary>Alias for <see cref="IsSuperscript"/>.</summary>
    public bool Superscript
    {
        get => IsSuperscript;
        set => IsSuperscript = value;
    }

    /// <summary>Whether the text is subscript.</summary>
    public bool IsSubscript { get; set; }

    /// <summary>Alias for <see cref="IsSubscript"/>.</summary>
    public bool Subscript
    {
        get => IsSubscript;
        set => IsSubscript = value;
    }

    /// <summary>Character spacing in text space units.</summary>
    public float CharacterSpacing { get; set; }

    /// <summary>Word spacing in text space units.</summary>
    public float WordSpacing { get; set; }

    /// <summary>Horizontal scaling percentage (default 100).</summary>
    public float HorizontalScaling { get; set; } = 100;

    /// <summary>Line spacing (leading) in text space units.</summary>
    public float LineSpacing { get; set; }

    /// <summary>String token inserted into the rendered text in place of a
    /// tab character. Returns "\t" — the default tab-character placeholder.</summary>
    public string TabTag => "\t";

    /// <summary>Horizontal alignment of the text.</summary>
    public HorizontalAlignment HorizontalAlignment { get; set; } = HorizontalAlignment.Left;

    /// <summary>Text rendering mode (Tr operator). Controls fill / stroke /
    /// clipping behaviour of glyph rendering.</summary>
    public TextRenderingMode RenderingMode { get; set; }

    /// <summary>
    /// Whether this text fragment is invisible: rendering mode 3, or text that a
    /// LATER opaque filled rectangle fully covers (hidden-by-occlusion, the way
    /// Aspose.Pdf reports redaction-style covered text while its
    /// RenderingMode stays FillText).
    /// Setting to true sets RenderingMode=Invisible; setting to false sets RenderingMode=FillText.
    /// </summary>
    public bool Invisible
    {
        get => RenderingMode == TextRenderingMode.Invisible || _occluded;
        set => RenderingMode = value ? TextRenderingMode.Invisible : TextRenderingMode.FillText;
    }

    private bool _occluded;

    /// <summary>Absorber-side capture: the run is fully covered by a later opaque
    /// fill rect (drawn over it), so it reads as invisible despite FillText mode.</summary>
    internal void SetCapturedOccluded(bool value) => _occluded = value;

    /// <summary>Text rise (superscript/subscript offset).</summary>
    public double TextRise { get; set; }

    /// <summary>Text rotation angle in degrees.</summary>
    public double Rotation { get; set; }

    /// <summary>
    /// The font used for this text. May be null if font info was not resolved.
    /// Set by TextAbsorber/TextFragmentAbsorber during extraction.
    /// </summary>
    private Font? _font = FontInfo.DefaultHelvetica;
    public Font? Font
    {
        get => _font;
        set
        {
            _font = value;
            if (value is null) return;
            // Mirror the assigned font's name into FontName so downstream code that
            // keys on FontName (TextParagraph.RenderAbsolute → ensureFont(fontName))
            // sees the requested font instead of falling back to Helvetica.
            FontName = value.FontName;

            // Reassigning an absorbed fragment's font to a real, embeddable font
            // (one that carries a font program — e.g. FontRepository.FindFont(...))
            // embeds it by default and rewrites the page content so the run is
            // shown with it. Fonts read back from a PDF dictionary during
            // absorption carry no SourceFontData, so they no-op here.
            // SetEmbeddedDefault (not the IsEmbedded setter) respects an explicit
            // caller IsEmbedded=false — the save/layout pipeline re-assigns the font
            // into fresh text states, which would otherwise re-embed it and clobber
            // the caller's choice (font embedded incorrectly became true after save).
            // Standard-14 fonts are referenced by name and are never embedded/subset;
            // any other real font (one carrying a program) embeds and subsets by
            // default. IsCoreName matches only the genuine Core-14 names, so an
            // aliased TrueType such as "Courier New" still embeds.
            var isCore = Standard14Fonts.IsCoreName(value.BaseFont)
                || Standard14Fonts.IsCoreName(value.FontName);
            if (isCore)
            {
                value.SetEmbeddedDefault(false);
                value.SetSubsetDefault(false);
            }
            else
            {
                if (value.SourceFontData is null) return;
                value.SetEmbeddedDefault(true);
                value.SetSubsetDefault(true);
            }
            // OwnerSegment is wired for segment-level state; a fragment-level
            // TextFragmentState wires OwnerFragment instead.
            var page = OwnerSegment?.Owner?.SourcePage ?? OwnerFragment?.SourcePage;
            var text = OwnerSegment?.Text ?? OwnerFragment?.Text;
            if (page is null || string.IsNullOrEmpty(text)) return;
            try
            {
                new TextStateModifier().ModifyFont(page, text!, value,
                    OwnerSegment?.Position?.YIndent ?? OwnerFragment?.PositionOrNull?.YIndent);
            }
            catch { /* best-effort: leave content unchanged if the rewrite fails */ }

            // When the fragment's absorber requested RemoveUnusedFonts, flag the page so
            // the save pipeline drops /Font resources the replacement left unreferenced.
            var frag = OwnerSegment?.Owner ?? OwnerFragment;
            if (frag?.TextEditOptions?.FontReplaceBehavior
                == TextEditOptions.FontReplace.RemoveUnusedFonts)
                page.PruneUnusedFontsOnSave = true;
        }
    }

    /// <summary>
    /// Report a substituted font on this state WITHOUT the embedding/content-rewrite
    /// side effects of the <see cref="Font"/> setter. The glyphs were already switched
    /// in the content stream by the byte-level replacer; this only updates what the
    /// fragment reports (e.g. after a default no-character font fallback).
    /// </summary>
    internal void SetReportedFont(string family)
    {
        Font? f = null;
        try { f = FontRepository.FindFont(family); } catch { /* not installed */ }
        if (f is not null) _font = f;
        FontName = f?.FontName ?? family;
    }

    /// <summary>Rough text-width estimate at the current font/size. Uses the
    /// configured <see cref="Font"/>'s glyph widths when available; falls
    /// back to a half-em approximation per character.</summary>
    public double MeasureString(string str)
    {
        if (string.IsNullOrEmpty(str)) return 0;

        // Arabic is cursive: the simple-font metric path measures each base codepoint as a
        // missing glyph (it isn't in a Latin font's WinAnsi range). Shape the run to its
        // contextual presentation forms and measure those against an Arabic-capable face so
        // the width reflects the joined glyphs actually drawn.
        if (ArabicShaper.ContainsArabic(str))
        {
            var arabic = ArabicMeasurer.Measure(str, FontSize);
            if (arabic > 0) return arabic;
        }

        var font = Font;
        if (font is not null)
        {
            try { return font.MeasureString(str, FontSize); }
            catch { /* fall through to estimate */ }
        }
        return str.Length * FontSize * 0.5;
    }

    /// <summary>Height of <paramref name="character"/> at the current font / size, in
    /// points — the glyph's own bounding-box height (yMax − yMin) mapped to text space as
    /// <c>height × FontSize / 1000</c>. Returns 0 when the font carries no glyph for the
    /// character (e.g. a subset that never used it) or its outline is unavailable.</summary>
    public double MeasureHeight(char character)
    {
        if (Font is { } font)
        {
            var units = font.GlyphHeightUnits(character);
            if (units > 0) return units * FontSize / 1000.0;
        }
        return 0;
    }

    /// <summary>
    /// External font data for embedding (set via FontRepository.OpenFont).
    /// When set, TextBuilder will embed this font in the PDF instead of using Standard 14.
    /// </summary>
    public FontData? FontData { get; set; }

    /// <summary>Text formatting options (WordWrapMode, LineSpacingMode, etc.).
    /// Auto-initialized so callers can set
    /// <c>state.FormattingOptions.WrapMode = ...</c> on a fresh instance.</summary>
    public TextFormattingOptions FormattingOptions { get; set; } = new TextFormattingOptions();

    /// <summary>
    /// Copy every public formatting property from <paramref name="other"/> into
    /// this state (leaving owner linkage intact). Matches the Aspose.PDF for
    /// .NET <c>TextState.ApplyChangesFrom</c> API.
    /// </summary>
    public void ApplyChangesFrom(TextState textState)
    {
        var other = textState;
        if (other is null) return;
        FontName = other.FontName;
        FontSize = other.FontSize;
        ForegroundColor = other.ForegroundColor;
        BackgroundColor = other.BackgroundColor;
        IsBold = other.IsBold;
        IsItalic = other.IsItalic;
        Underline = other.Underline;
        IsStrikeOut = other.IsStrikeOut;
        IsSuperscript = other.IsSuperscript;
        IsSubscript = other.IsSubscript;
        CharacterSpacing = other.CharacterSpacing;
        WordSpacing = other.WordSpacing;
        HorizontalScaling = other.HorizontalScaling;
        LineSpacing = other.LineSpacing;
        HorizontalAlignment = other.HorizontalAlignment;
        RenderingMode = other.RenderingMode;
        _occluded = other._occluded; // hidden-by-occlusion capture (field: no setter side effects)
        StrokingColor = other.StrokingColor;
        TextRise = other.TextRise;
        Rotation = other.Rotation;
        if (other.Font is not null) Font = other.Font;
        if (other.FontData is not null) FontData = other.FontData;
        if (other.FormattingOptions is not null) FormattingOptions = other.FormattingOptions;
    }
}

/// <summary>
/// A 1-indexed collection of <see cref="TextFragment"/> objects, matching the public API.
/// </summary>
public sealed class TextFragmentCollection : System.Collections.Generic.IEnumerable<TextFragment>
{
    private readonly System.Collections.Generic.List<TextFragment> _list = new();

    /// <summary>Number of fragments.</summary>
    public int Count => _list.Count;

    /// <summary>1-based indexer (index 1 returns the first fragment).</summary>
    public TextFragment this[int index]
    {
        get
        {
            if (index < 1 || index > _list.Count)
                throw new IndexOutOfRangeException($"Index {index} out of range [1, {_list.Count}].");
            return _list[index - 1];
        }
    }

    /// <summary>Backing list — internal so the absorber can reorder a
    /// just-added range into reading order.</summary>
    internal System.Collections.Generic.List<TextFragment> Inner => _list;

    public bool IsReadOnly => false;
    public bool IsSynchronized => false;
    public object SyncRoot { get; } = new();

    public void Add(TextFragment fragment)
    {
        if (fragment is null) throw new ArgumentNullException(nameof(fragment));
        _list.Add(fragment);
    }

    /// <summary>Append every fragment from <paramref name="fragments"/> to this collection.</summary>
    public void AddRange(System.Collections.Generic.IEnumerable<TextFragment> fragments)
    {
        if (fragments is null) throw new ArgumentNullException(nameof(fragments));
        foreach (var fragment in fragments) Add(fragment);
    }

    public bool Contains(TextFragment item) => _list.Contains(item);

    public void CopyTo(TextFragment[] array, int index) => _list.CopyTo(array, index);

    public bool Remove(TextFragment item)
    {
        if (item is null) return false;
        bool removed = _list.Remove(item);
        // Deleting a fragment from an absorber's result collection deletes the
        // corresponding text from the page content stream, so the next save no
        // longer emits these glyphs.
        if (removed)
            item.DeleteFromContent();
        return removed;
    }

    /// <summary>Clear all fragments from the collection.</summary>
    public void Clear() => _list.Clear();

    /// <summary>Remove the element at the given 0-based internal index.</summary>
    internal void RemoveAt(int zeroBasedIndex) => _list.RemoveAt(zeroBasedIndex);

    /// <summary>0-based internal access for use within the library.</summary>
    internal TextFragment GetInternal(int zeroBasedIndex) => _list[zeroBasedIndex];

    public System.Collections.Generic.IEnumerator<TextFragment> GetEnumerator() => _list.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _list.GetEnumerator();
}

// Note class moved to top-level Aspose.Pdf namespace (src/Note.cs)
// to match the Aspose.Pdf reflection signature.

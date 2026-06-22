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

    /// <summary>
    /// The bounding rectangle of this segment on the page.
    /// Falls back to the owning fragment's rectangle when segment-level bounds aren't available.
    /// </summary>
    public Rectangle? Rectangle { get; internal set; }

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
    /// Standalone single-arg ctor (Aspose.PDF for .NET reflection signature).</summary>
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
            if (string.Equals(_text, value, StringComparison.Ordinal))
                return;

            // NoCharacterAction.ThrowException (opt-in): reject replacement text the
            // fragment's font can't represent, before mutating anything.
            ThrowIfFontLacksGlyph(value);

            var oldText = _text;
            _text = value;

            if (SourcePage is not null)
            {
                var replacer = new TextReplacer();
                // Scope to this fragment's page-space Y so iterating
                // fragments[i].Text in a loop replaces only the operator that
                // produced this fragment, not every matching occurrence on the
                // page. Without this, the replacement string accumulates across
                // iterations: setting fragment 1 first replaces all occurrences,
                // then fragment 2's setter re-matches inside the just-replaced
                // text and appends another copy of the replacement, etc.
                if (Position is { } pos)
                    replacer.TargetY = pos.YIndent;
                replacer.Replace(SourcePage, oldText, value);

                // Fallback: if simple replace found nothing and fragment has multiple segments,
                // try cross-operator replacement (handles text split across TJ/Tj operators)
                if (replacer.ReplacementCount == 0 && _segments.Count > 1)
                {
                    var crossReplacer = new TextReplacer();
                    if (Position is { } pos2)
                        crossReplacer.TargetY = pos2.YIndent;
                    crossReplacer.ReplaceWithCrossOperator(SourcePage, oldText, value);
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
            seg.Position = Position;
            // Wire OwnerSegment so subsequent TextState.ForegroundColor /
            // BackgroundColor / FontSize setters propagate to the page's
            // content stream (TextStateModifier looks up via OwnerSegment.Owner.SourcePage).
            seg.TextState.OwnerSegment = seg;
            _segments.Add(seg);

            // Under TextReplaceOptions.ReplaceAdjustment.None, the visible width of
            // the fragment shrinks to the replacement's natural advance — surrounding
            // glyphs do NOT reflow (the TJ-array compensation kerns preserve their
            // positions), so the fragment's rectangle must reflect the new width.
            if (_rectangle is not null
                && ReplaceOptions?.ReplaceAdjustmentAction == TextReplaceOptions.ReplaceAdjustment.None
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
        if (Position is { } pos)
            replacer.TargetY = pos.YIndent;
        replacer.Replace(SourcePage, _text, string.Empty);

        // Fall back to substring replacement for fragments whose text spans only
        // part of an operator (or multiple operators) and so was not removed by
        // the exact whole-operator pass.
        if (replacer.ReplacementCount == 0)
        {
            var fallback = new TextReplacer();
            if (Position is { } pos2)
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
        if (Position is { } pos)
            replacer.TargetY = pos.YIndent;
        replacer.Replace(SourcePage, _text, string.Empty);

        if (replacer.ReplacementCount == 0)
        {
            var fallback = new TextReplacer { PreserveAdvanceOnDelete = true };
            if (Position is { } pos2)
                fallback.TargetY = pos2.YIndent;
            fallback.Replace(SourcePage, _text, string.Empty);
        }

        _text = string.Empty;
        _segments.Clear();
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
            // Used before the fragment is placed on a page.
            if (Text != null && Text.Length > 0)
            {
                double fontSize = TextState.FontSize;
                double width;
                var font = TextState.Font;
                if (font != null)
                {
                    try { width = font.MeasureString(Text, fontSize); }
                    catch { width = Text.Length * fontSize * 0.5; }
                }
                else
                    width = Text.Length * fontSize * 0.5;

                double height = fontSize * 1.2; // approximate line height
                double x = _position?.XIndent ?? 0;
                double y = _position?.YIndent ?? 0;
                return new Rectangle(x, y - height * 0.2, x + width, y + height * 0.8);
            }
            return null;
        }
        internal set => _rectangle = value;
    }
    private Rectangle? _rectangle;

    /// <summary>The text position.</summary>
    public Position? Position
    {
        get => _position;
        set
        {
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

    /// <summary>
    /// The text baseline position. Position includes descent offset (bottom of text rect);
    /// BaselinePosition is the actual text baseline (higher by |descent|).
    /// </summary>
    public Position? BaselinePosition
    {
        get
        {
            if (_position is null) return null;
            // Compute descent from font metrics
            double descent = 0;
            var font = TextState.Font;
            var metrics = font?.GetMetrics();
            var fs = TextState.FontSize;
            if (metrics is not null && metrics.Descent != 0)
                descent = metrics.Descent * fs / 1000.0; // negative value
            return new Position(_position.XIndent, _position.YIndent - descent);
        }
        set
        {
            if (value is null) { _position = null; return; }
            // Reverse: add descent to get Position from BaselinePosition
            double descent = 0;
            var font = TextState.Font;
            var metrics = font?.GetMetrics();
            var fs = TextState.FontSize;
            if (metrics is not null && metrics.Descent != 0)
                descent = metrics.Descent * fs / 1000.0;
            _position = new Position(value.XIndent, value.YIndent + descent);
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
    /// Aspose.PDF for .NET surface; the underlying hyperlink action is wired into
    /// the page's annotation stream at save time (stored only in this build).</summary>
    public new Hyperlink Hyperlink { set => _hyperlink = value; }
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
        XIndent = xIndent;
        YIndent = yIndent;
    }

    public double XIndent { get; set; }
    public double YIndent { get; set; }

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
    /// field to match the non-static field reflection signature of Aspose.PDF for .NET.</summary>
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

    /// <summary>Approximate text height in points; matches the Aspose.PDF for .NET
    /// glyph-bounding-box-derived height by returning the font size (typical
    /// ratio between FontSize and rendered text height is close enough for
    /// the layout math callers use this for).</summary>
    public float TextHeight => FontSize;

    private double _fontSize = 10;

    public float FontSize
    {
        get => (float)_fontSize;
        set
        {
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
        var page = OwnerSegment?.Owner?.SourcePage;
        if (page is null) return;
        var text = OwnerSegment?.Text;
        if (string.IsNullOrEmpty(text)) return;
        var modifier = new TextStateModifier();
        modifier.ModifyFontSize(page, text, oldSize, newSize);
    }

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
                OwnerSegment?.Position?.YIndent ?? OwnerSegment?.Owner?.Position?.YIndent);
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
            if (value)
            {
                var frag = OwnerSegment?.Owner ?? OwnerFragment;
                frag?.SourcePage?.RegisterUnderlineFragment(frag);
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
    /// Whether this text fragment is invisible (rendering mode 3).
    /// Setting to true sets RenderingMode=Invisible; setting to false sets RenderingMode=FillText.
    /// </summary>
    public bool Invisible
    {
        get => RenderingMode == TextRenderingMode.Invisible;
        set => RenderingMode = value ? TextRenderingMode.Invisible : TextRenderingMode.FillText;
    }

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
            if (value.SourceFontData is null) return;
            value.SetEmbeddedDefault(true);
            // OwnerSegment is wired for segment-level state; a fragment-level
            // TextFragmentState wires OwnerFragment instead.
            var page = OwnerSegment?.Owner?.SourcePage ?? OwnerFragment?.SourcePage;
            var text = OwnerSegment?.Text ?? OwnerFragment?.Text;
            if (page is null || string.IsNullOrEmpty(text)) return;
            try
            {
                new TextStateModifier().ModifyFont(page, text!, value,
                    OwnerSegment?.Position?.YIndent ?? OwnerFragment?.Position?.YIndent);
            }
            catch { /* best-effort: leave content unchanged if the rewrite fails */ }
        }
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

    /// <summary>Rough character-height estimate at the current font/size.
    /// Returns ~1.0 em (≈ 1.0 × FontSize) — the FOSS extractor doesn't
    /// inspect per-glyph bbox metrics here.</summary>
    public double MeasureHeight(char character)
    {
        _ = character;
        return FontSize;
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
// to match the Aspose.PDF for .NET reflection signature.

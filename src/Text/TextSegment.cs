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
            var old = _text;
            _text = value;
            Owner?.RefreshTextFromSegments();
            PropagateTextToPage(old, value);
        }
    }

    /// <summary>Rewrite THIS segment's show operator on the owning fragment's source
    /// page when the caller assigns new segment text — scoped to the segment's own
    /// position exactly like the fragment-level setter, so sibling occurrences stay
    /// untouched. An Arabic replacement is shaped to its contextual presentation
    /// forms and written in visual order: the font-switch embeds
    /// the forms with a /ToUnicode targeting U+FExx, and extraction reads them back
    /// reversed into logical order, "exactly as seen".</summary>
    private void PropagateTextToPage(string oldText, string newText)
    {
        var page = Owner?.SourcePage;
        if (page is null || string.IsNullOrEmpty(oldText) || oldText == newText) return;
        var emit = ArabicShaper.ContainsArabic(newText)
            ? ArabicShaper.ShapeForDisplay(newText)
            : newText;
        var segY = (BaselinePosition ?? Position)?.YIndent;
        var segX = Position?.XIndent;
        // Whole-op scoped first (an absorbed segment maps to a concrete run), then
        // scoped substring (skipped for whitespace-only text, which would eat the
        // spaces of every neighbouring operator), then Y-only — never page-wide.
        var sweeps = oldText.Trim().Length > 0
            ? new (double? x, bool wholeOp)[] { (segX, true), (segX, false), (null, false) }
            : new (double? x, bool wholeOp)[] { (segX, true), (null, true) };
        foreach (var (mx, mo) in sweeps)
        {
            var replacer = new TextReplacer { TargetY = segY, TargetX = mx, MatchWholeOperator = mo };
            replacer.Replace(page, oldText, emit);
            if (replacer.ReplacementCount > 0) break;
            if (segY is null) break; // no geometry at all: single unscoped try
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

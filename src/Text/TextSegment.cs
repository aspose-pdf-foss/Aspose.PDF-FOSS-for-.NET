namespace Aspose.Pdf.Text;

/// <summary>
/// A single segment within a <see cref="TextFragment"/>.
/// A fragment may be composed of one or more segments that share the same text state.
/// </summary>
public sealed class TextSegment
{
    private string _text;

    /// <summary>Creates a segment with empty text and a default text state.</summary>
    public TextSegment() : this(string.Empty) { }

    /// <summary>Creates a segment with the given text and a default text state.</summary>
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

    /// <summary>Set the segment text without the page write-back (the caller is
    /// already writing the change to the page some other way).</summary>
    internal void SetTextQuiet(string text) => _text = text;

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
        if (Owner?.AttachedSegment is not null) return; // rewritten from state at save
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
    public Position? Position
    {
        get => _position;
        set
        {
            _position = value;
            // Assigning a position re-seats the run: the seat lift is captured again
            // from the state the segment carries NOW, and stays frozen through later
            // font or size changes (which only patch the run's face).
            if (Owner is { AttachedSegment: not null } owner)
                SeatLift = TextBuilder.SeatLiftFor(owner, this);
        }
    }
    private Position? _position;

    /// <summary>The descriptor descent the writer added to <see cref="Position"/> when
    /// this segment was last seated, or null while it has never been seated. Frozen
    /// until the segment is seated again — see <c>TextBuilder.SeatLiftFor</c>.</summary>
    internal double? SeatLift { get; set; }

    public Position? BaselinePosition { get; set; }

    /// <summary>The box of this segment's glyph-bearing content: its run's advance box
    /// less the whitespace-only array pieces at either end, null when the segment
    /// shows no glyph at all. Set by the absorber; the page content box reads it.</summary>
    internal Rectangle? InkRectangle { get; set; }

    /// <summary>The page-space y of the run's baseline when the run is upright, as the
    /// absorber extracted it; null for rotated or flipped text.</summary>
    internal double? BaselineY { get; set; }

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

    /// <summary>A picture standing in this segment's place on the line: it takes the
    /// picture's box (<see cref="Image.FixWidth"/> by <see cref="Image.FixHeight"/>)
    /// on the baseline and raises the line's ascent to its height when it is taller,
    /// and the segment's text is not drawn. Honoured by a paragraph whose segments
    /// flow as runs (<see cref="TextFormattingOptions.SegmentsFlowAsRuns"/>).</summary>
    public Image? InlineImage { get; set; }

    /// <summary>The segment is a tab: it draws nothing and carries the line on to the next of
    /// the paragraph's tab stops past where the line has got to
    /// (<see cref="TextFormattingOptions.RunTabStops"/>), else to the next multiple of
    /// <see cref="TextFormattingOptions.RunTabInterval"/>; its text is not drawn. Honoured by a
    /// paragraph whose segments flow as runs (<see cref="TextFormattingOptions.SegmentsFlowAsRuns"/>).</summary>
    public bool IsTab { get; set; }

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

/// <summary>Per-character layout information (glyph rectangle + page position): the box
/// spans the glyph's own advance, from the descent to the ascent.</summary>
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

/// <summary>The characters of a <see cref="TextSegment"/>: each one's position and glyph rectangle,
/// filled by the text fragment absorber for the segments it finds.</summary>
public sealed class CharInfoCollection : System.Collections.Generic.IEnumerable<CharInfo>
{
    private readonly System.Collections.Generic.List<CharInfo> _items = new();

    /// <summary>Creates an empty character collection.</summary>
    public CharInfoCollection() { }

    /// <summary>Gets the number of characters in the collection.</summary>
    public int Count => _items.Count;
    public bool IsReadOnly => false;
    public bool IsSynchronized => false;
    public object SyncRoot { get; } = new();

    /// <summary>1-based accessor for the character at the given position.</summary>
    public CharInfo this[int index] => _items[index - 1];

    /// <summary>Adds a character to the end of the collection; throws when <c>item</c> is <c>null</c>.</summary>
    public void Add(CharInfo item)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));
        _items.Add(item);
    }

    /// <summary>Removes all characters from the collection.</summary>
    public void Clear() => _items.Clear();
    /// <summary>Returns <c>true</c> when the collection contains the given character.</summary>
    public bool Contains(CharInfo item) => _items.Contains(item);
    /// <summary>Copies the characters into <c>array</c>, starting at the zero-based <c>index</c>.</summary>
    public void CopyTo(CharInfo[] array, int index) => _items.CopyTo(array, index);
    /// <summary>Removes the given character; returns <c>true</c> when it was found.</summary>
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

    /// <summary>Creates an empty segment collection that belongs to no fragment.</summary>
    public TextSegmentCollection() { }

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

    /// <summary>Adds a segment to the end of the collection and refreshes the owning fragment's text; throws when <c>segment</c> is <c>null</c>.</summary>
    public void Add(TextSegment segment)
    {
        if (segment is null) throw new ArgumentNullException(nameof(segment));
        segment.Owner = Owner;
        _segments.Add(segment);
        // Joining a fragment that is already on a page seats the segment now, with
        // the state it carries at this moment (see TextSegment.SeatLift).
        if (Owner is { AttachedSegment: not null } owner)
            segment.SeatLift = TextBuilder.SeatLiftFor(owner, segment);
        Owner?.RefreshTextFromSegments();
    }

    /// <summary>Returns <c>true</c> when the collection contains the given segment.</summary>
    public bool Contains(TextSegment item) => _segments.Contains(item);

    /// <summary>Copies the segments into <c>array</c>, starting at the zero-based <c>index</c>.</summary>
    public void CopyTo(TextSegment[] array, int index) => _segments.CopyTo(array, index);

    /// <summary>Removes the given segment and refreshes the owning fragment's text; returns <c>true</c> when it was found.</summary>
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

    /// <summary>Removes all segments and refreshes the owning fragment's text.</summary>
    public void Clear()
    {
        foreach (var seg in _segments) seg.Owner = null;
        _segments.Clear();
        Owner?.RefreshTextFromSegments();
    }

    /// <summary>Remove the segment at the 1-based <paramref name="index"/>.</summary>
    public void Delete(int index)
    {
        if (index < 1 || index > _segments.Count)
            throw new IndexOutOfRangeException($"Index {index} out of range [1, {_segments.Count}].");
        var seg = _segments[index - 1];
        _segments.RemoveAt(index - 1);
        seg.Owner = null;
        Owner?.RefreshTextFromSegments();
    }

    public System.Collections.Generic.IEnumerator<TextSegment> GetEnumerator() => _segments.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _segments.GetEnumerator();
}

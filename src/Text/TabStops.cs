namespace Aspose.Pdf.Text;

/// <summary>
/// Tab alignment type for tab stops.
/// </summary>
public enum TabAlignmentType
{
    Left,
    Center,
    Right,
    Decimal,
}

/// <summary>
/// Tab leader character type.
/// </summary>
public enum TabLeaderType
{
    None,
    Solid,
    Dash,
    Dot,
}

/// <summary>
/// Represents a single tab stop position.
/// </summary>
public sealed class TabStop
{
    /// <summary>Tab stop position in points from the left margin.</summary>
    public float Position { get; set; }

    /// <summary>Alignment type at this tab stop.</summary>
    public TabAlignmentType AlignmentType { get; set; } = TabAlignmentType.Left;

    /// <summary>Leader character type.</summary>
    public TabLeaderType LeaderType { get; set; } = TabLeaderType.None;

    /// <summary>The character a <see cref="TabAlignmentType.Decimal"/> stop aligns on: what follows
    /// the tab stands with the first such character at the stop, and ends at the stop when it has
    /// none. A full stop unless set. Honoured by segments that flow as runs.</summary>
    public char AnchorCharacter { get; set; } = '.';

    /// <summary>The stroke drawn across the room a tab takes to reach this stop: along the
    /// baseline, its middle half its line width above it, from where the tab starts to where
    /// the text after it does. Null draws none. Honoured by segments that flow as runs, where
    /// it stands in for <see cref="LeaderType"/>.</summary>
    public GraphInfo? LeaderRule { get; set; }

    /// <summary>Whether the stop is locked against further edits. Stored only.</summary>
    public bool IsReadOnly { get; internal set; }

    /// <summary>Creates a left-aligned tab stop at position 0 with no leader.</summary>
    public TabStop() { }

    /// <summary>Creates a left-aligned tab stop at the given position, in points, with no leader.</summary>
    public TabStop(float position) => Position = position;
}

/// <summary>
/// A collection of tab stop positions for text layout.
/// Tab characters (#$TAB) in text are aligned to these positions.
/// </summary>
public sealed class TabStops
{
    private readonly List<TabStop> _stops = new();

    /// <summary>Creates an empty tab stop collection.</summary>
    public TabStops() { }

    /// <summary>The tab stop entries.</summary>
    public IReadOnlyList<TabStop> Stops => _stops;

    /// <summary>Gets the number of tab stops.</summary>
    public int Count => _stops.Count;
    public bool IsReadOnly => false;

    /// <summary>Gets or sets the tab stop at the given zero-based index.</summary>
    public TabStop this[int index]
    {
        get => _stops[index];
        set => _stops[index] = value;
    }

    /// <summary>Add a tab stop at the given position (in points).</summary>
    public TabStop Add(double position) => Add((float)position);

    /// <summary>Add an unset tab stop (position 0, leader None).</summary>
    public TabStop Add()
    {
        var stop = new TabStop(0f);
        _stops.Add(stop);
        return stop;
    }

    /// <summary>Add a tab stop at the given position.</summary>
    public TabStop Add(float position)
    {
        var stop = new TabStop(position);
        _stops.Add(stop);
        return stop;
    }

    /// <summary>Add a tab stop with a leader-character style at the given position.</summary>
    public TabStop Add(float position, TabLeaderType leaderType)
    {
        var stop = new TabStop(position) { LeaderType = leaderType };
        _stops.Add(stop);
        return stop;
    }

    /// <summary>Append an existing tab stop.</summary>
    public void Add(TabStop tabStop)
    {
        if (tabStop is null) throw new ArgumentNullException(nameof(tabStop));
        _stops.Add(tabStop);
    }

    /// <summary>Shallow clone of the collection (stops are shared by reference).</summary>
    public object Clone()
    {
        var c = new TabStops();
        c._stops.AddRange(_stops);
        return c;
    }
}

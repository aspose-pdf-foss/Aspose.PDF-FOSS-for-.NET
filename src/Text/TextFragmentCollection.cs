namespace Aspose.Pdf.Text;

/// <summary>
/// A 1-indexed collection of <see cref="TextFragment"/> objects, matching the public API.
/// </summary>
public sealed class TextFragmentCollection : System.Collections.Generic.IEnumerable<TextFragment>
{
    private readonly System.Collections.Generic.List<TextFragment> _list = new();

    /// <summary>Creates an empty fragment collection.</summary>
    public TextFragmentCollection() { }

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

    /// <summary>Adds a fragment to the end of the collection; throws when <c>fragment</c> is <c>null</c>.</summary>
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

    /// <summary>Returns <c>true</c> when the collection contains the given fragment.</summary>
    public bool Contains(TextFragment item) => _list.Contains(item);

    /// <summary>Copies the fragments into <c>array</c>, starting at the zero-based <c>index</c>.</summary>
    public void CopyTo(TextFragment[] array, int index) => _list.CopyTo(array, index);

    /// <summary>Removes the given fragment and deletes its text from the page content it was absorbed from; returns <c>true</c> when it was found.</summary>
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

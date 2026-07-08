namespace Aspose.Pdf;

/// <summary>How a <see cref="BoundsCheckableList{T}"/> reacts when an item lies outside its container.</summary>
public enum BoundsCheckMode
{
    /// <summary>Items added without bounds enforcement.</summary>
    Default = 0,
    /// <summary>Throw when an item falls outside the container rectangle.</summary>
    ThrowExceptionIfDoesNotFit = 1,
}

/// <summary>A list of <typeparamref name="T"/> that optionally enforces container bounds on insertion.
/// The FOSS implementation records the mode + container dimensions but never raises — bounds are
/// checked at render time, not at add time. Mirrors the Aspose.Pdf public surface.</summary>
public class BoundsCheckableList<T> : System.Collections.Generic.IEnumerable<T>
{
    private readonly System.Collections.Generic.List<T> _items = new();
    private BoundsCheckMode _mode;
    private double _w;
    private double _h;

    public BoundsCheckableList() { }

    public BoundsCheckableList(BoundsCheckMode boundsCheckMode, double containerWidth, double containerHeight)
    {
        _mode = boundsCheckMode;
        _w = containerWidth;
        _h = containerHeight;
    }

    public int Count => _items.Count;
    public bool IsReadOnly => false;

    public T this[int index]
    {
        get => _items[index];
        set => _items[index] = value;
    }

    public void Add(T item) => _items.Add(item);
    public void Clear() => _items.Clear();
    public bool Contains(T item) => _items.Contains(item);
    public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    public int IndexOf(T item) => _items.IndexOf(item);
    public void Insert(int index, T item) => _items.Insert(index, item);
    public bool Remove(T item) => _items.Remove(item);
    public void RemoveAt(int index) => _items.RemoveAt(index);

    /// <summary>Switch the bounds-check mode without changing the container.</summary>
    public void UpdateBoundsCheckMode(BoundsCheckMode boundsCheckMode) { _mode = boundsCheckMode; }

    /// <summary>Switch the bounds-check mode and the container dimensions.</summary>
    public void UpdateBoundsCheckMode(BoundsCheckMode boundsCheckMode, double containerWidth, double containerHeight)
    {
        _mode = boundsCheckMode;
        _w = containerWidth;
        _h = containerHeight;
    }

    public System.Collections.Generic.IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

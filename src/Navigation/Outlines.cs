namespace Aspose.Pdf;

/// <summary>
/// Document-level bookmark (outline) collection. Returned by
/// <see cref="OutlineItemCollection.Parent"/> so callers can navigate from a
/// nested bookmark back to the top-level outline tree.
/// </summary>
public sealed class Outlines
{
    private readonly OutlineCollection _backing;

    internal Outlines(OutlineCollection backing) => _backing = backing;

    /// <summary>Number of top-level outline items.</summary>
    public int Count => _backing.Count;

    /// <summary>Whether the collection is read-only. Always false.</summary>
    public bool IsReadOnly => false;

    /// <summary>Number of bookmarks that are visible (counts /Count entries recursively).</summary>
    public int VisibleCount => _backing.Count;

    /// <summary>Append an outline item.</summary>
    public void Add(OutlineItemCollection item)
    {
        if (item is null) return;
        _backing.Add(item);
    }

    /// <summary>Remove every outline item.</summary>
    public void Clear()
    {
        // Backing collection lacks Clear; iterate top-level items and remove each.
        var snapshot = new List<OutlineItem>();
        foreach (var i in _backing) snapshot.Add(i);
        foreach (var i in snapshot) _backing.Remove(i);
    }

    /// <summary>Whether the supplied item is present at the top level.</summary>
    public bool Contains(OutlineItemCollection item)
    {
        if (item is null) return false;
        foreach (var i in _backing)
            if (ReferenceEquals(i, item)) return true;
        return false;
    }

    /// <summary>Copy the collection into an array starting at <paramref name="arrayIndex"/>.</summary>
    public void CopyTo(OutlineItemCollection[] array, int arrayIndex)
    {
        if (array is null) throw new ArgumentNullException(nameof(array));
        var i = arrayIndex;
        foreach (var item in _backing)
        {
            if (item is OutlineItemCollection oic) array[i++] = oic;
        }
    }

    /// <summary>Enumerator over the top-level outline items.</summary>
    public IEnumerator<OutlineItemCollection> GetEnumerator()
    {
        foreach (var item in _backing)
        {
            if (item is OutlineItemCollection oic) yield return oic;
        }
    }

    /// <summary>Remove the supplied item and report whether it was present.</summary>
    public bool Remove(OutlineItemCollection item)
    {
        if (item is null) return false;
        return _backing.Remove(item);
    }
}

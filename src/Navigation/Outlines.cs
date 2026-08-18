using System.Collections;
using System.Collections.Generic;

namespace Aspose.Pdf;

/// <summary>
/// Base class for a bookmark (outline) collection. <see cref="OutlineCollection"/>
/// derives from it, so a document's <see cref="Document.Outlines"/> and any
/// <see cref="OutlineItem.Parent"/> are both assignable to <c>Outlines</c>
/// (matching the public type hierarchy).
/// </summary>
public abstract class Outlines : IEnumerable<OutlineItemCollection>
{
    private protected Outlines() { }

    /// <summary>Number of top-level outline items.</summary>
    public abstract int Count { get; }

    /// <summary>Number of bookmarks that are visible (counts /Count entries recursively).</summary>
    public virtual int VisibleCount => Count;

    /// <summary>Whether the collection is read-only. Always false.</summary>
    public virtual bool IsReadOnly => false;

    /// <summary>Append an outline item.</summary>
    public abstract void Add(OutlineItemCollection item);

    /// <summary>Remove every outline item.</summary>
    public abstract void Clear();

    /// <summary>Whether the supplied item is present at the top level.</summary>
    public abstract bool Contains(OutlineItemCollection item);

    /// <summary>Copy the collection into an array starting at <paramref name="arrayIndex"/>.</summary>
    public abstract void CopyTo(OutlineItemCollection[] array, int arrayIndex);

    /// <summary>Remove the supplied item and report whether it was present.</summary>
    public abstract bool Remove(OutlineItemCollection item);

    /// <summary>Enumerator over the top-level outline items.</summary>
    public abstract IEnumerator<OutlineItemCollection> GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

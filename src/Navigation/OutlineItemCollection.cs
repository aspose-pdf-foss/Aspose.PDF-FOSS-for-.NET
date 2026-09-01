using System;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

/// <summary>
/// The document outline (bookmark tree).
/// </summary>
/// <summary>
/// Alias for <see cref="OutlineItem"/> that mirrors the the public API
/// public type name <c>OutlineItemCollection</c>. .NET uses this type both for
/// individual items and as the parent collection element type — this stub keeps
/// the public API source-compatible.
/// </summary>
/// <summary>
/// A single outline item that may have child items (bookmarks/TOC entries).
/// Implements IEnumerable to allow iterating over child outline items,
/// matching the public API.
/// </summary>
public class OutlineItemCollection : OutlineItem, System.Collections.Generic.IEnumerable<OutlineItemCollection>
{
    /// <summary>Construct an empty outline item.</summary>
    public OutlineItemCollection() : base() { }

    /// <summary>Construct an outline item bound to an existing outline collection
    /// (mirrors <c>new OutlineItemCollection(doc.Outlines)</c>).</summary>
    public OutlineItemCollection(OutlineCollection? outlines) : base()
    {
        _backingCollection = outlines;
    }

    private readonly OutlineCollection? _backingCollection;

    /// <summary>The top-level collection that owns this item, so <c>Delete()</c>
    /// can remove it. Set when <see cref="OutlineCollection.Items"/> materializes
    /// the item; null for a nested item, whose owner is <c>_ownerItem</c>.</summary>
    internal OutlineCollection? _ownerCollection;

    internal OutlineItemCollection(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>
    /// Iterates over child outline items. Each child is wrapped as OutlineItemCollection
    /// so recursive iteration (foreach inside foreach) works naturally.
    /// </summary>
    public System.Collections.Generic.IEnumerator<OutlineItemCollection> GetEnumerator()
    {
        // Snapshot the child list so callers can Remove/Add items mid-enumeration
        // (a common outline-pruning pattern) without triggering "Collection was
        // modified" — a tolerant enumerator.
        foreach (var child in new System.Collections.Generic.List<OutlineItem>(Children))
        {
            // Children are OutlineItemCollection instances already (created in OutlineItem.Children)
            if (child is OutlineItemCollection oic)
                yield return oic;
            else
                yield return new OutlineItemCollection(child.Dict, child.Reader!);
        }
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Number of child outline items.</summary>
    public int Count => Children.Count;

    /// <summary>The first child outline item, or null if none.</summary>
    public OutlineItemCollection? First
    {
        get
        {
            var kids = Children;
            if (kids.Count == 0) return null;
            return kids[0] as OutlineItemCollection;
        }
    }

    /// <summary>The last child outline item, or null if none.</summary>
    public OutlineItemCollection? Last
    {
        get
        {
            var kids = Children;
            if (kids.Count == 0) return null;
            return kids[^1] as OutlineItemCollection;
        }
    }

    /// <summary>The next sibling outline item, or null if this is the last sibling.</summary>
    public OutlineItemCollection? Next
    {
        get
        {
            if (Reader is null) return null;
            var nextDict = Reader.ResolveDict(Dict.Get("Next"));
            return nextDict is null ? null : new OutlineItemCollection(nextDict, Reader);
        }
    }

    /// <summary>The previous sibling outline item, or null if this is the first sibling.</summary>
    public OutlineItemCollection? Prev
    {
        get
        {
            if (Reader is null) return null;
            var prevDict = Reader.ResolveDict(Dict.Get("Prev"));
            return prevDict is null ? null : new OutlineItemCollection(prevDict, Reader);
        }
    }

    /// <summary>True when this item has a next sibling.</summary>
    public bool HasNext => Next is not null;

    // ── Inherited properties redeclared with `new` so the reflection dump
    //    surfaces them on this derived type. Values delegate to the base
    //    OutlineItem implementation.

    /// <summary>The bookmark title.</summary>
    public new string Title { get => base.Title; set => base.Title = value; }

    /// <summary>The action associated with this outline item, if any.</summary>
    public new Aspose.Pdf.Annotations.PdfAction? Action { get => base.Action; set => base.Action = value; }

    /// <summary>The destination of this outline item.</summary>
    public new Aspose.Pdf.Annotations.IAppointment? Destination { get => base.Destination; set => base.Destination = value; }

    /// <summary>Whether the bookmark is rendered in bold.</summary>
    public new bool Bold { get => base.Bold; set => base.Bold = value; }

    /// <summary>Whether the bookmark is rendered in italic.</summary>
    public new bool Italic { get => base.Italic; set => base.Italic = value; }

    /// <summary>Bookmark colour.</summary>
    public new System.Drawing.Color Color { get => base.Color; set => base.Color = value; }

    /// <summary>Whether the bookmark is expanded.</summary>
    public new bool Open { get => base.Open; set => base.Open = value; }

    // ── Public-API additions ─────────────────────────────────────

    /// <summary>Whether the collection is read-only. Always false.</summary>
    public bool IsReadOnly => false;

    /// <summary>Whether the collection is thread-safe. Always false.</summary>
    public bool IsSynchronized => false;

    /// <summary>Synchronization root for <see cref="IsSynchronized"/>; returns this collection.</summary>
    public object SyncRoot => this;

    /// <summary>Child outline item at the supplied 1-based index.</summary>
    public new OutlineItemCollection this[int index]
    {
        get
        {
            var kids = Children;
            if (index < 1 || index > kids.Count)
                throw new ArgumentOutOfRangeException(nameof(index), $"Index {index} is out of range. Valid range: 1 to {kids.Count}.");
            return (OutlineItemCollection)kids[index - 1];
        }
    }

    /// <summary>Depth of this bookmark in the outline tree (top-level = 0).</summary>
    public int Level
    {
        get
        {
            if (Reader is null) return 0;
            var depth = 0;
            var parentObj = Dict.Get("Parent");
            var visited = new HashSet<int>();
            while (parentObj is not null)
            {
                if (parentObj is Aspose.Pdf.Core.PdfIndirectRef iref && !visited.Add(iref.ObjectNumber)) break;
                var parentDict = Reader.ResolveDict(parentObj);
                if (parentDict is null) break;
                // Stop at the /Outlines dict itself (which has no /Parent and no /Title).
                if (parentDict.Get("Parent") is null) break;
                depth++;
                parentObj = parentDict.Get("Parent");
            }
            return depth;
        }
    }

    /// <summary>The owning outline tree, or null when this item is not yet attached.</summary>
    public Outlines? Parent => _backingCollection;

    /// <summary>Signed visible-descendant count following PDF /Count semantics:
    /// the number of items that appear when this node is open (immediate children
    /// plus the visible counts of open descendants), negated while the node is
    /// closed. 0 for a leaf.</summary>
    public int VisibleCount
    {
        get
        {
            var mag = VisibleMagnitude;
            if (mag == 0) return 0;
            return IsOpen ? mag : -mag;
        }
    }

    /// <summary>Add an outline item as a child of this one.</summary>
    public void Add(OutlineItemCollection outline)
    {
        if (outline is null) return;
        Add((OutlineItem)outline);
    }

    /// <summary>Remove every child outline item.</summary>
    public void Clear()
    {
        // The base Children list is initialised lazily; access it to force creation,
        // then clear via the (internal) underlying list using reflection-free access:
        // Children itself is IReadOnlyList, but we expose Insert/Add on the base — there's
        // no Remove. Walk and remove top-level entries through their parent dict instead.
        var kids = Children;
        for (var i = kids.Count - 1; i >= 0; i--)
        {
            if (kids[i] is OutlineItemCollection oic) Remove(oic);
        }
    }

    /// <summary>Whether the supplied item is a direct child of this one.</summary>
    public bool Contains(OutlineItemCollection item)
    {
        if (item is null) return false;
        foreach (var kid in Children)
            if (ReferenceEquals(kid, item)) return true;
        return false;
    }

    /// <summary>Copy children into <paramref name="array"/> starting at <paramref name="index"/>.</summary>
    public void CopyTo(OutlineItemCollection[] array, int index)
    {
        if (array is null) throw new ArgumentNullException(nameof(array));
        var kids = Children;
        for (var i = 0; i < kids.Count; i++)
        {
            if (kids[i] is OutlineItemCollection oic) array[index + i] = oic;
        }
    }

    /// <summary>Remove this outline item from its parent (a top-level
    /// <see cref="OutlineCollection"/> or an enclosing item), so that
    /// <c>outlines[i].Delete()</c> drops that bookmark.
    /// A detached item with no known owner falls back to clearing its children.</summary>
    public new void Delete()
    {
        if (_ownerCollection is not null) { _ownerCollection.Remove(this); return; }
        if (_ownerItem is not null) { _ownerItem.RemoveChild(this); return; }
        Clear();
    }

    /// <summary>Remove the first child whose title matches <paramref name="name"/>.</summary>
    public void Delete(string name)
    {
        var kids = Children;
        for (var i = 0; i < kids.Count; i++)
        {
            if (kids[i].Title == name && kids[i] is OutlineItemCollection oic)
            {
                Remove(oic);
                return;
            }
        }
    }

    /// <summary>Insert an outline item at the supplied 1-based index.</summary>
    public void Insert(int index, OutlineItemCollection outline)
    {
        if (outline is null) return;
        base.Insert(index, outline);
    }

    /// <summary>Remove the supplied child and report whether it was present.</summary>
    public bool Remove(OutlineItemCollection item)
    {
        if (item is null) return false;
        if (_children is null) _ = Children;
        var initial = _children!.Count;
        for (var i = _children.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_children[i], item)) _children.RemoveAt(i);
        }
        var removed = _children.Count < initial;
        if (removed) MarkTreeDirty();
        return removed;
    }

    /// <summary>Remove the child at the supplied 1-based index.</summary>
    public void Remove(int index)
    {
        if (_children is null) _ = Children;
        if (index < 1 || index > _children!.Count) return;
        _children.RemoveAt(index - 1);
        MarkTreeDirty();
    }

    /// <summary>A top-level item reports the change to its owning collection;
    /// nested items delegate up the parent chain.</summary>
    internal override void MarkTreeDirty()
    {
        if (_ownerCollection is not null) { _ownerCollection.MarkDirty(); return; }
        base.MarkTreeDirty();
    }
}

using System;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

/// <summary>
/// The top-level bookmarks (outline items) of a document, backed by its <c>/Outlines</c> tree. Obtained from
/// <c>Document.Outlines</c>; indexes are 1-based.
/// </summary>
public sealed partial class OutlineCollection : Outlines, System.Collections.Generic.IEnumerable<OutlineItem>
{
    private readonly PdfDictionary _dict;
    private readonly PdfReader _reader;
    private List<OutlineItem>? _items;
    private bool _dirty;
    // The /Outlines root the last finalize wrote, once one has: the catalog names it, not _dict.
    private PdfDictionary? _writtenRoot;

    /// <summary>Enumerator over the top-level outline items, typed as
    /// <see cref="OutlineItemCollection"/> to match the public
    /// reflection signature. The items stored are concrete
    /// OutlineItemCollection instances anyway.</summary>
    public override System.Collections.Generic.IEnumerator<OutlineItemCollection> GetEnumerator()
    {
        foreach (var item in Items)
            yield return (OutlineItemCollection)item;
    }

    System.Collections.Generic.IEnumerator<OutlineItem>
        System.Collections.Generic.IEnumerable<OutlineItem>.GetEnumerator() => Items.GetEnumerator();

    internal OutlineCollection(PdfDictionary dict, PdfReader reader)
    {
        _dict = dict;
        _reader = reader;
    }

    /// <summary>Create an outline collection bound to <paramref name="document"/>'s
    /// bookmark tree, creating an empty /Outlines dictionary if the document has none
    /// (mirrors <see cref="Document.Outlines"/>).</summary>
    public OutlineCollection(Document document)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        _reader = document.Reader;
        var dict = _reader.ResolveDict(_reader.Catalog.Get("Outlines"));
        if (dict is null)
        {
            dict = new PdfDictionary();
            dict.Set("Type", new PdfName("Outlines"));
            _reader.Catalog.Set("Outlines", dict);
        }
        _dict = dict;
    }

    /// <summary>Top-level outline items.</summary>
    public IReadOnlyList<OutlineItem> Items
    {
        get
        {
            if (_items is not null) return _items;
            _items = [];

            // A chain that loops back is read with each item once.
            var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
            var current = _reader.ResolveDict(_dict.Get("First"));
            while (current is not null && seen.Add(current))
            {
                _items.Add(new OutlineItemCollection(current, _reader) { _ownerCollection = this });
                current = _reader.ResolveDict(current.Get("Next"));
            }

            return _items;
        }
    }

    public override int Count => Items.Count;

    /// <summary>The first top-level outline item, or null if the collection is empty.</summary>
    public OutlineItemCollection? First
    {
        get
        {
            var items = Items;
            if (items.Count == 0) return null;
            return items[0] as OutlineItemCollection;
        }
    }

    /// <summary>The last top-level outline item, or null if the collection is empty.</summary>
    public OutlineItemCollection? Last
    {
        get
        {
            var items = Items;
            if (items.Count == 0) return null;
            return items[^1] as OutlineItemCollection;
        }
    }

    /// <summary>Gets the outline item at the specified 1-based index.</summary>
    public OutlineItemCollection this[int index]
    {
        get
        {
            var items = Items;
            if (index < 1 || index > items.Count)
#pragma warning disable S112 // public API: the exception type callers catch
                throw new IndexOutOfRangeException($"Index {index} is out of range. Valid range: 1 to {items.Count}.");
#pragma warning restore S112
            // Items stores OutlineItemCollection instances (see Items getter).
            return (OutlineItemCollection)items[index - 1];
        }
    }

    /// <summary>Removes all outline items from the collection.</summary>
    public void Delete()
    {
        _ = Items;
        _items!.Clear();
        _dirty = true;
    }

    /// <summary>Removes the outline items with the specified name (matched against
    /// <see cref="OutlineItem.Title"/>): the top-level ones, or else the first one found below them.</summary>
    public void Delete(string name)
    {
        _ = Items;
        if (_items!.RemoveAll(item => item.Title == name) == 0)
        {
            foreach (var item in _items)
            {
                if (item is OutlineItemCollection kids && kids.DeleteNamed(name)) break;
            }
        }
        _dirty = true;
    }

#pragma warning disable S4019 // public API: the published overload
    /// <summary>Adds an outline item to the collection.</summary>
    public void Add(OutlineItem item)
#pragma warning restore S4019
    {
        if (item is null) throw new ArgumentNullException(nameof(item));
        // An item added again moves to the end: listed twice, it would be saved as its own next sibling. An item
        // of another collection stays there too - callers copy bookmarks from a collection they are enumerating.
        _ = Items;
        _items!.RemoveAll(existing => ReferenceEquals(existing, item));
        _items.Add(item);
        if (item is OutlineItemCollection top) top._ownerCollection = this;
        _dirty = true;
    }

#pragma warning disable S927 // public API: the published parameter name
    /// <summary>Add an item typed as <see cref="OutlineItemCollection"/> (the
    /// public overload signature). Forwards to the OutlineItem-typed
    /// path.</summary>
    public override void Add(OutlineItemCollection outline)
#pragma warning restore S927
        => Add((OutlineItem)outline);

#pragma warning disable S4144 // public API: Clear is the collection-interface name of Delete
    /// <summary>Removes every item from the collection (counterpart to
    /// <see cref="Delete()"/>).</summary>
    public override void Clear()
#pragma warning restore S4144
    {
        _ = Items;
        _items!.Clear();
        _dirty = true;
    }

    /// <summary>Whether <paramref name="item"/> is currently in the collection.</summary>
    public override bool Contains(OutlineItemCollection item)
    {
        if (item is null) return false;
        _ = Items;
        return (_items!).Any(existing => ReferenceEquals(existing, item));
    }

#pragma warning disable S927 // public API: the published parameter name
    /// <summary>Copy items into <paramref name="array"/> starting at
    /// <paramref name="index"/>.</summary>
    public override void CopyTo(OutlineItemCollection[] array, int index)
#pragma warning restore S927
    {
        if (array is null) throw new ArgumentNullException(nameof(array));
        _ = Items;
        for (var i = 0; i < _items!.Count; i++)
            array[index + i] = (OutlineItemCollection)_items[i];
    }

#pragma warning disable S4019 // public API: the published overload
    /// <summary>Remove the supplied item by reference and report whether it was present.</summary>
    public bool Remove(OutlineItem item)
#pragma warning restore S4019
    {
        _ = Items;
        var removed = _items!.RemoveAll(i => ReferenceEquals(i, item)) > 0;
        if (removed) _dirty = true;
        return removed;
    }

    /// <summary>Remove the OutlineItemCollection-typed item by reference;
    /// reports whether it was present.</summary>
    public override bool Remove(OutlineItemCollection item)
    {
        if (item is null) return false;
        return Remove((OutlineItem)item);
    }

    /// <summary>Remove the item at the given 1-based index, matching the
    /// 1-based <see cref="this[int]"/> indexer. A stale 0-based call from
    /// pre-shift callers (Remove(0)) is a no-op rather than a throw.</summary>
    public void Remove(int index)
    {
        _ = Items;
        if (index < 1 || index > _items!.Count) return;
        _items.RemoveAt(index - 1);
        _dirty = true;
    }

    /// <summary>Always false: the collection is mutable.</summary>
    public override bool IsReadOnly => false;

#pragma warning disable S2325 // public API: the published instance member
    /// <summary>Always false: callers serialise their own access.</summary>
    public bool IsSynchronized => false;
#pragma warning restore S2325

    /// <summary>Sentinel object for ICollection.SyncRoot-style locking.</summary>
    public object SyncRoot { get; } = new();

    /// <summary>Total number of outline items currently visible (PDF /Count of
    /// the /Outlines root): every top-level item plus, for each open item, its
    /// visible-descendant magnitude. Computed live from the in-memory tree.</summary>
    public override int VisibleCount
    {
        get
        {
            int count = 0;
#pragma warning disable S3217 // the collection holds OutlineItemCollection items only
            foreach (OutlineItemCollection item in Items)
#pragma warning restore S3217
            {
                count++;
                if (item.Open)
                    count += item.VisibleMagnitude;
            }
            return count;
        }
    }

    /// <summary>Whether the outline collection was modified after loading.</summary>
    internal bool IsDirty => _dirty;

    /// <summary>Flag a structural change somewhere in the tree (child item
    /// added/removed) so Finalize re-serialises the outlines on save.</summary>
    internal void MarkDirty() => _dirty = true;

    /// <summary>
    /// Serialize the in-memory outline tree to new PDF objects and update the
    /// /Outlines dictionary in the catalog. Called from Document.ToArray().
    /// </summary>
    internal void Finalize(Document doc)
    {
        var ol = new OutlineFinalizeState();
        ol.doc = doc;
        if (!_dirty) return;
        if (_items is null || _items.Count == 0)
        {
            // All outlines were deleted — remove /Outlines from catalog
            // An earlier save of this session wrote the tree under a root of its own, and the
            // catalog names that one now: both are emptied.
            foreach (var root in new[] { _dict, _writtenRoot })
            {
                if (root is null) continue;
                root.Remove("First");
                root.Remove("Last");
                root.Set("Count", new PdfInteger(0));
            }
            _dirty = false;
            return;
        }

        ol.flatItems = new List<(OutlineItem item, int objNum)>();
        ol.baseObjNum = ol.doc.AllocateObjectNumber();
        ol.outlinesObjNum = ol.baseObjNum++;

        Flatten(ol, _items);

        ol.objMap = new Dictionary<OutlineItem, int>(ReferenceEqualityComparer.Instance);
        foreach (var (item, objNum) in ol.flatItems)
            ol.objMap[item] = objNum;

        // Write each outline item as a new object
        foreach (var (item, objNum) in ol.flatItems)
        {
            WriteOutlineItem(ol, item, objNum);
        }

        ol.rootCount = 0;
        foreach (var item in _items)
        {
            ol.rootCount++;
            if (item.IsOpen) ol.rootCount += item.VisibleMagnitude;
        }
        ol.outlinesDict = new PdfDictionary();
        ol.outlinesDict.Set("Type", new PdfName("Outlines"));
        ol.outlinesDict.Set("First", new PdfIndirectRef(ol.objMap[_items[0]], 0));
        ol.outlinesDict.Set("Last", new PdfIndirectRef(ol.objMap[_items[^1]], 0));
        ol.outlinesDict.Set("Count", new PdfInteger(ol.rootCount));

        ol.doc.AddNewObject(ol.outlinesObjNum, ol.outlinesDict);
        _writtenRoot = ol.outlinesDict;
        ol.doc.Reader.Catalog.Set("Outlines", new PdfIndirectRef(ol.outlinesObjNum, 0));

        _dirty = false;
    }

    private OutlineItem? FindParent(OutlineItem target)
    {
        OutlineItem? SearchIn(IReadOnlyList<OutlineItem> items)
        {
            foreach (var item in items)
            {
                if (item.Children.Any(child => ReferenceEquals(child, target))) return item;
                var found = SearchIn(item.Children);
                if (found is not null) return found;
            }
            return null;
        }
        return SearchIn(_items!);
    }

    private static int IndexOf(IReadOnlyList<OutlineItem> list, OutlineItem target)
    {
        for (int i = 0; i < list.Count; i++)
            if (ReferenceEquals(list[i], target)) return i;
        return -1;
    }

    private static void CopyEntryIfPresent(PdfDictionary src, PdfDictionary dst, string key,
        PdfReader? sourceReader, Document targetDoc)
    {
        var val = src.Get(key);
        if (val is null) return;

        // Deep-clone the value for cross-document import
        if (sourceReader is not null && sourceReader != targetDoc.Reader)
        {
            val = CloneForImport(val, sourceReader, targetDoc);
        }
        if (val is not null)
            dst.Set(key, val);
    }

    private static PdfObject? CloneForImport(PdfObject obj, PdfReader sourceReader, Document targetDoc)
    {
        var visited = new HashSet<int>();
        return CloneForImportCore(obj, sourceReader, targetDoc, visited, 0);
    }

    private static PdfObject? CloneForImportCore(PdfObject obj, PdfReader sourceReader,
        Document targetDoc, HashSet<int> visited, int depth)
    {
        if (depth > 20) return null; // Prevent deep recursion

        if (obj is PdfIndirectRef iref)
        {
            if (!visited.Add(iref.ObjectNumber)) return null; // Circular reference
            var resolved = sourceReader.Resolve(iref);
            if (resolved is null) return null;
            return CloneForImportCore(resolved, sourceReader, targetDoc, visited, depth + 1);
        }

        obj = sourceReader.Resolve(obj) ?? obj;
        switch (obj)
        {
            case PdfDictionary dict:
                var newDict = new PdfDictionary();
                foreach (var k in dict.Keys)
                {
                    // Skip /Parent links to avoid circular cloning
                    if (k == "Parent") continue;
                    var v = CloneForImportCore(dict.Get(k)!, sourceReader, targetDoc, visited, depth + 1);
                    if (v is not null) newDict.Set(k, v);
                }
                return newDict;
            case PdfArray arr:
                var newArr = new PdfArray();
                foreach (var item in arr)
                {
                    var v = CloneForImportCore(item, sourceReader, targetDoc, visited, depth + 1);
                    if (v is not null) newArr.Add(v);
                }
                return newArr;
            default:
                // Primitive types (PdfString, PdfName, PdfInteger, PdfReal, PdfBoolean, PdfNull)
                return obj;
        }
    }
}

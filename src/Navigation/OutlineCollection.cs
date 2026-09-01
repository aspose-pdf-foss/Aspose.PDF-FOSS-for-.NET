using System;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

public sealed class OutlineCollection : Outlines, System.Collections.Generic.IEnumerable<OutlineItem>
{
    private readonly PdfDictionary _dict;
    private readonly PdfReader _reader;
    private List<OutlineItem>? _items;
    private bool _dirty;

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

            var first = _reader.ResolveDict(_dict.Get("First"));
            var current = first;
            var visited = new HashSet<int>();

            while (current is not null)
            {
                _items.Add(new OutlineItemCollection(current, _reader) { _ownerCollection = this });

                var nextRef = current.Get("Next");
                if (nextRef is PdfIndirectRef iref && !visited.Add(iref.ObjectNumber))
                    break;

                current = _reader.ResolveDict(nextRef);
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
                throw new IndexOutOfRangeException($"Index {index} is out of range. Valid range: 1 to {items.Count}.");
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

    /// <summary>Removes the outline item with the specified name (matched
    /// against <see cref="OutlineItem.Title"/>).</summary>
    public void Delete(string name)
    {
        _ = Items;
        _items!.RemoveAll(item => item.Title == name);
        _dirty = true;
    }

    /// <summary>Adds an outline item to the collection.</summary>
    public void Add(OutlineItem item)
    {
        _ = Items;
        _items!.Add(item);
        _dirty = true;
    }

    /// <summary>Add an item typed as <see cref="OutlineItemCollection"/> (the
    /// public overload signature). Forwards to the OutlineItem-typed
    /// path.</summary>
    public override void Add(OutlineItemCollection outline)
    {
        _ = Items;
        _items!.Add(outline);
        _dirty = true;
    }

    /// <summary>Removes every item from the collection (counterpart to
    /// <see cref="Delete()"/>).</summary>
    public override void Clear()
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
        foreach (var existing in _items!)
            if (ReferenceEquals(existing, item)) return true;
        return false;
    }

    /// <summary>Copy items into <paramref name="array"/> starting at
    /// <paramref name="index"/>.</summary>
    public override void CopyTo(OutlineItemCollection[] array, int index)
    {
        if (array is null) throw new ArgumentNullException(nameof(array));
        _ = Items;
        for (var i = 0; i < _items!.Count; i++)
            array[index + i] = (OutlineItemCollection)_items[i];
    }

    /// <summary>Remove the supplied item by reference and report whether it was present.</summary>
    public bool Remove(OutlineItem item)
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

    /// <summary>Always false: callers serialise their own access.</summary>
    public bool IsSynchronized => false;

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
            foreach (OutlineItemCollection item in Items)
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
        if (!_dirty) return;
        if (_items is null || _items.Count == 0)
        {
            // All outlines were deleted — remove /Outlines from catalog
            _dict.Remove("First");
            _dict.Remove("Last");
            _dict.Set("Count", new PdfInteger(0));
            return;
        }

        // Allocate object numbers for all items (flat list)
        var flatItems = new List<(OutlineItem item, int objNum)>();
        var baseObjNum = doc.AllocateObjectNumber();
        var outlinesObjNum = baseObjNum++;

        void Flatten(IReadOnlyList<OutlineItem> items)
        {
            foreach (var item in items)
            {
                flatItems.Add((item, baseObjNum++));
                if (item.Children.Count > 0)
                    Flatten((IReadOnlyList<OutlineItem>)item.Children);
            }
        }
        Flatten(_items);

        // Build item → objNum map
        var objMap = new Dictionary<OutlineItem, int>(ReferenceEqualityComparer.Instance);
        foreach (var (item, objNum) in flatItems)
            objMap[item] = objNum;

        // Write each outline item as a new object
        foreach (var (item, objNum) in flatItems)
        {
            var dict = new PdfDictionary();

            // Title (PDFDocEncoding for ASCII, UTF-16BE BOM for non-ASCII)
            dict.Set("Title", OutlineItem.EncodePdfText(item.Title ?? string.Empty));

            // Parent
            var parentItem = FindParent(item);
            var parentObjNum = parentItem is not null && objMap.ContainsKey(parentItem)
                ? objMap[parentItem]
                : outlinesObjNum;
            dict.Set("Parent", new PdfIndirectRef(parentObjNum, 0));

            // Prev / Next siblings
            var siblings = parentItem is not null
                ? (IReadOnlyList<OutlineItem>)parentItem.Children
                : (IReadOnlyList<OutlineItem>)_items;
            var idx = IndexOf(siblings, item);
            if (idx > 0)
                dict.Set("Prev", new PdfIndirectRef(objMap[siblings[idx - 1]], 0));
            if (idx < siblings.Count - 1)
                dict.Set("Next", new PdfIndirectRef(objMap[siblings[idx + 1]], 0));

            // First / Last children
            if (item.Children.Count > 0)
            {
                dict.Set("First", new PdfIndirectRef(objMap[item.Children[0]], 0));
                dict.Set("Last", new PdfIndirectRef(objMap[item.Children[^1]], 0));
                // PDF /Count semantics: visible-descendant magnitude, negated
                // while the node is closed (so Open state survives reload).
                var count = item.VisibleMagnitude;
                dict.Set("Count", new PdfInteger(item.IsOpen ? count : -count));
            }

            // Copy /Dest or /A from the original dict if available
            if (item.Dict is not null)
            {
                var sourceReader = item.Reader;
                CopyEntryIfPresent(item.Dict, dict, "Dest", sourceReader, doc);
                CopyEntryIfPresent(item.Dict, dict, "A", sourceReader, doc);
                CopyEntryIfPresent(item.Dict, dict, "C", sourceReader, doc);
                CopyEntryIfPresent(item.Dict, dict, "F", sourceReader, doc);
            }

            doc.AddNewObject(objNum, dict);
        }

        // Build /Outlines root dict — /Count is the total number of visible
        // items: every top-level item plus open items' visible descendants.
        var rootCount = 0;
        foreach (var item in _items)
        {
            rootCount++;
            if (item.IsOpen) rootCount += item.VisibleMagnitude;
        }
        var outlinesDict = new PdfDictionary();
        outlinesDict.Set("Type", new PdfName("Outlines"));
        outlinesDict.Set("First", new PdfIndirectRef(objMap[_items[0]], 0));
        outlinesDict.Set("Last", new PdfIndirectRef(objMap[_items[^1]], 0));
        outlinesDict.Set("Count", new PdfInteger(rootCount));

        doc.AddNewObject(outlinesObjNum, outlinesDict);
        doc.Reader.Catalog.Set("Outlines", new PdfIndirectRef(outlinesObjNum, 0));

        _dirty = false;
    }

    private OutlineItem? FindParent(OutlineItem target)
    {
        OutlineItem? SearchIn(IReadOnlyList<OutlineItem> items)
        {
            foreach (var item in items)
            {
                foreach (var child in item.Children)
                    if (ReferenceEquals(child, target)) return item;
                var found = SearchIn((IReadOnlyList<OutlineItem>)item.Children);
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

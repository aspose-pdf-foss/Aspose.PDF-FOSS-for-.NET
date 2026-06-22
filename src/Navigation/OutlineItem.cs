using System;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

/// <summary>
/// Represents a bookmark (outline item) in the document outline hierarchy.
/// </summary>
public class OutlineItem
{
    private readonly PdfDictionary _dict;
    private readonly PdfReader _reader;
    private protected List<OutlineItem>? _children;

    internal OutlineItem(PdfDictionary dict, PdfReader reader)
    {
        _dict = dict;
        _reader = reader;
    }

    /// <summary>Internal access to the backing dictionary.</summary>
    internal PdfDictionary Dict => _dict;

    /// <summary>Internal access to the reader.</summary>
    internal PdfReader? Reader => _reader;

    /// <summary>
    /// Create a new outline item that can be added to an OutlineCollection or another OutlineItem.
    /// Mirrors the <c>new OutlineItemCollection(outlines)</c> constructor.
    /// </summary>
    public OutlineItem()
    {
        _dict = new PdfDictionary();
        _reader = null!;
    }

    /// <summary>The bookmark title.</summary>
    public string Title
    {
        get
        {
            var raw = _dict.Get("Title");
            var obj = _reader is not null ? _reader.Resolve(raw) : raw;
            return obj switch
            {
                PdfString s => s.ToText(),
                _ => string.Empty,
            };
        }
        set
        {
            _dict.Set("Title", EncodePdfText(value));
        }
    }

    // PDF text strings use PDFDocEncoding (single byte, mostly Latin1) unless
    // they begin with the UTF-16BE BOM 0xFEFF, in which case they are UTF-16BE.
    // ASCII content fits both; non-ASCII must be encoded as UTF-16BE with BOM
    // to round-trip characters such as CJK or accented letters.
    internal static PdfString EncodePdfText(string value)
    {
        bool isAscii = true;
        foreach (var c in value)
            if (c > 0x7F) { isAscii = false; break; }

        if (isAscii)
            return new PdfString(Encoding.Latin1.GetBytes(value));

        var utf16 = Encoding.BigEndianUnicode.GetBytes(value);
        var withBom = new byte[utf16.Length + 2];
        withBom[0] = 0xFE;
        withBom[1] = 0xFF;
        Buffer.BlockCopy(utf16, 0, withBom, 2, utf16.Length);
        return new PdfString(withBom);
    }

    /// <summary>
    /// The action associated with this outline item, if any.
    /// Setting an action stores the action's PDF dictionary in the /A entry.
    /// </summary>
    public PdfAction? Action
    {
        get
        {
            // A freshly-built outline item (new OutlineItemCollection(doc.Outlines)) has no
            // reader, so resolve the stored /A dictionary directly in that case — otherwise
            // the getter would return null right after the setter stored an action, breaking
            // `(item.Action as GoToRemoteAction).NewWindow = …` with a NullReferenceException.
            var aObj = _dict.Get("A");
            var actionDict = _reader is not null ? _reader.ResolveDict(aObj) : aObj as Core.PdfDictionary;
            return actionDict is not null ? PdfAction.Create(actionDict, _reader!) : null;
        }
        set
        {
            if (value is null)
                _dict.Remove("A");
            else
                _dict.Set("A", value.Dict);
        }
    }

    /// <summary>
    /// The destination of this outline item (reads /Dest only). Returns null
    /// when the outline uses an /A action entry instead — callers should then
    /// read <see cref="Action"/> and extract the destination from the action.
    /// Setting an <see cref="ExplicitDestination"/> writes the /Dest array;
    /// setting a <see cref="PdfAction"/> writes the /A dictionary and clears /Dest.
    /// </summary>
    public IAppointment? Destination
    {
        get
        {
            var destObj = _reader?.Resolve(_dict.Get("Dest"));
            if (destObj is Core.PdfArray destArr)
                return ExplicitDestination.FromArray(destArr, _reader);
            // A /Dest given as a string or name is a *named* destination (PDF 32000
            // §12.3.2.3): resolve it through the catalog's /Dests dict or /Names→/Dests
            // name tree. Falls back to an unresolved NamedDestination so the name is
            // still surfaced when the target isn't present.
            if (_reader is not null && destObj is Core.PdfString or Core.PdfName)
            {
                var name = destObj is Core.PdfString s ? s.ToText() : ((Core.PdfName)destObj).Value;
                return new NamedDestinationCollection(_reader.Catalog, _reader)[name]
                    ?? new NamedDestination(name);
            }
            return null;
        }
        set
        {
            _dict.Remove("Dest");
            _dict.Remove("A");
            switch (value)
            {
                case null:
                    return;
                case PdfAction action:
                    _dict.Set("A", action.Dict);
                    return;
                case ExplicitDestination dest:
                    _dict.Set("Dest", dest.ToPdfArrayPublic());
                    return;
            }
        }
    }

    /// <summary>Gets the child outline item at the specified 1-based index.</summary>
    public OutlineItem this[int index]
    {
        get
        {
            var children = Children;
            if (index < 1 || index > children.Count)
                throw new IndexOutOfRangeException($"Index {index} is out of range. Valid range: 1 to {children.Count}.");
            return children[index - 1];
        }
    }

    /// <summary>Whether this outline item is initially open (expanded).</summary>
    public bool IsOpen
    {
        get => _dict.GetInt("Count") > 0;
        set
        {
            var count = Math.Abs(_dict.GetInt("Count"));
            if (count == 0) count = Children.Count;
            if (count == 0) count = 1; // At least 1 to make open/close meaningful
            _dict.Set("Count", new Core.PdfInteger(value ? count : -count));
        }
    }

    /// <summary>Alias for <see cref="IsOpen"/>.</summary>
    public bool Open { get => IsOpen; set => IsOpen = value; }

    /// <summary>Whether the outline item title is displayed in bold.</summary>
    public bool IsBold
    {
        get => (_dict.GetInt("F") & 2) != 0;
        set => SetFontFlag(2, value);
    }

    /// <summary>Alias for <see cref="IsBold"/>.</summary>
    public bool Bold { get => IsBold; set => IsBold = value; }

    /// <summary>Whether the outline item title is displayed in italic.</summary>
    public bool IsItalic
    {
        get => (_dict.GetInt("F") & 1) != 0;
        set => SetFontFlag(1, value);
    }

    /// <summary>Alias for <see cref="IsItalic"/>.</summary>
    public bool Italic { get => IsItalic; set => IsItalic = value; }

    /// <summary>Sets or clears a font style flag bit in the /F entry.</summary>
    private void SetFontFlag(int bit, bool value)
    {
        int flags = (int)_dict.GetInt("F");
        flags = value ? (flags | bit) : (flags & ~bit);
        _dict.Set("F", new Core.PdfInteger(flags));
    }

    /// <summary>
    /// The color of the outline item title, or empty (default black) if not set.
    /// Uses System.Drawing.Color to match the public API.
    /// The PDF spec stores outline colors as RGB arrays with values 0.0–1.0 (/C entry).
    /// </summary>
    public System.Drawing.Color Color
    {
        get
        {
            var cArr = _reader?.Resolve(_dict.Get("C")) as Core.PdfArray;
            if (cArr is null || cArr.Count < 3) return System.Drawing.Color.Black;
            double GetVal(Core.PdfObject obj) => obj switch
            {
                Core.PdfReal r => r.Value,
                Core.PdfInteger i => i.Value,
                _ => 0.0,
            };
            int r = (int)(GetVal(cArr[0]) * 255);
            int g = (int)(GetVal(cArr[1]) * 255);
            int b = (int)(GetVal(cArr[2]) * 255);
            return System.Drawing.Color.FromArgb(r, g, b);
        }
        set
        {
            var arr = new Core.PdfArray();
            arr.Add(new Core.PdfReal(value.R / 255.0));
            arr.Add(new Core.PdfReal(value.G / 255.0));
            arr.Add(new Core.PdfReal(value.B / 255.0));
            _dict.Set("C", arr);
        }
    }

    /// <summary>The destination page number (1-based), or 0 if not set or not a page destination.</summary>
    public int DestinationPageNumber
    {
        get
        {
            // /Dest may be inline array; or /A action with /D either inline array
            // or a named-destination string.
            var dest = _reader.Resolve(_dict.Get("Dest"));
            if (dest is Core.PdfArray arr && PageNumberFromDestArray(arr) is int n1)
                return n1;

            // /Dest may itself be a GoTo-action dictionary carrying the explicit
            // destination under /D (some producers inline the action there instead
            // of using a separate /A entry).
            if (dest is Core.PdfDictionary destDict
                && _reader.Resolve(destDict.Get("D")) is Core.PdfArray destDictArr
                && PageNumberFromDestArray(destDictArr) is int nDest)
                return nDest;

            var action = _reader.ResolveDict(_dict.Get("A"));
            if (action is not null)
            {
                var actionDest = _reader.Resolve(action.Get("D"));
                if (actionDest is Core.PdfArray destArr
                    && PageNumberFromDestArray(destArr) is int n2)
                    return n2;
            }

            return 0;
        }
    }

    /// <summary>The destination array's leading element either references the
    /// target Page indirectly (page reference must walk the
    /// Pages tree) or is a 0-based page index (FOSS writer's ExplicitDestination.
    /// ToPdfArray emits this shape). Resolve both; returns null when neither
    /// shape decodes to a real page.</summary>
    private int? PageNumberFromDestArray(Core.PdfArray arr)
    {
        if (arr.Count < 1) return null;
        var head = arr[0];
        if (head is Core.PdfIndirectRef iref)
        {
            var pageDict = _reader.ResolveDict(iref);
            if (pageDict?.GetName("Type") == "Page")
                return FindPageNumber(pageDict);
        }
        if (head is Core.PdfInteger pi && pi.Value >= 0)
        {
            // 0-based page index → 1-based page number.
            return (int)pi.Value + 1;
        }
        return null;
    }

    private int FindPageNumber(Core.PdfDictionary targetPage)
    {
        // Walk pages tree to find 1-based page number
        var catalog = _reader.Catalog;
        var pagesDict = _reader.ResolveDict(catalog.Get("Pages"));
        if (pagesDict is null) return 0;

        int pageNum = 0;
        bool found = false;
        CountPages(pagesDict, targetPage, ref pageNum, ref found);
        return found ? pageNum : 0;
    }

    private void CountPages(Core.PdfDictionary node, Core.PdfDictionary target,
        ref int pageNum, ref bool found)
    {
        if (found) return;
        var type = node.GetName("Type");
        if (type == "Page")
        {
            pageNum++;
            if (ReferenceEquals(node, target)) found = true;
            return;
        }

        var kids = _reader.Resolve(node.Get("Kids")) as Core.PdfArray;
        if (kids is null) return;
        foreach (var kid in kids)
        {
            if (found) return;
            var kidDict = _reader.ResolveDict(kid);
            if (kidDict is not null)
                CountPages(kidDict, target, ref pageNum, ref found);
        }
    }

    /// <summary>Removes the first child outline item.</summary>
    public void Delete()
    {
        var children = Children;
        if (_children is not null && _children.Count > 0)
            _children.RemoveAt(0);
    }

    /// <summary>Appends a child outline item.</summary>
    public void Add(OutlineItem child)
    {
        _ = Children; // ensure lazy init
        _children!.Add(child);
    }

    /// <summary>Inserts a child outline item at a 1-based position.</summary>
    public void Insert(int index, OutlineItem child)
    {
        _ = Children; // ensure lazy init
        if (index < 1) index = 1;
        if (index > _children!.Count + 1) index = _children.Count + 1;
        _children.Insert(index - 1, child);
    }

    /// <summary>Child outline items.</summary>
    public IReadOnlyList<OutlineItem> Children
    {
        get
        {
            if (_children is not null) return _children;
            _children = [];

            if (_reader is null) return _children;
            var first = _reader.ResolveDict(_dict.Get("First"));
            var current = first;
            var visited = new HashSet<int>();

            while (current is not null)
            {
                _children.Add(new OutlineItemCollection(current, _reader));

                var nextRef = current.Get("Next");
                if (nextRef is PdfIndirectRef iref && !visited.Add(iref.ObjectNumber))
                    break;

                current = _reader.ResolveDict(nextRef);
            }

            return _children;
        }
    }
}

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
    private Outlines? _parentOutlines;

    internal OutlineItemCollection(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>
    /// Iterates over child outline items. Each child is wrapped as OutlineItemCollection
    /// so recursive iteration (foreach inside foreach) works naturally.
    /// </summary>
    public System.Collections.Generic.IEnumerator<OutlineItemCollection> GetEnumerator()
    {
        foreach (var child in Children)
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

    // ── Aspose.PDF for .NET additions ─────────────────────────────────────

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
    public Outlines? Parent
    {
        get
        {
            if (_parentOutlines is not null) return _parentOutlines;
            if (_backingCollection is not null)
            {
                _parentOutlines = new Outlines(_backingCollection);
                return _parentOutlines;
            }
            return null;
        }
    }

    /// <summary>Number of bookmarks that are visible (counts /Count entries recursively).</summary>
    public int VisibleCount => Children.Count;

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

    /// <summary>Remove every child outline item (alias for <see cref="Clear"/>).</summary>
    public new void Delete() => Clear();

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
        return _children.Count < initial;
    }

    /// <summary>Remove the child at the supplied 1-based index.</summary>
    public void Remove(int index)
    {
        if (_children is null) _ = Children;
        if (index < 1 || index > _children!.Count) return;
        _children.RemoveAt(index - 1);
    }
}

public sealed class OutlineCollection : System.Collections.Generic.IEnumerable<OutlineItem>
{
    private readonly PdfDictionary _dict;
    private readonly PdfReader _reader;
    private List<OutlineItem>? _items;
    private bool _dirty;

    /// <summary>Enumerator over the top-level outline items, typed as
    /// <see cref="OutlineItemCollection"/> to match the Aspose.PDF for .NET
    /// reflection signature. The items stored are concrete
    /// OutlineItemCollection instances anyway.</summary>
    public System.Collections.Generic.IEnumerator<OutlineItemCollection> GetEnumerator()
    {
        foreach (var item in Items)
            yield return (OutlineItemCollection)item;
    }

    System.Collections.Generic.IEnumerator<OutlineItem>
        System.Collections.Generic.IEnumerable<OutlineItem>.GetEnumerator() => Items.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    internal OutlineCollection(PdfDictionary dict, PdfReader reader)
    {
        _dict = dict;
        _reader = reader;
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
                _items.Add(new OutlineItemCollection(current, _reader));

                var nextRef = current.Get("Next");
                if (nextRef is PdfIndirectRef iref && !visited.Add(iref.ObjectNumber))
                    break;

                current = _reader.ResolveDict(nextRef);
            }

            return _items;
        }
    }

    public int Count => Items.Count;

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
    /// Aspose.PDF for .NET overload signature). Forwards to the OutlineItem-typed
    /// path.</summary>
    public void Add(OutlineItemCollection outline)
    {
        _ = Items;
        _items!.Add(outline);
        _dirty = true;
    }

    /// <summary>Removes every item from the collection (counterpart to
    /// <see cref="Delete()"/>).</summary>
    public void Clear()
    {
        _ = Items;
        _items!.Clear();
        _dirty = true;
    }

    /// <summary>Whether <paramref name="item"/> is currently in the collection.</summary>
    public bool Contains(OutlineItemCollection item)
    {
        if (item is null) return false;
        _ = Items;
        foreach (var existing in _items!)
            if (ReferenceEquals(existing, item)) return true;
        return false;
    }

    /// <summary>Copy items into <paramref name="array"/> starting at
    /// <paramref name="index"/>.</summary>
    public void CopyTo(OutlineItemCollection[] array, int index)
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
    public bool Remove(OutlineItemCollection item)
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
    public bool IsReadOnly => false;

    /// <summary>Always false: callers serialise their own access.</summary>
    public bool IsSynchronized => false;

    /// <summary>Sentinel object for ICollection.SyncRoot-style locking.</summary>
    public object SyncRoot { get; } = new();

    /// <summary>Number of outline items currently visible (Open == true).
    /// Items that aren't open hide their children from the visible count.</summary>
    public int VisibleCount
    {
        get
        {
            int count = 0;
            foreach (OutlineItemCollection item in Items)
            {
                count++;
                if (item.Open)
                    count += CountVisibleChildren(item);
            }
            return count;
        }
    }

    private static int CountVisibleChildren(OutlineItemCollection parent)
    {
        int count = 0;
        foreach (OutlineItemCollection child in parent)
        {
            count++;
            if (child.Open) count += CountVisibleChildren(child);
        }
        return count;
    }

    /// <summary>Whether the outline collection was modified after loading.</summary>
    internal bool IsDirty => _dirty;

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
                var count = CountDescendants(item);
                dict.Set("Count", new PdfInteger(count));
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

        // Build /Outlines root dict
        var outlinesDict = new PdfDictionary();
        outlinesDict.Set("Type", new PdfName("Outlines"));
        outlinesDict.Set("First", new PdfIndirectRef(objMap[_items[0]], 0));
        outlinesDict.Set("Last", new PdfIndirectRef(objMap[_items[^1]], 0));
        outlinesDict.Set("Count", new PdfInteger(_items.Count));

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

    private static int CountDescendants(OutlineItem item)
    {
        var count = item.Children.Count;
        foreach (var child in item.Children)
            count += CountDescendants(child);
        return count;
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

/// <summary>
/// Builder for creating bookmarks (outlines) programmatically.
/// Registers with the document for auto-finalization on save.
/// </summary>
public sealed class OutlineBuilder
{
    private readonly Document _document;
    private readonly List<OutlineItemBuilder> _items = [];

    public OutlineBuilder(Document document)
    {
        _document = document;
        document.RegisterOutlineBuilder(this);
    }

    /// <summary>Add a top-level bookmark pointing to a page (0-based index).</summary>
    public OutlineItemBuilder Add(string title, int pageIndex)
    {
        var item = new OutlineItemBuilder(title, pageIndex);
        _items.Add(item);
        return item;
    }

    /// <summary>Add a top-level bookmark pointing to a page.</summary>
    public OutlineItemBuilder Add(string title, Page page)
        => Add(title, page.Index);

    /// <summary>
    /// Build the outline dictionary tree and register it with the document.
    /// Called automatically by Document.ToArray().
    /// </summary>
    internal void Build()
    {
        if (_items.Count == 0) return;

        // Collect all items (flat) and assign object numbers
        var allItems = new List<(OutlineItemBuilder builder, int objNum, OutlineItemBuilder? parent, int siblingIndex, int siblingCount)>();
        var baseObjNum = _document.AllocateObjectNumber() + 50;
        var outlinesObjNum = baseObjNum++;

        void Collect(IReadOnlyList<OutlineItemBuilder> items, OutlineItemBuilder? parent)
        {
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var objNum = baseObjNum++;
                allItems.Add((item, objNum, parent, i, items.Count));
                if (item.Children.Count > 0)
                    Collect(item.Children, item);
            }
        }

        Collect(_items, null);

        // Build a map: builder → objNum
        var objMap = new Dictionary<OutlineItemBuilder, int>();
        foreach (var (builder, objNum, _, _, _) in allItems)
            objMap[builder] = objNum;

        // Get page object refs for destinations
        var pageRefs = BuildPageRefs();

        // Write each outline item
        foreach (var (builder, objNum, parent, siblingIdx, siblingCount) in allItems)
        {
            var dict = new PdfDictionary();
            dict.Set("Title", OutlineItem.EncodePdfText(builder.Title ?? string.Empty));

            // /Parent
            var parentObjNum = parent is not null ? objMap[parent] : outlinesObjNum;
            dict.Set("Parent", new PdfIndirectRef(parentObjNum, 0));

            // /Prev and /Next for sibling linked list
            var siblings = parent?.Children ?? (IReadOnlyList<OutlineItemBuilder>)_items;
            if (siblingIdx > 0)
                dict.Set("Prev", new PdfIndirectRef(objMap[siblings[siblingIdx - 1]], 0));
            if (siblingIdx < siblingCount - 1)
                dict.Set("Next", new PdfIndirectRef(objMap[siblings[siblingIdx + 1]], 0));

            // /First and /Last for children
            if (builder.Children.Count > 0)
            {
                dict.Set("First", new PdfIndirectRef(objMap[builder.Children[0]], 0));
                dict.Set("Last", new PdfIndirectRef(objMap[builder.Children[^1]], 0));
                var count = CountDescendants(builder);
                dict.Set("Count", new PdfInteger(builder.IsOpen ? count : -count));
            }

            // /Dest — page destination [pageRef /Fit]
            if (builder.PageIndex >= 0 && builder.PageIndex < pageRefs.Count)
            {
                var dest = new PdfArray();
                dest.Add(pageRefs[builder.PageIndex]);
                dest.Add(new PdfName("Fit"));
                dict.Set("Dest", dest);
            }

            // /C — color
            if (builder.ColorR is not null)
            {
                var c = new PdfArray();
                c.Add(new PdfReal(builder.ColorR.Value));
                c.Add(new PdfReal(builder.ColorG!.Value));
                c.Add(new PdfReal(builder.ColorB!.Value));
                dict.Set("C", c);
            }

            // /F — style flags
            var flags = 0;
            if (builder.IsItalic) flags |= 1;
            if (builder.IsBold) flags |= 2;
            if (flags != 0)
                dict.Set("F", new PdfInteger(flags));

            // registerOverlay: expose the outline item in-memory so a read path
            // (e.g. PdfBookmarkEditor.ExtractBookmarks after FlushPendingOutlineBuilder)
            // can walk the tree before the document is saved.
            _document.AddNewObject(objNum, dict, registerOverlay: true);
        }

        // Build /Outlines dict
        var outlinesDict = new PdfDictionary();
        outlinesDict.Set("Type", new PdfName("Outlines"));
        outlinesDict.Set("First", new PdfIndirectRef(objMap[_items[0]], 0));
        outlinesDict.Set("Last", new PdfIndirectRef(objMap[_items[^1]], 0));
        outlinesDict.Set("Count", new PdfInteger(_items.Count));
        _document.AddNewObject(outlinesObjNum, outlinesDict, registerOverlay: true);
        _document.Catalog.Set("Outlines", new PdfIndirectRef(outlinesObjNum, 0));
    }

    private List<PdfObject> BuildPageRefs()
    {
        // Build indirect refs to each page object
        var refs = new List<PdfObject>();
        var xref = _document.Reader.XRefTable;
        var catalog = _document.Reader.Catalog;
        var pagesDict = _document.Reader.ResolveDict(catalog.Get("Pages"));
        if (pagesDict is null) return refs;

        CollectPageRefs(pagesDict, _document.Reader, refs);
        return refs;
    }

    private static void CollectPageRefs(PdfDictionary node, PdfReader reader, List<PdfObject> result)
    {
        var type = node.GetName("Type");
        if (type == "Page")
        {
            // We need an indirect ref — find it from kids array
            result.Add(node); // placeholder, will use direct dict ref
            return;
        }

        var kids = reader.Resolve(node.Get("Kids")) as PdfArray;
        if (kids is null) return;
        foreach (var kid in kids)
        {
            if (kid is PdfIndirectRef)
                result.Add(kid); // keep the indirect ref
            else
            {
                var kidDict = reader.ResolveDict(kid);
                if (kidDict is not null)
                    CollectPageRefs(kidDict, reader, result);
            }
        }
    }

    private static int CountDescendants(OutlineItemBuilder item)
    {
        var count = item.Children.Count;
        foreach (var child in item.Children)
            count += CountDescendants(child);
        return count;
    }
}

/// <summary>
/// Builder for a single outline item with fluent API.
/// </summary>
public sealed class OutlineItemBuilder
{
    private readonly List<OutlineItemBuilder> _children = [];

    internal OutlineItemBuilder(string title, int pageIndex)
    {
        Title = title;
        PageIndex = pageIndex;
    }

    public string Title { get; set; }
    public int PageIndex { get; }
    public bool IsOpen { get; private set; } = true;
    public bool IsBold { get; private set; }
    public bool IsItalic { get; private set; }
    internal double? ColorR { get; private set; }
    internal double? ColorG { get; private set; }
    internal double? ColorB { get; private set; }
    internal IReadOnlyList<OutlineItemBuilder> Children => _children;

    /// <summary>Add a child bookmark.</summary>
    public OutlineItemBuilder AddChild(string title, int pageIndex)
    {
        var child = new OutlineItemBuilder(title, pageIndex);
        _children.Add(child);
        return child;
    }

    /// <summary>Add a child bookmark pointing to a page.</summary>
    public OutlineItemBuilder AddChild(string title, Page page)
        => AddChild(title, page.Index);

    /// <summary>Set whether this bookmark is initially open/expanded.</summary>
    public OutlineItemBuilder SetOpen(bool open) { IsOpen = open; return this; }

    /// <summary>Set the bookmark text color (RGB 0.0-1.0).</summary>
    public OutlineItemBuilder SetColor(double r, double g, double b)
    {
        ColorR = r; ColorG = g; ColorB = b;
        return this;
    }

    /// <summary>Set bold style.</summary>
    public OutlineItemBuilder SetBold(bool bold) { IsBold = bold; return this; }

    /// <summary>Set italic style.</summary>
    public OutlineItemBuilder SetItalic(bool italic) { IsItalic = italic; return this; }
}

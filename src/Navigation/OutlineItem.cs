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

    /// <summary>The item that owns this node as a child, so <c>Delete()</c> can
    /// remove this node from its parent. Set when the parent materializes its
    /// children (see <see cref="Children"/>); null for a top-level item, whose
    /// owner is an <see cref="OutlineCollection"/> instead.</summary>
    private protected OutlineItem? _ownerItem;

    /// <summary>Remove <paramref name="child"/> from this item's children.</summary>
    internal bool RemoveChild(OutlineItem child)
    {
        _ = Children; // ensure lazy init
        var removed = _children!.RemoveAll(c => ReferenceEquals(c, child)) > 0;
        if (removed) MarkTreeDirty();
        return removed;
    }

    /// <summary>Propagate a structural change up to the owning
    /// <see cref="OutlineCollection"/> so the tree is re-serialised on save.</summary>
    internal virtual void MarkTreeDirty() => _ownerItem?.MarkTreeDirty();

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

    /// <summary>Number of descendants that appear when this node is open:
    /// immediate children plus, for every open child, that child's own
    /// visible magnitude (PDF /Count magnitude, computed live from the tree).</summary>
    internal int VisibleMagnitude
    {
        get
        {
            var count = 0;
            foreach (var child in Children)
            {
                count++;
                if (child.IsOpen) count += child.VisibleMagnitude;
            }
            return count;
        }
    }

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
        {
            _children.RemoveAt(0);
            MarkTreeDirty();
        }
    }

    /// <summary>Appends a child outline item.</summary>
    public void Add(OutlineItem child)
    {
        _ = Children; // ensure lazy init
        _children!.Add(child);
        child._ownerItem = this;
        MarkTreeDirty();
    }

    /// <summary>Inserts a child outline item at a 1-based position.</summary>
    public void Insert(int index, OutlineItem child)
    {
        _ = Children; // ensure lazy init
        if (index < 1) index = 1;
        if (index > _children!.Count + 1) index = _children.Count + 1;
        _children.Insert(index - 1, child);
        child._ownerItem = this;
        MarkTreeDirty();
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
                _children.Add(new OutlineItemCollection(current, _reader) { _ownerItem = this });

                var nextRef = current.Get("Next");
                if (nextRef is PdfIndirectRef iref && !visited.Add(iref.ObjectNumber))
                    break;

                current = _reader.ResolveDict(nextRef);
            }

            return _children;
        }
    }
}

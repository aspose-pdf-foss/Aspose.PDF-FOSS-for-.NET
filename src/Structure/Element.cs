using System.Collections;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Structure;

/// <summary>
/// A node in a PDF's logical-structure tree (the /StructTreeRoot /K
/// subtree, plus their /K descendants). Wraps a structure-element
/// dictionary; reads/writes /Alt, /ActualText, /E, /Lang on demand.
/// </summary>
public class Element
{
    internal readonly PdfDictionary _dict;
    internal readonly PdfReader? _reader;
    internal Element? _parent;
    private ElementCollection? _children;

    internal Element(PdfDictionary dict, PdfReader? reader, Element? parent = null)
    {
        _dict = dict;
        _reader = reader;
        _parent = parent;
    }

    /// <summary>The actual text content of this structure element
    /// (/ActualText entry).</summary>
    public string ActualText
    {
        get => GetString("ActualText") ?? string.Empty;
        set => SetString("ActualText", value);
    }

    /// <summary>The alternate-text description (/Alt entry), used by
    /// assistive technology to read the element aloud.</summary>
    public string Alt
    {
        get => GetString("Alt") ?? string.Empty;
        set => SetString("Alt", value);
    }

    /// <summary>The expansion text for an abbreviation (/E entry).</summary>
    public string E
    {
        get => GetString("E") ?? string.Empty;
        set => SetString("E", value);
    }

    /// <summary>The natural-language code for the element's text
    /// content (/Lang entry, e.g. "en-US").</summary>
    public string Lang
    {
        get => GetString("Lang") ?? string.Empty;
        set => SetString("Lang", value);
    }

    /// <summary>Child structure elements held under this element's /K
    /// entry. Returns a live collection — Remove() mutates both the
    /// in-memory snapshot and the underlying /K array.</summary>
    public ElementCollection Children =>
        _children ??= new ElementCollection(this, LoadChildren());

    /// <summary>The element this one was attached to, or null when it
    /// is a root element. Tracked in-memory; not derived from /P.</summary>
    public Element? Parent => _parent;

    /// <summary>Detach this element from its parent (drop it from the
    /// parent's /K array).</summary>
    public void Remove()
    {
        _parent?.Children.Remove(this);
    }

    // ── internal helpers ─────────────────────────────────────────────

    private string? GetString(string key)
    {
        var obj = _reader?.Resolve(_dict.Get(key)) ?? _dict.Get(key);
        return obj is PdfString s ? s.ToText() : null;
    }

    private void SetString(string key, string? value)
    {
        if (string.IsNullOrEmpty(value)) _dict.Remove(key);
        else _dict.Set(key, new PdfString(Encoding.UTF8.GetBytes(value)));
    }

    private List<Element> LoadChildren()
    {
        var list = new List<Element>();
        if (_reader is null) return list;
        var kids = _reader.Resolve(_dict.Get("K"));
        if (kids is PdfArray arr)
        {
            foreach (var item in arr)
            {
                if (_reader.Resolve(item) is PdfDictionary kd
                    && kd.GetName("Type") is null or "StructElem")
                {
                    list.Add(Materialize(kd, _reader, this));
                }
            }
        }
        else if (kids is PdfDictionary singleKid
                 && singleKid.GetName("Type") is null or "StructElem")
        {
            list.Add(Materialize(singleKid, _reader, this));
        }
        return list;
    }

    internal static Element Materialize(PdfDictionary dict, PdfReader reader, Element? parent)
    {
        var role = dict.GetName("S");
        return role switch
        {
            "Figure" => new FigureElement(dict, reader, parent),
            "Span" or "P" or "Quote" or "Note" or "Reference" or "BibEntry"
                => new TextElement(dict, reader, parent),
            _ => new StructElement(dict, reader, parent),
        };
    }

    /// <summary>Re-bind this element to a different parent (used when
    /// the element is moved within the tree).</summary>
    internal void SetParent(Element? parent) => _parent = parent;

    /// <summary>Drop the cached children so the next read pulls from
    /// /K again (used when /K is rewritten externally).</summary>
    internal void InvalidateChildren() => _children = null;

    /// <summary>The backing structure dictionary.</summary>
    internal PdfDictionary Dict => _dict;
}

/// <summary>A live, mutable collection of structure elements held
/// under a parent's /K array. Mirrors the Aspose.PDF for .NET
/// <c>Aspose.Pdf.Structure.ElementCollection</c> surface.</summary>
public class ElementCollection : IEnumerable<Element>
{
    private readonly Element _parent;
    private readonly List<Element> _items;

    internal ElementCollection(Element parent, List<Element> items)
    {
        _parent = parent;
        _items = items;
    }

    /// <summary>Number of direct children.</summary>
    public int Count => _items.Count;

    /// <summary>0-based access to a child by index.</summary>
    public Element this[int index] => _items[index];

    /// <summary>Iterate the children in tree order.</summary>
    public IEnumerator<Element> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Remove <paramref name="item"/> from this collection and
    /// from the parent's /K array. Returns true when the child was
    /// found and removed.</summary>
    public bool Remove(Element item)
    {
        if (item is null) return false;
        var idx = _items.IndexOf(item);
        if (idx < 0) return false;
        _items.RemoveAt(idx);
        RemoveFromParentK(item);
        item.SetParent(null);
        return true;
    }

    private void RemoveFromParentK(Element item)
    {
        if (_parent._reader is null) return;
        var k = _parent._reader.Resolve(_parent.Dict.Get("K"));
        if (k is PdfArray arr)
        {
            // Walk the array and drop the entry that resolves to item.Dict.
            for (var i = 0; i < arr.Count; i++)
            {
                var resolved = _parent._reader.Resolve(arr[i]);
                if (ReferenceEquals(resolved, item.Dict))
                {
                    arr.RemoveAt(i);
                    return;
                }
            }
        }
        else if (k is PdfDictionary single && ReferenceEquals(single, item.Dict))
        {
            _parent.Dict.Remove("K");
        }
    }
}

/// <summary>Top-level wrapper for the PDF /StructTreeRoot dictionary.
/// Returned by <see cref="Aspose.Pdf.Document.LogicalStructure"/>.</summary>
public sealed class RootElement : Element
{
    internal RootElement(PdfDictionary dict, PdfReader reader)
        : base(dict, reader, parent: null) { }
}

/// <summary>A generic structure element (anything other than the
/// recognised typed subclasses).</summary>
public class StructElement : Element
{
    internal StructElement(PdfDictionary dict, PdfReader reader, Element? parent)
        : base(dict, reader, parent) { }
}

/// <summary>A text-bearing structure element (Span / P / Quote /
/// Note / Reference / BibEntry).</summary>
public class TextElement : Element
{
    internal TextElement(PdfDictionary dict, PdfReader reader, Element? parent)
        : base(dict, reader, parent) { }

    /// <summary>The text content of this element. Returns the
    /// /ActualText entry when present; otherwise the value of /T (the
    /// element title) when set; otherwise an empty string.</summary>
    public string Text
    {
        get
        {
            if (!string.IsNullOrEmpty(ActualText)) return ActualText;
            var obj = _reader?.Resolve(_dict.Get("T")) ?? _dict.Get("T");
            return obj is PdfString s ? s.ToText() : string.Empty;
        }
    }
}

/// <summary>A figure structure element (/S = "Figure") that wraps a
/// raster or vector picture.</summary>
public class FigureElement : Element
{
    internal FigureElement(PdfDictionary dict, PdfReader reader, Element? parent)
        : base(dict, reader, parent) { }

    /// <summary>The figure's image extracted from the page's /Resources
    /// /XObject dictionary, or null when the figure has no embedded
    /// image stream. Returned as a <see cref="System.Drawing.Image"/>
    /// matching the Aspose.PDF for .NET public type. Throws
    /// <see cref="System.PlatformNotSupportedException"/> on non-Windows
    /// runtimes (per System.Drawing.Common's runtime contract).</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public System.Drawing.Image? Image
    {
        get
        {
            if (_reader is null) return null;
            var stream = ResolveFirstImageStream();
            if (stream is null) return null;
            try
            {
                var data = _reader.DecodeStream(stream);
                using var ms = new MemoryStream(data);
                return System.Drawing.Image.FromStream(ms);
            }
            catch
            {
                return null;
            }
        }
    }

    private PdfStream? ResolveFirstImageStream()
    {
        // Walk /K. An entry that resolves to a PdfStream with /Subtype
        // /Image is the figure's image. The PDF spec also allows the
        // image to be referenced via /MCID into a content stream, but
        // that path requires page rendering — out of scope here.
        var k = _reader!.Resolve(_dict.Get("K"));
        return FirstStream(k);
    }

    private PdfStream? FirstStream(PdfObject? obj)
    {
        switch (obj)
        {
            case PdfStream s when s.Dict.GetName("Subtype") == "Image":
                return s;
            case PdfArray arr:
                foreach (var item in arr)
                {
                    var resolved = _reader!.Resolve(item);
                    var found = FirstStream(resolved);
                    if (found is not null) return found;
                }
                break;
        }
        return null;
    }
}

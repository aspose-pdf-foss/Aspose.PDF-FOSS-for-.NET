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

    /// <summary>
    /// After this element has been detached from its parent's /K, physically remove its
    /// backing structure-element object — and every structure element in its descendant
    /// subtree — from the document, then scrub the /StructTreeRoot's /ParentTree and
    /// /IDTree of the now-dangling references to them. Without this the orphaned StructElem
    /// objects stay in the file (still reachable via /ParentTree), so removing tags never
    /// shrinks the saved document.
    /// </summary>
    internal void PurgeStructureSubtree(int ownObjectNumber)
    {
        if (_reader is null) return;

        var freed = new HashSet<int>();
        if (ownObjectNumber > 0) freed.Add(ownObjectNumber);
        CollectChildStructElems(_dict, freed);
        if (freed.Count == 0) return;

        // Mark each freed object's xref entry as free so the writer skips it on save.
        var xref = _reader.XRefTable;
        foreach (var num in freed)
        {
            var entry = xref.GetEntry(num);
            if (entry is { InUse: true } e)
                xref.SetEntry(num, new XRefEntry { ObjectNumber = num, Generation = e.Generation, InUse = false });
        }

        // Replace references to freed objects in the number/name trees with null so the
        // saved file has no dangling references.
        var root = _reader.ResolveDict(_reader.Catalog.Get("StructTreeRoot"));
        if (root is not null)
        {
            var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
            ScrubTreeReferences(root.Get("ParentTree"), freed, seen);
            ScrubTreeReferences(root.Get("IDTree"), freed, seen);
        }
    }

    /// <summary>Collect the object numbers of every indirect structure element reachable
    /// through <paramref name="structElem"/>'s /K subtree (recursively). MCID integers and
    /// /MCR / /OBJR content references are skipped — they are not part of the tree.</summary>
    private void CollectChildStructElems(PdfDictionary structElem, HashSet<int> freed)
    {
        var kResolved = _reader!.Resolve(structElem.Get("K"));
        if (kResolved is PdfArray arr)
        {
            foreach (var item in arr) HandleKItem(item, freed);
        }
        else
        {
            // /K may be a single child (a direct reference or an inline dictionary).
            HandleKItem(structElem.Get("K"), freed);
        }
    }

    private void HandleKItem(PdfObject? item, HashSet<int> freed)
    {
        if (item is null) return;
        var objNum = item is PdfIndirectRef iref ? iref.ObjectNumber : -1;
        if (_reader!.Resolve(item) is not PdfDictionary d) return; // MCID integer
        var type = d.GetName("Type");
        if (type is "MCR" or "OBJR") return;                       // marked-content / object reference
        if (type is not null && type != "StructElem") return;      // not a structure element
        if (objNum > 0 && !freed.Add(objNum)) return;              // already visited (cycle guard)
        CollectChildStructElems(d, freed);
    }

    /// <summary>Walk a PDF number tree (/Nums) or name tree (/Names) and replace any value
    /// that references a freed object with null, descending through /Kids.</summary>
    private void ScrubTreeReferences(PdfObject? node, HashSet<int> freed, HashSet<PdfDictionary> seen)
    {
        if (_reader!.Resolve(node) is not PdfDictionary d || !seen.Add(d)) return;

        foreach (var valuesKey in new[] { "Nums", "Names" })
        {
            if (_reader.Resolve(d.Get(valuesKey)) is PdfArray entries)
                // Entries are laid out as [key value key value ...]; values sit at odd indices.
                for (var i = 1; i < entries.Count; i += 2)
                    ScrubTreeValue(entries, i, freed);
        }

        if (_reader.Resolve(d.Get("Kids")) is PdfArray kids)
            foreach (var kid in kids) ScrubTreeReferences(kid, freed, seen);
    }

    private void ScrubTreeValue(PdfArray entries, int index, HashSet<int> freed)
    {
        var value = entries[index];
        if (value is PdfIndirectRef r && freed.Contains(r.ObjectNumber))
        {
            entries.ReplaceAt(index, PdfNull.Instance);
            return;
        }
        // A page's value is an array of per-MCID structure-element references.
        if (_reader!.Resolve(value) is PdfArray arr)
            for (var i = 0; i < arr.Count; i++)
                if (arr[i] is PdfIndirectRef ir && freed.Contains(ir.ObjectNumber))
                    arr.ReplaceAt(i, PdfNull.Instance);
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
/// under a parent's /K array. Mirrors the
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
        var removedObjNum = RemoveFromParentK(item);
        item.SetParent(null);
        // Physically drop the detached subtree's structure-element objects so a
        // subsequent Save shrinks the file instead of carrying them as orphans.
        item.PurgeStructureSubtree(removedObjNum);
        return true;
    }

    /// <summary>Drop <paramref name="item"/> from the parent's /K array (or single /K
    /// entry). Returns the object number of the removed entry when it was an indirect
    /// reference, or -1 when it was inline (or not found).</summary>
    private int RemoveFromParentK(Element item)
    {
        if (_parent._reader is null) return -1;
        var k = _parent._reader.Resolve(_parent.Dict.Get("K"));
        if (k is PdfArray arr)
        {
            // Walk the array and drop the entry that resolves to item.Dict.
            for (var i = 0; i < arr.Count; i++)
            {
                var raw = arr[i];
                var resolved = _parent._reader.Resolve(raw);
                if (ReferenceEquals(resolved, item.Dict))
                {
                    arr.RemoveAt(i);
                    return raw is PdfIndirectRef r ? r.ObjectNumber : -1;
                }
            }
        }
        else if (k is PdfDictionary single && ReferenceEquals(single, item.Dict))
        {
            var raw = _parent.Dict.Get("K");
            _parent.Dict.Remove("K");
            return raw is PdfIndirectRef r ? r.ObjectNumber : -1;
        }
        return -1;
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
    /// matching the public type. Throws
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
                // Reconstruct a raster via ImageXObject, which decodes the full range
                // of image filters and colour spaces (FlateDecode/ICCBased/DCT/…) into
                // an encoded PNG or JPEG. Feeding raw DecodeStream bytes to
                // Image.FromStream only works for an embedded JPEG codestream and fails
                // for inflated raw samples.
                var xobj = new ImageXObject("Img", stream, _reader);
                using var ms = new MemoryStream();
                xobj.Save(ms);
                ms.Position = 0;
                using var loaded = System.Drawing.Image.FromStream(ms);
                return new System.Drawing.Bitmap(loaded);
            }
            catch
            {
                return null;
            }
        }
    }

    private PdfStream? ResolveFirstImageStream()
    {
        // Walk /K. An entry that resolves to a PdfStream with /Subtype /Image is the
        // figure's image. The PDF spec also allows the image to be referenced via an
        // /MCID marked-content sequence in a page content stream; resolve that too.
        var k = _reader!.Resolve(_dict.Get("K"));
        return FirstStream(k) ?? ResolveImageViaMarkedContent(k);
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

    // ── Marked-content (MCID) image resolution ────────────────────────────────

    // Operators of interest, scanned left-to-right: a marked-content point with a
    // property list (BDC), a tag-only marked-content point (BMC), its end (EMC), and an
    // XObject paint (Do). Group captures pick which one matched.
    private static readonly System.Text.RegularExpressions.Regex MarkedContentScanner =
        new(@"(?<bdc>/[\w.\-]+\s*(?<props><<[^>]*?>>|/[\w.\-]+)\s*BDC)" +
            @"|(?<bmc>/[\w.\-]+\s*BMC)" +
            @"|(?<emc>\bEMC\b)" +
            @"|/(?<doname>[\w.\-]+)\s+(?<do>Do)\b",
            System.Text.RegularExpressions.RegexOptions.Compiled |
            System.Text.RegularExpressions.RegexOptions.Singleline);

    /// <summary>Resolve the figure's image when its /K is (or contains) an MCID that
    /// points into a page content stream, by finding the image XObject painted inside
    /// that marked-content region.</summary>
    private PdfStream? ResolveImageViaMarkedContent(PdfObject? k)
    {
        foreach (var (mcid, pgOverride) in CollectMcids(k))
        {
            var page = _reader!.ResolveDict(pgOverride) ?? _reader!.ResolveDict(_dict.Get("Pg"));
            if (page is null) continue;
            var img = FindImageInMarkedContent(page, mcid);
            if (img is not null) return img;
        }
        return null;
    }

    /// <summary>Collect (MCID, optional page) pairs reachable from a structure element's
    /// /K: a bare integer, an /MCR marked-content reference, or an array of either.</summary>
    private IEnumerable<(int mcid, PdfObject? pg)> CollectMcids(PdfObject? k)
    {
        switch (_reader!.Resolve(k))
        {
            case PdfInteger pi:
                yield return ((int)pi.Value, null);
                break;
            case PdfDictionary d when d.GetName("Type") == "MCR":
                yield return ((int)d.GetInt("MCID", -1), d.Get("Pg"));
                break;
            case PdfArray arr:
                foreach (var item in arr)
                    foreach (var pair in CollectMcids(item))
                        yield return pair;
                break;
        }
    }

    private PdfStream? FindImageInMarkedContent(PdfDictionary page, int mcid)
    {
        if (mcid < 0) return null;
        var content = GetPageContentText(page);
        if (content is null) return null;
        var properties = _reader!.ResolveDict(GetInheritedResources(page)?.Get("Properties"));

        // Track the open marked-content stack; an image painted while the target MCID is
        // open (anywhere on the stack, to allow nested marked content) is the figure's.
        var stack = new List<int>();
        foreach (System.Text.RegularExpressions.Match m in MarkedContentScanner.Matches(content))
        {
            if (m.Groups["bdc"].Success)
                stack.Add(ResolveBdcMcid(m.Groups["props"].Value, properties));
            else if (m.Groups["bmc"].Success)
                stack.Add(-1);
            else if (m.Groups["emc"].Success)
            {
                if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
            }
            else if (m.Groups["do"].Success)
            {
                if (!stack.Contains(mcid)) continue;
                var xobj = ResolveXObject(page, m.Groups["doname"].Value);
                if (xobj is not null && xobj.Dict.GetName("Subtype") == "Image")
                    return xobj;
            }
        }
        return null;
    }

    /// <summary>Read the MCID of a BDC operand: an inline <c>&lt;&lt;/MCID n&gt;&gt;</c>
    /// dictionary, or a named property list resolved through /Resources/Properties.</summary>
    private int ResolveBdcMcid(string props, PdfDictionary? properties)
    {
        props = props.Trim();
        if (props.StartsWith("<<", StringComparison.Ordinal))
        {
            var mm = System.Text.RegularExpressions.Regex.Match(props, @"/MCID\s+(\d+)");
            return mm.Success ? int.Parse(mm.Groups[1].Value) : -1;
        }
        if (props.StartsWith("/", StringComparison.Ordinal) && properties is not null)
        {
            var pd = _reader!.ResolveDict(properties.Get(props[1..]));
            return pd is not null ? (int)pd.GetInt("MCID", -1) : -1;
        }
        return -1;
    }

    private PdfStream? ResolveXObject(PdfDictionary page, string name)
    {
        var xobjects = _reader!.ResolveDict(GetInheritedResources(page)?.Get("XObject"));
        return _reader!.ResolveStream(xobjects?.Get(name));
    }

    /// <summary>The page's /Resources, walking up the /Pages tree when a page inherits
    /// them rather than carrying its own.</summary>
    private PdfDictionary? GetInheritedResources(PdfDictionary page)
    {
        var node = page;
        for (var depth = 0; node is not null && depth < 32; depth++)
        {
            var res = _reader!.ResolveDict(node.Get("Resources"));
            if (res is not null) return res;
            node = _reader!.ResolveDict(node.Get("Parent"));
        }
        return null;
    }

    private string? GetPageContentText(PdfDictionary page)
    {
        var contents = _reader!.Resolve(page.Get("Contents"));
        if (contents is PdfStream s)
            return Encoding.Latin1.GetString(_reader.DecodeStream(s));
        if (contents is PdfArray arr)
        {
            var sb = new StringBuilder();
            foreach (var item in arr)
                if (_reader.ResolveStream(item) is PdfStream cs)
                {
                    sb.Append(Encoding.Latin1.GetString(_reader.DecodeStream(cs)));
                    sb.Append('\n');
                }
            return sb.ToString();
        }
        return null;
    }
}

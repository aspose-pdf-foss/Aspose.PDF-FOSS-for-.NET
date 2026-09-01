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

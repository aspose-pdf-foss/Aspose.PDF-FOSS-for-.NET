using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

public sealed partial class PageCollection
{
    // A name tree or a page tree deeper than this is taken for a loop.
    private const int MaxNameTreeDepth = 64;

    /// <summary>The named destinations of one source document: where it keeps them, and what each one that an
    /// imported page uses is called in this document.</summary>
    private sealed class CarriedDestinations
    {
        public PdfDictionary? ByName;
        public PdfDictionary? Tree;
        public Dictionary<string, PdfObject>? TreeEntries;
        public readonly Dictionary<string, PdfObject> Carried = new(StringComparer.Ordinal);
        // The source's pages in order, for a destination that states its page by index.
        public List<PdfObject>? PageRefs;
    }

    /// <summary>A destination stating its page by its index in the source ([13 /XYZ ...], a form some writers
    /// use) re-stated with that page's reference, which the remap carries to the page's place here - the index
    /// would count this document's pages instead; a destination naming its page by reference is returned as is.</summary>
    private static PdfObject WithPageReference(CarriedDestinations carried, PdfReader reader, PdfObject destination)
    {
        var resolved = reader.Resolve(destination);
        if (resolved is PdfDictionary dict)
        {
            if (reader.Resolve(dict.Get("D")) is not PdfArray inner || IndexedPageByReference(carried, reader, inner) is not { } byRef)
                return destination;
            var copy = new PdfDictionary();
            foreach (var key in dict.Keys) copy.Set(key, key == "D" ? byRef : dict.Get(key)!);
            return copy;
        }
        return resolved is PdfArray array && IndexedPageByReference(carried, reader, array) is { } stated ? stated : destination;
    }

    private static PdfArray? IndexedPageByReference(CarriedDestinations carried, PdfReader reader, PdfArray dest)
    {
        if (dest.Count == 0 || dest[0] is not PdfInteger index) return null;
        carried.PageRefs ??= LeafPageRefs(reader);
        if (index.Value < 0 || index.Value >= carried.PageRefs.Count) return null;
        var copy = new PdfArray();
        copy.Add(carried.PageRefs[(int)index.Value]);
        for (var i = 1; i < dest.Count; i++) copy.Add(dest[i]);
        return copy;
    }

    private static List<PdfObject> LeafPageRefs(PdfReader reader)
    {
        var refs = new List<PdfObject>();
        if (reader.ResolveDict(reader.Catalog.Get("Pages")) is { } root) CollectLeafPages(root, reader, refs, 0);
        return refs;
    }

    private static void CollectLeafPages(PdfDictionary node, PdfReader reader, List<PdfObject> refs, int depth)
    {
        if (depth > MaxNameTreeDepth || reader.Resolve(node.Get("Kids")) is not PdfArray kids) return;
        foreach (var kid in kids)
        {
            if (reader.ResolveDict(kid) is not { } child) continue;
            if (child.ContainsKey("Kids")) CollectLeafPages(child, reader, refs, depth + 1);
            else refs.Add(kid);
        }
    }

    // Keyed weakly on the source reader, like the clone cache beside it.
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<PdfReader, CarriedDestinations> _carriedDestinations = new();

    // The destination names this document has, its own and the carried ones: read once, on the first carry.
    private HashSet<string>? _destinationNames;

    /// <summary>The source's named destinations, or null when it has none: a link on an imported page that
    /// names its target needs that target carried over, as nothing else of the source's catalog comes along.</summary>
    private CarriedDestinations? SourceDestinations(PdfReader sourceReader)
    {
        var carried = _carriedDestinations.GetValue(sourceReader, static reader => new CarriedDestinations
        {
            ByName = reader.ResolveDict(reader.Catalog.Get("Dests")),
            Tree = reader.ResolveDict(reader.ResolveDict(reader.Catalog.Get("Names"))?.Get("Dests")),
        });
        return carried.ByName is null && carried.Tree is null ? null : carried;
    }

    /// <summary>Whether <paramref name="key"/> of <paramref name="holder"/> is where a destination is named:
    /// the /Dest of an annotation or outline item, the /D of a GoTo action.</summary>
    private static bool NamesADestination(PdfDictionary holder, string key) =>
        key == "Dest" || (key == "D" && holder.GetName("S") == "GoTo");

    /// <summary>Carry the destination <paramref name="named"/> refers to in the source into this document and
    /// return the name it has here: the same one, or a new one when this document already uses that name for
    /// a destination of its own or of another source.</summary>
    private PdfObject CarryNamedDestination(ObjectRemapState ro, PdfObject named)
    {
        var carried = ro.destinations!;
        var cacheKey = named is PdfString text ? "S" + ByteText(text) : "N" + ((PdfName)named).Value;
        if (carried.Carried.TryGetValue(cacheKey, out var known)) return known;

        var here = named;
        if (FindSourceDestination(carried, ro.sourceReader, named) is { } destination)
        {
            var taken = _destinationNames ??= ReadDestinationNames();
            for (var copy = 2; !taken.Add(NameText(here)); copy++) here = WithSuffix(named, " (" + copy + ")");
            var name = here;
            ro.stack.Push((WithPageReference(carried, ro.sourceReader, destination), remapped => RegisterCarriedDestination(name, remapped)));
        }
        carried.Carried[cacheKey] = here;
        return here;
    }

    private static string ByteText(PdfString value) => Compat.Latin1.GetString(value.Value);

    private static string NameText(PdfObject named) => named is PdfString text ? ByteText(text) : ((PdfName)named).Value;

    private static PdfObject WithSuffix(PdfObject named, string suffix)
    {
        if (named is not PdfString text) return new PdfName(((PdfName)named).Value + suffix);
        var isUtf16 = text.Value.Length >= 2 && text.Value[0] == 0xFE && text.Value[1] == 0xFF;
        var tail = isUtf16 ? Encoding.BigEndianUnicode.GetBytes(suffix) : Compat.Latin1.GetBytes(suffix);
        var bytes = new byte[text.Value.Length + tail.Length];
        Buffer.BlockCopy(text.Value, 0, bytes, 0, text.Value.Length);
        Buffer.BlockCopy(tail, 0, bytes, text.Value.Length, tail.Length);
        return new PdfString(bytes);
    }

    /// <summary>A name is looked up in the catalog's /Dests dictionary, a string in the /Names /Dests tree
    /// (ISO 32000-1 12.3.2.3); a file that keeps only the other one is answered from it.</summary>
    private static PdfObject? FindSourceDestination(CarriedDestinations carried, PdfReader reader, PdfObject named)
    {
        var name = named is PdfString text ? text.ToText() : ((PdfName)named).Value;
        if (named is PdfName && carried.ByName?.Get(name) is { } direct) return direct;
        if (carried.Tree is not null)
        {
            if (carried.TreeEntries is null)
            {
                carried.TreeEntries = new Dictionary<string, PdfObject>(StringComparer.Ordinal);
                CollectTreeEntries(carried.Tree, reader, (key, value) => carried.TreeEntries[key.ToText()] = value, 0);
            }
            if (carried.TreeEntries.TryGetValue(name, out var entry)) return entry;
        }
        return carried.ByName?.Get(name);
    }

    private static void CollectTreeEntries(PdfDictionary node, PdfReader reader, Action<PdfString, PdfObject> take, int depth)
    {
        if (depth > MaxNameTreeDepth) return;
        if (reader.Resolve(node.Get("Names")) is PdfArray names)
        {
            for (var i = 0; i + 1 < names.Count; i += 2)
            {
                if (reader.Resolve(names[i]) is PdfString key) take(key, names[i + 1]);
            }
        }
        if (reader.Resolve(node.Get("Kids")) is PdfArray kids)
        {
            foreach (var kid in kids)
            {
                if (reader.ResolveDict(kid) is { } child) CollectTreeEntries(child, reader, take, depth + 1);
            }
        }
    }

    private HashSet<string> ReadDestinationNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (_reader.ResolveDict(_reader.Catalog.Get("Dests")) is { } dests)
        {
            foreach (var key in dests.Keys) names.Add(key);
        }
        if (_reader.ResolveDict(_reader.ResolveDict(_reader.Catalog.Get("Names"))?.Get("Dests")) is { } tree)
            CollectTreeEntries(tree, _reader, (key, _) => names.Add(ByteText(key)), 0);
        return names;
    }

    /// <summary>Enter a carried destination in this document's catalog: under /Dests for a name, in the
    /// /Names /Dests tree for a string.</summary>
    private void RegisterCarriedDestination(PdfObject named, PdfObject destination)
    {
        var catalog = _reader.Catalog;
        if (named is PdfName name)
        {
            var dests = _reader.ResolveDict(catalog.Get("Dests"));
            if (dests is null)
            {
                dests = new PdfDictionary();
                catalog.Set("Dests", dests);
            }
            dests.Set(name.Value, destination);
            return;
        }
        var names = _reader.ResolveDict(catalog.Get("Names"));
        if (names is null)
        {
            names = new PdfDictionary();
            catalog.Set("Names", names);
        }
        var tree = _reader.ResolveDict(names.Get("Dests"));
        if (tree is null)
        {
            tree = new PdfDictionary();
            names.Set("Dests", tree);
        }
        InsertIntoLeaf(SingleLeaf(tree), (PdfString)named, destination);
    }

    /// <summary>The tree as one leaf under its root, which is where a new key can go without re-balancing:
    /// the entries of its kids, already in key order, move up into the root's /Names.</summary>
    private PdfArray SingleLeaf(PdfDictionary tree)
    {
        if (!tree.ContainsKey("Kids") && _reader.Resolve(tree.Get("Names")) is PdfArray leaf) return leaf;
        var entries = new PdfArray();
        CollectTreeEntries(tree, _reader, (key, value) =>
        {
            entries.Add(key);
            entries.Add(value);
        }, 0);
        tree.Remove("Kids");
        tree.Remove("Limits");
        tree.Set("Names", entries);
        return entries;
    }

    /// <summary>Insert in the byte order of the keys, the order a name tree is searched in.</summary>
    private void InsertIntoLeaf(PdfArray leaf, PdfString key, PdfObject value)
    {
        var text = ByteText(key);
        int low = 0, high = leaf.Count / 2;
        while (low < high)
        {
            var middle = (low + high) / 2;
            var isAfter = _reader.Resolve(leaf[2 * middle]) is PdfString other
                && string.CompareOrdinal(ByteText(other), text) > 0;
            if (isAfter) high = middle;
            else low = middle + 1;
        }
        leaf.Insert(2 * low, key);
        leaf.Insert(2 * low + 1, value);
    }
}

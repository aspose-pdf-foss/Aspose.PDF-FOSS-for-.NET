using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfFileEditor
{
    /// <summary>Carry the inputs' named destinations into the output, so a link that names its target still
    /// has one: the catalog's /Dests dictionaries merge into one dictionary, the /Names /Dests trees into one
    /// tree under <paramref name="namesDict"/>. Where two inputs define the same name the first one stands.
    /// An input without named destinations costs two catalog lookups.</summary>
    private static void MergeNamedDestinations(ConcatenationState cc, PdfDictionary namesDict)
    {
        PdfDictionary? byName = null;
        SortedDictionary<string, (PdfString Key, PdfObject Value)>? byString = null;
        for (var ri = 0; ri < cc.inputReaders.Count; ri++)
        {
            var reader = cc.inputReaders[ri];
            var dests = reader.ResolveDict(reader.Catalog.Get("Dests"));
            var tree = reader.ResolveDict(reader.ResolveDict(reader.Catalog.Get("Names"))?.Get("Dests"));
            if (dests is null && tree is null) continue;
            // The input's pages are already written: its page references lead to them through the seed.
            var remap = ri < cc.inputOutlineSeeds.Count
                ? new Dictionary<int, int>(cc.inputOutlineSeeds[ri])
                : new Dictionary<int, int>();
            if (dests is not null)
            {
                byName ??= new PdfDictionary();
                foreach (var key in dests.Keys)
                {
                    if (!byName.ContainsKey(key) && dests.Get(key) is { } value)
                        byName.Set(key, RemapObject(value, reader, remap, cc.writer));
                }
            }
            if (tree is not null)
            {
                byString ??= new SortedDictionary<string, (PdfString, PdfObject)>(StringComparer.Ordinal);
                CollectDestinationTree(tree, reader, remap, cc.writer, byString, 0);
            }
        }

        if (byName is { Count: > 0 })
        {
            var destsObjNum = cc.writer.AllocateObjectNumber();
            cc.writer.WriteIndirectObject(destsObjNum, byName);
            cc.catalogDict.Set("Dests", new PdfIndirectRef(destsObjNum, 0));
        }
        if (byString is { Count: > 0 })
        {
            // One leaf, its keys in the byte order a name tree is searched in.
            var names = new PdfArray();
            foreach (var (key, value) in byString.Values)
            {
                names.Add(key);
                names.Add(value);
            }
            var leaf = new PdfDictionary();
            leaf.Set("Names", names);
            var leafObjNum = cc.writer.AllocateObjectNumber();
            cc.writer.WriteIndirectObject(leafObjNum, leaf);
            namesDict.Set("Dests", new PdfIndirectRef(leafObjNum, 0));
        }
    }

    private static void CollectDestinationTree(PdfDictionary node, PdfReader reader, Dictionary<int, int> remap,
        PdfWriter writer, SortedDictionary<string, (PdfString Key, PdfObject Value)> entries, int depth)
    {
        if (depth > MaxNameTreeDepth) return;
        if (reader.Resolve(node.Get("Names")) is PdfArray names)
        {
            for (var i = 0; i + 1 < names.Count; i += 2)
            {
                if (reader.Resolve(names[i]) is not PdfString key) continue;
                // One character per byte: the ordinal order of the text is the order of the bytes.
                var sortKey = Compat.Latin1.GetString(key.Value);
                if (!entries.ContainsKey(sortKey))
                    entries[sortKey] = (key, RemapObject(names[i + 1], reader, remap, writer));
            }
        }
        if (reader.Resolve(node.Get("Kids")) is PdfArray kids)
        {
            foreach (var kid in kids)
            {
                if (reader.ResolveDict(kid) is { } child)
                    CollectDestinationTree(child, reader, remap, writer, entries, depth + 1);
            }
        }
    }
}

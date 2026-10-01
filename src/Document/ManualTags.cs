using System.Collections.Generic;
using Aspose.Pdf.Core;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>The marked-content and object references callers attached to structure
    /// elements by hand since the last save (see <see cref="Tagged.ManualTagWiring"/>).</summary>
    internal List<Tagged.ManualTag> PendingManualTags { get; } = new();

    /// <summary>The marked content a table-of-contents page drew for an authored tagged document
    /// (its title, and each entry with the entry's link annotation), waiting for the structure
    /// wiring that follows the page layout (see <see cref="Tagged.TaggedTocWiring"/>).</summary>
    internal List<Tagged.TocTag> PendingTocTags { get; } = new();

    private Dictionary<Page, int>? _tocMcids;

    /// <summary>The next marked-content id for content this save draws on a TOC page.</summary>
    internal int NextTocMcid(Page page)
    {
        _tocMcids ??= new Dictionary<Page, int>(ReferenceEqualityComparer.Instance);
        var next = _tocMcids.TryGetValue(page, out var held) ? held : 0;
        _tocMcids[page] = next + 1;
        return next;
    }

    /// <summary>Every dictionary the file's cross-reference table resolves to, by the number it
    /// holds it under: the map that tells a loaded structure element from one made since.</summary>
    internal Dictionary<PdfDictionary, int> KnownDictionaryNumbers()
    {
        var known = new Dictionary<PdfDictionary, int>(ReferenceEqualityComparer.Instance);
        foreach (var entry in _reader.XRefTable.Entries.Values)
            if (_reader.Resolve(new PdfIndirectRef(entry.ObjectNumber, 0)) is PdfDictionary dict)
                known.TryAdd(dict, entry.ObjectNumber);
        // Objects this save has already numbered (an earlier wiring pass, an annotation made indirect).
        foreach (var (objNum, obj) in _newObjects)
            if (obj is PdfDictionary dict)
                known.TryAdd(dict, objNum);
        return known;
    }
}

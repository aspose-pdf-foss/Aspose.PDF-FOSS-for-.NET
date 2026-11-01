using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfFileEditor
{
    // A name tree deeper than this is taken for a loop.
    private const int MaxNameTreeDepth = 64;

    // The entries of an outline item that style its title: colour and the bold/italic flags.
    private static readonly string[] OutlineStyleKeys = ["C", "F"];

    /// <summary>What copying one source document's bookmarks needs, read from the source once.</summary>
    private sealed class OutlineCopyState
    {
        public Document source = null!;
        public Document dest = null!;
        public int pageOffset;
        public Dictionary<PdfDictionary, int> pageNumbers = null!;
        // The source's named destinations by name, read on the first bookmark that names one.
        public Dictionary<string, PdfObject>? named;
    }

    /// <summary>The bookmarks of a source document whose pages were just appended to
    /// <paramref name="dest"/> after its first <paramref name="pageOffset"/> pages: each is added to the
    /// target's outline with its title, colour, style and open state, and opens the copy of its page.</summary>
    private static void AppendOutlines(Document source, Document dest, int pageOffset)
    {
        // Asked for its outline, a document without one is given an empty one: the source is left as it is.
        if (!source.Catalog.ContainsKey("Outlines") || source.Outlines.Count == 0) return;
        var oc = new OutlineCopyState
        {
            source = source,
            dest = dest,
            pageOffset = pageOffset,
            // One pass over the source pages serves every bookmark of the source.
            pageNumbers = new Dictionary<PdfDictionary, int>(ReferenceEqualityComparer.Instance),
        };
        foreach (var page in source.Pages) oc.pageNumbers[page.Dict] = page.Number;
        foreach (var item in source.Outlines) dest.Outlines.Add(CopyOutline(oc, item));
    }

    private static OutlineItemCollection CopyOutline(OutlineCopyState oc, OutlineItemCollection item)
    {
        var copy = new OutlineItemCollection(oc.dest.Outlines) { Title = item.Title };
        foreach (var key in OutlineStyleKeys)
        {
            if (DirectCopy(Resolved(item, item.Dict.Get(key))) is { } value) copy.Dict.Set(key, value);
        }
        if (LocalDestination(oc, item) is { Count: > 0 } destination
            && SourcePageNumber(oc, item, destination) is { } number
            && oc.pageOffset + number <= oc.dest.Pages.Count
            && DirectCopy(destination) is PdfArray target)
        {
            var page = oc.dest.Pages[oc.pageOffset + number];
            target.ReplaceAt(0, new PdfInteger(page.Number - 1));
            copy.Dict.Set("Dest", target);
            // The save swaps the index for a reference to the page, wherever the page is by then.
            oc.dest.PendingDestinationPages.Add((target, page));
        }
        foreach (var child in item) copy.Add(CopyOutline(oc, child));
        if (item.Dict.ContainsKey("Count")) copy.Open = item.Open;
        return copy;
    }

    private static PdfObject? Resolved(OutlineItem item, PdfObject? value) =>
        item.Reader is { } reader ? reader.Resolve(value) : value;

    /// <summary>The destination array an item opens in its own document: its /Dest, or the /D of its GoTo
    /// action, a named one looked up. Null for an item that goes elsewhere or nowhere.</summary>
    private static PdfArray? LocalDestination(OutlineCopyState oc, OutlineItemCollection item)
    {
        var value = Resolved(item, item.Dict.Get("Dest"));
        if (value is null && Resolved(item, item.Dict.Get("A")) is PdfDictionary action
            && action.GetName("S") == "GoTo")
            value = Resolved(item, action.Get("D"));
        if (item.Reader is { } reader && value is PdfString or PdfName)
            value = NamedDestination(oc, reader, value is PdfString text ? text.ToText() : ((PdfName)value).Value);
        if (value is PdfDictionary wrapper) value = Resolved(item, wrapper.Get("D"));
        return value as PdfArray;
    }

    private static PdfObject? NamedDestination(OutlineCopyState oc, PdfReader reader, string name)
    {
        if (oc.named is null)
        {
            oc.named = new Dictionary<string, PdfObject>(StringComparer.Ordinal);
            if (reader.ResolveDict(reader.Catalog.Get("Dests")) is { } dests)
            {
                foreach (var key in dests.Keys) oc.named[key] = dests.Get(key)!;
            }
            if (reader.ResolveDict(reader.ResolveDict(reader.Catalog.Get("Names"))?.Get("Dests")) is { } tree)
                ReadDestinationTree(tree, reader, oc.named, 0);
        }
        return oc.named.TryGetValue(name, out var value) ? reader.Resolve(value) : null;
    }

    private static void ReadDestinationTree(PdfDictionary node, PdfReader reader, Dictionary<string, PdfObject> entries,
        int depth)
    {
        if (depth > MaxNameTreeDepth) return;
        if (reader.Resolve(node.Get("Names")) is PdfArray names)
        {
            for (var i = 0; i + 1 < names.Count; i += 2)
            {
                if (reader.Resolve(names[i]) is PdfString key) entries[key.ToText()] = names[i + 1];
            }
        }
        if (reader.Resolve(node.Get("Kids")) is PdfArray kids)
        {
            foreach (var kid in kids)
            {
                if (reader.ResolveDict(kid) is { } child) ReadDestinationTree(child, reader, entries, depth + 1);
            }
        }
    }

    /// <summary>The 1-based number, in the source, of the page a destination array opens: by the page object
    /// it references, by the page a destination made in memory is waiting for, or by its page index.</summary>
    private static int? SourcePageNumber(OutlineCopyState oc, OutlineItem item, PdfArray destination)
    {
        if (destination[0] is PdfIndirectRef && Resolved(item, destination[0]) is PdfDictionary pageDict)
            return oc.pageNumbers.TryGetValue(pageDict, out var number) ? number : null;
        foreach (var (pending, page) in oc.source.PendingDestinationPages)
        {
            if (ReferenceEquals(pending, destination))
                return oc.pageNumbers.TryGetValue(page.Dict, out var number) ? number : null;
        }
        if (destination[0] is PdfInteger index && index.Value >= 0 && index.Value < oc.source.Pages.Count)
            return (int)index.Value + 1;
        return null;
    }

    /// <summary>A copy of a value made of numbers, names, strings and arrays of them, with nothing shared
    /// with the source document. Null for anything else.</summary>
    private static PdfObject? DirectCopy(PdfObject? value)
    {
        switch (value)
        {
            case PdfInteger or PdfReal or PdfName or PdfString or PdfBoolean or PdfNull:
                return value;
            case PdfArray array:
                var copy = new PdfArray();
                foreach (var element in array) copy.Add(DirectCopy(element) ?? PdfNull.Instance);
                return copy;
            default:
                return null;
        }
    }
}

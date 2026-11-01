using Aspose.Pdf.Core;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>Set once a named destination has been written through this document.</summary>
    internal bool NamedDestinationsTouched { get; set; }

    private const int ActionChainLimit = 16;

    /// <summary>
    /// A destination given by page NUMBER (a go-to action on page 3, an anchor of an imported
    /// HTML page, a contents entry) holds a page index until the document is saved; ISO 32000-1
    /// 12.3.2.2 wants the page object there, and a reader that follows the standard goes nowhere
    /// on an index. This finds those arrays where this session could have put them - the
    /// annotations of pages whose annotation list was opened, the outline once it was opened,
    /// the open action and the named destinations once one was written - and adds each to
    /// <paramref name="pending"/> with the page its index names. Pages nobody touched are not read.
    /// </summary>
    private void CollectIndexedDestinations(List<(PdfArray Destination, Page Page)> pending)
    {
        var known = new HashSet<PdfArray>(ReferenceEqualityComparer.Instance);
        foreach (var (destination, _) in pending) known.Add(destination);
        var pageCount = Pages.Count;

        void Take(PdfObject? value)
        {
            if (_reader.Resolve(value) is not PdfArray { Count: > 1 } array) return;
            if (array[0] is not PdfInteger index || array[1] is not PdfName) return;
            if (index.Value < 0 || index.Value >= pageCount || !known.Add(array)) return;
            pending.Add((array, Pages[(int)index.Value + 1]));
        }

        void TakeAction(PdfObject? value)
        {
            var action = _reader.ResolveDict(value);
            for (var depth = 0; action is not null && depth < ActionChainLimit; depth++)
            {
                if (action.GetName("S") == "GoTo") Take(action.Get("D"));
                action = _reader.ResolveDict(action.Get("Next"));
            }
        }

        void TakeHolder(PdfDictionary holder)
        {
            Take(holder.Get("Dest"));
            TakeAction(holder.Get("A"));
        }

        var catalog = _reader.Catalog;
        Take(catalog.Get("OpenAction"));
        TakeAction(catalog.Get("OpenAction"));

        foreach (var page in Pages)
        {
            if (!page.HasOpenedAnnotations || _reader.Resolve(page.Dict.Get("Annots")) is not PdfArray annots) continue;
            foreach (var entry in annots)
                if (_reader.ResolveDict(entry) is { } annotation) TakeHolder(annotation);
        }

        if (_outlines is not null) CollectOutlineDestinations(TakeHolder);
        if (NamedDestinationsTouched) CollectNamedDestinations(Take);
    }

    private void CollectOutlineDestinations(Action<PdfDictionary> takeHolder)
    {
        var root = _reader.ResolveDict(_reader.Catalog.Get("Outlines"));
        var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<PdfDictionary>();
        if (_reader.ResolveDict(root?.Get("First")) is { } first) stack.Push(first);
        while (stack.Count > 0)
        {
            var item = stack.Pop();
            if (!visited.Add(item)) continue;
            takeHolder(item);
            if (_reader.ResolveDict(item.Get("First")) is { } child) stack.Push(child);
            if (_reader.ResolveDict(item.Get("Next")) is { } next) stack.Push(next);
        }
    }

    private void CollectNamedDestinations(Action<PdfObject?> take)
    {
        void TakeEntry(PdfObject? value)
        {
            var resolved = _reader.Resolve(value);
            take(resolved is PdfDictionary holder ? holder.Get("D") : resolved);
        }

        var catalog = _reader.Catalog;
        if (_reader.ResolveDict(catalog.Get("Dests")) is { } dests)
            foreach (var key in dests.Keys.ToList()) TakeEntry(dests.Get(key));

        // The name tree this library writes is a single /Names array on the tree's root.
        var names = _reader.ResolveDict(catalog.Get("Names"));
        if (_reader.ResolveDict(names?.Get("Dests")) is { } tree
            && _reader.Resolve(tree.Get("Names")) is PdfArray pairs)
            for (var i = 1; i < pairs.Count; i += 2) TakeEntry(pairs[i]);
    }
}

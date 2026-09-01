using System;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

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

using System.Collections.Generic;
using Aspose.Pdf.Core;
using LS = Aspose.Pdf.LogicalStructure;

namespace Aspose.Pdf.Tagged;

/// <summary>A marked-content or object reference a caller attached to a structure element
/// through <see cref="LS.StructureElement.Tag(Aspose.Pdf.Operators.BDC)"/> or
/// <see cref="LS.StructureElement.Tag(Aspose.Pdf.Annotations.Annotation)"/>: what the file has to
/// say about it is decided on save, once pages and objects have their numbers.</summary>
internal sealed record ManualTag(LS.StructureElement Element, PdfDictionary Kid, Page? Page, int Mcid,
    PdfDictionary? Annotation);

/// <summary>Writes the file side of the references callers tag by hand: the tagged elements
/// (and any element added to a loaded tree since) become indirect objects with /P, the
/// references get their /Pg and /Obj, and the parent tree gains an entry for each - merged
/// into what the document already carries, since a loaded tree keeps its numbers.</summary>
internal static class ManualTagWiring
{
    public static void Wire(Document doc, LS.StructureElement root)
    {
        var tags = doc.PendingManualTags;
        if (tags.Count == 0) return;
        var structRoot = StructureWiring.EnsureIndirectStructTreeRoot(doc);
        if (structRoot is null) return;

        var refs = NumberNewElements(doc, root, structRoot);
        var nums = ExistingParentTree(doc, structRoot, out var nextKey);
        foreach (var tag in tags)
        {
            if (!refs.TryGetValue(tag.Element._dict, out var elementRef)) continue;
            if (tag.Page is { } page) PlaceOnPage(doc, tag.Kid, page);
            if (tag.Annotation is { } annot)
                nextKey = LinkObject(doc, tag, annot, elementRef, nums, nextKey);
            else if (tag.Page is { } marked)
                nextKey = LinkMarkedContent(doc, marked, tag.Mcid, elementRef, nums, nextKey);
        }
        var numsArray = new PdfArray();
        foreach (var (key, value) in nums)
        {
            numsArray.Add(new PdfInteger(key));
            numsArray.Add(value);
        }
        var parentTree = new PdfDictionary();
        parentTree.Set("Nums", numsArray);
        structRoot.Set("ParentTree", parentTree);
        structRoot.Set("ParentTreeNextKey", new PdfInteger(nextKey));
        tags.Clear();
    }

    /// <summary>Every element of the tree as its reference: a loaded element under the number
    /// the file gave it, an element added since under a new one, with /P set and its direct
    /// entry in the parent's /K replaced by the reference. Numbers are never reassigned.</summary>
    private static Dictionary<PdfDictionary, PdfIndirectRef> NumberNewElements(
        Document doc, LS.StructureElement root, PdfDictionary structRoot)
    {
        var known = doc.KnownDictionaryNumbers();
        var refs = new Dictionary<PdfDictionary, PdfIndirectRef>(ReferenceEqualityComparer.Instance);
        var next = 0;

        void Number(LS.StructureElement el)
        {
            if (IsReference(el)) return;
            if (known.TryGetValue(el._dict, out var num))
                refs[el._dict] = new PdfIndirectRef(num, 0);
            else if (!refs.ContainsKey(el._dict))
            {
                var objNum = next > 0 ? next : doc.AllocateObjectNumber();
                next = objNum + 1;
                doc.AddNewObject(objNum, el._dict, registerOverlay: true);
                refs[el._dict] = new PdfIndirectRef(objNum, 0);
            }
            foreach (var child in el.ChildElements) Number(child);
        }
        Number(root);

        void Link(LS.StructureElement el)
        {
            var k = doc.Reader.Resolve(el._dict.Get("K")) as PdfArray;
            foreach (var child in el.ChildElements)
            {
                if (IsReference(child)) continue;
                child._dict.Set("P", refs[el._dict]);
                if (k is not null)
                    for (var i = 0; i < k.Count; i++)
                        if (k[i] is PdfDictionary direct && ReferenceEquals(direct, child._dict))
                            k.ReplaceAt(i, refs[child._dict]);
                Link(child);
            }
        }
        if (doc.Catalog.Get("StructTreeRoot") is PdfIndirectRef structRootRef)
            root._dict.Set("P", structRootRef);
        Link(root);
        if (structRoot.Get("K") is PdfArray rootK)
        {
            for (var i = 0; i < rootK.Count; i++)
                if (rootK[i] is PdfDictionary kd && refs.TryGetValue(kd, out var r))
                    rootK.ReplaceAt(i, r);
        }
        else if (structRoot.Get("K") is PdfDictionary single && refs.TryGetValue(single, out var singleRef))
            structRoot.Set("K", singleRef);
        return refs;
    }

    private static bool IsReference(LS.StructureElement el) =>
        el is LS.MCRElement or LS.OBJRElement || el._dict.GetName("Type") is "MCR" or "OBJR";

    /// <summary>The document's parent tree as the map its number tree spells, leaves of a
    /// /Kids tree included, and the next key it hands out.</summary>
    private static SortedDictionary<int, PdfObject> ExistingParentTree(Document doc, PdfDictionary structRoot,
        out int nextKey)
    {
        var nums = new SortedDictionary<int, PdfObject>();
        void Read(PdfDictionary? node)
        {
            if (node is null) return;
            if (doc.Reader.Resolve(node.Get("Nums")) is PdfArray pairs)
                for (var i = 0; i + 1 < pairs.Count; i += 2)
                    if (doc.Reader.Resolve(pairs[i]) is PdfInteger key)
                        nums[(int)key.Value] = pairs[i + 1];
            if (doc.Reader.Resolve(node.Get("Kids")) is PdfArray kids)
                foreach (var kid in kids)
                    Read(doc.Reader.ResolveDict(kid));
        }
        Read(doc.Reader.ResolveDict(structRoot.Get("ParentTree")));
        nextKey = 0;
        foreach (var key in nums.Keys) nextKey = System.Math.Max(nextKey, key + 1);
        if (doc.Reader.Resolve(structRoot.Get("ParentTreeNextKey")) is PdfInteger declared)
            nextKey = System.Math.Max(nextKey, (int)declared.Value);
        return nums;
    }

    /// <summary>/Pg on a reference: the page's number when the file already has one, else
    /// stamped by the save once the page is numbered.</summary>
    private static void PlaceOnPage(Document doc, PdfDictionary kid, Page page)
    {
        var num = doc.FindObjectNumber(page.Dict);
        if (num > 0) kid.Set("Pg", new PdfIndirectRef(num, 0));
        else doc.PendingStructPgFixups.Add((kid, page));
    }

    /// <summary>The page's parent-tree array, under its /StructParents key (given one when it
    /// has none), points the marked-content id at the element.</summary>
    private static int LinkMarkedContent(Document doc, Page page, int mcid, PdfIndirectRef elementRef,
        SortedDictionary<int, PdfObject> nums, int nextKey)
    {
        int key;
        if (doc.Reader.Resolve(page.Dict.Get("StructParents")) is PdfInteger existing)
            key = (int)existing.Value;
        else
        {
            key = nextKey++;
            page.Dict.Set("StructParents", new PdfInteger(key));
        }
        var arr = nums.TryGetValue(key, out var held) ? doc.Reader.Resolve(held) as PdfArray : null;
        if (arr is null)
        {
            arr = new PdfArray();
            nums[key] = arr;
        }
        while (arr.Count <= mcid) arr.Add(PdfNull.Instance);
        arr.ReplaceAt(mcid, elementRef);
        return nextKey;
    }

    /// <summary>The annotation becomes an indirect object the reference names under /Obj, and
    /// its /StructParent key points the parent tree at the element.</summary>
    private static int LinkObject(Document doc, ManualTag tag, PdfDictionary annot, PdfIndirectRef elementRef,
        SortedDictionary<int, PdfObject> nums, int nextKey)
    {
        var num = doc.FindObjectNumber(annot);
        if (num <= 0)
        {
            num = doc.AllocateObjectNumber();
            doc.AddNewObject(num, annot, registerOverlay: true);
            if (tag.Page is { } page && doc.Reader.Resolve(page.Dict.Get("Annots")) is PdfArray annots)
                for (var i = 0; i < annots.Count; i++)
                    if (annots[i] is PdfDictionary direct && ReferenceEquals(direct, annot))
                        annots.ReplaceAt(i, new PdfIndirectRef(num, 0));
        }
        tag.Kid.Set("Obj", new PdfIndirectRef(num, 0));
        int key;
        if (annot.Get("StructParent") is PdfInteger existing)
            key = (int)existing.Value;
        else
        {
            key = nextKey++;
            annot.Set("StructParent", new PdfInteger(key));
        }
        nums[key] = elementRef;
        return nextKey;
    }
}

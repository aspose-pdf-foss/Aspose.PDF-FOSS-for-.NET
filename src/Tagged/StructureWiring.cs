using System.Collections.Generic;
using Aspose.Pdf.Core;
using LS = Aspose.Pdf.LogicalStructure;

namespace Aspose.Pdf.Tagged;

/// <summary>Turns an in-memory structure tree into its file form: every structure element
/// becomes an indirect object with /P naming its parent, and /K arrays reference their child
/// elements indirectly. Marked-content and object references (MCR, OBJR) stay direct
/// dictionaries inside their element's /K. Shared by the tagged-content renderer and the
/// auto-tagger.</summary>
internal static class StructureWiring
{
    /// <summary>The /StructTreeRoot dictionary, made an indirect object of the catalog when
    /// it is still inline (a document root's /P must reference it).</summary>
    public static PdfDictionary? EnsureIndirectStructTreeRoot(Document doc)
    {
        var entry = doc.Catalog.Get("StructTreeRoot");
        if (entry is PdfIndirectRef) return doc.Reader.ResolveDict(entry);
        if (entry is not PdfDictionary dict) return null;
        var num = doc.AllocateObjectNumber();
        doc.AddNewObject(num, dict, registerOverlay: true);
        doc.Catalog.Set("StructTreeRoot", new PdfIndirectRef(num, 0));
        return dict;
    }

    /// <summary>Number every element of the tree under <paramref name="root"/> and link it: /P
    /// to its parent (the root's to the StructTreeRoot), child entries of /K and of the
    /// StructTreeRoot's /K replaced by indirect references. The elements stay resolvable in
    /// memory: a validation of this same document walks the tree through the reader, and an
    /// element it cannot resolve is an element it never sees. Returns each element's reference.</summary>
    public static Dictionary<PdfDictionary, PdfIndirectRef> NumberAndLink(
        Document doc, LS.StructureElement root, PdfDictionary structRootDict)
    {
        var refs = new Dictionary<PdfDictionary, PdfIndirectRef>(ReferenceEqualityComparer.Instance);
        // One allocation, then numbers counting on from it: allocating looks over every object,
        // once per element it made numbering a tree of many thousand cells slow.
        var next = 0;
        void NumberTree(LS.StructureElement el)
        {
            if (IsReference(el)) return;
            if (!refs.ContainsKey(el._dict))
            {
                var objNum = next > 0 ? next : doc.AllocateObjectNumber();
                next = objNum + 1;
                doc.AddNewObject(objNum, el._dict, registerOverlay: true);
                refs[el._dict] = new PdfIndirectRef(objNum, 0);
            }
            foreach (var child in el.ChildElements) NumberTree(child);
        }
        NumberTree(root);

        void LinkTree(LS.StructureElement el)
        {
            foreach (var child in el.ChildElements)
            {
                if (IsReference(child)) continue;
                child._dict.Set("P", refs[el._dict]);
                LinkTree(child);
            }
            if (el._dict.Get("K") is PdfArray k)
            {
                for (var i = 0; i < k.Count; i++)
                    if (k[i] is PdfDictionary kd && refs.TryGetValue(kd, out var r))
                        k.ReplaceAt(i, r);
            }
            // /Ref associations recorded through AddRef (PDF 32000 §14.7.4.3):
            // written as an array of the referenced elements' indirect refs.
            if (el._referencedElements is { Count: > 0 } targets)
            {
                var refArr = new PdfArray();
                foreach (var target in targets)
                    if (refs.TryGetValue(target._dict, out var tr))
                        refArr.Add(tr);
                if (refArr.Count > 0) el._dict.Set("Ref", refArr);
            }
        }
        if (doc.Catalog.Get("StructTreeRoot") is PdfIndirectRef structRootRef)
            root._dict.Set("P", structRootRef);
        LinkTree(root);
        if (structRootDict.Get("K") is PdfArray rootK)
        {
            for (var i = 0; i < rootK.Count; i++)
                if (rootK[i] is PdfDictionary kd && refs.TryGetValue(kd, out var r))
                    rootK.ReplaceAt(i, r);
        }
        return refs;
    }

    /// <summary>A marked-content or object reference: part of its element's /K, not an
    /// element of its own.</summary>
    private static bool IsReference(LS.StructureElement el) =>
        el is LS.MCRElement or LS.OBJRElement || el._dict.GetName("Type") is "MCR" or "OBJR";
}

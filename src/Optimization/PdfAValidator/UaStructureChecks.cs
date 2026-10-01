using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Optimization;

internal static partial class PdfAValidator
{
    /// <summary>What one structure element claims: the marked-content id it owns, and the
    /// page it owns it on.</summary>
    private readonly record struct MarkedContentClaim(int Mcid, int ElementObject, PdfDictionary? Page);

    /// <summary>One structure element as the tree walk reached it: its role-mapped type, the
    /// type of the element holding it, the page it belongs to, and the object number of the
    /// reference that reached it (zero when it is a direct dictionary).</summary>
    private readonly record struct StructureElementView(
        PdfDictionary Element, int ObjectNumber, string? Type, string? ParentType, PdfDictionary? Page);

    /// <summary>A structure tree read once: every element the walk reached, every
    /// marked-content id those elements claim, and what the /ParentTree rows say.</summary>
    private sealed class StructureTreeView
    {
        public List<StructureElementView> Elements { get; } = [];
        public List<MarkedContentClaim> Claims { get; } = [];

        /// <summary>Object numbers the rows name at all. An element the rows never name owns
        /// no page content of its own.</summary>
        public HashSet<int> ContentOwners { get; } = [];

        /// <summary>The (element, marked-content id) pairs the rows name.</summary>
        public HashSet<(int Element, int Mcid)> OwnedIds { get; } = [];
    }

    /// <summary>
    /// PDF/UA-1 clause 6.2: the structural parent tree must agree with the structure
    /// elements. A row of /ParentTree lists, per marked-content id, the element that owns
    /// that content, and an element states the ids it owns in its /K. When an element claims
    /// an id that no row hands to it - two elements claiming one id, or an id no row covers
    /// - a reader following the tree lands somewhere else and the mapping is unusable.
    /// </summary>
    /// <remarks>The rows are searched as a whole rather than page by page: content inside a
    /// form XObject is keyed by that XObject's own /StructParents, not the page's, so an
    /// element that draws into one is named by a row the page never mentions. Reported once
    /// per page, against the page object - the whole mapping is suspect once any entry
    /// disagrees, and listing every entry says nothing more.</remarks>
    private static void CheckUaParentTree(Document document, StructureTreeView tree, UaReport report)
    {
        if (tree.OwnedIds.Count == 0) return;
        var flagged = new HashSet<PdfDictionary>();
        foreach (var claim in tree.Claims)
        {
            if (claim.Page is null || tree.OwnedIds.Contains((claim.ElementObject, claim.Mcid))) continue;
            if (!flagged.Add(claim.Page)) continue;
            var page = FindPage(document, claim.Page);
            if (page is null) continue;
            var pageObject = document.FindObjectNumber(claim.Page);
            report.Add(UaProblems.ParentTreeInconsistentEntry, "UaParentTree",
                "Structural parent tree: Inconsistent entry found",
                page.Number, pageObject < 0 ? null : pageObject.ToString());
        }
    }

    /// <summary>Structure types that describe a run inside a block rather than a block of
    /// their own.</summary>
    private static readonly HashSet<string> UaInlineStructureTypes =
        new(StringComparer.Ordinal) { "Span", "Quote", "Code", "Note", "Figure" };

    /// <summary>Structure types that group other elements and are expected to hold blocks.</summary>
    private static readonly HashSet<string> UaGroupingStructureTypes = new(StringComparer.Ordinal)
    {
        "Document", "Part", "Art", "Sect", "Div", "BlockQuote", "Caption",
        "TOC", "TOCI", "Index", "NonStruct", "Private",
    };

    /// <summary>
    /// PDF/UA-1 clause 7.1: an inline-level structure element sitting directly inside a
    /// grouping element is possibly inappropriate - the grouping element is meant to hold
    /// blocks, and a bare run inside one usually means a block was never tagged. Only
    /// elements that own page content are examined; an element the parent tree never names
    /// describes nothing a reader reaches.
    /// </summary>
    /// <remarks>A figure is reported in the Graphics section whatever layout attributes it
    /// carries, and additionally in General when it states no /BBox - a figure without one
    /// claims no place on the page. Every other inline type is reported in General, again
    /// only without a /BBox. An element placed as a block has declared itself one and is
    /// exempt from both.</remarks>
    private static void CheckUaInlineStructureElements(Document document, StructureTreeView tree,
        UaReport report)
    {
        var pageNumbers = new Dictionary<PdfDictionary, int>();
        foreach (var page in document.Pages) pageNumbers[page.Dict] = page.Number;

        foreach (var view in tree.Elements)
        {
            if (view.Type is not { } type || !UaInlineStructureTypes.Contains(type)) continue;
            if (view.ParentType is not { } parent || !UaGroupingStructureTypes.Contains(parent)) continue;
            if (!tree.ContentOwners.Contains(view.ObjectNumber)) continue;

            var (placedAsBlock, hasBoundingBox) = ReadLayoutAttributes(document.Reader, view.Element);
            if (placedAsBlock) continue;

            var description = $"Possibly inappropriate use of a '{type}' structure element";
            var pageNumber = view.Page is not null && pageNumbers.TryGetValue(view.Page, out var n)
                ? n
                : (int?)null;
            var objectId = view.ObjectNumber == 0 ? null : view.ObjectNumber.ToString();
            if (type == "Figure")
                report.Add(UaProblems.InappropriateFigureElement, "UaInlineStructure",
                    description, pageNumber, objectId);
            if (!hasBoundingBox)
                report.Add(UaProblems.InappropriateStructureElement, "UaInlineStructure",
                    description, pageNumber, objectId);
        }
    }

    /// <summary>The two layout attributes this check reads out of an element's /A: whether it
    /// is placed as a block, and whether it states a bounding box.</summary>
    private static (bool PlacedAsBlock, bool HasBoundingBox) ReadLayoutAttributes(
        PdfReader reader, PdfDictionary element)
    {
        var attributes = element.Get("A");
        var placedAsBlock = false;
        var hasBoundingBox = false;
        foreach (var entry in reader.Resolve(attributes) is PdfArray array ? array : attributes is null ? [] : [attributes])
        {
            if (reader.ResolveDict(entry) is not { } attribute) continue;
            // The values are resolved: an attribute dictionary may hold its /Placement as
            // an indirect reference, and reading the entry directly would miss it.
            if (reader.Resolve(attribute.Get("Placement")) is PdfName { Value: "Block" })
                placedAsBlock = true;
            if (attribute.Get("BBox") is not null) hasBoundingBox = true;
        }
        return (placedAsBlock, hasBoundingBox);
    }

    /// <summary>The page whose dictionary is this one, or null when it belongs to no page of
    /// this document.</summary>
    private static Page? FindPage(Document document, PdfDictionary pageDict)
    {
        foreach (var page in document.Pages)
            if (ReferenceEquals(page.Dict, pageDict)) return page;
        return null;
    }

    /// <summary>Read the structure tree once: the elements, the marked-content ids they
    /// claim, and what the /ParentTree rows say about both.</summary>
    private static StructureTreeView ReadStructureTree(Document document)
    {
        var reader = document.Reader;
        var tree = new StructureTreeView();
        var structRoot = reader.ResolveDict(document.Catalog.Get("StructTreeRoot"));
        if (structRoot is null) return tree;

        foreach (var (_, value) in NumberTree.Entries(reader.ResolveDict(structRoot.Get("ParentTree")), reader))
        {
            if (reader.Resolve(value) is not PdfArray row) continue;
            for (var i = 0; i < row.Count; i++)
                if (row[i] is PdfIndirectRef owner)
                {
                    tree.ContentOwners.Add(owner.ObjectNumber);
                    tree.OwnedIds.Add((owner.ObjectNumber, i));
                }
        }

        var roleMap = reader.ResolveDict(structRoot.Get("RoleMap"));
        var visited = new HashSet<PdfDictionary>();

        void Walk(PdfObject? node, string? parentType, PdfDictionary? inheritedPage, int depth)
        {
            if (depth > 64 || reader.ResolveDict(node) is not { } element) return;
            if (!visited.Add(element)) return;
            var objectNumber = (node as PdfIndirectRef)?.ObjectNumber ?? 0;
            var page = reader.ResolveDict(element.Get("Pg")) ?? inheritedPage;
            var type = StandardStructureType(roleMap, element.GetName("S"));
            if (type is not null)
                tree.Elements.Add(new StructureElementView(element, objectNumber, type, parentType, page));

            // The kids are read UNRESOLVED: an element is identified by the object number of
            // the reference that reaches it, and resolving a lone /K reference here would
            // throw that number away.
            var kids = element.Get("K");
            foreach (var kid in reader.Resolve(kids) is PdfArray array ? array : kids is null ? [] : [kids])
            {
                switch (kid)
                {
                    // A bare integer names a marked-content id on the element's own page.
                    case PdfInteger mcid:
                        tree.Claims.Add(new MarkedContentClaim((int)mcid.Value, objectNumber, page));
                        continue;
                    case null:
                        continue;
                }
                var kidDict = reader.ResolveDict(kid);
                switch (kidDict?.GetName("Type"))
                {
                    case "MCR":
                        if (reader.Resolve(kidDict.Get("MCID")) is PdfInteger m)
                            tree.Claims.Add(new MarkedContentClaim((int)m.Value, objectNumber,
                                reader.ResolveDict(kidDict.Get("Pg")) ?? page));
                        continue;
                    // An object reference points at an annotation, not at page content.
                    case "OBJR":
                        continue;
                    default:
                        Walk(kid, type ?? parentType, page, depth + 1);
                        continue;
                }
            }
        }

        Walk(structRoot, null, null, 0);
        return tree;
    }

    /// <summary>Resolve a structure type through the role map to the standard type it stands
    /// for; a type the map does not mention is returned as it is.</summary>
    private static string? StandardStructureType(PdfDictionary? roleMap, string? type)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (type is not null && seen.Add(type) && roleMap?.GetName(type) is { } mapped)
            type = mapped;
        return type;
    }
}

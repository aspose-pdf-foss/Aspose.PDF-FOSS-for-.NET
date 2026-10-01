using System.Text;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Tagged;

/// <summary>
/// Builds a logical structure tree for a tagged PDF document.
/// Creates the /StructTreeRoot, /MarkInfo, and structure elements
/// needed for PDF accessibility (PDF32000 §14.7, §14.8).
/// Structure elements are written as indirect objects to avoid circular
/// references during serialization (child→parent→child loops).
/// Call <see cref="BuildParentTree"/> before saving to finalize the tree.
/// </summary>
public sealed partial class StructureTreeBuilder
{
    private readonly Document _document;
    private readonly List<StructureElementBuilder> _rootElements = [];
    private readonly Dictionary<string, string> _roleMappings = new(StringComparer.Ordinal);

    /// <summary>
    /// The next marked-content id for each page, and the page's own key in the
    /// number tree.
    ///
    /// ⚠⚠ Marked-content ids are PER PAGE, not per document (PDF 32000-1
    /// §14.7.4.2): a reader finds the element that owns a piece of content by
    /// looking the page's /StructParents up in /ParentTree and then indexing
    /// that entry's array BY the id. A document-wide counter makes both of
    /// those wrong the moment a second page is tagged.
    /// </summary>
    private readonly Dictionary<Page, PageMarks> _marks = [];

    private sealed class PageMarks
    {
        public int Key;
        public int Next;
    }

    public StructureTreeBuilder(Document document)
    {
        _document = document;

        // Set /MarkInfo << /Marked true >> on the catalog
        var markInfo = new PdfDictionary();
        markInfo.Set("Marked", PdfBoolean.True);
        document.Catalog.Set("MarkInfo", markInfo);

        // Register with document for auto-finalization on save
        document.RegisterStructureTreeBuilder(this);
    }

    /// <summary>
    /// Create a top-level structure element (e.g., "Document").
    /// </summary>
    public StructureElementBuilder CreateElement(string structureType)
    {
        var elem = new StructureElementBuilder(this, structureType);
        _rootElements.Add(elem);
        return elem;
    }

    /// <summary>
    /// Add a role mapping that maps a custom structure type to a standard type.
    /// Standard types per PDF32000 §14.8.4: Document, Part, Art, Sect, Div,
    /// BlockQuote, Caption, TOC, TOCI, Index, NonStruct, Private,
    /// H, H1–H6, P, L, LI, Lbl, LBody, Table, TR, TH, TD, THead, TBody, TFoot,
    /// Span, Quote, Note, Reference, BibEntry, Code, Link, Annot,
    /// Ruby, Warichu, Figure, Formula, Form.
    /// </summary>
    public void AddRoleMapping(string customRole, string standardRole)
    {
        Compat.ThrowIfNull(customRole);
        Compat.ThrowIfNull(standardRole);
        _roleMappings[customRole] = standardRole;
    }

    /// <summary>
    /// Get the current role mappings as a dictionary (custom role → standard role).
    /// </summary>
    public Dictionary<string, string> GetRoleMappings()
    {
        return new Dictionary<string, string>(_roleMappings, StringComparer.Ordinal);
    }

    /// <summary>Which object the page is, so a structure element can point at
    /// it. Null for a page the document cannot place.</summary>
    internal int? PageNumber(Page page)
    {
        var found = _document.FindObjectNumber(page.Dict);
        return found > 0 ? found : null;
    }

    /// <summary>The next id on this page, and the page's number-tree key,
    /// assigning both the first time the page is marked.</summary>
    internal (int Mcid, int Key) AllocateMcid(Page page)
    {
        if (!_marks.TryGetValue(page, out var marks))
        {
            _marks[page] = marks = new PageMarks { Key = _marks.Count };
            page.Dict.Set("StructParents", new PdfInteger(marks.Key));
        }

        return (marks.Next++, marks.Key);
    }

    /// <summary>
    /// Finalize the structure tree and write it to the document catalog.
    /// Must be called before saving the document.
    /// Assigns object numbers and builds the /ParentTree.
    /// </summary>
    public void BuildParentTree()
    {
        // Allocate object numbers for all structure elements
        var allElements = new List<StructureElementBuilder>();
        foreach (var root in _rootElements)
        {
            root.CollectAll(allElements);
        }

        // Assign object numbers starting from a high range to avoid conflicts
        var baseObjNum = _document.AllocateObjectNumber() + 100;
        var structTreeRootObjNum = baseObjNum;
        baseObjNum++;

        foreach (var elem in allElements)
        {
            elem.ObjectNumber = baseObjNum++;
        }

        // Build StructTreeRoot dict
        var structTreeRoot = new PdfDictionary();
        structTreeRoot.Set("Type", new PdfName("StructTreeRoot"));

        // /K — root kids
        if (_rootElements.Count == 1)
        {
            structTreeRoot.Set("K", new PdfIndirectRef(_rootElements[0].ObjectNumber, 0));
        }
        else
        {
            var rootKids = new PdfArray();
            foreach (var root in _rootElements)
                rootKids.Add(new PdfIndirectRef(root.ObjectNumber, 0));
            structTreeRoot.Set("K", rootKids);
        }

        // /RoleMap — custom structure types mapped to standard types
        if (_roleMappings.Count > 0)
        {
            var roleMap = new PdfDictionary();
            foreach (var (customRole, standardRole) in _roleMappings)
            {
                roleMap.Set(customRole, new PdfName(standardRole));
            }
            structTreeRoot.Set("RoleMap", roleMap);
        }

        // ⭐⭐ /ParentTree is keyed by the PAGE — its /StructParents number — and
        // each entry is an ARRAY indexed by the marked-content id on that page
        // (PDF 32000-1 §14.7.4.4). It is not a flat list of id/element pairs:
        // read that way the ids of a second page overwrite the first page's,
        // and every reference on it resolves to the wrong element.
        var owners = new SortedDictionary<int, Dictionary<int, int>>();
        foreach (var elem in allElements)
        {
            foreach (var mark in elem.Marked)
            {
                if (!owners.TryGetValue(mark.Key, out var byMcid))
                    owners[mark.Key] = byMcid = [];

                byMcid[mark.Mcid] = elem.ObjectNumber;
            }
        }

        var parentTreeNums = new PdfArray();
        var nextKey = 0;
        nextKey = WireMcidOwners(owners, parentTreeNums, nextKey);

        if (parentTreeNums.Count > 0)
        {
            var parentTree = new PdfDictionary();
            parentTree.Set("Nums", parentTreeNums);
            structTreeRoot.Set("ParentTree", parentTree);
            structTreeRoot.Set("ParentTreeNextKey", new PdfInteger(nextKey));
        }

        // Register StructTreeRoot as a new object
        _document.AddNewObject(structTreeRootObjNum, structTreeRoot);
        _document.Catalog.Set("StructTreeRoot", new PdfIndirectRef(structTreeRootObjNum, 0));

        // Register each structure element as an indirect object
        foreach (var elem in allElements)
        {
            var dict = elem.BuildDict(structTreeRootObjNum);
            _document.AddNewObject(elem.ObjectNumber, dict);
        }
    }
}

/// <summary>
/// Builder for creating a structure element with children and marked content.
/// </summary>
public sealed class StructureElementBuilder
{
    private readonly StructureTreeBuilder _tree;
    private readonly string _structureType;
    private readonly List<StructureElementBuilder> _children = [];
    private readonly List<Mark> _marked = [];
    private StructureElementBuilder? _parent;

    /// <summary>One piece of page content this element owns.</summary>
    internal sealed class Mark
    {
        public int Mcid;
        public int Key;
        public Page Page = null!;
    }

    private string? _title;
    private string? _language;
    private string? _altText;
    private string? _actualText;

    internal int ObjectNumber { get; set; }
    internal IReadOnlyList<Mark> Marked => _marked;

    internal StructureElementBuilder(StructureTreeBuilder tree, string structureType)
    {
        _tree = tree;
        _structureType = structureType;
    }

    /// <summary>The structure type (e.g. "P", "H1", "Table").</summary>
    public string StructureType => _structureType;

    /// <summary>Set the title for this element.</summary>
    public StructureElementBuilder SetTitle(string title) { _title = title; return this; }

    /// <summary>Set the language for this element (BCP 47 tag).</summary>
    public StructureElementBuilder SetLanguage(string lang) { _language = lang; return this; }

    /// <summary>Set the alternate description for accessibility.</summary>
    public StructureElementBuilder SetAltText(string alt) { _altText = alt; return this; }

    /// <summary>Set the actual text replacement.</summary>
    public StructureElementBuilder SetActualText(string text) { _actualText = text; return this; }

    /// <summary>Create a child structure element.</summary>
    public StructureElementBuilder CreateChild(string structureType)
    {
        var child = new StructureElementBuilder(_tree, structureType);
        child._parent = this;
        _children.Add(child);
        return child;
    }

    /// <summary>
    /// Associate marked content on a page with this structure element.
    /// Returns a MarkedContentInfo with the MCID and BDC/EMC operators.
    /// </summary>
    public MarkedContentInfo AddMarkedContent(Page page)
    {
        var (mcid, key) = _tree.AllocateMcid(page);
        _marked.Add(new Mark { Mcid = mcid, Key = key, Page = page });

        return new MarkedContentInfo(mcid, _structureType);
    }

    internal void CollectAll(List<StructureElementBuilder> result)
    {
        result.Add(this);
        foreach (var child in _children)
            child.CollectAll(result);
    }

    /// <summary>
    /// Build the PdfDictionary for this element, using indirect references
    /// for parent and children to avoid circular serialization.
    /// </summary>
    internal PdfDictionary BuildDict(int structTreeRootObjNum)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("StructElem"));
        dict.Set("S", new PdfName(_structureType));

        // /P — parent (indirect ref to parent element or StructTreeRoot)
        if (_parent is not null)
            dict.Set("P", new PdfIndirectRef(_parent.ObjectNumber, 0));
        else
            dict.Set("P", new PdfIndirectRef(structTreeRootObjNum, 0));

        // ⭐ /Pg — the page this element's content is on. Without it a
        // marked-content reference cannot be resolved at all: a reader has no
        // way to know which page's stream to look in, and the tree describes
        // nothing. Named on the element when every piece it owns is on one
        // page, and on each reference separately when they are not.
        var pages = new List<Page>();
        foreach (var mark in _marked)
            if (!pages.Contains(mark.Page)) pages.Add(mark.Page);

        var onePage = pages.Count == 1 ? pages[0] : null;
        if (onePage is not null && _tree.PageNumber(onePage) is { } only)
            dict.Set("Pg", new PdfIndirectRef(only, 0));

        // /K — kids: child element refs + marked-content refs
        var kids = new PdfArray();
        foreach (var mark in _marked)
        {
            var mcidDict = new PdfDictionary();
            mcidDict.Set("Type", new PdfName("MCR"));
            mcidDict.Set("MCID", new PdfInteger(mark.Mcid));
            if (onePage is null && _tree.PageNumber(mark.Page) is { } spread)
                mcidDict.Set("Pg", new PdfIndirectRef(spread, 0));
            kids.Add(mcidDict);
        }
        foreach (var child in _children)
        {
            kids.Add(new PdfIndirectRef(child.ObjectNumber, 0));
        }
        if (kids.Count == 1)
            dict.Set("K", kids[0]); // single child: inline
        else if (kids.Count > 1)
            dict.Set("K", kids);

        // Optional properties
        if (_title is not null)
            dict.Set("T", new PdfString(Compat.Latin1.GetBytes(_title)));
        if (_language is not null)
            dict.Set("Lang", new PdfString(Compat.Latin1.GetBytes(_language)));
        if (_altText is not null)
            dict.Set("Alt", new PdfString(Compat.Latin1.GetBytes(_altText)));
        if (_actualText is not null)
            dict.Set("ActualText", new PdfString(Compat.Latin1.GetBytes(_actualText)));

        return dict;
    }
}

/// <summary>
/// Information about a marked content sequence, used to wrap content in BDC/EMC operators.
/// </summary>
public sealed class MarkedContentInfo
{
    internal MarkedContentInfo(int mcid, string tag)
    {
        Mcid = mcid;
        Tag = tag;
    }

    /// <summary>The marked content identifier (MCID).</summary>
    public int Mcid { get; }

    /// <summary>The structure element tag (e.g. "P", "H1").</summary>
    public string Tag { get; }

    /// <summary>BDC operator to begin the marked content sequence.</summary>
    public string BeginMarkedContent() => $"/{Tag} <</MCID {Mcid}>> BDC\n";

    /// <summary>EMC operator to end the marked content sequence.</summary>
    public string EndMarkedContent() => "EMC\n";
}

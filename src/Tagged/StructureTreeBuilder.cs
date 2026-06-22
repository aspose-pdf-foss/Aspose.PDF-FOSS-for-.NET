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
public sealed class StructureTreeBuilder
{
    private readonly Document _document;
    private readonly List<StructureElementBuilder> _rootElements = [];
    private readonly Dictionary<string, string> _roleMappings = new(StringComparer.Ordinal);
    private int _nextMcid;

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
        ArgumentNullException.ThrowIfNull(customRole);
        ArgumentNullException.ThrowIfNull(standardRole);
        _roleMappings[customRole] = standardRole;
    }

    /// <summary>
    /// Get the current role mappings as a dictionary (custom role → standard role).
    /// </summary>
    public Dictionary<string, string> GetRoleMappings()
    {
        return new Dictionary<string, string>(_roleMappings, StringComparer.Ordinal);
    }

    internal int AllocateMcid() => _nextMcid++;

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

        // Build /ParentTree (number tree: MCID → parent struct elem indirect ref)
        var parentTreeNums = new PdfArray();
        foreach (var elem in allElements)
        {
            foreach (var mcid in elem.Mcids)
            {
                parentTreeNums.Add(new PdfInteger(mcid));
                parentTreeNums.Add(new PdfIndirectRef(elem.ObjectNumber, 0));
            }
        }

        if (parentTreeNums.Count > 0)
        {
            var parentTree = new PdfDictionary();
            parentTree.Set("Nums", parentTreeNums);
            structTreeRoot.Set("ParentTree", parentTree);
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
    private readonly List<int> _mcids = [];
    private StructureElementBuilder? _parent;

    private string? _title;
    private string? _language;
    private string? _altText;
    private string? _actualText;

    internal int ObjectNumber { get; set; }
    internal IReadOnlyList<int> Mcids => _mcids;

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
        var mcid = _tree.AllocateMcid();
        _mcids.Add(mcid);

        if (!page.Dict.ContainsKey("StructParents"))
            page.Dict.Set("StructParents", new PdfInteger(0));

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

        // /K — kids: child element refs + MCID refs
        var kids = new PdfArray();
        foreach (var mcid in _mcids)
        {
            var mcidDict = new PdfDictionary();
            mcidDict.Set("Type", new PdfName("MCR"));
            mcidDict.Set("MCID", new PdfInteger(mcid));
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
            dict.Set("T", new PdfString(Encoding.Latin1.GetBytes(_title)));
        if (_language is not null)
            dict.Set("Lang", new PdfString(Encoding.Latin1.GetBytes(_language)));
        if (_altText is not null)
            dict.Set("Alt", new PdfString(Encoding.Latin1.GetBytes(_altText)));
        if (_actualText is not null)
            dict.Set("ActualText", new PdfString(Encoding.Latin1.GetBytes(_actualText)));

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

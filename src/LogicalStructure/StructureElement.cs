using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>
/// Base class for tagged-PDF logical-structure elements. Each instance
/// wraps an underlying PDF structure-element dictionary (with /Type
/// /StructElem and /S = role); property reads/writes pass through to
/// the dict so the in-memory tree round-trips to the saved file.
/// </summary>
public class StructureElement : Element, ITextElement
{
    internal readonly PdfDictionary _dict;
    internal PdfReader? _reader;
    internal StructureElement? _parent;
    private ElementList? _children;

    /// <summary>The standard structure type this element maps to (e.g.
    /// "P" for a paragraph). Stays fixed even when <see cref="SetTag"/>
    /// renames the element's role to a custom tag.</summary>
    internal readonly string _standardType;

    /// <summary>Role map shared by all elements created from the same
    /// <see cref="Aspose.Pdf.Tagged.ITaggedContent"/>; null for elements
    /// read back from an existing document.</summary>
    internal RoleMap? _roleMap;

    /// <summary>Element-ID registry shared across elements created from the
    /// same <see cref="Aspose.Pdf.Tagged.ITaggedContent"/>; null for
    /// elements read back from an existing document.</summary>
    internal IdRegistry? _idRegistry;

    private StructureTextState? _textState;

    internal StructureElement(string structureType)
    {
        _dict = new PdfDictionary();
        _dict.Set("Type", new PdfName("StructElem"));
        _dict.Set("S", new PdfName(structureType));
        _standardType = structureType;
        _children = new ElementList();
    }

    internal StructureElement(PdfDictionary dict, PdfReader? reader)
    {
        _dict = dict;
        _reader = reader;
        _standardType = dict.GetName("S") ?? string.Empty;
    }

    /// <summary>Text-state applied to this element's inline content
    /// (stored only — the FOSS structure builder doesn't re-render
    /// through it).</summary>
    public StructureTextState StructureTextState => _textState ??= new StructureTextState();

    private StructureElementAttributes? _attributes;

    /// <summary>The structure attributes (/A) attached to this element,
    /// organised by owner. For an element read from an existing document the
    /// set is populated from the element's /A entry on first access.</summary>
    public StructureElementAttributes Attributes
        => _attributes ??= _reader is not null
            ? new StructureElementAttributes(_dict, _reader)
            : new StructureElementAttributes();

    /// <summary>The PDF structure-type role tag as written to /S
    /// (e.g. "P", "Span", or a custom tag after <see cref="SetTag"/>).</summary>
    internal string Role => _dict.GetName("S") ?? string.Empty;

    /// <summary>The element's standard structure type. A custom role set via
    /// <see cref="SetTag"/> (or read from a document whose role map covers it)
    /// still reports the STANDARD type it maps to; null when the role is not
    /// a standard type and no mapping is known (e.g. MCR/OBJR leaves).</summary>
    public StructureTypeStandard? StructureType
    {
        get
        {
            var role = _dict.GetName("S");
            if (role is not null)
            {
                var std = StructureTypeStandard.FromTag(role);
                if (std is not null) return std;
                if (_roleMap is not null && _roleMap.TryGet(role, out var mapped))
                    return StructureTypeStandard.FromTag(mapped);
            }
            return StructureTypeStandard.FromTag(_standardType);
        }
    }

    /// <summary>The element's structure type (/S) as a <see cref="LogicalStructure.StructureType"/>
    /// whose <see cref="LogicalStructure.StructureType.Name"/> is the role tag (e.g. "H1").</summary>
    public StructureType S => new(_dict.GetName("S") ?? string.Empty);

    /// <summary>The replacement text for the marked content (/ActualText).</summary>
    public string ActualText
    {
        get => GetString("ActualText") ?? string.Empty;
        set => SetString("ActualText", value);
    }

    /// <summary>Alternate text used by assistive technology (/Alt).</summary>
    public string AlternativeText
    {
        get => GetString("Alt") ?? string.Empty;
        set => SetString("Alt", value);
    }

    /// <summary>Expansion text for an abbreviation (/E).</summary>
    public string ExpansionText
    {
        get => GetString("E") ?? string.Empty;
        set => SetString("E", value);
    }

    /// <summary>BCP-47 language code for the element's content (/Lang).</summary>
    public string Language
    {
        get => GetString("Lang") ?? string.Empty;
        set => SetString("Lang", value);
    }

    /// <summary>Element identifier (/ID); read-only — assigned at save.</summary>
    public string ID => GetString("ID") ?? string.Empty;

    /// <summary>Title of the structure element (/T).</summary>
    public string Title
    {
        get => GetString("T") ?? string.Empty;
        set => SetString("T", value);
    }

    /// <summary>Page hosting this element's content. For an element read
    /// from an existing document the page is resolved from the structure
    /// tree (/Pg on the element, an ancestor, or the first marked-content
    /// descendant); null when the element isn't attached to a page.</summary>
    public Page? Page
    {
        get => _page ??= ResolvePage();
        internal set => _page = value;
    }

    private Page? _page;

    /// <summary>Document this element was materialised from; set on the
    /// tree root by <see cref="Aspose.Pdf.Tagged.TaggedContent"/> and
    /// inherited by children as the tree loads.</summary>
    internal Document? _sourceDoc;

    private Page? ResolvePage()
    {
        var doc = FindSourceDocument();
        if (doc is null) return null;
        // Nearest /Pg on this element or an ancestor…
        for (StructureElement? el = this; el is not null; el = el._parent)
        {
            var pg = el.OwnPageDict();
            if (pg is not null) return MapPage(doc, pg);
        }
        // …else the first marked-content descendant that names one.
        return ResolvePageFromDescendants(doc);
    }

    private Document? FindSourceDocument()
    {
        for (StructureElement? el = this; el is not null; el = el._parent)
            if (el._sourceDoc is not null) return el._sourceDoc;
        return null;
    }

    /// <summary>The page dictionary this element's own entries name: /Pg on
    /// the element itself, or the /Pg of an MCR dictionary in its /K.</summary>
    private PdfDictionary? OwnPageDict()
    {
        if (Resolve(_dict.Get("Pg")) is PdfDictionary pg) return pg;
        var k = Resolve(_dict.Get("K"));
        switch (k)
        {
            case PdfDictionary kd when kd.GetName("Type") == "MCR":
                return Resolve(kd.Get("Pg")) as PdfDictionary;
            case PdfArray arr:
                foreach (var item in arr)
                    if (Resolve(item) is PdfDictionary id && id.GetName("Type") == "MCR"
                        && Resolve(id.Get("Pg")) is PdfDictionary mpg)
                        return mpg;
                break;
        }
        return null;
    }

    private PdfObject? Resolve(PdfObject? obj) => _reader is not null ? _reader.Resolve(obj) : obj;

    private Page? ResolvePageFromDescendants(Document doc)
    {
        EnsureChildrenLoaded();
        foreach (var child in _children!)
        {
            var pg = child.OwnPageDict();
            if (pg is not null) return MapPage(doc, pg);
            var deep = child.ResolvePageFromDescendants(doc);
            if (deep is not null) return deep;
        }
        return null;
    }

    private static Page? MapPage(Document doc, PdfDictionary pageDict)
    {
        var pages = doc.Pages;
        for (int i = 1; i <= pages.Count; i++)
            if (ReferenceEquals(pages[i].Dict, pageDict))
                return pages[i];
        return null;
    }

    /// <summary>Append <paramref name="child"/> under this element's /K
    /// array. Updates the in-memory tree and writes the child dict
    /// into the parent's /K so the change persists to the saved
    /// PDF.</summary>
    public virtual StructureElement AppendChild(StructureElement child)
        => AppendChildCore(child, validate: true);

    // Elements this one references (/Ref, PDF 32000 §14.7.4.3) — e.g. a TOCI
    // referencing the header its entry navigates to. Written out by the
    // structure wiring on save, once every element has its object number.
    internal List<StructureElement>? _referencedElements;

    /// <summary>Record a /Ref association from this element to
    /// <paramref name="referencedStructureElement"/> (PDF 32000 §14.7.4.3 —
    /// e.g. a TOCI element references the header element its entry navigates
    /// to in a tagged table of contents).</summary>
    public void AddRef(StructureElement referencedStructureElement)
    {
        if (referencedStructureElement is null)
            throw new ArgumentNullException(nameof(referencedStructureElement));
        (_referencedElements ??= new List<StructureElement>()).Add(referencedStructureElement);
    }

    /// <summary>True when this element is the document-level root of the
    /// structure tree (the /StructTreeRoot itself, or the single element
    /// directly under it).</summary>
    private bool IsDocumentRoot
        => this is StructTreeRootElement || _parent is StructTreeRootElement;

    /// <summary>Containment rules the authoring API enforces when
    /// validate=true: a Table may hold only row groups / rows / captions,
    /// a TR only cells, a TOC sits under the document root only, and a
    /// TOCI only under a TOC. Throws <see cref="Aspose.Pdf.Tagged.TaggedException"/>
    /// when appending <paramref name="child"/> here would break a rule.</summary>
    private void ValidateAppend(StructureElement child)
    {
        string? rule = null;
        if (child is TOCElement && !IsDocumentRoot)
            rule = "a TOC element may be a child of the root element only";
        else if (child is TOCIElement && this is not TOCElement)
            rule = "a TOCI element may be a child of a TOC element only";
        else if (this is TableElement
                 && child is not (TableTRElement or TableTHeadElement or TableTBodyElement
                     or TableTFootElement or CaptionElement))
            rule = "a Table element may hold only THead/TBody/TFoot/TR/Caption children";
        else if (this is TableTRElement && child is not (TableTHElement or TableTDElement))
            rule = "a TR element may hold only TH/TD children";
        if (rule is not null)
            throw new Aspose.Pdf.Tagged.TaggedException(
                $"Appending structure element '{child}' to the element '{this}' is not allowed: {rule}.");
    }

    private StructureElement AppendChildCore(StructureElement child, bool validate)
    {
        if (child is null) throw new ArgumentNullException(nameof(child));
        // Re-appending an element that is already a child of this element is
        // invalid — it would duplicate the /K reference. Move it explicitly
        // with ChangeParentElement to re-parent.
        if (ReferenceEquals(child._parent, this))
            throw new Aspose.Pdf.Tagged.TaggedException(
                "Structure element is already a child of this element.");
        if (validate) ValidateAppend(child);
        EnsureChildrenLoaded();
        Adopt(child);
        _children!.Add(child);
        var k = _dict.Get("K") as PdfArray;
        if (k is null)
        {
            k = new PdfArray();
            _dict.Set("K", k);
        }
        k.Add(child._dict);
        return child;
    }

    /// <summary>The parent structure element, or null when this element
    /// is detached or directly under the structure-tree root.</summary>
    public StructureElement? ParentElement => _parent;

    /// <summary>Direct children of this element.</summary>
    public override ElementList ChildElements
    {
        get
        {
            EnsureChildrenLoaded();
            return _children!;
        }
    }

    /// <summary>Remap this element's role to a custom <paramref name="tag"/>,
    /// registering <paramref name="tag"/> → its standard type in the
    /// document role map. Throws <see cref="Aspose.Pdf.Tagged.TaggedException"/>
    /// if <paramref name="tag"/> is itself a standard type, or is already
    /// mapped to a different standard type.</summary>
    public void SetTag(string tag)
    {
        if (string.IsNullOrEmpty(tag)) throw new ArgumentException("Tag must not be empty.", nameof(tag));

        if (RoleMap.IsStandardType(tag))
            throw new Aspose.Pdf.Tagged.TaggedException(
                $"Standard structure type {tag} can't be remapped");

        var std = _standardType;
        if (_roleMap is not null)
        {
            if (_roleMap.TryGet(tag, out var existing) && existing != std)
                throw new Aspose.Pdf.Tagged.TaggedException(
                    $"Non-standard structure type {tag} has already mapped on standard type {existing}");
            _roleMap.Set(tag, std);
        }

        _dict.Set("S", new PdfName(tag));
    }

    /// <summary>Textual representation used when dumping the structure
    /// tree (role name; falls back to the type name).</summary>
    public override string ToString()
    {
        var role = _dict.GetName("S");
        return string.IsNullOrEmpty(role) ? GetType().Name : role!;
    }

    /// <summary>Attach <paramref name="child"/> as a child of this element,
    /// inheriting the shared role map / ID registry when the child doesn't
    /// already carry its own (so a whole tree materialised from a loaded
    /// document shares the document's registry).</summary>
    private void Adopt(StructureElement child)
    {
        child._parent = this;
        child._roleMap ??= _roleMap;
        child._idRegistry ??= _idRegistry;
    }

    /// <summary>Remove all children (and the underlying /K entries). Used by the
    /// auto-tagger to clear an existing structure tree before regenerating it.</summary>
    internal void ClearChildren()
    {
        _dict.Remove("K");
        _children = new ElementList();
    }

    private void EnsureChildrenLoaded()
    {
        if (_children is not null) return;
        _children = new ElementList();
        if (_reader is null) return;
        var k = _reader.Resolve(_dict.Get("K"));
        switch (k)
        {
            case PdfArray arr:
                foreach (var item in arr)
                {
                    if (_reader.Resolve(item) is PdfDictionary kd
                        && kd.GetName("Type") is null or "StructElem")
                    {
                        var child = MaterializeChild(kd);
                        Adopt(child);
                        _children.Add(child);
                    }
                }
                break;
            case PdfDictionary single
                when single.GetName("Type") is null or "StructElem":
                {
                    var child = MaterializeChild(single);
                    Adopt(child);
                    _children.Add(child);
                }
                break;
        }
    }

    /// <summary>Recursively find descendant elements of type T.
    /// FOSS-extra mirroring the same-named helper on
    /// <see cref="Aspose.Pdf.Tagged.StructureElement"/>.</summary>
    public List<T> FindElements<T>(bool recursive = false) where T : class
    {
        var results = new List<T>();
        EnsureChildrenLoaded();
        FindElementsInternal(results, recursive);
        return results;
    }

    private void FindElementsInternal<T>(List<T> results, bool recursive) where T : class
    {
        EnsureChildrenLoaded();
        foreach (var child in _children!)
        {
            if (child is T typed) results.Add(typed);
            if (recursive) child.FindElementsInternal(results, recursive);
        }
    }

    /// <summary>Append <paramref name="child"/> with an optional
    /// validation pass. FOSS-extra matching the
    /// <see cref="Aspose.Pdf.Tagged.StructureElement.AppendChild(Aspose.Pdf.Tagged.StructureElement, bool)"/>
    /// overload — validate=false skips the containment rules.</summary>
    public void AppendChild(StructureElement child, bool validate)
        => AppendChildCore(child, validate);

    /// <summary>Set the element's text content (stored as
    /// /ActualText). FOSS-extra alias.</summary>
    public void SetText(string text) => ActualText = text;

    /// <summary>Set the element identifier (/ID). Throws
    /// <see cref="Aspose.Pdf.Tagged.TaggedException"/> if <paramref name="id"/>
    /// is empty, or already used by another element in the same document.</summary>
    public void SetId(string id)
    {
        if (string.IsNullOrEmpty(id))
            throw new Aspose.Pdf.Tagged.TaggedException("Structure element ID can not be null or empty");
        if (_idRegistry is not null && _idRegistry.IsUsedByOther(id, this))
            throw new Aspose.Pdf.Tagged.TaggedException($"Structure element with ID='{id}' already exists");

        var old = GetString("ID");
        _idRegistry?.Unregister(old, this);
        _dict.Set("ID", new PdfString(System.Text.Encoding.UTF8.GetBytes(id)));
        _idRegistry?.Register(id, this);
    }

    /// <summary>Remove the element identifier (/ID).</summary>
    public void ClearId()
    {
        _idRegistry?.Unregister(GetString("ID"), this);
        _dict.Remove("ID");
    }

    /// <summary>Layout margin captured from <see cref="AdjustPosition"/> or
    /// <see cref="StructureTextState"/>.MarginInfo, consumed by the tagged-content
    /// renderer to place an authored block. Null when no position was requested.</summary>
    internal MarginInfo? _positionMargin;

    /// <summary>The element starts on a new page (PositionSettings.IsInNewPage).</summary>
    internal bool _posInNewPage;

    /// <summary>The element continues the current text line instead of opening
    /// its own (PositionSettings.IsInLineParagraph).</summary>
    internal bool _posInline;

    /// <summary>Capture the requested layout position for an authored structure
    /// element. The settings object is an <c>Aspose.Pdf.Tagged.PositionSettings</c>
    /// exposing Margin / IsInNewPage / IsInLineParagraph.
    /// The values are stored so the tagged-content renderer can place the block
    /// (Left/Right indent the column, Top/Bottom add space around it, IsInNewPage
    /// breaks the page, IsInLineParagraph flows the element inline).</summary>
    public void AdjustPosition(object settings)
    {
        if (settings is null) return;
        // Both the Tagged-side and LogicalStructure PositionSettings expose these
        // properties; read them structurally so we don't hard-depend on either
        // concrete type here.
        var t = settings.GetType();
        if (t.GetProperty("Margin")?.GetValue(settings) is MarginInfo margin)
            _positionMargin = margin;
        if (t.GetProperty("IsInNewPage")?.GetValue(settings) is bool newPage)
            _posInNewPage = newPage;
        if (t.GetProperty("IsInLineParagraph")?.GetValue(settings) is bool inline)
            _posInline = inline;
    }

    /// <summary>Move this element under <paramref name="newParent"/>.
    /// FOSS-extra mirroring the Tagged-side authoring helper. The
    /// containment rules run BEFORE detaching so a rejected move leaves
    /// the tree unchanged.</summary>
    public void ChangeParentElement(StructureElement newParent, bool validate = true)
    {
        if (newParent is not null && validate) newParent.ValidateAppend(this);
        Detach();
        newParent?.AppendChildCore(this, validate: false);
    }

    /// <summary>Detach this element, then re-parent its children
    /// under this element's former parent (taking this element's
    /// slot). FOSS-extra mirroring the Tagged-side helper.</summary>
    public void RemoveAndMoveItsChildObjectsToItsParent(bool validate = true)
    {
        var parent = _parent;
        if (parent is null) return;
        EnsureChildrenLoaded();
        parent.EnsureChildrenLoaded();
        // Rules run before any mutation so a rejected move leaves the tree intact.
        if (validate)
            foreach (var c in _children!)
                parent.ValidateAppend(c);
        var copy = new List<StructureElement>(_children!);

        // Record this element's slot in the parent (in-memory list + /K array) so
        // the moved children take its place rather than landing at the parent's end.
        var listIdx = parent._children!.IndexOf(this);
        if (listIdx < 0) listIdx = parent._children.Count;
        var parentK = parent._reader?.Resolve(parent._dict.Get("K")) as PdfArray
                      ?? parent._dict.Get("K") as PdfArray;
        var kIdx = -1;
        if (parentK is not null)
            for (var i = 0; i < parentK.Count; i++)
                if (ReferenceEquals(parent._reader?.Resolve(parentK[i]) ?? parentK[i], _dict)) { kIdx = i; break; }

        Detach(); // removes this from parent._children (at listIdx) and /K (at kIdx)

        for (var j = 0; j < copy.Count; j++)
        {
            var c = copy[j];
            parent.Adopt(c);
            parent._children.Insert(Math.Min(listIdx + j, parent._children.Count), c);
            if (parentK is null)
            {
                parentK = new PdfArray();
                parent._dict.Set("K", parentK);
                parentK.Add(c._dict);
            }
            else if (kIdx >= 0)
                parentK.Insert(Math.Min(kIdx + j, parentK.Count), c._dict);
            else
                parentK.Add(c._dict);
        }
    }

    /// <summary>Detach this element from its parent. FOSS-extra.</summary>
    public void Detach()
    {
        var parent = _parent;
        if (parent is null) return;
        parent.EnsureChildrenLoaded();
        parent._children!.Remove(this);
        // Remove from underlying /K array as well.
        var k = parent._reader?.Resolve(parent._dict.Get("K")) as PdfArray
                ?? parent._dict.Get("K") as PdfArray;
        if (k is not null)
        {
            for (var i = 0; i < k.Count; i++)
            {
                var resolved = parent._reader?.Resolve(k[i]) ?? k[i];
                if (ReferenceEquals(resolved, _dict))
                {
                    k.RemoveAt(i);
                    break;
                }
            }
        }
        _parent = null;
    }

    private StructureElement MaterializeChild(PdfDictionary dict)
    {
        var role = dict.GetName("S") ?? string.Empty;
        StructureElement el = role switch
        {
            "Document" => new DocumentElement(dict, _reader),
            "Annot" => new AnnotElement(dict, _reader),
            "Art" => new ArtElement(dict, _reader),
            "BibEntry" => new BibEntryElement(dict, _reader),
            "BlockQuote" => new BlockQuoteElement(dict, _reader),
            "Caption" => new CaptionElement(dict, _reader),
            "Code" => new CodeElement(dict, _reader),
            "Div" => new DivElement(dict, _reader),
            "Figure" => new FigureElement(dict, _reader),
            "Form" => new FormElement(dict, _reader),
            "Formula" => new FormulaElement(dict, _reader),
            "H" or "H1" or "H2" or "H3" or "H4" or "H5" or "H6" => new HeaderElement(dict, _reader),
            "Index" => new IndexElement(dict, _reader),
            "L" => new ListElement(dict, _reader),
            "LBody" => new ListLBodyElement(dict, _reader),
            "LI" => new ListLIElement(dict, _reader),
            "Lbl" => new ListLblElement(dict, _reader),
            "Link" => new LinkElement(dict, _reader),
            "MCR" => new MCRElement(dict, _reader),
            "OBJR" => new OBJRElement(dict, _reader),
            "NonStruct" => new NonStructElement(dict, _reader),
            "Note" => new NoteElement(dict, _reader),
            "P" => new ParagraphElement(dict, _reader),
            "Part" => new PartElement(dict, _reader),
            "Private" => new PrivateElement(dict, _reader),
            "Quote" => new QuoteElement(dict, _reader),
            "Reference" => new ReferenceElement(dict, _reader),
            "Ruby" => new RubyElement(dict, _reader),
            "RB" => new RubyRBElement(dict, _reader),
            "RT" => new RubyRTElement(dict, _reader),
            "RP" => new RubyRPElement(dict, _reader),
            "Sect" => new SectElement(dict, _reader),
            "Span" => new SpanElement(dict, _reader),
            "TOC" => new TOCElement(dict, _reader),
            "TOCI" => new TOCIElement(dict, _reader),
            "Table" => new TableElement(dict, _reader),
            "TBody" => new TableTBodyElement(dict, _reader),
            "TD" => new TableTDElement(dict, _reader),
            "TFoot" => new TableTFootElement(dict, _reader),
            "TH" => new TableTHElement(dict, _reader),
            "THead" => new TableTHeadElement(dict, _reader),
            "TR" => new TableTRElement(dict, _reader),
            "Warichu" => new WarichuElement(dict, _reader),
            "WT" => new WarichuWTElement(dict, _reader),
            "WP" => new WarichuWPElement(dict, _reader),
            _ => new GenericStructureElement(dict, _reader),
        };
        return el;
    }

    private string? GetString(string key)
    {
        var obj = _reader?.Resolve(_dict.Get(key)) ?? _dict.Get(key);
        return obj is PdfString s ? s.ToText() : null;
    }

    private void SetString(string key, string? value)
    {
        if (string.IsNullOrEmpty(value)) _dict.Remove(key);
        else _dict.Set(key, new PdfString(System.Text.Encoding.UTF8.GetBytes(value)));
    }
}

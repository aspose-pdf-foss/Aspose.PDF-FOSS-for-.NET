using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>
/// Common base for nodes in the logical-structure tree. Exposes the
/// child collection and a textual representation used when dumping the
/// tree for diagnostics.
/// </summary>
public abstract class Element
{
    /// <summary>Direct children of this element.</summary>
    public abstract ElementList ChildElements { get; }
}

/// <summary>A structure-type role (the /S entry value), exposed via
/// <see cref="StructureElement.S"/>. <see cref="Name"/> is the role tag, e.g. "P", "H1".</summary>
public sealed class StructureType
{
    internal StructureType(string name) => Name = name;

    /// <summary>The structure-type role tag (e.g. "P", "Sect", "H1").</summary>
    public string Name { get; }

    public override string ToString() => Name;
}

/// <summary>An ordered list of structure elements, as returned by
/// <see cref="Element.ChildElements"/>.</summary>
public sealed class ElementList : List<StructureElement>
{
    internal ElementList() { }
    internal ElementList(IEnumerable<StructureElement> items) : base(items) { }
}

/// <summary>A structure element that can carry inline text content
/// (written to the element's /ActualText entry).</summary>
public interface ITextElement
{
    /// <summary>Set the element's text content.</summary>
    void SetText(string text);
}

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

    /// <summary>The PDF structure-type role (/S entry, e.g. "P", "Span").</summary>
    public string StructureType => _dict.GetName("S") ?? string.Empty;

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

    /// <summary>Page hosting this element's content, or null when the
    /// element hasn't been attached to a page yet.</summary>
    public Page? Page { get; internal set; }

    /// <summary>Append <paramref name="child"/> under this element's /K
    /// array. Updates the in-memory tree and writes the child dict
    /// into the parent's /K so the change persists to the saved
    /// PDF.</summary>
    public virtual StructureElement AppendChild(StructureElement child)
        => AppendChildCore(child, validate: true);

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

/// <summary>The /StructTreeRoot wrapper at the top of the logical-
/// structure tree. Hosts the document's top-level structure elements
/// under its /K entry.</summary>
public sealed class StructTreeRootElement : StructureElement
{
    internal StructTreeRootElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
    internal StructTreeRootElement() : base(new PdfDictionary(), null)
    {
        _dict.Set("Type", new PdfName("StructTreeRoot"));
    }

    /// <summary>Flat list of every structure element below the root,
    /// produced by a depth-first walk of <see cref="StructureElement.ChildElements"/>.</summary>
    public IReadOnlyList<StructureElement> AllElements => FindElements<StructureElement>(recursive: true);
}

/// <summary>Text-state snapshot used to format inline structure-element
/// runs. The FOSS structure-tree builder doesn't currently render
/// content through this state — values are stored only.</summary>
public sealed class StructureTextState
{
    /// <summary>Font name applied to the run.</summary>
    public string? FontName { get; set; }
    /// <summary>Font size in points.</summary>
    public float FontSize { get; set; } = 12f;
    /// <summary>Foreground fill colour.</summary>
    public Aspose.Pdf.Color? ForegroundColor { get; set; }
    /// <summary>Background fill colour.</summary>
    public Aspose.Pdf.Color? BackgroundColor { get; set; }
    /// <summary>Font style (bold / italic) applied to the run.</summary>
    public Aspose.Pdf.Text.FontStyles FontStyle { get; set; }
    /// <summary>Font applied to the run.</summary>
    public Aspose.Pdf.Text.Font? Font { get; set; }
    /// <summary>Whether the run is underlined.</summary>
    public bool Underline { get; set; }
    /// <summary>Whether the run is struck through.</summary>
    public bool StrikeOut { get; set; }
    /// <summary>Whether the run is rendered as subscript.</summary>
    public bool Subscript { get; set; }
    /// <summary>Whether the run is rendered as superscript.</summary>
    public bool Superscript { get; set; }
    /// <summary>Horizontal glyph scaling (percent).</summary>
    public float HorizontalScaling { get; set; } = 100f;
    /// <summary>Leading between lines, in points.</summary>
    public float LineSpacing { get; set; }
    /// <summary>Extra spacing between characters, in points.</summary>
    public float CharacterSpacing { get; set; }
    /// <summary>Extra spacing between words, in points.</summary>
    public float WordSpacing { get; set; }

    /// <summary>Layout margin for the element the state is applied to. An
    /// alternative to <see cref="Aspose.Pdf.Tagged.StructureElement.AdjustPosition"/>
    /// for positioning an authored block; consumed by the tagged-content renderer.</summary>
    public Aspose.Pdf.MarginInfo? MarginInfo { get; set; }
}


/// <summary>
/// Maps custom (non-standard) structure-type names to PDF standard
/// types, persisting entries to the structure tree's /RoleMap dictionary
/// so a tagged document round-trips its custom tags.
/// </summary>
internal sealed class RoleMap
{
    private readonly PdfDictionary _structTreeRoot;

    internal RoleMap(PdfDictionary structTreeRoot) => _structTreeRoot = structTreeRoot;

    private PdfDictionary GetOrCreateMap()
    {
        if (_structTreeRoot.Get("RoleMap") is PdfDictionary existing) return existing;
        var map = new PdfDictionary();
        _structTreeRoot.Set("RoleMap", map);
        return map;
    }

    internal bool TryGet(string customTag, out string standardType)
    {
        if (_structTreeRoot.Get("RoleMap") is PdfDictionary map && map.GetName(customTag) is { } v)
        {
            standardType = v;
            return true;
        }
        standardType = string.Empty;
        return false;
    }

    internal void Set(string customTag, string standardType)
        => GetOrCreateMap().Set(customTag, new PdfName(standardType));

    /// <summary>The PDF 1.7 standard structure types (ISO 32000-1 Table 333-337).</summary>
    private static readonly HashSet<string> Standard = new(StringComparer.Ordinal)
    {
        "Document", "Part", "Art", "Sect", "Div", "BlockQuote", "Caption", "TOC", "TOCI",
        "Index", "NonStruct", "Private", "P", "H", "H1", "H2", "H3", "H4", "H5", "H6",
        "L", "LI", "Lbl", "LBody", "Table", "TR", "TH", "TD", "THead", "TBody", "TFoot",
        "Span", "Quote", "Note", "Reference", "BibEntry", "Code", "Link", "Annot",
        "Ruby", "RB", "RT", "RP", "Warichu", "WT", "WP", "Figure", "Formula", "Form",
    };

    internal static bool IsStandardType(string tag) => Standard.Contains(tag);
}

/// <summary>
/// Tracks the structure-element identifiers (/ID) in use across a document
/// so <see cref="StructureElement.SetId"/> can reject duplicates. Shared by
/// all elements created from one <see cref="Aspose.Pdf.Tagged.ITaggedContent"/>.
/// </summary>
internal sealed class IdRegistry
{
    private readonly Dictionary<string, StructureElement> _ids = new(StringComparer.Ordinal);

    internal bool IsUsedByOther(string id, StructureElement self)
        => _ids.TryGetValue(id, out var owner) && !ReferenceEquals(owner, self);

    internal void Register(string id, StructureElement element) => _ids[id] = element;

    internal void Unregister(string? id, StructureElement element)
    {
        if (!string.IsNullOrEmpty(id) && _ids.TryGetValue(id!, out var owner) && ReferenceEquals(owner, element))
            _ids.Remove(id!);
    }
}

// ── Typed structure-element subclasses ────────────────────────────────
//
// Each subclass just fixes the /S role for its node; the public API declares
// these as distinct nominal types so callers can pattern-match on the
// element kind. No subclass adds members beyond what the base provides
// (matches DeclaredOnly reflection, which reports zero
// declared members on most of these subclasses).

internal sealed class GenericStructureElement : StructureElement
{
    internal GenericStructureElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}

public sealed class AnnotElement : StructureElement
{
    internal AnnotElement() : base("Annot") { }
    internal AnnotElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
public sealed class ArtElement : StructureElement
{
    internal ArtElement() : base("Art") { }
    internal ArtElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
public sealed class BibEntryElement : StructureElement
{
    internal BibEntryElement() : base("BibEntry") { }
    internal BibEntryElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
public sealed class BlockQuoteElement : StructureElement
{
    internal BlockQuoteElement() : base("BlockQuote") { }
    internal BlockQuoteElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
public sealed class CaptionElement : StructureElement
{
    internal CaptionElement() : base("Caption") { }
    internal CaptionElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
public sealed class CodeElement : StructureElement
{
    internal CodeElement() : base("Code") { }
    internal CodeElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
public sealed class DivElement : StructureElement
{
    internal DivElement() : base("Div") { }
    internal DivElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
/// <summary>Abstract base for illustration-kind structure elements
/// (Figure, Formula). Lets callers treat any illustration uniformly.</summary>
public abstract class IllustrationElement : StructureElement
{
    internal IllustrationElement(string structureType) : base(structureType) { }
    internal IllustrationElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }

    /// <summary>Image file backing this illustration. FOSS-extra — stored only.</summary>
    public string? ImagePath { get; private set; }

    /// <summary>Image-width override (0 = auto). FOSS-extra.</summary>
    public double ImageWidth { get; private set; }

    /// <summary>Image-height override (0 = auto). FOSS-extra.</summary>
    public double ImageHeight { get; private set; }

    /// <summary>Image resolution (DPI) override (0 = auto). FOSS-extra.</summary>
    public int Resolution { get; private set; }

    /// <summary>Bind a raster/vector picture to this illustration. FOSS-extra
    /// mirroring the Tagged-side authoring helper. Stored only.</summary>
    public void SetImage(string imagePath)
    {
        ImagePath = imagePath;
        ImageWidth = 0;
        ImageHeight = 0;
    }

    /// <summary>Bind a picture with explicit dimensions.</summary>
    public void SetImage(string imagePath, double width, double height)
    {
        ImagePath = imagePath;
        ImageWidth = width;
        ImageHeight = height;
    }

    /// <summary>Bind a picture with an explicit resolution (DPI).</summary>
    public void SetImage(string imagePath, int resolution)
    {
        ImagePath = imagePath;
        ImageWidth = 0;
        ImageHeight = 0;
        Resolution = resolution;
    }
}

public sealed class FigureElement : IllustrationElement
{
    internal FigureElement() : base("Figure") { }
    internal FigureElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
public sealed class FormElement : StructureElement
{
    internal FormElement() : base("Form") { }
    internal FormElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
public sealed class FormulaElement : IllustrationElement
{
    internal FormulaElement() : base("Formula") { }
    internal FormulaElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}

public sealed class HeaderElement : StructureElement
{
    internal HeaderElement() : base("H") { }
    internal HeaderElement(int level) : base(level <= 0 ? "H" : $"H{level}") { }
    internal HeaderElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}

public sealed class IndexElement : StructureElement
{
    internal IndexElement() : base("Index") { }
    internal IndexElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
public sealed class LinkElement : StructureElement
{
    internal LinkElement() : base("Link") { }
    internal LinkElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }

    /// <summary>The hyperlink target for this link element. Stored only —
    /// the FOSS structure builder records it but doesn't emit a /Link
    /// annotation for it.</summary>
    public Aspose.Pdf.WebHyperlink? Hyperlink { get; set; }

    /// <summary>Alternate description(s) for the link (/Alt). Stored only.</summary>
    public string? AlternateDescriptions { get; set; }
}
public sealed class ListElement : StructureElement
{
    internal ListElement() : base("L") { }
    internal ListElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
public sealed class ListLBodyElement : StructureElement
{
    internal ListLBodyElement() : base("LBody") { }
    internal ListLBodyElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
public sealed class ListLIElement : StructureElement
{
    internal ListLIElement() : base("LI") { }
    internal ListLIElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
public sealed class ListLblElement : StructureElement
{
    internal ListLblElement() : base("Lbl") { }
    internal ListLblElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
public sealed class NonStructElement : StructureElement
{
    internal NonStructElement() : base("NonStruct") { }
    internal NonStructElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
public sealed class NoteElement : StructureElement
{
    internal NoteElement() : base("Note") { }
    internal NoteElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
public sealed class ParagraphElement : StructureElement
{
    internal ParagraphElement() : base("P") { }
    internal ParagraphElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
public sealed class PartElement : StructureElement
{
    internal PartElement() : base("Part") { }
    internal PartElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}

/// <summary>The document root structure element (/S Document).</summary>
public sealed class DocumentElement : StructureElement
{
    internal DocumentElement() : base("Document") { }
    internal DocumentElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}

/// <summary>A marked-content reference leaf (role "MCR") emitted by the auto-tagger to mark
/// where a structure element's page content lives. Counted by
/// <see cref="StructTreeRootElement.AllElements"/>. (A loaded document's bare MCID integers /
/// /MCR dicts in /K are intentionally NOT surfaced as elements — only these explicit role-MCR
/// structure elements are, so the auto-tagger's tree round-trips without inflating the element
/// count of externally-authored tagged PDFs.)</summary>
public sealed class MCRElement : StructureElement
{
    internal MCRElement() : base("MCR") { }
    internal MCRElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}

/// <summary>An object reference (role "OBJR") — links a structure element to a PDF object on a
/// page (typically an annotation, e.g. a Link's widget). <see cref="Obj"/> resolves the
/// referenced object so callers can wrap it (e.g. <c>new LinkAnnotation(objr.Obj, doc)</c>).</summary>
public sealed class OBJRElement : StructureElement
{
    internal OBJRElement() : base("OBJR") { }
    internal OBJRElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }

    /// <summary>Record the referenced object's indirect reference under /Obj.</summary>
    internal void SetObj(PdfObject objRef) => _dict.Set("Obj", objRef);

    /// <summary>The referenced PDF object (resolved from /Obj), or null.</summary>
    public object? Obj => _reader is not null ? _reader.Resolve(_dict.Get("Obj")) : _dict.Get("Obj");
}
public sealed class PrivateElement : StructureElement
{
    internal PrivateElement() : base("Private") { }
    internal PrivateElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
public sealed class QuoteElement : StructureElement
{
    internal QuoteElement() : base("Quote") { }
    internal QuoteElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
public sealed class ReferenceElement : StructureElement
{
    internal ReferenceElement() : base("Reference") { }
    internal ReferenceElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
public sealed class RubyElement : StructureElement
{
    internal RubyElement() : base("Ruby") { }
    internal RubyElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
public sealed class SectElement : StructureElement
{
    internal SectElement() : base("Sect") { }
    internal SectElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
public sealed class SpanElement : StructureElement
{
    internal SpanElement() : base("Span") { }
    internal SpanElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
public sealed class TOCElement : StructureElement
{
    internal TOCElement() : base("TOC") { }
    internal TOCElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }

    // PDF/UA-1 tagged-TOC navigation: the TOC page whose TocInfo.Title the
    // linked header element mirrors (LinkTocPageTitleToHeaderElement).
    private Page? _linkedTocPage;
    private HeaderElement? _linkedTitleHeader;

    /// <summary>Links the TOC page's <see cref="TocInfo"/> title to the given
    /// header element so the tagged navigation header carries the page title
    /// (PDF/UA-1 tagged TOC). Throws <see cref="TOCpageHasNoTitleException"/>
    /// when the page's TocInfo has no title to link.</summary>
    public void LinkTocPageTitleToHeaderElement(Page tocPage, HeaderElement tocTitleHeader)
    {
        if (tocPage?.TocInfo?.Title is not { } title || string.IsNullOrEmpty(title.Text))
            throw new TOCpageHasNoTitleException();
        _linkedTocPage = tocPage;
        _linkedTitleHeader = tocTitleHeader;
    }

    /// <summary>Save-time consistency check for the linked title (called from
    /// the document's tagged-save path): a header that carries its OWN text
    /// different from the TOC page title is a conflict; an empty header
    /// inherits the page title.</summary>
    internal void ValidateLinkedTitleOnSave()
    {
        if (_linkedTocPage?.TocInfo?.Title is not { } title || _linkedTitleHeader is null)
            return;
        var headerText = _linkedTitleHeader.ActualText;
        var titleText = title.Text ?? string.Empty;
        if (!string.IsNullOrEmpty(headerText) && headerText != titleText)
            throw new HeaderElementTextConflictException();
        if (string.IsNullOrEmpty(headerText))
            _linkedTitleHeader.SetText(titleText);
    }
}
public sealed class TOCIElement : StructureElement
{
    internal TOCIElement() : base("TOCI") { }
    internal TOCIElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
public sealed class TableElement : StructureElement
{
    internal TableElement() : base("Table") { }
    internal TableElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }

    // Table-level style properties (stored only — the FOSS structure
    // builder records them on the element but doesn't re-flow the table).
    public int RepeatingRowsCount { get; set; }
    public int RepeatingColumnsCount { get; set; }
    public Aspose.Pdf.Text.TextState? RepeatingRowsStyle { get; set; }
    public Aspose.Pdf.Color? BackgroundColor { get; set; }
    public Aspose.Pdf.BorderInfo? Border { get; set; }
    public Aspose.Pdf.HorizontalAlignment Alignment { get; set; }
    public Aspose.Pdf.BorderCornerStyle CornerStyle { get; set; }
    public Aspose.Pdf.TableBroken Broken { get; set; }
    public Aspose.Pdf.ColumnAdjustment ColumnAdjustment { get; set; }
    public string? ColumnWidths { get; set; }
    public string? DefaultColumnWidth { get; set; }
    public Aspose.Pdf.BorderInfo? DefaultCellBorder { get; set; }
    public Aspose.Pdf.MarginInfo? DefaultCellPadding { get; set; }
    public Aspose.Pdf.Text.TextState DefaultCellTextState { get; set; } = new Aspose.Pdf.Text.TextState();
    public bool IsBroken { get; set; }
    public bool IsBordersIncluded { get; set; }
    public float Left { get; set; }
    public float Top { get; set; }

    /// <summary>Create + append a TBody child. FOSS-extra authoring helper.</summary>
    public TableTBodyElement CreateTBody()
    {
        var el = new TableTBodyElement();
        AppendChild(el);
        return el;
    }

    /// <summary>Create + append a THead child. FOSS-extra authoring helper.</summary>
    public TableTHeadElement CreateTHead()
    {
        var el = new TableTHeadElement();
        AppendChild(el);
        return el;
    }

    /// <summary>Create + append a TFoot child. FOSS-extra authoring helper.</summary>
    public TableTFootElement CreateTFoot()
    {
        var el = new TableTFootElement();
        AppendChild(el);
        return el;
    }
}
public sealed class TableTBodyElement : StructureElement
{
    internal TableTBodyElement() : base("TBody") { }
    internal TableTBodyElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
    /// <summary>Create + append a TR child. FOSS-extra.</summary>
    public TableTRElement CreateTR() { var el = new TableTRElement(); AppendChild(el); return el; }
}
public sealed class TableTDElement : StructureElement
{
    internal TableTDElement() : base("TD") { }
    internal TableTDElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }

    // Cell-level style properties (stored only).
    public Aspose.Pdf.Color? BackgroundColor { get; set; }
    public Aspose.Pdf.BorderInfo? Border { get; set; }
    public bool IsNoBorder { get; set; }
    public Aspose.Pdf.MarginInfo? Margin { get; set; }
    public Aspose.Pdf.HorizontalAlignment Alignment { get; set; }
    public Aspose.Pdf.VerticalAlignment VerticalAlignment { get; set; }
    public Aspose.Pdf.Text.TextState DefaultCellTextState { get; set; } = new Aspose.Pdf.Text.TextState();
    public bool IsWordWrapped { get; set; }
    public int ColSpan { get; set; }
    public int RowSpan { get; set; }
}
public sealed class TableTFootElement : StructureElement
{
    internal TableTFootElement() : base("TFoot") { }
    internal TableTFootElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
    public TableTRElement CreateTR() { var el = new TableTRElement(); AppendChild(el); return el; }
}
public sealed class TableTHElement : StructureElement
{
    internal TableTHElement() : base("TH") { }
    internal TableTHElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }

    // Cell-level style properties (stored only).
    public Aspose.Pdf.Color? BackgroundColor { get; set; }
    public Aspose.Pdf.BorderInfo? Border { get; set; }
    public bool IsNoBorder { get; set; }
    public Aspose.Pdf.MarginInfo? Margin { get; set; }
    public Aspose.Pdf.HorizontalAlignment Alignment { get; set; }
    public Aspose.Pdf.VerticalAlignment VerticalAlignment { get; set; }
    public Aspose.Pdf.Text.TextState DefaultCellTextState { get; set; } = new Aspose.Pdf.Text.TextState();
    public bool IsWordWrapped { get; set; }
    public int ColSpan { get; set; }
    public int RowSpan { get; set; }
}
public sealed class TableTHeadElement : StructureElement
{
    internal TableTHeadElement() : base("THead") { }
    internal TableTHeadElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
    public TableTRElement CreateTR() { var el = new TableTRElement(); AppendChild(el); return el; }
}
public sealed class TableTRElement : StructureElement
{
    internal TableTRElement() : base("TR") { }
    internal TableTRElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }

    // Row-level style properties (stored only).
    public Aspose.Pdf.Color? BackgroundColor { get; set; }
    public Aspose.Pdf.BorderInfo? Border { get; set; }
    public Aspose.Pdf.BorderInfo? DefaultCellBorder { get; set; }
    public double MinRowHeight { get; set; }
    public double FixedRowHeight { get; set; }
    public bool IsInNewPage { get; set; }
    public bool IsRowBroken { get; set; }
    public Aspose.Pdf.Text.TextState DefaultCellTextState { get; set; } = new Aspose.Pdf.Text.TextState();
    public Aspose.Pdf.MarginInfo? DefaultCellPadding { get; set; }
    public Aspose.Pdf.VerticalAlignment VerticalAlignment { get; set; }

    /// <summary>Create + append a TD child. FOSS-extra.</summary>
    public TableTDElement CreateTD() { var el = new TableTDElement(); AppendChild(el); return el; }
    /// <summary>Create + append a TH child. FOSS-extra.</summary>
    public TableTHElement CreateTH() { var el = new TableTHElement(); AppendChild(el); return el; }
}
public sealed class WarichuElement : StructureElement
{
    internal WarichuElement() : base("Warichu") { }
    internal WarichuElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}

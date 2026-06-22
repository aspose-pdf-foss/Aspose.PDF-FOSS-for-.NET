using System.Text;
using Aspose.Pdf.Core;
using LS = Aspose.Pdf.LogicalStructure;

namespace Aspose.Pdf.Tagged;

/// <summary>
/// Provides access to tagged PDF metadata (title, language) and the logical structure tree.
/// Implements <see cref="ITaggedContent"/>.
/// </summary>
public sealed class TaggedContent : ITaggedContent
{
    private LS.StructTreeRootElement? _lsRoot;
    private LS.StructureElement? _lsRootElement;
    private LS.StructureTextState? _lsTextState;
    private readonly Document _document;

    private TaggedContent(Document document)
    {
        _document = document;
    }

    /// <summary>
    /// Create a TaggedContent instance. Returns null if the document is not tagged.
    /// </summary>
    internal static TaggedContent? Create(Document document)
    {
        if (!document.IsTagged) return null;
        return new TaggedContent(document);
    }

    /// <summary>
    /// Create a TaggedContent instance for a new (potentially untagged) document.
    /// Ensures the document has MarkInfo and StructTreeRoot.
    /// Creates an in-memory root element that does not depend on the PDF reader.
    /// </summary>
    internal static TaggedContent CreateForNewDocument(Document document)
    {
        // Ensure the document is marked as tagged
        if (!document.IsTagged)
        {
            var markInfo = new PdfDictionary();
            markInfo.Set("Marked", PdfBoolean.True);
            document.Catalog.Set("MarkInfo", markInfo);
        }

        // Ensure StructTreeRoot exists in the catalog (for IsTagged checks)
        if (!document.Catalog.ContainsKey("StructTreeRoot"))
        {
            var structTreeRoot = new PdfDictionary();
            structTreeRoot.Set("Type", new PdfName("StructTreeRoot"));
            var rootObjNum = document.AllocateObjectNumber();
            document.AddNewObject(rootObjNum, structTreeRoot);
            document.Catalog.Set("StructTreeRoot", new PdfIndirectRef(rootObjNum, 0));
        }

        return new TaggedContent(document);
    }

    /// <summary>
    /// The document title (from the Info dictionary).
    /// </summary>
    public string? Title => _document.Info.Title;

    /// <summary>
    /// The document language (BCP 47 tag, from the catalog /Lang entry).
    /// </summary>
    public string? Language => _document.Language;

    /// <summary>
    /// Set the document title in the information dictionary.
    /// </summary>
    public void SetTitle(string title)
    {
        _document.Info.Title = title;
    }

    /// <summary>
    /// Set the document language (BCP 47 tag, e.g. "en-US").
    /// </summary>
    public void SetLanguage(string language)
    {
        _document.Language = language;
    }

    // ── ITaggedContent implementation ────────────────────────────────────────

    private LS.RoleMap? _roleMapCache;
    private LS.IdRegistry? _idRegistryCache;

    /// <summary>Role map shared by every element this facade creates, so
    /// SetTag can detect conflicting custom-tag mappings across siblings
    /// even before they're appended to the tree.</summary>
    private LS.RoleMap GetRoleMap() => _roleMapCache ??= new LS.RoleMap(GetOrCreateStructTreeRoot()._dict);

    /// <summary>ID registry shared by every element this facade creates, so
    /// SetId can reject duplicate identifiers across siblings.</summary>
    private LS.IdRegistry GetIdRegistry() => _idRegistryCache ??= new LS.IdRegistry();

    /// <summary>Stamp a freshly created element with the shared role map
    /// and ID registry.</summary>
    private T Track<T>(T element) where T : LS.StructureElement
    {
        element._roleMap = GetRoleMap();
        element._idRegistry = GetIdRegistry();
        return element;
    }

    /// <summary>Read-only view of the document's structure-tree root
    /// (/StructTreeRoot), or null when the document is not tagged.</summary>
    public Aspose.Pdf.Tagged.StructTreeRoot? StructTreeRoot => _document.StructTreeRoot;

    private LS.StructTreeRootElement GetOrCreateStructTreeRoot()
    {
        if (_lsRoot is not null) return _lsRoot;
        var dict = _document.Reader.ResolveDict(_document.Reader.Catalog.Get("StructTreeRoot"));
        if (dict is null)
        {
            dict = new PdfDictionary();
            dict.Set("Type", new PdfName("StructTreeRoot"));
            _document.Reader.Catalog.Set("StructTreeRoot", dict);
        }
        // Assign the cache BEFORE touching GetRoleMap/GetIdRegistry — those
        // call back into GetOrCreateStructTreeRoot and must see the cached
        // instance to avoid infinite recursion.
        _lsRoot = new LS.StructTreeRootElement(dict, _document.Reader);
        _lsRoot._roleMap = GetRoleMap();
        _lsRoot._idRegistry = GetIdRegistry();

        // Seed the ID registry with identifiers already present in a loaded
        // document so SetId can detect duplicates against existing elements.
        foreach (var element in _lsRoot.AllElements)
        {
            var id = element.ID;
            if (!string.IsNullOrEmpty(id))
                _idRegistryCache!.Register(id, element);
        }

        return _lsRoot;
    }

    /// <inheritdoc />
    LS.StructTreeRootElement ITaggedContent.StructTreeRootElement => GetOrCreateStructTreeRoot();

    /// <inheritdoc />
    LS.StructureElement ITaggedContent.RootElement
    {
        get
        {
            if (_lsRootElement is not null) return _lsRootElement;
            var root = GetOrCreateStructTreeRoot();
            if (root.ChildElements.Count > 0)
                _lsRootElement = root.ChildElements[0];
            else
            {
                var doc = new LS.PartElement();
                root.AppendChild(doc);
                _lsRootElement = doc;
            }
            return _lsRootElement;
        }
    }

    /// <inheritdoc />
    LS.StructureTextState ITaggedContent.StructureTextState =>
        _lsTextState ??= new LS.StructureTextState();

    LS.AnnotElement ITaggedContent.CreateAnnotElement() => Track(new LS.AnnotElement());
    LS.ArtElement ITaggedContent.CreateArtElement() => Track(new LS.ArtElement());
    LS.BibEntryElement ITaggedContent.CreateBibEntryElement() => Track(new LS.BibEntryElement());
    LS.BlockQuoteElement ITaggedContent.CreateBlockQuoteElement() => Track(new LS.BlockQuoteElement());
    LS.CaptionElement ITaggedContent.CreateCaptionElement() => Track(new LS.CaptionElement());
    LS.CodeElement ITaggedContent.CreateCodeElement() => Track(new LS.CodeElement());
    LS.DivElement ITaggedContent.CreateDivElement() => Track(new LS.DivElement());
    LS.FigureElement ITaggedContent.CreateFigureElement() => Track(new LS.FigureElement());
    LS.FormElement ITaggedContent.CreateFormElement() => Track(new LS.FormElement());
    LS.FormulaElement ITaggedContent.CreateFormulaElement() => Track(new LS.FormulaElement());
    LS.HeaderElement ITaggedContent.CreateHeaderElement() => Track(new LS.HeaderElement());
    LS.HeaderElement ITaggedContent.CreateHeaderElement(int level) => Track(new LS.HeaderElement(level));
    LS.IndexElement ITaggedContent.CreateIndexElement() => Track(new LS.IndexElement());
    LS.LinkElement ITaggedContent.CreateLinkElement() => Track(new LS.LinkElement());
    LS.ListElement ITaggedContent.CreateListElement() => Track(new LS.ListElement());
    LS.ListLBodyElement ITaggedContent.CreateListLBodyElement() => Track(new LS.ListLBodyElement());
    LS.ListLIElement ITaggedContent.CreateListLIElement() => Track(new LS.ListLIElement());
    LS.ListLblElement ITaggedContent.CreateListLblElement() => Track(new LS.ListLblElement());
    LS.NonStructElement ITaggedContent.CreateNonStructElement() => Track(new LS.NonStructElement());
    LS.NoteElement ITaggedContent.CreateNoteElement() => Track(new LS.NoteElement());
    LS.ParagraphElement ITaggedContent.CreateParagraphElement() => Track(new LS.ParagraphElement());
    LS.PartElement ITaggedContent.CreatePartElement() => Track(new LS.PartElement());
    LS.PrivateElement ITaggedContent.CreatePrivateElement() => Track(new LS.PrivateElement());
    LS.QuoteElement ITaggedContent.CreateQuoteElement() => Track(new LS.QuoteElement());
    LS.ReferenceElement ITaggedContent.CreateReferenceElement() => Track(new LS.ReferenceElement());
    LS.RubyElement ITaggedContent.CreateRubyElement() => Track(new LS.RubyElement());
    LS.SectElement ITaggedContent.CreateSectElement() => Track(new LS.SectElement());
    LS.SpanElement ITaggedContent.CreateSpanElement() => Track(new LS.SpanElement());
    LS.TOCElement ITaggedContent.CreateTOCElement() => Track(new LS.TOCElement());
    LS.TOCIElement ITaggedContent.CreateTOCIElement() => Track(new LS.TOCIElement());
    LS.TableElement ITaggedContent.CreateTableElement() => Track(new LS.TableElement());
    LS.TableTBodyElement ITaggedContent.CreateTableTBodyElement() => Track(new LS.TableTBodyElement());
    LS.TableTDElement ITaggedContent.CreateTableTDElement() => Track(new LS.TableTDElement());
    LS.TableTFootElement ITaggedContent.CreateTableTFootElement() => Track(new LS.TableTFootElement());
    LS.TableTHElement ITaggedContent.CreateTableTHElement() => Track(new LS.TableTHElement());
    LS.TableTHeadElement ITaggedContent.CreateTableTHeadElement() => Track(new LS.TableTHeadElement());
    LS.TableTRElement ITaggedContent.CreateTableTRElement() => Track(new LS.TableTRElement());
    LS.WarichuElement ITaggedContent.CreateWarichuElement() => Track(new LS.WarichuElement());

    /// <summary>No-op hook: the FOSS save path doesn't run an explicit
    /// pre-save normalisation pass over the structure tree yet — children
    /// appended via the LogicalStructure builder are already written into
    /// the underlying /K array by <see cref="LS.StructureElement.AppendChild"/>.</summary>
    void ITaggedContent.PreSave() { }

    /// <summary>Ensure /Catalog/MarkInfo /Marked is set, then leave
    /// /StructTreeRoot in place — element dictionaries are already
    /// linked into /K by AppendChild, so re-writing the document picks
    /// up the structure tree automatically.</summary>
    void ITaggedContent.Save()
    {
        var catalog = _document.Catalog;
        var markInfo = _document.Reader.ResolveDict(catalog.Get("MarkInfo"));
        if (markInfo is null)
        {
            markInfo = new PdfDictionary();
            catalog.Set("MarkInfo", markInfo);
        }
        markInfo.Set("Marked", PdfBoolean.True);
        GetOrCreateStructTreeRoot();
    }

    void ITaggedContent.SetLanguage(string lang) => SetLanguage(lang);
    void ITaggedContent.SetTitle(string title) => SetTitle(title);
}

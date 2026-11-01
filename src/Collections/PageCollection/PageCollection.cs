using System.Collections;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

/// <summary>
/// Collection of pages in a PDF document.
/// </summary>
public sealed partial class PageCollection : IEnumerable<Page>
{
    private readonly PdfReader _reader;
    private List<Page>? _pages;
    private readonly List<PendingPage> _pendingAdds = [];
    private readonly HashSet<int> _deletedIndices = [];
    private readonly List<(int objNum, PdfObject obj)> _importedObjects = [];

    /// <summary>The fields materialised so far by one multi-page import, keyed by the
    /// SOURCE field they came from (source reader + the widget's /Parent object). A
    /// foreign field whose widgets sit on several imported pages must end up as ONE
    /// field with kids, not one renamed field per widget - the map lives for a batch
    /// Add/Insert and is page-local for a single-page import.</summary>
    private Dictionary<(PdfReader reader, int parentObjNum),
        (PdfIndirectRef fieldRef, PdfDictionary fieldDict, int fieldsIndex, bool promoted)>?
        _importedFieldOwners;

    /// <summary>Field-level entries that move from the first imported widget onto the
    /// promoted field dictionary (the widget keeps its appearance keys).</summary>
    private static readonly string[] PromotedFieldKeys = ["T", "FT", "Ff", "V", "DV", "Opt", "TU", "TM", "MaxLen"];

    /// <summary>Inheritable presentation entries the promoted field copies while the
    /// widget keeps its own.</summary>
    private static readonly string[] SharedFieldKeys = ["DA", "Q"];

    /// <summary>Turn an imported merged field-widget into a pure field dictionary whose
    /// /Kids holds that widget; the AcroForm /Fields slot is re-pointed at the field.</summary>
    private (PdfIndirectRef fieldRef, PdfDictionary fieldDict, int fieldsIndex, bool promoted)
        PromoteImportedWidgetToField(
            (PdfIndirectRef fieldRef, PdfDictionary fieldDict, int fieldsIndex, bool promoted) owner,
            PdfArray fieldsArr)
    {
        var field = new PdfDictionary();
        foreach (var key in PromotedFieldKeys)
        {
            if (owner.fieldDict.Get(key) is not { } value) continue;
            field.Set(key, value);
            owner.fieldDict.Remove(key);
        }
        foreach (var key in SharedFieldKeys)
            if (owner.fieldDict.Get(key) is { } value) field.Set(key, value);

        var fieldObjNum = ImportObjNumBase();
        _importedObjects.Add((fieldObjNum, field));
        var fieldRef = new PdfIndirectRef(fieldObjNum, 0);

        var kids = new PdfArray();
        kids.Add(owner.fieldRef);
        field.Set("Kids", kids);
        owner.fieldDict.Set("Parent", fieldRef);
        fieldsArr.ReplaceAt(owner.fieldsIndex, fieldRef);
        return (fieldRef, field, owner.fieldsIndex, true);
    }

    // Cache of cloned objects per source reader, so shared resources (images, fonts)
    // referenced by multiple pages from the same source document are cloned only once.
    // This prevents output bloat when adding many pages that share the same resources.
    // Keyed WEAKLY on the source reader: the dedup cache survives while the caller is
    // still importing from that source, but a strong key would pin every merged-in
    // document's reader/stream/byte[] for the destination's lifetime.
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<PdfReader, Dictionary<int, PdfObject>> _cloneCache = new();

    // A GoTo/Link annotation on an imported page targets another page via an indirect
    // reference. Following that reference during RemapObject would deep-import the whole
    // target page (its contents, resources, images) — bloating a few-page copy to the
    // source document's full size. Instead each source page object
    // number is mapped to a reserved "slot" object number: the destination reference is
    // pointed at the slot and the page itself, when it is among the copied pages, is
    // written at that slot (see RebuildPagesTree) so the destination resolves to the
    // imported page. Slots for pages that were not copied resolve to null (a valid, empty
    // PDF destination).
    //
    // Keyed WEAKLY on the source reader (like _cloneCache): a strong key would pin every
    // merged-in source document's reader/stream/byte[] for the destination's lifetime
    // Dedup only needs to hold while the caller is still importing from
    // that source; the allocated slot numbers live on independently (Page.ImportSlotObjNum
    // and the int refs already written into destination arrays).
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<PdfReader, Dictionary<int, int>> _importPageSlots = new();

    // Total slots allocated and the highest slot number, tracked outside the weak table so
    // object-number allocation and the writer's reservation don't need to enumerate it.
    private int _slotCount;
    private int _maxSlotObjNum;

    // Slots already bound to a written page. The same source page may be imported several
    // times (e.g. a template page copied per output page); only the FIRST copy is written
    // at the shared destination slot, so later copies must take their own object number or
    // all copies would collide at one number and the file would show a single page.
    private readonly HashSet<int> _claimedSlots = new();

    internal PageCollection(PdfReader reader)
    {
        _reader = reader;
    }

    /// <summary>The document that owns this page collection.</summary>
    internal Document? OwnerDocument { get; set; }

    /// <summary>Total number of pages.</summary>
    public int Count
    {
        get
        {
            EnsurePages();
            return _pages!.Count;
        }
    }

    /// <summary>
    /// Get page by 1-based index.
    /// </summary>
    public Page this[int index]
    {
        get
        {
            EnsurePages();
            if (index < 1 || index > _pages!.Count)
                throw new ArgumentOutOfRangeException(nameof(index),
                    $"Page number must be between 1 and {_pages!.Count}. Got: {index}");
            return _pages[index - 1];
        }
    }

    /// <summary>
    /// Alias for indexer — get page by 1-based page number.
    /// </summary>
    public Page At(int oneBasedIndex) => this[oneBasedIndex];

    /// <summary>
    /// Returns the 1-based index of the given page, or -1 if not found.
    ///
    /// </summary>
    public int IndexOf(Page? entity)
    {
        if (entity is null) return -1;
        EnsurePages();
        for (int i = 0; i < _pages!.Count; i++)
        {
            if (_pages[i] is null) continue; // slot nulled by a corrupt-source merge
            if (ReferenceEquals(_pages[i], entity) || ReferenceEquals(_pages[i].Dict, entity.Dict))
                return i + 1; // 1-based
        }
        return -1;
    }

    /// <summary>Accept a TextAbsorber visitor; iterates every page.</summary>
    public void Accept(Text.TextAbsorber visitor)
    {
        OwnerDocument?.LayOutXfaForm();
        foreach (var page in this.Where(page => page is not null))
            visitor.Visit(page);
    }

    /// <summary>Accept a TextFragmentAbsorber visitor; accumulates fragments across all pages.</summary>
    public void Accept(Text.TextFragmentAbsorber visitor)
    {
        // Clear once then visit all pages without clearing between them.
        // Whole-document sweeps are tolerant of undecodable fonts — one bad
        // font must not abort the other pages (strictness is a page-level
        // Accept behaviour).
        OwnerDocument?.LayOutXfaForm();
        visitor.TextFragments.Clear();
        // One dedup set for the WHOLE walk, exactly as Visit(Document) does: a Form
        // XObject shared by every page (a running header, say) is ONE piece of content
        // in ONE stream, so it yields ONE result — editing it edits every page that
        // draws it. Without the shared set the same header came back once per page.
        var seenForms = new Text.TextFragmentAbsorber.FormDedupSet();
        foreach (var page in this.Where(page => page is not null))
            visitor.VisitInternal(page, tolerantFonts: true, seenForms: seenForms);
    }

    /// <summary>Accept an ImagePlacementAbsorber visitor across every page.</summary>
    public void Accept(ImagePlacementAbsorber visitor)
    {
        foreach (var page in this.Where(page => page is not null))
            visitor.Visit(page);
    }

    /// <summary>Walk every annotation on every page and dispatch it to
    /// <paramref name="visitor"/>. Selected matches accumulate in
    /// <see cref="Annotations.AnnotationSelector.Selected"/>.</summary>
    public void Accept(Annotations.AnnotationSelector visitor)
    {
        if (visitor is null) return;
        foreach (var page in this)
            page?.Annotations.Accept(visitor);
    }

    /// <summary>
    /// Add a new blank page. Uses the most frequent page size from existing pages,
    /// or US Letter (612x792) if the document is empty. Matches the public API behavior.
    /// </summary>
    public Page Add()
    {
        EnsurePages();
        var (w, h) = GetMostFrequentPageSize();
        var pageDict = CreatePageDict(w, h);
        var page = new Page(pageDict, _reader, _pages!.Count);
        _pages.Add(page);
        _pendingAdds.Add(new PendingPage(pageDict));
        // Propagate Document.PageInfo.Margin to the new page so callers that
        // configure margins on the document level (rather than per-page) see
        // them honoured during content layout. Only when the doc-level margin
        // was explicitly set (IsTouched=true) -- otherwise writing zeros from
        // a default-constructed MarginInfo would still trip the page's
        // IsTouched flag and override the layout's 72-pt fallback for users
        // who never touched margins at all.
        var docMargin = OwnerDocument?.PageInfo?.Margin;
        if (docMargin is { IsTouched: true })
        {
            // Copy only the sides the caller actually set on the document. Copying
            // an untouched (zero) side would touch it on the page and override the
            // layout's per-side default -- e.g. setting only Left/Right for a
            // multi-column box must leave Top/Bottom at the 72-pt default.
            var pm = page.PageInfo.Margin;
            if (docMargin.LeftTouched)   pm.Left   = docMargin.Left;
            if (docMargin.RightTouched)  pm.Right  = docMargin.Right;
            if (docMargin.TopTouched)    pm.Top    = docMargin.Top;
            if (docMargin.BottomTouched) pm.Bottom = docMargin.Bottom;
        }
        return page;
    }

    /// <summary>
    /// Add a new blank page with the given dimensions.
    /// </summary>
    public Page Add(double width, double height)
    {
        EnsurePages();
        var pageDict = CreatePageDict(width, height);
        var page = new Page(pageDict, _reader, _pages!.Count);
        _pages.Add(page);
        _pendingAdds.Add(new PendingPage(pageDict));
        return page;
    }

    /// <summary>
    /// Add a page from another (or the same) document by deep-cloning its page dictionary.
    /// All indirect references from the source reader are resolved and inlined.
    /// </summary>
    public Page Add(Page entity)
    {
        EnsurePages();
        entity = ImportSource(entity);
        var clonedDict = ClonePageForImport(entity.Dict, entity.Reader);
        clonedDict.Remove("Parent");
        var added = AddFromDict(clonedDict);
        BindImportedPageSlot(added, entity.Reader, entity.SourceObjectNumber);
        ImportFormFieldsFromPage(clonedDict, entity.Dict, entity.Reader);
        CarryOverPendingContent(entity, added);
        return added;
    }

    /// <summary>
    /// Add all pages from another page collection (cross-document merge).
    /// Safe for self-copy (doc.Pages.Add(doc.Pages)).
    /// </summary>
    public void Add(PageCollection otherPages)
    {
        // Flush the source document's pending generator content (Paragraphs, Headers,
        // Footers) into its page content streams before importing, so DOM paragraphs
        // added to the source pages survive the merge — the source document may never
        // be saved on its own, and the import clones the raw page content stream.
        // ProcessParagraphs is idempotent (per-page LayoutApplied gate).
        if (!ReferenceEquals(otherPages, this))
        {
            otherPages.OwnerDocument?.ProcessParagraphs();
            // A form a viewer has to build from XFA packets holds only a notice page: the
            // source gives its laid-out pages, and a receiving form becomes a standard one,
            // since a viewer told to build the pages from its packets would never show the
            // added ones.
            otherPages.OwnerDocument?.LayOutXfaForm();
            OwnerDocument?.LayOutXfaForm(convert: true);
        }

        // Snapshot to avoid issues when otherPages == this (self-copy)
        var snapshot = new List<Page>();
        foreach (var page in otherPages.Where(page => page is not null))
            snapshot.Add(page);

        _importedFieldOwners = new();
        try
        {
        foreach (var page in snapshot)
        {
            // Expected behaviour (probed): the merge resolves source pages strictly
            // via the DECLARED xref — a kid whose object never loaded (an
            // unresolved placeholder) or that only exists thanks to the full-scan
            // xref recovery is NOT copied, and the SOURCE collection permanently
            // reports that slot as null afterwards (its Count stays unchanged).
            if (page.IsUnresolvedStub || !page.Reader.IsObjectDeclaredReachable(page.SourceObjectNumber))
            {
                otherPages.PoisonUnresolvedSlot(page);
                continue;
            }
            EnsurePages();
            var clonedDict = ClonePageForImport(page.Dict, page.Reader);
            clonedDict.Remove("Parent");
            var added = AddFromDict(clonedDict);
            BindImportedPageSlot(added, page.Reader, page.SourceObjectNumber);
            ImportFormFieldsFromPage(clonedDict, page.Dict, page.Reader);
        }
        }
        finally { _importedFieldOwners = null; }
    }

    /// <summary>
    /// Add multiple pages from a collection (cross-document merge).
    /// </summary>
    public void Add(ICollection<Page> pages)
    {
        AddPagesFromEnumerable(pages);
    }

    /// <summary>
    /// Add multiple pages from an array (cross-document merge).
    /// </summary>
    public void Add(Page[] pages)
    {
        AddPagesFromEnumerable(pages);
    }

    /// <summary>
    /// Add a new blank page bypassing licensing page-count restrictions. This build
    /// carries no evaluation limits, so the behaviour matches <see cref="Add()"/>.
    /// </summary>
    public Page AddUnrestricted()
    {
        return Add();
    }

    /// <summary>
    /// Copy a source page's not-yet-rendered generator content to a freshly imported page.
    /// Paragraphs are flushed into the page content stream only at save time, so a page added
    /// before save would otherwise contribute an empty dictionary and lose its content.
    /// </summary>
    private static void CarryOverPendingContent(Page source, Page target)
    {
        if (source.Paragraphs is { Count: > 0 })
            target.Paragraphs = source.Paragraphs;
        // The margins the paragraphs are laid out at travel with them; the size is
        // already the copied page's own.
        if (source.HasPageInfo)
            target.PageInfo.InheritFrom(source.PageInfo);
    }

    private void AddPagesFromEnumerable(IEnumerable<Page> pages)
    {
        var snapshot = new List<(PdfDictionary dict, PdfReader reader, int srcObjNum)>();
        foreach (var page in pages.Select(ImportSource))
            snapshot.Add((page.Dict, page.Reader, page.SourceObjectNumber));

        _importedFieldOwners = new();
        try
        {
        foreach (var (dict, reader, srcObjNum) in snapshot)
        {
            EnsurePages();
            var clonedDict = ClonePageForImport(dict, reader);
            clonedDict.Remove("Parent");
            var added = AddFromDict(clonedDict);
            BindImportedPageSlot(added, reader, srcObjNum);
            ImportFormFieldsFromPage(clonedDict, dict, reader);
        }
        }
        finally { _importedFieldOwners = null; }
    }

    /// <summary>The page an import from another document copies: a page of a form a viewer has to build from
    /// XFA packets is only its notice page, so the form is laid out first and its page of that number is copied,
    /// as an import of the whole collection does.</summary>
    private Page ImportSource(Page page) =>
        page.OwnerDocument is { } source && !ReferenceEquals(source, OwnerDocument) ? Document.PageToRead(page) : page;

    /// <summary>
    /// Add a page with the given dictionary (used for page import).
    /// </summary>
    internal Page AddFromDict(PdfDictionary pageDict, PdfReader? sourceReader = null)
    {
        EnsurePages();
        var page = new Page(pageDict, sourceReader ?? _reader, _pages!.Count);
        _pages.Add(page);
        _pendingAdds.Add(new PendingPage(pageDict));
        return page;
    }

    /// <summary>
    /// Insert a page with the given dictionary before the given 1-based index (used for page import).
    /// </summary>
    internal Page InsertFromDict(int beforeOneBasedIndex, PdfDictionary pageDict)
    {
        EnsurePages();
        if (beforeOneBasedIndex < 1 || beforeOneBasedIndex > _pages!.Count + 1)
            throw new ArgumentOutOfRangeException(nameof(beforeOneBasedIndex));

        var insertIndex = beforeOneBasedIndex - 1;
        var page = new Page(pageDict, _reader, insertIndex);
        _pages.Insert(insertIndex, page);
        _pendingAdds.Add(new PendingPage(pageDict, insertIndex));

        // Re-index pages after insertion (in-place to avoid orphaning returned page)
        for (var i = insertIndex; i < _pages.Count; i++)
            _pages[i]?.SetIndex(i);

        return page;
    }

    /// <summary>
    /// Insert a new blank page before the given 1-based index.
    /// Uses the most frequent page size from existing pages, or US Letter if empty.
    /// </summary>
    public Page Insert(int pageNumber)
    {
        // A no-size Insert inherits the document's prevailing page size
        // (a TOC page inserted into a US-Letter document
        // renders 612×792). The page is flagged size-inherited: if the caller
        // then REQUESTS landscape via PageInfo.IsLandscape, layout replaces
        // the inherited box with the A4-landscape default (842×595) — such
        // a page resolves from its PageInfo defaults, not from
        // the inherited box.
        var (w, h) = GetMostFrequentPageSize();
        var page = Insert(pageNumber, w, h);
        page.SizeInherited = true;
        return page;
    }

    /// <summary>
    /// Insert a new blank page with explicit dimensions before the given 1-based index.
    /// </summary>
    public Page Insert(int pageNumber, double width, double height)
    {
        EnsurePages();
        if (pageNumber < 1 || pageNumber > _pages!.Count + 1)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));

        var pageDict = CreatePageDict(width, height);
        var insertIndex = pageNumber - 1;
        var page = new Page(pageDict, _reader, insertIndex);
        _pages.Insert(insertIndex, page);
        _pendingAdds.Add(new PendingPage(pageDict, insertIndex));

        // Re-index pages after insertion (in-place to avoid orphaning returned page)
        for (var i = insertIndex; i < _pages.Count; i++)
            _pages[i]?.SetIndex(i);

        return page;
    }

    /// <summary>
    /// Insert a single page (from any document) before the given 1-based index.
    /// The page is deep-cloned so changes to the source page after insertion do not affect the copy.
    /// </summary>
    public Page Insert(int pageNumber, Page entity)
    {
        EnsurePages();
        if (pageNumber < 1 || pageNumber > _pages!.Count + 1)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));

        var clonedDict = ClonePageForImport(entity.Dict, entity.Reader);
        clonedDict.Remove("Parent");
        var inserted = InsertFromDict(pageNumber, clonedDict);
        BindImportedPageSlot(inserted, entity.Reader, entity.SourceObjectNumber);
        ImportFormFieldsFromPage(clonedDict, entity.Dict, entity.Reader);
        CarryOverPendingContent(entity, inserted);
        return inserted;
    }

    /// <summary>
    /// Insert multiple pages from a collection before the given 1-based index (cross-document merge).
    /// </summary>
    public void Insert(int pageNumber, ICollection<Page> pages)
    {
        InsertPagesFromEnumerable(pageNumber, pages);
    }

    /// <summary>
    /// Insert multiple pages from an array before the given 1-based index (cross-document merge).
    /// </summary>
    public void Insert(int pageNumber, Page[] pages)
    {
        InsertPagesFromEnumerable(pageNumber, pages);
    }

    /// <summary>
    /// Delete a page by 1-based page number.
    /// </summary>
    public void Delete(int index)
    {
        EnsurePages();
        if (index < 1 || index > _pages!.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        var zeroBased = index - 1;
        var deletedPage = _pages[zeroBased];
        _deletedIndices.Add(zeroBased);
        _pages.RemoveAt(zeroBased);
        // The page's objects stay in the file's table; the save writes only what is still reachable.
        _reader.MayHaveOrphansOnSave = true;

        // Mark deleted page as removed (Number returns -1); a kid the file held nowhere has no page
        deletedPage?.SetIndex(-2);

        // Re-index remaining pages (in-place)
        for (var i = zeroBased; i < _pages.Count; i++)
            _pages[i]?.SetIndex(i);
    }

    /// <summary>
    /// Delete multiple pages by 1-based page numbers.
    /// </summary>
    public void Delete(params int[] pages)
    {
        // Deduplicate then sort descending to avoid index shifting issues
        foreach (var num in pages.Distinct().OrderDescending())
            Delete(num);
    }

    /// <summary>
    /// Delete all pages from the document.
    /// </summary>
    public void Delete()
    {
        EnsurePages();
        var count = _pages!.Count;
        // Delete all pages from last to first to avoid index shifting
        for (var i = count; i >= 1; i--)
            Delete(i);
    }

    private void InsertPagesFromEnumerable(int pageNumber, IEnumerable<Page> pages)
    {
        var snapshot = new List<(PdfDictionary dict, PdfReader reader, int srcObjNum)>();
        foreach (var page in pages.Select(ImportSource))
            snapshot.Add((page.Dict, page.Reader, page.SourceObjectNumber));

        for (var i = 0; i < snapshot.Count; i++)
        {
            var (dict, reader, srcObjNum) = snapshot[i];
            EnsurePages();
            var clonedDict = ClonePageForImport(dict, reader);
            clonedDict.Remove("Parent");
            var inserted = InsertFromDict(pageNumber + i, clonedDict);
            BindImportedPageSlot(inserted, reader, srcObjNum);
        }
    }

    /// <summary>Whether pages have been added or deleted (requiring full rewrite on save).</summary>
    internal bool IsModified => _pendingAdds.Count > 0 || _deletedIndices.Count > 0;

    /// <summary>Get all pending page dictionaries for writing.</summary>
    internal IReadOnlyList<PendingPage> PendingAdds => _pendingAdds;
    internal IReadOnlyList<(int objNum, PdfObject obj)> ImportedObjects => _importedObjects;

    /// <summary>Clears cached data on every page.</summary>
    public void FreeMemory()
    {
        if (_pages is null) return;
        foreach (var page in _pages)
            page?.FreeMemory();
    }

#pragma warning disable S2325 // public API: the published instance member
    /// <summary>Whether the collection is read-only. Always false.</summary>
    public bool IsReadOnly => false;
#pragma warning restore S2325

#pragma warning disable S2325 // public API: the published instance member
    /// <summary>Whether the collection is thread-safe. Always false.</summary>
    public bool IsSynchronized => false;
#pragma warning restore S2325

    /// <summary>Synchronization root for <see cref="IsSynchronized"/>; returns this collection.</summary>
    public object SyncRoot => this;

    /// <summary>Suspend page-tree maintenance during a batch of mutations. Stored only; no batching is currently performed.</summary>
    public void BeginUpdate()
    {
        // No-op kept for API compatibility.
    }

    /// <summary>Resume page-tree maintenance after a <see cref="BeginUpdate"/> batch.</summary>
    public void EndUpdate()
    {
        // No-op kept for API compatibility.
    }

    /// <summary>Remove every page from the collection.</summary>
    public void Clear() => Delete();

    /// <summary>Whether the supplied page belongs to this collection.</summary>
    public bool Contains(Page item) => item is not null && IndexOf(item) > 0;

    /// <summary>Copy the collection contents into an array starting at <paramref name="index"/>.</summary>
    public void CopyTo(Page[] array, int index)
    {
        if (array is null) throw new ArgumentNullException(nameof(array));
        EnsurePages();
        for (var i = 0; i < _pages!.Count; i++)
            array[index + i] = _pages[i];
    }

    /// <summary>Flatten every form and annotation on every page. The current implementation
    /// delegates to <see cref="Forms.Form.Flatten()"/> when the document has a form.</summary>
    public void Flatten()
    {
        if (OwnerDocument is not null && OwnerDocument.HasForm)
            OwnerDocument.Form.Flatten(OwnerDocument);
    }

    /// <summary>Remove the supplied page and report whether it was present.</summary>
    public bool Remove(Page item)
    {
        if (item is null) return false;
        var idx = IndexOf(item);
        if (idx < 1) return false;
        Delete(idx);
        return true;
    }

    public IEnumerator<Page> GetEnumerator()
    {
        EnsurePages();
        return Walk();
    }

    // One enumerator type for both walks. Returning the list's own enumerator for the usual
    // document and an iterator for a form that awaits its layout crashed the .NET 10 runtime:
    // its profile-guided JIT keeps the enumerator this method usually returns on the caller's
    // stack, and a walk that got the other one faulted in MoveNext (AccessViolationException
    // on every save of a dynamic form, net10.0 only).
    private IEnumerator<Page> Walk()
    {
        if (OwnerDocument is { AwaitsXfaLayout: true })
        {
            // A form laid out while its notice page is being read replaces the pages: such a
            // walk goes by index, so it carries on over the form's pages.
            for (var i = 0; i < _pages!.Count; i++)
                yield return _pages[i];
            yield break;
        }
        foreach (var page in _pages!)
            yield return page;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void EnsurePages()
    {
        if (_pages is not null) return;

        var catalog = _reader.Catalog;
        var pagesDict = _reader.ResolveDict(catalog.Get("Pages"));
        if (pagesDict is null)
        {
            // Corrupt or minimal PDF: gracefully treat as empty document
            _pages = new List<Page>();
            return;
        }

        var pageList = new List<Page>();
        var pagesObjNum = (catalog.Get("Pages") as PdfIndirectRef)?.ObjectNumber ?? -1;
        CollectPages(pagesDict, pageList, pagesObjNum);
        _pages = pageList;
    }

    /// <summary>The leaves of the page tree in order, depth first. The walk keeps its own stack, so a tree
    /// thousands of levels deep cannot overflow the call stack, and a /Kids entry naming a node of the path
    /// that leads to it (a cycle) is skipped: walked, it would lead back to itself forever.</summary>
    private void CollectPages(PdfDictionary root, List<Page> result, int rootObjNum)
    {
        var rootKids = KidsOrLeaf(root, result, rootObjNum);
        if (rootKids is null) return;

        var path = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance) { root };
        var stack = new Stack<(PdfDictionary Node, PdfArray Kids, int Next)>();
        stack.Push((root, rootKids, 0));
        while (stack.Count > 0)
        {
            var (node, kids, next) = stack.Pop();
            var descended = false;
            while (next < kids.Count && !descended)
            {
                var kid = kids[next++];
                var kidObjNum = (kid as PdfIndirectRef)?.ObjectNumber ?? -1;
                var kidDict = _reader.ResolveDict(kid);
                if (kidDict is null)
                {
                    AddUnresolvedKid(result, kidObjNum);
                    continue;
                }
                // A node already on the path has kids, so a leaf never meets the path set.
                var grandKids = KidsOrLeaf(kidDict, result, kidObjNum);
                if (grandKids is null || !path.Add(kidDict)) continue;
                stack.Push((node, kids, next));
                stack.Push((kidDict, grandKids, 0));
                descended = true;
            }
            if (!descended) path.Remove(node);
        }
    }

    /// <summary>The node's /Kids; a leaf is added to <paramref name="result"/> instead and gives null.</summary>
    private PdfArray? KidsOrLeaf(PdfDictionary node, List<Page> result, int nodeObjNum)
    {
        // Check for /Kids first — a corrupt PDF may have /Type /Page on a
        // tree node that actually has children.
        if (_reader.Resolve(node.Get("Kids")) is PdfArray kids) return kids;

        // No /Kids — a page, or a node with page-like properties (Contents, Resources)
        // taken as a leaf page despite a wrong /Type (e.g. a "Pages" typo on a leaf)
        if (node.GetName("Type") == "Page" || node.ContainsKey("Contents") || node.ContainsKey("Resources"))
            result.Add(new Page(node, _reader, result.Count) { SourceObjectNumber = nodeObjNum });
        return null;
    }

    private void AddUnresolvedKid(List<Page> result, int kidObjNum)
    {
        if (_reader.XRefTable.RecoveredFromBrokenTail)
        {
            // Broken-tail read: a kid the file holds NOWHERE is a NULL slot —
            // the declared /Count survives, the indexer reports null (measured
            // on such a first kid; an exception surfaces only when a caller
            // hands that null onward, e.g. TextAbsorber.Visit).
            result.Add(null!);
            return;
        }
        // Unresolvable kid (corrupt/zeroed-out object stream) — add a placeholder page so
        // PageCount matches the declared page tree structure for partially corrupt PDFs.
        var placeholder = new PdfDictionary();
        placeholder.Set("Type", new PdfName("Page"));
        result.Add(new Page(placeholder, _reader, result.Count)
        {
            SourceObjectNumber = kidObjNum,
            IsUnresolvedStub = true,
        });
    }

    /// <summary>Drops the slots of kids the file holds nowhere (placeholders and null slots), so the
    /// tree a save writes lists real pages only. True when any was dropped.</summary>
    internal bool DropUnresolvedPages()
    {
        EnsurePages();
        var dropped = false;
        for (var i = _pages!.Count; i >= 1; i--)
        {
            if (_pages[i - 1] is { IsUnresolvedStub: false }) continue;
            Delete(i);
            dropped = true;
        }
        return dropped;
    }

    private (double width, double height) GetMostFrequentPageSize()
    {
        EnsurePages();
        if (_pages!.Count == 0)
        {
            // Honour Document.PageInfo defaults for the first page so callers
            // that set `doc.PageInfo.Width = N` before `doc.Pages.Add()` get the
            // configured size instead of falling back to A4.
            var docInfo = OwnerDocument?.PageInfo;
            if (docInfo is not null)
                return (docInfo.Width, docInfo.Height);
            return (595, 842); // A4, the library default
        }

        // Find most frequent page size from a bounded sample (avoids O(n²) on bulk Add).
        // On ties, use first page's size.
        var sampleCount = Math.Min(_pages.Count, 100);
        var counts = new Dictionary<(double w, double h), int>();
        for (int i = 0; i < sampleCount; i++)
        {
            if (_pages[i] is null) continue; // slot nulled by a corrupt-source merge
            var box = _pages[i].MediaBox;
            var key = (Math.Round(box.Width, 2), Math.Round(box.Height, 2));
            counts.TryGetValue(key, out var c);
            counts[key] = c + 1;
        }
        if (counts.Count == 0) return (595, 842);
        var maxCount = counts.Values.Max();
        for (int i = 0; i < sampleCount; i++)
        {
            if (_pages[i] is null) continue;
            var box = _pages[i].MediaBox;
            var key = (Math.Round(box.Width, 2), Math.Round(box.Height, 2));
            if (counts[key] == maxCount)
                return (box.Width, box.Height);
        }
        var fb = _pages[0].MediaBox;
        return (fb.Width, fb.Height);
    }

    private static PdfDictionary CreatePageDict(double width, double height)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Page"));

        var mediaBox = new PdfArray();
        mediaBox.Add(new PdfInteger(0));
        mediaBox.Add(new PdfInteger(0));
        mediaBox.Add(new PdfReal(width));
        mediaBox.Add(new PdfReal(height));
        dict.Set("MediaBox", mediaBox);
        // A page must have resources, even none (an empty dictionary): a page left blank was
        // written without them, with nothing to inherit from the page tree.
        dict.Set("Resources", new PdfDictionary());

        return dict;
    }

    /// <summary>
    /// Copy inheritable page properties (MediaBox, CropBox, Rotate, Resources) from the
    /// source page's parent chain when they are not set directly on the page dict.
    /// </summary>
    private void EnsureInherited(PdfDictionary clone, PdfDictionary sourceDict, PdfReader sourceReader)
    {
        foreach (var key in new[] { "MediaBox", "CropBox", "Rotate" })
        {
            if (clone.ContainsKey(key)) continue;
            // Walk the source page's parent chain to find the inherited value
            var value = ResolveInheritedValue(sourceDict, key, sourceReader);
            if (value is not null)
            {
                if (sourceReader == _reader)
                    clone.Set(key, value);
                else
                {
                    var remap = GetOrCreateCloneCache(sourceReader);
                    clone.Set(key, RemapObject(value, sourceReader, remap));
                }
            }
        }

        // Resources inherited from the source page tree must be carried over too —
        // otherwise a cross-document copy of a page whose /Resources lives on a
        // parent /Pages node loses all its fonts/images. Copy the *raw* inherited
        // reference (not the resolved dict) so the clone cache dedupes it: every
        // imported page that inherited the same /Resources ends up sharing one
        // remapped copy, which keeps the file small and lets a later
        // OptimizeResources prune that shared dict to the union of their usage.
        if (!clone.ContainsKey("Resources"))
        {
            var rawRes = ResolveInheritedRawValue(sourceDict, "Resources", sourceReader);
            if (rawRes is not null)
            {
                if (sourceReader == _reader)
                    clone.Set("Resources", rawRes);
                else
                {
                    var remap = GetOrCreateCloneCache(sourceReader);
                    clone.Set("Resources", RemapObject(rawRes, sourceReader, remap));
                }
            }
        }
    }

    /// <summary>Walk the source page's parent chain for <paramref name="key"/> and
    /// return the RAW value (an indirect reference is returned unresolved) so the
    /// clone cache can dedupe a reference shared by several pages.</summary>
    internal static PdfObject? ResolveInheritedRawValue(PdfDictionary dict, string key, PdfReader reader)
    {
        var parentObj = dict.Get("Parent");
        var visited = new HashSet<int>();
        while (parentObj is not null)
        {
            var parent = reader.ResolveDict(parentObj);
            if (parent is null) break;
            if (parentObj is PdfIndirectRef iref && !visited.Add(iref.ObjectNumber)) break;
            var val = parent.Get(key);
            if (val is not null) return val;
            parentObj = parent.Get("Parent");
        }
        return null;
    }

    private static PdfObject? ResolveInheritedValue(PdfDictionary dict, string key, PdfReader reader)
    {
        var parentObj = dict.Get("Parent");
        var visited = new HashSet<int>();
        while (parentObj is not null)
        {
            var parent = reader.ResolveDict(parentObj);
            if (parent is null) break;
            if (parentObj is PdfIndirectRef iref && !visited.Add(iref.ObjectNumber)) break;
            var val = parent.Get(key);
            if (val is not null) return reader.Resolve(val) ?? val;
            parentObj = parent.Get("Parent");
        }
        return null;
    }

    internal readonly record struct PendingPage(PdfDictionary Dict, int? InsertIndex = null);
}

using System.Collections;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

/// <summary>
/// Collection of pages in a PDF document.
/// </summary>
public sealed class PageCollection : IEnumerable<Page>
{
    private readonly PdfReader _reader;
    private List<Page>? _pages;
    private readonly List<PendingPage> _pendingAdds = [];
    private readonly HashSet<int> _deletedIndices = [];
    private readonly List<(int objNum, PdfObject obj)> _importedObjects = [];

    // Cache of cloned objects per source reader, so shared resources (images, fonts)
    // referenced by multiple pages from the same source document are cloned only once.
    // This prevents output bloat when adding many pages that share the same resources.
    private readonly Dictionary<PdfReader, Dictionary<int, PdfObject>> _cloneCache = new();

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
            return _pages![index - 1];
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
            if (ReferenceEquals(_pages[i], entity) || ReferenceEquals(_pages[i].Dict, entity.Dict))
                return i + 1; // 1-based
        }
        return -1;
    }

    /// <summary>Accept a TextAbsorber visitor; iterates every page.</summary>
    public void Accept(Text.TextAbsorber visitor)
    {
        foreach (var page in this)
            visitor.Visit(page);
    }

    /// <summary>Accept a TextFragmentAbsorber visitor; accumulates fragments across all pages.</summary>
    public void Accept(Text.TextFragmentAbsorber visitor)
    {
        // Clear once then visit all pages without clearing between them.
        visitor.TextFragments.Clear();
        foreach (var page in this)
            visitor.VisitInternal(page);
    }

    /// <summary>Accept an ImagePlacementAbsorber visitor across every page.</summary>
    public void Accept(ImagePlacementAbsorber visitor)
    {
        foreach (var page in this)
            visitor.Visit(page);
    }

    /// <summary>Walk every annotation on every page and dispatch it to
    /// <paramref name="visitor"/>. Selected matches accumulate in
    /// <see cref="Annotations.AnnotationSelector.Selected"/>.</summary>
    public void Accept(Annotations.AnnotationSelector visitor)
    {
        if (visitor is null) return;
        foreach (var page in this)
            page.Annotations.Accept(visitor);
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
        _pages!.Add(page);
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
        _pages!.Add(page);
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
        var clonedDict = ClonePageForImport(entity.Dict, entity.Reader);
        clonedDict.Remove("Parent");
        var added = AddFromDict(clonedDict);
        ImportFormFieldsFromPage(clonedDict, entity.Dict, entity.Reader);
        CarryOverPendingContent(entity, added);
        return added;
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
    }

    /// <summary>
    /// Add all pages from another page collection (cross-document merge).
    /// Safe for self-copy (doc.Pages.Add(doc.Pages)).
    /// </summary>
    public void Add(PageCollection otherPages)
    {
        // Snapshot to avoid issues when otherPages == this (self-copy)
        var snapshot = new List<(PdfDictionary dict, PdfReader reader)>();
        foreach (var page in otherPages)
            snapshot.Add((page.Dict, page.Reader));

        foreach (var (dict, reader) in snapshot)
        {
            EnsurePages();
            var clonedDict = ClonePageForImport(dict, reader);
            clonedDict.Remove("Parent");
            AddFromDict(clonedDict);
            ImportFormFieldsFromPage(clonedDict, dict, reader);
        }
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

    private void AddPagesFromEnumerable(IEnumerable<Page> pages)
    {
        var snapshot = new List<(PdfDictionary dict, PdfReader reader)>();
        foreach (var page in pages)
            snapshot.Add((page.Dict, page.Reader));

        foreach (var (dict, reader) in snapshot)
        {
            EnsurePages();
            var clonedDict = ClonePageForImport(dict, reader);
            clonedDict.Remove("Parent");
            AddFromDict(clonedDict);
            ImportFormFieldsFromPage(clonedDict, dict, reader);
        }
    }

    /// <summary>
    /// Add a page with the given dictionary (used for page import).
    /// </summary>
    internal Page AddFromDict(PdfDictionary pageDict, PdfReader? sourceReader = null)
    {
        EnsurePages();
        var page = new Page(pageDict, sourceReader ?? _reader, _pages!.Count);
        _pages!.Add(page);
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
        _pages!.Insert(insertIndex, page);
        _pendingAdds.Add(new PendingPage(pageDict, insertIndex));

        // Re-index pages after insertion (in-place to avoid orphaning returned page)
        for (var i = insertIndex; i < _pages.Count; i++)
            _pages[i].SetIndex(i);

        return page;
    }

    /// <summary>
    /// Insert a new blank page before the given 1-based index.
    /// Uses the most frequent page size from existing pages, or US Letter if empty.
    /// </summary>
    public Page Insert(int pageNumber)
    {
        var (w, h) = GetMostFrequentPageSize();
        return Insert(pageNumber, w, h);
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
        _pages!.Insert(insertIndex, page);
        _pendingAdds.Add(new PendingPage(pageDict, insertIndex));

        // Re-index pages after insertion (in-place to avoid orphaning returned page)
        for (var i = insertIndex; i < _pages.Count; i++)
            _pages[i].SetIndex(i);

        return page;
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
        var deletedPage = _pages![zeroBased];
        _deletedIndices.Add(zeroBased);
        _pages.RemoveAt(zeroBased);

        // Mark deleted page as removed (Number returns -1)
        deletedPage.SetIndex(-2);

        // Re-index remaining pages (in-place)
        for (var i = zeroBased; i < _pages.Count; i++)
            _pages[i].SetIndex(i);
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

    private void InsertPagesFromEnumerable(int pageNumber, IEnumerable<Page> pages)
    {
        var snapshot = new List<(PdfDictionary dict, PdfReader reader)>();
        foreach (var page in pages)
            snapshot.Add((page.Dict, page.Reader));

        for (var i = 0; i < snapshot.Count; i++)
        {
            var (dict, reader) = snapshot[i];
            EnsurePages();
            var clonedDict = ClonePageForImport(dict, reader);
            clonedDict.Remove("Parent");
            InsertFromDict(pageNumber + i, clonedDict);
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
            page.FreeMemory();
    }

    /// <summary>Whether the collection is read-only. Always false.</summary>
    public bool IsReadOnly => false;

    /// <summary>Whether the collection is thread-safe. Always false.</summary>
    public bool IsSynchronized => false;

    /// <summary>Synchronization root for <see cref="IsSynchronized"/>; returns this collection.</summary>
    public object SyncRoot => this;

    /// <summary>Suspend page-tree maintenance during a batch of mutations. Stored only; no batching is currently performed.</summary>
    public void BeginUpdate()
    {
        // No-op kept for API parity.
    }

    /// <summary>Resume page-tree maintenance after a <see cref="BeginUpdate"/> batch.</summary>
    public void EndUpdate()
    {
        // No-op kept for API parity.
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
        return _pages!.GetEnumerator();
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
        CollectPages(pagesDict, pageList);
        _pages = pageList;
    }

    private void CollectPages(PdfDictionary node, List<Page> result)
    {
        var type = node.GetName("Type");

        // Check for /Kids first — a corrupt PDF may have /Type /Page on a
        // tree node that actually has children (e.g., 37914.pdf).
        var kids = _reader.Resolve(node.Get("Kids")) as PdfArray;

        if (type == "Page" && kids is null)
        {
            result.Add(new Page(node, _reader, result.Count));
            return;
        }
        if (kids is null)
        {
            // No /Kids — if the node has page-like properties (Contents, Resources),
            // treat it as a leaf page despite wrong /Type (e.g. "Pages" typo in 46507.pdf)
            if (node.ContainsKey("Contents") || node.ContainsKey("Resources"))
                result.Add(new Page(node, _reader, result.Count));
            return;
        }

        foreach (var kid in kids)
        {
            var kidDict = _reader.ResolveDict(kid);
            if (kidDict is not null)
                CollectPages(kidDict, result);
            else
            {
                // Unresolvable kid (corrupt/zeroed-out object stream) — add a placeholder page so
                // PageCount matches the declared page tree structure for partially corrupt PDFs.
                var placeholder = new PdfDictionary();
                placeholder.Set("Type", new PdfName("Page"));
                result.Add(new Page(placeholder, _reader, result.Count));
            }
        }
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
            return (595, 842); // A4, the Aspose.PDF for .NET default
        }

        // Find most frequent page size from a bounded sample (avoids O(n²) on bulk Add).
        // On ties, use first page's size (Aspose.PDF for .NET behavior).
        var sampleCount = Math.Min(_pages!.Count, 100);
        var counts = new Dictionary<(double w, double h), int>();
        for (int i = 0; i < sampleCount; i++)
        {
            var box = _pages[i].MediaBox;
            var key = (Math.Round(box.Width, 2), Math.Round(box.Height, 2));
            counts.TryGetValue(key, out var c);
            counts[key] = c + 1;
        }
        var maxCount = counts.Values.Max();
        for (int i = 0; i < sampleCount; i++)
        {
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

        return dict;
    }

    /// <summary>
    /// Get or create the clone cache for a given source reader.
    /// This ensures that shared resources (images, fonts) referenced by indirect object number
    /// are only deep-cloned once, even when adding many pages from the same source.
    /// </summary>
    private Dictionary<int, PdfObject> GetOrCreateCloneCache(PdfReader reader)
    {
        if (!_cloneCache.TryGetValue(reader, out var visited))
        {
            visited = new Dictionary<int, PdfObject>();
            _cloneCache[reader] = visited;
        }
        return visited;
    }

    /// <summary>
    /// Clone a page dictionary for cross-document import. Resolves all indirect refs
    /// from the source reader and copies the referenced objects into this document's
    /// new-objects list. Uses object number remapping to avoid collisions.
    /// </summary>
    private PdfDictionary ClonePageForImport(PdfDictionary dict, PdfReader sourceReader)
    {
        PdfDictionary clone;
        if (sourceReader == _reader)
        {
            // Same document — shallow clone is sufficient
            clone = new PdfDictionary();
            foreach (var key in dict.Keys)
            {
                if (key == "Parent") continue;
                var val = dict.Get(key);
                if (val is not null) clone.Set(key, val);
            }
        }
        else
        {
            // Cross-document: remap indirect refs from source to new object numbers
            var remap = GetOrCreateCloneCache(sourceReader);
            clone = (PdfDictionary)RemapObject(dict, sourceReader, remap);
        }

        // Ensure inheritable properties are set directly on the clone.
        // Without a parent chain, inherited MediaBox/CropBox/Rotate/Resources would be lost.
        EnsureInherited(clone, dict, sourceReader);
        return clone;
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
    private static PdfObject? ResolveInheritedRawValue(PdfDictionary dict, string key, PdfReader reader)
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

    /// <summary>
    /// Recursively remap a PDF object's indirect refs from a source reader to new object
    /// numbers in this document, copying the referenced objects as new objects.
    /// Iterative to avoid stack overflow.
    /// </summary>
    private PdfObject RemapObject(PdfObject obj, PdfReader sourceReader, Dictionary<int, PdfObject> remap)
    {
        var stack = new Stack<(PdfObject source, Action<PdfObject> setter)>();
        var visitedIdentity = new HashSet<object>(ReferenceEqualityComparer.Instance);
        // Allocate object numbers from a counter that starts past the existing xref
        var nextObjNum = _reader.XRefTable.Entries.Keys.DefaultIfEmpty(0).Max() + _importedObjects.Count + 1;
        PdfObject? root = null;

        void Process(PdfObject src, Action<PdfObject> setter)
        {
            switch (src)
            {
                case PdfIndirectRef iref:
                {
                    // Already remapped?
                    if (remap.TryGetValue(iref.ObjectNumber, out var cached))
                    {
                        setter(cached);
                        return;
                    }
                    // Resolve from source, allocate new obj number, register for writing
                    var resolved = sourceReader.Resolve(iref);
                    if (resolved is null) { setter(src); return; }

                    var newObjNum = nextObjNum++;
                    var newRef = new PdfIndirectRef(newObjNum, 0);
                    remap[iref.ObjectNumber] = newRef; // map before recursing (cycle breaking)

                    // Schedule remapping of the resolved object's contents
                    stack.Push((resolved, remapped =>
                    {
                        _importedObjects.Add((newObjNum, remapped));
                        // Also expose the imported object to the destination reader so
                        // it resolves in-memory (e.g. Page.Contents) before the next save —
                        // otherwise the ref dangles until _importedObjects is written out.
                        _reader.RegisterOverlayObject(newObjNum, remapped);
                    }));
                    setter(newRef);
                    return;
                }

                case PdfDictionary dict:
                {
                    if (!visitedIdentity.Add(dict)) { setter(dict); return; }
                    var clone = new PdfDictionary();
                    setter(clone);
                    foreach (var key in dict.Keys)
                    {
                        if (key == "Parent") continue;
                        var val = dict.Get(key);
                        if (val is not null)
                        {
                            var k = key;
                            stack.Push((val, v => clone.Set(k, v)));
                        }
                    }
                    return;
                }

                case PdfArray arr:
                {
                    if (!visitedIdentity.Add(arr)) { setter(arr); return; }
                    var clone = new PdfArray();
                    setter(clone);
                    for (int i = arr.Count - 1; i >= 0; i--)
                        stack.Push((arr[i], v => clone.Add(v)));
                    return;
                }

                case PdfStream stream:
                {
                    if (!visitedIdentity.Add(stream)) { setter(stream); return; }
                    var dictClone = new PdfDictionary();
                    var dataCopy = new byte[stream.RawData.Length];
                    Array.Copy(stream.RawData, dataCopy, stream.RawData.Length);
                    var streamClone = new PdfStream(dictClone, dataCopy);
                    setter(streamClone);
                    foreach (var key in stream.Dict.Keys)
                    {
                        if (key == "Parent") continue;
                        var val = stream.Dict.Get(key);
                        if (val is not null)
                        {
                            var k = key;
                            stack.Push((val, v => dictClone.Set(k, v)));
                        }
                    }
                    return;
                }

                default:
                    setter(src);
                    return;
            }
        }

        Process(obj, result => root = result);
        int safetyLimit = 1_000_000;
        while (stack.Count > 0 && --safetyLimit > 0)
        {
            var (source, setter) = stack.Pop();
            Process(source, setter);
        }

        return root!;
    }

    /// <summary>
    /// After importing a page, scan its /Annots for Widget annotations and register
    /// them in the target document's AcroForm /Fields array. Handles duplicate field
    /// names by appending numeric suffixes.
    /// </summary>
    private void ImportFormFieldsFromPage(PdfDictionary clonedPageDict,
        PdfDictionary sourcePageDict, PdfReader sourceReader)
    {
        if (OwnerDocument is null) return;

        var annotsObj = clonedPageDict.Get("Annots");
        var annots = annotsObj as PdfArray ?? ResolveImportedObject(annotsObj) as PdfArray;
        if (annots is null) return;

        // Also resolve source page's annotations for name lookup
        var srcAnnotsObj = sourceReader.Resolve(sourcePageDict.Get("Annots"));
        var srcAnnots = srcAnnotsObj as PdfArray;

        // Ensure AcroForm exists in the catalog
        var catalog = _reader.Catalog;
        var acroForm = _reader.ResolveDict(catalog.Get("AcroForm"));
        if (acroForm is null)
        {
            acroForm = new PdfDictionary();
            catalog.Set("AcroForm", acroForm);
        }

        var fieldsArr = _reader.Resolve(acroForm.Get("Fields")) as PdfArray;
        if (fieldsArr is null)
        {
            fieldsArr = new PdfArray();
            acroForm.Set("Fields", fieldsArr);
        }

        // Collect existing field names for deduplication
        var existingNames = new HashSet<string>();
        CollectFieldNames(fieldsArr, existingNames);

        for (int i = 0; i < annots.Count; i++)
        {
            var annotObj = annots[i];
            var annotDict = ResolveForImport(annotObj);
            if (annotDict is null) continue;

            // Check if this is a Widget annotation
            var subtype = annotDict.GetName("Subtype");
            if (subtype != "Widget") continue;

            // Get the field name from the cloned dict
            var partialName = GetFieldPartialName(annotDict);

            // If no /T in clone (Parent was stripped), look up from source
            if (string.IsNullOrEmpty(partialName) && srcAnnots is not null && i < srcAnnots.Count)
            {
                var srcAnnotDict = sourceReader.ResolveDict(srcAnnots[i]);
                if (srcAnnotDict is not null)
                    partialName = GetFullFieldName(srcAnnotDict, sourceReader);
            }

            if (string.IsNullOrEmpty(partialName)) continue;

            // Set the name on the cloned Widget so it becomes a standalone field
            annotDict.Set("T", new PdfString(System.Text.Encoding.Latin1.GetBytes(partialName)));

            // For same-source multi-widget fields: skip if same name already exists
            // (same field appearing on multiple pages within one document)
            if (existingNames.Contains(partialName))
            {
                // Check if this is a same-source duplicate (same reader = same document)
                // vs a different-source field needing rename
                bool isSameSourceDuplicate = false;
                if (srcAnnots is not null && i < srcAnnots.Count)
                {
                    var srcAnnotDict = sourceReader.ResolveDict(srcAnnots[i]);
                    if (srcAnnotDict?.Get("Parent") is PdfIndirectRef parentRef)
                    {
                        // This Widget shares a parent field — it's a multi-widget field
                        // within the same document. Skip the duplicate.
                        isSameSourceDuplicate = true;
                    }
                }

                if (isSameSourceDuplicate)
                    continue;

                // Different source — rename the field
                var newName = DeduplicateFieldName(partialName, existingNames);
                annotDict.Set("T", new PdfString(System.Text.Encoding.Latin1.GetBytes(newName)));
                partialName = newName;
            }
            existingNames.Add(partialName);

            // Copy field properties from parent chain if missing on the Widget
            if (srcAnnots is not null && i < srcAnnots.Count)
            {
                var srcAnnotDict = sourceReader.ResolveDict(srcAnnots[i]);
                if (srcAnnotDict is not null)
                    CopyFieldPropertiesFromParent(srcAnnotDict, sourceReader, annotDict);
            }

            // Add to AcroForm /Fields
            fieldsArr.Add(annotObj!);
        }

        // Register imported objects with the reader so they can be resolved in-memory
        foreach (var (objNum, obj) in _importedObjects)
            _reader.RegisterOverlayObject(objNum, obj);

        // Invalidate the cached Form object so it re-reads from the updated AcroForm
        OwnerDocument.InvalidateForm();
    }

    /// <summary>Get full field name by walking /Parent chain in the source document.</summary>
    private static string? GetFullFieldName(PdfDictionary annotDict, PdfReader reader)
    {
        var name = GetFieldPartialName(annotDict);
        var parent = reader.ResolveDict(annotDict.Get("Parent"));
        while (parent is not null)
        {
            var parentName = GetFieldPartialName(parent);
            if (!string.IsNullOrEmpty(parentName))
                name = string.IsNullOrEmpty(name) ? parentName : parentName + "." + name;
            parent = reader.ResolveDict(parent.Get("Parent"));
        }
        return name;
    }

    /// <summary>
    /// Copy field properties (/FT, /V, /Ff, /DA) from source annotation's parent chain
    /// to the cloned Widget dict, making it a standalone field.
    /// </summary>
    private static void CopyFieldPropertiesFromParent(PdfDictionary srcAnnot, PdfReader reader, PdfDictionary target)
    {
        // Properties to inherit from parent chain (only if not already on the Widget)
        string[] keysToInherit = ["FT", "V", "Ff", "DA", "DV"];

        var current = srcAnnot;
        while (current is not null)
        {
            foreach (var key in keysToInherit)
            {
                if (!target.ContainsKey(key))
                {
                    var val = current.Get(key);
                    if (val is not null)
                    {
                        // Deep-copy the value (resolve indirect refs)
                        var resolved = reader.Resolve(val) ?? val;
                        target.Set(key, resolved);
                    }
                }
            }
            current = reader.ResolveDict(current.Get("Parent"));
        }
    }

    private PdfObject? ResolveImportedObject(PdfObject? obj)
    {
        if (obj is null) return null;
        if (obj is PdfIndirectRef iref)
        {
            foreach (var (num, imported) in _importedObjects)
            {
                if (num == iref.ObjectNumber)
                    return imported;
            }
            return _reader.Resolve(iref);
        }
        return obj;
    }

    private PdfDictionary? ResolveForImport(PdfObject? obj)
    {
        var resolved = ResolveImportedObject(obj);
        return resolved as PdfDictionary;
    }

    private static string? GetFieldPartialName(PdfDictionary dict)
    {
        var tObj = dict.Get("T");
        if (tObj is PdfString s)
            return s.ToText();
        return null;
    }

    private static string DeduplicateFieldName(string baseName, HashSet<string> existing)
    {
        for (int suffix = 1; ; suffix++)
        {
            var candidate = baseName + suffix;
            if (!existing.Contains(candidate))
                return candidate;
        }
    }

    private void CollectFieldNames(PdfArray fieldsArr, HashSet<string> names)
    {
        foreach (var item in fieldsArr)
        {
            var dict = ResolveForImport(item);
            if (dict is null) continue;
            var name = GetFieldPartialName(dict);
            if (name is not null) names.Add(name);

            // Also check kids
            var kids = _reader.Resolve(dict.Get("Kids")) as PdfArray;
            if (kids is not null) CollectFieldNames(kids, names);
        }
    }

    internal readonly record struct PendingPage(PdfDictionary Dict, int? InsertIndex = null);
}

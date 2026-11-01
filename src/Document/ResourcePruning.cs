using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>Shrinks the document using the default settings: unused resources and objects are dropped, duplicate streams are linked and duplicate images are removed. The result takes effect when the document is saved.</summary>
    public void OptimizeResources() => OptimizeResources(Aspose.Pdf.Optimization.OptimizationOptions.Default);

    /// <summary>Shrinks the document using the given settings (unused resources and objects, duplicate streams and images, image compression and downsampling, font subsetting or unembedding, metadata removal). The result takes effect when the document is saved; <c>null</c> uses the defaults.</summary>
    public void OptimizeResources(Aspose.Pdf.Optimization.OptimizationOptions strategy)
    {
        var options = strategy ?? Aspose.Pdf.Optimization.OptimizationOptions.Default;
        if (options.CompressObjects) _packObjectsOnSave = true;
        BringSessionContentIntoGraph();

        // Drop /Resources entries (fonts, XObjects, ...) that no content stream
        // references. Done before the reachability pass below so the now-orphaned
        // resource objects also fall out of the saved file.
        if (options.RemoveUnusedStreams)
        {
            PruneUnusedResources();
        }

        // Apply image compression if requested
        if (options.CompressImages)
        {
            ImageCompressor.CompressImages(_reader, options.ImageQuality);
        }

        // Downsample images exceeding max DPI
        if (options.MaxImageDpi > 0)
        {
            ImageCompressor.DownsampleImages(_reader, options.MaxImageDpi, options.ImageQuality);
        }

        // Convert images to grayscale
        if (options.ConvertImagesToGrayscale)
        {
            ImageCompressor.ConvertToGrayscale(_reader);
        }

        // Remove duplicate images
        if (options.RemoveDuplicateImages)
        {
            ImageCompressor.RemoveDuplicateImages(_reader);
        }

        // Apply font subsetting if requested. The public SubsetFonts option means
        // real embedded-program subsetting, not just the
        // standard-14 strip — routing only the internal SubsetEmbeddedFonts flag to
        // the TrueType subsetter left full 400KB font programs in "optimized" files.
        if (options.SubsetFonts || options.SubsetEmbeddedFonts)
        {
            FontSubsetter.SubsetFonts(_reader, subsetEmbedded: true);
        }

        // Drop embedded font programs for fonts a viewer can substitute (Standard 14 or
        // installed system faces). The programs leave the file with them, whatever the other
        // options: one read from the file falls out through the save's reachability pass, one
        // this session made (a program a conversion embedded) is not written at all.
        if (options.UnembedFonts && FontSubsetter.UnembedFonts(_reader) is { Count: > 0 } detached)
        {
            _reader.MayHaveOrphansOnSave = true;
            foreach (var program in detached) RemoveNewObject(program);
        }

        // Remove metadata if requested
        if (options.RemoveMetadata)
        {
            _reader.Catalog.Remove("Metadata");
        }

        // Link duplicate streams
        if (options.LinkDuplicateStreams)
        {
            LinkDuplicateStreams();
        }

        // Unused objects are left out by the save, which walks from the trailer as the document is
        // then: what the steps above orphaned (an unembedded /FontFile2 program, a linked-away
        // duplicate) falls out, and what is added after this call (a page copied in) is kept.
        _reachableObjects = null;
        if (options.RemoveUnusedObjects) _reader.MayHaveOrphansOnSave = true;
    }

    /// <summary>What this session added waits for the save to join the document: paragraphs are laid
    /// out by the save, and the page tree the passes walk still lists the pages as loaded. Both are
    /// brought in first, so a page added, imported or filled since the document was opened is
    /// optimized as it would be after a save and reload, and a deleted page is not walked.</summary>
    private void BringSessionContentIntoGraph()
    {
        if (_pages is null) return;
        if (_pages.Any(page => page.Paragraphs.Count > 0)) ApplyPageContent();
        SyncInMemoryPageTree();
    }

    /// <summary>The objects the document still reaches: from the trailer, through the page tree as the session
    /// left it (pages deleted since load are no longer listed), from every page as it is held now - a page
    /// added or copied in waits for the save to join the tree - and from the objects the session made, which
    /// the save writes whatever happens and so must not lose what they refer to.</summary>
    private HashSet<int> CollectSessionReachable()
    {
        SyncInMemoryPageTree();
        var reachable = new HashSet<int>();
        CollectReachable(_reader.Trailer, reachable);
        if (_pages is not null)
            foreach (var page in _pages)
                CollectReachable(page.Dict, reachable);
        foreach (var (_, obj) in _newObjects)
            CollectReachable(obj, reachable);
        return reachable;
    }

    private void CollectReachable(PdfObject? root, HashSet<int> visited)
    {
        if (root is null or PdfNull) return;

        // Iterative traversal with explicit stack to avoid stack overflow on large PDFs
        var stack = new Stack<PdfObject>();
        stack.Push(root);
        var seenDicts = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);

        while (stack.Count > 0)
        {
            var obj = stack.Pop();
            if (obj is null or PdfNull) continue;

            if (obj is PdfIndirectRef iref)
            {
                if (!visited.Add(iref.ObjectNumber)) continue;
                var resolved = _reader.Resolve(iref);
                if (resolved is not null) stack.Push(resolved);
                continue;
            }

            if (obj is PdfStream stream)
            {
                stack.Push(stream.Dict);
                continue;
            }

            if (obj is PdfDictionary dict)
            {
                if (!seenDicts.Add(dict)) continue;
                foreach (var key in dict.Keys)
                {
                    var val = dict.Get(key);
                    if (val is not null) stack.Push(val);
                }
                continue;
            }

            if (obj is PdfArray arr)
            {
                foreach (var item in arr.Where(item => item is not null))
                    stack.Push(item);
            }
        }
    }

    /// <summary>The /Resources sub-dictionaries whose entries are name-referenced
    /// from content streams and so can be pruned when unreferenced.</summary>
    private static readonly string[] PrunableResourceCategories =
        { "Font", "XObject", "ExtGState", "Pattern", "Shading", "ColorSpace", "Properties" };

    /// <summary>
    /// Remove /Resources entries (fonts, XObjects, ExtGStates, ...) that no content
    /// stream reachable from a page actually references. Conservative by design: an
    /// entry is kept whenever its resource name appears as a /Name token in the page
    /// content or in any form XObject invoked (directly or transitively) from it, so
    /// a face used only through a form's parent-resource fallback is never dropped.
    /// Only page-level resource dictionaries are pruned; per-form resources are left
    /// intact (they are small and self-contained).
    /// </summary>
    private void PruneUnusedResources()
    {
        // A /Resources dict may be SHARED by several pages (e.g. inherited from a
        // common parent in the page tree). Pruning it per-page would drop entries
        // another page still uses, so accumulate the UNION of used names per shared
        // resources dict (by reference identity) and prune each dict once.
        var usedByResources = new Dictionary<PdfDictionary, HashSet<string>>(ReferenceEqualityComparer.Instance);
        var keepAll = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);

        foreach (var page in Pages)
        {
            var resources = _reader.ResolveDict(page.Dict.Get("Resources"));
            if (resources is null) continue;

            // Operators added through Page.Contents wait in memory until the page is
            // saved or rendered; an image placed by such a pending /Do is a used resource,
            // not an orphan, so the pending content joins the stream before it is read.
            page.FlushPendingContents();
            var content = page.GetContentStreamBytes();
            // No analysable content => keep every resource on this dict rather than guess.
            if (content is null || content.Length == 0)
            {
                keepAll.Add(resources);
                continue;
            }

            if (!usedByResources.TryGetValue(resources, out var used))
            {
                used = new HashSet<string>(StringComparer.Ordinal);
                usedByResources[resources] = used;
            }
            var visitedForms = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
            CollectContentResourceNames(content, resources, used, visitedForms);
        }

        // A colour space is reached by NAME from content and by OBJECT from an image
        // XObject (or another colour space). An entry no operator names may still be
        // live for an image on another page, so collect the objects the document
        // references before pruning any dictionary.
        var liveColorSpaces = CollectLiveColorSpaceObjects();

        foreach (var (resources, used) in usedByResources)
        {
            if (keepAll.Contains(resources)) continue;
            AddReferencedXObjectNames(resources, used);
            AddLiveColorSpaceNames(resources, used, liveColorSpaces);
            PruneResourceCategories(resources, used);
        }
    }

    /// <summary>Drop /Font resource entries no longer referenced by any content after a
    /// <see cref="Text.TextEditOptions.FontReplace.RemoveUnusedFonts"/> text edit. Walks
    /// the page content and every invoked form XObject, pruning each scope's OWN /Font
    /// dictionary against the fonts its content selects with <c>Tf</c>.</summary>
    private void PruneUnusedFontsForPage(Page page)
    {
        var resources = _reader.ResolveDict(page.Dict.Get("Resources"));
        var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var rewritten = PruneFontsInScope(page.GetContentStreamBytes(), resources, visited);
        if (rewritten is not null) page.SetContentStream(rewritten);
    }

    /// <summary>Prune unused /Font entries in a content scope and rename the replacement
    /// fonts to sequential F0, F1, … keys (a replacement font is
    /// named "F0"). Returns the rewritten content when a rename changed it,
    /// else null. Form XObject scopes are rewritten in place.</summary>
    private byte[]? PruneFontsInScope(byte[]? content, PdfDictionary? resources, HashSet<PdfDictionary> visitedForms)
    {
        var fp = new FontPruneState();
        fp.content = content;
        fp.resources = resources;
        fp.visitedForms = visitedForms;
        if (fp.content is null || fp.content.Length == 0 || fp.resources is null) return null;

        fp.usedFonts = new HashSet<string>(StringComparer.Ordinal);
        fp.formNames = new List<string>();
        fp.lexer = new IO.PdfLexer(fp.content);
        fp.lastName = null;      // most recent /Name operand (font for Tf, form for Do)
        fp.currentFont = null;   // font selected by the last Tf
        fp.sawGlyphs = false;       // a non-empty string appeared since the last operator
        while (true)
        {
            if (!ScanContentForFonts(fp)) break;
        }

        fp.rewritten = null;
        fp.fontDict = _reader.ResolveDict(fp.resources.Get("Font"));
        if (fp.fontDict is not null)
        {
            PruneFontDictionary(fp);
        }

        fp.xobjects = _reader.ResolveDict(fp.resources.Get("XObject"));
        if (fp.xobjects is not null)
        {
            PruneNestedFormFonts(fp);
        }
        return fp.rewritten;
    }

    /// <summary>Rewrite a content stream, replacing the font operand of every <c>Tf</c>
    /// that names a key of <paramref name="renameMap"/> with its mapped name. Scoped to
    /// Tf operands on purpose: the same /Name can also key an XObject or ExtGState, and
    /// those references must not follow a font-resource rename.</summary>
    private static byte[] RepointTfNamesInContent(byte[] content, Dictionary<string, string> renameMap)
    {
        var lexer = new IO.PdfLexer(content);
        var patches = new List<(int start, int end, string nw)>();
        // The Tf operand is the name most recently seen before the operator.
        int nameStart = -1, nameEnd = -1; string? pendingName = null;
        while (true)
        {
            var startPos = (int)lexer.Position;
            var token = lexer.NextToken();
            if (token.Kind == IO.TokenKind.Eof) break;
            if (token.Kind == IO.TokenKind.Name)
            {
                pendingName = token.StringValue;
                nameStart = startPos;
                nameEnd = (int)lexer.Position;
            }
            else if (token.Kind == IO.TokenKind.Keyword)
            {
                if (token.StringValue == "Tf" && pendingName is not null
                    && renameMap.TryGetValue(pendingName, out var nw))
                    patches.Add((nameStart, nameEnd, nw));
                pendingName = null;
            }
        }
        // Apply right-to-left so earlier offsets stay valid.
        patches.Sort((a, b) => b.start.CompareTo(a.start));
        foreach (var (s, e, nw) in patches)
        {
            var nameBytes = System.Text.Encoding.ASCII.GetBytes("/" + nw);
            var result = new byte[content.Length - (e - s) + nameBytes.Length];
            Array.Copy(content, 0, result, 0, s);
            Array.Copy(nameBytes, 0, result, s, nameBytes.Length);
            Array.Copy(content, e, result, s + nameBytes.Length, content.Length - e);
            content = result;
        }
        return content;
    }

    /// <summary>Expand <paramref name="used"/> with the /XObject names that a used
    /// image references through its /SMask or /Mask — a soft mask is part of the
    /// image even though no content stream names it directly, so pruning it would
    /// drop a glyph/picture's transparency. Iterates to a fixpoint for mask chains.</summary>
    private void AddReferencedXObjectNames(PdfDictionary resources, HashSet<string> used)
    {
        var xobjects = _reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is null) return;

        // Map each entry's underlying stream object to its name so a mask referenced
        // by object can be matched back to the /XObject key the test inspects.
        var streamToName = new Dictionary<PdfStream, string>(ReferenceEqualityComparer.Instance);
        foreach (var name in xobjects.Keys)
            if (_reader.ResolveStream(xobjects.Get(name)) is { } s)
                streamToName[s] = name;

        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var name in used.ToList())
            {
                if (_reader.ResolveStream(xobjects.Get(name)) is not { } s) continue;
                foreach (var maskKey in new[] { "SMask", "Mask" })
                    if (_reader.ResolveStream(s.Dict.Get(maskKey)) is { } mask
                        && streamToName.TryGetValue(mask, out var maskName)
                        && used.Add(maskName))
                        changed = true;
            }
        }
    }

    /// <summary>The colour-space OBJECTS the document references from somewhere other
    /// than a /Resources /ColorSpace dictionary - an image XObject's /ColorSpace, or the
    /// base of an /Indexed space. Those entries stay even when no operator names them;
    /// the resource dictionaries themselves do not count, or every entry would be live.</summary>
    private HashSet<int> CollectLiveColorSpaceObjects()
    {
        var colorSpaceDicts = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        foreach (var page in Pages)
        {
            var resources = _reader.ResolveDict(page.Dict.Get("Resources"));
            if (resources is not null && _reader.ResolveDict(resources.Get("ColorSpace")) is { } cs)
                colorSpaceDicts.Add(cs);
        }

        var referenced = new HashSet<int>();
        var seen = new HashSet<PdfObject>(ReferenceEqualityComparer.Instance);
        foreach (var entry in _reader.XRefTable.Entries.Values)
            CollectColorSpaceRefs(_reader.Resolve(new PdfIndirectRef(entry.ObjectNumber, entry.Generation)),
                colorSpaceDicts, referenced, seen, 0);
        return referenced;
    }

    /// <summary>Walk one object, recording every indirect reference it makes. A
    /// /Resources /ColorSpace dictionary is skipped: its entries are the candidates.</summary>
    private static void CollectColorSpaceRefs(PdfObject? obj, HashSet<PdfDictionary> colorSpaceDicts,
        HashSet<int> referenced, HashSet<PdfObject> seen, int depth)
    {
        if (obj is null || depth > 8 || !seen.Add(obj)) return;
        switch (obj)
        {
            case PdfIndirectRef iref:
                referenced.Add(iref.ObjectNumber);
                break;
            case PdfArray arr:
                for (var i = 0; i < arr.Count; i++)
                    CollectColorSpaceRefs(arr[i], colorSpaceDicts, referenced, seen, depth + 1);
                break;
            case PdfStream stream:
                CollectColorSpaceRefs(stream.Dict, colorSpaceDicts, referenced, seen, depth + 1);
                break;
            case PdfDictionary dict:
                if (colorSpaceDicts.Contains(dict)) return;
                foreach (var key in dict.Keys)
                    CollectColorSpaceRefs(dict.Get(key), colorSpaceDicts, referenced, seen, depth + 1);
                break;
        }
    }

    /// <summary>Keep the /ColorSpace entries whose object the document still references.</summary>
    private void AddLiveColorSpaceNames(PdfDictionary resources, HashSet<string> used, HashSet<int> live)
    {
        var colorSpaces = _reader.ResolveDict(resources.Get("ColorSpace"));
        if (colorSpaces is null) return;
        foreach (var key in colorSpaces.Keys)
            if (colorSpaces.Get(key) is PdfIndirectRef iref && live.Contains(iref.ObjectNumber))
                used.Add(key);
    }

    /// <summary>Add every /Name token in <paramref name="content"/> to
    /// <paramref name="used"/>, then recurse through the form XObjects it invokes so
    /// names referenced only inside a nested form (or via its parent-resource
    /// fallback) are counted as used too.</summary>
    private void CollectContentResourceNames(byte[] content, PdfDictionary resources,
        HashSet<string> used, HashSet<PdfDictionary> visitedForms)
    {
        var localNames = new List<string>();
        var lexer = new IO.PdfLexer(content);
        while (true)
        {
            var token = lexer.NextToken();
            if (token.Kind == IO.TokenKind.Eof) break;
            // Inline images carry raw binary between ID and EI that must not be
            // tokenised — left unskipped it desyncs the lexer and the /Name tokens
            // after it (e.g. later `Do` references) are missed, pruning live images.
            if (token.Kind == IO.TokenKind.Keyword && token.StringValue == "BI")
            {
                SkipInlineImage(lexer, used);
                continue;
            }
            if (token.Kind == IO.TokenKind.Name && token.StringValue is { } name && used.Add(name))
                localNames.Add(name);
        }

        var xobjects = _reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is null) return;
        foreach (var name in localNames)
        {
            var xstream = _reader.ResolveStream(xobjects.Get(name));
            if (xstream is null || xstream.Dict.GetName("Subtype") != "Form") continue;
            if (!visitedForms.Add(xstream.Dict)) continue; // cycle / shared-form guard
            var formContent = _reader.DecodeStream(xstream);
            if (formContent.Length == 0) continue;
            // A form may declare its own /Resources; absent, it inherits the page's.
            var formRes = _reader.ResolveDict(xstream.Dict.Get("Resources")) ?? resources;
            CollectContentResourceNames(formContent, formRes, used, visitedForms);
        }
    }

    /// <summary>Consume an inline image (the lexer has just read its <c>BI</c>):
    /// collect any /Name values among its parameters — an inline image's <c>/CS</c>
    /// may name a colour space declared in /Resources/ColorSpace — then skip the raw
    /// image bytes up to and including the <c>EI</c> terminator.</summary>
    private static void SkipInlineImage(IO.PdfLexer lexer, HashSet<string> used)
    {
        while (true)
        {
            var token = lexer.NextToken();
            if (token.Kind == IO.TokenKind.Eof) return;
            if (token.Kind == IO.TokenKind.Keyword && token.StringValue == "ID") break;
            if (token.Kind == IO.TokenKind.Name && token.StringValue is { } name)
                used.Add(name);
        }
        lexer.ReadInlineImageData();
    }

    /// <summary>Remove entries not present in <paramref name="used"/> from each
    /// prunable sub-dictionary of <paramref name="resources"/>.</summary>
    private void PruneResourceCategories(PdfDictionary resources, HashSet<string> used)
    {
        foreach (var category in PrunableResourceCategories)
        {
            var dict = _reader.ResolveDict(resources.Get(category));
            if (dict is null) continue;
            var unused = dict.Keys.Where(k => !used.Contains(k)).ToList();
            foreach (var key in unused)
                dict.Remove(key);
            if (!dict.Keys.Any())
                resources.Remove(category);
        }
    }

    /// <summary>
    /// Find streams with identical content and redirect duplicate references to a single canonical object.
    /// This reduces file size when the same content (e.g., images) appears multiple times.
    /// </summary>
    private void LinkDuplicateStreams()
    {
        // Phase 1: Hash every stream, once each. The file's own objects come first, so a copy made in
        // this session is linked to the file's object; then what the session added and no save has
        // written yet: the objects copied in with another document's pages, and the streams held
        // inline in page resources (an image placed on a page).
        var ls = new StreamLinkState();
        var objects = new List<PdfObject>();
        foreach (var entry in _reader.XRefTable.Entries.Values)
        {
            if (!entry.InUse || entry.ObjectNumber == 0) continue;
            var obj = _reader.Resolve(new PdfIndirectRef(entry.ObjectNumber, entry.Generation));
            if (obj is null) continue;
            objects.Add(obj);
            if (obj is PdfStream stream) AddLinkCandidate(ls, entry.ObjectNumber, entry.Generation, stream);
        }
        if (_pages is not null)
            foreach (var (objNum, obj) in _pages.ImportedObjects)
                if (obj is PdfStream stream) AddLinkCandidate(ls, objNum, 0, stream);
        var visited = new HashSet<PdfObject>(ReferenceEqualityComparer.Instance);
        foreach (var page in Pages)
            CollectInlineStreams(ls, page.Dict.Get("Resources"), visited);

        // The dictionary is part of the identity: a form with the same operators but other
        // resources, or an image with another mask, paints something else. A reference in it
        // counts by the object it will point at once duplicates are linked, so two images whose
        // masks are themselves duplicates are duplicates too: the rounds below link the masks
        // first and the images on the next pass, until no further link appears.
        while (true)
        {
            var hashToId = new Dictionary<string, int>(StringComparer.Ordinal);
            var linked = new Dictionary<int, int>();
            foreach (var (id, _, stream, contentHash) in ls.streams)
            {
                var hash = contentHash + StreamIdentity(stream.Dict, ls);
                if (hashToId.TryGetValue(hash, out var canonicalId))
                    linked[id] = canonicalId;
                else
                    hashToId[hash] = id;
            }
            // A pass can only add links (references become equal, never different), so an
            // unchanged count is the fixed point.
            var settled = linked.Count == ls.redirections.Count;
            ls.redirections = linked;
            if (settled) break;
        }

        if (ls.redirections.Count == 0) return;
        var kept = ls.streams.ToDictionary(s => s.id, s => (s.generation, s.stream));
        foreach (var (duplicate, canonical) in ls.redirections)
        {
            var (generation, stream) = kept[canonical];
            ls.targets[duplicate] = canonical < 0 ? stream : new PdfIndirectRef(canonical, generation);
        }

        // Phase 2: Replace the references throughout the document: the catalog and the pages, every
        // object of the file - a font program is referenced from a font descriptor, an image from a
        // resources dictionary or a form of its own - and the objects this session holds for the next save.
        RedirectReferences(_reader.Catalog, ls);
        foreach (var page in Pages)
            RedirectReferences(page.Dict, ls);
        foreach (var obj in objects)
            RedirectWithin(obj, ls);
        if (_pages is not null)
            foreach (var (_, obj) in _pages.ImportedObjects)
                RedirectWithin(obj, ls);
        foreach (var (_, obj) in _newObjects)
            RedirectWithin(obj, ls);
    }

    /// <summary>Hash one stream's decoded content for <see cref="LinkDuplicateStreams"/>.</summary>
    private void AddLinkCandidate(StreamLinkState ls, int id, int generation, PdfStream stream)
    {
        // Decode the stream data for content comparison; an image the duplicate-image pass
        // decoded just before is read from the reader's cache. The streams of an encrypted file
        // are held encrypted, so they are read through the reader, which decrypts them.
        byte[] decoded;
        try
        {
            decoded = _reader.DecodedIfCached(stream)
                ?? (_reader.IsDecrypted ? _reader.DecodeStream(stream) : StreamFilter.Decode(stream.RawData, stream.Dict));
        }
        catch
        {
            return; // Skip streams that fail to decode
        }

        // The platform's digest: every stream of the document goes through it, and the
        // library's own pure-C# one costs most of an optimize on a stream-heavy file.
        // Data the filters cannot read comes out empty, and compared as empty every such stream
        // would be one: it is compared by its stored bytes and their encoding instead.
        var unread = decoded.Length == 0 && stream.RawData.Length > 0;
        var hash = Compat.ToHexString(Compat.Sha256(unread ? stream.RawData : decoded));
        if (unread)
        {
            var encoding = new System.Text.StringBuilder(" stored ");
            AppendIdentity(encoding, stream.Dict.Get("Filter"), ls);
            AppendIdentity(encoding, stream.Dict.Get("DecodeParms"), ls);
            hash += encoding.ToString();
        }
        ls.streams.Add((id, generation, stream, hash));
    }

    /// <summary>The streams held inline under a page's resources: made in this session, they have no
    /// object number yet. Each object is looked at once across all pages, and a /Parent is not
    /// followed up the tree.</summary>
    private void CollectInlineStreams(StreamLinkState ls, PdfObject? resources, HashSet<PdfObject> visited)
    {
        var stack = new Stack<PdfObject>();
        if (resources is not null) stack.Push(resources);
        while (stack.Count > 0)
        {
            var held = stack.Pop();
            var obj = held is PdfIndirectRef iref ? _reader.Resolve(iref) : held;
            if (obj is null || !visited.Add(obj)) continue;
            if (obj is PdfStream stream)
            {
                if (held is PdfStream) AddLinkCandidate(ls, ls.IdOf(stream), 0, stream);
                obj = stream.Dict;
            }
            if (obj is PdfDictionary dict)
            {
                foreach (var key in dict.Keys)
                    if (key != "Parent" && dict.Get(key) is PdfIndirectRef or PdfDictionary or PdfArray or PdfStream)
                        stack.Push(dict.Get(key)!);
            }
            else if (obj is PdfArray array)
            {
                foreach (var item in array.Where(item => item is PdfIndirectRef or PdfDictionary or PdfArray or PdfStream))
                    stack.Push(item);
            }
        }
    }

    /// <summary>The entries of a stream dictionary that must agree for two streams with the same
    /// decoded content to be one object, in key order. The encoding entries are left out: the
    /// content is compared decoded.</summary>
    private static string StreamIdentity(PdfDictionary dict, StreamLinkState ls)
    {
        var sb = new System.Text.StringBuilder();
        // An image's identity is its samples and how to read them; the mask it is drawn through
        // is not part of it here, as before.
        var isImage = dict.GetName("Subtype") == "Image";
        foreach (var key in dict.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (key is "Length" or "Filter" or "DecodeParms" or "DL") continue;
            if (isImage && key is "SMask" or "Mask") continue;
            sb.Append('/').Append(key).Append(' ');
            AppendIdentity(sb, dict.Get(key), ls);
        }
        return sb.ToString();
    }

    private static void AppendIdentity(System.Text.StringBuilder sb, PdfObject? value, StreamLinkState ls)
    {
        switch (value)
        {
            case PdfIndirectRef iref:
                var target = ls.redirections.TryGetValue(iref.ObjectNumber, out var canonical) ? canonical : iref.ObjectNumber;
                sb.Append(target).Append(" R ");
                break;
            case PdfStream inline:
                // An inline stream counts by the stream it will be once duplicates are linked, as a reference does.
                var id = ls.IdOf(inline);
                sb.Append(ls.redirections.TryGetValue(id, out var kept) ? kept : id).Append(" S ");
                break;
            case PdfDictionary dict:
                sb.Append("<<");
                foreach (var key in dict.Keys.OrderBy(k => k, StringComparer.Ordinal))
                {
                    sb.Append('/').Append(key).Append(' ');
                    AppendIdentity(sb, dict.Get(key), ls);
                }
                sb.Append(">> ");
                break;
            case PdfArray array:
                sb.Append('[');
                foreach (var item in array) AppendIdentity(sb, item, ls);
                sb.Append("] ");
                break;
            case PdfString str:
                sb.Append('(').Append(Compat.ToHexString(str.Value)).Append(") ");
                break;
            default:
                sb.Append(value).Append(' ');
                break;
        }
    }

    /// <summary>Replace the references to duplicates held in an object: in a stream's dictionary, or
    /// in a dictionary or array and the direct ones nested in it.</summary>
    private static void RedirectWithin(PdfObject? obj, StreamLinkState ls)
    {
        switch (obj)
        {
            case PdfStream stream:
                RedirectReferences(stream.Dict, ls);
                break;
            case PdfDictionary dict:
                RedirectReferences(dict, ls);
                break;
            case PdfArray array:
                RedirectReferencesInArray(array, ls);
                break;
        }
    }

    /// <summary>
    /// Recursively replace references to duplicates in a dictionary tree.
    /// </summary>
    private static void RedirectReferences(PdfDictionary dict, StreamLinkState ls)
    {
        if (!ls.redirected.Add(dict)) return;
        // Every object of the document passes here, nearly all with nothing to change: the
        // replacements are gathered, and set once the keys are no longer enumerated.
        List<(string key, PdfObject value)>? replacements = null;
        foreach (var key in dict.Keys)
        {
            if (Redirected(dict.Get(key), ls) is { } replacement)
                (replacements ??= []).Add((key, replacement));
        }
        if (replacements is null) return;
        foreach (var (key, value) in replacements)
            dict.Set(key, value);
    }

    private static void RedirectReferencesInArray(PdfArray arr, StreamLinkState ls)
    {
        if (!ls.redirected.Add(arr)) return;
        for (var i = 0; i < arr.Count; i++)
        {
            if (Redirected(arr[i], ls) is { } replacement)
                arr.ReplaceAt(i, replacement);
        }
    }

    /// <summary>What a held value becomes when it names a duplicate, else null after redirecting
    /// within it.</summary>
    private static PdfObject? Redirected(PdfObject? value, StreamLinkState ls)
    {
        switch (value)
        {
            case PdfIndirectRef iref when ls.targets.TryGetValue(iref.ObjectNumber, out var target):
                return target;
            case PdfStream inline when ls.inlineIds.TryGetValue(inline, out var id) && ls.targets.TryGetValue(id, out var target):
                return target;
            case PdfStream or PdfDictionary or PdfArray:
                RedirectWithin(value, ls);
                break;
        }
        return null;
    }
}

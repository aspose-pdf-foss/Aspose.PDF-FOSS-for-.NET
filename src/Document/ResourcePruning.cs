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
    public void OptimizeResources() => OptimizeResources(Aspose.Pdf.Optimization.OptimizationOptions.Default);

    public void OptimizeResources(Aspose.Pdf.Optimization.OptimizationOptions strategy)
    {
        var options = strategy ?? Aspose.Pdf.Optimization.OptimizationOptions.Default;

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
        // installed system faces). The now-orphaned font streams fall out via the
        // reachability pass below.
        if (options.UnembedFonts)
        {
            FontSubsetter.UnembedFonts(_reader);
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

        // Compute reachable objects from the trailer. Done LAST, after the resource prune,
        // font unembedding, and duplicate-stream linking above, so any object those steps
        // orphaned (e.g. an unembedded /FontFile2 program or a linked-away duplicate) is
        // excluded from the saved file rather than written from a stale snapshot.
        var reachable = new HashSet<int>();
        if (options.RemoveUnusedObjects)
        {
            CollectReachable(_reader.Trailer, reachable);
            // Cross-document page imports aren't linked into the trailer's /Pages tree
            // until save (RebuildPagesTree), so walk the pending page dicts explicitly.
            // Otherwise their still-referenced imported resource objects look unreachable
            // and a copied page's images would be dropped from the saved file.
            if (_pages is not null)
                foreach (var pending in _pages.PendingAdds)
                    CollectReachable(pending.Dict, reachable);
        }

        // Mark document as needing optimization on next save
        _optimizationOptions = options;
        _reachableObjects = reachable.Count > 0 ? reachable : null;
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
                foreach (var item in arr)
                    if (item is not null) stack.Push(item);
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

        foreach (var (resources, used) in usedByResources)
        {
            if (keepAll.Contains(resources)) continue;
            AddReferencedXObjectNames(resources, used);
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
    private byte[]? PruneFontsInScope(byte[]? content, PdfDictionary? resources,
        HashSet<PdfDictionary> visitedForms)
    {
        if (content is null || content.Length == 0 || resources is null) return null;

        // Collect the fonts a `Tf` selects that actually SHOW text, and the form
        // XObjects a `Do` invokes. A font selected only by an empty run (`/F Tf`
        // followed by `[] TJ` with no glyphs, then another `Tf`) is not really used —
        // counting it would keep an orphan font after a full RemoveUnusedFonts replace.
        var usedFonts = new HashSet<string>(StringComparer.Ordinal);
        var formNames = new List<string>();
        var lexer = new IO.PdfLexer(content);
        string? lastName = null;      // most recent /Name operand (font for Tf, form for Do)
        string? currentFont = null;   // font selected by the last Tf
        bool sawGlyphs = false;       // a non-empty string appeared since the last operator
        while (true)
        {
            var token = lexer.NextToken();
            if (token.Kind == IO.TokenKind.Eof) break;
            switch (token.Kind)
            {
                case IO.TokenKind.Name when token.StringValue is { } n:
                    lastName = n;
                    break;
                case IO.TokenKind.LiteralString:
                case IO.TokenKind.HexString:
                    if (token.BytesValue is { Length: > 0 }) sawGlyphs = true;
                    break;
                case IO.TokenKind.Keyword:
                    var kw = token.StringValue;
                    if (kw == "BI") { SkipInlineImage(lexer, usedFonts); break; }
                    if (kw == "Tf") currentFont = lastName;
                    else if (kw == "Do" && lastName is not null) formNames.Add(lastName);
                    else if ((kw == "Tj" || kw == "TJ" || kw == "'" || kw == "\"")
                             && sawGlyphs && currentFont is not null)
                        usedFonts.Add(currentFont);
                    sawGlyphs = false; // operator boundary resets the operand scan
                    break;
            }
        }

        byte[]? rewritten = null;
        var fontDict = _reader.ResolveDict(resources.Get("Font"));
        if (fontDict is not null)
        {
            var pruned = new List<string>();
            foreach (var key in fontDict.Keys.ToList())
                if (!usedFonts.Contains(key))
                {
                    fontDict.Remove(key);
                    pruned.Add(key);
                }

            // Rename the replacement fonts (registered under an "AsRp…" key) to F0, F1, …,
            // avoiding collision with any surviving original font, and patch the content's
            // Tf operands to match.
            var survivors = fontDict.Keys.ToList();
            var taken = new HashSet<string>(survivors.Where(k => !k.StartsWith("AsRp", StringComparison.Ordinal)),
                StringComparer.Ordinal);
            var renameMap = new Dictionary<string, string>(StringComparer.Ordinal);
            var n = 0;
            foreach (var rk in survivors.Where(k => k.StartsWith("AsRp", StringComparison.Ordinal)))
            {
                string fn;
                do { fn = "F" + n++; } while (taken.Contains(fn));
                taken.Add(fn);
                renameMap[rk] = fn;
            }
            if (renameMap.Count > 0)
            {
                foreach (var (oldKey, newKey) in renameMap)
                {
                    var val = fontDict.Get(oldKey);
                    fontDict.Remove(oldKey);
                    if (val is not null) fontDict.Set(newKey, val);
                }
                // A pruned font is still SELECTED by the content: a `/F2 Tf` that shows
                // no text before the next Tf keeps its operator even though its resource
                // is gone, leaving a Tf pointing at nothing. Repoint those selections at
                // the replacement font so every Tf in the rewritten content names a font
                // that still exists.
                var replacement = renameMap.Values.OrderBy(v => v, StringComparer.Ordinal).First();
                foreach (var key in pruned)
                    renameMap[key] = replacement;
                rewritten = RepointTfNamesInContent(content, renameMap);
            }
        }

        var xobjects = _reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is not null)
        {
            foreach (var name in formNames)
            {
                var xstream = _reader.ResolveStream(xobjects.Get(name));
                if (xstream is null || xstream.Dict.GetName("Subtype") != "Form") continue;
                if (!visitedForms.Add(xstream.Dict)) continue; // cycle / shared-form guard
                var formRes = _reader.ResolveDict(xstream.Dict.Get("Resources"));
                // Only prune a form's OWN /Font dict — a form inheriting the page's
                // resources shares that dict, handled at the page scope.
                if (formRes is not null && !ReferenceEquals(formRes, resources))
                {
                    var newForm = PruneFontsInScope(_reader.DecodeStream(xstream), formRes, visitedForms);
                    if (newForm is not null)
                    {
                        xstream.Dict.Remove("Filter");
                        xstream.Dict.Remove("DecodeParms");
                        xstream.Dict.Set("Length", new PdfInteger(newForm.Length));
                        xstream.ReplaceData(newForm);
                    }
                }
            }
        }
        return rewritten;
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
        // Phase 1: Hash all stream objects
        var hashToObjNum = new Dictionary<string, int>(StringComparer.Ordinal);
        var redirections = new Dictionary<int, int>(); // oldObjNum → canonicalObjNum

        foreach (var entry in _reader.XRefTable.Entries.Values)
        {
            if (!entry.InUse || entry.ObjectNumber == 0) continue;

            var obj = _reader.Resolve(new PdfIndirectRef(entry.ObjectNumber, entry.Generation));
            if (obj is not PdfStream stream) continue;

            // Decode the stream data for content comparison
            byte[] decoded;
            try
            {
                decoded = StreamFilter.Decode(stream.RawData, stream.Dict);
            }
            catch
            {
                continue; // Skip streams that fail to decode
            }

            // Build a hash that includes stream properties (width/height/colorspace for images)
            var hash = System.Convert.ToHexString(Security.ShaDigest.Sha256(decoded));

            // Append key properties to distinguish structurally different streams
            var width = stream.Dict.GetInt("Width");
            var height = stream.Dict.GetInt("Height");
            if (width > 0) hash += $"_W{width}_H{height}";

            if (hashToObjNum.TryGetValue(hash, out var canonicalObjNum))
            {
                redirections[entry.ObjectNumber] = canonicalObjNum;
            }
            else
            {
                hashToObjNum[hash] = entry.ObjectNumber;
            }
        }

        if (redirections.Count == 0) return;

        // Phase 2: Replace indirect references throughout the document
        RedirectReferences(_reader.Catalog, redirections);

        // Also redirect in each page's annotations and resources
        foreach (var page in Pages)
        {
            RedirectReferences(page.Dict, redirections);
        }
    }

    /// <summary>
    /// Recursively replace indirect references in a dictionary tree.
    /// </summary>
    private void RedirectReferences(PdfDictionary dict, Dictionary<int, int> redirections)
    {
        foreach (var key in dict.Keys.ToList())
        {
            var value = dict.Get(key);
            switch (value)
            {
                case PdfIndirectRef iref when redirections.TryGetValue(iref.ObjectNumber, out var newObjNum):
                    dict.Set(key, new PdfIndirectRef(newObjNum, 0));
                    break;
                case PdfDictionary childDict:
                    RedirectReferences(childDict, redirections);
                    break;
                case PdfArray arr:
                    RedirectReferencesInArray(arr, redirections);
                    break;
            }
        }
    }

    private void RedirectReferencesInArray(PdfArray arr, Dictionary<int, int> redirections)
    {
        for (var i = 0; i < arr.Count; i++)
        {
            switch (arr[i])
            {
                case PdfIndirectRef iref when redirections.TryGetValue(iref.ObjectNumber, out var newObjNum):
                    arr.ReplaceAt(i, new PdfIndirectRef(newObjNum, 0));
                    break;
                case PdfDictionary childDict:
                    RedirectReferences(childDict, redirections);
                    break;
                case PdfArray nested:
                    RedirectReferencesInArray(nested, redirections);
                    break;
            }
        }
    }
}

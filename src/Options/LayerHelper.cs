using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

/// <summary>
/// Helper for layer content stream operations (delete, flatten, extract).
/// </summary>
internal static class LayerHelper
{
    /// <summary>
    /// Get the page content stream as a single byte array.
    /// </summary>
    internal static byte[] GetPageContentBytes(Page page)
    {
        var reader = page.Reader;
        var contents = reader.Resolve(page.Dict.Get("Contents"));

        if (contents is PdfStream stream)
            return reader.DecodeStream(stream);

        if (contents is PdfArray arr)
        {
            using var ms = new MemoryStream();
            foreach (var item in arr)
            {
                var s = reader.ResolveStream(item);
                if (s is not null)
                {
                    var data = reader.DecodeStream(s);
                    ms.Write(data, 0, data.Length);
                    ms.WriteByte((byte)'\n');
                }
            }
            return ms.ToArray();
        }

        return Array.Empty<byte>();
    }

    /// <summary>
    /// Extract content bytes from XForm objects that reference the given OCG dict.
    /// </summary>
    internal static IReadOnlyList<byte[]> ExtractXFormLayerContents(Page page, string layerId, PdfDictionary ocgDict)
    {
        var reader = page.Reader;
        var resources = reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null) return Array.Empty<byte[]>();

        var xobjects = reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is null) return Array.Empty<byte[]>();

        // Get OCG name for fallback comparison
        var ocgName = GetOcgName(ocgDict);

        var results = new List<byte[]>();
        foreach (var key in xobjects.Keys)
        {
            var xobj = reader.ResolveStream(xobjects.Get(key));
            if (xobj is null) continue;

            var ocRef = xobj.Dict.Get("OC");
            if (ocRef is null) continue;

            if (MatchesOcg(reader, ocRef, ocgDict, ocgName))
            {
                var data = reader.DecodeStream(xobj);
                if (data.Length > 0)
                {
                    // Split into individual operator lines to match .NET behavior
                    SplitContentIntoOperators(data, results);
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Split a content stream into individual operator lines.
    /// Each non-empty line (trimmed) becomes a separate byte[] entry.
    /// </summary>
    private static void SplitContentIntoOperators(byte[] data, List<byte[]> results)
    {
        var text = Encoding.Latin1.GetString(data);
        var lines = text.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim('\r', ' ', '\t');
            if (trimmed.Length > 0)
                results.Add(Encoding.Latin1.GetBytes(trimmed));
        }
    }

    /// <summary>
    /// Check if an /OC reference points to the given OCG dict (directly, via OCMD, or by name).
    /// </summary>
    private static bool MatchesOcg(PdfReader reader, PdfObject ocRef, PdfDictionary ocgDict, string? ocgName)
    {
        var ocDict = reader.ResolveDict(ocRef);
        if (ocDict is null) return false;

        // Direct match by reference
        if (ReferenceEquals(ocDict, ocgDict)) return true;

        // Match by OCG Name
        var resolvedName = GetOcgName(ocDict);
        if (ocgName is not null && resolvedName == ocgName) return true;

        // OCMD: check /OCGs
        var ocmdOcgs = reader.Resolve(ocDict.Get("OCGs"));
        if (ocmdOcgs is PdfDictionary singleOcg)
        {
            if (ReferenceEquals(singleOcg, ocgDict)) return true;
            if (ocgName is not null && GetOcgName(singleOcg) == ocgName) return true;
        }
        else if (ocmdOcgs is PdfArray arr)
        {
            foreach (var item in arr)
            {
                var d = reader.ResolveDict(item);
                if (d is null) continue;
                if (ReferenceEquals(d, ocgDict)) return true;
                if (ocgName is not null && GetOcgName(d) == ocgName) return true;
            }
        }

        return false;
    }

    private static string? GetOcgName(PdfDictionary dict)
    {
        var obj = dict.Get("Name");
        if (obj is PdfString s) return s.ToText();
        return dict.GetName("Name");
    }

    /// <summary>
    /// Extract content bytes that belong to a specific layer (between /OC /{id} BDC … EMC).
    /// </summary>
    internal static IReadOnlyList<byte[]> ExtractLayerContents(Page page, string layerId)
    {
        var contentBytes = GetPageContentBytes(page);
        var text = Encoding.Latin1.GetString(contentBytes);
        var results = new List<byte[]>();

        // Find all /OC /{layerId} BDC … EMC blocks
        var pattern = $@"/OC\s+/{Regex.Escape(layerId)}\s+BDC\b";
        var matches = Regex.Matches(text, pattern);

        foreach (Match m in matches)
        {
            var start = m.Index + m.Length;
            var depth = 1;
            var pos = start;

            while (pos < text.Length && depth > 0)
            {
                // Find next BDC or EMC
                var bdcIdx = FindOperator(text, "BDC", pos);
                var emcIdx = FindOperator(text, "EMC", pos);

                if (emcIdx < 0) break; // malformed

                if (bdcIdx >= 0 && bdcIdx < emcIdx)
                {
                    depth++;
                    pos = bdcIdx + 3;
                }
                else
                {
                    depth--;
                    if (depth == 0)
                    {
                        var block = text.Substring(start, emcIdx - start).Trim();
                        if (block.Length > 0)
                            results.Add(Encoding.Latin1.GetBytes(block));
                    }
                    pos = emcIdx + 3;
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Flatten a layer — remove BDC/EMC markers but keep the content.
    /// Also removes the OCG from document-level OCProperties.
    /// </summary>
    internal static void FlattenLayer(Page page, string layerId, PdfDictionary ocgDict)
    {
        var contentBytes = GetPageContentBytes(page);

        // Replay the page content dropping only the target's markers — the
        // operation rewrites (re-serializes) the whole stream, so the
        // template-compared output must go through the same writer.
        var result = LayerContentFilter.Filter(
            contentBytes, layerId, Array.Empty<string>(), LayerFilterMode.Flatten);
        if (result is not null)
            page.SetContentStream(result);

        // Remove the OCG property from page resources
        RemovePropertyFromResources(page, layerId);

        // Remove OCG from document-level OCProperties
        RemoveOcgFromDocument(page, ocgDict);

        // Clean up /OC refs from XObjects
        CleanupXObjectOcRefs(page);
    }

    /// <summary>
    /// Delete a layer — remove BDC/EMC blocks AND their content.
    /// Also removes the OCG from document-level OCProperties.
    /// </summary>
    internal static void DeleteLayer(Page page, string layerId, PdfDictionary ocgDict)
    {
        var contentBytes = GetPageContentBytes(page);

        // Replay the page content reducing the target's blocks to their state
        // skeleton and dropping its XObject draws — the operation
        // rewrites (re-serializes) the whole stream, so the template-compared
        // output must go through the same writer.
        var result = LayerContentFilter.Filter(
            contentBytes, layerId, FindLayerXObjectNames(page, layerId, ocgDict),
            LayerFilterMode.Delete);
        if (result is not null)
            page.SetContentStream(result);

        // Remove the OCG property from page resources
        RemovePropertyFromResources(page, layerId);

        // XForm-style layer (/OC on a Form XObject): remove the XObject
        // resource entries so the OCG reference does not survive the save
        // (the filter already dropped the Do invocations).
        RemoveXFormLayerResources(page, ocgDict);

        // Remove OCG from document-level OCProperties
        RemoveOcgFromDocument(page, ocgDict);
    }

    /// <summary>Delete an XForm-level layer: drop the Do invocations of every Form
    /// XObject whose /OC matches <paramref name="ocgDict"/> from the page content,
    /// then remove those XObject resource entries. No-op for BDC-style layers.</summary>
    /// <summary>Remove the XObject resource entries of every Form XObject whose
    /// /OC matches <paramref name="ocgDict"/>. The content filter has already
    /// dropped their Do invocations. No-op for BDC-style layers.</summary>
    private static void RemoveXFormLayerResources(Page page, PdfDictionary ocgDict)
    {
        var reader = page.Reader;
        var resources = reader.ResolveDict(page.Dict.Get("Resources"));
        var xobjects = resources is null ? null : reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is null) return;

        var ocgName = GetOcgName(ocgDict);
        var toRemove = new List<string>();
        foreach (var key in xobjects.Keys)
        {
            var xobj = reader.ResolveStream(xobjects.Get(key));
            var ocRef = xobj?.Dict.Get("OC");
            if (ocRef is null) continue;
            if (MatchesOcg(reader, ocRef, ocgDict, ocgName))
                toRemove.Add(key);
        }
        foreach (var name in toRemove)
            xobjects.Remove(name);
    }

    /// <summary>
    /// Save the layer content as a new single-page PDF with only that layer's content.
    /// </summary>
    internal static byte[] SaveLayerToPdf(Page page, string layerId)
    {
        return SaveLayerToPdf(page, layerId, null);
    }

    internal static byte[] SaveLayerToPdf(Page page, string layerId, PdfDictionary? ocgDict)
    {
        // Layer.Save REPLAYS the whole page content, keeping the
        // target layer's ops verbatim and reducing everything else to its
        // structure/state skeleton — see LayerContentFilter for the full model.
        var pageBytes = GetPageContentBytes(page);
        var xobjNames = ocgDict is not null
            ? FindLayerXObjectNames(page, layerId, ocgDict)
            : Array.Empty<string>();
        var filtered = LayerContentFilter.Filter(pageBytes, layerId, xobjNames);
        if (filtered is null)
        {
            // No contribution at all — still a valid single-page document (a
            // 0-page file fails to open).
            var emptyDoc = Document.Create();
            var mb = page.MediaBox;
            emptyDoc.Pages.Add(mb.Width, mb.Height);
            return emptyDoc.ToArray();
        }

        var mediaBox = page.MediaBox;
        var doc = Document.Create();
        var newPage = doc.Pages.Add(mediaBox.Width, mediaBox.Height);
        CopyLayerResources(page, newPage, filtered);
        newPage.SetContentStream(filtered);
        return doc.ToArray();
    }

    /// <summary>The page XObject resource names whose stream carries this
    /// layer's /OC — the Do-style layer contributions the content filter keeps.</summary>
    private static IReadOnlyCollection<string> FindLayerXObjectNames(
        Page page, string layerId, PdfDictionary ocgDict)
    {
        _ = layerId;
        var reader = page.Reader;
        var resources = reader.ResolveDict(page.Dict.Get("Resources"));
        var xobjects = resources is null ? null : reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is null) return Array.Empty<string>();
        var ocgName = GetOcgName(ocgDict);
        List<string>? names = null;
        foreach (var key in xobjects.Keys)
        {
            var xobj = reader.ResolveStream(xobjects.Get(key));
            var ocRef = xobj?.Dict.Get("OC");
            if (ocRef is null) continue;
            if (MatchesOcg(reader, ocRef, ocgDict, ocgName))
                (names ??= new List<string>()).Add(key);
        }
        return (IReadOnlyCollection<string>?)names ?? Array.Empty<string>();
    }

    /// <summary>
    /// Merge all layers on a page into a single layer with the given name.
    /// </summary>
    internal static void MergeLayersOnPage(Page page, string newLayerName, PdfReader reader)
    {
        var layers = GetPageLayers(page, reader);
        if (layers.Count == 0) return;

        // Flatten all existing layers (remove BDC/EMC markers, keep content)
        var contentBytes = GetPageContentBytes(page);
        var text = Encoding.Latin1.GetString(contentBytes);

        foreach (var layer in layers)
        {
            if (layer.Id is not null)
            {
                text = RemoveLayerMarkers(text, layer.Id, keepContent: true);
                RemovePropertyFromResources(page, layer.Id);
                RemoveOcgFromDocument(page, layer.Dict);
            }
        }

        // Clean up /OC refs from XObjects
        CleanupXObjectOcRefs(page);

        // Create a new OCG for the merged layer
        var ocgDict = new PdfDictionary();
        ocgDict.Set("Type", new PdfName("OCG"));
        ocgDict.Set("Name", new PdfString(Encoding.Latin1.GetBytes(newLayerName)));

        // Register in page Resources/Properties
        var resources = reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            page.Dict.Set("Resources", resources);
        }
        var props = reader.ResolveDict(resources.Get("Properties"));
        if (props is null)
        {
            props = new PdfDictionary();
            resources.Set("Properties", props);
        }
        var propName = "MC0";
        props.Set(propName, ocgDict);

        // Wrap all content in new BDC/EMC
        var wrappedContent = $"/OC /{propName} BDC\n{text.Trim()}\nEMC\n";
        page.SetContentStream(Encoding.Latin1.GetBytes(wrappedContent));

        // Add OCG to document OCProperties
        var catalog = reader.Catalog;
        var ocPropsDict = reader.ResolveDict(catalog.Get("OCProperties"));
        if (ocPropsDict is null)
        {
            ocPropsDict = new PdfDictionary();
            catalog.Set("OCProperties", ocPropsDict);
        }
        var ocgsArr = reader.Resolve(ocPropsDict.Get("OCGs")) as PdfArray ?? new PdfArray();
        ocgsArr.Add(ocgDict);
        ocPropsDict.Set("OCGs", ocgsArr);

        var dConfig = reader.ResolveDict(ocPropsDict.Get("D"));
        if (dConfig is null)
        {
            dConfig = new PdfDictionary();
            ocPropsDict.Set("D", dConfig);
        }
        var orderArr = reader.Resolve(dConfig.Get("Order")) as PdfArray ?? new PdfArray();
        orderArr.Add(ocgDict);
        dConfig.Set("Order", orderArr);
    }

    /// <summary>
    /// Get layers on a specific page by inspecting Resources/Properties for OCG references,
    /// and also XForm /OC references in the page's XObject resources.
    /// </summary>
    internal static List<OptionalContentGroup> GetPageLayers(Page page, PdfReader reader)
    {
        var result = new List<OptionalContentGroup>();

        // Get document-level OCG properties so layers can persist state changes.
        // Build a lookup from OCG dict → existing group instance so that changes
        // to a page layer's DefaultState propagate to the document-level group.
        var ocPropsDict = reader.ResolveDict(reader.Catalog.Get("OCProperties"));
        var ocProps = ocPropsDict is not null ? new OptionalContentProperties(ocPropsDict, reader) : null;
        var ocgLookup = new Dictionary<PdfDictionary, OptionalContentGroup>(ReferenceEqualityComparer.Instance);
        if (ocProps is not null)
        {
            for (int i = 0; i < ocProps.Count; i++)
                ocgLookup[ocProps[i].Dict] = ocProps[i];
        }

        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var resources = reader.ResolveDict(page.Dict.Get("Resources"));

        // 1. Check Resources/Properties for OCG references (BDC-style layers)
        if (resources is not null)
        {
            var props = reader.ResolveDict(resources.Get("Properties"));
            if (props is not null)
            {
                foreach (var key in props.Keys)
                {
                    var propDict = reader.ResolveDict(props.Get(key));
                    if (propDict is null) continue;

                    var type = propDict.GetName("Type");
                    if (type != "OCG") continue;
                    if (!seen.Add(propDict)) continue;

                    // Reuse the document-level group instance so state changes propagate
                    OptionalContentGroup ocg;
                    if (ocgLookup.TryGetValue(propDict, out var existing))
                    {
                        ocg = existing;
                        ocg.Id = key;
                        ocg._page = page;
                    }
                    else
                    {
                        ocg = new OptionalContentGroup(propDict) { Id = key, _page = page };
                        ocg.SetRegisteredReader(reader);
                        ApplyDocLevelState(ocg, propDict, reader);
                    }
                    result.Add(ocg);
                }
            }

            // 2. Check XObject resources for /OC references (XForm-level layers)
            var xobjects = reader.ResolveDict(resources.Get("XObject"));
            if (xobjects is not null)
            {
                foreach (var key in xobjects.Keys)
                {
                    var xobj = reader.ResolveStream(xobjects.Get(key));
                    if (xobj is null) continue;

                    var ocRef = xobj.Dict.Get("OC");
                    if (ocRef is null) continue;

                    var ocDict = reader.ResolveDict(ocRef);
                    if (ocDict is null) continue;

                    // /OC can point directly to an OCG dict or to an OCMD
                    var ocType = ocDict.GetName("Type");
                    PdfDictionary? actualOcgDict = null;
                    string? propId = null;

                    if (ocType == "OCG")
                    {
                        actualOcgDict = ocDict;
                    }
                    else if (ocType == "OCMD")
                    {
                        // OCMD — resolve the first OCG in its /OCGs
                        var ocmdOcgs = reader.Resolve(ocDict.Get("OCGs"));
                        if (ocmdOcgs is PdfArray arr && arr.Count > 0)
                            actualOcgDict = reader.ResolveDict(arr[0]);
                        else if (ocmdOcgs is PdfDictionary d)
                            actualOcgDict = d;
                    }
                    else
                    {
                        // No /Type — assume it's an OCG
                        actualOcgDict = ocDict;
                    }

                    if (actualOcgDict is null) continue;

                    // Don't dedup XObject-level layers — each XObject with /OC
                    // is a separate layer entry (matches the public behavior).
                    propId = key;
                    // XObject layers: always create new instances since multiple
                    // XObjects may share the same OCG but need separate Id/page refs.
                    // Copy visibility state from the document-level group if available.
                    var ocg = new OptionalContentGroup(actualOcgDict) { Id = propId, _page = page };
                    ocg.SetRegisteredReader(reader);
                    if (ocgLookup.TryGetValue(actualOcgDict, out var docGroup))
                    {
                        ocg.IsVisible = docGroup.IsVisible;
                        ocg.IsLocked = docGroup.IsLocked;
                        ocg.SetOwner(ocProps!);
                        ocg._docTwin = docGroup;
                    }
                    else
                    {
                        ApplyDocLevelState(ocg, actualOcgDict, reader);
                    }
                    result.Add(ocg);
                }

            }
        }

        return result;
    }

    private static void ApplyDocLevelState(OptionalContentGroup ocg, PdfDictionary ocgDict, PdfReader reader)
    {
        var ocPropsDict = reader.ResolveDict(reader.Catalog.Get("OCProperties"));
        if (ocPropsDict is null) return;

        var dConfig = reader.ResolveDict(ocPropsDict.Get("D"));
        if (dConfig is null) return;

        var ocgName = ocg.Name;

        var offArray = reader.Resolve(dConfig.Get("OFF")) as PdfArray;
        if (offArray is not null)
        {
            foreach (var item in offArray)
            {
                var d = reader.ResolveDict(item);
                if (d is not null && MatchesOcg(d, ocgDict, ocgName))
                    ocg.IsVisible = false;
            }
        }

        var lockedArray = reader.Resolve(dConfig.Get("Locked")) as PdfArray;
        if (lockedArray is not null)
        {
            foreach (var item in lockedArray)
            {
                var d = reader.ResolveDict(item);
                if (d is not null && MatchesOcg(d, ocgDict, ocgName))
                    ocg.IsLocked = true;
            }
        }
    }

    private static bool MatchesOcg(PdfDictionary candidate, PdfDictionary ocgDict, string ocgName)
    {
        if (ReferenceEquals(candidate, ocgDict)) return true;
        // Fallback: compare by /Name for OCG dicts that were inlined during save
        if (candidate.GetName("Type") == "OCG" && ocgName.Length > 0)
        {
            var nameObj = candidate.Get("Name");
            var candidateName = nameObj is PdfString s ? s.ToText() : "";
            return candidateName == ocgName;
        }
        return false;
    }

    private static string RemoveLayerMarkers(string text, string layerId, bool keepContent)
    {
        var sb = new StringBuilder(text.Length);
        var pattern = $@"/OC\s+/{Regex.Escape(layerId)}\s+BDC\b";
        int lastEnd = 0;

        var matches = Regex.Matches(text, pattern);
        foreach (Match m in matches)
        {
            // Add text before this BDC
            sb.Append(text, lastEnd, m.Index - lastEnd);

            // Find matching EMC
            var start = m.Index + m.Length;
            var depth = 1;
            var pos = start;
            var emcEnd = -1;

            while (pos < text.Length && depth > 0)
            {
                var bdcIdx = FindOperator(text, "BDC", pos);
                var emcIdx = FindOperator(text, "EMC", pos);

                if (emcIdx < 0) { emcEnd = text.Length; break; }

                if (bdcIdx >= 0 && bdcIdx < emcIdx)
                {
                    depth++;
                    pos = bdcIdx + 3;
                }
                else
                {
                    depth--;
                    if (depth == 0)
                    {
                        emcEnd = emcIdx + 3;
                        if (keepContent)
                        {
                            var content = text.Substring(start, emcIdx - start).Trim();
                            if (content.Length > 0)
                            {
                                sb.Append(content);
                                sb.Append('\n');
                            }
                        }
                    }
                    pos = emcIdx + 3;
                }
            }

            lastEnd = emcEnd >= 0 ? emcEnd : text.Length;
        }

        // Add remaining text
        if (lastEnd < text.Length)
            sb.Append(text, lastEnd, text.Length - lastEnd);

        return sb.ToString();
    }

    private static int FindOperator(string text, string op, int startPos)
    {
        var idx = startPos;
        while (idx < text.Length)
        {
            idx = text.IndexOf(op, idx, StringComparison.Ordinal);
            if (idx < 0) return -1;

            // Verify it's a standalone operator (not part of another word)
            bool validBefore = idx == 0 || !char.IsLetterOrDigit(text[idx - 1]);
            bool validAfter = idx + op.Length >= text.Length ||
                              !char.IsLetterOrDigit(text[idx + op.Length]);

            if (validBefore && validAfter) return idx;
            idx += op.Length;
        }
        return -1;
    }

    private static void RemovePropertyFromResources(Page page, string layerId)
    {
        var resources = page.Reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null) return;

        var props = page.Reader.ResolveDict(resources.Get("Properties"));
        if (props is null) return;

        props.Remove(layerId);

        // If Properties is now empty, remove it
        if (!props.Keys.Any())
            resources.Remove("Properties");
    }

    private static void RemoveOcgFromDocument(Page page, PdfDictionary ocgDict)
    {
        var reader = page.Reader;
        var catalog = reader.Catalog;
        var ocPropsDict = reader.ResolveDict(catalog.Get("OCProperties"));
        if (ocPropsDict is null) return;

        var ocProps = new OptionalContentProperties(ocPropsDict, reader);
        ocProps.RemoveOcg(ocgDict);
    }

    private static void CleanupXObjectOcRefs(Page page)
    {
        var reader = page.Reader;
        var resources = reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null) return;

        var xobjects = reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is null) return;

        foreach (var key in xobjects.Keys)
        {
            var xobj = reader.ResolveStream(xobjects.Get(key));
            if (xobj is not null)
                xobj.Dict.Remove("OC");
        }
    }

    private static void CopyLayerResources(Page source, Page target, byte[] contentBytes)
    {
        var reader = source.Reader;
        var srcResources = reader.ResolveDict(source.Dict.Get("Resources"));
        if (srcResources is null) return;

        // Copy Font and XObject resources
        var targetResources = new PdfDictionary();

        var srcFonts = reader.ResolveDict(srcResources.Get("Font"));
        if (srcFonts is not null)
            targetResources.Set("Font", srcFonts);

        var srcXObjects = reader.ResolveDict(srcResources.Get("XObject"));
        if (srcXObjects is not null)
        {
            // Clone XObjects without /OC refs
            var newXObjects = new PdfDictionary();
            foreach (var key in srcXObjects.Keys)
            {
                newXObjects.Set(key, srcXObjects.Get(key)!);
            }
            targetResources.Set("XObject", newXObjects);
        }

        var srcExtGState = reader.ResolveDict(srcResources.Get("ExtGState"));
        if (srcExtGState is not null)
            targetResources.Set("ExtGState", srcExtGState);

        var srcColorSpace = reader.ResolveDict(srcResources.Get("ColorSpace"));
        if (srcColorSpace is not null)
            targetResources.Set("ColorSpace", srcColorSpace);

        target.Dict.Set("Resources", targetResources);
    }

    /// <summary>
    /// Add a new layer to a page: register the OCG, inject BDC/EMC content.
    /// </summary>
    internal static void AddLayerToPage(Page page, OptionalContentGroup layer)
    {
        var reader = page.Reader;
        var pageDict = page.Dict;

        // 1. Ensure Resources/Properties exists
        var resources = reader.ResolveDict(pageDict.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            pageDict.Set("Resources", resources);
        }
        var properties = reader.ResolveDict(resources.Get("Properties"));
        if (properties is null)
        {
            properties = new PdfDictionary();
            resources.Set("Properties", properties);
        }

        // 2. Assign a property name (MC0, MC1, .) for the OCG on this page
        var propName = layer.Id ?? "MC0";
        int counter = 0;
        while (properties.ContainsKey(propName))
            propName = $"MC{++counter}";
        layer.Id = propName;
        properties.Set(propName, layer.Dict);

        // 3. Register OCG in document OCProperties
        RegisterOcgInDocument(page, layer);

        // 4. Build content bytes from pending operators
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"/OC /{propName} BDC");
        if (layer.PendingOperators is not null)
        {
            foreach (var op in layer.PendingOperators)
                sb.AppendLine(op.ToPdf());
        }
        sb.AppendLine("EMC");
        var layerBytes = System.Text.Encoding.ASCII.GetBytes(sb.ToString());

        // 5. Append to page content stream
        var existing = reader.Resolve(pageDict.Get("Contents"));
        byte[] existingData;
        if (existing is PdfStream es)
            existingData = reader.DecodeStream(es);
        else if (existing is PdfArray arr)
        {
            using var ms = new System.IO.MemoryStream();
            foreach (var item in arr)
            {
                var s = reader.ResolveStream(item);
                if (s is not null)
                {
                    var d = reader.DecodeStream(s);
                    ms.Write(d, 0, d.Length);
                    ms.WriteByte((byte)'\n');
                }
            }
            existingData = ms.ToArray();
        }
        else
            existingData = [];

        var combined = new byte[existingData.Length + 1 + layerBytes.Length];
        existingData.CopyTo(combined, 0);
        if (existingData.Length > 0) combined[existingData.Length] = (byte)'\n';
        layerBytes.CopyTo(combined, existingData.Length + (existingData.Length > 0 ? 1 : 0));
        pageDict.Set("Contents", new PdfStream(new PdfDictionary(), combined));

        layer._page = page;
    }

    private static void RegisterOcgInDocument(Page page, OptionalContentGroup layer)
    {
        var reader = page.Reader;
        var catalog = reader.Catalog;

        // Get or create OCProperties
        var ocProps = reader.ResolveDict(catalog.Get("OCProperties"));
        if (ocProps is null)
        {
            ocProps = new PdfDictionary();
            catalog.Set("OCProperties", ocProps);
        }

        layer.SetRegisteredReader(reader);

        // Add to /OCGs array
        var ocgs = reader.Resolve(ocProps.Get("OCGs")) as PdfArray;
        if (ocgs is null)
        {
            ocgs = new PdfArray();
            ocProps.Set("OCGs", ocgs);
        }
        ocgs.Add(layer.Dict);

        // Ensure /D (default config) exists with /Order
        var defaultConfig = reader.ResolveDict(ocProps.Get("D"));
        if (defaultConfig is null)
        {
            defaultConfig = new PdfDictionary();
            defaultConfig.Set("Name", new PdfString(System.Text.Encoding.Latin1.GetBytes("Default")));
            ocProps.Set("D", defaultConfig);
        }

        // Add to /Order array (controls display in viewers)
        var order = reader.Resolve(defaultConfig.Get("Order")) as PdfArray;
        if (order is null)
        {
            order = new PdfArray();
            defaultConfig.Set("Order", order);
        }
        order.Add(layer.Dict);

        // Persist lock state if locked
        if (layer.IsLocked)
        {
            var locked = reader.Resolve(defaultConfig.Get("Locked")) as PdfArray;
            if (locked is null)
            {
                locked = new PdfArray();
                defaultConfig.Set("Locked", locked);
            }
            locked.Add(layer.Dict);
        }

        // Persist visibility state
        if (!layer.IsVisible)
        {
            var off = reader.Resolve(defaultConfig.Get("OFF")) as PdfArray;
            if (off is null)
            {
                off = new PdfArray();
                defaultConfig.Set("OFF", off);
            }
            off.Add(layer.Dict);
        }
    }
}

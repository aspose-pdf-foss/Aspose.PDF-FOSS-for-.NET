using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfFileEditor
{
    /// <summary>How the facade reacts to corrupted input during Concatenate.</summary>
    public enum ConcatenateCorruptedFileAction
    {
        /// <summary>Stop on the first corrupted file (.NET default).</summary>
        StopWithError,
        /// <summary>Skip the corrupted file and continue concatenating.</summary>
        ConcatenateIgnoringCorruptedObjects,
        /// <summary>Alias for <see cref="ConcatenateIgnoringCorruptedObjects"/>.</summary>
        ConcatenateIgnoringCorrupted = ConcatenateIgnoringCorruptedObjects,
    }

    /// <summary>
    /// Mirrors <c>PdfFileEditor.CorruptedFileAction</c>. Stored only;
    /// Concatenate currently stops on any PDF parse error regardless of this value.
    /// </summary>
    public ConcatenateCorruptedFileAction CorruptedFileAction { get; set; }

    /// <summary>
    /// When true, the stream-based Concatenate / Append / Insert overloads
    /// dispose the input streams (and the output stream after writing) once
    /// the operation completes. Default is false — callers retain ownership
    /// of their streams. Mirrors the .NET API.
    /// </summary>
    public bool CloseConcatenatedStreams { get; set; }

    /// <summary>
    /// Concatenate multiple PDF documents into one.
    /// </summary>
    public byte[] Concatenate(params byte[][] inputFiles)
    {
        if (inputFiles.Length == 0)
            throw new ArgumentException("At least one input file required", nameof(inputFiles));

        if (inputFiles.Length == 1)
            return inputFiles[0];

        var allPageObjNums = new List<int>();

        // Use a temp file to avoid MemoryStream 2GB limit for large concatenations (100+ copies)
        var tempPath = Path.GetTempFileName();
        try
        {
            using (var output = File.Create(tempPath))
            {
                var writer = new PdfWriter(output);
                // Concatenated output is written as PDF 1.7 regardless
                // of the input versions.
                writer.WriteHeader("1.7");

                // Reserve obj 1 (catalog) and obj 2 (pages) — written last once all pages known.
                // All resource/page/content objects are allocated from 3 upwards.
                writer.SetMinObjectNumber(3);

                // Parse all inputs once, reuse readers for outlines/embedded files/AcroForm.
                // inputPageCounts preserves pages-per-input so MergeOutlines can shift
                // destinations by the sum of preceding page counts.
                var inputReaders = new List<PdfReader>();
                var inputPageCounts = new List<int>();
                // Retain the first input's reader + object map so its catalog-level
                // /OpenAction can be preserved and remapped through
                // the same map — the open-action destination then still points at its
                // (already-written) page rather than a duplicate.
                PdfReader? firstReader = null;
                Dictionary<int, int>? firstObjRemap = null;
                // Per-input seed maps for MergeOutlines: the input's object map PLUS the
                // source-page-object → output-page-object pairs, so an outline /Dest that
                // references a page remaps onto the page ALREADY written into the tree
                // instead of cloning an orphan copy of it.
                var inputOutlineSeeds = new List<Dictionary<int, int>>();
                // Per-input offset for the /StructParents key space: parent-tree keys of
                // all inputs share one numbering in the output, so each input's keys are
                // shifted by the key span of the inputs before it (the same shape as the
                // page-count shift MergeOutlines applies to destinations).
                var structParentBases = new List<int>();
                var structParentNext = 0;
                foreach (var inputData in inputFiles)
                {
                    PdfReader reader;
                    try
                    {
                        reader = PdfReader.FromBytes(inputData);
                    }
                    catch (Exception ex)
                    {
                        // An input that is not a PDF at all names ITSELF in the failure: the
                        // concatenation reports which file (1-based) it choked on and keeps the
                        // parse error as the InnerException, so callers can read the root cause.
                        throw new ArgumentException(
                            $"Exception occured during the processing file: {inputReaders.Count + 1}", ex);
                    }
                    inputReaders.Add(reader);
                    var catalog = reader.Catalog;
                    var pagesDict = reader.ResolveDict(catalog.Get("Pages"));
                    if (pagesDict is null) { inputPageCounts.Add(0); inputOutlineSeeds.Add(new Dictionary<int, int>()); structParentBases.Add(structParentNext); continue; }

                    var pages = new List<PdfDictionary>();
                    var pageSrcNums = new List<int>();
                    CollectPages(pagesDict, reader, pages, pageSrcNums);
                    inputPageCounts.Add(pages.Count);

                    // This input's slice of the merged parent-tree key space.
                    structParentBases.Add(structParentNext);
                    if (reader.ResolveDict(catalog.Get("StructTreeRoot")) is { } structRootDict)
                        structParentNext += StructParentKeySpan(structRootDict, reader);

                    // Per-input object remapping: sourceObjNum → outputObjNum.
                    // Objects referenced by multiple pages in the same input are written once
                    // and shared via indirect refs — preventing resource duplication bloat.
                    var objRemap = new Dictionary<int, int>();
                    var pageSrcToOut = new Dictionary<int, int>();
                    if (firstReader is null) { firstReader = reader; firstObjRemap = objRemap; }

                    for (var pi = 0; pi < pages.Count; pi++)
                    {
                        var pageDict = pages[pi];
                        // Remap the page dict: each source indirect ref is assigned a new
                        // output obj num and the referenced object is written once.
                        // RemapObject uses writer.AllocateObjectNumber() so it stays in sync
                        // with deferred stream promotions inside PdfWriter.WriteDictionary.
                        var cloned = (PdfDictionary)RemapObject(pageDict, reader, objRemap, writer);
                        cloned.Set("Parent", new PdfIndirectRef(2, 0));
                        if (structParentBases[structParentBases.Count - 1] is > 0 and var spBase
                            && cloned.Get("StructParents") is PdfInteger spKey)
                            cloned.Set("StructParents", new PdfInteger(spKey.Value + spBase));

                        var pageObjNum = writer.AllocateObjectNumber();
                        writer.WriteIndirectObject(pageObjNum, cloned);
                        allPageObjNums.Add(pageObjNum);
                        // Remember where this SOURCE page landed (for the outline seed
                        // below) without touching objRemap itself — the main pass keeps
                        // its allocation order.
                        if (pageSrcNums[pi] >= 0) pageSrcToOut[pageSrcNums[pi]] = pageObjNum;
                    }
                    var outlineSeed = new Dictionary<int, int>(objRemap);
                    foreach (var kv in pageSrcToOut) outlineSeed[kv.Key] = kv.Value;
                    inputOutlineSeeds.Add(outlineSeed);
                }

                // Write Pages object
                var kids = new PdfArray();
                foreach (var pObjNum in allPageObjNums)
                    kids.Add(new PdfIndirectRef(pObjNum, 0));

                var pagesObj = new PdfDictionary();
                pagesObj.Set("Type", new PdfName("Pages"));
                pagesObj.Set("Kids", kids);
                pagesObj.Set("Count", new PdfInteger(allPageObjNums.Count));
                writer.WriteIndirectObject(2, pagesObj);

                // Merge embedded files from all input documents
                var embeddedEntries = new List<(string name, PdfObject fileSpec)>();
                foreach (var reader in inputReaders)
                {
                    var cat = reader.Catalog;
                    var names = reader.ResolveDict(cat.Get("Names"));
                    if (names is null) continue;
                    var efTree = reader.ResolveDict(names.Get("EmbeddedFiles"));
                    if (efTree is null) continue;

                    // Per-input remap for embedded file objects
                    var efRemap = new Dictionary<int, int>();
                    CollectNameTreeEntries(efTree, reader, embeddedEntries, efRemap, writer);
                }

                // Write catalog
                var catalogDict = new PdfDictionary();
                catalogDict.Set("Type", new PdfName("Catalog"));
                catalogDict.Set("Pages", new PdfIndirectRef(2, 0));

                // Add /Names/EmbeddedFiles if any were found
                if (embeddedEntries.Count > 0)
                {
                    var namesArr = new PdfArray();
                    foreach (var (name, fileSpec) in embeddedEntries)
                    {
                        namesArr.Add(new PdfString(System.Text.Encoding.Latin1.GetBytes(name)));
                        namesArr.Add(fileSpec);
                    }
                    var efTreeDict = new PdfDictionary();
                    efTreeDict.Set("Names", namesArr);
                    var efObjNum = writer.AllocateObjectNumber();
                    writer.WriteIndirectObject(efObjNum, efTreeDict);

                    var namesDict = new PdfDictionary();
                    namesDict.Set("EmbeddedFiles", new PdfIndirectRef(efObjNum, 0));
                    var namesObjNum = writer.AllocateObjectNumber();
                    writer.WriteIndirectObject(namesObjNum, namesDict);

                    catalogDict.Set("Names", new PdfIndirectRef(namesObjNum, 0));
                }

                // Merge AcroForm fields from all input documents. Two top-level fields
                // that share a fully-qualified name denote the same field: with
                // KeepFieldsUnique the later one is renamed via UniqueSuffix (%NUM% →
                // incrementing counter); otherwise their widgets are merged under one field.
                PdfDictionary? acroFormDict = null;

                // When two or more inputs carry an XFA template, their AcroForm fields form a
                // hierarchical tree (top subform node → page-subform nodes → leaf widgets).
                // Re-parent each input's top-level field nodes under one synthetic "root" field,
                // applying the SAME name disambiguation as the /XFA template merge
                // (BuildMergedXfaArray) so FindByName("root[0].eApp[0]") resolves and the node
                // /Kids counts match. The flat per-widget merge (else branch) would both drop the
                // subtree (MergeFieldWidgets keeps only widget keys) and lose the /Parent chain
                // (RemapObject strips /Parent → bare leaf FullNames), so it is used only for
                // non-XFA / single-XFA concatenations.
                var xfaRenames = ComputeXfaTopSubformRenames(inputReaders);
                if (xfaRenames is not null)
                {
                    int rootNum = writer.AllocateObjectNumber();
                    // Group each input's top-level field nodes by their merged name. Same merged
                    // name → one output node whose /Kids are the union of all members' kids
                    // (KeepFieldsUnique=false → a single eApp[0] carrying every source's pages);
                    // differing subtrees rename to eApp1[0]/eApp2[0] and stay separate.
                    var groupOrder = new List<string>();
                    var groupMembers = new Dictionary<string, List<(PdfReader rdr, PdfDictionary dict)>>();
                    for (int i = 0; i < inputReaders.Count; i++)
                    {
                        var reader = inputReaders[i];
                        var acroForm = reader.ResolveDict(reader.Catalog.Get("AcroForm"));
                        var fieldsArr = acroForm is null ? null : reader.Resolve(acroForm.Get("Fields")) as PdfArray;
                        if (fieldsArr is null) continue;
                        var map = i < xfaRenames.Count ? xfaRenames[i] : new Dictionary<string, string>();
                        foreach (var fieldRef in fieldsArr)
                        {
                            var fdict = reader.ResolveDict(fieldRef);
                            if (fdict is null) continue;
                            var t = (reader.Resolve(fdict.Get("T")) as PdfString)?.ToText() ?? "";
                            var (baseName, idx) = SplitFieldNameIndex(t);
                            var newName = (map.TryGetValue(baseName, out var nb) ? nb : baseName) + idx;
                            if (!groupMembers.TryGetValue(newName, out var lst))
                            {
                                lst = new List<(PdfReader, PdfDictionary)>();
                                groupMembers[newName] = lst;
                                groupOrder.Add(newName);
                            }
                            lst.Add((reader, fdict));
                        }
                    }

                    var rootKids = new PdfArray();
                    foreach (var newName in groupOrder)
                    {
                        var members = groupMembers[newName];
                        int nodeNum = writer.AllocateObjectNumber();
                        // /Kids = the union of every member node's child field-nodes, deep-cloned
                        // with their /Parent chain rewired (so BuildFullName / CollectGroupFields
                        // surface this node and compute the root[0].<name> full names).
                        var nodeKids = new PdfArray();
                        foreach (var (rdr, dict) in members)
                        {
                            var memberKids = rdr.Resolve(dict.Get("Kids")) as PdfArray;
                            if (memberKids is null) continue;
                            var remap = new Dictionary<int, int>();
                            foreach (var kid in memberKids)
                            {
                                var kd = rdr.ResolveDict(kid);
                                if (kd is null) continue;
                                var branch = new HashSet<int>();
                                if (kid is PdfIndirectRef kr) branch.Add(kr.ObjectNumber);
                                nodeKids.Add(new PdfIndirectRef(CloneFieldNode(kd, rdr, remap, writer, nodeNum, branch), 0));
                            }
                        }
                        // Build the node dict from the first member's own attributes (minus the
                        // tree/parent/name keys we set explicitly).
                        var (firstRdr, firstDict) = members[0];
                        var nodeDict = new PdfDictionary();
                        var nodeRemap = new Dictionary<int, int>();
                        foreach (var key in firstDict.Keys)
                        {
                            if (key is "Kids" or "Parent" or "T" or "P") continue;
                            var v = firstDict.Get(key);
                            if (v is not null) nodeDict.Set(key, RemapObject(v, firstRdr, nodeRemap, writer));
                        }
                        nodeDict.Set("T", new PdfString(System.Text.Encoding.UTF8.GetBytes(newName)));
                        nodeDict.Set("Parent", new PdfIndirectRef(rootNum, 0));
                        nodeDict.Set("Kids", nodeKids);
                        writer.WriteIndirectObject(nodeNum, nodeDict);
                        rootKids.Add(new PdfIndirectRef(nodeNum, 0));
                    }

                    if (rootKids.Count > 0)
                    {
                        var rootDict = new PdfDictionary();
                        // The synthetic root's /T is written as UTF-16BE with BOM — the
                        // standard PDF text-string form for names introduced by a merge
                        // (ToText decodes it back to "root[0]" for name lookups).
                        var rootT = "root[0]";
                        var rootTBytes = new byte[2 + rootT.Length * 2];
                        rootTBytes[0] = 0xFE; rootTBytes[1] = 0xFF;
                        System.Text.Encoding.BigEndianUnicode.GetBytes(rootT, 0, rootT.Length, rootTBytes, 2);
                        rootDict.Set("T", new PdfString(rootTBytes));
                        rootDict.Set("Kids", rootKids);
                        writer.WriteIndirectObject(rootNum, rootDict);
                        acroFormDict = new PdfDictionary();
                        var rootFields = new PdfArray();
                        rootFields.Add(new PdfIndirectRef(rootNum, 0));
                        acroFormDict.Set("Fields", rootFields);
                    }
                }
                else
                {
                    // Rename mode = KeepFieldsUnique set OR an explicit UniqueSuffix: every input
                    // field is KEPT (duplicate top-level names disambiguated), so the output field
                    // count equals the sum of the inputs'. Deep-clone each top-level field with its
                    // whole /Kids subtree and /Parent chain rewired (CloneTopFieldNode), so nested
                    // fields keep their hierarchical FullNames instead of collapsing to bare leaf
                    // names (RemapObject strips /Parent). Merge mode (the plain default) keeps the
                    // legacy per-widget merge, where colliding fields fold their widgets together.
                    // Renaming is the DEFAULT: only an explicit KeepFieldsUnique=false folds
                    // colliding fields together (even when a UniqueSuffix is set - the suffix only
                    // names duplicates when renaming is in effect). An unset KeepFieldsUnique
                    // renames with the plain occurrence index (textField -> textField1).
                    var renameMode = _keepFieldsUnique != false;
                    var nameCounts = new Dictionary<string, int>();
                    var seenNames = new HashSet<string>();
                    var allFieldRefs = new PdfArray();
                    var outFields = new List<(PdfDictionary dict, int objNum)>();
                    var byName = new Dictionary<string, int>();

                    foreach (var reader in inputReaders)
                    {
                        var cat = reader.Catalog;
                        var acroForm = reader.ResolveDict(cat.Get("AcroForm"));
                        if (acroForm is null) continue;
                        var fieldsArr = reader.Resolve(acroForm.Get("Fields")) as PdfArray;
                        if (fieldsArr is null) continue;

                        var acroRemap = new Dictionary<int, int>();
                        foreach (var fieldRef in fieldsArr)
                        {
                            var srcDict = reader.ResolveDict(fieldRef);
                            if (srcDict is null) continue;
                            var name = (reader.Resolve(srcDict.Get("T")) as PdfString)?.ToText();

                            // A NAMELESS top-level entry (no /T - e.g. a bare Link annotation
                            // mistakenly listed in /Fields) has no name to make unique, so it
                            // folds with the other nameless entries whichever mode is in
                            // effect; only NAMED duplicates are renamed.
                            if (renameMode && name is not null)
                            {
                                string? finalName = name;
                                if (name is not null && !seenNames.Add(name))
                                {
                                    nameCounts.TryGetValue(name, out var n);
                                    nameCounts[name] = ++n;
                                    finalName = name + ApplyUniqueSuffix(_uniqueSuffix, n);
                                    seenNames.Add(finalName);
                                }
                                var num = CloneTopFieldNode(srcDict, reader, acroRemap, writer,
                                    finalName == name ? null : finalName,
                                    (fieldRef as PdfIndirectRef)?.ObjectNumber);
                                allFieldRefs.Add(new PdfIndirectRef(num, 0));
                                continue;
                            }

                            // Merge mode (legacy): pre-allocate, flat-clone, merge colliding widgets.
                            // Top-level fields that resolve to the same fully-qualified name are the
                            // same field and fold their widgets together — including nameless entries
                            // (e.g. bare Link annotations mistakenly listed in /Fields), which all share
                            // the empty name and collapse into a single field rather than each counting.
                            int outNum = writer.AllocateObjectNumber();
                            if (fieldRef is PdfIndirectRef fr) acroRemap[fr.ObjectNumber] = outNum;
                            var cloned = (PdfDictionary)RemapObject(srcDict, reader, acroRemap, writer);
                            var mergeKey = name ?? "";
                            if (byName.TryGetValue(mergeKey, out var existingIdx))
                                MergeFieldWidgets(outFields[existingIdx].dict, cloned);
                            else
                            {
                                byName[mergeKey] = outFields.Count;
                                outFields.Add((cloned, outNum));
                            }
                        }
                    }

                    // Folded entries (every field in merge mode; the nameless ones in rename
                    // mode) are written once their widgets are all in.
                    foreach (var (fld, num) in outFields)
                    {
                        writer.WriteIndirectObject(num, fld);
                        allFieldRefs.Add(new PdfIndirectRef(num, 0));
                    }

                    if (allFieldRefs.Count > 0)
                    {
                        acroFormDict = new PdfDictionary();
                        acroFormDict.Set("Fields", allFieldRefs);
                    }
                }

                // Merge XFA packets (dynamic/static XFA forms). When two or more inputs
                // carry an XFA template, re-parent each input's top-level subform(s) under
                // a single synthetic "root" subform (datasets in parallel), disambiguating
                // colliding names. A pure dynamic XFA form has no AcroForm widget fields, so
                // the AcroForm dict may hold only /XFA (no /Fields).
                var xfaArr = BuildMergedXfaArray(inputReaders);
                if (xfaArr is not null)
                {
                    acroFormDict ??= new PdfDictionary();
                    acroFormDict.Set("XFA", xfaArr);
                }

                if (acroFormDict is not null)
                {
                    var acroObjNum = writer.AllocateObjectNumber();
                    writer.WriteIndirectObject(acroObjNum, acroFormDict);
                    catalogDict.Set("AcroForm", new PdfIndirectRef(acroObjNum, 0));
                }

                // Merge outlines (bookmarks) from all input documents
                MergeOutlines(inputReaders, inputPageCounts, inputOutlineSeeds, catalogDict, writer);
                // Tagged inputs keep their tagging by default - a concatenation of
                // PDF/UA documents must stay PDF/UA. (CopyLogicalStructure predates the
                // remapped merge and no longer gates it.)
                MergeStructTrees(inputReaders, inputOutlineSeeds, structParentBases,
                    structParentNext, catalogDict, writer);

                // Merge /PageLabels: when any source carries page labels, emit a
                // merged number tree so every concatenated page keeps the label it
                // had in its source (or a sequential default), with page indices
                // offset by the preceding inputs' page counts.
                MergePageLabels(inputReaders, inputPageCounts, catalogDict, writer);

                // Preserve the first document's catalog /OpenAction (keep the
                // leading document's open action; the result opens at its start). Remap it
                // through the first input's object map so its destination still resolves to
                // the correct already-written page.
                if (firstReader is not null && firstObjRemap is not null)
                {
                    var openAction = firstReader.Catalog.Get("OpenAction");
                    if (openAction is not null)
                        catalogDict.Set("OpenAction", RemapObject(openAction, firstReader, firstObjRemap, writer));

                    // The concatenation's identity is the leading document's: its XMP
                    // metadata (the document title lives there), viewer preferences
                    // (PDF/UA requires DisplayDocTitle) and language carry over. Without
                    // these a concatenation of PDF/UA inputs loses its compliance for
                    // reasons no input had.
                    foreach (var key in new[] { "Metadata", "ViewerPreferences", "Lang" })
                    {
                        var value = firstReader.Catalog.Get(key);
                        if (value is not null && !catalogDict.ContainsKey(key))
                            catalogDict.Set(key, RemapObject(value, firstReader, firstObjRemap, writer));
                    }
                }

                writer.WriteIndirectObject(1, catalogDict);

                var trailer = new PdfDictionary();
                trailer.Set("Root", new PdfIndirectRef(1, 0));
                // The leading document's /Info travels with the concatenation - the
                // document title lives there, and PDF/UA keeps requiring one.
                if (firstReader is not null && firstObjRemap is not null
                    && firstReader.Trailer?.Get("Info") is { } infoRef)
                {
                    var info = RemapObject(infoRef, firstReader, firstObjRemap, writer);
                    trailer.Set("Info", info);
                }
                // A fresh file gets a fresh /ID pair (PDF/UA requires one; both halves
                // equal, the same shape the PDF/A converter writes).
                var fileId = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
                var idArr = new PdfArray();
                idArr.Add(new PdfString(fileId, isHex: true));
                idArr.Add(new PdfString(fileId, isHex: true));
                trailer.Set("ID", idArr);
                writer.WriteXRefAndTrailer(trailer);
            }

            // Read the result — use FileStream for chunk reading to avoid File.ReadAllBytes
            // 2 GB limit on older runtimes.
            var bytes = ReadAllBytesFromFile(tempPath);
            AppendConversionLog($"Concatenated {inputFiles.Length} inputs into {allPageObjNums.Count} pages.");
            return ApplyPostConcatenateOptions(bytes);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    /// <summary>Post-Concatenate pass that honours the RemoveSignatures /
    /// OwnerPassword / ConvertTo properties on the facade. Real implementations
    /// — each flag triggers a follow-on Document open/operate/save.</summary>
    private byte[] ApplyPostConcatenateOptions(byte[] bytes)
    {
        if (RemoveSignatures)
        {
            using var doc = Document.Open(bytes);
            var form = doc.Form;
            if (form is not null)
            {
                foreach (var field in form.Fields)
                {
                    if (field.Type != Forms.FieldType.Signature) continue;
                    field.Dict.Remove("V");
                }
                bytes = doc.ToArray();
                AppendConversionLog("RemoveSignatures: stripped /V from every signature field.");
            }
        }
        if (!string.IsNullOrEmpty(OwnerPassword))
        {
            using var doc = Document.Open(bytes);
            doc.Encrypt(string.Empty, OwnerPassword,
                permissions: null,
                algorithm: Aspose.Pdf.CryptoAlgorithm.AESx128);
            bytes = doc.ToArray();
            AppendConversionLog($"OwnerPassword set; output encrypted with AES-128.");
        }
        if (_convertToFormat is { } fmt)
        {
            using var doc = Document.Open(bytes);
            doc.Convert(Stream.Null, fmt, ConvertErrorAction.Delete);
            bytes = doc.ToArray();
            AppendConversionLog($"ConvertTo: output converted to {fmt}.");
        }
        return bytes;
    }

    // CompactAfterPageRemoval only detaches the deleted pages; objects the source reached by
    // other routes still travel with the extract, so a one-page cut of a large document keeps
    // paying for the whole original. Under OptimizeSize the extract is additionally reduced to
    // what the surviving pages actually reach. Streams are left alone — this is a pure
    // reachability prune, not a re-encode.
    //
    // The prune runs on the serialized extract rather than on the still-open document: page
    // deletions live in the in-memory page tree, while the reachability walk starts from the
    // trailer as parsed, which still names every original page. Walking that would mark the
    // whole source reachable and prune nothing. Writing first collapses the two views into one.
    private byte[] ApplySizeOptimization(byte[] extracted)
    {
        if (!OptimizeSize) return extracted;
        using var doc = Document.Open(extracted);
        doc.OptimizeResources(new Aspose.Pdf.Optimization.OptimizationOptions
        {
            RemoveUnusedObjects = true,
            RemoveUnusedStreams = false,
        });
        return doc.ToArray();
    }

    /// <summary>
    /// Concatenate multiple PDF files into one output file.
    /// </summary>
    public bool Concatenate(string[] inputFiles, string outputFile)
    {
        _corrupted.Clear();
        List<(byte[], string?)> named;
        try
        {
            named = inputFiles.Select(f => (File.ReadAllBytes(f), (string?)f)).ToList();
        }
        catch (IOException ex)
        {
            // Missing/unreadable inputs surface as a PdfException
            // WRAPPING the IO error — callers (and TryConcatenate's LastException)
            // pattern-match on InnerException being e.g. FileNotFoundException.
            throw new PdfException(ex.Message, ex);
        }
        var inputs = FilterCorruptedInputs(named).ToArray();
        var result = Concatenate(inputs);
        File.WriteAllBytes(outputFile, result);
        return true;
    }

    /// <summary>Parse-probe each input and honour <see cref="CorruptedFileAction"/>.
    /// Returns the inputs that parsed cleanly; the rest are recorded in
    /// <see cref="CorruptedItems"/>. When the action is
    /// <see cref="ConcatenateCorruptedFileAction.StopWithError"/>, the first
    /// unparseable input raises an <see cref="ArgumentException"/>. The recorded
    /// <see cref="CorruptedItem.Index"/> is the position within
    /// <paramref name="inputs"/>.</summary>
    private List<byte[]> FilterCorruptedInputs(IReadOnlyList<(byte[] data, string? name)> inputs)
    {
        var valid = new List<byte[]>();
        for (int i = 0; i < inputs.Count; i++)
        {
            var (data, name) = inputs[i];
            try
            {
                var reader = PdfReader.FromBytes(data);
                // Force trailer/catalog/page-tree resolution so a structurally
                // broken file is detected here rather than mid-merge.
                var pages = reader.ResolveDict(reader.Catalog.Get("Pages"));
                if (pages is null) throw new PdfException("No page tree.");
                valid.Add(data);
            }
            catch (Exception ex)
            {
                if (CorruptedFileAction == ConcatenateCorruptedFileAction.StopWithError)
                    throw new ArgumentException($"Input at index {i} could not be parsed.", ex);
                _corrupted.Add(new CorruptedItem(name, i, ex));
            }
        }
        return valid;
    }

    /// <summary>
    /// Concatenate two PDF files into one output file.
    /// </summary>
    public bool Concatenate(string firstInputFile, string secInputFile, string outputFile)
    {
        return Concatenate(new[] { firstInputFile, secInputFile }, outputFile);
    }

    /// <summary>
    /// Concatenate two PDF files with a blank-page separator inserted between them.
    /// </summary>
    public bool Concatenate(string firstInputFile, string secInputFile, string blankPageFile, string outputFile)
    {
        return Concatenate(new[] { firstInputFile, blankPageFile, secInputFile }, outputFile);
    }

    /// <summary>Concatenate multiple PDF streams into one output stream.</summary>
    public bool Concatenate(Stream[] inputStream, Stream outputStream)
    {
        var inputs = inputStream.Select(ReadStream).ToArray();
        var result = Concatenate(inputs);
        outputStream.Write(result, 0, result.Length);
        // A seekable output is left rewound so callers can read
        // the concatenated bytes back without seeking.
        if (outputStream.CanSeek) outputStream.Position = 0;
        if (CloseConcatenatedStreams)
        {
            foreach (var s in inputStream) s.Dispose();
            outputStream.Dispose();
        }
        return true;
    }

    /// <summary>Concatenate two PDF streams into one output stream.</summary>
    public bool Concatenate(Stream firstInputStream, Stream secInputStream, Stream outputStream)
    {
        var result = Concatenate(ReadStream(firstInputStream), ReadStream(secInputStream));
        outputStream.Write(result, 0, result.Length);
        // A seekable output is left rewound so callers can read
        // the concatenated bytes back without seeking.
        if (outputStream.CanSeek) outputStream.Position = 0;
        if (CloseConcatenatedStreams)
        {
            firstInputStream.Dispose();
            secInputStream.Dispose();
            outputStream.Dispose();
        }
        return true;
    }

    /// <summary>Concatenate two PDF streams with a blank-page separator inserted between them.</summary>
    public bool Concatenate(Stream firstInputStream, Stream secInputStream, Stream blankPageStream, Stream outputStream)
    {
        var result = Concatenate(new[]
        {
            ReadStream(firstInputStream),
            ReadStream(blankPageStream),
            ReadStream(secInputStream),
        });
        outputStream.Write(result, 0, result.Length);
        // A seekable output is left rewound so callers can read
        // the concatenated bytes back without seeking.
        if (outputStream.CanSeek) outputStream.Position = 0;
        if (CloseConcatenatedStreams)
        {
            firstInputStream.Dispose();
            secInputStream.Dispose();
            blankPageStream.Dispose();
            outputStream.Dispose();
        }
        return true;
    }

    /// <summary>Concatenate the pages of <paramref name="src"/> into the
    /// existing <paramref name="dest"/> document. Each source is left
    /// untouched; the target receives all pages in source-order. Honours
    /// the same flag set as the file/stream Concatenate overloads.</summary>
    public bool Concatenate(Document[] src, Document dest)
    {
        if (dest is null) throw new ArgumentNullException(nameof(dest));
        if (src is null) return false;
        foreach (var s in src)
        {
            if (s is null) continue;
            dest.Pages.Add(s.Pages);
            if (CopyLogicalStructure) dest.MergeLogicalStructure(s);
        }
        return true;
    }

    /// <summary>Read a stream fully into a byte array.</summary>
    private static byte[] ReadStream(Stream s)
    {
        // ToArray() returns exactly the logical content (Length bytes from offset 0)
        // regardless of the stream's Position or spare capacity. The previous
        // TryGetBuffer fast-path returned the whole backing array — which includes
        // trailing unused-capacity zero bytes when Capacity > Length — corrupting the
        // PDF (trailing garbage after %%EOF → "root object missing" on re-read).
        if (s is MemoryStream ms) return ms.ToArray();
        if (s.CanSeek) s.Position = 0;
        using var copy = new MemoryStream();
        s.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>
    /// Read all bytes from a file, supporting files larger than 2 GB.
    /// </summary>
    private static byte[] ReadAllBytesFromFile(string path)
    {
        var fileInfo = new FileInfo(path);
        var length = fileInfo.Length;
        if (length > Array.MaxLength)
            throw new InvalidOperationException(
                $"Concatenated PDF is {length / (1024 * 1024)} MB, exceeding the 2 GB byte[] limit.");

        var bytes = new byte[length];
        using var fs = File.OpenRead(path);
        var bytesRead = 0;
        while (bytesRead < bytes.Length)
        {
            var read = fs.Read(bytes, bytesRead, bytes.Length - bytesRead);
            if (read == 0) break;
            bytesRead += read;
        }
        return bytes;
    }
}

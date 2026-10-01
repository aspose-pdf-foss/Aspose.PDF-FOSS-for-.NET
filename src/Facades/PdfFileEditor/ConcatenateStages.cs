using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfFileEditor
{
    /// <summary>Write the concatenated document: every input's pages remapped into one tree, then the merged catalog, forms, outlines, structure and labels.</summary>
    private void WriteConcatenation(Stream output, byte[][] inputFiles, List<int> allPageObjNums)
    {
        var cc = new ConcatenationState();
        OpenWriterAndRemapInputs(cc, output, inputFiles, allPageObjNums);

        WritePageTreeAndEmbeddedFiles(cc, allPageObjNums);

        cc.xfaRenames = ComputeXfaTopSubformRenames(cc.inputReaders);
        if (cc.xfaRenames is not null)
        {
            MergeXfaFieldTrees(cc);
        }
        else
        {
            MergeFlatAcroFormFields(cc);
        }

        WriteCatalogAndTrailer(cc);
    }

    /// <summary>Install the merged XFA packet and AcroForm, merge outlines, structure trees and page labels, carry the first input's open action, then write the catalog, the trailer and a fresh /ID.</summary>
    private void WriteCatalogAndTrailer(ConcatenationState cc)
    {
        cc.xfaArr = BuildMergedXfaArray(cc.inputReaders);
        if (cc.xfaArr is not null)
        {
            cc.acroFormDict ??= new PdfDictionary();
            cc.acroFormDict.Set("XFA", cc.xfaArr);
        }

        if (cc.acroFormDict is not null)
        {
            var acroObjNum = cc.writer.AllocateObjectNumber();
            cc.writer.WriteIndirectObject(acroObjNum, cc.acroFormDict);
            cc.catalogDict.Set("AcroForm", new PdfIndirectRef(acroObjNum, 0));
        }

        // Merge outlines (bookmarks) from all input documents
        MergeOutlines(cc.inputReaders, cc.inputPageCounts, cc.inputOutlineSeeds, cc.catalogDict, cc.writer);
        // Tagged inputs keep their tagging by default - a concatenation of
        // PDF/UA documents must stay PDF/UA. (CopyLogicalStructure predates the
        // remapped merge and no longer gates it.)
        MergeStructTrees(cc.inputReaders, cc.inputOutlineSeeds, cc.structParentBases,
            cc.structParentNext, cc.catalogDict, cc.writer);

        // Merge /PageLabels: when any source carries page labels, emit a
        // merged number tree so every concatenated page keeps the label it
        // had in its source (or a sequential default), with page indices
        // offset by the preceding inputs' page counts.
        MergePageLabels(cc.inputReaders, cc.inputPageCounts, cc.catalogDict, cc.writer);

        // Preserve the first document's catalog /OpenAction (keep the
        // leading document's open action; the result opens at its start). Remap it
        // through the first input's object map so its destination still resolves to
        // the correct already-written page.
        if (cc.firstReader is not null && cc.firstObjRemap is not null)
        {
            var openAction = cc.firstReader.Catalog.Get("OpenAction");
            if (openAction is not null)
                cc.catalogDict.Set("OpenAction", RemapObject(openAction, cc.firstReader, cc.firstObjRemap, cc.writer));

            // The concatenation's identity is the leading document's: its XMP
            // metadata (the document title lives there), viewer preferences
            // (PDF/UA requires DisplayDocTitle) and language carry over. Without
            // these a concatenation of PDF/UA inputs loses its compliance for
            // reasons no input had.
            foreach (var key in new[] { "Metadata", "ViewerPreferences", "Lang" })
            {
                var value = cc.firstReader.Catalog.Get(key);
                if (value is not null && !cc.catalogDict.ContainsKey(key))
                    cc.catalogDict.Set(key, RemapObject(value, cc.firstReader, cc.firstObjRemap, cc.writer));
            }
        }

        cc.writer.WriteIndirectObject(1, cc.catalogDict);

        cc.trailer = new PdfDictionary();
        cc.trailer.Set("Root", new PdfIndirectRef(1, 0));
        // The leading document's /Info travels with the concatenation - the
        // document title lives there, and PDF/UA keeps requiring one.
        if (cc.firstReader is not null && cc.firstObjRemap is not null
            && cc.firstReader.Trailer?.Get("Info") is { } infoRef)
        {
            var info = RemapObject(infoRef, cc.firstReader, cc.firstObjRemap, cc.writer);
            cc.trailer.Set("Info", info);
        }
        cc.fileId = Compat.RandomBytes(16);
        cc.idArr = new PdfArray();
        cc.idArr.Add(new PdfString(cc.fileId, isHex: true));
        cc.idArr.Add(new PdfString(cc.fileId, isHex: true));
        cc.trailer.Set("ID", cc.idArr);
        cc.writer.WriteXRefAndTrailer(cc.trailer);
    }

    /// <summary>The non-XFA merge: every input's top-level fields flattened into one /Fields array, a duplicate name either renamed with the unique suffix or its widgets merged under the first field.</summary>
    private void MergeFlatAcroFormFields(ConcatenationState cc)
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

        foreach (var reader in cc.inputReaders)
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
                    var num = CloneTopFieldNode(srcDict, reader, acroRemap, cc.writer,
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
                int outNum = cc.writer.AllocateObjectNumber();
                if (fieldRef is PdfIndirectRef fr) acroRemap[fr.ObjectNumber] = outNum;
                var cloned = (PdfDictionary)RemapObject(srcDict, reader, acroRemap, cc.writer);
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
            cc.writer.WriteIndirectObject(num, fld);
            allFieldRefs.Add(new PdfIndirectRef(num, 0));
        }

        if (allFieldRefs.Count > 0)
        {
            cc.acroFormDict = new PdfDictionary();
            cc.acroFormDict.Set("Fields", allFieldRefs);
        }
    }

    /// <summary>The multi-XFA merge: every input's top-level field nodes re-parented under one synthetic root field, applying the same renames as the template merge.</summary>
    private void MergeXfaFieldTrees(ConcatenationState cc)
    {
        int rootNum = cc.writer.AllocateObjectNumber();
        // Group each input's top-level field nodes by their merged name. Same merged
        // name → one output node whose /Kids are the union of all members' kids
        // (KeepFieldsUnique=false → a single eApp[0] carrying every source's pages);
        // differing subtrees rename to eApp1[0]/eApp2[0] and stay separate.
        var groupOrder = new List<string>();
        var groupMembers = new Dictionary<string, List<(PdfReader rdr, PdfDictionary dict)>>();
        for (int i = 0; i < cc.inputReaders.Count; i++)
        {
            var reader = cc.inputReaders[i];
            var acroForm = reader.ResolveDict(reader.Catalog.Get("AcroForm"));
            var fieldsArr = acroForm is null ? null : reader.Resolve(acroForm.Get("Fields")) as PdfArray;
            if (fieldsArr is null) continue;
            var map = i < cc.xfaRenames!.Count ? cc.xfaRenames[i] : new Dictionary<string, string>();
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
            int nodeNum = cc.writer.AllocateObjectNumber();
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
                    nodeKids.Add(new PdfIndirectRef(CloneFieldNode(kd, rdr, remap, cc.writer, nodeNum, branch), 0));
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
                if (v is not null) nodeDict.Set(key, RemapObject(v, firstRdr, nodeRemap, cc.writer));
            }
            nodeDict.Set("T", new PdfString(System.Text.Encoding.UTF8.GetBytes(newName)));
            nodeDict.Set("Parent", new PdfIndirectRef(rootNum, 0));
            nodeDict.Set("Kids", nodeKids);
            cc.writer.WriteIndirectObject(nodeNum, nodeDict);
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
            cc.writer.WriteIndirectObject(rootNum, rootDict);
            cc.acroFormDict = new PdfDictionary();
            var rootFields = new PdfArray();
            rootFields.Add(new PdfIndirectRef(rootNum, 0));
            cc.acroFormDict.Set("Fields", rootFields);
        }
    }

    /// <summary>Write the page tree, then start the catalog with every input's embedded files merged into one name tree.</summary>
    private void WritePageTreeAndEmbeddedFiles(ConcatenationState cc, List<int> allPageObjNums)
    {
        cc.kids = new PdfArray();
        foreach (var pObjNum in allPageObjNums)
            cc.kids.Add(new PdfIndirectRef(pObjNum, 0));

        cc.pagesObj = new PdfDictionary();
        cc.pagesObj.Set("Type", new PdfName("Pages"));
        cc.pagesObj.Set("Kids", cc.kids);
        cc.pagesObj.Set("Count", new PdfInteger(allPageObjNums.Count));
        cc.writer.WriteIndirectObject(2, cc.pagesObj);

        cc.embeddedEntries = new List<(string name, PdfObject fileSpec)>();
        foreach (var reader in cc.inputReaders)
        {
            var cat = reader.Catalog;
            var names = reader.ResolveDict(cat.Get("Names"));
            if (names is null) continue;
            var efTree = reader.ResolveDict(names.Get("EmbeddedFiles"));
            if (efTree is null) continue;

            // Per-input remap for embedded file objects
            var efRemap = new Dictionary<int, int>();
            CollectNameTreeEntries(efTree, reader, cc.embeddedEntries, efRemap, cc.writer);
        }

        cc.catalogDict = new PdfDictionary();
        cc.catalogDict.Set("Type", new PdfName("Catalog"));
        cc.catalogDict.Set("Pages", new PdfIndirectRef(2, 0));

        // Add /Names/EmbeddedFiles if any were found
        if (cc.embeddedEntries.Count > 0)
        {
            var namesArr = new PdfArray();
            foreach (var (name, fileSpec) in cc.embeddedEntries)
            {
                namesArr.Add(new PdfString(Compat.Latin1.GetBytes(name)));
                namesArr.Add(fileSpec);
            }
            var efTreeDict = new PdfDictionary();
            efTreeDict.Set("Names", namesArr);
            var efObjNum = cc.writer.AllocateObjectNumber();
            cc.writer.WriteIndirectObject(efObjNum, efTreeDict);

            var namesDict = new PdfDictionary();
            namesDict.Set("EmbeddedFiles", new PdfIndirectRef(efObjNum, 0));
            var namesObjNum = cc.writer.AllocateObjectNumber();
            cc.writer.WriteIndirectObject(namesObjNum, namesDict);

            cc.catalogDict.Set("Names", new PdfIndirectRef(namesObjNum, 0));
        }

        cc.acroFormDict = null;
    }

    /// <summary>Open the writer, then parse every input and write its pages remapped into the output, collecting the readers, page counts, outline seeds and struct-parent bases the later merges need.</summary>
    private void OpenWriterAndRemapInputs(ConcatenationState cc, Stream output, byte[][] inputFiles, List<int> allPageObjNums)
    {
        cc.writer = new PdfWriter(output);
        // Concatenated output is written as PDF 1.7 regardless
        // of the input versions.
        cc.writer.WriteHeader("1.7");

        // Reserve obj 1 (catalog) and obj 2 (pages) — written last once all pages known.
        // All resource/page/content objects are allocated from 3 upwards.
        cc.writer.SetMinObjectNumber(3);

        cc.inputReaders = new List<PdfReader>();
        cc.inputPageCounts = new List<int>();
        cc.firstReader = null;
        cc.firstObjRemap = null;
        cc.inputOutlineSeeds = new List<Dictionary<int, int>>();
        cc.structParentBases = new List<int>();
        cc.structParentNext = 0;
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
                    $"Exception occured during the processing file: {cc.inputReaders.Count + 1}", ex);
            }
            cc.inputReaders.Add(reader);
            var catalog = reader.Catalog;
            var pagesDict = reader.ResolveDict(catalog.Get("Pages"));
            if (pagesDict is null) { cc.inputPageCounts.Add(0); cc.inputOutlineSeeds.Add(new Dictionary<int, int>()); cc.structParentBases.Add(cc.structParentNext); continue; }

            var pages = new List<PdfDictionary>();
            var pageSrcNums = new List<int>();
            CollectPages(pagesDict, reader, pages, pageSrcNums);
            cc.inputPageCounts.Add(pages.Count);

            // This input's slice of the merged parent-tree key space.
            cc.structParentBases.Add(cc.structParentNext);
            if (reader.ResolveDict(catalog.Get("StructTreeRoot")) is { } structRootDict)
                cc.structParentNext += StructParentKeySpan(structRootDict, reader);

            // Per-input object remapping: sourceObjNum → outputObjNum.
            // Objects referenced by multiple pages in the same input are written once
            // and shared via indirect refs — preventing resource duplication bloat.
            var objRemap = new Dictionary<int, int>();
            var pageSrcToOut = new Dictionary<int, int>();
            if (cc.firstReader is null) { cc.firstReader = reader; cc.firstObjRemap = objRemap; }

            for (var pi = 0; pi < pages.Count; pi++)
            {
                var pageDict = pages[pi];
                // Remap the page dict: each source indirect ref is assigned a new
                // output obj num and the referenced object is written once.
                // RemapObject uses writer.AllocateObjectNumber() so it stays in sync
                // with deferred stream promotions inside PdfWriter.WriteDictionary.
                var cloned = (PdfDictionary)RemapObject(pageDict, reader, objRemap, cc.writer);
                cloned.Set("Parent", new PdfIndirectRef(2, 0));
                if (cc.structParentBases[cc.structParentBases.Count - 1] is > 0 and var spBase
                    && cloned.Get("StructParents") is PdfInteger spKey)
                    cloned.Set("StructParents", new PdfInteger(spKey.Value + spBase));

                var pageObjNum = cc.writer.AllocateObjectNumber();
                cc.writer.WriteIndirectObject(pageObjNum, cloned);
                allPageObjNums.Add(pageObjNum);
                // Remember where this SOURCE page landed (for the outline seed
                // below) without touching objRemap itself — the main pass keeps
                // its allocation order.
                if (pageSrcNums[pi] >= 0) pageSrcToOut[pageSrcNums[pi]] = pageObjNum;
            }
            var outlineSeed = new Dictionary<int, int>(objRemap);
            foreach (var kv in pageSrcToOut) outlineSeed[kv.Key] = kv.Value;
            cc.inputOutlineSeeds.Add(outlineSeed);
        }
    }
}

using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfFileEditor
{
    /// <summary>
    /// Mirrors <c>PdfFileEditor.CopyLogicalStructure</c>. Stored only;
    /// Concatenate does not currently copy structure trees.
    /// </summary>
    public bool CopyLogicalStructure { get; set; }

    /// <summary>
    /// Mirrors <c>PdfFileEditor.CopyOutlines</c>. Stored only;
    /// Concatenate does not currently copy outlines.
    /// </summary>
    public bool CopyOutlines { get; set; }

    /// <summary>When true, Concatenate renames colliding form-field names
    /// in the inputs so the output has unique field names. The public default
    /// is false (colliding AcroForm widgets are merged). Backed by a nullable
    /// so the XFA merge can tell an explicit <c>false</c> (keep duplicate top
    /// subforms as occurrences) from the unset default (disambiguate them).</summary>
    public bool KeepFieldsUnique { get => _keepFieldsUnique ?? false; set => _keepFieldsUnique = value; }

    /// <summary>When true, identical outline entries from the inputs are
    /// merged in the output instead of duplicated. Stored only; Concatenate
    /// does not currently dedupe outlines.</summary>
    public bool MergeDuplicateOutlines { get; set; }

    // Keys that should not be remapped (circular refs reset during merge).
    private static readonly HashSet<string> SkipRemapKeys =
        ["Parent", "StructParent", "StructParents"];

    /// <summary>
    /// Remap a source PdfObject into the output stream.
    /// Indirect references are assigned new output object numbers via
    /// <see cref="PdfWriter.AllocateObjectNumber"/> so allocations stay in sync with
    /// deferred stream promotions inside the writer; each source object is written exactly
    /// once per input PDF (deduplicated via <paramref name="objRemap"/>).
    /// Inline dicts/arrays/streams have their contents recursively remapped.
    /// </summary>
    /// <summary>Apply the unique-suffix template to a counter: replace the <c>%NUM%</c>
    /// placeholder with <paramref name="n"/>, or append it when no placeholder is present.</summary>
    private static string ApplyUniqueSuffix(string suffix, int n)
        => suffix.Contains("%NUM%") ? suffix.Replace("%NUM%", n.ToString(System.Globalization.CultureInfo.InvariantCulture))
                                    : suffix + n.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Merge a second same-named field into <paramref name="parent"/> by demoting
    /// each field's own widget into a /Kids entry, so the result is one field with both
    /// visual widgets (matching Concatenate with KeepFieldsUnique = false).</summary>
    private static void MergeFieldWidgets(PdfDictionary parent, PdfDictionary second)
    {
        PdfDictionary ExtractWidget(PdfDictionary src)
        {
            var w = new PdfDictionary();
            foreach (var k in src.Keys)
                if (s_widgetKeys.Contains(k) && src.Get(k) is { } v)
                    w.Set(k, v);
            w.Set("Type", new PdfName("Annot"));
            w.Set("Subtype", new PdfName("Widget"));
            return w;
        }

        var kids = parent.Get("Kids") as PdfArray;
        if (kids is null)
        {
            // First merge: move the parent's own widget into a first kid and strip the
            // widget keys off the (now non-terminal) parent field.
            kids = new PdfArray();
            kids.Add(ExtractWidget(parent));
            foreach (var k in new List<string>(s_widgetKeys))
                parent.Remove(k);
            parent.Set("Kids", kids);
        }
        // A second that is itself a non-terminal field node contributes its widget
        // kids directly — the merged field stays one level deep with every visual
        // widget as a direct kid. A merged field+widget node demotes to one kid.
        if (second.Get("Kids") is PdfArray secondKids)
            foreach (var kid in secondKids)
                kids.Add(kid);
        else
            kids.Add(ExtractWidget(second));
    }

    private static PdfObject RemapObject(PdfObject obj, PdfReader reader,
        Dictionary<int, int> objRemap, PdfWriter writer, int depth = 0)
    {
        if (depth > 200) return obj; // guard against StackOverflow on deeply nested/circular structures
        if (obj is PdfIndirectRef indRef)
        {
            // Already remapped — return existing output ref.
            if (objRemap.TryGetValue(indRef.ObjectNumber, out var existing))
                return new PdfIndirectRef(existing, 0);

            // Allocate output obj num BEFORE recursing to handle circular refs.
            // Use writer.AllocateObjectNumber() so we stay in sync with deferred stream
            // allocations that PdfWriter.WriteDictionary makes internally.
            var outputNum = writer.AllocateObjectNumber();
            objRemap[indRef.ObjectNumber] = outputNum;

            var resolved = reader.Resolve(obj);
            if (resolved is null)
                return new PdfIndirectRef(outputNum, 0); // unresolvable — placeholder

            // Recursively remap the resolved object and write it.
            var remapped = RemapObject(resolved, reader, objRemap, writer, depth + 1);
            writer.WriteIndirectObject(outputNum, remapped);

            return new PdfIndirectRef(outputNum, 0);
        }

        switch (obj)
        {
            case PdfDictionary dict:
            {
                var clone = new PdfDictionary();
                foreach (var key in dict.Keys)
                {
                    if (SkipRemapKeys.Contains(key)) continue;
                    var val = dict.Get(key);
                    if (val is not null)
                        clone.Set(key, RemapObject(val, reader, objRemap, writer, depth + 1));
                }
                return clone;
            }

            case PdfArray arr:
            {
                var clonedArr = new PdfArray();
                foreach (var item in arr)
                    clonedArr.Add(RemapObject(item, reader, objRemap, writer, depth + 1));
                return clonedArr;
            }

            case PdfStream stream:
            {
                var clonedDict = new PdfDictionary();
                foreach (var key in stream.Dict.Keys)
                {
                    if (SkipRemapKeys.Contains(key)) continue;
                    var val = stream.Dict.Get(key);
                    if (val is not null)
                        clonedDict.Set(key, RemapObject(val, reader, objRemap, writer, depth + 1));
                }
                var dataCopy = new byte[stream.RawData.Length];
                Array.Copy(stream.RawData, dataCopy, stream.RawData.Length);
                return new PdfStream(clonedDict, dataCopy);
            }

            default:
                return obj; // Immutable primitive types: PdfName, PdfInteger, PdfReal, PdfBoolean, PdfString, PdfNull
        }
    }

    /// <summary>
    /// Merge outlines (bookmarks) from all input documents into the output catalog.
    /// Collects top-level outline items from each source and writes them as a flat
    /// linked list under a single /Outlines dictionary in the output.
    /// </summary>
    /// <summary>Merge the /PageLabels number trees of all inputs into the concatenated
    /// output. No-op unless at least one source carries page labels; when it does, every
    /// output page gets a label entry — the label it had in its source (page index offset
    /// by the preceding inputs' page counts), or a sequential decimal default for pages
    /// from a source without labels — so the concatenation never leaves a page unlabelled.</summary>
    private static void MergePageLabels(List<PdfReader> readers, List<int> pageCounts,
        PdfDictionary catalogDict, PdfWriter writer)
    {
        var perReaderLabels = new List<PageLabelCollection?>();
        var any = false;
        foreach (var r in readers)
        {
            var tree = r.ResolveDict(r.Catalog.Get("PageLabels"));
            if (tree is not null) { any = true; perReaderLabels.Add(new PageLabelCollection(tree, r)); }
            else perReaderLabels.Add(null);
        }
        if (!any) return;

        var nums = new PdfArray();
        var outIdx = 0;
        for (var i = 0; i < readers.Count; i++)
        {
            var labels = perReaderLabels[i];
            var count = i < pageCounts.Count ? pageCounts[i] : 0;
            for (var local = 0; local < count; local++, outIdx++)
            {
                var active = labels?.GetLabel(local);
                NumberingStyle style;
                string? prefix;
                int st;
                if (active is not null)
                {
                    style = active.Style;
                    prefix = active.Prefix;
                    st = active.Start + (local - active.StartPage);
                }
                else
                {
                    style = NumberingStyle.Decimal;
                    prefix = null;
                    st = outIdx + 1;
                }

                nums.Add(new PdfInteger(outIdx));
                var dict = new PdfDictionary();
                var styleStr = style switch
                {
                    NumberingStyle.Decimal => "D",
                    NumberingStyle.UpperRoman => "R",
                    NumberingStyle.LowerRoman => "r",
                    NumberingStyle.UpperAlpha => "A",
                    NumberingStyle.LowerAlpha => "a",
                    _ => null,
                };
                if (styleStr is not null) dict.Set("S", new PdfName(styleStr));
                if (!string.IsNullOrEmpty(prefix))
                    dict.Set("P", new PdfString(System.Text.Encoding.Latin1.GetBytes(prefix!)));
                if (st != 1) dict.Set("St", new PdfInteger(st));
                nums.Add(dict);
            }
        }
        if (nums.Count == 0) return;

        var treeDict = new PdfDictionary();
        treeDict.Set("Nums", nums);
        var treeObjNum = writer.AllocateObjectNumber();
        writer.WriteIndirectObject(treeObjNum, treeDict);
        catalogDict.Set("PageLabels", new PdfIndirectRef(treeObjNum, 0));
    }

    /// <summary>Merge the logical-structure trees (/StructTreeRoot) of all inputs into a
    /// single tree in the concatenated output. Each input subtree is remapped through the
    /// input's seed map - its object map plus the source-page-to-output-page pairs - so
    /// /Pg page references land on the pages already written into the output, and marked-
    /// content ids stay valid because page content is copied verbatim. The inputs' parent
    /// trees are merged into one, with each input's keys shifted by the bases computed at
    /// page-clone time (where the pages' /StructParents got the same shift).
    ///
    /// The inputs' top elements become children of ONE wrapper Document element; an input
    /// whose own top element is a Document is flattened into it, so concatenating tagged
    /// documents never nests Document inside Document. The wrapper and the hoisted
    /// children carry explicit /P links, which is what structure walkers verify.</summary>
    private static void MergeStructTrees(List<PdfReader> readers,
        List<Dictionary<int, int>> seeds, List<int> structParentBases, int parentTreeNextKey,
        PdfDictionary catalogDict, PdfWriter writer)
    {
        var anyTree = readers.Any(r => r.ResolveDict(r.Catalog.Get("StructTreeRoot")) is not null);
        if (!anyTree) return;

        // Root and wrapper are allocated up front so kids can name their /P and the
        // root its /K before either object is written.
        var rootNum = writer.AllocateObjectNumber();
        var wrapperNum = writer.AllocateObjectNumber();

        var kidRefs = new PdfArray();
        var mergedNums = new List<(int Key, PdfObject Value)>();
        var mergedRoleMap = new PdfDictionary();

        for (var ri = 0; ri < readers.Count; ri++)
        {
            var reader = readers[ri];
            var root = reader.ResolveDict(reader.Catalog.Get("StructTreeRoot"));
            if (root is null) continue;

            var remap = ri < seeds.Count ? new Dictionary<int, int>(seeds[ri]) : new Dictionary<int, int>();
            var spBase = ri < structParentBases.Count ? structParentBases[ri] : 0;

            foreach (var (kidDict, kidSrcNum) in TopStructElements(root, reader))
            {
                // The hoisted child is cloned by hand so its /P can point at the wrapper;
                // pre-seeding its source number makes every deeper /P reference to it
                // resolve to this same clone instead of a duplicate.
                var outNum = writer.AllocateObjectNumber();
                if (kidSrcNum >= 0) remap[kidSrcNum] = outNum;

                var clone = new PdfDictionary();
                foreach (var key in kidDict.Keys)
                {
                    if (key == "P") continue;
                    clone.Set(key, RemapObject(kidDict.Get(key)!, reader, remap, writer));
                }
                clone.Set("P", new PdfIndirectRef(wrapperNum, 0));
                writer.WriteIndirectObject(outNum, clone);
                kidRefs.Add(new PdfIndirectRef(outNum, 0));
            }

            // Parent tree: shift this input's keys into its slice and remap the values
            // (arrays of struct-element refs, or a single ref) through the same map.
            foreach (var (key, value) in NumberTreeEntries(reader.ResolveDict(root.Get("ParentTree")), reader))
                mergedNums.Add((key + spBase, RemapObject(value, reader, remap, writer)));

            // Role map: structure-type aliases merge first-wins; entries are names only.
            if (reader.ResolveDict(root.Get("RoleMap")) is { } rm)
                foreach (var key in rm.Keys)
                    if (!mergedRoleMap.ContainsKey(key) && rm.Get(key) is PdfName alias)
                        mergedRoleMap.Set(key, alias);
        }

        if (kidRefs.Count == 0) return;

        var wrapper = new PdfDictionary();
        wrapper.Set("Type", new PdfName("StructElem"));
        wrapper.Set("S", new PdfName("Document"));
        wrapper.Set("P", new PdfIndirectRef(rootNum, 0));
        wrapper.Set("K", kidRefs);
        writer.WriteIndirectObject(wrapperNum, wrapper);

        var structRoot = new PdfDictionary();
        structRoot.Set("Type", new PdfName("StructTreeRoot"));
        structRoot.Set("K", new PdfIndirectRef(wrapperNum, 0));
        if (mergedNums.Count > 0)
        {
            var nums = new PdfArray();
            foreach (var (key, value) in mergedNums.OrderBy(e => e.Key))
            {
                nums.Add(new PdfInteger(key));
                nums.Add(value);
            }
            var parentTree = new PdfDictionary();
            parentTree.Set("Nums", nums);
            var ptNum = writer.AllocateObjectNumber();
            writer.WriteIndirectObject(ptNum, parentTree);
            structRoot.Set("ParentTree", new PdfIndirectRef(ptNum, 0));
            structRoot.Set("ParentTreeNextKey", new PdfInteger(parentTreeNextKey));
        }
        if (mergedRoleMap.Count > 0) structRoot.Set("RoleMap", mergedRoleMap);
        writer.WriteIndirectObject(rootNum, structRoot);
        catalogDict.Set("StructTreeRoot", new PdfIndirectRef(rootNum, 0));

        // A tagged result must SAY it is tagged.
        var markInfo = new PdfDictionary();
        markInfo.Set("Marked", PdfBoolean.True);
        catalogDict.Set("MarkInfo", markInfo);
    }

    /// <summary>The number of parent-tree keys an input's structure tree occupies: its
    /// /ParentTreeNextKey when the file declares one, else max /Nums key + 1, else 0.</summary>
    private static int StructParentKeySpan(PdfDictionary structRoot, PdfReader reader)
    {
        if (reader.Resolve(structRoot.Get("ParentTreeNextKey")) is PdfInteger next && next.Value > 0)
            return (int)next.Value;
        var max = -1;
        foreach (var (key, _) in NumberTreeEntries(reader.ResolveDict(structRoot.Get("ParentTree")), reader))
            if (key > max) max = key;
        return max + 1;
    }

    /// <summary>The structure-element kids (/K) of a structure dictionary.</summary>
    private static List<PdfDictionary> StructKids(PdfDictionary structDict, PdfReader reader)
    {
        var result = new List<PdfDictionary>();
        var k = reader.Resolve(structDict.Get("K"));
        if (k is PdfArray arr)
        {
            foreach (var item in arr)
                if (reader.ResolveDict(item) is { } d && d.GetName("Type") is null or "StructElem")
                    result.Add(d);
        }
        else if (k is PdfDictionary single && single.GetName("Type") is null or "StructElem")
        {
            result.Add(single);
        }
        return result;
    }

    /// <summary>Clone a structure element (and element children) as an inline dict,
    /// copying primitive attributes and dropping page / marked-content references.</summary>
    private static PdfDictionary CloneStructElemInline(PdfDictionary src, PdfReader reader)
    {
        var clone = new PdfDictionary();
        foreach (var key in src.Keys)
        {
            if (key is "Pg" or "P" or "K") continue;
            var v = src.Get(key);
            if (v is PdfName or PdfString or PdfInteger or PdfReal or PdfBoolean)
                clone.Set(key, v!);
        }
        var children = new PdfArray();
        foreach (var c in StructKids(src, reader))
            children.Add(CloneStructElemInline(c, reader));
        if (children.Count > 0) clone.Set("K", children);
        return clone;
    }

    private static void MergeOutlines(List<PdfReader> readers, List<int> inputPageCounts,
        List<Dictionary<int, int>> inputOutlineSeeds, PdfDictionary catalogDict, PdfWriter writer)
    {
        // Phase 1: collect all top-level outline items with pre-allocated object numbers
        var items = new List<(int objNum, PdfDictionary dict)>();

        int pagesBefore = 0;
        for (int ri = 0; ri < readers.Count; ri++)
        {
            var reader = readers[ri];
            // Page-number offset for destinations in this input: sum of page counts
            // of all preceding inputs. Concatenate output appends inputs in order.
            int offset = pagesBefore;
            pagesBefore += ri < inputPageCounts.Count ? inputPageCounts[ri] : 0;

            var cat = reader.Catalog;
            var outlinesDict = reader.ResolveDict(cat.Get("Outlines"));
            if (outlinesDict is null) continue;

            var firstRef = outlinesDict.Get("First");
            var current = reader.ResolveDict(firstRef);
            // Seeded with the input's already-written objects (pages included), so an
            // outline /Dest referencing a page remaps onto the page in the output tree
            // rather than cloning an orphan copy the destination would dangle on.
            var outlineRemap = ri < inputOutlineSeeds.Count
                ? new Dictionary<int, int>(inputOutlineSeeds[ri])
                : new Dictionary<int, int>();
            while (current is not null)
            {
                var cloned = (PdfDictionary)RemapObject(current, reader, outlineRemap, writer);
                cloned.Remove("Parent");
                cloned.Remove("Prev");
                cloned.Remove("Next");

                // Destinations in /Dest or /A→/D may carry page indices that were
                // valid in the original document. After concatenation, shift them
                // by the total page count of preceding inputs so the bookmark
                // still points to the intended page.
                ShiftDestinationPages(cloned, offset, reader);

                var itemObjNum = writer.AllocateObjectNumber();
                items.Add((itemObjNum, cloned));

                var nextRef = current.Get("Next");
                if (nextRef is null) break;
                current = reader.ResolveDict(nextRef);
            }
        }

        if (items.Count == 0) return;

        // Allocate the /Outlines dict object number first so items can reference it as Parent
        var outlinesObjNum = writer.AllocateObjectNumber();

        // Phase 2: link items and write them
        for (int i = 0; i < items.Count; i++)
        {
            var (objNum, dict) = items[i];
            dict.Set("Parent", new PdfIndirectRef(outlinesObjNum, 0));
            if (i > 0)
                dict.Set("Prev", new PdfIndirectRef(items[i - 1].objNum, 0));
            if (i < items.Count - 1)
                dict.Set("Next", new PdfIndirectRef(items[i + 1].objNum, 0));
            writer.WriteIndirectObject(objNum, dict);
        }

        // Write the /Outlines dictionary
        var outlines = new PdfDictionary();
        outlines.Set("Type", new PdfName("Outlines"));
        outlines.Set("Count", new PdfInteger(items.Count));
        outlines.Set("First", new PdfIndirectRef(items[0].objNum, 0));
        outlines.Set("Last", new PdfIndirectRef(items[^1].objNum, 0));
        writer.WriteIndirectObject(outlinesObjNum, outlines);
        catalogDict.Set("Outlines", new PdfIndirectRef(outlinesObjNum, 0));
    }

    /// <summary>
    /// Walk an outline-item dict and shift every page-index destination by the
    /// given offset. Handles /Dest arrays and /A→/D arrays where the first
    /// element is a PdfInteger (0-based page index). Indirect refs to pages
    /// are left alone — they were already remapped by RemapObject.
    /// </summary>
    private static void ShiftDestinationPages(PdfDictionary item, int offset, PdfReader reader)
    {
        if (offset == 0) return;

        ShiftDest(item.Get("Dest"));
        var action = item.Get("A");
        if (action is PdfDictionary actionDict)
            ShiftDest(actionDict.Get("D"));

        void ShiftDest(PdfObject? destObj)
        {
            if (destObj is not PdfArray arr || arr.Count == 0) return;
            if (arr[0] is PdfInteger pi)
            {
                var newArr = new PdfArray();
                newArr.Add(new PdfInteger(pi.Value + offset));
                for (int k = 1; k < arr.Count; k++)
                    newArr.Add(arr[k]);
                // Mutate in place isn't possible for PdfArray; replace the key.
                if (destObj == item.Get("Dest"))
                    item.Set("Dest", newArr);
                else if (item.Get("A") is PdfDictionary ad)
                    ad.Set("D", newArr);
            }
        }
    }

    // Page attributes that are inheritable down the /Pages tree (PDF spec §7.7.3.4).
    private static readonly string[] InheritablePageKeys = { "Resources", "MediaBox", "CropBox", "Rotate" };

    private static void CollectPages(PdfDictionary node, PdfReader reader, List<PdfDictionary> result,
        List<int>? sourceObjNums = null)
        => CollectPages(node, reader, result, null, sourceObjNums, -1);

    private static void CollectPages(PdfDictionary node, PdfReader reader, List<PdfDictionary> result,
        Dictionary<string, PdfObject>? inherited)
        => CollectPages(node, reader, result, inherited, null, -1);

    private static void CollectPages(PdfDictionary node, PdfReader reader, List<PdfDictionary> result,
        Dictionary<string, PdfObject>? inherited, List<int>? sourceObjNums, int selfObjNum)
    {
        // Accumulate the inheritable attributes declared at this node so descendant pages
        // that omit them (a page whose /Resources lives on an ancestor /Pages node) carry
        // an explicit copy. The output /Pages tree is flat, so without this an inherited
        // /Resources (or /MediaBox) would be silently lost on concatenation.
        Dictionary<string, PdfObject>? effective = inherited;
        foreach (var key in InheritablePageKeys)
        {
            var v = node.Get(key);
            if (v is null) continue;
            effective = effective is null ? new Dictionary<string, PdfObject>() : new Dictionary<string, PdfObject>(effective);
            effective[key] = v;
        }

        var type = node.GetName("Type");
        bool isPage = type == "Page"
            // Some PDFs omit explicit /Type on page nodes; treat as Page if /MediaBox is
            // present (and no /Kids — otherwise it's a Pages node).
            || (type is null && node.ContainsKey("MediaBox") && !node.ContainsKey("Kids"));

        if (isPage)
        {
            result.Add(MaterializeInherited(node, effective));
            // Record the page's SOURCE object number alongside — callers that
            // rewrite references to pages (outline destinations) need to map the
            // source ref onto the page's output object.
            sourceObjNums?.Add(selfObjNum);
            return;
        }

        var kids = reader.Resolve(node.Get("Kids")) as PdfArray;
        if (kids is null) return;
        foreach (var kid in kids)
        {
            var kidDict = reader.ResolveDict(kid);
            if (kidDict is not null)
                CollectPages(kidDict, reader, result, effective, sourceObjNums,
                    kid is PdfIndirectRef kidRef ? kidRef.ObjectNumber : -1);
        }
    }

    /// <summary>Return a page dict that carries the inheritable attributes explicitly: if the
    /// page already declares a key it is kept; otherwise the inherited value is added on a
    /// shallow copy (the source dict is never mutated).</summary>
    private static PdfDictionary MaterializeInherited(PdfDictionary page, Dictionary<string, PdfObject>? inherited)
    {
        if (inherited is null) return page;
        var missing = new List<string>();
        foreach (var key in InheritablePageKeys)
            if (!page.ContainsKey(key) && inherited.ContainsKey(key)) missing.Add(key);
        if (missing.Count == 0) return page;

        var copy = new PdfDictionary();
        foreach (var k in page.Keys)
        {
            var v = page.Get(k);
            if (v is not null) copy.Set(k, v);
        }
        foreach (var k in missing) copy.Set(k, inherited[k]);
        return copy;
    }

    /// <summary>
    /// Collect entries from a PDF name tree (/Names array + /Kids subtrees).
    /// Each entry is (name, cloned fileSpec ref).
    /// </summary>
    private static void CollectNameTreeEntries(PdfDictionary treeNode, PdfReader reader,
        List<(string name, PdfObject fileSpec)> result,
        Dictionary<int, int> remap, PdfWriter writer)
    {
        // /Names array: pairs of [string, value, string, value, .]
        var namesArr = reader.Resolve(treeNode.Get("Names")) as PdfArray;
        if (namesArr is not null)
        {
            for (int i = 0; i + 1 < namesArr.Count; i += 2)
            {
                var nameObj = reader.Resolve(namesArr[i]);
                var valueObj = namesArr[i + 1];

                var name = nameObj switch
                {
                    PdfString s => s.ToText(),
                    _ => $"file_{result.Count}"
                };

                // Clone the value (file spec dict) via remap
                var cloned = RemapObject(valueObj, reader, remap, writer);
                result.Add((name, cloned));
            }
        }

        // /Kids array: subtrees
        var kids = reader.Resolve(treeNode.Get("Kids")) as PdfArray;
        if (kids is not null)
        {
            foreach (var kid in kids)
            {
                var kidDict = reader.ResolveDict(kid);
                if (kidDict is not null)
                    CollectNameTreeEntries(kidDict, reader, result, remap, writer);
            }
        }
    }
}

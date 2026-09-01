using System.Collections;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

public sealed partial class PageCollection
{
    /// <summary>The first free object number in the cross-document import space: past the
    /// destination's existing xref and every already-allocated imported object and page
    /// slot. Imported objects and page-destination slots draw from this one space so they
    /// never collide.</summary>
    private int ImportObjNumBase() =>
        _reader.XRefTable.Entries.Keys.DefaultIfEmpty(0).Max()
        + _importedObjects.Count + _slotCount + 1;

    /// <summary>Record a freshly allocated slot number for the running counters.</summary>
    private void RegisterSlot(int slot)
    {
        _slotCount++;
        if (slot > _maxSlotObjNum) _maxSlotObjNum = slot;
    }

    /// <summary>Reserve (or reuse) the destination-slot object number for a source page,
    /// for slots allocated outside <see cref="RemapObject"/> (i.e. after its stack has
    /// fully drained, so <c>_importedObjects</c> is up to date).</summary>
    private int SlotForSourcePage(PdfReader reader, int sourceObjNum)
    {
        var map = _importPageSlots.GetValue(reader, static _ => new Dictionary<int, int>());
        if (!map.TryGetValue(sourceObjNum, out var slot))
        {
            slot = ImportObjNumBase();
            map[sourceObjNum] = slot;
            RegisterSlot(slot);
        }
        return slot;
    }

    /// <summary>Whether <paramref name="dict"/> is a page-tree object (a leaf /Page or an
    /// intermediate /Pages node) — the targets of GoTo/Link destinations and widget /P
    /// references that page-import must slot rather than deep-clone.</summary>
    private static bool IsPageTreeNode(PdfDictionary dict)
    {
        var type = dict.GetName("Type");
        return type == "Page" || type == "Pages";
    }

    /// <summary>Bind a freshly imported page to the destination slot reserved for its
    /// source page, so a GoTo/Link destination that targets it resolves to this copy.
    /// Cross-document imports only; same-document copies keep the reader's own objects.</summary>
    private void BindImportedPageSlot(Page added, PdfReader sourceReader, int sourceObjNum)
    {
        if (sourceReader == _reader || sourceObjNum <= 0) return;
        var slot = SlotForSourcePage(sourceReader, sourceObjNum);
        // Only the first imported copy of a given source page lives at the shared slot (so
        // destinations targeting that page resolve to it). Further copies keep
        // ImportSlotObjNum = 0 and get a fresh writer-allocated number in RebuildPagesTree.
        if (_claimedSlots.Add(slot))
            added.ImportSlotObjNum = slot;
    }

    /// <summary>The highest reserved page-destination slot object number, exposed so the
    /// save path can reserve the writer's number space above every slot (including
    /// destination-only slots that are referenced but never written).</summary>
    internal int ImportSlotHighWater => _maxSlotObjNum;

    /// <summary>Replace an unresolved-placeholder page's slot with null after a merge
    /// consumed the collection — enumerators and the indexer then report null for it
    /// while <see cref="Count"/> keeps the declared page-tree count.</summary>
    private void PoisonUnresolvedSlot(Page page)
    {
        if (_pages is null) return;
        var i = _pages.IndexOf(page);
        if (i >= 0) _pages[i] = null!;
    }

    /// <summary>
    /// Get or create the clone cache for a given source reader.
    /// This ensures that shared resources (images, fonts) referenced by indirect object number
    /// are only deep-cloned once, even when adding many pages from the same source.
    /// </summary>
    private Dictionary<int, PdfObject> GetOrCreateCloneCache(PdfReader reader)
    {
        return _cloneCache.GetValue(reader, static _ => new Dictionary<int, PdfObject>());
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
            // An encrypted source must be decrypted before any of its raw stream bytes are
            // copied into this document: RemapObject clones PdfStream.RawData verbatim, so
            // ciphertext copied into an unencrypted (or differently-keyed) document would be
            // Flate-decoded into garbage on read — the page's content, fonts and images all
            // come out empty/corrupt. EnsurePlaintextStreams decrypts the source in place and
            // is idempotent (it forgets the decryptor after the first call), so repeating it
            // per imported page costs nothing.
            sourceReader.EnsurePlaintextStreams();
            // Cross-document: remap indirect refs from source to new object numbers
            var remap = GetOrCreateCloneCache(sourceReader);
            clone = (PdfDictionary)RemapObject(dict, sourceReader, remap);
        }

        // Ensure inheritable properties are set directly on the clone.
        // Without a parent chain, inherited MediaBox/CropBox/Rotate/Resources would be lost.
        EnsureInherited(clone, dict, sourceReader);
        return clone;
    }

    /// <summary>Import a foreign object graph (e.g. a Form XObject referenced by a
    /// replayed vector element) into this document, returning the remapped
    /// object. Same-document objects come back unchanged; repeated imports of
    /// the same source object dedupe through the clone cache.</summary>
    internal PdfObject ImportForeignObject(PdfObject obj, PdfReader sourceReader)
    {
        if (sourceReader == _reader) return obj;
        sourceReader.EnsurePlaintextStreams();
        var remap = GetOrCreateCloneCache(sourceReader);
        return RemapObject(obj, sourceReader, remap);
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
        // Allocate object numbers from a counter that starts past the existing xref and
        // every prior import/slot. Slots allocated below share this space via _importPageSlots.
        var nextObjNum = ImportObjNumBase();
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

                    // A reference to another PAGE (a leaf /Page or a /Pages tree node) is a
                    // navigation target — a GoTo/Link destination or a widget's /P — not
                    // content to copy. Cloning it would drag the whole target page's object
                    // graph (its images, fonts) into this import. Point it at a reserved
                    // destination slot instead; the page, if copied, is written there.
                    if (resolved is PdfDictionary pd && IsPageTreeNode(pd))
                    {
                        var slotMap = _importPageSlots.GetValue(sourceReader, static _ => new Dictionary<int, int>());
                        if (!slotMap.TryGetValue(iref.ObjectNumber, out var slot))
                        {
                            slot = nextObjNum++;
                            slotMap[iref.ObjectNumber] = slot;
                            RegisterSlot(slot);
                        }
                        var slotRef = new PdfIndirectRef(slot, 0);
                        remap[iref.ObjectNumber] = slotRef;
                        setter(slotRef);
                        return;
                    }

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

        // Carry the source AcroForm's default resources across the merge: imported
        // widgets' /DA strings name fonts by /DR alias (e.g. /HeBo), so the
        // destination AcroForm needs those /DR entries (and a default /DA) or the
        // aliases dangle — appearance regeneration and DefaultResources readers
        // would come up empty after the merge.
        if (sourceReader != _reader
            && sourceReader.ResolveDict(sourceReader.Catalog.Get("AcroForm")) is { } srcAcro)
        {
            if (acroForm.Get("DA") is null && srcAcro.Get("DA") is PdfString srcDa)
                acroForm.Set("DA", srcDa);
            if (sourceReader.ResolveDict(srcAcro.Get("DR")) is { } srcDr)
            {
                var remapDr = GetOrCreateCloneCache(sourceReader);
                var destDr = _reader.ResolveDict(acroForm.Get("DR"));
                if (destDr is null)
                {
                    acroForm.Set("DR", RemapObject(srcDr, sourceReader, remapDr));
                }
                else if (sourceReader.ResolveDict(srcDr.Get("Font")) is { } srcDrFonts)
                {
                    // Merge font aliases that don't collide with existing ones.
                    var destFonts = _reader.ResolveDict(destDr.Get("Font"));
                    if (destFonts is null)
                    {
                        destFonts = new PdfDictionary();
                        destDr.Set("Font", destFonts);
                    }
                    foreach (var alias in srcDrFonts.Keys)
                    {
                        if (destFonts.ContainsKey(alias)) continue;
                        var entry = srcDrFonts.Get(alias);
                        if (entry is not null)
                            destFonts.Set(alias, RemapObject(entry, sourceReader, remapDr));
                    }
                }
            }
        }

        // Collect existing field names for deduplication
        var existingNames = new HashSet<string>();
        CollectFieldNames(fieldsArr, existingNames);

        var owners = _importedFieldOwners
            ?? new Dictionary<(PdfReader, int), (PdfIndirectRef, PdfDictionary, int, bool)>();

        // A distinct /Annots array for the inserted page, materialised lazily when a
        // same-document widget slot must be re-pointed at a synthesized kid (so the
        // source page's shared /Annots array is left untouched).
        PdfArray? distinctAnnots = null;

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

            // A later widget of a FOREIGN field this import already materialised (the
            // same source /Parent - a multi-widget field repeated on one page or spread
            // over several imported pages) joins that field as a kid: no /T of its
            // own, /Parent to the imported field, linked into its /Kids. Renaming it
            // into a field of its own would grow Form.Count by one per widget.
            // Only a /T-less source widget is a widget OF its parent; a kid carrying its
            // own /T is a named child field and keeps its own identity.
            int? srcParentNum = null;
            if (sourceReader != _reader && srcAnnots is not null && i < srcAnnots.Count
                && sourceReader.ResolveDict(srcAnnots[i]) is { } srcWidget
                && srcWidget.Get("T") is null
                && srcWidget.Get("Parent") is PdfIndirectRef srcParentRef)
                srcParentNum = srcParentRef.ObjectNumber;
            if (srcParentNum is int parentNum
                && owners.TryGetValue((sourceReader, parentNum), out var owner)
                && annotObj is PdfIndirectRef kidRef)
            {
                // The field imported first was a merged field-widget; the moment a
                // second widget arrives it becomes a PURE field whose kids are ALL its
                // widgets (the first one included) - a radio group's Value setter walks
                // /Kids for the option state, so the first widget must be a kid too.
                if (!owner.promoted)
                {
                    owner = PromoteImportedWidgetToField(owner, fieldsArr);
                    owners[(sourceReader, parentNum)] = owner;
                }
                annotDict.Remove("T");
                annotDict.Set("Parent", owner.fieldRef);
                ((PdfArray)owner.fieldDict.Get("Kids")!).Add(kidRef);
                continue;
            }

            // Set the name on the cloned Widget so it becomes a standalone field
            annotDict.Set("T", new PdfString(System.Text.Encoding.Latin1.GetBytes(partialName)));

            // For same-source multi-widget fields: skip if same name already exists
            // (same field appearing on multiple pages within one document)
            if (existingNames.Contains(partialName))
            {
                // Same-document page copy/insert: the inserted page's /Annots still
                // points at the SOURCE field's own widget dict (the shallow page clone
                // shares it). Instead of renaming it into a separate field, give the
                // existing field a fresh DISTINCT kid widget for the new page and link
                // it into /Kids — so Field.Count grows by one and the original field
                // keeps its name (FindByName still resolves it).
                if (sourceReader == _reader
                    && OwnerDocument.FindObjectNumber(annotDict) is int fieldObjNum && fieldObjNum >= 0)
                {
                    var kid = new PdfDictionary();
                    kid.Set("Type", new PdfName("Annot"));
                    kid.Set("Subtype", new PdfName("Widget"));
                    foreach (var vk in new[] { "Rect", "AP", "MK", "DA", "BS", "Border", "F", "Q", "H", "AS", "DV" })
                        if (annotDict.Get(vk) is { } vv) kid.Set(vk, vv);
                    kid.Set("Parent", new PdfIndirectRef(fieldObjNum, 0));

                    var kidObjNum = ImportObjNumBase();
                    _importedObjects.Add((kidObjNum, kid));

                    // Promote the merged leaf to "merged-self + one kid": the field keeps
                    // its own /Rect+/AP on the source page and gains the new page's kid.
                    var kids = _reader.Resolve(annotDict.Get("Kids")) as PdfArray;
                    if (kids is null) { kids = new PdfArray(); annotDict.Set("Kids", kids); }
                    kids.Add(new PdfIndirectRef(kidObjNum, 0));

                    // Re-point the inserted page's /Annots slot at the fresh kid, on a
                    // distinct array so the source page's /Annots is not mutated.
                    distinctAnnots ??= CloneAnnotArray(annots, clonedPageDict);
                    distinctAnnots.ReplaceAt(i, new PdfIndirectRef(kidObjNum, 0));
                    continue;
                }

                // Check if this is a same-source duplicate (same reader = same document)
                // vs a different-source field needing rename. Only a SAME-READER
                // source can claim the multi-widget exemption — a widget imported
                // from another document that happens to carry a /Parent is a
                // FOREIGN field colliding by name, and keeping it unrenamed leaves
                // two fields with one /T in the merged form.
                bool isSameSourceDuplicate = false;
                if (sourceReader == _reader && srcAnnots is not null && i < srcAnnots.Count)
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
            if (srcParentNum is int ownedParentNum && annotObj is PdfIndirectRef fieldRef)
                owners[(sourceReader, ownedParentNum)] = (fieldRef, annotDict, fieldsArr.Count - 1, false);
        }

        // Register imported objects with the reader so they can be resolved in-memory
        foreach (var (objNum, obj) in _importedObjects)
            _reader.RegisterOverlayObject(objNum, obj);

        // Invalidate the cached Form object so it re-reads from the updated AcroForm
        OwnerDocument.InvalidateForm();
    }

    /// <summary>Replace <paramref name="pageDict"/>'s /Annots with a fresh array holding
    /// the same entries, so per-slot edits don't mutate a /Annots array shared with the
    /// source page (same-document shallow page clone). Returns the new array.</summary>
    private static PdfArray CloneAnnotArray(PdfArray source, PdfDictionary pageDict)
    {
        var copy = new PdfArray();
        foreach (var e in source) copy.Add(e);
        pageDict.Set("Annots", copy);
        return copy;
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
}

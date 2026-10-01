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
        var ro = new ObjectRemapState();
        ro.sourceReader = sourceReader;
        ro.remap = remap;
        ro.stack = new Stack<(PdfObject source, Action<PdfObject> setter)>();
        ro.visitedIdentity = new HashSet<object>(ReferenceEqualityComparer.Instance);
        // Allocate object numbers from a counter that starts past the existing xref and
        // every prior import/slot. Slots allocated below share this space via _importPageSlots.
        ro.nextObjNum = ImportObjNumBase();
        ro.root = null;

        RemapSourceObject(ro, obj, result => ro.root = result);
        int safetyLimit = 1_000_000;
        while (ro.stack.Count > 0 && --safetyLimit > 0)
        {
            var (source, setter) = ro.stack.Pop();
            RemapSourceObject(ro, source, setter);
        }

        return ro.root!;
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

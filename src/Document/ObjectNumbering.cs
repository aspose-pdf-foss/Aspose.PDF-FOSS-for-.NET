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
    /// <summary>
    /// Save using incremental update — appends changes without rewriting the original file.
    /// This preserves the original byte structure, which is required for digital signatures.
    /// </summary>
    internal byte[] SaveIncremental(params (int objectNumber, PdfObject obj)[] modifiedObjects)
    {
        using var ms = new MemoryStream();
        SaveIncremental(ms, modifiedObjects);
        return ms.ToArray();
    }

    /// <summary>
    /// Save using incremental update to a stream.
    /// </summary>
    internal void SaveIncremental(Stream output, params (int objectNumber, PdfObject obj)[] modifiedObjects)
    {
        var xref = _reader.XRefTable;
        var trailer = _reader.Trailer;
        var size = (int)trailer.GetInt("Size", 1);
        var originalStartXref = XRefTable.FindStartXref(_data);

        var writer = new IncrementalWriter(output, _data, Math.Max(size, xref.Entries.Keys.DefaultIfEmpty(0).Max() + 1),
            _reader.Decryptor);

        foreach (var (objNum, obj) in modifiedObjects)
        {
            writer.WriteObject(objNum, obj);
        }

        writer.Flush(trailer, originalStartXref);
    }

    /// <summary>Serialize as an incremental update (original bytes verbatim +
    /// appended modified/new objects + a new xref section). Unlike
    /// <see cref="ToArray"/>'s full rewrite, this preserves every original byte,
    /// so an existing digital signature's /ByteRange stays valid. Used when
    /// editing a signed document (e.g. filling a form field).</summary>
    internal byte[] ToArrayIncremental()
    {
        FireBeforePageGenerateEvents();
        using var ms = new MemoryStream();
        SaveIncremental(ms);
        return ms.ToArray();
    }

    private void SaveIncremental(Stream output)
    {
        // The original bytes are written by IncrementalWriter.Flush (which rewinds
        // the stream first), so nothing is pre-written here.

        // Collect all modified objects: new objects + modified catalog/info
        var modified = new List<(int objectNumber, PdfObject obj)>();

        // Add any new objects registered during the session
        foreach (var (objNum, obj) in _newObjects)
            modified.Add((objNum, obj));

        // If metadata was modified, write the updated catalog
        if (_metadataChecked || _taggedContent is not null)
        {
            var catalogRef = _reader.Trailer.Get("Root") as PdfIndirectRef;
            if (catalogRef is not null)
                modified.Add((catalogRef.ObjectNumber, _reader.Catalog));
        }

        // Include objects explicitly marked as dirty (e.g., form field value changes)
        foreach (var (objNum, obj) in _dirtyObjects)
            modified.Add((objNum, obj));

        // Persist page-tree structural changes (page insert/delete) incrementally.
        // Pages.Insert/Delete update only the in-memory page list; without rewriting
        // the /Pages node the appended xref still points at the original /Kids, so a
        // reopened document shows the pre-edit page count. SyncInMemoryPageTree rebuilds
        // the catalog's /Pages dict (/Kids + /Count) to the current order — keeping each
        // surviving page's original indirect reference — and we emit it as a modified
        // object so the incremental update reflects the deletion/insertion.
        if (_pages is not null && _pages.IsModified)
        {
            SyncInMemoryPageTree();
            if (_reader.Catalog.Get("Pages") is PdfIndirectRef pagesRef
                && _reader.ResolveDict(pagesRef) is { } pagesDict)
                modified.Add((pagesRef.ObjectNumber, pagesDict));
        }

        // Use the real incremental writer
        var xref = _reader.XRefTable;
        var trailer = _reader.Trailer;
        var size = (int)trailer.GetInt("Size", 1);
        var originalStartXref = XRefTable.FindStartXref(_data);

        var writer = new IncrementalWriter(output, _data,
            Math.Max(size, xref.Entries.Keys.DefaultIfEmpty(0).Max() + 1),
            _reader.Decryptor);

        foreach (var (objNum, obj) in modified)
            writer.WriteObject(objNum, obj);

        writer.Flush(trailer, originalStartXref);
        output.SetLength(output.Position);
        output.Flush();
    }

    /// <summary>Objects registered under an XML template <c>id</c> attribute
    /// during <see cref="BindXml(string)"/>.</summary>
    internal Dictionary<string, object>? XmlIdObjects { get; set; }

    internal void RegisterXmlObject(string id, object value)
    {
        XmlIdObjects ??= new Dictionary<string, object>();
        XmlIdObjects[id] = value;
    }

    /// <summary>Resolve a PDF object by string id. Returns null when not found.</summary>
    public object? GetObjectById(string id)
    {
        if (id is not null && XmlIdObjects is not null && XmlIdObjects.TryGetValue(id, out var value))
            return value;
        return null;
    }

    /// <summary>
    /// Mark an existing indirect object as dirty so it gets written during incremental save.
    /// </summary>
    internal void MarkDirty(int objectNumber, PdfObject obj)
    {
        _dirtyObjects[objectNumber] = obj;
    }

    /// <summary>
    /// Find the object number for a PdfDictionary by scanning xref entries.
    /// Returns -1 if not found.
    /// </summary>
    internal int FindObjectNumber(PdfDictionary dict)
    {
        foreach (var entry in _reader.XRefTable.Entries.Values)
        {
            var resolved = _reader.Resolve(new PdfIndirectRef(entry.ObjectNumber, 0));
            if (ReferenceEquals(resolved, dict))
                return entry.ObjectNumber;
        }
        return -1;
    }

    internal int AllocateObjectNumber()
    {
        var xref = _reader.XRefTable;
        var max = 0;
        foreach (var entry in xref.Entries.Values)
        {
            if (entry.ObjectNumber > max) max = entry.ObjectNumber;
        }
        // Also consider already-allocated new objects
        foreach (var (objNum, _) in _newObjects)
        {
            if (objNum > max) max = objNum;
        }
        // Also consider imported objects from page merging
        if (_pages is not null)
        {
            foreach (var (objNum, _) in _pages.ImportedObjects)
            {
                if (objNum > max) max = objNum;
            }
            // Cross-document page-import reserves destination-slot object numbers
            // (Page.ImportSlotObjNum, up to ImportSlotHighWater) that are written at
            // their reserved numbers during save — including destination-only slots
            // not yet in ImportedObjects. An allocation here must sit above them, or a
            // page slot overwrites e.g. the /Outlines root that OutlineCollection.Finalize
            // allocates during the same save.
            if (_pages.ImportSlotHighWater > max) max = _pages.ImportSlotHighWater;
        }
        // ...and the XMP packet, whose number is decided during save and appears in neither
        // the source xref nor the pending list.
        if (_reservedMetadataObjNum > max) max = _reservedMetadataObjNum;
        return max + 1;
    }

    /// <summary>
    /// Register a new indirect object to be written on the next save.
    /// </summary>
    internal void AddNewObject(int objNum, PdfObject obj, bool registerOverlay = false)
    {
        _newObjects.Add((objNum, obj));
        // Optionally expose the object to in-memory resolution. The writer enumerates
        // _newObjects (not the overlay), so this never double-writes — it only lets a
        // freshly created indirect object be walked via _reader.Resolve before save.
        // Off by default: most callers (e.g. lazy /StructTreeRoot creation) rely on the
        // object staying unresolvable until saved, so a catalog-backed read view stays
        // null/empty until then. Opt in only where in-memory chaining is required
        // (OutlineBuilder, so a second PdfBookmarkEditor sees just-added bookmarks).
        if (registerOverlay)
            _reader.RegisterOverlayObject(objNum, obj);
    }

    /// <summary>Drop a pending object added by <see cref="AddNewObject"/> so the writer
    /// no longer serialises it. Used when a decision taken after the object was created
    /// strands it — e.g. a replacement font whose embedded program is dropped again
    /// because the caller cleared <see cref="Text.Font.IsEmbedded"/>. The writer
    /// enumerates the pending list unconditionally, so an unreferenced object would
    /// otherwise still be written out.</summary>
    internal void RemoveNewObject(int objNum)
    {
        for (var i = _newObjects.Count - 1; i >= 0; i--)
            if (_newObjects[i].objNum == objNum)
                _newObjects.RemoveAt(i);
    }

    internal PdfDictionary? ResolveExistingInfoDict()
        => _reader.ResolveDict(_reader.Trailer.Get("Info")) ?? _pendingInfoDict;

    /// <summary>True when this document was created from scratch (<c>new Document()</c>)
    /// rather than loaded from existing bytes. A from-scratch document seeds the standard
    /// document-information text entries as empty strings the first time its /Info dict is
    /// materialised (see <see cref="DocumentInfo"/>), so unset fields round-trip through
    /// save/reopen as empty rather than absent.</summary>
    internal bool IsNewDocument => _isNewDocument;
}

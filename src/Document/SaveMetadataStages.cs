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
    /// <summary>The document's XMP packet is written as its own object, reusing the metadata stream the document was opened with when it still fits.</summary>
    private void WriteSaveXmpMetadata(SaveDocumentState sv)
    {
        // After the first save the catalog names the packet that save wrote, so the file's own packet,
        // still in the table, is left out by number on every save that follows.
        if (_replacedMetadataObjNum > 0) sv.infraObjNums.Add(_replacedMetadataObjNum);
        if (sv.xmpBytes is not null)
        {
            // The XMP packet is being rewritten (or cleared). Whichever way, the catalog's
            // existing /Metadata stream is stale: skip it on write and drop it from the
            // object-number ceiling so we don't carry the old packet as a dead object.
            if (_reader.Catalog.Get("Metadata") is PdfIndirectRef oldMetaRef)
            {
                sv.infraObjNums.Add(oldMetaRef.ObjectNumber);
                if (_replacedMetadataObjNum == 0 && sv.xref.Entries.ContainsKey(oldMetaRef.ObjectNumber))
                    _replacedMetadataObjNum = oldMetaRef.ObjectNumber;
            }

            if (sv.xmpBytes.Length > 0)
            {
                var metaDict = new PdfDictionary();
                metaDict.Set("Type", new PdfName("Metadata"));
                metaDict.Set("Subtype", new PdfName("XML"));
                metaDict.Set("Length", new PdfInteger(sv.xmpBytes.Length));
                sv.metaStream = new PdfStream(metaDict, sv.xmpBytes);

                // A packet that CLAIMS a conformance level goes out unfiltered. ISO 19005
                // requires the document's XMP to be readable by a tool that cannot decode
                // PDF streams at all — that is what putting the metadata there is for — so
                // a compressed packet breaks the very rule it is announcing, which is worse
                // than making no claim. Only a claiming packet pays the size: everything
                // else still compresses.
                sv.metaStream.DoNotCompress = ClaimsConformance(sv.xmpBytes);

                // A rewritten packet REPLACES the one the catalog already points at, so it
                // keeps that object's number: allocating a fresh one strands the old number
                // as a hole and grows the document by an object every time the metadata is
                // touched. Only a document with no /Metadata yet needs a new number, and
                // then it is the NEXT FREE one - the ceiling has to clear EVERY planned
                // number, not just the source xref, because page merging reserves numbers
                // for imported objects (annots, fonts) far above the original max and
                // landing the packet on one of those silently orphans it: the xref keeps
                // whichever is written last, and a merged form field vanishes on reload.
                // Reserving the number afterwards is what keeps the allocators off it;
                // padding the ceiling instead left a hole, and a 39-object document came
                // back declaring /Size 142.
                var maxObj = MaxRealObjNum(sv);
                foreach (var (n, _) in _newObjects)
                    if (n > maxObj) maxObj = n;
                if (_pages is not null)
                {
                    foreach (var (n, _) in _pages.ImportedObjects)
                        if (n > maxObj) maxObj = n;
                    if (_pages.ImportSlotHighWater > maxObj) maxObj = _pages.ImportSlotHighWater;
                }
                // NOT the number the catalog already points at, tempting as that is: reusing
                // it costs the document an object but drops a header form off a page that a
                // page-import copied, for a reason not yet understood. The packet takes
                // the next free number until that is explained.
                sv.metaObjNum = maxObj + 1;
                _reservedMetadataObjNum = sv.metaObjNum;
                _reader.Catalog.Set("Metadata", new PdfIndirectRef(sv.metaObjNum, 0));
            }
            else
            {
                // Empty packet (e.g. SetXmpMetadata(Stream.Null)) means "remove the document
                // metadata". Drop the catalog reference and write no stream so the file
                // actually shrinks rather than gaining an empty /Metadata object.
                _reader.Catalog.Remove("Metadata");
            }
        }
        else if (_reader.ResolveStream(_reader.Catalog.Get("Metadata")) is { } kept
                 && !kept.Dict.ContainsKey("Filter") && ClaimsConformance(kept.RawData))
        {
            // The file's own packet goes through as it is, and one that claims conformance stays unfiltered for
            // the same reason a rewritten one does.
            kept.DoNotCompress = true;
        }
    }

    /// <summary>The object number of the file's own XMP packet once a save has replaced it, else 0.</summary>
    private int _replacedMetadataObjNum;

    /// <summary>Every object the cross-reference table still reaches is written through, in object-number order.</summary>
    private void WriteSaveExistingObjects(SaveDocumentState sv)
    {
        var rebuilt = PageTreeRebuildNumbers();
        foreach (var entry in sv.xref.Entries.Values.OrderBy(e => e.ObjectNumber))
        {
            if (!entry.InUse || entry.ObjectNumber == 0) continue;

            // Skip unreachable objects when optimizing
            if (_reachableObjects is not null && !_reachableObjects.Contains(entry.ObjectNumber))
                continue;

            PdfObject? obj;
            try
            {
                obj = _reader.Resolve(new PdfIndirectRef(entry.ObjectNumber, entry.Generation));
            }
            catch (InvalidOperationException)
            {
                // Compressed object whose object stream is unavailable (e.g. corrupt xref or
                // partially-linearized PDFs) — skip gracefully rather than aborting the save.
                continue;
            }
            if (obj is null) continue;

            // Skip the source file's cross-reference infrastructure (see infraObjNums above).
            if (sv.infraObjNums.Contains(entry.ObjectNumber)) continue;
            // A cross-reference stream the table still lists (a linearized file's first-page
            // section) carried over under an encryption would be enciphered, which no reader
            // expects of an xref stream; the writer makes its own.
            if (sv.encryptor is not null && obj is PdfStream { Dict: var streamDict }
                && streamDict.GetName("Type") == "XRef") continue;

            // Written here as well, each would leave a dead copy in the file body ahead of the live one.
            if (rebuilt.Contains(entry.ObjectNumber)) continue;

            // Prefer the live in-memory page dictionary over a (possibly stale,
            // post-ClearCache re-parsed) reader resolution. See livePageDicts above.
            if (sv.livePageDicts.TryGetValue(entry.ObjectNumber, out var liveDict))
                obj = liveDict;

            sv.writer.WriteIndirectObject(entry.ObjectNumber, obj);
        }
    }

    /// <summary>The objects the page tree rebuild writes when the page list changed: the tree's root and the
    /// pages loaded from this document.</summary>
    private HashSet<int> PageTreeRebuildNumbers()
    {
        var numbers = new HashSet<int>();
        if (_pages is null || !_pages.IsModified) return numbers;
        if (_reader.Catalog.Get("Pages") is PdfIndirectRef root) numbers.Add(root.ObjectNumber);
        foreach (var page in _pages)
            if (page.ImportSlotObjNum <= 0 && page.SourceObjectNumber > 0) numbers.Add(page.SourceObjectNumber);
        return numbers;
    }
}

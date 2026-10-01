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
    /// <summary>The trailer the save writes: its root, info, encryption and file identifier, and the linearised copy when one was asked for.</summary>
    private void WriteSaveTrailer(SaveDocumentState sv, Stream output)
    {
        sv.newTrailer = new PdfDictionary();
        CopyTrailerEntry(sv.trailer, sv.newTrailer, "Root");

        // Use new Info ref if we created one, otherwise copy from original
        if (_newInfoObjNum is not null)
        {
            sv.newTrailer.Set("Info", new PdfIndirectRef(_newInfoObjNum.Value, 0));
        }
        else
        {
            CopyTrailerEntry(sv.trailer, sv.newTrailer, "Info");
        }

        if (_encryptor is not null)
        {
            // Write encrypt dictionary as an indirect object (excluded from encryption)
            var encryptObjNum = sv.writer.AllocateObjectNumber();
            sv.writer.ExcludeFromEncryption(encryptObjNum);
            sv.writer.WriteIndirectObject(encryptObjNum, _encryptor.BuildEncryptDict());
            sv.newTrailer.Set("Encrypt", new PdfIndirectRef(encryptObjNum, 0));

            // Set file ID (required for encryption)
            var idArray = new PdfArray();
            idArray.Add(new PdfString(_encryptor.FileId, isHex: true));
            idArray.Add(new PdfString(_encryptor.FileId, isHex: true));
            sv.newTrailer.Set("ID", idArray);
        }
        else if (_forceWriteId && sv.trailer.Get("ID") is null)
        {
            // Generate a file ID (required for PDF/A)
            var fileId = Compat.RandomBytes(16);
            var idArray = new PdfArray();
            idArray.Add(new PdfString(fileId, isHex: true));
            idArray.Add(new PdfString(fileId, isHex: true));
            sv.newTrailer.Set("ID", idArray);
        }
        else
        {
            CopyTrailerEntry(sv.trailer, sv.newTrailer, "ID");
        }

        sv.writer.WriteXRefAndTrailer(sv.newTrailer);

        if (sv.doLinearize)
        {
            var normal = ((MemoryStream)sv.writeTarget).ToArray();
            var linearized = IO.PdfLinearizer.Linearize(normal);
            output.Write(linearized, 0, linearized.Length);
        }
    }

    /// <summary>Every live object of the document, written in object-number order with the page tree and metadata it now carries.</summary>
    private void WriteSaveObjects(SaveDocumentState sv)
    {
        sv.writer.MarkSharedDicts(_reader.Catalog);

        sv.livePageDicts = new Dictionary<int, PdfDictionary>();
        if (_pages is not null)
        {
            foreach (var p in _pages)
                if (p.SourceObjectNumber > 0)
                    sv.livePageDicts[p.SourceObjectNumber] = p.Dict;
        }

        // An in-place image replacement supersedes the original image object but leaves it
        // in the xref, so without a reachability pass the write loop below would emit both
        // the old and the new image and the file would never shrink.
        // Compute reachability once here (only when nothing else already did) so the
        // orphaned original falls out. Runs only when such an edit actually happened.
        if (_reader.MayHaveOrphansOnSave && _reachableObjects is null)
        {
            var reachable = new HashSet<int>();
            CollectReachable(_reader.Trailer, reachable);
            if (_pages is not null)
                foreach (var pending in _pages.PendingAdds)
                    CollectReachable(pending.Dict, reachable);
            if (reachable.Count > 0) _reachableObjects = reachable;
        }

        // Write all existing objects (skipping unreachable ones if optimized).
        // Iterate in ascending object-number order rather than the dictionary's
        // insertion order, which reflects the source file's physical layout and so
        // differs between an original and a re-saved copy. A deterministic write order
        // keeps byte offsets — and therefore the regenerated xref stream — stable across
        // a load/save round-trip.
        WriteSaveExistingObjects(sv);

        // Write any new objects that were added (e.g., deep-cloned resources from imports,
        // new Info dict). These must be written BEFORE RebuildPagesTree so their obj numbers
        // don't collide with writer-allocated numbers.
        foreach (var (objNum, obj) in _newObjects)
        {
            sv.writer.WriteIndirectObject(objNum, obj);
        }

        // Write cross-document imported objects (from page merge)
        if (_pages is not null)
        {
            foreach (var (objNum, obj) in _pages.ImportedObjects)
            {
                // After an OptimizeResources prune, only write imported objects still
                // reachable from the (pruned) pages — otherwise resources dropped from a
                // copied page's /Resources would bloat the file even though nothing uses them.
                if (_reachableObjects is not null && !_reachableObjects.Contains(objNum)) continue;
                sv.writer.WriteIndirectObject(objNum, obj);
            }
        }

        // Handle page additions/deletions by rebuilding the Pages tree
        if (_pages is not null && _pages.IsModified)
        {
            // Rebuild /Pages with updated /Kids and /Count
            RebuildPagesTree(sv.writer);
        }

        // Write XMP metadata stream
        if (sv.metaStream is not null && sv.metaObjNum >= 0)
        {
            // A document that says its metadata is NOT encrypted has to mean it:
            // the packet is written in the clear while everything else goes
            // through the cipher. The /Encrypt dictionary states the choice and
            // the key derivation records it; this is the part a reader sees.
            if (_encryptor is not null && !_encryptor.EncryptMetadata)
                sv.writer.ExcludeFromEncryption(sv.metaObjNum);
            sv.writer.WriteIndirectObject(sv.metaObjNum, sv.metaStream);
        }
    }

    /// <summary>The file header, the XMP packet and the structure fixups the save records before its objects.</summary>
    private void WriteSaveMetadata(SaveDocumentState sv)
    {
        if (sv.pdfA1Target) sv.writer.RecompressFlateStreams = true;

        sv.infraObjNums = sv.xref.InfrastructureObjectNumbers();
        foreach (var linInfra in _reader.LinearizationInfraObjects) sv.infraObjNums.Add(linInfra);

        sv.writer.WriteHeader(_versionOverride ?? PdfVersion ?? "1.4");

        sv.metaObjNum = -1;
        sv.metaStream = null;
        sv.xmpBytes = _rawXmpOverride
            ?? ((_metadata is not null && _metadata.IsDirty) ? _metadata.ToXmpBytes() : null);
        WriteSaveXmpMetadata(sv);

        // Pre-advance the writer's object counter past ALL known object numbers
        // (existing xref, _newObjects, and metaObjNum) so that any indirect objects
        // promoted from inline PdfStream values during serialization get fresh numbers
        // that don't collide with anything already planned.
        {
            var maxKnown = MaxRealObjNum(sv);
            foreach (var (objNum, _) in _newObjects)
                if (objNum > maxKnown) maxKnown = objNum;
            if (sv.metaObjNum > maxKnown) maxKnown = sv.metaObjNum;
            sv.writer.SetMinObjectNumber(maxKnown + 1);
        }

        // A structure element authored against a not-yet-numbered page (fresh
        // document) still needs its /Pg before the catalog below is serialized
        // inline. Decide those pages' object numbers now — RebuildPagesTree
        // writes a page at SourceObjectNumber when one is assigned.
        if (PendingStructPgFixups.Count > 0)
        {
            if (_pages is not null && _pages.ImportSlotHighWater > 0)
                sv.writer.ReserveObjectNumber(_pages.ImportSlotHighWater);
            foreach (var (elem, page) in PendingStructPgFixups)
            {
                if (page.ImportSlotObjNum <= 0 && page.SourceObjectNumber <= 0)
                    page.SourceObjectNumber = sv.writer.AllocateObjectNumber();
                var pgNum = page.ImportSlotObjNum > 0 ? page.ImportSlotObjNum : page.SourceObjectNumber;
                elem.Set("Pg", new PdfIndirectRef(pgNum, 0));
            }
            PendingStructPgFixups.Clear();
        }
    }

    /// <summary>The XMP identification namespaces a packet uses to announce that the file
    /// conforms to a standard — PDF/A, PDF/UA and PDF/X respectively.</summary>
    private static readonly string[] ConformanceIdPrefixes = ["pdfaid:", "pdfuaid:", "pdfxid:"];

    /// <summary>Whether an XMP packet announces conformance to a standard, which is what
    /// obliges the writer to emit it unfiltered.</summary>
    private static bool ClaimsConformance(byte[] xmpBytes)
    {
        // The packet is ASCII-compatible XML (UTF-8 by the XMP specification), and the
        // identification properties are written as element or attribute names, so a byte
        // scan for the namespace prefix answers this without parsing a packet whose shape
        // varies by producer.
        var text = Encoding.UTF8.GetString(xmpBytes);
        foreach (var prefix in ConformanceIdPrefixes)
            if (text.Contains(prefix, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>The writer the save streams through: its target, its encryption, and whether the file linearises or keeps its object streams.</summary>
    private void OpenSaveWriter(SaveDocumentState sv, Stream output)
    {
        if (_prunedFontsThisSave && _reachableObjects is null)
        {
            var reachable = new HashSet<int>();
            CollectReachable(_reader.Trailer, reachable);
            if (reachable.Count > 0) _reachableObjects = reachable;
            _prunedFontsThisSave = false;
        }

        // Sync AcroForm field values into the XFA datasets for static XFA forms,
        // so XFA[field] reflects values set through the typed field API.
        _form?.SyncAcroFormToXfa();

        // Auto-finalize structure tree if one was created
        _structureTreeBuilder?.BuildParentTree();
        _structureTreeBuilder = null;

        // Flush the tagged-content tree and accessibility metadata so a
        // document authored via TaggedContent saves as PDF/UA-1 compliant.
        EnsureTaggedPdfMetadata();

        // Auto-finalize outline builder if one was created
        _outlineBuilder?.Build();
        _outlineBuilder = null;

        // Finalize outline collection if items were added/removed via the DOM API
        if (_outlines is not null && _outlines.IsDirty)
            _outlines.Finalize(this);


        // Auto-finalize page labels if created
        _pageLabelBuilder?.Build();
        _pageLabelBuilder = null;

        // Persist label changes made through the doc.PageLabels collection API.
        if (_pageLabels is { IsDirty: true })
            _pageLabels.Serialize(this);

        // EmbedStandardFonts opts the page fonts — including the Standard-14 faces a
        // viewer would otherwise substitute — into a real embedded program, resolving a
        // system face (Helvetica→Arial, Courier→Courier New, …) per the existing embed
        // pass. Without this the property is inert and a re-read still reports the
        // Standard-14 fonts as non-embedded.
        if (EmbedStandardFonts)
            EmbedNonEmbeddedFonts(includeStandard14: true);

        // If the source was encrypted but we're saving without re-encryption, materialize
        // every stream's raw bytes in plaintext now. The writer's pass-through path would
        // otherwise copy ciphertext into a trailer with no /Encrypt, leaving a PDF whose
        // streams can't be /FlateDecode-decoded. Mirrors PDF 32000-2 § 7.6.1.
        if (_encryptor is null && _reader.IsDecrypted)
        {
            _reader.EnsurePlaintextStreams();
        }

        sv.doLinearize = (_linearize || IsLinearized) && !OptimizeSize && _encryptor is null;
        sv.writeTarget = sv.doLinearize ? new MemoryStream() : output;
        sv.writer = new PdfWriter(sv.writeTarget, _encryptor);

        sv.xref = _reader.XRefTable;
        sv.trailer = _reader.Trailer;

        sv.hasCompressedObjects = sv.xref.Entries.Values.Any(e => e.IsCompressed);
        sv.pdfA1Target = _lastConvertedFormat
            is Aspose.Pdf.PdfFormat.PDF_A_1A or Aspose.Pdf.PdfFormat.PDF_A_1B;
        if ((sv.hasCompressedObjects || _packObjectsOnSave) && _encryptor is null && !sv.doLinearize && !sv.pdfA1Target)
        {
            sv.writer.UseObjectStreams = true;
            // The information dictionary is written direct, never packed: its
            // ModDate moves on every save, and inside a compressed object stream a
            // changed digit changes the deflated length, so re-saving the same
            // document a second later would move the file's size (a size-stability
            // test re-saves twenty times and allows one byte). Direct, the date is
            // fixed-width and the size does not move.
            var infoObjNum = _newInfoObjNum ?? (sv.trailer.Get("Info") as PdfIndirectRef)?.ObjectNumber;
            if (infoObjNum is { } keepDirect) sv.writer.KeepOutOfObjectStreams(keepDirect);
        }
    }

    /// <summary>The document information the save stamps: producer, creator, dates, page content and per-page fixups.</summary>
    private void StampSaveInfo(SaveDocumentState sv)
    {
        sv.producerExplicit = Info.ProducerAssigned ||
            (HasMetadata && GetOrCreateMetadata().ProducerExplicitlySet);
        if (SavesAsPdf20)
        {
            // The dates first: a from-scratch document's /Info is materialised by the
            // first write, which seeds its standard entries, and the transfer then
            // takes everything the dictionary holds.
            StampModDate();
            MoveInfoIntoXmp();
            StampPdf20Identity(sv);
        }
        else
        {
            StampInfoIdentity(sv);
            StampModDate();
        }

        FlushFormContentEdits();

        // Apply page-level paragraphs, headers, and footers before saving
        ApplyPageContent();

        // What a table-of-contents page drew for an authored tagged document joins the
        // structure tree now that it is drawn and its link annotations exist.
        if (_taggedContent is not null)
            Tagged.TaggedTocWiring.Wire(this, ((Tagged.ITaggedContent)_taggedContent).RootElement);

        // Persist document-level /AA additional actions to the catalog. Save()/
        // ToArray() do this via FireBeforePageGenerateEvents, but the Save(string)
        // → Save(Stream) funnel bypasses that, so /AA would otherwise be dropped.
        // WriteToCatalog is idempotent, so a redundant call is harmless.
        _actions?.WriteToCatalog();

        // Sync any attached text fragments modified after AppendText
        foreach (var page in Pages)
        {
            page.FlushBgColorRectangles();
            page.FlushUnderlineRectangles();
            // Removal LAST: its own write-back is what folds a prepended or appended
            // decoration stream into the page's single operator list, which is the list a
            // caller reads after save.
            page.FlushUnderlineRemovals();
            page.FlushStrikeOutRectangles();
            page.FlushHyperlinkAnnotations();
            // PageInformationAnnotation prints the output file name + date; generate its
            // appearance here, when the save file name is known.
            if (_pendingSaveFileName is not null)
                page.FlushPageInfoAnnotations(_pendingSaveFileName, DateTime.Today);
            page.SyncAttachedFragments();
            page.SyncAttachedParagraphs();
            if (page.PruneUnusedFontsOnSave) { PruneUnusedFontsForPage(page); _prunedFontsThisSave = true; }
            page.FlushPendingLayers();
        }

        // Shrink any freshly embedded Type0 font programs (TextFragment.Text
        // replacements that fell back to a system font) to sparse GID-preserving
        // subsets of the glyphs actually shown — the full multi-MB program would
        // otherwise ship in every saved file.
        Text.Type0FontEmbedder.SparseSubsetEmbeddedFontsForSave();
    }

    /// <summary>/ModDate, stamped with the time of this save unless the caller pinned it.</summary>
    private void StampModDate()
    {
        // Update /ModDate to current time on every save (PDF convention) — but
        // only when the caller did not pin a specific value via Info.ModDate=…
        // before saving. For example, a caller sets ModDate
        // to a fixed historical date and expects the saved doc to round-trip
        // that exact value. StampModDateOnSave bypasses the public setter so
        // the "explicitly set" flag stays clean across repeated saves.
        if (!Info.ModDateExplicitlySet)
        {
            Info.StampModDateOnSave(DateTime.UtcNow);
            // PDF 32000-2 § 14.3.3 — /Info /ModDate and xmp:ModifyDate are
            // equivalent representations of one value. When the XMP packet is
            // being re-serialised this save anyway (dirty — e.g. after a PDF/A
            // conversion), an existing xmp:ModifyDate must follow the freshly
            // stamped /ModDate or the two dates disagree in the saved file.
            // A clean packet is left byte-identical: touching it on every save
            // would break size-stability across repeated saves.
            if (HasMetadata)
            {
                var xmpMeta = GetOrCreateXmpMetadata();
                if (xmpMeta.IsDirty && !string.IsNullOrEmpty(xmpMeta.Get("xmp:ModifyDate")))
                    xmpMeta.Set("xmp:ModifyDate", FormatXmpDate(Info.ModDate, Info.ModTimeZone));
            }
        }
    }

    /// <summary>The library's identity on a 1.x save: the producer in /Info (and in an XMP pdf:Producer the packet
    /// already has), the default creator in /Info.</summary>
    private void StampInfoIdentity(SaveDocumentState sv)
    {
        if (!sv.producerExplicit)
        {
            // No-op when the stamp is already in place: rewriting an identical
            // Producer would dirty the /Info object on every save and make an
            // iterated load/save loop creep by a few bytes per round (a
            // size-stability regression the field-move test measures).
            if (Info.Producer != BuildVersionInfo.ProducerString)
                Info.StampProducer(BuildVersionInfo.ProducerString);
            if (HasMetadata)
            {
                var xmpMeta = GetOrCreateMetadata();
                var xmpProducer = xmpMeta.Get("pdf:Producer");
                if (!string.IsNullOrEmpty(xmpProducer) && xmpProducer != BuildVersionInfo.ProducerString)
                    xmpMeta.SetStamped("pdf:Producer", BuildVersionInfo.ProducerString);
            }
        }

        // Stamp the default Creator when the caller left it unset — the FOSS
        // library's own identity, per the same self-identification rule the
        // Producer follows (user directive 2026-08-30; corpus tests pinning a
        // company brand here park as intentional divergence).
        if (string.IsNullOrEmpty(Info.Creator))
            Info.Creator = BuildVersionInfo.CreatorString;
    }

    /// <summary>The library's identity on a 2.0 save, which keeps no documentary /Info entry: the producer and the
    /// default creator go into the packet under the names the 2.0 transfer gives them.</summary>
    private void StampPdf20Identity(SaveDocumentState sv)
    {
        var xmp = GetOrCreateXmpMetadata();
        if (!sv.producerExplicit && xmp.Get("xmp:Producer") != BuildVersionInfo.ProducerString)
            xmp.SetStamped("xmp:Producer", BuildVersionInfo.ProducerString);
        if (string.IsNullOrEmpty(xmp.Get("xmp:Creator")))
            xmp.SetStamped("xmp:Creator", BuildVersionInfo.CreatorString);
    }

    /// <summary>The document's own pre-save pass: signature changes, pending images, XMP sync and the version the file declares.</summary>
    private void PrepareSaveDocument()
    {
        if (HandleSignatureChange && Form.SignaturesExist)
            throw new PdfException(
                "The document contains a digital signature and HandleSignatureChange is enabled; saving would invalidate the signature.");

        // Validate deferred XML image file references. Read the LIVE File values:
        // a template image resolved through GetObjectById may have been re-pointed
        // at a real file (or handed a stream) between BindXml and Save.
        if (PendingXmlImages is { Count: > 0 } pendingImages)
        {
            foreach (var img in pendingImages)
            {
                if (img.ImageStream is not null || string.IsNullOrEmpty(img.File)) continue;
                if (Image.IsRemote(img.File))
                {
                    // A remote reference is validated by fetching it (the result is
                    // cached, so layout does not request it again). An unreachable host
                    // is a TRANSPORT failure, not a missing file, and is reported as
                    // itself so it reads as the outage it is.
                    if (img.RemoteFailure() is { } transport)
                        throw new PdfException($"Image could not be fetched: {img.File}", transport);
                    continue;
                }
                if (!File.Exists(img.File))
                    throw new FileNotFoundException($"Image file not found: {img.File}", img.File);
            }
        }

        // PDF 2.0 (ISO 32000-2 § 14.3.2) deprecates the documentary /Info: every
        // entry the dictionary carries moves into the XMP packet under the xmp
        // prefix, under its own name (a custom key included), and only the two
        // dates stay behind - mirrored into the packet as well. The version the
        // SAVE will stamp decides - a conversion sets the override without touching
        // the loaded header. The packet's own dc:/pdf: properties are the file's
        // and are left as they are.
        // (The transfer itself runs with the date stamps, in StampSaveInfo.)
        if (SavesAsPdf20) return;

        // PDF 32000-2 § 14.3.3 — when both /Info and XMP /Metadata exist they
        // are equivalent representations. Pull XMP-side values into /Info for
        // keys that XMP carries (non-empty) but /Info does not.
        SyncXmpIntoInfo();
    }

    /// <summary>Whether this save writes a PDF 2.0 file: the version a conversion set, else the loaded header.</summary>
    private bool SavesAsPdf20 => (_versionOverride ?? PdfVersion) == "2.0";

    /// <summary>The 2.0 transfer: each /Info entry becomes <c>xmp:&lt;key&gt;</c>, and leaves /Info unless it is a date.</summary>
    private void MoveInfoIntoXmp()
    {
        foreach (var key in Info.Keys.ToList())
        {
            var value = Info[key];
            if (!string.IsNullOrEmpty(value)) GetOrCreateXmpMetadata().SetStamped("xmp:" + key, value);
            if (key is not ("CreationDate" or "ModDate")) Info.Remove(key);
        }
    }

    /// <summary>The highest object number the document itself owns - the infrastructure
    /// objects the save allocates are not part of the document's own numbering.</summary>
    private static int MaxRealObjNum(SaveDocumentState sv) =>
        sv.xref.Entries.Keys.Where(k => !sv.infraObjNums.Contains(k)).DefaultIfEmpty(0).Max();
}

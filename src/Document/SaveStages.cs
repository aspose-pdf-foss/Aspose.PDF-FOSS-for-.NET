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

        if (sv.encryptor is not null)
        {
            // Write encrypt dictionary as an indirect object (excluded from encryption)
            var encryptObjNum = sv.writer.AllocateObjectNumber();
            sv.writer.ExcludeFromEncryption(encryptObjNum);
            sv.writer.WriteIndirectObject(encryptObjNum, sv.encryptor.BuildEncryptDict());
            sv.newTrailer.Set("Encrypt", new PdfIndirectRef(encryptObjNum, 0));

            // Set file ID (required for encryption)
            var idArray = new PdfArray();
            idArray.Add(new PdfString(sv.encryptor.FileId, isHex: true));
            idArray.Add(new PdfString(sv.encryptor.FileId, isHex: true));
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
            var linearized = IO.PdfLinearizer.Linearize(normal, sv.linearizedObjectStreams);
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
            {
                NumberPageOfAnnotations(sv, p);
                if (p.SourceObjectNumber > 0)
                    sv.livePageDicts[p.SourceObjectNumber] = p.Dict;
            }
        }

        // An in-place image replacement supersedes the original image object but leaves it
        // in the xref, so without a reachability pass the write loop below would emit both
        // the old and the new image and the file would never shrink.
        // Compute reachability once here (only when nothing else already did) so the
        // orphaned original falls out. Runs only when such an edit actually happened.
        var reachableOfThisSave = _reader.MayHaveOrphansOnSave && _reachableObjects is null;
        if (reachableOfThisSave)
        {
            var reachable = CollectSessionReachable();
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
            if (sv.encryptor is not null && !sv.encryptor.EncryptMetadata)
                sv.writer.ExcludeFromEncryption(sv.metaObjNum);
            sv.writer.WriteIndirectObject(sv.metaObjNum, sv.metaStream);
        }

        // The set is this save's: a later save, after more edits (a page copied in), walks again.
        if (reachableOfThisSave) _reachableObjects = null;
    }

    /// <summary>
    /// A widget placed on a page holds the page's dictionary itself as its /P. The writer is told which object
    /// the page is written as, so the entry goes out as a reference to the page in the tree rather than as a
    /// copy of the page. A new page that carries annotations is numbered now, as nothing else has yet.
    /// </summary>
    private void NumberPageOfAnnotations(SaveDocumentState sv, Page page)
    {
        if (page.ImportSlotObjNum <= 0 && page.SourceObjectNumber <= 0)
        {
            if (!page.Dict.ContainsKey("Annots")) return;
            sv.writer.ReserveObjectNumber(_pages!.ImportSlotHighWater);
            page.SourceObjectNumber = sv.writer.AllocateObjectNumber();
            sv.destinationNumberedPages.Add(page);
        }
        sv.writer.WrittenAs(page.Dict, page.ImportSlotObjNum > 0 ? page.ImportSlotObjNum : page.SourceObjectNumber);
    }

    /// <summary>The file header, the XMP packet and the structure fixups the save records before its objects.</summary>
    private void WriteSaveMetadata(SaveDocumentState sv)
    {
        sv.infraObjNums = sv.xref.InfrastructureObjectNumbers();
        foreach (var linInfra in _reader.LinearizationInfraObjects) sv.infraObjNums.Add(linInfra);
        // The source's /Encrypt dictionary is no object of the document: the save writes the
        // encryption it keeps afresh, or none.
        if (sv.trailer.Get("Encrypt") is PdfIndirectRef sourceEncrypt) sv.infraObjNums.Add(sourceEncrypt.ObjectNumber);

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
        var maxKnown = MaxRealObjNum(sv);
        foreach (var (objNum, _) in _newObjects)
            if (objNum > maxKnown) maxKnown = objNum;
        if (sv.metaObjNum > maxKnown) maxKnown = sv.metaObjNum;
        // Objects copied in by a page import are written at their own numbers after the document's: a stream
        // held inline by one of them (a widget's appearance) is promoted past them, not onto one of them.
        if (_pages is not null)
        {
            foreach (var (objNum, _) in _pages.ImportedObjects)
                if (objNum > maxKnown) maxKnown = objNum;
            if (_pages.ImportSlotHighWater > maxKnown) maxKnown = _pages.ImportSlotHighWater;
        }
        sv.writer.SetMinObjectNumber(maxKnown + 1);

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

        ReferenceDestinationPages(sv);
    }

    /// <summary>
    /// Local destinations get their page as a reference to the page object for this save,
    /// numbered as <see cref="RebuildPagesTree"/> will write the page. A destination whose
    /// page has left the document keeps its index.
    /// </summary>
    private void ReferenceDestinationPages(SaveDocumentState sv)
    {
        var pending = new List<(PdfArray Destination, Page Page)>(PendingDestinationPages);
        CollectIndexedDestinations(pending);
        if (pending.Count == 0) return;

        var pages = new HashSet<Page>(Pages);
        PendingDestinationPages.RemoveAll(p => !pages.Contains(p.Page));
        pending.RemoveAll(p => !pages.Contains(p.Page));
        if (_pages is not null && _pages.ImportSlotHighWater > 0)
            sv.writer.ReserveObjectNumber(_pages.ImportSlotHighWater);
        foreach (var (destination, page) in pending)
        {
            if (destination.Count == 0 || destination[0] is not PdfInteger index) continue;
            if (page.ImportSlotObjNum <= 0 && page.SourceObjectNumber <= 0)
            {
                page.SourceObjectNumber = sv.writer.AllocateObjectNumber();
                sv.destinationNumberedPages.Add(page);
            }
            var objNum = page.ImportSlotObjNum > 0 ? page.ImportSlotObjNum : page.SourceObjectNumber;
            destination.ReplaceAt(0, new PdfIndirectRef(objNum, 0));
            sv.destinationIndexes.Add((destination, index));
        }
    }

    /// <summary>The indices <see cref="ReferenceDestinationPages"/> took out, and the page
    /// numbers it gave, put back.</summary>
    private static void RestoreDestinationIndexes(SaveDocumentState sv)
    {
        foreach (var (destination, index) in sv.destinationIndexes)
            destination.ReplaceAt(0, index);
        sv.destinationIndexes.Clear();
        foreach (var page in sv.destinationNumberedPages)
            page.SourceObjectNumber = -1;
        sv.destinationNumberedPages.Clear();
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
        return ConformanceIdPrefixes.Any(prefix => text.Contains(prefix, StringComparison.Ordinal));
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

        FinishObjectModelForSave();

        // One rule for what the file holds encrypted: everything the save writes goes through
        // its encryptor, so every stream must hold plaintext first. The source's streams are
        // deciphered now whether the save encrypts or not: passed through, ciphertext would be
        // encrypted a second time or land under a trailer with no /Encrypt.
        sv.encryptor = SaveEncryptor();
        if (_reader.IsDecrypted)
        {
            _reader.EnsurePlaintextStreams();
        }

        sv.doLinearize = (_linearize || IsLinearized) && !OptimizeSize && sv.encryptor is null;
        sv.writeTarget = sv.doLinearize ? new MemoryStream() : output;
        sv.writer = new PdfWriter(sv.writeTarget, sv.encryptor);

        sv.xref = _reader.XRefTable;
        sv.trailer = _reader.Trailer;

        sv.hasCompressedObjects = sv.xref.Entries.Values.Any(e => e.IsCompressed);
        sv.pdfA1Target = _lastConvertedFormat
            is Aspose.Pdf.PdfFormat.PDF_A_1A or Aspose.Pdf.PdfFormat.PDF_A_1B;
        var packObjects = (sv.hasCompressedObjects || _packObjectsOnSave) && sv.encryptor is null && !sv.pdfA1Target
            && !_savesBelowObjectStreams;
        // A linearized save packs in the linearizer, which re-lays-out a plain body.
        sv.linearizedObjectStreams = packObjects && sv.doLinearize;
        if (packObjects && !sv.doLinearize)
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

    /// <summary>What every save completes in the object model before it writes, a whole one or an
    /// update: the form's XFA data, the structure tree, outlines, page labels and requested font programs.</summary>
    private void FinishObjectModelForSave()
    {
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

        // Fonts read from the document that IsEmbedded / IsSubset asked to carry a program get it now.
        EmbedRequestedFonts();

        // EmbedStandardFonts opts the page fonts — including the Standard-14 faces a
        // viewer would otherwise substitute — into a real embedded program, resolving a
        // system face (Helvetica→Arial, Courier→Courier New, …) per the existing embed
        // pass. Without this the property is inert and a re-read still reports the
        // Standard-14 fonts as non-embedded.
        if (EmbedStandardFonts)
        {
            EmbedNonEmbeddedFonts(includeStandard14: true);
            // A composite font that pass embedded holds the whole system face (a CJK
            // face runs to many MB); it is cut to the glyphs the pages show.
            Optimization.FontSubsetter.SubsetNewlyEmbeddedCidFonts(_reader, ResolvePendingStream);
        }
    }

    /// <summary>The encryption the source was opened with, kept for every save once the first
    /// one has deciphered the source's streams (the reader no longer holds the key then).</summary>
    private PdfEncryptor? _carriedEncryptor;

    /// <summary>The encryption a save writes: the one Encrypt armed, else the one the document
    /// was opened with until Decrypt (or a conversion that forbids encryption) drops it, so a
    /// plain save of an encrypted document stays encrypted, with its passwords and permissions.</summary>
    private PdfEncryptor? SaveEncryptor()
    {
        if (_encryptor is not null) return _encryptor;
        if (!_reader.Trailer.ContainsKey("Encrypt")) return null;
        if (_carriedEncryptor is null && _reader.Decryptor is { } source
            && _reader.ResolveDict(_reader.Trailer.Get("Encrypt")) is { } encryptDict)
        {
            var fileId = _reader.Trailer.Get("ID") is PdfArray { Count: > 0 } id && id[0] is PdfString first
                ? first.Value
                : [];
            _carriedEncryptor = PdfEncryptor.CreateFromSource(source, encryptDict, fileId);
        }
        return _carriedEncryptor;
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
        CarrySaveStampsIntoXmp(sv.producerExplicit);

        LayOutContentForSave();
    }

    /// <summary>What every save draws before it writes, a whole one or an update: form content
    /// edits, paragraphs, headers and footers, decorations and pending layers - on every page, or
    /// on the pages <paramref name="laysOut"/> picks.</summary>
    private void LayOutContentForSave(Func<Page, bool>? laysOut = null)
    {
        FlushFormContentEdits();

        // Apply page-level paragraphs, headers, and footers before saving
        ApplyPageContent(laysOut);

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
        foreach (var page in Pages.Where(page => laysOut is null || laysOut(page)))
        {
            page.FlushTextDecorations();
            page.FlushHyperlinkAnnotations();
            // PageInformationAnnotation prints the output file name + date; generate its
            // appearance here, when the save file name is known.
            if (_pendingSaveFileName is not null)
                page.FlushPageInfoAnnotations(_pendingSaveFileName, DateTime.Today);
            page.SyncAttachedFragments();
            page.SyncAttachedParagraphs();
            page.OrderAppendedTextByZIndex();
            if (page.PruneUnusedFontsOnSave) { PruneUnusedFontsForPage(page); _prunedFontsThisSave = true; }
            page.FlushPendingLayers();
            page.JoinGeneratedContentStreams();
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
        // the "explicitly set" flag stays clean across repeated saves. A date the
        // caller wrote into the packet instead is the one value both sides state.
        if (!Info.ModDateExplicitlySet)
            Info.StampModDateOnSave(XmpModifyDateSetByCaller() ?? DateTime.UtcNow);
    }

    /// <summary>The library's identity on a 1.x save: the producer and the default creator in /Info (the packet
    /// follows in <see cref="CarrySaveStampsIntoXmp"/>).</summary>
    private void StampInfoIdentity(SaveDocumentState sv)
    {
        // No-op when the stamp is already in place: rewriting an identical
        // Producer would dirty the /Info object on every save and make an
        // iterated load/save loop creep by a few bytes per round (a
        // size-stability regression the field-move test measures).
        if (!sv.producerExplicit && Info.Producer != BuildVersionInfo.ProducerString)
            Info.StampProducer(BuildVersionInfo.ProducerString);

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
        RefuseSignatureChange();

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

    /// <summary>The strict signature handling the caller opted into: a signed document is not saved.</summary>
    private void RefuseSignatureChange()
    {
        if (HandleSignatureChange && Form.SignaturesExist)
            throw new PdfException(
                "The document contains a digital signature and HandleSignatureChange is enabled; saving would invalidate the signature.");
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

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
    private void SyncXmpIntoInfo()
    {
        if (!HasMetadata) return;
        var meta = GetOrCreateXmpMetadata();

        // ISO 16684-1 / PDF 32000-2 § 14.3.3 mapping between /Info entries and
        // their XMP property equivalents. Sync only when the XMP side carries
        // a non-empty value and the /Info side is missing-or-empty.
        SyncIfMissing(meta, "dc:title", "Title");
        SyncIfMissing(meta, "dc:description", "Subject");
        SyncIfMissing(meta, "dc:creator", "Author");
        SyncIfMissing(meta, "pdf:Keywords", "Keywords");
        SyncIfMissing(meta, "xmp:CreatorTool", "Creator");
        SyncIfMissing(meta, "pdf:Producer", "Producer");
    }

    private void SyncIfMissing(XmpMetadata meta, string xmpKey, string infoKey)
    {
        if (!string.IsNullOrEmpty(Info[infoKey])) return;
        var v = meta.Get(xmpKey);
        if (string.IsNullOrEmpty(v)) return;
        Info[infoKey] = v;
    }

    /// <summary>Format an /Info date (DateTime + timezone offset) as an ISO 8601
    /// XMP date string (e.g. <c>2026-06-20T12:34:56+03:00</c>) that round-trips
    /// through <see cref="Aspose.Pdf.Xmp.XmpValue.ToDateTime"/>.</summary>
    private static string FormatXmpDate(DateTime value, TimeSpan offset)
    {
        // PDF dates in the wild carry corrupt timezone offsets; DateTimeOffset only
        // accepts whole-minute offsets within ±14h. Sanitize instead of letting a
        // junk /CreationDate fail the whole save or PDF/A conversion: sub-minute
        // precision is truncated, an out-of-range offset falls back to UTC.
        if (offset.Ticks % TimeSpan.TicksPerMinute != 0)
            offset = new TimeSpan(offset.Ticks - offset.Ticks % TimeSpan.TicksPerMinute);
        if (offset > TimeSpan.FromHours(14) || offset < TimeSpan.FromHours(-14))
            offset = TimeSpan.Zero;
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), offset)
            .ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Save the document incrementally to a stream: keeps original bytes + appends only
    /// modified/new objects. Uses IncrementalWriter for a true incremental update that
    /// preserves the original byte structure and keeps the file size small.
    /// </summary>
    /// <summary>When the document was authored or edited through
    /// <see cref="TaggedContent"/>, flush the in-memory structure tree
    /// and stamp the accessibility metadata PDF/UA-1 requires: the title
    /// shown in the window bar (<c>/ViewerPreferences /DisplayDocTitle</c>),
    /// an XMP packet carrying the UA identifier plus <c>dc:title</c>, and
    /// a file <c>/ID</c>.</summary>
    private void EnsureTaggedPdfMetadata()
    {
        if (_taggedContent is null) return;

        // Tagged-TOC navigation consistency (PDF/UA-1): a TOC element whose
        // linked header carries text conflicting with the TOC page title
        // must fail the save (HeaderElementTextConflictException) before any
        // structure is flushed.
        static void ValidateTocLinks(Aspose.Pdf.LogicalStructure.Element el)
        {
            if (el is Aspose.Pdf.LogicalStructure.TOCElement toc)
                toc.ValidateLinkedTitleOnSave();
            foreach (var child in el.ChildElements)
                ValidateTocLinks(child);
        }
        ValidateTocLinks(((Tagged.ITaggedContent)_taggedContent).RootElement);

        // Link the authored structure tree into /Catalog (sets /MarkInfo,
        // /StructTreeRoot — element dicts are already in their parents' /K).
        ((Tagged.ITaggedContent)_taggedContent).Save();

        // Render the authored structure (headers/paragraphs/tables/figures/
        // lists/links) onto pages when the document was built purely through
        // TaggedContent and has no page content yet. A from-scratch tagged
        // document otherwise saves with a blank canvas. Blank pages the caller
        // pre-added don't suppress the render: the flow is laid out
        // starting ON them (measured on the tagged-TOC document — one blank
        // Pages.Add() becomes the first TOC page, not a leading empty page).
        if (_isNewDocument && (Pages.Count == 0 || AllPagesAreContentless()))
        {
            var root = ((Tagged.ITaggedContent)_taggedContent).RootElement;
            Tagged.TaggedContentRenderer.TryRender(this, root);
            // Structure content that can't be laid out as text (e.g. a table) still
            // needs a page so the authored document doesn't save with zero pages.
            if (Pages.Count == 0 && root.ChildElements.Count > 0)
                Pages.Add();
            // An authored tagged document that never called SetTitle still saves
            // as a titled, PDF/UA-identified file — it is stamped with the
            // default title "Tagged PDF" (and with it dc:title, pdfuaid:part and
            // /DisplayDocTitle below), so Validate(PDF_UA_1) of the authored
            // output succeeds.
            if (root.ChildElements.Count > 0 && string.IsNullOrEmpty(Info.Title))
                Info.Title = "Tagged PDF";
        }

        bool AllPagesAreContentless()
        {
            foreach (var page in Pages)
            {
                try
                {
                    var contents = Reader.Resolve(page.Dict.Get("Contents"));
                    if (contents is null) continue;
                    if (contents is Core.PdfStream s && s.RawData.Length == 0) continue;
                    if (contents is Core.PdfArray arr)
                    {
                        var empty = true;
                        foreach (var item in arr)
                            if (Reader.ResolveStream(item) is { } cs && cs.RawData.Length > 0)
                            {
                                empty = false;
                                break;
                            }
                        if (empty) continue;
                    }
                    return false;
                }
                catch
                {
                    return false;
                }
            }
            return true;
        }

        var title = Info.Title;
        if (!string.IsNullOrEmpty(title))
        {
            DisplayDocTitle = true;
            var meta = GetOrCreateMetadata();
            if (string.IsNullOrEmpty(meta.Get("dc:title"))) meta.Set("dc:title", title);
            if (string.IsNullOrEmpty(meta.Get("pdf:Producer"))) meta.SetStamped("pdf:Producer", BuildVersionInfo.ProducerString);
            if (string.IsNullOrEmpty(meta.Get("pdfuaid:part"))) meta.Set("pdfuaid:part", "1");
        }

        if (_reader.Trailer.Get("ID") is null)
        {
            var fileId = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
            var idArray = new PdfArray();
            idArray.Add(new PdfString(fileId, isHex: true));
            idArray.Add(new PdfString(fileId, isHex: true));
            _reader.Trailer.Set("ID", idArray);
            _forceWriteId = true;
        }
    }

    /// <summary>Write the document's XMP /Metadata packet to <paramref name="stream"/>.</summary>
    public void GetXmpMetadata(Stream stream)
    {
        if (stream is null) return;
        byte[]? bytes = _rawXmpOverride;
        if (bytes is null)
        {
            var metaStream = _reader.ResolveStream(_reader.Catalog.Get("Metadata"));
            if (metaStream is not null) bytes = _reader.DecodeStream(metaStream);
        }
        if (bytes is { Length: > 0 }) stream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Replace the document's XMP /Metadata packet from <paramref name="stream"/>.
    /// The full stream content (from its start) is stored and written verbatim on save.</summary>
    public void SetXmpMetadata(Stream stream)
    {
        if (stream is null) return;
        if (stream.CanSeek) stream.Position = 0;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        _rawXmpOverride = ms.ToArray();
    }
}

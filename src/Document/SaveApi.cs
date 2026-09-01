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
    /// Save the document in-place: writes back to the file the document
    /// was opened from (<see cref="FileName"/>), or performs an incremental
    /// save to the original source stream when the document was opened
    /// from a writable <see cref="FileStream"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the
    /// document was loaded from a byte buffer or read-only stream with
    /// no associated file path.</exception>
    public void Save()
    {
        FireBeforePageGenerateEvents();

        // DOM-signed bytes (SignatureField.Sign) persist verbatim: they already
        // carry every incremental signature revision, and the in-memory field
        // additions were replicated into those revisions by the signer.
        if (PendingSignedBytes is not null && _sourceStream is { CanWrite: true, CanSeek: true })
        {
            _sourceStream.Seek(0, SeekOrigin.Begin);
            _sourceStream.Write(PendingSignedBytes, 0, PendingSignedBytes.Length);
            _sourceStream.SetLength(PendingSignedBytes.Length);
            _sourceStream.Flush();
            return;
        }

        if (_sourceStream is not null && _sourceStream.CanWrite)
        {
            SaveIncremental(_sourceStream);
            return;
        }

        if (!string.IsNullOrEmpty(FileName))
        {
            using var fs = File.Create(FileName);
            Save(fs);
            return;
        }

        // A document created in memory (no source path/stream) still supports
        // a bare Save(): it finalizes the document in place —
        // paragraph processing, stamp materialisation into page annotations —
        // without a destination. Serialize into a scratch buffer to run the
        // same pipeline; the bytes are discarded, the object-model effects stay.
        using var scratch = new MemoryStream();
        Save(scratch);
    }

    private void FireBeforePageGenerateEvents()
    {
        // Real wiring for Page.OnBeforePageGenerate: walk the page tree
        // and fire each page's event subscribers (if any) before the
        // writer serialises them. Mutations to page dicts inside the
        // handler are picked up by the subsequent save.
        for (var i = 1; i <= PageCount; i++)
            Pages[i].RaiseBeforePageGenerate();
        _actions?.WriteToCatalog();
        EmitBackgroundOnPages();
    }

    private void EmitBackgroundOnPages()
    {
        if (Background is null) return;
        var bg = Background;
        for (var i = 1; i <= PageCount; i++)
        {
            var page = Pages[i];
            var media = page.MediaBox;
            // Build a "q rg 0 0 W H re f Q" prologue and prepend it to the
            // page content stream. Real — saved PDF carries the fill rect.
            var prologue = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "q {0:0.######} {1:0.######} {2:0.######} rg 0 0 {3:0.######} {4:0.######} re f Q\n",
                bg.R / 255.0, bg.G / 255.0, bg.B / 255.0,
                media.Width, media.Height);
            page.PrependContentStream(System.Text.Encoding.ASCII.GetBytes(prologue));
        }
    }

    /// <summary>
    /// Save the document to a file.
    /// </summary>
    public void Save(string outputFileName)
    {
        // Expose the output file name so save-time appearance generation that needs it
        // (e.g. PageInformationAnnotation, which prints the file name + date) can read it.
        _pendingSaveFileName = System.IO.Path.GetFileName(outputFileName);
        try
        {
            using (var fs = new FileStream(outputFileName, FileMode.Create, FileAccess.Write))
            {
                Save(fs);
            }
        }
        finally { _pendingSaveFileName = null; }
        // Update internal state so HasIncrementalUpdate() reflects the saved content
        var fileInfo = new FileInfo(outputFileName);
        if (fileInfo.Length <= int.MaxValue)
            _data = File.ReadAllBytes(outputFileName);
    }

    /// <summary>
    /// Save the document to a file in the specified format. Only
    /// <see cref="SaveFormat.Pdf"/> and <see cref="SaveFormat.Html"/> are supported.
    /// </summary>
    public void Save(string outputFileName, SaveFormat format)
    {
        switch (format)
        {
            case SaveFormat.Pdf:
                Save(outputFileName);
                break;
            case SaveFormat.Html:
                Save(outputFileName, new HtmlSaveOptions());
                break;
            case SaveFormat.Markdown:
                System.IO.File.WriteAllText(outputFileName,
                    new Converters.PdfToMarkdownConverter().SaveAsMarkdown(this), System.Text.Encoding.UTF8);
                break;
            case SaveFormat.Xml:
                System.IO.File.WriteAllBytes(outputFileName, Tagged.TaggedXmlExporter.Export(this));
                break;
            case SaveFormat.Svg:
                Save(outputFileName, new SvgSaveOptions());
                break;
            default:
                throw new System.NotSupportedException($"Only SaveFormat.Pdf, SaveFormat.Html, SaveFormat.Markdown, SaveFormat.Xml and SaveFormat.Svg are supported; requested {format}.");
        }
    }

    /// <summary>
    /// Save the document to a stream in the specified format. Only
    /// <see cref="SaveFormat.Pdf"/> and <see cref="SaveFormat.Html"/> are supported.
    /// </summary>
    public void Save(Stream outputStream, SaveFormat format)
    {
        switch (format)
        {
            case SaveFormat.Pdf:
                Save(outputStream);
                break;
            case SaveFormat.Html:
                // Saving HTML to a stream needs resource-saving strategies that only
                // the HtmlSaveOptions overload can carry, so the format-only overload
                // cannot service an HTML stream target.
                throw new System.InvalidOperationException(
                    "To save a document to a html stream it's necessary to supply several additional conversion " +
                    "parameters. Please use overload of this method that uses instance of HtmlSaveOptions as second parameter.");
            case SaveFormat.Markdown:
                var mdBytes = System.Text.Encoding.UTF8.GetBytes(
                    new Converters.PdfToMarkdownConverter().SaveAsMarkdown(this));
                outputStream.Write(mdBytes, 0, mdBytes.Length);
                break;
            case SaveFormat.Xml:
                var xmlBytes = Tagged.TaggedXmlExporter.Export(this);
                outputStream.Write(xmlBytes, 0, xmlBytes.Length);
                break;
            default:
                throw new System.NotSupportedException($"Only SaveFormat.Pdf, SaveFormat.Html, SaveFormat.Markdown and SaveFormat.Xml are supported; requested {format}.");
        }
    }

    /// <summary>
    /// Save the document as HTML to a stream using the specified options.
    /// Delegates to <see cref="Converters.PdfToHtmlConverter"/>.
    /// </summary>
    public void Save(Stream output, HtmlSaveOptions options)
    {
        options.CheckParametersConsistensyAndThrowExceptionOtherwise(targetIsStream: true);
        WriteHtmlCore(output, options);
    }

    /// <summary>
    /// Save the document as HTML to a file using the specified options.
    /// </summary>
    public void Save(string path, HtmlSaveOptions options)
    {
        options.CheckParametersConsistensyAndThrowExceptionOtherwise(targetIsStream: false);
        ValidateExplicitPageList(options.ExplicitListOfSavedPages);
        if (options.SplitIntoPages)
        {
            SaveHtmlSplitToFiles(path, options);
            return;
        }
        // A non-split save with a page-markup strategy hands the whole document's
        // bytes to the caller, which writes them itself (the supplied path may be a
        // directory that File.Create could not open). It only falls back to writing
        // the path when the strategy cancels.
        if (options.CustomHtmlSavingStrategy is { } htmlStrategy)
        {
            using var ms = new MemoryStream();
            WriteHtmlCore(ms, options);
            ms.Position = 0;
            var info = new HtmlSaveOptions.HtmlPageMarkupSavingInfo
            {
                ContentStream = ms,
                HtmlHostPageNumber = 1,
                PdfHostPageNumber = 1,
                SupposedFileName = Path.GetFileName(path),
                CustomProcessingCancelled = false,
            };
            htmlStrategy(info);
            if (info.CustomProcessingCancelled)
            {
                ms.Position = 0;
                using var cfs = File.Create(path);
                ms.CopyTo(cfs);
            }
            return;
        }
        // A plain file save externalises each page's vector graphics and the
        // stylesheet into a "<stem>_files" sidecar folder.
        // The whole-page raster background mode externalises
        // one flattened PNG per page instead of SVGs and images — but only when
        // the caller did NOT ask for everything in one file: EmbedAllIntoHtml +
        // PNG-background produces a single self-contained HTML with the page
        // rasters inlined as base64.
        if (options.RasterImagesSavingMode == HtmlSaveOptions.RasterImagesSavingModes.AsEmbeddedPartsOfPngPageBackground
            && options.PartsEmbeddingMode == HtmlSaveOptions.PartsEmbeddingModes.EmbedAllIntoHtml)
        {
            using var fs = File.Create(path);
            WriteHtmlCore(fs, options);
            return;
        }
        SaveHtmlWithExternalResources(path, options);
    }

    /// <summary>
    /// Save to a stream using general SaveOptions (stub type from Aspose.Pdf namespace).
    /// For stub SaveOptions subclasses without real implementations, saves as PDF.
    /// </summary>
    public void Save(Stream outputStream, SaveOptions options)
    {
        if (options is SvgSaveOptions svgStreamOpts)
        {
            var svg = new Devices.SvgDevice { SaveOptions = svgStreamOpts }.Process(Pages[1]);
            var bytes = System.Text.Encoding.UTF8.GetBytes(svg);
            outputStream.Write(bytes, 0, bytes.Length);
            return;
        }
        if (options is PdfToMarkdown.MarkdownSaveOptions md)
        {
            // A file-backed stream still anchors the image resources directory next to
            // the markdown file it writes; a pure in-memory stream has no anchor and
            // keeps the references without saving the files.
            var outDir = (outputStream as FileStream)?.Name is string fsName
                ? System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(fsName))
                : null;
            var markdown = PdfToMarkdown.MarkdownRenderer.Render(this, md, outDir);
            var bytes = System.Text.Encoding.UTF8.GetBytes(markdown);
            outputStream.Write(bytes, 0, bytes.Length);
            return;
        }
        ApplyPdfSaveOptions(options);
        Save(outputStream);
    }

    /// <summary>
    /// Save to a file using general SaveOptions (stub type from Aspose.Pdf namespace).
    /// </summary>
    public void Save(string outputFileName, SaveOptions options)
    {
        if (options is SvgSaveOptions svgOpts)
        {
            // Render real SVG markup instead of writing a PDF to the .svg path (the
            // historic no-op that made round-trip tests pass by a compensating
            // load-side bug). Page 1 goes to the requested path; a multi-page
            // document additionally writes page N to "<stem>_N.svg" next to it
            // (the per-page file naming scheme). With CompressOutputToZipArchive
            // the same per-page files become entries of a zip archive at the
            // target path instead.
            var svgStem = System.IO.Path.GetFileNameWithoutExtension(outputFileName);
            var svgExt = svgOpts.CompressOutputToZipArchive
                ? ".svg"
                : System.IO.Path.GetExtension(outputFileName);
            string PageFile(int n) => n == 1 ? $"{svgStem}{svgExt}" : $"{svgStem}_{n}{svgExt}";
            var device = new Devices.SvgDevice
            {
                SaveOptions = svgOpts,
                PageLinkTarget = PageFile,
            };
            if (svgOpts.CompressOutputToZipArchive)
            {
                using var zipStream = File.Create(outputFileName);
                using var zip = new System.IO.Compression.ZipArchive(
                    zipStream, System.IO.Compression.ZipArchiveMode.Create);
                for (var i = 1; i <= PageCount; i++)
                {
                    var entry = zip.CreateEntry(PageFile(i), System.IO.Compression.CompressionLevel.Optimal);
                    using var es = entry.Open();
                    var bytes = System.Text.Encoding.UTF8.GetBytes(device.Process(Pages[i]));
                    es.Write(bytes, 0, bytes.Length);
                }
                return;
            }
            var svgDir = System.IO.Path.GetDirectoryName(outputFileName) ?? "";
            for (var i = 1; i <= PageCount; i++)
                File.WriteAllText(System.IO.Path.Combine(svgDir, PageFile(i)),
                    device.Process(Pages[i]), new System.Text.UTF8Encoding(false));
            return;
        }
        if (options is PdfToMarkdown.MarkdownSaveOptions md)
        {
            var outDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(outputFileName));
            var markdown = PdfToMarkdown.MarkdownRenderer.Render(this, md, outDir);
            File.WriteAllText(outputFileName, markdown, new System.Text.UTF8Encoding(false));
            return;
        }
        ApplyPdfSaveOptions(options);
        using var fs = File.Create(outputFileName);
        Save(fs);
    }

    /// <summary>Apply the supported <see cref="PdfSaveOptions"/> settings to the
    /// document before it is written. Currently this honours
    /// <see cref="PdfSaveOptions.DefaultFontName"/>: every font that cannot be
    /// resolved (not embedded, no source data, and not a available system face) is
    /// rebased onto the requested default so the saved PDF — and the in-memory
    /// font collection — report that name.</summary>
    private void ApplyPdfSaveOptions(SaveOptions? options)
    {
        if (options is not PdfSaveOptions pso || string.IsNullOrEmpty(pso.DefaultFontName))
            return;

        foreach (var page in Pages)
        {
            var resDict = _reader.ResolveDict(page.Dict.Get("Resources"));
            var fontDict = resDict is not null ? _reader.ResolveDict(resDict.Get("Font")) : null;
            if (fontDict is null) continue;
            foreach (var key in fontDict.Keys)
            {
                var fd = _reader.ResolveDict(fontDict.Get(key));
                if (fd is null) continue;
                var font = new Text.Font(key, fd, _reader);
                if (!font.IsAccessible)
                    fd.Set("BaseFont", new Core.PdfName(pso.DefaultFontName));
            }
        }
    }

    /// <summary>Save the document using the configured <see cref="SaveOptions"/>.</summary>
    public void Save(SaveOptions options)
    {
        _ = options;
        if (string.IsNullOrEmpty(FileName))
            return; // No bound file; caller should use Save(Stream) or Save(string).
        Save(FileName);
    }

    /// <summary>Async wrapper around <see cref="Save()"/>.</summary>
    public System.Threading.Tasks.Task SaveAsync(System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>Async wrapper around <see cref="Save(SaveOptions)"/>.</summary>
    public System.Threading.Tasks.Task SaveAsync(SaveOptions options, System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Save(options);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>Async wrapper around <see cref="Save(Stream)"/>.</summary>
    public System.Threading.Tasks.Task SaveAsync(Stream output, System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Save(output);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>Async wrapper around <see cref="Save(Stream, SaveFormat)"/>.</summary>
    public System.Threading.Tasks.Task SaveAsync(Stream outputStream, SaveFormat format, System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Save(outputStream, format);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>Async wrapper around <see cref="Save(Stream, SaveOptions)"/>.</summary>
    public System.Threading.Tasks.Task SaveAsync(Stream outputStream, SaveOptions options, System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Save(outputStream, options);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>Async wrapper around <see cref="Save(string)"/>.</summary>
    public System.Threading.Tasks.Task SaveAsync(string outputFileName, System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Save(outputFileName);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>Async wrapper around <see cref="Save(string, SaveFormat)"/>.</summary>
    public System.Threading.Tasks.Task SaveAsync(string outputFileName, SaveFormat format, System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Save(outputFileName, format);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>Async wrapper around <see cref="Save(string, SaveOptions)"/>.</summary>
    public System.Threading.Tasks.Task SaveAsync(string outputFileName, SaveOptions options, System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Save(outputFileName, options);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>
    /// Save the document to a stream.
    /// </summary>
    public void Save(Stream output)
    {
        // When the caller opts into strict signature handling, refuse to re-save a
        // signed document — rewriting it would invalidate the existing signature.
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

        // PDF 32000-2 § 14.3.3 — when both /Info and XMP /Metadata exist they
        // are equivalent representations. Pull XMP-side values into /Info for
        // keys that XMP carries (non-empty) but /Info does not.
        SyncXmpIntoInfo();

        // PDF 2.0 (ISO 32000-2 § 14.3.2) deprecates the documentary /Info text
        // entries: the descriptive fields live in the XMP packet, and a 2.0 save
        // transfers Title/Author/Subject/Keywords there and drops them from /Info.
        // Dates and the producer convention stay - only the deprecated text moves.
        // The version the SAVE will stamp - a conversion sets the override without
        // touching the loaded header, and the 2.0 rule keys off what the file will say.
        if ((_versionOverride ?? PdfVersion) == "2.0")
        {
            // Every documentary entry is MIRRORED into the packet under the xmp
            // prefix - producer, creator and the dates included - but only the four
            // deprecated descriptive fields LEAVE /Info; dates and the producing
            // application remain meaningful there in 2.0.
            string[] mirrored =
            [
                "Title", "Author", "Subject", "Keywords",
                "Producer", "Creator", "CreationDate", "ModDate",
            ];
            string[] removed = ["Title", "Author", "Subject", "Keywords"];
            XmpMetadata? xmp = null;
            foreach (var key in mirrored)
            {
                var value = Info[key];
                if (string.IsNullOrEmpty(value)) continue;
                xmp ??= GetOrCreateXmpMetadata();
                xmp.Set("xmp:" + key, value);
            }
            foreach (var key in removed)
            {
                if (!string.IsNullOrEmpty(Info[key])) Info.Remove(key);
            }
        }

        // The Producer names the producing library, so every save stamps this
        // library's own string — the usual producer self-identification
        // convention. Only a producer the caller assigned explicitly (through
        // Info.Producer or the metadata mutators) survives; a value merely
        // carried by the loaded file does not. An existing XMP pdf:Producer is
        // restamped in step so the two representations stay equivalent.
        bool producerExplicit = Info.ProducerAssigned ||
            (HasMetadata && GetOrCreateMetadata().ProducerExplicitlySet);
        if (!producerExplicit)
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

        // Apply page-level paragraphs, headers, and footers before saving
        ApplyPageContent();

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

        // A RemoveUnusedFonts edit orphaned the replaced fonts' objects (dictionaries,
        // descriptors, /FontFile programs). Recompute reachability so the serializer drops
        // them from the saved file instead of carrying them over — otherwise the file keeps
        // the (now unused) embedded font programs and never shrinks.
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

        // Linearize ("optimize for fast web view", PDF 32000 Annex F) when the document was
        // explicitly linearized (Optimize()/LinearizeDocument()) or was loaded from a linearized
        // source. The body is serialized to a buffer with a traditional cross-reference table —
        // no object streams — so PdfLinearizer can re-lay-out the object bytes; the linearized
        // result is then written to the real output.
        //
        // OptimizeSize wins over linearization: a linearized file repeats the first-page
        // objects up front, carries a hint stream, and cannot pack objects into compressed
        // object streams — so it is always LARGER than the plain object-stream save. When the
        // caller asked to minimise size, skip linearization and keep the compact form
        // (a font-unembed + OptimizeSize save is 8.4 KB unlinearized vs 16 KB
        // linearized).
        bool doLinearize = (_linearize || IsLinearized) && !OptimizeSize && _encryptor is null;
        var writeTarget = doLinearize ? new MemoryStream() : output;
        var writer = new PdfWriter(writeTarget, _encryptor);

        var xref = _reader.XRefTable;
        var trailer = _reader.Trailer;

        // Enable object streams when the original PDF used them (reduces output size significantly).
        // Objects with inline PdfStream values are excluded from ObjStm packing by the writer.
        // Linearization needs each object at a top-level file offset, so it stays off there.
        var hasCompressedObjects = xref.Entries.Values.Any(e => e.IsCompressed);
        // PDF/A-1 (ISO 19005-1 §6.1.4) prohibits cross-reference streams — and object
        // streams require one — so a document converted to PDF/A-1 saves with a
        // classic xref table even when the source used compressed objects.
        bool pdfA1Target = _lastConvertedFormat
            is Aspose.Pdf.PdfFormat.PDF_A_1A or Aspose.Pdf.PdfFormat.PDF_A_1B;
        if (hasCompressedObjects && _encryptor is null && !doLinearize && !pdfA1Target)
        {
            writer.UseObjectStreams = true;
        }
        // The classic-xref PDF/A-1 output loses the object-stream packing win;
        // recover the size by re-deflating weakly-compressed source streams,
        // as the conversion save does.
        if (pdfA1Target) writer.RecompressFlateStreams = true;

        // The source file's cross-reference infrastructure — object streams (/Type /ObjStm)
        // and cross-reference streams (/Type /XRef) — is regenerated from scratch by the
        // writer. Carrying the originals over leaves them as dead, unreferenced streams in the
        // output, and because each save emits a fresh set, re-saving an already-saved file
        // would accumulate one dead ObjStm + one dead XRef per cycle and grow the file
        // monotonically. Skip them on write, and exclude their numbers from the object-number
        // ceiling below so the regenerated containers get the same numbers every cycle —
        // together this makes a load/save round-trip byte-stable.
        var infraObjNums = xref.InfrastructureObjectNumbers();
        foreach (var linInfra in _reader.LinearizationInfraObjects) infraObjNums.Add(linInfra);
        int MaxRealObjNum() => xref.Entries.Keys.Where(k => !infraObjNums.Contains(k)).DefaultIfEmpty(0).Max();

        writer.WriteHeader(_versionOverride ?? PdfVersion ?? "1.4");

        // Pre-allocate object number for XMP metadata (so catalog gets the reference)
        int metaObjNum = -1;
        PdfStream? metaStream = null;
        byte[]? xmpBytes = _rawXmpOverride
            ?? ((_metadata is not null && _metadata.IsDirty) ? _metadata.ToXmpBytes() : null);
        if (xmpBytes is not null)
        {
            // The XMP packet is being rewritten (or cleared). Whichever way, the catalog's
            // existing /Metadata stream is stale: skip it on write and drop it from the
            // object-number ceiling so we don't carry the old packet as a dead object.
            if (_reader.Catalog.Get("Metadata") is PdfIndirectRef oldMetaRef)
                infraObjNums.Add(oldMetaRef.ObjectNumber);

            if (xmpBytes.Length > 0)
            {
                var metaDict = new PdfDictionary();
                metaDict.Set("Type", new PdfName("Metadata"));
                metaDict.Set("Subtype", new PdfName("XML"));
                metaDict.Set("Length", new PdfInteger(xmpBytes.Length));
                metaStream = new PdfStream(metaDict, xmpBytes);

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
                var maxObj = MaxRealObjNum();
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
                metaObjNum = maxObj + 1;
                _reservedMetadataObjNum = metaObjNum;
                _reader.Catalog.Set("Metadata", new PdfIndirectRef(metaObjNum, 0));
            }
            else
            {
                // Empty packet (e.g. SetXmpMetadata(Stream.Null)) means "remove the document
                // metadata". Drop the catalog reference and write no stream so the file
                // actually shrinks rather than gaining an empty /Metadata object.
                _reader.Catalog.Remove("Metadata");
            }
        }

        // Pre-advance the writer's object counter past ALL known object numbers
        // (existing xref, _newObjects, and metaObjNum) so that any indirect objects
        // promoted from inline PdfStream values during serialization get fresh numbers
        // that don't collide with anything already planned.
        {
            var maxKnown = MaxRealObjNum();
            foreach (var (objNum, _) in _newObjects)
                if (objNum > maxKnown) maxKnown = objNum;
            if (metaObjNum > maxKnown) maxKnown = metaObjNum;
            writer.SetMinObjectNumber(maxKnown + 1);
        }

        // A structure element authored against a not-yet-numbered page (fresh
        // document) still needs its /Pg before the catalog below is serialized
        // inline. Decide those pages' object numbers now — RebuildPagesTree
        // writes a page at SourceObjectNumber when one is assigned.
        if (PendingStructPgFixups.Count > 0)
        {
            if (_pages is not null && _pages.ImportSlotHighWater > 0)
                writer.ReserveObjectNumber(_pages.ImportSlotHighWater);
            foreach (var (elem, page) in PendingStructPgFixups)
            {
                if (page.ImportSlotObjNum <= 0 && page.SourceObjectNumber <= 0)
                    page.SourceObjectNumber = writer.AllocateObjectNumber();
                var pgNum = page.ImportSlotObjNum > 0 ? page.ImportSlotObjNum : page.SourceObjectNumber;
                elem.Set("Pg", new PdfIndirectRef(pgNum, 0));
            }
            PendingStructPgFixups.Clear();
        }

        // Pre-scan the catalog's inline object graph for dictionaries shared between more than
        // one parent (e.g. a generated radio group reached from /AcroForm/Fields and from each
        // option widget's /Parent). These are written once as a shared indirect object so the
        // back-references survive a round-trip instead of being dropped at the write cycle.
        writer.MarkSharedDicts(_reader.Catalog);

        // Map each existing page's source object number to its authoritative
        // in-memory dictionary. The page renderer clears the reader's object cache
        // to free decoded streams, so re-resolving a page below would re-parse a
        // pristine dict and silently drop in-memory edits made after rendering
        // (e.g. an hOCR invisible-text overlay added via Convert). Writing the live
        // Page.Dict for these object numbers preserves those edits.
        var livePageDicts = new Dictionary<int, PdfDictionary>();
        if (_pages is not null)
        {
            foreach (var p in _pages)
                if (p.SourceObjectNumber > 0)
                    livePageDicts[p.SourceObjectNumber] = p.Dict;
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
        foreach (var entry in xref.Entries.Values.OrderBy(e => e.ObjectNumber))
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
            if (infraObjNums.Contains(entry.ObjectNumber)) continue;

            // Prefer the live in-memory page dictionary over a (possibly stale,
            // post-ClearCache re-parsed) reader resolution. See livePageDicts above.
            if (livePageDicts.TryGetValue(entry.ObjectNumber, out var liveDict))
                obj = liveDict;

            writer.WriteIndirectObject(entry.ObjectNumber, obj);
        }

        // Write any new objects that were added (e.g., deep-cloned resources from imports,
        // new Info dict). These must be written BEFORE RebuildPagesTree so their obj numbers
        // don't collide with writer-allocated numbers.
        foreach (var (objNum, obj) in _newObjects)
        {
            writer.WriteIndirectObject(objNum, obj);
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
                writer.WriteIndirectObject(objNum, obj);
            }
        }

        // Handle page additions/deletions by rebuilding the Pages tree
        if (_pages is not null && _pages.IsModified)
        {
            // Rebuild /Pages with updated /Kids and /Count
            RebuildPagesTree(writer);
        }

        // Write XMP metadata stream
        if (metaStream is not null && metaObjNum >= 0)
        {
            writer.WriteIndirectObject(metaObjNum, metaStream);
        }

        // Build trailer dict
        var newTrailer = new PdfDictionary();
        CopyTrailerEntry(trailer, newTrailer, "Root");

        // Use new Info ref if we created one, otherwise copy from original
        if (_newInfoObjNum is not null)
        {
            newTrailer.Set("Info", new PdfIndirectRef(_newInfoObjNum.Value, 0));
        }
        else
        {
            CopyTrailerEntry(trailer, newTrailer, "Info");
        }

        if (_encryptor is not null)
        {
            // Write encrypt dictionary as an indirect object (excluded from encryption)
            var encryptObjNum = writer.AllocateObjectNumber();
            writer.ExcludeFromEncryption(encryptObjNum);
            writer.WriteIndirectObject(encryptObjNum, _encryptor.BuildEncryptDict());
            newTrailer.Set("Encrypt", new PdfIndirectRef(encryptObjNum, 0));

            // Set file ID (required for encryption)
            var idArray = new PdfArray();
            idArray.Add(new PdfString(_encryptor.FileId, isHex: true));
            idArray.Add(new PdfString(_encryptor.FileId, isHex: true));
            newTrailer.Set("ID", idArray);
        }
        else if (_forceWriteId && trailer.Get("ID") is null)
        {
            // Generate a file ID (required for PDF/A)
            var fileId = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
            var idArray = new PdfArray();
            idArray.Add(new PdfString(fileId, isHex: true));
            idArray.Add(new PdfString(fileId, isHex: true));
            newTrailer.Set("ID", idArray);
        }
        else
        {
            CopyTrailerEntry(trailer, newTrailer, "ID");
        }

        writer.WriteXRefAndTrailer(newTrailer);

        if (doLinearize)
        {
            var normal = ((MemoryStream)writeTarget).ToArray();
            var linearized = IO.PdfLinearizer.Linearize(normal);
            output.Write(linearized, 0, linearized.Length);
        }
    }
}

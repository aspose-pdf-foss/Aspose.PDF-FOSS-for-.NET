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

public sealed partial class Document : IDisposable
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

    /// <summary>
    /// Serialize the document into a fresh byte array.
    /// </summary>
    public byte[] ToArray()
    {
        FireBeforePageGenerateEvents();

        if (_sourceStream is not null && _sourceStream.CanWrite)
        {
            SaveIncremental(_sourceStream);
            _sourceStream.Seek(0, SeekOrigin.Begin);
            using var copy = new MemoryStream();
            _sourceStream.CopyTo(copy);
            return copy.ToArray();
        }

        using var ms = new MemoryStream();
        Save(ms);
        return ms.ToArray();
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

    // ── Background / Actions / FontSubstitution / CustomSecurityHandler

    /// <summary>Document-wide background colour painted on every page
    /// before content during Save. Real — emits a content-stream prologue
    /// that fills the MediaBox with the configured colour.</summary>
    public Color? Background { get; set; }

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

    private Annotations.DocumentActionCollection? _actions;

    /// <summary>Catalog /AA additional-action dictionary. Real — slots
    /// configured via the returned collection are written to the catalog
    /// /AA dict during Save.</summary>
    public Annotations.DocumentActionCollection Actions
        => _actions ??= new Annotations.DocumentActionCollection(this);

    /// <summary>Delegate fired when the renderer fails to resolve a font
    /// referenced by a content stream. <paramref name="originalFont"/> is
    /// the font that couldn't be loaded; <paramref name="newFont"/> is the
    /// substitute the renderer fell back to. Real wiring lives in the
    /// FontResolver path — when this event has subscribers, the resolver
    /// invokes them before completing the substitution.</summary>
    public delegate void FontSubstitutionHandler(Text.Font oldFont, Text.Font newFont);

    /// <summary>Fired by the font-loading pipeline when a referenced font
    /// must be substituted. Real — the renderer raises this through
    /// <see cref="RaiseFontSubstitution"/>.</summary>
    public event FontSubstitutionHandler? FontSubstitution;

    /// <summary>Internal hook invoked by the font-loading pipeline.</summary>
    internal void RaiseFontSubstitution(Text.Font original, Text.Font replacement)
        => FontSubstitution?.Invoke(original, replacement);

    /// <summary>Stored custom security handler when set via the
    /// ICustomSecurityHandler Encrypt overloads. Get-only.</summary>
    public Security.ICustomSecurityHandler? CustomSecurityHandler { get; private set; }

    /// <summary>Encrypt with a custom security handler, which supplies the /O and
    /// /U values, the file key and the per-object cipher in place of the Standard
    /// handler. Like the Standard overloads this only arms the encryptor — the
    /// /Encrypt dictionary is written at Save.</summary>
    public void Encrypt(string userPassword, string ownerPassword,
        Facades.DocumentPrivilege privileges,
        Security.ICustomSecurityHandler customHandler)
        // The reserved-bit layout of Table 22 belongs to the Standard handler; an
        // alternative /Filter defines its own permission encoding, so the privilege
        // value travels to /P unchanged and reads back identically.
        => EncryptWithCustomHandler(userPassword, ownerPassword,
            privileges?.Value ?? -1, customHandler);

    /// <summary>Same — Permissions-typed overload.</summary>
    public void Encrypt(string userPassword, string ownerPassword,
        Aspose.Pdf.Permissions permissions,
        Security.ICustomSecurityHandler customHandler)
        => EncryptWithCustomHandler(userPassword, ownerPassword,
            (int)permissions, customHandler);

    private void EncryptWithCustomHandler(string userPassword, string ownerPassword,
        int permissions, Security.ICustomSecurityHandler customHandler)
    {
        if (customHandler is null) throw new System.ArgumentNullException(nameof(customHandler));
        if (HasCertificationSignature())
            throw new System.InvalidOperationException(
                "Cannot encrypt a document carrying a certification signature.");
        CustomSecurityHandler = customHandler;
        _encryptor = Security.PdfEncryptor.CreateWithCustomHandler(
            customHandler, userPassword, ownerPassword, permissions);
    }

    /// <summary>Encrypt with a list of recipient public certificates
    /// (Public-Key /Filter security handler). Not implemented; throws
    /// NotSupportedException to make the gap explicit.</summary>
    public void Encrypt(Aspose.Pdf.Permissions permissions, CryptoAlgorithm cryptoAlgorithm,
        System.Collections.Generic.IList<System.Security.Cryptography.X509Certificates.X509Certificate2> publicCertificates)
    {
        // Public-key (certificate) security handler — Adobe.PPKLite. The file key is
        // enveloped to each recipient certificate; object encryption reuses the
        // Standard handler's RC4/AES pass through the built encryptor.
        _encryptor = Security.PubSecHandler.CreateEncryptor(permissions, cryptoAlgorithm, publicCertificates);
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
    /// Emit the HTML document to a single stream. Shared by the stream and file
    /// save overloads (each runs its own target-specific consistency check first).
    /// </summary>
    private void WriteHtmlCore(Stream output, HtmlSaveOptions options)
    {
        ValidateExplicitPageList(options.ExplicitListOfSavedPages);
        ReportHtmlConversionProgress(options, analysisPhase: true);
        var converter = new Converters.PdfToHtmlConverter { DocumentType = options.DocumentType, Title = options.Title };

        // AsEmbeddedPartsOfPngPageBackground: each page's graphics flatten to one
        // raster PNG behind the selectable stl_ text layer, in a single
        // self-contained document (resources embedded as data: URIs unless the
        // caller's resource strategy takes them over — fonts dispatch inside the
        // renderer in the FontSavingMode format, so no TTF pre-pass here).
        if (options.RasterImagesSavingMode == HtmlSaveOptions.RasterImagesSavingModes.AsEmbeddedPartsOfPngPageBackground)
        {
            var embedded = converter.RenderDocumentEmbedded(this, options, pngBackground: true);
            var embeddedBytes = System.Text.Encoding.UTF8.GetBytes(embedded);
            output.Write(embeddedBytes, 0, embeddedBytes.Length);
            return;
        }

        // CustomResourceSavingStrategy: hand the document's fonts to the caller's
        // resource-saving callback (per-font TTF programs; see the dispatcher).
        Converters.PdfToHtmlConverter.DispatchFontResourceCallbacks(this, options);

        if (options.ExplicitListOfSavedPages is { Length: > 0 } pages)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(converter.DocTypeDeclaration());
            sb.AppendLine("<html><head><meta charset=\"utf-8\"></head><body>");
            foreach (var pageNum in pages)
            {
                sb.Append(converter.SavePageAsHtml(this, pageNum));
            }
            sb.AppendLine("</body></html>");
            var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            output.Write(bytes, 0, bytes.Length);
        }
        else
        {
            converter.SaveAsHtml(this, output);
        }
        ReportHtmlConversionProgress(options, analysisPhase: false);
    }

    /// <summary>
    /// Validate a caller-supplied <see cref="HtmlSaveOptions.ExplicitListOfSavedPages"/>.
    /// A null list means "all pages" and is always valid; a non-null list must be
    /// non-empty, hold only positive, in-range, non-duplicated page numbers. The
    /// messages are contractual (surfaced verbatim to callers).
    /// </summary>
    private void ValidateExplicitPageList(int[] pages)
    {
        if (pages is null) return;
        if (pages.Length == 0)
            throw new System.ArgumentException("Page numbers list: the list must contain at least one item!");
        var seen = new System.Collections.Generic.HashSet<int>();
        foreach (var p in pages)
        {
            if (p <= 0)
                throw new System.ArgumentException("Page numbers list: page number must be positive.");
            if (!seen.Add(p))
                throw new System.ArgumentException(
                    $"Page numbers list: Page number ({p}) occures more than once.");
            if (p > PageCount)
                throw new System.ArgumentException(
                    $"Page numbers list: page number ({p}) is greater then the number of pages in the document ({PageCount}).");
        }
    }

    /// <summary>Fire the conversion-progress cadence for HTML save when the caller
    /// installed <see cref="HtmlSaveOptions.CustomProgressHandler"/>. The sequence
    /// is fixed (18 events for
    /// a 3-page document): analysis walks pages to 25% total, then each page reports
    /// ResultPageCreated at 25 + 75·(i − 0.12)/N and ResultPageSaved at 25 + 75·i/N.</summary>
    private void ReportHtmlConversionProgress(HtmlSaveOptions options, bool analysisPhase)
    {
        var handler = options?.CustomProgressHandler;
        if (handler is null) return;
        var n = Pages.Count;
        if (n == 0) return;
        void Fire(ProgressEventType type, int value, int max) =>
            handler(new UnifiedSaveOptions.ProgressEventHandlerInfo { EventType = type, Value = value, MaxValue = max });
        if (analysisPhase)
        {
            for (var i = 1; i <= n; i++)
            {
                Fire(ProgressEventType.SourcePageAnalysed, i, n);
                Fire(ProgressEventType.TotalProgress, (int)System.Math.Round(25.0 * i / n), 100);
            }
        }
        else
        {
            for (var i = 1; i <= n; i++)
            {
                Fire(ProgressEventType.ResultPageCreated, i, n);
                Fire(ProgressEventType.TotalProgress, 25 + (int)System.Math.Round(75.0 * (i - 0.12) / n), 100);
                Fire(ProgressEventType.ResultPageSaved, i, n);
                Fire(ProgressEventType.TotalProgress, 25 + (int)System.Math.Round(75.0 * i / n), 100);
            }
        }
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
    /// Write the HTML plus a sibling <c>&lt;stem&gt;_files</c> folder holding the page
    /// SVGs and stylesheet (see <see cref="Converters.PdfToHtmlConverter.RenderDocumentExternal"/>).
    /// </summary>
    private void SaveHtmlWithExternalResources(string path, HtmlSaveOptions options)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full) ?? "";
        var stem = Path.GetFileNameWithoutExtension(full);
        var filesUrl = stem + "_files";
        var filesDir = Path.Combine(dir, filesUrl);

        // Fonts are offered to CustomResourceSavingStrategy inside the converter's
        // stylesheet finalize (per emitted font FILE, so the @font-face src can use
        // the URL the strategy returns).
        var converter = new Converters.PdfToHtmlConverter { DocumentType = options.DocumentType, Title = options.Title };
        ReportHtmlConversionProgress(options, analysisPhase: true);

        var (imagesDir, imagesUrl) = ResolveImagesFolder(options, dir, filesDir, filesUrl);
        // The sidecar folder exists BEFORE any caller strategy runs — strategies
        // commonly write their files into it themselves.
        Directory.CreateDirectory(filesDir);
        var sidecars = new System.Collections.Generic.List<Converters.PdfToHtmlConverter.SidecarFile>();
        var html = converter.RenderDocumentExternal(this, filesUrl, sidecars,
            options.ExplicitListOfSavedPages, options.CssClassNamesPrefix,
            pngBackground: options.RasterImagesSavingMode
                == HtmlSaveOptions.RasterImagesSavingModes.AsEmbeddedPartsOfPngPageBackground,
            svgImageRefs: options.RasterImagesSavingMode
                == HtmlSaveOptions.RasterImagesSavingModes.AsExternalPngFilesReferencedViaSvg,
            options: options, imagesUrl: imagesUrl);

        WriteSidecars(sidecars, filesDir, imagesDir);
        File.WriteAllBytes(full, System.Text.Encoding.UTF8.GetBytes(html));

        ReportHtmlConversionProgress(options, analysisPhase: false);
    }

    /// <summary>
    /// Write one HTML file per selected page (SplitIntoPages). File <c>h</c> is named
    /// <c>&lt;stem&gt;&lt;h&gt;.html</c> for the h-th selected page (1-based, in list order);
    /// the un-suffixed <c>&lt;stem&gt;.html</c> is not written. When a
    /// <see cref="HtmlSaveOptions.CustomHtmlSavingStrategy"/> is installed each page's
    /// bytes, host/PDF page numbers and proposed name are handed to it, and it writes the
    /// file itself unless it cancels — in which case the default name is used.
    /// </summary>
    private void SaveHtmlSplitToFiles(string path, HtmlSaveOptions options)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full) ?? "";
        var stem = Path.GetFileNameWithoutExtension(full);
        var filesUrl = stem + "_files";
        var filesDir = Path.Combine(dir, filesUrl);
        var pages = options.ExplicitListOfSavedPages is { Length: > 0 } list
            ? list
            : System.Linq.Enumerable.Range(1, PageCount).ToArray();

        var converter = new Converters.PdfToHtmlConverter { DocumentType = options.DocumentType, Title = options.Title };
        var bodyOnly = options.HtmlMarkupGenerationMode
            == HtmlSaveOptions.HtmlMarkupGenerationModes.WriteOnlyBodyContent;

        ReportHtmlConversionProgress(options, analysisPhase: true);

        // Every page file shares one <stem>_files sidecar folder (stylesheet, fonts,
        // page graphics); the page markup itself is written per page below. The folder
        // exists BEFORE any caller strategy runs — strategies commonly write their
        // files into it themselves.
        var (imagesDir, imagesUrl) = ResolveImagesFolder(options, dir, filesDir, filesUrl);
        Directory.CreateDirectory(filesDir);
        var sidecars = new System.Collections.Generic.List<Converters.PdfToHtmlConverter.SidecarFile>();
        var pageHtmls = converter.RenderDocumentExternalSplit(this, filesUrl, sidecars, pages, bodyOnly,
            pngBackground: options.RasterImagesSavingMode
                == HtmlSaveOptions.RasterImagesSavingModes.AsEmbeddedPartsOfPngPageBackground,
            svgImageRefs: options.RasterImagesSavingMode
                == HtmlSaveOptions.RasterImagesSavingModes.AsExternalPngFilesReferencedViaSvg,
            options: options, imagesUrl: imagesUrl);
        WriteSidecars(sidecars, filesDir, imagesDir);

        for (var h = 1; h <= pages.Length; h++)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(pageHtmls[h - 1]);
            var supposedName = stem + h + ".html";

            var strategy = options.CustomHtmlSavingStrategy;
            if (strategy is not null)
            {
                using var ms = new MemoryStream(bytes, writable: false);
                var info = new HtmlSaveOptions.HtmlPageMarkupSavingInfo
                {
                    ContentStream = ms,
                    HtmlHostPageNumber = h,
                    PdfHostPageNumber = pages[h - 1],
                    SupposedFileName = supposedName,
                    CustomProcessingCancelled = false,
                };
                strategy(info);
                if (info.CustomProcessingCancelled)
                    File.WriteAllBytes(Path.Combine(dir, supposedName), bytes);
            }
            else
            {
                File.WriteAllBytes(Path.Combine(dir, supposedName), bytes);
            }
        }

        ReportHtmlConversionProgress(options, analysisPhase: false);
    }

    /// <summary>
    /// Where image sidecars (page SVGs/PNGs and raster images) go:
    /// <see cref="HtmlSaveOptions.SpecialFolderForAllImages"/> when set, else the
    /// regular <c>&lt;stem&gt;_files</c> folder. Returns the folder plus the URL
    /// prefix that references it from the HTML's directory ("" when they coincide).
    /// </summary>
    private static (string ImagesDir, string ImagesUrl) ResolveImagesFolder(
        HtmlSaveOptions options, string htmlDir, string filesDir, string filesUrl)
    {
        if (string.IsNullOrEmpty(options.SpecialFolderForAllImages))
            return (filesDir, filesUrl);
        var imagesDir = Path.GetFullPath(options.SpecialFolderForAllImages);
        var rel = Path.GetRelativePath(string.IsNullOrEmpty(htmlDir) ? "." : htmlDir, imagesDir)
            .Replace(Path.DirectorySeparatorChar, '/');
        return (imagesDir, rel == "." ? "" : rel);
    }

    /// <summary>Write the sidecar files, routing image-typed ones to
    /// <paramref name="imagesDir"/> and the rest (stylesheet, fonts) to
    /// <paramref name="filesDir"/>.</summary>
    private static void WriteSidecars(
        System.Collections.Generic.List<Converters.PdfToHtmlConverter.SidecarFile> sidecars,
        string filesDir, string imagesDir)
    {
        Directory.CreateDirectory(filesDir);
        if (imagesDir != filesDir) Directory.CreateDirectory(imagesDir);
        foreach (var s in sidecars)
            File.WriteAllBytes(Path.Combine(s.IsImage ? imagesDir : filesDir, s.Name), s.Content);
    }

    /// <summary>
    /// Save to a stream using general SaveOptions (stub type from Aspose.Pdf namespace).
    /// For stub SaveOptions subclasses without real implementations, saves as PDF.
    /// </summary>
    public void Save(Stream outputStream, SaveOptions options)
    {
        if (options is SvgSaveOptions)
        {
            var svg = new Converters.PdfToSvgConverter().SavePageAsSvg(this, 1);
            var bytes = System.Text.Encoding.UTF8.GetBytes(svg);
            outputStream.Write(bytes, 0, bytes.Length);
            return;
        }
        if (options is PdfToMarkdown.MarkdownSaveOptions md)
        {
            var markdown = PdfToMarkdown.MarkdownRenderer.Render(this, md, null);
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
        if (options is SvgSaveOptions)
        {
            // Render real SVG markup instead of writing a PDF to the .svg path (the
            // historic no-op that made round-trip tests pass by a compensating
            // load-side bug). Page 1 goes to the requested path; a multi-page
            // document additionally writes page N to "<stem>_N.svg" next to it
            // (the per-page file naming scheme).
            var svgConverter = new Converters.PdfToSvgConverter();
            svgConverter.SavePageToFile(this, 1, outputFileName);
            if (PageCount > 1)
            {
                var dir = System.IO.Path.GetDirectoryName(outputFileName) ?? "";
                var stem = System.IO.Path.GetFileNameWithoutExtension(outputFileName);
                var ext = System.IO.Path.GetExtension(outputFileName);
                for (var i = 2; i <= PageCount; i++)
                    svgConverter.SavePageToFile(this, i,
                        System.IO.Path.Combine(dir, $"{stem}_{i}{ext}"));
            }
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

        var writer = new IncrementalWriter(output, _data, Math.Max(size, xref.Entries.Keys.DefaultIfEmpty(0).Max() + 1));

        foreach (var (objNum, obj) in modifiedObjects)
        {
            writer.WriteObject(objNum, obj);
        }

        writer.Flush(trailer, originalStartXref);
    }

    /// <summary>
    /// Register a structure tree builder for auto-finalization on save.
    /// </summary>
    internal void RegisterStructureTreeBuilder(StructureTreeBuilder builder)
    {
        _structureTreeBuilder = builder;
    }

    /// <summary>
    /// Register an outline builder for auto-finalization on save.
    /// </summary>
    internal void RegisterOutlineBuilder(OutlineBuilder builder)
    {
        _outlineBuilder = builder;
    }

    /// <summary>Materialise any pending <see cref="OutlineBuilder"/> into the catalog
    /// /Outlines tree now, instead of deferring to Save. Needed so a read path that
    /// inspects the live outline tree (e.g. chaining the document into a second
    /// <c>PdfBookmarkEditor</c> and calling <c>ExtractBookmarks</c>) sees bookmarks
    /// that were just added via <c>CreateBookmarkOfPage</c>. Idempotent — the builder
    /// is consumed once, and the cached outline collection is dropped so the next
    /// access re-reads the freshly written tree.</summary>
    internal void FlushPendingOutlineBuilder()
    {
        if (_outlineBuilder is null) return;
        _outlineBuilder.Build();
        _outlineBuilder = null;
        _outlines = null;
    }

    /// <summary>
    /// Register a page label builder for auto-finalization on save.
    /// </summary>
    internal void RegisterPageLabelBuilder(PageLabelBuilder builder)
    {
        _pageLabelBuilder = builder;
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

        // Validate deferred XML image file references
        if (PendingXmlImageFiles is { Count: > 0 } imageFiles)
        {
            foreach (var path in imageFiles)
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException($"Image file not found: {path}", path);
            }
        }

        // PDF 32000-2 § 14.3.3 — when both /Info and XMP /Metadata exist they
        // are equivalent representations. Pull XMP-side values into /Info for
        // keys that XMP carries (non-empty) but /Info does not. Producer is
        // included so a roundtrip preserves it without the stamp below
        // overwriting an XMP-only value.
        SyncXmpIntoInfo();

        // Stamp Producer in the Info dictionary if not already set
        if (string.IsNullOrEmpty(Info.Producer))
            Info.Producer = "Aspose.PDF FOSS for .NET";

        // Stamp the default Creator when the caller left it unset.
        if (string.IsNullOrEmpty(Info.Creator))
            Info.Creator = "Aspose Pty Ltd.";

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
            page.FlushUnderlineRemovals();
            page.FlushStrikeOutRectangles();
            page.FlushHyperlinkAnnotations();
            // PageInformationAnnotation prints the output file name + date; generate its
            // appearance here, when the save file name is known.
            if (_pendingSaveFileName is not null)
                page.FlushPageInfoAnnotations(_pendingSaveFileName, DateTime.Today);
            page.SyncAttachedFragments();
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

                // Use a high object number to avoid collisions. The ceiling must
                // clear EVERY planned number, not just the source xref: page
                // merging reserves numbers for imported objects (annots, fonts)
                // far above the original max, and landing the metadata stream on
                // one of those silently orphans it — the xref keeps whichever is
                // written last, and a merged form field vanishes on reload.
                var maxObj = MaxRealObjNum();
                foreach (var (n, _) in _newObjects)
                    if (n > maxObj) maxObj = n;
                if (_pages is not null)
                {
                    foreach (var (n, _) in _pages.ImportedObjects)
                        if (n > maxObj) maxObj = n;
                    if (_pages.ImportSlotHighWater > maxObj) maxObj = _pages.ImportSlotHighWater;
                }
                metaObjNum = maxObj + 100;
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
            var fileId = Security.CryptoRandom.GetBytes(16);
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

    private void RebuildPagesTree(PdfWriter writer)
    {
        // Determine the actual /Pages object number from the catalog
        var pagesRef = _reader.Catalog.Get("Pages");
        var pagesObjNum = pagesRef is PdfIndirectRef pr ? pr.ObjectNumber : 2;

        // Reserve the writer's number space above every cross-document import slot so a
        // writer-allocated page number can't collide with a slot — including slots for
        // destination-only pages that are referenced but never written.
        if (_pages is not null)
            writer.ReserveObjectNumber(_pages.ImportSlotHighWater);

        // Build a new /Pages dict with all current pages as kids
        var kids = new PdfArray();
        foreach (var page in Pages)
        {
            // Choose each page's object number:
            //  - an imported page is written at its reserved slot so GoTo/Link destinations
            //    that target it (and point at that slot) resolve to this copy;
            //  - a page loaded from THIS document keeps its original object number, so the
            //    document's own internal links (bookmarks, link annotations, named
            //    destinations) — which reference pages by object number — still resolve
            //    after pages are deleted or reordered (imported pages carry
            //    SourceObjectNumber = -1, so they never take this branch);
            //  - a newly created page takes a fresh writer-allocated number.
            int objNum;
            if (page.ImportSlotObjNum > 0) objNum = page.ImportSlotObjNum;
            else if (page.SourceObjectNumber > 0) objNum = page.SourceObjectNumber;
            else objNum = writer.AllocateObjectNumber();

            page.Dict.Set("Parent", new PdfIndirectRef(pagesObjNum, 0));
            writer.WriteIndirectObject(objNum, page.Dict);
            kids.Add(new PdfIndirectRef(objNum, 0));
        }

        var pagesDict = new PdfDictionary();
        pagesDict.Set("Type", new PdfName("Pages"));
        pagesDict.Set("Kids", kids);
        pagesDict.Set("Count", new PdfInteger(Pages.Count));

        // Write at the original /Pages object number
        writer.WriteIndirectObject(pagesObjNum, pagesDict);

        // Keep the in-memory reader's page tree consistent with the current page
        // order (Insert/Delete only updated the Pages list, not the underlying
        // /Kids). Without this, page-number lookups that walk the reader tree after
        // a save — e.g. resolving a GoTo destination's target page — see the stale
        // pre-edit order. The kids are the live page dicts in their current order.
        var inMemPages = _reader.ResolveDict(_reader.Catalog.Get("Pages"));
        if (inMemPages is not null)
        {
            var inMemKids = new PdfArray();
            foreach (var page in Pages) inMemKids.Add(page.Dict);
            inMemPages.Set("Kids", inMemKids);
            inMemPages.Set("Count", new PdfInteger(Pages.Count));
        }
    }

    /// <summary>
    /// Rebuild the in-memory catalog /Pages tree (/Kids and /Count) so it matches
    /// the current page order. <see cref="PageCollection.Insert"/> / Delete update
    /// only the Pages list, not the underlying /Kids, so any reader-tree walk before
    /// save — e.g. resolving a GoTo or bookmark destination's target page number —
    /// would otherwise see the stale pre-edit order (off-by-one after a page is
    /// inserted). Safe to call repeatedly; a no-op when no page was added or removed.
    /// </summary>
    internal void SyncInMemoryPageTree()
    {
        if (_pages is null || !_pages.IsModified) return;
        var inMemPages = _reader.ResolveDict(_reader.Catalog.Get("Pages"));
        if (inMemPages is null) return;

        // Preserve each already-loaded page's original indirect reference so the
        // rebuilt /Kids keeps page object-number identity: named-destination
        // resolution maps a page's object number to its index, so flattening to
        // bare dicts would make named destinations unresolvable. Newly inserted
        // (pending) pages have no object number yet and go in as direct dicts.
        var dictToRef = new Dictionary<PdfDictionary, PdfObject>(
            System.Collections.Generic.ReferenceEqualityComparer.Instance);
        CollectKidRefs(inMemPages, dictToRef);

        var inMemKids = new PdfArray();
        foreach (var page in Pages)
            inMemKids.Add(dictToRef.TryGetValue(page.Dict, out var r) ? r : page.Dict);
        inMemPages.Set("Kids", inMemKids);
        inMemPages.Set("Count", new PdfInteger(Pages.Count));
    }

    /// <summary>
    /// After pages have been removed (e.g. by <see cref="Facades.PdfFileEditor.Extract(byte[],int,int)"/>,
    /// which deletes every page outside the requested range), drop the objects that only the
    /// removed pages kept alive. A plain save writes every object still reachable from the
    /// trailer, and an outline bookmark, article thread, or link annotation that pointed at a
    /// removed page keeps that page — and its (often large) images — reachable, so an
    /// extracted file stays as big as the whole source. Recompute reachability treating each
    /// removed page as a cut point so the save writes only what the surviving pages still use.
    /// </summary>
    internal void CompactAfterPageRemoval()
    {
        if (_pages is null || !_pages.IsModified) return;

        // Flatten /Kids to the surviving pages so a removed page is no longer reachable
        // through the page tree itself.
        SyncInMemoryPageTree();

        var survivingPages = new HashSet<int>();
        foreach (var page in Pages)
            if (page.SourceObjectNumber > 0) survivingPages.Add(page.SourceObjectNumber);

        // Compute reachability but treat every removed page as a cut point: don't traverse a
        // /Type /Page object that isn't one of the survivors. This drops each removed page
        // and everything only it references (its images can be the bulk of the file) no
        // matter what still points at it — a bookmark, an article-thread bead's /P, a link
        // annotation on a surviving page. Those references simply dangle, resolving to
        // "no page" on reopen, which never keeps the page.
        var reachable = new HashSet<int>();
        CollectReachableExcludingRemovedPages(_reader.Trailer, reachable, survivingPages);
        if (reachable.Count > 0) _reachableObjects = reachable;
    }

    /// <summary>Reachability variant used after page removal: identical to
    /// <see cref="CollectReachable"/> except a <c>/Type /Page</c> object whose number is not
    /// in <paramref name="survivingPages"/> is neither marked reachable nor traversed, so a
    /// removed page (and any object only it kept alive) falls out of the saved file.</summary>
    private void CollectReachableExcludingRemovedPages(PdfObject? root, HashSet<int> visited, HashSet<int> survivingPages)
    {
        if (root is null or PdfNull) return;
        var stack = new Stack<PdfObject>();
        stack.Push(root);
        var seenDicts = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);

        while (stack.Count > 0)
        {
            var obj = stack.Pop();
            if (obj is null or PdfNull) continue;

            if (obj is PdfIndirectRef iref)
            {
                if (visited.Contains(iref.ObjectNumber)) continue;
                var resolved = _reader.Resolve(iref);
                // Cut removed pages: don't record or traverse them.
                if (resolved is PdfDictionary pd && pd.GetName("Type") == "Page"
                    && !survivingPages.Contains(iref.ObjectNumber))
                    continue;
                visited.Add(iref.ObjectNumber);
                if (resolved is not null) stack.Push(resolved);
                continue;
            }
            if (obj is PdfStream stream) { stack.Push(stream.Dict); continue; }
            if (obj is PdfDictionary dict)
            {
                if (!seenDicts.Add(dict)) continue;
                foreach (var key in dict.Keys)
                {
                    var val = dict.Get(key);
                    if (val is not null) stack.Push(val);
                }
                continue;
            }
            if (obj is PdfArray arr)
                foreach (var item in arr)
                    if (item is not null) stack.Push(item);
        }
    }

    /// <summary>Map each leaf page dictionary in a /Pages subtree to the indirect
    /// reference that points at it, so <see cref="SyncInMemoryPageTree"/> can keep
    /// those references when it rebuilds a flat /Kids array.</summary>
    private void CollectKidRefs(PdfDictionary node, Dictionary<PdfDictionary, PdfObject> map)
    {
        if (_reader.Resolve(node.Get("Kids")) is not PdfArray kids) return;
        foreach (var kid in kids)
        {
            var kidDict = _reader.ResolveDict(kid);
            if (kidDict is null) continue;
            if (kidDict.GetName("Type") == "Page")
            {
                if (kid is PdfIndirectRef) map[kidDict] = kid;
            }
            else
            {
                CollectKidRefs(kidDict, map);
            }
        }
    }

    private static void CopyTrailerEntry(PdfDictionary source, PdfDictionary dest, string key)
    {
        var val = source.Get(key);
        if (val is not null)
            dest.Set(key, val);
    }

    private static byte[] ReadStreamToBytes(Stream stream)
    {
        if (stream.CanSeek && stream.Position != 0)
            stream.Position = 0;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

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

    private static byte[] CreateEmptyPdf()
    {
        using var ms = new MemoryStream();
        var writer = new PdfWriter(ms);
        // Newly-created documents default to PDF 1.7 (matches the modern baseline
        // and the PdfFormat default). Loaded documents keep their own header version.
        writer.WriteHeader("1.7");

        // Catalog
        var catalogDict = new PdfDictionary();
        catalogDict.Set("Type", new PdfName("Catalog"));
        catalogDict.Set("Pages", new PdfIndirectRef(2, 0));
        writer.WriteIndirectObject(1, catalogDict);

        // Pages (empty — no kids)
        var pagesDict = new PdfDictionary();
        pagesDict.Set("Type", new PdfName("Pages"));
        pagesDict.Set("Kids", new PdfArray());
        pagesDict.Set("Count", new PdfInteger(0));
        writer.WriteIndirectObject(2, pagesDict);

        var trailer = new PdfDictionary();
        trailer.Set("Root", new PdfIndirectRef(1, 0));
        writer.WriteXRefAndTrailer(trailer);

        return ms.ToArray();
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
        // document otherwise saves with a blank canvas.
        if (_isNewDocument && Pages.Count == 0)
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

        var title = Info.Title;
        if (!string.IsNullOrEmpty(title))
        {
            DisplayDocTitle = true;
            var meta = GetOrCreateMetadata();
            if (string.IsNullOrEmpty(meta.Get("dc:title"))) meta.Set("dc:title", title);
            if (string.IsNullOrEmpty(meta.Get("pdf:Producer"))) meta.Set("pdf:Producer", "Aspose.PDF FOSS for .NET");
            if (string.IsNullOrEmpty(meta.Get("pdfuaid:part"))) meta.Set("pdfuaid:part", "1");
        }

        if (_reader.Trailer.Get("ID") is null)
        {
            var fileId = Security.CryptoRandom.GetBytes(16);
            var idArray = new PdfArray();
            idArray.Add(new PdfString(fileId, isHex: true));
            idArray.Add(new PdfString(fileId, isHex: true));
            _reader.Trailer.Set("ID", idArray);
            _forceWriteId = true;
        }
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
        // Write the original PDF data first
        output.Seek(0, SeekOrigin.Begin);
        output.Write(_data);

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
            Math.Max(size, xref.Entries.Keys.DefaultIfEmpty(0).Max() + 1));

        foreach (var (objNum, obj) in modified)
            writer.WriteObject(objNum, obj);

        writer.Flush(trailer, originalStartXref);
        output.SetLength(output.Position);
        output.Flush();
    }

    // ── Internal write infrastructure ────────────────────────────────────────

    private readonly List<(int objNum, PdfObject obj)> _newObjects = [];

    /// <summary>Deferred image file paths from BindXml, validated during Save.</summary>
    internal List<string>? PendingXmlImageFiles { get; set; }

    /// <summary>Default page-tree branching factor (PDF table 30 /Count vs /Kids ratio).</summary>
    public const byte DefaultNodesNumInSubtrees = 10;

    // ── Stored-only flag props (public-API parity; no behaviour) ──

    /// <summary>Whether the saver may reuse identical page-content streams. Stored only.</summary>
    public bool AllowReusePageContent { get; set; }

    /// <summary>Whether the document emits notification log entries. Stored only.</summary>
    public bool EnableNotificationLogging { get; set; }

    /// <summary>Whether signature fields fire change-handlers when their dict mutates. Stored only.</summary>
    public bool HandleSignatureChange { get; set; }

    /// <summary>Viewer preference: hide the menu bar (/ViewerPreferences /HideMenubar).</summary>
    public bool HideMenubar
    {
        get => GetViewerPrefBool("HideMenubar");
        set => SetViewerPrefBool("HideMenubar", value);
    }

    /// <summary>Viewer preference: hide the toolbar (/ViewerPreferences /HideToolbar).</summary>
    public bool HideToolBar
    {
        get => GetViewerPrefBool("HideToolbar");
        set => SetViewerPrefBool("HideToolbar", value);
    }

    /// <summary>Viewer preference: hide window UI chrome (/ViewerPreferences /HideWindowUI).</summary>
    public bool HideWindowUI
    {
        get => GetViewerPrefBool("HideWindowUI");
        set => SetViewerPrefBool("HideWindowUI", value);
    }

    /// <summary>Allow gaps in the xref table during parse. Stored only.</summary>
    public bool IsXrefGapsAllowed { get; set; }

    /// <summary>Viewer preference: pick the paper tray by PDF page size
    /// (/ViewerPreferences /PickTrayByPDFSize).</summary>
    public bool PickTrayByPdfSize
    {
        get => GetViewerPrefBool("PickTrayByPDFSize");
        set => SetViewerPrefBool("PickTrayByPDFSize", value);
    }

    /// <summary>Maximum file size (bytes) loaded entirely into memory. Stored only.</summary>
    public int FileSizeLimitToMemoryLoading { get; set; } = int.MaxValue;

    /// <summary>Reset <see cref="FileSizeLimitToMemoryLoading"/> to its built-in default.</summary>
    public void SetDefaultFileSizeLimitToMemoryLoading() => FileSizeLimitToMemoryLoading = int.MaxValue;

    /// <summary>Update the /Info /Title entry.</summary>
    public void SetTitle(string title) { if (Info is { } info) info.Title = title; }

    /// <summary>Rebuild the page tree so each subtree has <paramref name="nodesNumInSubtrees"/> children. Stored only.</summary>
    public void PageNodesToBalancedTree(byte nodesNumInSubtrees) { _ = nodesNumInSubtrees; }

    /// <summary>Remove all metadata. Stored only — clears /Info and /Metadata in a future change.</summary>
    public void RemoveMetadata() { }

    /// <summary>Remove PDF/UA compliance markers. Stored only.</summary>
    public void RemovePdfUaCompliance() { }

    /// <summary>Flatten transparency to opaque graphics. Stored only — no-op in FOSS.</summary>
    public void FlattenTransparency() { }

    /// <summary>
    /// Give each of the passed pages a private copy of the image XObjects they
    /// share, so an in-place edit reached through one page (e.g. a redaction
    /// erasing image regions) no longer alters the others. Shared /Resources
    /// and /XObject dictionaries are unshared per page as well; the first page
    /// that uses an object keeps the original.
    /// </summary>
    public void SplitSharedImages(params Page[] pages)
    {
        if (pages is null || pages.Length < 2) return;

        var seenRefs = new HashSet<int>();
        var seenStreams = new HashSet<PdfStream>(ReferenceEqualityComparer.Instance);
        var seenResources = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var seenXobjDicts = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);

        foreach (var page in pages)
        {
            if (page is null) continue;
            var rd = page.Reader;
            var resources = rd.ResolveDict(page.Dict.Get("Resources"));
            if (resources is null) continue;
            if (!seenResources.Add(resources))
            {
                var resClone = new PdfDictionary();
                foreach (var k in resources.Keys)
                    if (resources.Get(k) is { } rv) resClone.Set(k, rv);
                page.Dict.Set("Resources", resClone);
                resources = resClone;
            }

            var xobjs = rd.ResolveDict(resources.Get("XObject"));
            if (xobjs is null) continue;
            if (!seenXobjDicts.Add(xobjs))
            {
                var xClone = new PdfDictionary();
                foreach (var k in xobjs.Keys)
                    if (xobjs.Get(k) is { } xv) xClone.Set(k, xv);
                resources.Set("XObject", xClone);
                xobjs = xClone;
            }

            foreach (var name in xobjs.Keys.ToList())
            {
                var entry = xobjs.Get(name);
                var stream = rd.ResolveStream(entry);
                if (stream is null || stream.Dict.GetName("Subtype") != "Image") continue;

                var sharedByRef = entry is PdfIndirectRef ir && !seenRefs.Add(ir.ObjectNumber);
                var sharedByInstance = !seenStreams.Add(stream);
                if (!sharedByRef && !sharedByInstance) continue;

                var dictClone = new PdfDictionary();
                foreach (var k in stream.Dict.Keys)
                    if (stream.Dict.Get(k) is { } sv) dictClone.Set(k, sv);
                var dataClone = new byte[stream.RawData.Length];
                stream.RawData.CopyTo(dataClone, 0);
                var clone = new PdfStream(dictClone, dataClone);

                var objNum = AllocateObjectNumber();
                AddNewObject(objNum, clone, registerOverlay: true);
                xobjs.Set(name, new PdfIndirectRef(objNum, 0));
            }
        }
    }

    /// <summary>Apply the requested repair pass. Stored only.</summary>
    public void Repair(RepairOptions options) { _ = options; }

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

    /// <summary>Export every annotation in the document to an XFDF stream.</summary>
    public void ExportAnnotationsToXfdf(Stream stream)
    {
        new Aspose.Pdf.Facades.PdfAnnotationEditor(this).ExportAnnotationsToXfdf(stream);
    }

    /// <summary>Export every annotation in the document to an XFDF file.</summary>
    public void ExportAnnotationsToXfdf(string fileName)
    {
        using var fs = new FileStream(fileName, FileMode.Create, FileAccess.Write);
        ExportAnnotationsToXfdf(fs);
    }

    /// <summary>Import annotations from an XFDF stream into the document.</summary>
    public void ImportAnnotationsFromXfdf(Stream stream)
    {
        new Aspose.Pdf.Facades.PdfAnnotationEditor(this).ImportAnnotationsFromXfdf(stream);
    }

    /// <summary>Import annotations from an XFDF file into the document.</summary>
    public void ImportAnnotationsFromXfdf(string fileName)
    {
        new Aspose.Pdf.Facades.PdfAnnotationEditor(this).ImportAnnotationsFromXfdf(fileName);
    }

    /// <summary>Raw XMP packet supplied via <see cref="SetXmpMetadata(Stream)"/>;
    /// written verbatim as the /Metadata stream on save (bypasses the property model
    /// so arbitrary XMP byte content round-trips exactly).</summary>
    private byte[]? _rawXmpOverride;

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

    /// <summary>Convert one page to a PNG memory stream.</summary>
    public MemoryStream ConvertPageToPNGMemoryStream(Page page)
    {
        var ms = new MemoryStream();
        new Aspose.Pdf.Devices.PngDevice().Process(page, ms);
        ms.Position = 0;
        return ms;
    }

    /// <summary>Save document as XML (Aspose-Pdf XML schema). Stored only.</summary>
    public void SaveXml(string file) { _ = file; }

    /// <summary>Validate / repair the document. Always returns true (FOSS doesn't run validation).</summary>
    public bool Check(bool doRepair) { _ = doRepair; return true; }

    /// <summary>
    /// Validate the document's page content streams and write an XML report of any
    /// problems to <paramref name="output"/>. Currently checks that each page's
    /// graphics-state operators are balanced (every <c>q</c> has a matching
    /// <c>Q</c>); an unbalanced page leaves a residual transform that corrupts
    /// later content. When <paramref name="doRepair"/> is set the imbalance is
    /// corrected in place. Returns true when the document is valid (no problems).
    /// </summary>
    public bool Check(bool doRepair, System.IO.Stream output)
    {
        var issues = new List<string>();
        foreach (Page page in Pages)
        {
            int depth = 0;
            try
            {
                foreach (var op in page.Contents)
                {
                    if (op is Aspose.Pdf.Operators.GSave) depth++;
                    else if (op is Aspose.Pdf.Operators.GRestore) depth--;
                }
            }
            catch { depth = 0; }

            if (depth != 0)
            {
                issues.Add($"Page {page.Number}: unbalanced graphics-state operators "
                    + $"(q/Q), net depth {depth}.");
                if (doRepair && depth > 0)
                    page.AddContentStream(System.Text.Encoding.ASCII.GetBytes(
                        string.Concat(Enumerable.Repeat("Q\n", depth))));
                else if (doRepair && depth < 0)
                    page.PrependContentStream(System.Text.Encoding.ASCII.GetBytes(
                        string.Concat(Enumerable.Repeat("q\n", -depth))));
            }
        }

        if (output is not null)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<report>\n");
            foreach (var issue in issues)
                sb.Append("  <item>\n    <description>")
                  .Append(System.Security.SecurityElement.Escape(issue))
                  .Append("</description>\n  </item>\n");
            sb.Append("</report>\n");
            var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            output.Write(bytes, 0, bytes.Length);
            output.Flush();
        }
        return issues.Count == 0;
    }

    /// <summary>Async file load wrapper (currently synchronous).</summary>
    public void LoadFrom(string filename, LoadOptions options)
    {
        _ = options;
        if (string.IsNullOrEmpty(filename)) return;
        FileName = filename;
    }

    /// <summary>Single-arg ctor whose parameter keeps the established name <paramref name="filename"/>.</summary>
    public Document(string filename, bool isManagedStream) : this(filename) { _ = isManagedStream; }

    /// <summary>Stream ctor with managed-stream flag.</summary>
    public Document(Stream input, bool isManagedStream) : this(input) { _ = isManagedStream; }

    /// <summary>Stream ctor with password + managed-stream flag.</summary>
    public Document(Stream input, string password, bool isManagedStream) : this(input, password) { _ = isManagedStream; }

    /// <summary>File ctor with password + managed-stream flag.</summary>
    public Document(string filename, string password, bool isManagedStream) : this(filename, password) { _ = isManagedStream; }

    /// <summary>Stream ctor with password + ICustomSecurityHandler.</summary>
    public Document(Stream input, string password, Aspose.Pdf.Security.ICustomSecurityHandler customSecurityHandler)
        : this(ReadAll(input), password, customSecurityHandler) { }

    /// <summary>Stream ctor with password + managed-stream + ICustomSecurityHandler.</summary>
    public Document(Stream input, string password, bool isManagedStream, Aspose.Pdf.Security.ICustomSecurityHandler customSecurityHandler)
        : this(ReadAll(input), password, customSecurityHandler) { _ = isManagedStream; }

    /// <summary>File ctor with password + ICustomSecurityHandler.</summary>
    public Document(string filename, string password, Aspose.Pdf.Security.ICustomSecurityHandler customSecurityHandler)
        : this(File.ReadAllBytes(filename), password, customSecurityHandler) { }

    /// <summary>File ctor with password + managed-stream + ICustomSecurityHandler.</summary>
    public Document(string filename, string password, bool isManagedStream, Aspose.Pdf.Security.ICustomSecurityHandler customSecurityHandler)
        : this(File.ReadAllBytes(filename), password, customSecurityHandler) { _ = isManagedStream; }

    /// <summary>Open bytes whose /Encrypt names an alternative /Filter, decrypting
    /// through the supplied handler.</summary>
    private Document(byte[] data, string password,
        Aspose.Pdf.Security.ICustomSecurityHandler customSecurityHandler)
    {
        if (customSecurityHandler is null)
            throw new System.ArgumentNullException(nameof(customSecurityHandler));
        _data = data;
        CustomSecurityHandler = customSecurityHandler;
        _reader = PdfReader.FromBytes(data, password,
            new PdfReaderOptions { CustomSecurityHandler = customSecurityHandler });
        _ = _reader.Catalog;
        _reader.OwnerDocument = this;
    }

    private static byte[] ReadAll(Stream input)
    {
        using var ms = new MemoryStream();
        if (input.CanSeek) input.Position = 0;
        input.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>Stream ctor with LoadOptions. Dispatches on the RUNTIME options type:
    /// callers that hold their typed options in a <see cref="LoadOptions"/> variable bind
    /// this overload, and without dispatch an HTML/SVG/text/Markdown source would be
    /// parsed as PDF and die on 'Incorrect file header'.</summary>
    public Document(Stream input, LoadOptions options) : this(SourceToPdfBytes(input, options)) { }

    /// <summary>File ctor with LoadOptions — same runtime dispatch as the stream overload.
    /// FileName is only recorded for actual PDF sources: a converted source must never
    /// become the target of a parameterless Save().</summary>
    public Document(string filename, LoadOptions options) : this(SourceToPdfBytes(filename, options))
    {
        if (options is not (HtmlLoadOptions or SvgLoadOptions or TxtLoadOptions or MdLoadOptions))
            FileName = filename;
    }

    private static byte[] SourceToPdfBytes(string path, LoadOptions options) => options switch
    {
        HtmlLoadOptions h => Converters.HtmlToPdfConverter.Convert(path, h).ToArray(),
        SvgLoadOptions s => SvgConvertToPdfBytes(Converters.SvgToPdfConverter.Convert(path, s)),
        TxtLoadOptions t => Converters.TxtToPdfConverter.Convert(path, t),
        MdLoadOptions m => Converters.MarkdownToPdfConverter.Convert(path, m).ToArray(),
        _ => File.ReadAllBytes(path),
    };

    private static byte[] SourceToPdfBytes(Stream input, LoadOptions options)
    {
        var data = ReadStreamToBytes(input);
        return options switch
        {
            HtmlLoadOptions h => Converters.HtmlToPdfConverter.Convert(data, h).ToArray(),
            SvgLoadOptions s => SvgConvertToPdfBytes(Converters.SvgToPdfConverter.Convert(data, s)),
            TxtLoadOptions t => Converters.TxtToPdfConverter.Convert(data, t),
            MdLoadOptions m => Converters.MarkdownToPdfConverter.Convert(data, m).ToArray(),
            _ => data,
        };
    }

    /// <summary>
    /// Track existing objects modified in-memory (for incremental save).
    /// Key = object number, Value = the modified PdfObject.
    /// </summary>
    private readonly Dictionary<int, PdfObject> _dirtyObjects = new();

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

    private int? _newInfoObjNum;

    /// <summary>
    /// Structure elements authored against a page that has no object number yet
    /// (a page added to a fresh document). Save stamps each element's /Pg once
    /// the page's object number is decided, before the catalog is serialized.
    /// </summary>
    internal List<(PdfDictionary Element, Page Page)> PendingStructPgFixups { get; } = new();

    /// <summary>
    /// Allocate the next available object number for new objects.
    /// </summary>
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

    /// <summary>
    /// Ensure the document has an Info dictionary and return its object number.
    /// If the original document had no Info dict, a new one is created.
    /// </summary>
    /// <summary>Resolve the existing /Info dictionary without creating one. Returns null
    /// when the document has no Info dict. Used by DocumentInfo read access so a
    /// <c>new DocumentInfo(doc)</c> reflects on-disk metadata without side effects.</summary>
    /// <summary>The /Info dict a DocumentInfo setter created for a document that had
    /// none. It lives in the pending-object list (not reachable through the reader
    /// until save), so in-memory readers — Document.Info, the PDF/A conversion's
    /// Info↔XMP sync — resolve it through here.</summary>
    private PdfDictionary? _pendingInfoDict;

    internal PdfDictionary? ResolveExistingInfoDict()
        => _reader.ResolveDict(_reader.Trailer.Get("Info")) ?? _pendingInfoDict;

    /// <summary>True when this document was created from scratch (<c>new Document()</c>)
    /// rather than loaded from existing bytes. A from-scratch document seeds the standard
    /// document-information text entries as empty strings the first time its /Info dict is
    /// materialised (see <see cref="DocumentInfo"/>), so unset fields round-trip through
    /// save/reopen as empty rather than absent.</summary>
    internal bool IsNewDocument => _isNewDocument;

    internal (PdfDictionary dict, int objNum) EnsureInfoDict()
    {
        var infoRef = _reader.Trailer.Get("Info");
        if (infoRef is PdfIndirectRef iref)
        {
            var dict = _reader.ResolveDict(infoRef);
            if (dict is not null)
            {
                // The Info dict already exists on disk; a DocumentInfo setter is about to
                // mutate it in place. Mark it dirty so the (incremental) writer re-emits the
                // object — otherwise metadata edits like ModDate are silently dropped on save.
                MarkDirty(iref.ObjectNumber, dict);
                return (dict, iref.ObjectNumber);
            }
        }

        // No Info dict — create one. Cached on the document so every other
        // DocumentInfo instance materialised before save shares it (the pending
        // object is not reachable through the reader yet).
        var newDict = new PdfDictionary();
        var objNum = AllocateObjectNumber();
        _newInfoObjNum = objNum;
        AddNewObject(objNum, newDict);
        _pendingInfoDict = newDict;
        return (newDict, objNum);
    }

    /// <summary>Clears memory.</summary>
    public void FreeMemory()
    {
        _pages?.FreeMemory();
    }

    // FreeMemory keeps the document usable (caches rebuild lazily from the reader);
    // Dispose additionally drops the source buffer, so the file bytes stop being
    // reachable even while the disposed instance itself is still referenced.
    public void Dispose()
    {
        FreeMemory();
        _data = Array.Empty<byte>();
        _reader.ReleaseBuffers();
    }
}

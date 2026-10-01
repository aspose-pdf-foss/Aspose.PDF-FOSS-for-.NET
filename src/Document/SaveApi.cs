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
            if (MustRewriteWhole) RewriteSource(_sourceStream);
            else SaveIncremental(_sourceStream);
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

    /// <summary>Write the whole document over the stream it was opened from, which is left holding
    /// only it (see <see cref="MustRewriteWhole"/>).</summary>
    private void RewriteSource(Stream source)
    {
        using var whole = new MemoryStream();
        Save(whole);
        source.Seek(0, SeekOrigin.Begin);
        whole.Position = 0;
        whole.CopyTo(source);
        source.SetLength(whole.Length);
        source.Flush();
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
                using var zip = new IO.ZipWriter(File.Create(outputFileName));
                for (var i = 1; i <= PageCount; i++)
                    zip.AddEntry(PageFile(i), System.Text.Encoding.UTF8.GetBytes(device.Process(Pages[i])));
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
        PrepareSaveDocument();

        var sv = new SaveDocumentState();
        StampSaveInfo(sv);

        // A RemoveUnusedFonts edit orphaned the replaced fonts' objects (dictionaries,
        // descriptors, /FontFile programs). Recompute reachability so the serializer drops
        // them from the saved file instead of carrying them over — otherwise the file keeps
        // the (now unused) embedded font programs and never shrinks.
        OpenSaveWriter(sv, output);
        // The classic-xref PDF/A-1 output loses the object-stream packing win;
        // recover the size by re-deflating weakly-compressed source streams,
        // as the conversion save does.
        WriteSaveMetadata(sv);

        // Pre-scan the catalog's inline object graph for dictionaries shared between more than
        // one parent (e.g. a generated radio group reached from /AcroForm/Fields and from each
        // option widget's /Parent). These are written once as a shared indirect object so the
        // back-references survive a round-trip instead of being dropped at the write cycle.
        WriteSaveObjects(sv);

        WriteSaveTrailer(sv, output);
    }
}

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
        File.WriteAllBytes(full,
            System.Text.Encoding.UTF8.GetBytes(Converters.HtmlTextFormat.Crlfify(html)));

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
            var bytes = System.Text.Encoding.UTF8.GetBytes(
                Converters.HtmlTextFormat.Crlfify(pageHtmls[h - 1]));
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
        {
            // A caller-supplied images folder is joined the way the CALLER joins it -
            // its resource-saving callback is handed a bare file name that calling code
            // concatenates onto the same folder string, so a trailing separator must not
            // gain a second one and push the file a directory deeper than the caller looks.
            var target = s.IsImage
                ? IO.CallerPaths.AppendName(imagesDir, s.Name)
                : Path.Combine(filesDir, s.Name);
            if (Path.GetDirectoryName(target) is { Length: > 0 } parent)
                Directory.CreateDirectory(parent);
            File.WriteAllBytes(target, s.Content);
        }
    }
}

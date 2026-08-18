using System;
using System.Collections.Generic;
using System.Linq;
using Aspose.Pdf.Core;
using Aspose.Pdf.Optimization;

namespace Aspose.Pdf.Security.HiddenDataSanitization;

/// <summary>
/// Options controlling which categories of hidden or sensitive data
/// <see cref="HiddenDataSanitizer"/> strips from a document.
/// </summary>
public sealed class HiddenDataSanitizationOptions
{
    /// <summary>The default constructor. All flags are off.</summary>
    public HiddenDataSanitizationOptions() { }

    /// <summary>When set (together with <see cref="ConvertPagesToImages"/>), controls
    /// how page-render images are down-sampled/compressed.</summary>
    public ImageCompressionOptions? ImageCompressionOptions { get; set; }

    /// <summary>Rasterise every page to a single background image, discarding all
    /// vector/text content (the most aggressive form of hidden-data removal).</summary>
    public bool ConvertPagesToImages { get; set; }

    /// <summary>DPI used when <see cref="ConvertPagesToImages"/> is set.</summary>
    public int ImageDpi { get; set; } = 150;

    /// <summary>Remove every annotation from every page.</summary>
    public bool RemoveAnnotations { get; set; }

    /// <summary>Remove the document search index and private application data.</summary>
    public bool RemoveSearchIndexAndPrivateInfo { get; set; }

    /// <summary>Flatten interactive form fields into page content.</summary>
    public bool FlattenForms { get; set; }

    /// <summary>Flatten optional-content (OCG) layers into page content.</summary>
    public bool FlattenLayers { get; set; }

    /// <summary>Remove document/page/annotation JavaScript and additional-actions.</summary>
    public bool RemoveJavaScriptsAndActions { get; set; }

    /// <summary>Remove document XMP metadata.</summary>
    public bool RemoveMetadata { get; set; }

    /// <summary>Remove all embedded-file attachments.</summary>
    public bool RemoveAttachments { get; set; }

    /// <summary>An options instance with every removal/flatten flag enabled.</summary>
    public static HiddenDataSanitizationOptions All() => new()
    {
        RemoveAnnotations = true,
        RemoveSearchIndexAndPrivateInfo = true,
        FlattenForms = true,
        FlattenLayers = true,
        RemoveJavaScriptsAndActions = true,
        RemoveMetadata = true,
        RemoveAttachments = true,
    };
}

/// <summary>
/// Strips hidden or sensitive data (annotations, JavaScript/actions, attachments,
/// optional-content layers, form fields, metadata) from a document per the supplied
/// <see cref="HiddenDataSanitizationOptions"/>.
/// </summary>
public sealed class HiddenDataSanitizer
{
    private readonly HiddenDataSanitizationOptions _options;

    /// <summary>Create a sanitizer with default (all-off) options.</summary>
    public HiddenDataSanitizer() => _options = new HiddenDataSanitizationOptions();

    /// <summary>Create a sanitizer with the supplied options.</summary>
    public HiddenDataSanitizer(HiddenDataSanitizationOptions options)
        => _options = options ?? new HiddenDataSanitizationOptions();

    /// <summary>
    /// Rasterise every page of <paramref name="document"/> to a background image,
    /// discarding all original page content. Convenience for the
    /// <see cref="HiddenDataSanitizationOptions.ConvertPagesToImages"/> mode.
    /// </summary>
    public static void SanitizeAllToImages(Document document, int dpi = 150)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        new HiddenDataSanitizer(new HiddenDataSanitizationOptions
        {
            ConvertPagesToImages = true,
            ImageDpi = dpi,
        }).Sanitize(document);
    }

    /// <summary>Apply the configured sanitization to <paramref name="document"/> in place.</summary>
    public void Sanitize(Document document)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));

        if (_options.FlattenForms) FlattenForms(document);
        if (_options.FlattenLayers) FlattenLayers(document);
        if (_options.RemoveJavaScriptsAndActions) RemoveJavaScriptsAndActions(document);
        if (_options.RemoveAttachments) RemoveAttachments(document);
        if (_options.RemoveAnnotations) RemoveAnnotations(document);
        if (_options.RemoveMetadata) RemoveMetadata(document);
        if (_options.RemoveSearchIndexAndPrivateInfo) RemoveSearchIndexAndPrivateInfo(document);

        if (_options.ConvertPagesToImages) ConvertPagesToImages(document);
        else if (_options.ImageCompressionOptions is { } ico) CompressImages(document, ico);
    }

    /// <summary>Rasterize every page to a single image and replace the page content with a
    /// draw of that image, discarding all other page resources (fonts, XObjects, hidden
    /// text/vector data). Optional <see cref="HiddenDataSanitizationOptions.ImageCompressionOptions"/>
    /// downsamples the rasterized image afterwards.</summary>
    private void ConvertPagesToImages(Document document)
    {
        var reader = document.Reader;
        var dpi = _options.ImageDpi > 0 ? _options.ImageDpi : 150;
        foreach (var page in document.Pages)
        {
            var media = page.MediaBox;
            double w = media.URX - media.LLX;
            double h = media.URY - media.LLY;
            if (w <= 0 || h <= 0) continue;

            // Rasterize the page to a PNG at the requested DPI.
            byte[] png;
            using (var ms = new System.IO.MemoryStream())
            {
                new Aspose.Pdf.Devices.PngDevice(new Aspose.Pdf.Devices.Resolution(dpi)).Process(page, ms);
                png = ms.ToArray();
            }

            // Embed the raster as an image XObject and keep ONLY it in the page resources.
            var name = page.Resources.Images.Add(new System.IO.MemoryStream(png));
            var res = reader.ResolveDict(page.Dict.Get("Resources"));
            var xobj = res is null ? null : reader.ResolveDict(res.Get("XObject"));
            var newRes = new PdfDictionary();
            if (xobj is not null && xobj.Get(name) is { } imgRef)
            {
                var keep = new PdfDictionary();
                keep.Set(name, imgRef);
                newRes.Set("XObject", keep);
            }
            page.Dict.Set("Resources", newRes);
            page.Dict.Remove("Annots");

            // Replace the content with a single full-page image draw, tagged as an
            // /Artifact and doubly q/Q-nested (8 operators: BDC q q cm Do Q Q EMC).
            var content = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            sb.Append("/Artifact BDC\n");
            sb.Append("q\n");
            sb.Append("q\n");
            sb.AppendFormat(content, "{0:F2} 0 0 {1:F2} {2:F2} {3:F2} cm\n", w, h, media.LLX, media.LLY);
            sb.AppendFormat(content, "/{0} Do\n", name);
            sb.Append("Q\n");
            sb.Append("Q\n");
            sb.Append("EMC\n");
            page.SetContentStream(System.Text.Encoding.ASCII.GetBytes(sb.ToString()));
        }

        if (_options.ImageCompressionOptions is { } ico)
            CompressImages(document, ico);
    }

    /// <summary>Downsample and/or recompress the document's existing images per the supplied
    /// <see cref="ImageCompressionOptions"/> (used when pages are NOT rasterized).</summary>
    private static void CompressImages(Document document, ImageCompressionOptions ico)
    {
        var reader = document.Reader;
        if (ico.MaxResolution > 0)
            Aspose.Pdf.Optimization.ImageCompressor.DownsampleImages(reader, ico.MaxResolution, ico.ImageQuality);
        if (ico.CompressImages)
            Aspose.Pdf.Optimization.ImageCompressor.CompressImages(reader, ico.ImageQuality);
    }

    private static void RemoveAnnotations(Document document)
    {
        foreach (var page in document.Pages)
            page.Annotations.Delete();
    }

    private static void FlattenLayers(Document document)
    {
        foreach (var page in document.Pages)
        {
            // Snapshot: Flatten() marks each Layer removed and the list is purged on
            // the next access, so iterate a copy.
            foreach (var layer in page.Layers.ToList())
                layer.Flatten(cleanupContentStream: true);
        }
    }

    private static void FlattenForms(Document document)
    {
        if (document.Form.Count > 0)
            document.Form.Flatten();
    }

    private static void RemoveJavaScriptsAndActions(Document document)
    {
        var reader = document.Reader;
        var catalog = document.Catalog;

        // Document-level open action + additional actions.
        catalog.Remove("OpenAction");
        catalog.Remove("AA");

        // Document-level named JavaScript (/Names /JavaScript).
        if (reader.ResolveDict(catalog.Get("Names")) is PdfDictionary names)
            names.Remove("JavaScript");

        foreach (var page in document.Pages)
        {
            // Page additional actions.
            page.Dict.Remove("AA");

            // Annotation actions (/A action + /AA additional actions).
            if (reader.Resolve(page.Dict.Get("Annots")) is PdfArray annots)
            {
                foreach (var item in annots)
                {
                    if (reader.ResolveDict(item) is not PdfDictionary annot) continue;
                    annot.Remove("A");
                    annot.Remove("AA");
                }
            }
        }
    }

    private static void RemoveAttachments(Document document)
    {
        var reader = document.Reader;
        var catalog = document.Catalog;

        // Name-tree attachments (/Names /EmbeddedFiles) and catalog associated files.
        if (reader.ResolveDict(catalog.Get("Names")) is PdfDictionary names)
            names.Remove("EmbeddedFiles");
        catalog.Remove("AF");

        // FileAttachment annotations and page-level associated files.
        foreach (var page in document.Pages)
        {
            page.Dict.Remove("AF");
            if (reader.Resolve(page.Dict.Get("Annots")) is not PdfArray annots) continue;
            var kept = new PdfArray();
            foreach (var item in annots)
            {
                if (reader.ResolveDict(item) is PdfDictionary annot
                    && annot.GetName("Subtype") == "FileAttachment")
                    continue;
                kept.Add(item);
            }
            page.Dict.Set("Annots", kept);
        }
    }

    private static void RemoveMetadata(Document document)
        => document.Catalog.Remove("Metadata");

    private static void RemoveSearchIndexAndPrivateInfo(Document document)
    {
        var catalog = document.Catalog;
        // Full-text search index and PieceInfo private application data.
        catalog.Remove("PieceInfo");
        catalog.Remove("SpiderInfo");
    }
}

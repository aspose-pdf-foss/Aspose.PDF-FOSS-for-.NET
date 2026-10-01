using Aspose.Pdf.Core;
using Aspose.Pdf.Devices;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>
    /// PDF/A-1 transparency simulation. PDF/A-1 forbids
    /// transparency, and plain neutralisation (alpha → 1, blend → Normal)
    /// changes what the reader sees: a 50%-alpha fill turns opaque and hides
    /// the backdrop, a Multiply highlight turns into an opaque bar that hides
    /// the text under it. This pass preserves the appearance instead: it
    /// rasterises each transparency-using region from the original page and
    /// paints the composite as an opaque image on top, rewrites the
    /// transparent paint operators to no-ops, and flattens Highlight
    /// annotations (whose appearance streams blend with Multiply) into the
    /// content before deleting them. Concretely:
    ///   1. scan the page content for paints under 0 &lt; alpha &lt; 1 or a
    ///      non-Normal blend mode, collecting their device-space regions and a
    ///      rewrite that turns those paints into <c>n</c>;
    ///   2. flatten each Highlight annotation's /AP form into the content at
    ///      its /Rect and delete the annotation, adding the rect as a region;
    ///   3. render the page (original paints + flattened highlights) at
    ///      300 dpi, crop each region and append it as an opaque image drawn
    ///      over the neutralised content.
    /// Runs for the Default and Mask transparency actions; Mask additionally
    /// keeps its dedicated constant-alpha-image handling (this pass rewrites
    /// only path and text paints, never image Do).
    /// </summary>
    private void SimulateTransparencyRegions(Page page, bool recolorConstantAlpha = false)
    {
        var ts = new TransparencyRegionState();
        ts.page = page;
        ts.recolorConstantAlpha = recolorConstantAlpha;
        ts.resources = _reader.ResolveDict(ts.page.Dict.Get("Resources"));
        ts.content = ts.page.GetContentStreamBytes();
        if (ts.content is not { Length: > 0 }) return;

        ts.contentRegions = new List<double[]>();
        ts.formRewrites = new List<(PdfStream form, byte[] bytes)>();
        ts.rewritten = null;
        if (ts.resources is not null)
            ts.rewritten = ScanTransparentPaints(ts.content, ts.resources, ts.contentRegions, ts.formRewrites,
                ts.recolorConstantAlpha);

        if (!ApplyRecolourOnlyRewrites(ts)) return;

        ts.regions = new List<(double[] Box, double[]? MulColor)>();

        ts.flattenOps = new System.Text.StringBuilder();
        ts.annotIndices = new List<int>();
        ts.annotsArr = _reader.Resolve(ts.page.Dict.Get("Annots")) as PdfArray;
        CollectTransparentAnnotations(ts);

        if (ts.contentRegions.Count == 0 && ts.regions.Count == 0) return;

        CollectTransparencyRegions(ts);

        ts.savedAnnots = ts.page.Dict.Get("Annots");
        if (ts.savedAnnots is not null) ts.page.Dict.Remove("Annots");
        // The renderers clear the reader's resolved-object cache when done, which
        // would discard every in-memory edit the conversion has made to resolved
        // objects (metadata, fonts, the structure tree). Keep the cache alive for
        // this in-conversion render.
        _reader.SuppressCacheClear = true;
        try
        {
            // 150 dpi: the composites are consumed at raster-compare resolution;
            // half the nominal 300 dpi keeps the mid-conversion render cheap.
            using var ms = new MemoryStream();
            new PngDevice(new Resolution(150)).Process(ts.page, ms);
            (ts.pixels, ts.pngW, ts.pngH, ts.hasAlpha) = Facades.PdfFileMend.DecodePng(ms.ToArray());
        }
        catch
        {
            // Rendering unavailable: leave the page (and its annotations)
            // untouched; the regular neutralisation still applies.
            return;
        }
        finally
        {
            _reader.SuppressCacheClear = false;
            if (ts.savedAnnots is not null) ts.page.Dict.Set("Annots", ts.savedAnnots);
        }

        ts.pageRect = ts.page.GetPageRect(considerRotation: false);
        if (ts.pageRect.Width <= 0 || ts.pageRect.Height <= 0 || ts.pngW <= 0 || ts.pngH <= 0) return;

        ApplyTransparentFormRewrites(ts);

        ts.drawOps = new System.Text.StringBuilder();
        CompositeTransparencyRegions(ts);

        ts.baseBytes = ts.rewritten ?? ts.content;
        ts.tail = "\n" + ts.flattenOps + ts.drawOps;
        ts.page.SetContentStream(Combine(ts.baseBytes, Compat.Latin1.GetBytes(ts.tail)));

        if (ts.annotIndices.Count > 0)
        {
            // Bind the resolved array into the live page dict so the removal
            // survives a reader cache clear.
            ts.page.Dict.Set("Annots", ts.annotsArr!);
            RemoveAnnotations(ts.page, ts.annotsArr!, ts.annotIndices);
        }
    }
}

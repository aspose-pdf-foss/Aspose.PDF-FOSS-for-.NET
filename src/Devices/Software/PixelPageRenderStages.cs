using System.Runtime.InteropServices;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Devices.Rasterizer;
using Aspose.Pdf.IO;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Devices;

public sealed partial class SoftwarePageRenderer : IPageRenderer
{
    /// <summary>The stages of the pixel-size page render: the rotation fit, the pinned-aspect reconcile and the content render.</summary>
    private void RenderPixelPageContent(PixelPageRenderState px)
    {
        var ctx = new RenderContext(px.pixels, px.pixelW, px.pixelH, px.scale, px.effectiveMb, px.reader)
        {
            ConvertFontsToUnicodeTtf = ConvertFontsToUnicodeTtf,
            PdfXOverprintSim = HasPdfXOutputIntent(px.reader),
        };

        px.resources = ResolveInheritedPageResources(px.page.Dict, px.reader);
        px.extGStates = ResolveExtGStates(px.resources, px.reader);
        px.fontDicts = ResolveFontDicts(px.resources, px.reader);
        px.allXObjects = ResolveAllXObjects(px.resources, px.reader);

        ctx.PageCtm = px.initialPageCtm;
        ctx.AllXObjects = px.allXObjects;
        ctx.FontDicts = px.fontDicts;
        // /Pattern entry in page resources holds colour-pattern dicts (tiling or shading).
        // Cached on the context so DrawPath can resolve "/Pn scn" in O(1) without re-walking
        // the resources tree per fill.
        ctx.Patterns = px.reader.ResolveDict(px.resources?.Get("Pattern"));
        // /Shading entry is a sibling of /Pattern and feeds the `sh` operator directly
        // (PDF 32000 §8.7.4.5). Stored on the context so OnShadingPainted can resolve
        // names without re-walking the resources tree.
        ctx.Shadings = px.reader.ResolveDict(px.resources?.Get("Shading"));
        // /ColorSpace entry: dictionary of named Separation/DeviceN/etc. spaces
        // that `cs`/`CS` operators reference. The parser consumes this to
        // pre-resolve tint transforms (Pantone spot colours, etc.) so `scn`
        // produces real RGB instead of falling through to the gray default.
        ctx.ColorSpaces = px.reader.ResolveDict(px.resources?.Get("ColorSpace"));
        // /Properties is where named BDC props live (e.g. /OC /MC0 BDC →
        // resources./Properties/MC0 → OCG dict). Needed alongside the
        // /OCProperties OFF set so the renderer can skip hidden layers.
        ctx.Properties = px.reader.ResolveDict(px.resources?.Get("Properties"));
        ctx.OcgHidden = ResolveHiddenOcgs(px.reader);

        px.contentBytes = GetPageContent(px.page.Dict, px.reader);
        RenderContent(px.contentBytes, ctx, px.extGStates, initialCtm: px.initialPageCtm);

        // Annotations are painted *after* the page content (PDF 32000-1:2008 §12.5):
        // Highlight annotations use Multiply blending so underlying text shows through.
        DrawAnnotations(ctx, px.page.Dict);

        // Clear the objects this render resolved, to prevent memory growth when
        // rendering many pages sequentially.
        px.reader.ClearCacheExcept(px.cachedBefore);

    }

    /// <summary></summary>
    private void ReconcilePinnedAspect(PixelPageRenderState px)
    {
        if (px.scale > 0 && Math.Abs(px.yFit / px.scale - 1.0) > PinnedAspectTolerance)
        {
            var k = px.yFit / px.scale;
            var lly = px.effectiveMb.LLY;
            var stretch = new[] { 1.0, 0.0, 0.0, k, 0.0, lly * (1 - k) };
            px.initialPageCtm = px.initialPageCtm is null
                ? stretch
                : GraphicsState.MultiplyMatrices(px.initialPageCtm, stretch);
            px.effectiveMb = new Aspose.Pdf.Rectangle(px.effectiveMb.LLX, lly,
                px.effectiveMb.URX, lly + px.effectiveMb.Height * k);
        }
    }

    /// <summary></summary>
    private void FitPixelPageRotation(PixelPageRenderState px)
    {
        if (px.rot == 90 || px.rot == 180 || px.rot == 270)
        {
            // The rotation swings the CROP rectangle (the visible region), not the
            // media box — a crop offset from the media origin must rotate with the
            // content, and the canvas edge the content lands against is the crop's,
            // so both the dimensions AND the lower-left offset below come from crop.
            // (Anchoring on the media box shifted a 270°-rotated cropped page by the
            // media/crop height difference.)
            var w = px.crop.Width;
            var h = px.crop.Height;
            // Rotated bounding box: 90/270 swap dimensions, 180 keeps them.
            px.effectiveMb = px.rot == 180
                ? new Aspose.Pdf.Rectangle(0, 0, w, h)
                : new Aspose.Pdf.Rectangle(0, 0, h, w);
            // Initial CTM = clockwise rotation of the unrotated content into
            // the rotated canvas's coord frame. PDF 32000 §14.8.2.7 says /Rotate
            // is the *clockwise* angle the page is shown at, so the content's
            // crop-frame corners need to swing CW into the visible canvas:
            //   Rotate=90 maps crop (LLX,LLY) → visible (0,w)   [top-left]
            //   Rotate=180 maps crop (LLX,LLY) → visible (w,h)  [top-right]
            //   Rotate=270 maps crop (LLX,LLY) → visible (h,0)  [bottom-right]
            px.initialPageCtm = px.rot switch
            {
                90 => new[] { 0.0, -1.0, 1.0, 0.0, -px.crop.LLY, w + px.crop.LLX },
                180 => new[] { -1.0, 0.0, 0.0, -1.0, w + px.crop.LLX, h + px.crop.LLY },
                270 => new[] { 0.0, 1.0, -1.0, 0.0, h + px.crop.LLY, -px.crop.LLX },
                _ => null,
            };
        }
        else
        {
            // Unrotated: the device box is the crop rectangle. Its lower-left maps to
            // the bottom-left pixel, so cropped content is positioned correctly and the
            // area outside the crop box falls off the (crop-sized) canvas.
            px.effectiveMb = px.crop;
        }
    }
}

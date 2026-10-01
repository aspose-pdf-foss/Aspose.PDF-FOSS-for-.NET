using System.Runtime.InteropServices;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Devices.Rasterizer;
using Aspose.Pdf.IO;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Devices;

/// <summary>
/// Built-in software PDF page renderer. No external dependencies.
/// Renders text placeholders, images (JPEG/raw), and vector paths
/// to an RGBA pixel buffer using a pure .NET software rasterizer.
/// </summary>
public sealed partial class SoftwarePageRenderer : IPageRenderer
{
    /// <summary>
    /// Render a PDF page to RGBA pixels (IPageRenderer interface — re-parses the PDF).
    /// Prefer the internal overload that takes a Page directly for memory efficiency.
    /// </summary>
    public RgbaBuffer RenderPage(byte[] pdfBytes, int pageNumber, int dpi)
    {
        using var doc = Document.Open(pdfBytes);
        return RenderPage(doc.Pages.At(pageNumber), dpi);
    }

    /// <summary>
    /// Render a PDF page to RGBA pixels using the page's existing PdfReader.
    /// Avoids re-parsing the PDF, keeping memory bounded for multi-page rendering.
    /// </summary>
    internal RgbaBuffer RenderPage(Page page, int dpi) => RenderPage(page, dpi, dpi);

    /// <summary>
    /// Render a PDF page with independent X/Y resolutions, so callers that set
    /// <c>Resolution(75, 100)</c> get a non-square pixel grid. Width and height are
    /// computed independently — height ignored Y resolution before this overload existed.
    /// </summary>
    internal RgbaBuffer RenderPage(Page page, int xDpi, int yDpi)
    {
        // Ceiling overshoots integer-point pages by 1px because of FP rounding
        // (e.g. 792 * 150/72 evaluates to 1650.0000000002 in IEEE 754). Snap to
        // the nearest integer when we're within a pixel fraction of it — the
        // GDI+ renderer does the same, and an off-by-one pixel dimension is
        // a visible defect.
        // Size the image to the crop box (clipped to the media box), not the media
        // box: GDI+ presents only the cropped region. Rotation
        // swaps the visible dimensions exactly as it does for the media box.
        // Live-document render: unsaved Contents edits must be visible to the
        // stream read below, as a live document is expected to render them.
        page.FlushPendingContents();
        var crop = EffectiveCropRect(page);
        var rot = ((page.RotateDegrees % 360) + 360) % 360;
        var visW = (rot == 90 || rot == 270) ? crop.Height : crop.Width;
        var visH = (rot == 90 || rot == 270) ? crop.Width : crop.Height;
        var pixelW = PagePixels(visW, xDpi);
        var pixelH = PagePixels(visH, yDpi);
        // Hand the REQUESTED resolution down rather than letting the canvas imply it.
        // PagePixels TRUNCATES, so re-deriving the scale as pixelW/width shrinks the page
        // by up to a whole pixel across its width - and since the error grows with x, the
        // content drifts steadily left of where the GDI+ renderer puts it. Measured on an A4
        // landscape page at 150 dpi: a uniform one-pixel horizontal disagreement between the
        // two rasterisers, over the whole page. GDI+ has passed the true scale here for a
        // while; this is the software half of that.
        return RenderPageAtPixelSize(page, pixelW, pixelH, xDpi / 72.0, yDpi / 72.0);
    }

    /// <summary>How far the requested canvas's vertical fit may drift from its horizontal one
    /// before the page is treated as PINNED to a non-proportional size and stretched to fill it.
    /// A size derived from the page at a DPI lands within a pixel or two of the page's own aspect;
    /// a deliberately pinned one (1000x2000 for a letter page) is off by tens of per cent.</summary>
    private const double PinnedAspectTolerance = 0.01;

    /// <summary>
    /// Render a PDF page directly at the requested pixel dimensions (no resample).
    /// Preserves the AA scanline filler's fractional coverage on thin strokes — a
    /// render-at-high-DPI-then-downsample detour smears those coverage values back
    /// toward binary when neighbouring source rows differ, which is how 50%-grey
    /// page-frame edges used to be lost.
    /// </summary>
    /// <param name="page">The page to render.</param>
    /// <param name="pixelW">Width of the output canvas in pixels.</param>
    /// <param name="pixelH">Height of the output canvas in pixels.</param>
    /// <param name="xScale">Device pixels per PDF point horizontally. Null means derive it
    /// from the canvas, which is what a caller that PINNED a pixel size wants; a caller that
    /// asked for a DPI passes the true scale so the truncated canvas does not shrink the page.</param>
    /// <param name="yScale">The vertical counterpart; see <paramref name="xScale"/>.</param>
    internal RgbaBuffer RenderPageAtPixelSize(Page page, int pixelW, int pixelH, double? xScale = null, double? yScale = null)
    {
        var px = new PixelPageRenderState();
        px.page = page;
        px.pixelW = pixelW;
        px.pixelH = pixelH;
        px.xScale = xScale;
        px.yScale = yScale;
        if (px.pixelW <= 0) px.pixelW = 1;
        if (px.pixelH <= 0) px.pixelH = 1;

        px.reader = px.page.Reader;
        px.cachedBefore = px.reader.CachedObjects();
        px.rawMb = px.page.MediaBox;
        px.crop = EffectiveCropRect(px.page);

        px.rot = ((px.page.RotateDegrees % 360) + 360) % 360;
        px.initialPageCtm = null;
        FitPixelPageRotation(px);

        px.scale = px.xScale ?? px.pixelW / px.effectiveMb.Width;

        px.yFit = px.yScale ?? px.pixelH / px.effectiveMb.Height;
        ReconcilePinnedAspect(px);

        px.pixels = new byte[px.pixelW * px.pixelH * 4];

        // Fill with white background - unless the caller asked for bare paper to stay
        // transparent, when the buffer starts (0, 0, 0, 0) as the GDI+ canvas does: the
        // compositor weights a backdrop by its own alpha, so content lands with its true
        // coverage and nothing is flattened onto white afterwards.
        for (int i = 0; i < px.pixels.Length && !TransparentBackground; i += 4)
        {
            px.pixels[i] = 255;     // R
            px.pixels[i + 1] = 255; // G
            px.pixels[i + 2] = 255; // B
            px.pixels[i + 3] = 255; // A
        }

        RenderPixelPageContent(px);
        return new RgbaBuffer(px.pixels, px.pixelW, px.pixelH);
    }

    /// <summary>
    /// Resolve the OCGs that the document's default Optional Content
    /// configuration marks as invisible. PDF 32000 §8.11.4.3: start from
    /// <c>/D/BaseState</c> (default ON), then apply <c>/OFF</c>; the result
    /// is the set of OCGs that should render as if absent.
    /// </summary>
    internal static HashSet<PdfDictionary>? ResolveHiddenOcgs(IO.PdfReader reader)
    {
        var ocProps = reader.ResolveDict(reader.Catalog.Get("OCProperties"));
        if (ocProps is null) return null;
        var dConfig = reader.ResolveDict(ocProps.Get("D"));
        if (dConfig is null) return null;

        var hidden = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);

        // BaseState — when explicitly "OFF" or "Unchanged", we'd need to seed
        // every OCG into `hidden` and then remove the /ON entries. Default ON
        // is the common case and the only one we handle for now; conservative
        // because mis-hiding active content would do more visible damage than
        // mis-showing a Design overlay.
        var baseState = dConfig.GetName("BaseState");

        if (baseState == "OFF" || baseState == "Unchanged")
        {
            var ocgs = reader.Resolve(ocProps.Get("OCGs")) as PdfArray;
            if (ocgs is not null)
            {
                for (int i = 0; i < ocgs.Count; i++)
                {
                    var ocg = reader.ResolveDict(ocgs[i]);
                    if (ocg is not null) hidden.Add(ocg);
                }
            }
        }

        if (reader.Resolve(dConfig.Get("OFF")) is PdfArray off)
        {
            for (int i = 0; i < off.Count; i++)
            {
                var ocg = reader.ResolveDict(off[i]);
                if (ocg is not null) hidden.Add(ocg);
            }
        }

        if (reader.Resolve(dConfig.Get("ON")) is PdfArray on)
        {
            for (int i = 0; i < on.Count; i++)
            {
                var ocg = reader.ResolveDict(on[i]);
                if (ocg is not null) hidden.Remove(ocg);
            }
        }

        // Usage auto-states (PDF 32000 §8.11.4.3 step c): each /AS entry applies its
        // member OCGs' own /Usage state for the given event. Rendering acts as a
        // viewer, so the /View event's /ViewState governs — an OCG whose usage says
        // /ViewState /OFF is hidden even though the base ON/OFF arrays leave it on
        // (the idiom scanner exports use for print-only /Background layers).
        if (reader.Resolve(dConfig.Get("AS")) is PdfArray asArr)
        {
            for (int i = 0; i < asArr.Count; i++)
            {
                var usage = reader.ResolveDict(asArr[i]);
                if (usage is null || usage.GetName("Event") != "View") continue;
                if (reader.Resolve(usage.Get("OCGs")) is not PdfArray members) continue;
                for (int j = 0; j < members.Count; j++)
                {
                    var ocg = reader.ResolveDict(members[j]);
                    if (ocg is null) continue;
                    var view = reader.ResolveDict(reader.ResolveDict(ocg.Get("Usage"))?.Get("View"));
                    var state = view?.GetName("ViewState");
                    if (state == "OFF") hidden.Add(ocg);
                    else if (state == "ON") hidden.Remove(ocg);
                }
            }
        }

        return hidden.Count == 0 ? null : hidden;
    }

    /// <summary>
    /// Evaluate an /OC entry (on an image/form XObject or marked-content sequence) against
    /// the document's default configuration and report whether the content is hidden.
    /// Handles a plain OCG (hidden iff in the OFF set) and an OCMD whose /P policy
    /// (AnyOn default, AllOn, AnyOff, AllOff) combines its member OCGs (PDF 32000 §8.11.2.3).
    /// </summary>
    internal static bool IsOcHidden(PdfObject? ocRef, IO.PdfReader reader, HashSet<PdfDictionary>? ocgHidden)
    {
        var oc = reader.ResolveDict(ocRef);
        if (oc is null) return false;
        bool IsOff(PdfDictionary g) => ocgHidden is not null && ocgHidden.Contains(g);

        if (oc.GetName("Type") == "OCMD")
        {
            var groups = new List<PdfDictionary>();
            if (reader.Resolve(oc.Get("OCGs")) is PdfArray arr)
            {
                foreach (var e in arr)
                    if (reader.ResolveDict(e) is { } g) groups.Add(g);
            }
            else if (reader.ResolveDict(oc.Get("OCGs")) is { } single)
            {
                groups.Add(single);
            }
            if (groups.Count == 0) return false;

            var visible = oc.GetName("P") switch
            {
                "AllOn" => groups.TrueForAll(g => !IsOff(g)),
                "AnyOff" => groups.Exists(IsOff),
                "AllOff" => groups.TrueForAll(IsOff),
                _ => groups.Exists(g => !IsOff(g)), // AnyOn (default)
            };
            return !visible;
        }

        return IsOff(oc);
    }

    /// <summary>
    /// Page dimension (points) → output pixel count at the given DPI: the page
    /// dimension is narrowed to float32, the dpi/72 zoom is applied in DOUBLE
    /// precision, and the product truncates toward zero. 845.04 pt at 300 dpi is
    /// exactly 3521 in decimal, but its float32 form dips to 845.039978 → 3520.9999
    /// → 3520 pixels. An all-float32 chain (dividing by 72f in single precision)
    /// double-rounds and diverges on other sizes, so the zoom must stay double.
    /// Any rounding or "nearly integer" snap-up here produces off-by-one canvases.
    /// </summary>
    internal static int PagePixels(double points, double dpi)
    {
        return (int)((double)(float)points * dpi / 72.0);
    }

    /// <summary>
    /// The region of the page that is rasterised to the output image: the crop box
    /// clipped to the media box (PDF 32000 §14.11.2). When the page declares no crop
    /// box this equals the media box. Image-export devices size the canvas to this
    /// rectangle and offset content by its lower-left corner, so anything outside the
    /// crop area falls off the canvas — matching GDI+, which presents
    /// only the cropped region.
    /// </summary>
    internal static Aspose.Pdf.Rectangle EffectiveCropRect(Page page)
    {
        var mb = page.MediaBox;
        var cb = page.CropBox;
        if (cb is null) return mb;
        // §14.11.2 intersection — measured on the CURRENT expected renders
        // with three crop/media overlap shapes (partial overlap, crop ⊃ media,
        // crop ⊂ media): it intersects in every case. (A vintage template of the
        // rotated-pages fixture shows a raw-crop canvas, but the current expected
        // render of that fixture is the intersection too — the raw-crop
        // reading was a template-era artifact, and following it regressed ten
        // baseline renders.)
        var llx = Math.Max(mb.LLX, cb.LLX);
        var lly = Math.Max(mb.LLY, cb.LLY);
        var urx = Math.Min(mb.URX, cb.URX);
        var ury = Math.Min(mb.URY, cb.URY);
        // Non-overlapping or degenerate crop → fall back to the full media box.
        if (urx <= llx || ury <= lly) return mb;
        return new Aspose.Pdf.Rectangle(llx, lly, urx, ury);
    }

    private static void RenderContent(byte[] contentBytes, RenderContext ctx,
        Dictionary<string, PdfDictionary>? extGStates, double[]? initialCtm = null,
        byte[]? initialClipMask = null, PdfDictionary? colorSpaces = null,
        GraphicsState? inheritState = null)
    {
        var parser = new ContentStreamParser(ctx.Reader);
        // Content run on behalf of another state starts from it: a Type 3 glyph that
        // opens with `d1` describes a shape and no colour and is painted in the colour
        // the text is set in (PDF 32000 §9.6.5), at the text's alphas and blend mode -
        // the same fields the GDI+ renderer hands its char procs.
        if (inheritState is not null)
        {
            var st = parser.State;
            st.FillAlpha = inheritState.FillAlpha;
            st.StrokeAlpha = inheritState.StrokeAlpha;
            st.BlendMode = inheritState.BlendMode;
            st.FillR = inheritState.FillR; st.FillG = inheritState.FillG; st.FillB = inheritState.FillB;
            st.StrokeR = inheritState.StrokeR; st.StrokeG = inheritState.StrokeG; st.StrokeB = inheritState.StrokeB;
        }

        parser.OnTextShown += (text, rawBytes, state) =>
        {
            if (ctx.IsContentHidden) return;
            ctx.ClipMask = state.ClipMask;
            DrawText(ctx, text, rawBytes, state);
        };

        parser.OnImageDrawn += (name, state) =>
        {
            if (ctx.IsContentHidden) return;
            ctx.ClipMask = state.ClipMask;
            DrawXObject(ctx, name, state, extGStates);
        };

        parser.OnPathPainted += (op, state, segments) =>
        {
            if (ctx.IsContentHidden) return;
            // Sync the rendering-time clip cache from the graphics-state stack before
            // every paint so any `Q` that restored an outer clip takes effect here.
            ctx.ClipMask = state.ClipMask;
            DrawPath(ctx, segments, op, state);
        };

        parser.OnPathClipped += (evenOdd, state, segments) =>
        {
            // Always install clips, even inside hidden OCGs — clip state needs
            // to round-trip through Q/W/W*/Q regardless of drawing.
            InstallClipFromPath(ctx, segments, state, evenOdd);
        };

        parser.OnShadingPainted += (name, state) =>
        {
            if (ctx.IsContentHidden) return;
            ctx.ClipMask = state.ClipMask;
            DrawShading(ctx, name, state);
        };

        parser.OnInlineImage += (dict, data) =>
        {
            if (ctx.IsContentHidden) return;
            // Inline images (BI…ID…EI) appear most often inside Type 3 font CharProc
            // streams (each glyph is a tiny ImageMask). The parser fires this event
            // synchronously while parsing the content stream, so parser.State.Ctm
            // here is the CTM at the BI operator — which for Type 3 glyphs is the
            // glyph's effective transform (FontMatrix × text rendering matrix).
            ctx.ClipMask = parser.State.ClipMask;
            DrawInlineImage(ctx, dict, data, parser.State);
        };

        // A text clip becomes effective at ET and lasts until the enclosing Q, exactly like
        // a W-installed path clip - so it is written onto the graphics state, which q/Q
        // already save and restore. BT clears any half-collected shape from a text object
        // that never reached its ET.
        parser.OnOperator += (op, _, state) =>
        {
            if (op == "BT") { ctx.TextClipAccum = null; ctx.TextClipPaints = false; }
            else if (op == "ET") EndTextClip(ctx, state);
        };

        parser.OnMarkedContentBegin += (tag, props) =>
        {
            // PDF 32000 §8.11.4.4: a marked-content range tagged /OC with a
            // properties dict that is one of the OCGs the /D config flagged
            // OFF must render as if its content weren't there. Other tags
            // (e.g. /PlacedGraphic /MCn — accessibility/struct-tree) never
            // gate visibility. Push a single bool per BMC/BDC so EMC pops
            // the right depth either way.
            var hideThis = tag == "OC"
                && props is not null
                && ctx.OcgHidden is not null
                && ctx.OcgHidden.Contains(props);
            ctx.OcgHiddenStack.Push(hideThis);
        };

        parser.OnMarkedContentEnd += () =>
        {
            if (ctx.OcgHiddenStack.Count > 0) ctx.OcgHiddenStack.Pop();
        };

        // Used by tiling-pattern fill to pre-seed the CTM with the pattern's transformed
        // CTM so that the pattern's own `cm` operators concat correctly on top of it.
        if (initialCtm is not null) parser.State.Ctm = (double[])initialCtm.Clone();
        // Form XObjects inherit the caller's clip: PDF 32000 §8.10 wraps the Do in an
        // implicit q…Q, which means any ClipMask installed by a preceding W/W* is active
        // inside the form. Without this, strokes inside a form leak past the clip region.
        if (initialClipMask is not null) parser.State.ClipMask = initialClipMask;

        parser.Parse(contentBytes, ctx.FontDicts, null, extGStates, colorSpaces ?? ctx.ColorSpaces, ctx.Properties, ctx.Patterns);
    }

    // ── Text rendering ──────────────────────────────────────────────

    // ── XObject dispatch ────────────────────────────────────────────


    // ── Image rendering ─────────────────────────────────────────────

}

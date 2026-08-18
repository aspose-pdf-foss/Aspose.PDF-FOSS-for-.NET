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
        var crop = EffectiveCropRect(page);
        var rot = ((page.RotateDegrees % 360) + 360) % 360;
        var visW = (rot == 90 || rot == 270) ? crop.Height : crop.Width;
        var visH = (rot == 90 || rot == 270) ? crop.Width : crop.Height;
        var pixelW = PagePixels(visW, xDpi);
        var pixelH = PagePixels(visH, yDpi);
        return RenderPageAtPixelSize(page, pixelW, pixelH);
    }

    /// <summary>
    /// Render a PDF page directly at the requested pixel dimensions (no resample).
    /// Preserves the AA scanline filler's fractional coverage on thin strokes — a
    /// render-at-high-DPI-then-downsample detour smears those coverage values back
    /// toward binary when neighbouring source rows differ, which is how 50%-grey
    /// page-frame edges used to be lost.
    /// </summary>
    internal RgbaBuffer RenderPageAtPixelSize(Page page, int pixelW, int pixelH)
    {
        if (pixelW <= 0) pixelW = 1;
        if (pixelH <= 0) pixelH = 1;

        var reader = page.Reader;
        var rawMb = page.MediaBox;
        // The visible region is the crop box clipped to the media box; content is
        // sized and offset to it so anything outside the crop area is excluded.
        var crop = EffectiveCropRect(page);

        // PDF 32000 §14.8.2.7 — /Rotate (0/90/180/270, clockwise) defines how the
        // page is displayed. The content stream is authored in the unrotated
        // coordinate system; we have to compose the rotation into the initial CTM
        // so glyphs/images/paths land on the (rotated) visible canvas. Otherwise
        // a 90°-Rotate landscape page draws as portrait content shoved into the
        // left half of a landscape canvas (the symptom seen on a
        // facility-plan diagram).
        var rot = ((page.RotateDegrees % 360) + 360) % 360;
        Aspose.Pdf.Rectangle effectiveMb;
        double[]? initialPageCtm = null;
        if (rot == 90 || rot == 180 || rot == 270)
        {
            var w = rawMb.Width;
            var h = rawMb.Height;
            // Rotated bounding box: 90/270 swap dimensions, 180 keeps them.
            effectiveMb = rot == 180
                ? new Aspose.Pdf.Rectangle(0, 0, w, h)
                : new Aspose.Pdf.Rectangle(0, 0, h, w);
            // Initial CTM = clockwise rotation of the unrotated content into
            // the rotated canvas's coord frame. PDF 32000 §14.8.2.7 says /Rotate
            // is the *clockwise* angle the page is shown at, so the content's
            // unrotated corners need to swing CW into the visible canvas:
            //   Rotate=90 maps unrotated (0,0) → visible (0,w)   [top-left]
            //   Rotate=180 maps unrotated (0,0) → visible (w,h)  [top-right]
            //   Rotate=270 maps unrotated (0,0) → visible (h,0)  [bottom-right]
            initialPageCtm = rot switch
            {
                90 => new[] { 0.0, -1.0, 1.0, 0.0, 0.0, w },
                180 => new[] { -1.0, 0.0, 0.0, -1.0, w, h },
                270 => new[] { 0.0, 1.0, -1.0, 0.0, h, 0.0 },
                _ => null,
            };
        }
        else
        {
            // Unrotated: the device box is the crop rectangle. Its lower-left maps to
            // the bottom-left pixel, so cropped content is positioned correctly and the
            // area outside the crop box falls off the (crop-sized) canvas.
            effectiveMb = crop;
        }

        // Uniform scale: caller is expected to pick pixelW/pixelH with the visible box's
        // own aspect ratio. When they don't match exactly, X scale wins (height drifts
        // ±1px which is swallowed by the comparison tolerance anyway).
        var scale = pixelW / effectiveMb.Width;

        var pixels = new byte[pixelW * pixelH * 4];

        // Fill with white background
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;     // R
            pixels[i + 1] = 255; // G
            pixels[i + 2] = 255; // B
            pixels[i + 3] = 255; // A
        }

        var ctx = new RenderContext(pixels, pixelW, pixelH, scale, effectiveMb, reader);

        // Resolve page resources, walking up the Pages tree if the page itself omits
        // /Resources. PDF 32000 §7.7.3.4 makes /Resources an inheritable attribute —
        // many real PDFs list only /Group + /MediaBox + /Contents on the page and put
        // patterns / XObjects on the parent /Pages dict. Without inheritance, every
        // "/P1 scn" / "/X1 Do" resolves to nothing and the page renders blank.
        var resources = ResolveInheritedPageResources(page.Dict, reader);
        var extGStates = ResolveExtGStates(resources, reader);
        var fontDicts = ResolveFontDicts(resources, reader);
        var allXObjects = ResolveAllXObjects(resources, reader);

        ctx.AllXObjects = allXObjects;
        ctx.FontDicts = fontDicts;
        // /Pattern entry in page resources holds colour-pattern dicts (tiling or shading).
        // Cached on the context so DrawPath can resolve "/Pn scn" in O(1) without re-walking
        // the resources tree per fill.
        ctx.Patterns = reader.ResolveDict(resources?.Get("Pattern"));
        // /Shading entry is a sibling of /Pattern and feeds the `sh` operator directly
        // (PDF 32000 §8.7.4.5). Stored on the context so OnShadingPainted can resolve
        // names without re-walking the resources tree.
        ctx.Shadings = reader.ResolveDict(resources?.Get("Shading"));
        // /ColorSpace entry: dictionary of named Separation/DeviceN/etc. spaces
        // that `cs`/`CS` operators reference. The parser consumes this to
        // pre-resolve tint transforms (Pantone spot colours, etc.) so `scn`
        // produces real RGB instead of falling through to the gray default.
        ctx.ColorSpaces = reader.ResolveDict(resources?.Get("ColorSpace"));
        // /Properties is where named BDC props live (e.g. /OC /MC0 BDC →
        // resources./Properties/MC0 → OCG dict). Needed alongside the
        // /OCProperties OFF set so the renderer can skip hidden layers.
        ctx.Properties = reader.ResolveDict(resources?.Get("Properties"));
        ctx.OcgHidden = ResolveHiddenOcgs(reader);

        // Parse and render content stream
        var contentBytes = GetPageContent(page.Dict, reader);
        RenderContent(contentBytes, ctx, extGStates, initialCtm: initialPageCtm);

        // Annotations are painted *after* the page content (PDF 32000-1:2008 §12.5):
        // Highlight annotations use Multiply blending so underlying text shows through.
        DrawAnnotations(ctx, page.Dict);

        // Clear resolved object cache after rendering to prevent memory growth
        // when rendering many pages sequentially.
        reader.ClearCache();

        return new RgbaBuffer(pixels, pixelW, pixelH);
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
        byte[]? initialClipMask = null, PdfDictionary? colorSpaces = null)
    {
        var parser = new ContentStreamParser(ctx.Reader);

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

    private static void DrawText(RenderContext ctx, string text, byte[] rawBytes, GraphicsState state)
    {
        if (state.RenderingMode == 3) return; // invisible
        if (PageRenderFlags.SuppressText) return; // HTML PNG-background: graphics only
        // An empty decoded string with non-empty rawBytes can still happen for CID fonts whose
        // ToUnicode CMap is missing — we must still walk rawBytes to advance the cursor.
        if (string.IsNullOrEmpty(text) && (rawBytes is null || rawBytes.Length == 0)) return;

        // Inherit the active blend mode for glyph blits.
        ctx.CurrentBlendMode = state.BlendMode;
        ctx.SoftMaskAlpha = state.SoftMask is { } sm__ ? ResolveSoftMaskAlpha(ctx, sm__) : null;

        // Type 3 fonts (PDF 32000 §9.6.5) define each glyph as its own PDF content
        // stream stored under /CharProcs. They show up most often in old dot-matrix
        // report/invoice PDFs, typically as Type 3 fonts with inline-ImageMask glyphs
        // for the addresses, invoice numbers, table rows. Detect and route before
        // the CID/simple-font dispatch — Type 3 has no embedded TTF/Type 1 outlines
        // for our glyph rasteriser to consume.
        if (rawBytes is not null && rawBytes.Length > 0
            && state.FontName is { } fname
            && ctx.FontDicts is not null
            && ctx.FontDicts.TryGetValue(fname, out var fdict)
            && fdict.GetName("Subtype") == "Type3")
        {
            DrawType3Text(ctx, rawBytes, state, fdict);
            return;
        }

        var tm = state.TextMatrix;
        var ctm = state.Ctm;
        var trm = GraphicsState.MultiplyMatrices(tm, ctm);

        // PDF allows a negative /Tf font size — the text matrix encodes the direction
        // (e.g. mirrored text in some XFA-derived forms). Use |fontSize|
        // for the rasterizer's pixel-size budget; horizontal direction is already
        // baked into the text matrix via Tm × CTM.
        var fontSize = Math.Abs(state.FontSize);
        var effectiveSize = fontSize * Math.Sqrt(trm[1] * trm[1] + trm[3] * trm[3]);
        if (effectiveSize < 0.5) return;

        var x = trm[4];
        var y = trm[5];

        // Convert to pixel coords
        var px = (double)((x - ctx.MediaBox.LLX) * ctx.Scale);
        var py = (double)(ctx.PixelH - (y - ctx.MediaBox.LLY) * ctx.Scale);

        var r = (byte)(state.FillR * 255);
        var g = (byte)(state.FillG * 255);
        var b = (byte)(state.FillB * 255);
        var a = (byte)(state.FillAlpha * 255);

        var parser = GetGlyphParser(ctx, state.FontName, out var hScale);
        var fontMetrics = GetFontMetrics(ctx, state.FontName);
        var cidInfo = GetCidFontInfo(ctx, state.FontName);

        // Two code paths: CID (Type0 with 2-byte encoding) walks rawBytes to produce CIDs and
        // resolves GIDs via /CIDToGIDMap; simple fonts use the decoded Unicode string and the
        // TTF's own cmap. Subset CID fonts routinely strip their TTF cmap, so going via the
        // CID→GID map is the only path that produces correct glyphs.
        // Tc/Tw are in unscaled text-space units (PDF 32000 §9.3.3). They get multiplied by
        // the full text-to-device chain: |Tm × CTM| to land in PDF points, then × ctx.Scale
        // for pixels. Multiplying only by ctx.Scale (as before) over-counted by 1/|CTM| and
        // produced huge inter-word gaps on content streams with a sub-unit `cm` scaling
        // (ACORD forms use `0.12 cm`, making the over-count 8× too wide).
        var textSpaceScale = Math.Sqrt(trm[0] * trm[0] + trm[2] * trm[2]);
        var charSpacingPx = state.CharSpacing * textSpaceScale * ctx.Scale;
        var wordSpacingPx = state.WordSpacing * textSpaceScale * ctx.Scale;
        // A Type0 font with a 1-byte custom CMap (codespace <00> <FF>) still shows
        // CIDs, not byte-encoded characters. When its embedded program is a CID-keyed
        // CFF the simple-font path can never resolve a glyph (bare CFFs carry no
        // cmap), so route it through the CID path too — DrawCidText steps 1 byte per
        // code for these. Type0 fonts with TrueType descendants keep the old routing.
        if (cidInfo is not null && rawBytes is not null
            && (cidInfo.IsTwoByteEncoding || parser is CffGlyphSource { IsCidKeyed: true }))
        {
            DrawCidText(ctx, rawBytes, cidInfo, parser, fontMetrics,
                ref px, py, effectiveSize, hScale, charSpacingPx, wordSpacingPx, r, g, b, a);
        }
        else
        {
            DrawSimpleText(ctx, text, rawBytes, parser, fontMetrics,
                GetEncodingGidMap(ctx, state.FontName, parser),
                ref px, py, effectiveSize, hScale, charSpacingPx, wordSpacingPx, r, g, b, a);
        }
    }

    /// <summary>
    /// Build (and cache) a 256-entry byte→GID map for a simple font whose PDF /Encoding
    /// names glyphs the embedded font exposes by name (a /Differences array of custom
    /// names such as /G42). Returns null when the font has no /Differences or the parser
    /// can't resolve any of the names — callers fall back to the Unicode CMap path.
    /// </summary>
    private static int[]? GetEncodingGidMap(RenderContext ctx, string? fontName, IGlyphOutlineSource? parser)
    {
        if (fontName is null || parser is null) return null;
        if (ctx.EncodingGidMaps.TryGetValue(fontName, out var cached)) return cached;
        var map = BuildEncodingGidMap(ctx.FontDicts, ctx.Reader, fontName, parser);
        ctx.EncodingGidMaps[fontName] = map;
        return map;
    }

    /// <summary>Shared by both renderers: build the 256-entry byte→GID map from a simple
    /// font's /Encoding /Differences glyph names resolved through the embedded font's name
    /// table. Returns null when there is no /Differences or no name resolves.</summary>
    internal static int[]? BuildEncodingGidMap(Dictionary<string, PdfDictionary>? fontDicts,
        IO.PdfReader reader, string? fontName, IGlyphOutlineSource? parser)
    {
        if (fontName is null || parser is null || fontDicts is null
            || !fontDicts.TryGetValue(fontName, out var fdict)
            || reader.ResolveDict(fdict.Get("Encoding")) is not { } encDict
            || encDict.Get("Differences") is not PdfArray)
            return null;

        var names = ResolveEncoding(fdict, reader);
        var built = new int[256];
        var any = false;
        for (var code = 0; code < 256; code++)
        {
            if (names[code] is not { } n) continue;
            if (n is "space" or "nbspace" or "uni0020" or "uni00A0")
            {
                // A whitespace NAME never paints. Resolving it through the program can
                // land on an arbitrary visible glyph — subset tools renumber glyphs but
                // keep a stale cmap (an Arabic page drew digit chains where its spaces
                // belong). -1 = explicit blank: consumers skip both the cmap fallback
                // (gid != 0) and the paint (gid < 1); the advance still comes from /Widths.
                built[code] = -1;
                any = true;
                continue;
            }
            var gid = parser.GidForName(n);
            if (gid > 0)
            {
                built[code] = gid;
                any = true;
            }
        }
        return any ? built : null;
    }

    private static void DrawCidText(RenderContext ctx, byte[] rawBytes, CidFontInfo cidInfo,
        IGlyphOutlineSource? parser, FontMetrics? fontMetrics,
        ref double px, double py, double effectiveSize, double hScale,
        double charSpacingPx, double wordSpacingPx,
        byte r, byte g, byte b, byte a)
    {
        // Predefined legacy national CMaps (GBK-EUC-H, ETen-B5-H, 90ms-RKSJ-H, …)
        // encode their show-strings in a national multi-byte charset, not as Adobe
        // CIDs. Decode the whole run to Unicode and render through the resolved
        // system font / CJK fallback. Handled separately because the charset is
        // mixed-width (1-byte ASCII + 2-byte CJK), unlike the 2-byte CID path below.
        if (cidInfo.LegacyCodepage != 0)
        {
            DrawLegacyCjkText(ctx, rawBytes, cidInfo, parser, fontMetrics, ref px, py, effectiveSize,
                hScale, charSpacingPx, wordSpacingPx, r, g, b, a);
            return;
        }

        // Non-embedded predefined CJK fonts (HYGoThic, STSong, KozMin, etc.)
        // have no /FontFile*, so parser is null. PDF 32000 §9.6.6 says the
        // reader should supply a system font that matches /CIDSystemInfo.
        // CjkFallbackFont loads a broad-coverage TTF (Arial Unicode on macOS,
        // Noto CJK on Linux, etc.) once per process and reroutes glyph
        // resolution via CID → Unicode (Adobe tables) → fallback cmap.
        GlyphOutlineParser? fallback = null;
        if (parser is null)
        {
            var canFallback = cidInfo.IsUnicodeEncoding
                              || (cidInfo.Ordering is not null && cidInfo.Ordering != "Identity");
            // Resolve a system font by the CID ordering/base name (Korea1 -> Malgun,
            // GB1 -> SimSun, Japan1 -> MS Mincho), not the single generic broad-coverage
            // font: that one covers Han but not Hangul, so non-embedded Korean text
            // (UniKS-UTF16-H) was dropped while Chinese on the same page rendered.
            // ResolveNamed falls back to the generic font itself.
            if (canFallback) fallback = CjkFallbackFont.ResolveNamed(cidInfo.CjkBaseFont, cidInfo.Ordering);
        }

        // 1-byte custom CMaps (codespace <00> <FF>) show one CID per byte.
        var step = cidInfo.IsTwoByteEncoding ? 2 : 1;
        for (var i = 0; i + step <= rawBytes.Length; i += step)
        {
            var code = step == 2 ? (rawBytes[i] << 8) | rawBytes[i + 1] : rawBytes[i];
            // Custom CMaps (non-Identity-H) map byte-codes to CIDs via cidchar/
            // cidrange blocks. Predefined Identity-H/V CMaps are pass-through.
            var cid = cidInfo.CodeToCid(code);

            if (parser is not null)
            {
                // CID-keyed CFF subsets renumber glyphs (GID 1..N following the Charset
                // order) and the descendant CIDFontType0 dict has no /CIDToGIDMap.
                // Resolve through the CFF's own charset in that case.
                var gid = parser is CffGlyphSource cff && cff.IsCidKeyed
                    ? cff.CidToGid(cid)
                    : cidInfo.ResolveGid(cid);
                // Out-of-charset CID with a constant high byte over a small identity
                // charset: paint the low-byte glyph instead (see the GDI+
                // renderer's DrawCidText for the full note).
                if (gid == 0 && cid > 0xFF && parser is CffGlyphSource cffLow && cffLow.IsCidKeyed)
                    gid = cffLow.CidToGid(cid & 0xFF);
                if (gid > 0)
                {
                    var outline = parser.GetOutline(gid);
                    if (outline is not null)
                    {
                        var alphaMask = GlyphRasterizer.Rasterize(outline, parser.UnitsPerEm,
                            effectiveSize, ctx.Scale, out var gw, out var gh, out var bx, out var by,
                            hScale);
                        if (alphaMask is not null)
                        {
                            BlitAlphaMask(ctx, alphaMask, gw, gh,
                                (int)px + (int)(bx * hScale), (int)py + by, r, g, b, a);
                        }
                    }
                }
            }
            else if (fallback is not null)
            {
                // Two paths into the fallback font's cmap:
                // - Uni*-UCS2-* / Uni*-UTF16-* encodings: the 2-byte input is
                //   already a Unicode codepoint. Look up directly.
                // - Identity-H/V or bytecode-CMaps: input is an Adobe CID.
                //   Adobe-table → Unicode → fallback cmap.
                int fallbackGid;
                if (cidInfo.IsUnicodeEncoding)
                {
                    fallback.CMap.TryGetValue(cid, out fallbackGid);
                }
                else
                {
                    fallbackGid = CjkFallbackFont.ResolveFallbackGid(cidInfo.Ordering, cid);
                }
                if (fallbackGid > 0)
                {
                    var outline = fallback.GetOutline(fallbackGid);
                    if (outline is not null)
                    {
                        var alphaMask = GlyphRasterizer.Rasterize(outline, fallback.UnitsPerEm,
                            effectiveSize, ctx.Scale, out var gw, out var gh, out var bx, out var by,
                            hScale);
                        if (alphaMask is not null)
                        {
                            BlitAlphaMask(ctx, alphaMask, gw, gh,
                                (int)px + (int)(bx * hScale), (int)py + by, r, g, b, a);
                        }
                    }
                }
            }

            // Advance: CID fonts' /W table is keyed by CID, not Unicode.
            // Tc (charSpacingPx) is added per character; Tw (wordSpacingPx) only for CID 32 (space).
            var charWidth = fontMetrics?.GetWidth(cid) ?? 1000;
            px += charWidth / 1000.0 * effectiveSize * ctx.Scale * hScale + charSpacingPx;
            if (cid == 32) px += wordSpacingPx;
        }
    }

    /// <summary>
    /// Render a show-string from a non-embedded predefined legacy national CMap
    /// (GBK-EUC-H, ETen-B5-H, 90ms-RKSJ-H, KSCms-UHC-H, …). The bytes are decoded
    /// to Unicode through the CMap's national codepage, then each character is drawn
    /// with the resolved system font (e.g. Times for the Latin runs these fonts
    /// carry) or, when that lacks the glyph, a broad-coverage CJK fallback (SimSun).
    /// Advances come from the chosen font's own hmtx — the PDF /W is keyed by Adobe
    /// CIDs we never resolve, and its /DW is commonly 500 (half-width), which would
    /// crush full-width CJK.
    /// </summary>
    private static void DrawLegacyCjkText(RenderContext ctx, byte[] rawBytes, CidFontInfo cidInfo,
        IGlyphOutlineSource? parser, FontMetrics? fontMetrics, ref double px, double py,
        double effectiveSize, double hScale,
        double charSpacingPx, double wordSpacingPx, byte r, byte g, byte b, byte a)
    {
        var fallback = CjkFallbackFont.ResolveNamed(cidInfo.CjkBaseFont, cidInfo.Ordering);
        var vert = cidInfo.IsVertical;
        var penY = py; // vertical text advances a local Y down the column
        var i = 0;
        while (i < rawBytes.Length)
        {
            // Walk the mixed-width national charset: a lead byte 0x81-0xFE starts a
            // 2-byte code, everything else is single-byte ASCII.
            var step = cidInfo.LegacyByteLength(rawBytes[i]);
            if (step == 2 && i + 1 >= rawBytes.Length) step = 1;
            var code = step == 2 ? ((rawBytes[i] << 8) | rawBytes[i + 1]) : rawBytes[i];
            i += step;

            var uni = cidInfo.LegacyToUnicode(code) ?? -1;
            IGlyphOutlineSource? src = null;
            var gid = 0;
            if (uni >= 0)
            {
                // Latin runs render in the resolved system font; CJK in the fallback.
                if (parser is not null && parser.CMap.TryGetValue(uni, out var g1) && g1 > 0)
                { src = parser; gid = g1; }
                else if (fallback is not null && fallback.CMap.TryGetValue(uni, out var g2) && g2 > 0)
                { src = fallback; gid = g2; }
            }

            if (src is not null)
            {
                var outline = src.GetOutline(gid);
                if (outline is not null)
                {
                    var alphaMask = GlyphRasterizer.Rasterize(outline, src.UnitsPerEm,
                        effectiveSize, ctx.Scale, out var gw, out var gh, out var bx, out var by, hScale);
                    if (alphaMask is not null)
                        BlitAlphaMask(ctx, alphaMask, gw, gh,
                            (int)px + (int)(bx * hScale), (int)(vert ? penY : py) + by, r, g, b, a);
                }
            }

            // Nominal full-/half-width advance (must match ContentStreamParser's cursor
            // advance for these CMaps). Vertical (-V) text advances one em down per glyph.
            if (vert)
            {
                penY += effectiveSize * ctx.Scale + charSpacingPx;
            }
            else
            {
                var charWidth = CjkFallbackFont.AdvanceEm(cidInfo, fontMetrics, code, step);
                px += charWidth / 1000.0 * effectiveSize * ctx.Scale * hScale + charSpacingPx;
                if (uni == ' ') px += wordSpacingPx;
            }
        }
    }

    /// <summary>
    /// Render Type 3 font text (PDF 32000 §9.6.5). Each byte in the show string
    /// indexes into the font's encoding (we honour /BaseEncoding name aliases plus
    /// the /Differences override array) to get a glyph name; that name keys into
    /// /CharProcs to get a small PDF content stream which is executed against the
    /// page renderer with a per-glyph CTM = FontMatrix · TextSizeMatrix · Tm · Ctm.
    /// Each glyph's advance comes from /Widths (1000-units-per-em-style) scaled
    /// through FontMatrix and the active font size. The CharProc starts with d0
    /// or d1 (already-known metrics) which our parser treats as no-ops; the rest
    /// is regular PDF that includes inline ImageMask BI/ID/EI sequences (handled
    /// by the OnInlineImage hook installed by RenderContent).
    /// </summary>
    private static void DrawType3Text(RenderContext ctx, byte[] rawBytes,
        GraphicsState state, PdfDictionary fontDict)
    {
        var fontMatrix = ExtractFontMatrix(fontDict);
        var encoding = ResolveEncoding(fontDict, ctx.Reader);
        var charProcs = ctx.Reader.ResolveDict(fontDict.Get("CharProcs"));
        if (charProcs is null) return;
        var widths = ctx.Reader.Resolve(fontDict.Get("Widths")) as PdfArray;
        var firstChar = (int)fontDict.GetInt("FirstChar");

        // FontMatrix maps glyph space → text space. To map glyph space directly to
        // page user space we then multiply by Tfs·Tm·Ctm. The Tfs (font size) and
        // optional Th (horizontal scaling, default 100%) form the text size matrix.
        var hScale = state.HorizontalScaling / 100.0;
        var fontSizeMatrix = new[] { state.FontSize * hScale, 0.0, 0.0, state.FontSize, 0.0, 0.0 };

        // Resources for the CharProc's content stream. PDF 32000 §9.6.5.4 says a
        // Type 3 font may have its own /Resources; if absent, use page resources.
        var fontResources = ctx.Reader.ResolveDict(fontDict.Get("Resources"));
        var glyphExtGStates = ResolveExtGStates(fontResources, ctx.Reader);
        // Fall back to outer ExtGStates so glyphs that reference /GS0 via the page
        // continue to find them. (Type 3 glyphs rarely use ExtGStates but inline
        // image masks are common — those don't need an ExtGState anyway.)

        foreach (var b in rawBytes)
        {
            var glyphName = encoding[b];
            // Always advance Tm by the glyph width, even if the glyph is .notdef
            // or missing — the column layout of the giant @-tiled report headers
            // depends on consistent advances per byte.
            var widthUnits = 0.0;
            if (widths is not null)
            {
                var idx = b - firstChar;
                if (idx >= 0 && idx < widths.Count) widthUnits = NumFrom(widths[idx]);
            }
            // Advance in text space = widthUnits · FontMatrix.a (FontMatrix maps to
            // text-space units; the .a entry is the horizontal scale).
            var advanceTextSpace = widthUnits * fontMatrix[0];

            if (glyphName is not null && glyphName != ".notdef"
                && charProcs.Get(glyphName) is { } cpObj
                && ctx.Reader.ResolveStream(cpObj) is { } cpStream)
            {
                byte[] cpBytes;
                try { cpBytes = ctx.Reader.DecodeStream(cpStream); }
                catch { cpBytes = System.Array.Empty<byte>(); }
                if (cpBytes.Length > 0)
                {
                    // Compose the per-glyph CTM. Order matters — for our PDF row-
                    // vector × matrix convention, v through M1·M2 means apply M1
                    // first. We want glyph → font → text-size → text → page-user.
                    var tmCtm = GraphicsState.MultiplyMatrices(state.TextMatrix, state.Ctm);
                    var sizeTmCtm = GraphicsState.MultiplyMatrices(fontSizeMatrix, tmCtm);
                    var glyphCtm = GraphicsState.MultiplyMatrices(fontMatrix, sizeTmCtm);
                    // The CharProc may have its own /Resources too; merge with the
                    // font's resources (the page renderer will fall back to ctx's
                    // own font/extgstate dicts for unresolved names).
                    RenderContent(cpBytes, ctx, glyphExtGStates, glyphCtm);
                }
            }

            // Advance Tm by the glyph width × Tfs × Th in text space (which becomes
            // user-space displacement through the existing Tm · Ctm chain).
            var dx = advanceTextSpace * state.FontSize * hScale;
            state.AdvanceTextPosition(dx, 0);
        }
    }

    /// <summary>
    /// Extract a font's /FontMatrix. PDF 32000 §9.6.5: default is
    /// [0.001 0 0 0.001 0 0] for Type 3 fonts (glyph coords are in 1000 ths of an em).
    /// </summary>
    internal static double[] ExtractFontMatrix(PdfDictionary fontDict)
    {
        if (fontDict.Get("FontMatrix") is PdfArray arr && arr.Count >= 6)
        {
            var m = new double[6];
            for (var i = 0; i < 6; i++) m[i] = NumFrom(arr[i]);
            return m;
        }
        return new[] { 0.001, 0.0, 0.0, 0.001, 0.0, 0.0 };
    }

    /// <summary>
    /// Build a byte→glyph-name map (256 entries) from a font's /Encoding. PDF 32000
    /// §9.6.6: /Encoding can be a name (StandardEncoding, WinAnsiEncoding,
    /// MacRomanEncoding, MacExpertEncoding) or a dict with optional /BaseEncoding
    /// (one of those names; defaults to StandardEncoding) and /Differences (alternating
    /// integer code start and a sequence of glyph names). For Type 3 fonts in
    /// dot-matrix report PDFs the /Differences array names every byte explicitly,
    /// so even if we don't ship a full StandardEncoding table the result is correct.
    /// </summary>
    internal static string?[] ResolveEncoding(PdfDictionary fontDict, IO.PdfReader reader)
    {
        var result = new string?[256];
        for (var i = 0; i < 256; i++) result[i] = ".notdef";

        var encObj = reader.Resolve(fontDict.Get("Encoding"));
        if (encObj is PdfName encName)
        {
            ApplyBaseEncoding(result, encName.Value);
            return result;
        }
        if (encObj is PdfDictionary encDict)
        {
            var baseName = encDict.GetName("BaseEncoding");
            if (baseName is not null) ApplyBaseEncoding(result, baseName);
            if (encDict.Get("Differences") is PdfArray diffs)
            {
                int code = 0;
                foreach (var item in diffs)
                {
                    if (item is PdfInteger pi) { code = (int)pi.Value; }
                    else if (item is PdfName pn && code >= 0 && code < 256)
                    {
                        result[code] = pn.Value;
                        code++;
                    }
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Fill the byte→glyph map for a named base encoding, per PDF 32000 Annex D.
    /// The extended range matters: MacRomanEncoding keeps fi/fl at 0xDE/0xDF and
    /// accented letters in 0x80–0x9F, WinAnsi is CP1252 — leaving 0x80–0xFF as
    /// .notdef drops those glyphs from name-keyed (CFF/Type 1) programs.
    /// </summary>
    private static void ApplyBaseEncoding(string?[] result, string baseEncodingName)
    {
        for (var code = 0; code < 256; code++)
        {
            var name = baseEncodingName switch
            {
                "MacRomanEncoding" => Text.PdfEncodings.MacRomanName(code),
                "WinAnsiEncoding" => Text.PdfEncodings.WinAnsiName(code),
                _ => Text.Type1StandardEncoding.GetName(code),
            };
            if (name is not null) result[code] = name;
        }
    }

    private static void DrawSimpleText(RenderContext ctx, string text, byte[]? rawBytes,
        IGlyphOutlineSource? parser, FontMetrics? fontMetrics, int[]? encGidMap,
        ref double px, double py, double effectiveSize, double hScale,
        double charSpacingPx, double wordSpacingPx,
        byte r, byte g, byte b, byte a)
    {
        // When rawBytes is 1:1 with text (typical for simple TT fonts), each char position
        // also corresponds to a single byte. Subset TT fonts embedded in PDFs commonly
        // carry only a Mac Roman cmap (platform 1 / format 6) keyed by the raw byte values
        // from the content stream, not by /ToUnicode-derived chars. Try the byte-keyed lookup
        // first: for non-subset fonts using WinAnsi/StandardEncoding, byte == Unicode for
        // the ASCII range so this still hits the correct glyph. For subset fonts where
        // /ToUnicode maps byte 0x21 to 'T' but cmap[0x54] (the Unicode of 'T') would return
        // whatever glyph the subset tool chose for byte 0x54 (the wrong one), the byte
        // lookup picks the right glyph. Unicode is the fallback for cases like /Differences-
        // encoded fonts where byte 0x80 maps to a non-ASCII Unicode char (0x0105 etc.).
        bool useBytesFallback = rawBytes is not null && rawBytes.Length == text.Length;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            int gid = 0;
            if (parser is not null)
            {
                // An explicit /Encoding /Differences is authoritative for a simple font
                // (PDF 32000 §9.6.6.1) and overrides the embedded program's own byte cmap;
                // without this a code like 0x39 ("t" via Differences) wrongly draws the
                // embedded "nine" glyph. encGidMap is non-null only when
                // /Differences exists; a code with no resolvable name yields 0 → fall back.
                if (encGidMap is not null && rawBytes is not null && i < rawBytes.Length)
                    gid = encGidMap[rawBytes[i]];

                if (gid == 0)
                {
                    if (useBytesFallback && parser.CMap.TryGetValue(rawBytes![i], out gid) && gid > 0) { /* byte hit */ }
                    else if (parser.CMap.TryGetValue(ch, out gid) && gid > 0) { /* unicode hit */ }
                    else gid = 0;
                }
            }
            if (parser is not null && gid > 0)
            {
                // parser.CMap (TTF cmap) maps Unicode → GID for embedded simple TrueType fonts.
                var outline = parser.GetOutline(gid);
                if (outline is not null)
                {
                    var alphaMask = GlyphRasterizer.Rasterize(outline, parser.UnitsPerEm,
                        effectiveSize, ctx.Scale, out var gw, out var gh, out var bx, out var by,
                        hScale);
                    if (alphaMask is not null)
                    {
                        BlitAlphaMask(ctx, alphaMask, gw, gh,
                            (int)px + (int)(bx * hScale), (int)py + by, r, g, b, a);
                    }
                }
            }

            // Per-char advance: prefer the PDF font dict's /Widths (FontMetrics
            // keys those by byte code 0-255, plus Standard-14 AFM widths).
            // For subset TT fonts (Mac Roman cmap) the byte values in the content
            // stream ARE the /Widths keys; the /ToUnicode-derived char is unrelated.
            // For Standard-14 / WinAnsi fonts byte == Unicode for the ASCII range
            // so byte-keyed lookup is also correct there. For /Differences-mapped
            // Polish/Czech/etc. chars decoded to Unicode above 255, fall back to the
            // TTF hmtx advance via the parser's resolved GID. Without this fallback
            // the chars would all advance by 500 and visibly overlap (or, for subset
            // fonts, render with huge gaps between letters).
            int charWidth = 500;
            if (useBytesFallback && fontMetrics is not null)
                charWidth = fontMetrics.GetWidth(rawBytes![i]);
            else if (fontMetrics is not null)
                charWidth = fontMetrics.GetWidth(ch);
            if ((charWidth == 0 || (ch > 0xFF && (charWidth == 500 || charWidth <= 0)))
                && parser is not null && gid > 0)
            {
                var hmtxAdvance = parser.GetAdvanceWidth(gid);
                if (hmtxAdvance > 0 && parser.UnitsPerEm > 0)
                    charWidth = hmtxAdvance * 1000 / parser.UnitsPerEm;
            }
            px += charWidth / 1000.0 * effectiveSize * ctx.Scale * hScale + charSpacingPx;
            if (ch == ' ') px += wordSpacingPx;
        }
    }

    private static CidFontInfo? GetCidFontInfo(RenderContext ctx, string? fontName)
        => GetCidFontInfo(ctx.FontDicts, ctx.Reader, ctx.CidFontInfos, fontName);

    internal static CidFontInfo? GetCidFontInfo(
        Dictionary<string, PdfDictionary>? fontDicts, IO.PdfReader reader,
        Dictionary<string, CidFontInfo?> cache, string? fontName)
    {
        if (fontName is null || fontDicts is null) return null;
        if (cache.TryGetValue(fontName, out var cached)) return cached;

        CidFontInfo? info = null;
        if (fontDicts.TryGetValue(fontName, out var fontDict))
        {
            try { info = CidFontInfo.TryBuild(fontDict, reader); }
            catch { info = null; }
        }
        cache[fontName] = info;
        return info;
    }

    private static IGlyphOutlineSource? GetGlyphParser(RenderContext ctx, string? fontName,
        out double horizontalScale)
        => GetGlyphParser(ctx.FontDicts, ctx.Reader, ctx.FontParsers, fontName, out horizontalScale);

    /// <summary>True when the bytes begin with an sfnt signature (TrueType, OpenType, or
    /// a TrueType collection). Used to detect a TrueType/OpenType program embedded under
    /// the /FontFile key instead of the standard /FontFile2 or /FontFile3. Sets
    /// <paramref name="isOpenTypeCff"/> for an 'OTTO' (CFF-outline) container.</summary>
    private static bool LooksLikeSfnt(byte[] d, out bool isOpenTypeCff)
    {
        isOpenTypeCff = false;
        if (d.Length < 4) return false;
        uint tag = (uint)(d[0] << 24 | d[1] << 16 | d[2] << 8 | d[3]);
        isOpenTypeCff = tag == 0x4F54544Fu;                 // 'OTTO' — OpenType with CFF outlines
        return tag == 0x00010000u                           // TrueType outlines
            || tag == 0x74727565u                           // 'true'
            || tag == 0x74746366u                           // 'ttcf' — TrueType Collection
            || isOpenTypeCff;
    }

    /// <summary>
    /// Resolve a glyph-outline source for a font resource, independent of any render
    /// context, so alternate renderers (e.g. <see cref="GdiPlusPageRenderer"/>) can
    /// reuse the embedded-font/system-font resolution. <paramref name="cache"/> is a
    /// caller-owned per-render cache keyed by font resource name.
    /// </summary>
    internal static IGlyphOutlineSource? GetGlyphParser(
        Dictionary<string, PdfDictionary>? fontDicts, IO.PdfReader reader,
        Dictionary<string, (IGlyphOutlineSource? parser, double hScale)> cache,
        string? fontName, out double horizontalScale)
    {
        horizontalScale = 1.0;
        if (fontName is null || fontDicts is null) return null;
        if (!fontDicts.TryGetValue(fontName, out var fontDict)) return null;

        if (cache.TryGetValue(fontName, out var cached))
        {
            horizontalScale = cached.hScale;
            return cached.parser;
        }

        IGlyphOutlineSource? parser = null;
        var hScale = 1.0;
        try
        {
            // Look for embedded font data in FontDescriptor. For Type0 fonts the
            // descriptor lives on DescendantFonts[0], which is usually an indirect
            // reference — resolve it before casting.
            var descriptor = reader.ResolveDict(fontDict.Get("FontDescriptor"));
            if (descriptor is null)
            {
                var descendants = reader.Resolve(fontDict.Get("DescendantFonts")) as PdfArray;
                if (descendants is not null && descendants.Count > 0)
                {
                    var desc0 = reader.ResolveDict(descendants[0]);
                    descriptor = desc0 is not null ? reader.ResolveDict(desc0.Get("FontDescriptor")) : null;
                }
            }

            if (descriptor is not null)
            {
                // TrueType embedding: /FontFile2 (TrueType) — the common CIDFontType2 path.
                var fontFile2 = reader.ResolveStream(descriptor.Get("FontFile2"));
                if (fontFile2 is not null)
                {
                    var ttfData = reader.DecodeStream(fontFile2);
                    // Some generators park a BARE CFF program under /FontFile2 (a
                    // CIDFontType2 whose "TrueType" bytes begin with the CFF header
                    // 01 00 hdrSize offSize). A real sfnt starts 00 01 00 00 / 'true' /
                    // 'ttcf', so the two are unambiguous — route the CFF to its parser
                    // instead of reading garbage table offsets and painting nothing.
                    // An 'OTTO' container under /FontFile2 has CFF outlines too (no
                    // glyf table) — same rerouting.
                    if (ttfData.Length > 4 && ttfData[0] == 0x01 && ttfData[1] == 0x00)
                        parser = CffGlyphSource.TryLoad(ttfData);
                    else if (LooksLikeSfnt(ttfData, out var ff2IsOtto) && ff2IsOtto)
                        parser = CffGlyphSource.TryLoad(ttfData);
                    else if (ttfData.Length > 0)
                        parser = new GlyphOutlineParser(ttfData);
                }

                // CFF embedding: /FontFile3 with /Subtype /Type1C, /CIDFontType0C, or
                // /OpenType. CffGlyphSource unwraps the OpenType SFNT container if
                // present before parsing the CFF structure.
                if (parser is null)
                {
                    var fontFile3 = reader.ResolveStream(descriptor.Get("FontFile3"));
                    if (fontFile3 is not null)
                    {
                        var cffData = reader.DecodeStream(fontFile3);
                        if (cffData.Length > 0)
                            parser = CffGlyphSource.TryLoad(cffData);
                    }
                }

                // PostScript Type 1 embedding: /FontFile (no number). The stream
                // dict carries /Length1 (ASCII header) and /Length2 (eexec
                // encrypted body) byte counts that Type1GlyphSource needs to
                // know where to split. Falls through to system-font lookup when
                // the stream is missing or unparseable.
                if (parser is null)
                {
                    var fontFile1 = reader.ResolveStream(descriptor.Get("FontFile"));
                    if (fontFile1 is not null)
                    {
                        var t1Data = reader.DecodeStream(fontFile1);
                        if (LooksLikeSfnt(t1Data, out var isOpenTypeCff))
                        {
                            // Some PDFs embed a CIDFontType2 (TrueType) program under the
                            // /FontFile key instead of the standard /FontFile2.
                            // Parse the sfnt as TrueType/OpenType, not as Type1.
                            parser = isOpenTypeCff
                                ? CffGlyphSource.TryLoad(t1Data)
                                : new GlyphOutlineParser(t1Data);
                        }
                        else if (t1Data.Length > 0)
                        {
                            var len1 = (int)fontFile1.Dict.GetInt("Length1");
                            var len2 = (int)fontFile1.Dict.GetInt("Length2");
                            parser = Type1GlyphSource.TryLoad(t1Data, len1, len2);
                        }
                    }
                }
            }

            // Fallback: try to resolve host font by BaseFont name for non-subset
            // embeddings. For subset embeddings the encoding wouldn't match the
            // system font's CMap anyway, so don't even try.
            if (parser is null)
            {
                // Prefer the font dict's /BaseFont. Some non-embedded TrueType fonts
                // ship with no BaseFont — the host-font name (Arial / Arial,Bold) lives
                // only on FontDescriptor./FontName, and that's where Adobe Reader picks
                // it up too. Fall back to FontDescriptor./FontName, resolving the
                // indirect ref explicitly rather than relying on GetName.
                var baseFont = fontDict.GetName("BaseFont");
                if (baseFont is null && descriptor is not null
                    && reader.Resolve(descriptor.Get("FontName")) is PdfName descFontName)
                {
                    baseFont = descFontName.Value;
                }
                if (baseFont is not null)
                {
                    var isClassicSubset = baseFont.Length > 7 && baseFont[6] == '+' &&
                        baseFont[..6].All(c => c >= 'A' && c <= 'Z');
                    if (!isClassicSubset)
                    {
                        var systemTtf = SystemFontResolver.Resolve(baseFont, out hScale);
                        if (systemTtf is not null)
                            parser = new GlyphOutlineParser(systemTtf);
                    }
                }

                // Last resort: the BaseFont name matched no installed family (an obfuscated
                // name like "HE108E", or a Multiple-Master Type1 with no embedded program).
                // Substitute a Standard-14 face chosen from the FontDescriptor's flags/style
                // so the text still renders — the simple font's /Encoding maps each code to a
                // glyph name, which the substitute's cmap resolves. CID (Type0) fonts have
                // their own CJK fallback, so this applies only to simple fonts.
                if (parser is null && descriptor is not null && fontDict.GetName("Subtype") != "Type0")
                {
                    var sub = SystemFontResolver.ResolveDescriptorSubstitute(fontDict, descriptor, reader, out hScale);
                    if (sub is not null)
                        parser = new GlyphOutlineParser(sub);
                }
            }
        }
        catch
        {
            // Failed to parse font — will use fallback (or render nothing)
        }

        cache[fontName] = (parser, hScale);
        horizontalScale = hScale;
        return parser;
    }

    private static FontMetrics? GetFontMetrics(RenderContext ctx, string? fontName)
        => GetFontMetrics(ctx.FontDicts, ctx.Reader, fontName);

    internal static FontMetrics? GetFontMetrics(
        Dictionary<string, PdfDictionary>? fontDicts, IO.PdfReader reader, string? fontName)
    {
        if (fontName is null || fontDicts is null) return null;
        if (!fontDicts.TryGetValue(fontName, out var fontDict)) return null;
        try
        {
            return FontMetrics.FromFontDict(fontDict, reader);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Blit a single-channel alpha mask onto the RGBA pixel buffer with the given color.</summary>
    private static void BlitAlphaMask(RenderContext ctx, byte[] alpha, int maskW, int maskH,
        int dstX, int dstY, byte r, byte g, byte b, byte a)
    {
        for (var my = 0; my < maskH; my++)
        {
            var dy = dstY + my;
            if (dy < 0 || dy >= ctx.PixelH) continue;
            for (var mx = 0; mx < maskW; mx++)
            {
                var dx = dstX + mx;
                if (dx < 0 || dx >= ctx.PixelW) continue;

                var maskVal = alpha[my * maskW + mx];
                if (maskVal == 0) continue;

                var effectiveA = (byte)((maskVal * a) / 255);
                SetPixel(ctx, dx, dy, r, g, b, effectiveA);
            }
        }
    }

    // ── XObject dispatch ────────────────────────────────────────────


    private static void DrawXObject(RenderContext ctx, string name, GraphicsState state,
        Dictionary<string, PdfDictionary>? extGStates)
    {
        if (ctx.AllXObjects is null) return;
        if (!ctx.AllXObjects.TryGetValue(name, out var xobj)) return;

        var subtype = xobj.Dict.GetName("Subtype");
        if (subtype == "Image")
            DrawImage(ctx, xobj, state);
        else if (subtype == "Form")
            DrawFormXObject(ctx, xobj, state, extGStates);
    }

    // ── Image rendering ─────────────────────────────────────────────

    /// <summary>
    /// Decode an explicit /Mask stencil image (PDF 32000 §8.9.6.3) into a flat byte[]
    /// of per-pixel alpha values (0=masked/transparent, 255=painted/opaque). The mask
    /// is a 1-bit ImageMask XObject; its sample value 1 masks the base image by default
    /// (/Decode [0 1]) and /Decode [1 0] inverts it. The mask may use any resolution
    /// independent of the base image — it is sampled in the base image's coordinate
    /// space by the blit. Returns null when the entry is absent, is a colour-key array,
    /// or cannot be decoded as a 1-bit stencil.
    /// </summary>
    internal static byte[]? ResolveStencilMaskAlpha(PdfObject? maskRef, IO.PdfReader reader, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (maskRef is null) return null;
        // A colour-key /Mask is a PdfArray, not a stream; not handled here.
        var stream = reader.ResolveStream(maskRef);
        if (stream is null) return null;
        var d = stream.Dict;
        var w = (int)d.GetInt("Width");
        var h = (int)d.GetInt("Height");
        if (w <= 0 || h <= 0) return null;
        byte[] decoded;
        try { decoded = reader.DecodeStream(stream); }
        catch { return null; }
        var rowBytes = (w + 7) / 8;
        if (decoded.Length < (long)rowBytes * h) return null; // not the 1-bit stencil we expect
        // Default /Decode [0 1]: sample 1 ⇒ masked. /Decode [1 0] flips it.
        var invert = false;
        if (d.Get("Decode") is PdfArray da && da.Count >= 2)
            invert = NumFrom(da[0]) > NumFrom(da[1]);
        var alpha = new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            var rowBase = y * rowBytes;
            for (int x = 0; x < w; x++)
            {
                var bit = (decoded[rowBase + (x >> 3)] >> (7 - (x & 7))) & 1;
                if (invert) bit ^= 1;
                alpha[y * w + x] = bit == 1 ? (byte)0 : (byte)255; // 1 ⇒ masked out
            }
        }
        width = w;
        height = h;
        return alpha;
    }

    // Repair a soft mask's /DecodeParms /Colors to 1 (its true single-component value) when a
    // predictor is active and the producer left it at the parent image's component count.
    // Handles /DecodeParms as a single dict or a per-filter array. Idempotent.
    private static void ForceSoftMaskPredictorColors(PdfObject? decodeParms, IO.PdfReader reader)
    {
        switch (reader.Resolve(decodeParms))
        {
            case PdfDictionary dp:
                if (dp.GetInt("Predictor") > 1 && dp.GetInt("Colors", 1) != 1)
                    dp.Set("Colors", new PdfInteger(1));
                break;
            case PdfArray arr:
                foreach (var el in arr)
                    if (reader.Resolve(el) is PdfDictionary edp
                        && edp.GetInt("Predictor") > 1 && edp.GetInt("Colors", 1) != 1)
                        edp.Set("Colors", new PdfInteger(1));
                break;
        }
    }

    /// <summary>
    /// Decode an /SMask soft-mask image (PDF 32000 §11.6.5.3) into a flat byte[]
    /// of per-pixel alpha values (0=transparent, 255=opaque). The SMask is always
    /// a DeviceGray, 8-bpc image XObject; a /Decode [a b] entry can invert the
    /// mapping. Returns null if the entry is missing or the stream cannot be
    /// decoded as a grayscale image.
    /// </summary>
    internal static byte[]? ResolveSMaskAlpha(PdfObject? smaskRef, IO.PdfReader reader, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (smaskRef is null) return null;
        var stream = reader.ResolveStream(smaskRef);
        if (stream is null) return null;
        var d = stream.Dict;
        var w = (int)d.GetInt("Width");
        var h = (int)d.GetInt("Height");
        if (w <= 0 || h <= 0) return null;

        // A soft mask is a single-component DeviceGray image (PDF 32000 §11.6.5.1). Some
        // producers copy the parent image's /DecodeParms onto the mask verbatim, leaving
        // /Colors at the parent's component count (e.g. 4 for a CMYK base image). A PNG/TIFF
        // predictor unfiltered with the wrong /Colors uses the wrong per-row stride and
        // yields fewer bytes than W*H, so the 8-bpc branch below rejects it, the mask is
        // dropped, and the image composites fully opaque (occluding what should show through).
        // Force the mask's own predictor /Colors to its true value of 1.
        ForceSoftMaskPredictorColors(d.Get("DecodeParms"), reader);

        byte[] decoded;
        try { decoded = reader.DecodeStream(stream); }
        catch { return null; }

        // A soft mask compressed with DCTDecode/JPXDecode arrives here still encoded
        // (DecodeStream leaves image-specific filters in place for the renderer to
        // handle). Decode it to a grayscale alpha plane; otherwise the raw codestream
        // bytes are mistaken for 8-bpc samples and the masked image composites to
        // near-black.
        if (decoded.Length > 2 && decoded[0] == 0xFF && decoded[1] == 0xD8)
        {
            try
            {
                var (jp, jw, jh, jc) = IO.Filters.JpegDecoder.Decode(decoded);
                width = jw; height = jh;
                return JpegPlaneToAlpha(jp, jw, jh, jc);
            }
            catch { return null; }
        }
        bool smJ2k = (decoded.Length > 3 && decoded[0] == 0xFF && decoded[1] == 0x4F)
            || (decoded.Length > 12 && decoded[0] == 0x00 && decoded[1] == 0x00 && decoded[2] == 0x00
                && decoded[3] == 0x0C && decoded[4] == 0x6A && decoded[5] == 0x50);
        if (smJ2k)
        {
            if (IO.Filters.JpxDecoder.TryDecode(decoded, out var jp, out var jw, out var jh, out var jc))
            {
                width = jw; height = jh;
                return JpegPlaneToAlpha(jp, jw, jh, jc);
            }
            return null;
        }

        var bpc = (int)d.GetInt("BitsPerComponent");
        if (bpc == 0) bpc = 8;

        // Decode the bytes into a W*H byte buffer of alpha values.
        byte[] alpha;
        if (bpc == 8 && decoded.Length >= w * h)
        {
            alpha = new byte[w * h];
            Array.Copy(decoded, alpha, w * h);
        }
        else if (bpc == 1)
        {
            // 1-bpc soft mask: pack 8 alpha bits per byte, MSB-first per row,
            // each row padded to a byte. Convert to 0/255.
            alpha = new byte[w * h];
            var rowBytes = (w + 7) / 8;
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var bi = y * rowBytes + x / 8;
                    if (bi >= decoded.Length) break;
                    var bit = (decoded[bi] >> (7 - x % 8)) & 1;
                    alpha[y * w + x] = bit == 1 ? (byte)255 : (byte)0;
                }
            }
        }
        else
        {
            return null;
        }

        // PDF 32000 §11.6.5.3: soft-mask sample values are alpha (0=transparent,
        // max=opaque). A /Decode [1 0] entry reverses that mapping, which a layered
        // scan (a JPEG2000 "text colour" overlay gated by a 1-bpc JBIG2 mask
        // marked /Decode [1 0]) relies on to confine the overlay to the glyph pixels
        // instead of flooding the page.
        if (GrayDecodeInverts(d))
            for (var i = 0; i < alpha.Length; i++) alpha[i] = (byte)(255 - alpha[i]);

        width = w;
        height = h;
        return alpha;
    }

    /// <summary>Reduce a decoded image plane (gray or RGB) to a W×H 8-bit alpha buffer.</summary>
    private static byte[] JpegPlaneToAlpha(byte[] pixels, int w, int h, int comps)
    {
        var alpha = new byte[w * h];
        if (comps <= 1)
        {
            for (int i = 0; i < alpha.Length && i < pixels.Length; i++) alpha[i] = pixels[i];
        }
        else
        {
            for (int i = 0; i < w * h; i++)
            {
                int s = i * comps;
                if (s + 2 >= pixels.Length) break;
                alpha[i] = (byte)((pixels[s] * 299 + pixels[s + 1] * 587 + pixels[s + 2] * 114) / 1000);
            }
        }
        return alpha;
    }
}

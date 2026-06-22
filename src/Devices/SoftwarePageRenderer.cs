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
public sealed class SoftwarePageRenderer : IPageRenderer
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
        // template renderer (GDI+) does the same and visual comparison is
        // sensitive to off-by-one pixel dimension.
        // Size the image to the crop box (clipped to the media box), not the media
        // box: the reference renderer presents only the cropped region. Rotation
        // swaps the visible dimensions exactly as it does for the media box.
        var crop = EffectiveCropRect(page);
        var rot = ((page.RotateDegrees % 360) + 360) % 360;
        var visW = (rot == 90 || rot == 270) ? crop.Height : crop.Width;
        var visH = (rot == 90 || rot == 270) ? crop.Width : crop.Height;
        var pixelW = FloorSnap(visW * xDpi / 72.0);
        var pixelH = FloorSnap(visH * yDpi / 72.0);
        return RenderPageAtPixelSize(page, pixelW, pixelH);
    }

    /// <summary>
    /// Render a PDF page directly at the requested pixel dimensions (no resample).
    /// Preserves the AA scanline filler's fractional coverage on thin strokes — a
    /// render-at-high-DPI-then-downsample detour smears those coverage values back
    /// toward binary when neighbouring source rows differ, which is how we were losing
    /// the 50%-grey page-frame edges of the Template PNGs.
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
    /// Floor of <paramref name="v"/> that snaps to the nearest integer if within 1e-6.
    /// GDI+ (the reference renderer) truncates page-size*DPI/72 to int, so we match
    /// that behaviour. The snap guard prevents FP noise on exact-integer pages
    /// (e.g. 792pt * 150/72 = 1650.0000000002 in IEEE 754) from flooring to 1649.
    /// </summary>
    internal static int FloorSnap(double v)
    {
        var rounded = Math.Round(v);
        if (Math.Abs(v - rounded) < 1e-6) return (int)rounded;
        return (int)v; // truncation == floor for positive values
    }

    /// <summary>
    /// The region of the page that is rasterised to the output image: the crop box
    /// clipped to the media box (PDF 32000 §14.11.2). When the page declares no crop
    /// box this equals the media box. Image-export devices size the canvas to this
    /// rectangle and offset content by its lower-left corner, so anything outside the
    /// crop area falls off the canvas — matching the reference renderer, which presents
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
        // An empty decoded string with non-empty rawBytes can still happen for CID fonts whose
        // ToUnicode CMap is missing — we must still walk rawBytes to advance the cursor.
        if (string.IsNullOrEmpty(text) && (rawBytes is null || rawBytes.Length == 0)) return;

        // Inherit the active blend mode for glyph blits.
        ctx.CurrentBlendMode = state.BlendMode;
        ctx.SoftMaskAlpha = state.SoftMask is { } sm__ ? ResolveSoftMaskAlpha(ctx, sm__) : null;

        // Type 3 fonts (PDF 32000 §9.6.5) define each glyph as its own PDF content
        // stream stored under /CharProcs. They show up most often in old dot-matrix
        // report/invoice PDFs (33393's F0 is a Type 3 with inline-ImageMask glyphs
        // for the addresses, invoice numbers, table rows). Detect and route before
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
        // (e.g. mirrored text for some XFA-derived forms like 31992). Use |fontSize|
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
        if (cidInfo is not null && cidInfo.IsTwoByteEncoding && rawBytes is not null)
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
            if (names[code] is { } n && parser.GidForName(n) is var gid && gid > 0)
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

        for (var i = 0; i + 1 < rawBytes.Length; i += 2)
        {
            var code = (rawBytes[i] << 8) | rawBytes[i + 1];
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
    /// integer code start and a sequence of glyph names). For the Type 3 fonts we
    /// see in 33393 / 33772 the /Differences array names every byte explicitly,
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
    /// Fill the byte→glyph map for a named base encoding. We currently inline only
    /// the WinAnsiEncoding-style ASCII printable subset — Type 3 fonts in the wild
    /// (33393's F0) carry an explicit /Differences entry for every used code anyway,
    /// so this minimum table is enough to give .notdef → printable-ASCII names for
    /// any byte the /Differences doesn't override.
    /// </summary>
    private static void ApplyBaseEncoding(string?[] result, string baseEncodingName)
    {
        // ASCII printable (0x20..0x7E) — same in StandardEncoding / WinAnsi / MacRoman
        // for the alphanumeric range that matters for Type 3 fallback. Extended
        // 0x80..0xFF differs across encodings; left as .notdef here because the
        // /Differences override is what carries the meaningful mappings in practice.
        var ascii = new[]
        {
            "space","exclam","quotedbl","numbersign","dollar","percent","ampersand","quoteright",
            "parenleft","parenright","asterisk","plus","comma","hyphen","period","slash",
            "zero","one","two","three","four","five","six","seven","eight","nine",
            "colon","semicolon","less","equal","greater","question","at",
            "A","B","C","D","E","F","G","H","I","J","K","L","M","N","O","P","Q","R","S","T","U","V","W","X","Y","Z",
            "bracketleft","backslash","bracketright","asciicircum","underscore","quoteleft",
            "a","b","c","d","e","f","g","h","i","j","k","l","m","n","o","p","q","r","s","t","u","v","w","x","y","z",
            "braceleft","bar","braceright","asciitilde"
        };
        for (var i = 0; i < ascii.Length; i++) result[0x20 + i] = ascii[i];
        _ = baseEncodingName; // currently all base encodings share the printable-ASCII range
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
                    if (ttfData.Length > 0)
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
        // scan (33126: a JPEG2000 "text colour" overlay gated by a 1-bpc JBIG2 mask
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

    private static void DrawImage(RenderContext ctx, PdfStream xobjStream, GraphicsState state)
    {
        var dict = xobjStream.Dict;
        var imgW = (int)dict.GetInt("Width");
        var imgH = (int)dict.GetInt("Height");
        if (imgW <= 0 || imgH <= 0) return;

        // Skip an image whose optional-content group/membership is hidden by the
        // document's default configuration (PDF 32000 §8.11.4.4).
        if (IsOcHidden(dict.Get("OC"), ctx.Reader, ctx.OcgHidden)) return;

        // Inherit blend mode and fill-alpha (CA/ca via /gs) for this draw. Set on the
        // context up front; Blit* paths read it via SetPixel.
        ctx.CurrentBlendMode = state.BlendMode;
        ctx.SoftMaskAlpha = state.SoftMask is { } sm__ ? ResolveSoftMaskAlpha(ctx, sm__) : null;

        byte[] decoded;
        try { decoded = ctx.Reader.DecodeStream(xobjStream); }
        catch { return; }

        // Check for image mask
        var isImageMask = dict.Get("ImageMask") is PdfBoolean imb && imb.Value;

        // /SMask: PDF 32000 §11.6.5.3 — an indirect reference to a soft-mask
        // grayscale image (W×H bytes, 8bpc). Sample value = per-pixel opacity.
        // Resolved here so the various Blit branches below can pass it through.
        var smask = ResolveSMaskAlpha(dict.Get("SMask"), ctx.Reader, out var smaskW, out var smaskH);

        // /Mask: PDF 32000 §8.9.6.3 — an explicit 1-bit stencil mask selecting which
        // base-image pixels are painted. Folded into the same per-pixel alpha plane the
        // blit branches consume; when both /SMask and /Mask are present their opacities
        // multiply.
        var stencil = ResolveStencilMaskAlpha(dict.Get("Mask"), ctx.Reader, out var stencilW, out var stencilH);
        if (stencil is not null)
        {
            if (smask is null)
            {
                smask = stencil; smaskW = stencilW; smaskH = stencilH;
            }
            else
            {
                // Combine on the SMask grid (both are sampled in base-image coords).
                var combined = new byte[smaskW * smaskH];
                for (int y = 0; y < smaskH; y++)
                    for (int x = 0; x < smaskW; x++)
                    {
                        var sxr = x * stencilW / smaskW;
                        var syr = y * stencilH / smaskH;
                        combined[y * smaskW + x] = (byte)(smask[y * smaskW + x] * stencil[syr * stencilW + sxr] / 255);
                    }
                smask = combined;
            }
        }

        var ctm = state.Ctm;

        // A rotated or skewed image CTM (e.g. any image on a /Rotate 90|270 page) cannot
        // be represented by the axis-aligned blit paths below — they sample the source on
        // a straight x/y scale, so they place the image at the wrong size and orientation
        // (e.g. a /Rotate 270 page drew its CCITT text mask 3.7x off-canvas and
        // the page rendered blank). Decode the image to RGBA once and inverse-map each
        // destination pixel through the CTM. Axis-aligned images keep their optimised paths.
        if (Math.Abs(ctm[1]) > 1e-4 || Math.Abs(ctm[2]) > 1e-4)
        {
            // A rotated/skewed mask painted while a /Pattern fill is active shows the
            // pattern through the stencil, not a flat colour (see DrawImageMaskWithPattern).
            if (isImageMask && state.FillPatternName is not null)
            {
                var inv = dict.Get("Decode") is PdfArray dm && dm.Count >= 2 && NumFrom(dm[0]) > NumFrom(dm[1]);
                DrawImageMaskWithPatternAffine(ctx, decoded, imgW, imgH, ctm, state, inv);
                return;
            }
            var aff = DecodeImageToRgba(ctx, dict, decoded, imgW, imgH, isImageMask, smask, smaskW, smaskH, state);
            if (aff is not null) BlitRgbaAffine(ctx, aff.Value.rgba, aff.Value.w, aff.Value.h, ctm, state.FillAlpha);
            return;
        }

        // Compute destination rectangle in page coordinates. The PDF unit square
        // (0,0)-(1,1) maps to ctm[5]…ctm[5]+ctm[3] vertically; either bound can be
        // higher depending on the sign of ctm[3]. Most PDFs use positive ctm[3]
        // (image-y=0 at the bottom of the rect, image-y=1 at the top), but
        // generators that emit pre-flipped image data use ctm[3]<0 to compensate.
        // Pick the higher PDF y as the top of the rendered rectangle and the
        // lower as the bottom; pixel coordinates are origin-top-left, so the
        // higher PDF y becomes the lower pixel row.
        var destX = Math.Min(ctm[4], ctm[4] + ctm[0]);
        var destW = Math.Abs(ctm[0]);
        var destH = Math.Abs(ctm[3]);
        var topPdfY = Math.Max(ctm[5], ctm[5] + ctm[3]);
        if (destW < 0.01) destW = imgW;
        if (destH < 0.01) destH = imgH;

        // Convert to pixel coords. Round (not truncate) matches GDI+ behaviour on the
        // nearest integer pixel, which keeps image placement aligned with the template.
        var px = (int)Math.Round((destX - ctx.MediaBox.LLX) * ctx.Scale);
        var py = ctx.PixelH - (int)Math.Round((topPdfY - ctx.MediaBox.LLY) * ctx.Scale);
        var pw = (int)Math.Round(destW * ctx.Scale);
        var ph = (int)Math.Round(destH * ctx.Scale);

        if (isImageMask)
        {
            // /Decode [a b]: when a > b (e.g. [1 0]) the bit-to-opacity mapping is
            // inverted from the default. PDF 32000 §8.9.5.1: Decode component values
            // map source samples to colour-component values; for ImageMasks the
            // default is [0 1] (sample 0 ⇒ paint, 1 ⇒ transparent) and [1 0] flips it.
            var invertDecode = false;
            if (dict.Get("Decode") is PdfArray decodeArr && decodeArr.Count >= 2)
                invertDecode = NumFrom(decodeArr[0]) > NumFrom(decodeArr[1]);
            // A mask painted while a /Pattern fill is active (e.g. PowerPoint masks a
            // gradient pattern through a stencil) shows the pattern, not a flat colour;
            // painting the stale solid fill over-inks it dark. Fill the stencil with the
            // pattern instead.
            if (state.FillPatternName is not null)
            {
                DrawImageMaskWithPattern(ctx, decoded, imgW, imgH, px, py, pw, ph, state, invertDecode);
                return;
            }
            DrawImageMask(ctx, decoded, imgW, imgH, px, py, pw, ph, state, invertDecode);
            return;
        }

        // Decode pixels based on color space
        var bpc = (int)dict.GetInt("BitsPerComponent");
        if (bpc == 0) bpc = 8;
        var csInfo = ResolveImageColorSpace(dict.Get("ColorSpace"), ctx.Reader);
        var cs = csInfo.BaseName;

        // Decode JPEG images
        if (decoded.Length > 2 && decoded[0] == 0xFF && decoded[1] == 0xD8)
        {
            try
            {
                var jpeg = IO.Filters.JpegDecoder.Decode(decoded);
                if (jpeg.components == 1)
                    BlitGray(ctx, jpeg.pixels, jpeg.width, jpeg.height, px, py, pw, ph, smask, smaskW, smaskH, state.FillAlpha);
                else if (jpeg.components >= 3)
                    BlitRGB(ctx, jpeg.pixels, jpeg.width, jpeg.height, px, py, pw, ph, smask, smaskW, smaskH, state.FillAlpha);
                return;
            }
            catch
            {
                // No fallback — leave area as page background rather than painting a
                // false gray rectangle over content that the template renders correctly.
                return;
            }
        }

        // JPEG 2000 (JPXDecode filter): a JP2 box wrapper (signature box
        // 00 00 00 0C 6A 50 …) or a bare J2K codestream (0xFF 0x4F SOC).
        var isJ2kFile = decoded.Length > 12
            && decoded[0] == 0x00 && decoded[1] == 0x00 && decoded[2] == 0x00 && decoded[3] == 0x0C
            && decoded[4] == 0x6A && decoded[5] == 0x50;
        var isJ2kCodestream = decoded.Length > 4 && decoded[0] == 0xFF && decoded[1] == 0x4F;
        if (isJ2kFile || isJ2kCodestream)
        {
            if (IO.Filters.JpxDecoder.TryDecode(decoded, out var jp, out var jw, out var jh, out var jc))
            {
                if (jc >= 3)
                    BlitRGB(ctx, jp, jw, jh, px, py, pw, ph, smask, smaskW, smaskH, state.FillAlpha);
                else
                    BlitGray(ctx, jp, jw, jh, px, py, pw, ph, smask, smaskW, smaskH, state.FillAlpha);
            }
            return;
        }

        // Indexed: unpack bit-packed palette indices and look up RGB per pixel.
        // 4-bpc indexed is common for palette-based screenshots; 8-bpc indexed also appears.
        if (csInfo.Palette is not null)
        {
            BlitIndexed(ctx, decoded, imgW, imgH, px, py, pw, ph, bpc, csInfo);
            return;
        }

        // CTM-driven mirror flags: a negative ctm[3] flips image data vertically
        // (image-data is top-down per PDF spec, so the rendered raster has to be
        // sampled bottom-up to land upright). Negative ctm[0] mirrors horizontally.
        var flipY = ctm[3] < 0;
        var flipX = ctm[0] < 0;

        // Render raw pixel data
        if (cs == "DeviceRGB" && bpc == 8 && decoded.Length >= imgW * imgH * 3)
            BlitRGB(ctx, decoded, imgW, imgH, px, py, pw, ph, smask, smaskW, smaskH, state.FillAlpha, flipY, flipX);
        else if (cs == "DeviceCMYK" && bpc == 8 && decoded.Length >= imgW * imgH * 4)
            BlitCMYK(ctx, decoded, imgW, imgH, px, py, pw, ph);
        else if (cs == "DeviceGray" && bpc == 8 && decoded.Length >= imgW * imgH)
            BlitGray(ctx, decoded, imgW, imgH, px, py, pw, ph, smask, smaskW, smaskH, state.FillAlpha);
        else if (cs == "DeviceGray" && (bpc == 2 || bpc == 4))
            BlitGray(ctx, UnpackGraySamples(decoded, imgW, imgH, bpc, GrayDecodeInverts(dict)), imgW, imgH, px, py, pw, ph, smask, smaskW, smaskH, state.FillAlpha);
        else if (csInfo.TintTransform is not null && (bpc is 1 or 2 or 4 or 8))
            // Single-colorant /Separation (or /DeviceN) image: map each sample through the
            // tint transform. A 1-bpc /Separation/Black image (sample 1 ⇒ full ink) would
            // otherwise reach BlitBilevel and render inverted (e.g. 35751_2's white-hanger-
            // on-black graphic comes out black-on-white).
            BlitRGB(ctx, SeparationSamplesToRgb(decoded, imgW, imgH, bpc, BuildSeparationLut(csInfo, GrayDecodeInverts(dict))),
                imgW, imgH, px, py, pw, ph, smask, smaskW, smaskH, state.FillAlpha, flipY, flipX);
        else if (bpc == 1)
            BlitBilevel(ctx, decoded, imgW, imgH, px, py, pw, ph);
        // No fallback — a solid gray rectangle over an unrecognised image is false content
        // for visual comparison. Leaving the area white (page background) is less damaging
        // than painting over real content in the template.
    }

    /// <summary>
    /// Decode an image XObject into a flat RGBA buffer (w*h*4) for the affine blit path
    /// (rotated/skewed CTMs). ImageMask pixels carry the current fill colour with alpha
    /// 0/255; other formats are opaque (alpha 255) unless an /SMask supplies per-pixel
    /// alpha. Returns null for formats this path doesn't recognise.
    /// </summary>
    private static (byte[] rgba, int w, int h)? DecodeImageToRgba(RenderContext ctx, PdfDictionary dict,
        byte[] decoded, int imgW, int imgH, bool isImageMask, byte[]? smask, int smaskW, int smaskH, GraphicsState state)
    {
        if (imgW <= 0 || imgH <= 0 || (long)imgW * imgH * 4 > int.MaxValue) return null;

        if (isImageMask)
        {
            var rgbaM = new byte[imgW * imgH * 4];
            byte fr = (byte)(state.FillR * 255), fg = (byte)(state.FillG * 255), fb = (byte)(state.FillB * 255);
            var invert = dict.Get("Decode") is PdfArray dm && dm.Count >= 2 && NumFrom(dm[0]) > NumFrom(dm[1]);
            int paintBit = invert ? 1 : 0;
            long rb = (imgW + 7) / 8;
            for (int y = 0; y < imgH; y++)
                for (int x = 0; x < imgW; x++)
                {
                    var bi = y * rb + (x >> 3);
                    int bit = bi < decoded.Length ? (decoded[(int)bi] >> (7 - (x & 7))) & 1 : 1;
                    if (bit == paintBit) { var o = (y * imgW + x) * 4; rgbaM[o] = fr; rgbaM[o + 1] = fg; rgbaM[o + 2] = fb; rgbaM[o + 3] = 255; }
                }
            return (rgbaM, imgW, imgH);
        }

        var bpc = (int)dict.GetInt("BitsPerComponent"); if (bpc == 0) bpc = 8;
        var csInfo = ResolveImageColorSpace(dict.Get("ColorSpace"), ctx.Reader);
        var cs = csInfo.BaseName;

        byte[]? rgb = null; int rw = imgW, rh = imgH;
        if (decoded.Length > 2 && decoded[0] == 0xFF && decoded[1] == 0xD8)
        {
            try { var j = IO.Filters.JpegDecoder.Decode(decoded); rw = j.width; rh = j.height; rgb = j.components == 1 ? GrayToRgbBuf(j.pixels, rw, rh) : j.pixels; }
            catch { return null; }
        }
        else if ((decoded.Length > 12 && decoded[0] == 0 && decoded[1] == 0 && decoded[2] == 0 && decoded[3] == 0x0C && decoded[4] == 0x6A && decoded[5] == 0x50)
                 || (decoded.Length > 4 && decoded[0] == 0xFF && decoded[1] == 0x4F))
        {
            if (IO.Filters.JpxDecoder.TryDecode(decoded, out var jp, out var jw, out var jh, out var jc)) { rw = jw; rh = jh; rgb = jc >= 3 ? jp : GrayToRgbBuf(jp, jw, jh); }
            else return null;
        }
        else if (csInfo.Palette is not null) rgb = IndexedToRgbBuf(decoded, imgW, imgH, bpc, csInfo);
        else if (cs == "DeviceRGB" && bpc == 8 && decoded.Length >= imgW * imgH * 3) rgb = decoded;
        else if (cs == "DeviceCMYK" && bpc == 8 && decoded.Length >= imgW * imgH * 4) rgb = CmykToRgbBuf(decoded, imgW, imgH);
        else if (cs == "DeviceGray" && bpc == 8 && decoded.Length >= imgW * imgH) rgb = GrayToRgbBuf(decoded, imgW, imgH);
        else if (cs == "DeviceGray" && (bpc == 2 || bpc == 4)) rgb = GrayToRgbBuf(UnpackGraySamples(decoded, imgW, imgH, bpc, GrayDecodeInverts(dict)), imgW, imgH);
        else if (csInfo.TintTransform is not null && bpc is 1 or 2 or 4 or 8) rgb = SeparationSamplesToRgb(decoded, imgW, imgH, bpc, BuildSeparationLut(csInfo, GrayDecodeInverts(dict)));
        else if (bpc == 1) rgb = BilevelToRgbBuf(decoded, imgW, imgH);
        else return null;

        if (rgb is null || rgb.Length < rw * rh * 3) return null;
        var rgba = new byte[rw * rh * 4];
        for (int i = 0, j = 0, k = 0; i < rw * rh; i++, j += 3, k += 4) { rgba[k] = rgb[j]; rgba[k + 1] = rgb[j + 1]; rgba[k + 2] = rgb[j + 2]; rgba[k + 3] = 255; }

        if (smask is not null && smaskW > 0 && smaskH > 0 && smask.Length >= smaskW * smaskH)
            for (int y = 0; y < rh; y++)
                for (int x = 0; x < rw; x++)
                    rgba[(y * rw + x) * 4 + 3] = smask[(y * smaskH / rh) * smaskW + (x * smaskW / rw)];

        return (rgba, rw, rh);
    }

    private static byte[] GrayToRgbBuf(byte[] g, int w, int h)
    {
        var o = new byte[w * h * 3];
        for (int i = 0, j = 0; i < w * h; i++, j += 3) { byte v = i < g.Length ? g[i] : (byte)255; o[j] = o[j + 1] = o[j + 2] = v; }
        return o;
    }

    private static byte[] CmykToRgbBuf(byte[] d, int w, int h)
    {
        var o = new byte[w * h * 3];
        for (int i = 0, j = 0, k = 0; i < w * h; i++, j += 4, k += 3)
        {
            CmykToRgbClamp(d[j] / 255.0, d[j + 1] / 255.0, d[j + 2] / 255.0, d[j + 3] / 255.0, out var r, out var gg, out var b);
            o[k] = ToByteClamp(r * 255); o[k + 1] = ToByteClamp(gg * 255); o[k + 2] = ToByteClamp(b * 255);
        }
        return o;
    }

    private static byte[] BilevelToRgbBuf(byte[] d, int w, int h)
    {
        var o = new byte[w * h * 3]; long rb = (w + 7) / 8;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var bi = y * rb + (x >> 3);
                byte v = (byte)(bi < d.Length ? (((d[(int)bi] >> (7 - (x & 7))) & 1) == 1 ? 255 : 0) : 255);
                var k = (y * w + x) * 3; o[k] = o[k + 1] = o[k + 2] = v;
            }
        return o;
    }

    private static byte[] IndexedToRgbBuf(byte[] data, int w, int h, int bpc, ImageColorSpaceInfo cs)
    {
        var pal = cs.Palette!; var pc = cs.PaletteComponents; var o = new byte[w * h * 3];
        var rowBytes = (w * bpc + 7) / 8; var maxIdx = pc > 0 ? pal.Length / pc - 1 : 0;
        for (int y = 0; y < h; y++)
        {
            var rb = (long)y * rowBytes;
            for (int x = 0; x < w; x++)
            {
                int idx;
                if (bpc == 8) { var bi = rb + x; idx = bi < data.Length ? data[(int)bi] : 0; }
                else { var bit = x * bpc; var bi = rb + bit / 8; idx = bi < data.Length ? (data[(int)bi] >> (8 - bpc - (bit & 7))) & ((1 << bpc) - 1) : 0; }
                if (idx > maxIdx) idx = maxIdx; if (idx < 0) idx = 0;
                var po = idx * pc; var k = (y * w + x) * 3;
                if (pc >= 3 && po + 2 < pal.Length) { o[k] = pal[po]; o[k + 1] = pal[po + 1]; o[k + 2] = pal[po + 2]; }
                else if (pc == 1 && po < pal.Length) { o[k] = o[k + 1] = o[k + 2] = pal[po]; }
            }
        }
        return o;
    }

    /// <summary>
    /// Paint an RGBA source image under an arbitrary affine CTM by inverse-mapping each
    /// destination pixel back into the image's unit square. Used for rotated/skewed image
    /// CTMs that the axis-aligned blit paths can't represent (point-sampled).
    /// </summary>
    private static void BlitRgbaAffine(RenderContext ctx, byte[] rgba, int srcW, int srcH, double[] ctm, double fillAlpha)
    {
        if (srcW <= 0 || srcH <= 0 || rgba.Length < srcW * srcH * 4) return;
        double det = ctm[0] * ctm[3] - ctm[1] * ctm[2];
        if (Math.Abs(det) < 1e-12) return;
        double inv = 1.0 / det;

        // Destination bounding box from the four transformed unit-square corners.
        double x0 = ctm[4], x1 = ctm[4] + ctm[0], x2 = ctm[4] + ctm[2], x3 = ctm[4] + ctm[0] + ctm[2];
        double y0 = ctm[5], y1 = ctm[5] + ctm[1], y2 = ctm[5] + ctm[3], y3 = ctm[5] + ctm[1] + ctm[3];
        double minX = Math.Min(Math.Min(x0, x1), Math.Min(x2, x3)), maxX = Math.Max(Math.Max(x0, x1), Math.Max(x2, x3));
        double minY = Math.Min(Math.Min(y0, y1), Math.Min(y2, y3)), maxY = Math.Max(Math.Max(y0, y1), Math.Max(y2, y3));
        int pxMin = Math.Max(0, (int)Math.Floor((minX - ctx.MediaBox.LLX) * ctx.Scale));
        int pxMax = Math.Min(ctx.PixelW, (int)Math.Ceiling((maxX - ctx.MediaBox.LLX) * ctx.Scale));
        int pyMin = Math.Max(0, ctx.PixelH - (int)Math.Ceiling((maxY - ctx.MediaBox.LLY) * ctx.Scale));
        int pyMax = Math.Min(ctx.PixelH, ctx.PixelH - (int)Math.Floor((minY - ctx.MediaBox.LLY) * ctx.Scale));

        for (int dy = pyMin; dy < pyMax; dy++)
        {
            double uy = (ctx.PixelH - dy - 0.5) / ctx.Scale + ctx.MediaBox.LLY;
            for (int dx = pxMin; dx < pxMax; dx++)
            {
                double ux = (dx + 0.5) / ctx.Scale + ctx.MediaBox.LLX;
                double rx = ux - ctm[4], ry = uy - ctm[5];
                double u = (rx * ctm[3] - ry * ctm[2]) * inv;
                double v = (-rx * ctm[1] + ry * ctm[0]) * inv;
                if (u < 0 || u >= 1 || v < 0 || v >= 1) continue;
                int sx = (int)(u * srcW); if (sx >= srcW) sx = srcW - 1;
                int sy = (int)((1.0 - v) * srcH); if (sy >= srcH) sy = srcH - 1; if (sy < 0) sy = 0;
                int o = (sy * srcW + sx) * 4;
                byte a = rgba[o + 3];
                if (a == 0) continue;
                byte fa = fillAlpha >= 1.0 ? a : (byte)(a * fillAlpha);
                SetPixel(ctx, dx, dy, rgba[o], rgba[o + 1], rgba[o + 2], fa);
            }
        }
    }

    /// <summary>
    /// Render an inline image (BI/ID/EI). Inline images carry the same fields as
    /// regular image XObjects but inside the content stream — most commonly used
    /// for Type 3 font glyphs (each character is a tiny ImageMask). Honours
    /// /ImageMask + /Decode [a b], applies any /Filter chain via StreamFilter,
    /// and paints through the existing DrawImageMask / BlitRGB / BlitGray paths.
    /// CTM at the BI operator (captured by the caller via parser.State.Ctm) maps
    /// the unit square to the destination rect — same convention as XObject Do.
    /// </summary>
    private static void DrawInlineImage(RenderContext ctx, PdfDictionary dict, byte[] data, GraphicsState state)
    {
        var imgW = (int)dict.GetInt("Width");
        var imgH = (int)dict.GetInt("Height");
        if (imgW <= 0 || imgH <= 0) return;

        byte[] decoded;
        try { decoded = IO.Filters.StreamFilter.Decode(data, dict); }
        catch { return; }

        var ctm = state.Ctm;
        var destX = ctm[4];
        var destY = ctm[5];
        var destW = Math.Abs(ctm[0]);
        var destH = Math.Abs(ctm[3]);
        if (destW < 0.01) destW = imgW;
        if (destH < 0.01) destH = imgH;
        var px = (int)Math.Round((destX - ctx.MediaBox.LLX) * ctx.Scale);
        var py = ctx.PixelH - (int)Math.Round((destY + destH - ctx.MediaBox.LLY) * ctx.Scale);
        var pw = (int)Math.Round(destW * ctx.Scale);
        var ph = (int)Math.Round(destH * ctx.Scale);

        var isMask = dict.Get("ImageMask") is PdfBoolean im && im.Value;
        if (isMask)
        {
            var invertDecode = false;
            if (dict.Get("Decode") is PdfArray decodeArr && decodeArr.Count >= 2)
                invertDecode = NumFrom(decodeArr[0]) > NumFrom(decodeArr[1]);
            if (state.FillPatternName is not null)
            {
                DrawImageMaskWithPattern(ctx, decoded, imgW, imgH, px, py, pw, ph, state, invertDecode);
                return;
            }
            DrawImageMask(ctx, decoded, imgW, imgH, px, py, pw, ph, state, invertDecode);
            return;
        }

        var bpc = (int)dict.GetInt("BitsPerComponent");
        if (bpc == 0) bpc = 8;
        var csInfo = ResolveImageColorSpace(dict.Get("ColorSpace"), ctx.Reader);
        var cs = csInfo.BaseName;
        if (cs == "DeviceRGB" && bpc == 8 && decoded.Length >= imgW * imgH * 3)
            BlitRGB(ctx, decoded, imgW, imgH, px, py, pw, ph, null, 0, 0, state.FillAlpha);
        else if (cs == "DeviceGray" && bpc == 8 && decoded.Length >= imgW * imgH)
            BlitGray(ctx, decoded, imgW, imgH, px, py, pw, ph, null, 0, 0, state.FillAlpha);
        else if (bpc == 1)
            BlitBilevel(ctx, decoded, imgW, imgH, px, py, pw, ph);
    }

    /// <summary>True when a DeviceGray image's /Decode array reverses the default [0 1]
    /// mapping (i.e. sample 0 ⇒ white instead of black).</summary>
    internal static bool GrayDecodeInverts(PdfDictionary dict)
        => dict.Get("Decode") is PdfArray d && d.Count >= 2 && NumFrom(d[0]) > NumFrom(d[1]);

    /// <summary>
    /// Expand a sub-byte (1/2/4-bpc) DeviceGray image to one 8-bit grey byte per pixel.
    /// Samples are packed MSB-first and each row starts on a byte boundary (PDF 32000
    /// §8.9.5.2). The N-bit value is scaled to 0..255; /Decode [1 0] inverts it.
    /// </summary>
    internal static byte[] UnpackGraySamples(byte[] data, int w, int h, int bpc, bool invert)
    {
        var outp = new byte[w * h];
        var rowBytes = (w * bpc + 7) / 8;
        var maxv = (1 << bpc) - 1;
        for (int y = 0; y < h; y++)
        {
            int rowBase = y * rowBytes;
            for (int x = 0; x < w; x++)
            {
                int bitPos = x * bpc;
                int bi = rowBase + (bitPos >> 3);
                int shift = 8 - bpc - (bitPos & 7);
                int sample = bi < data.Length ? (data[bi] >> shift) & maxv : 0;
                int v = sample * 255 / maxv;
                outp[y * w + x] = (byte)(invert ? 255 - v : v);
            }
        }
        return outp;
    }

    /// <summary>Paint an ImageMask whose current fill is a pattern: build a device-space
    /// coverage stencil from the mask bits (AND-ed with the active clip) and fill the mask's
    /// quad with the pattern through it, so the pattern shows through the stencil rather than
    /// the stale solid fill colour over-inking the masked region.</summary>
    private static void DrawImageMaskWithPattern(RenderContext ctx, byte[] decoded, int imgW, int imgH,
        int px, int py, int pw, int ph, GraphicsState state, bool invertDecode)
    {
        if (state.FillPatternName is null || pw <= 0 || ph <= 0 || imgW <= 0 || imgH <= 0) return;
        var rowBytes = (imgW + 7) / 8;
        var paintBit = invertDecode ? 1 : 0;
        // Device-resolution stencil: a dest pixel is painted when the majority of the source
        // mask bits it covers are paint bits (area-averaged downsample, same mapping as
        // DrawImageMask). AND with the active clip so the fill stays inside both.
        var cov = new byte[ctx.PixelW * ctx.PixelH];
        for (int dy = 0; dy < ph; dy++)
        {
            int destY = py + dy;
            if (destY < 0 || destY >= ctx.PixelH) continue;
            long sy0 = (long)dy * imgH / ph, sy1 = (long)(dy + 1) * imgH / ph;
            if (sy1 == sy0) sy1 = sy0 + 1;
            for (int dx = 0; dx < pw; dx++)
            {
                int destX = px + dx;
                if (destX < 0 || destX >= ctx.PixelW) continue;
                long sx0 = (long)dx * imgW / pw, sx1 = (long)(dx + 1) * imgW / pw;
                if (sx1 == sx0) sx1 = sx0 + 1;
                long paint = 0, total = 0;
                for (long sy = sy0; sy < sy1; sy++)
                {
                    long rb = sy * rowBytes;
                    for (long sx = sx0; sx < sx1; sx++)
                    {
                        long bi = rb + (sx >> 3);
                        int bit = bi < decoded.Length ? (decoded[(int)bi] >> (int)(7 - (sx & 7))) & 1 : 1 - paintBit;
                        if (bit == paintBit) paint++;
                        total++;
                    }
                }
                if (total > 0 && paint * 2 >= total)
                    cov[destY * ctx.PixelW + destX] = 255;
            }
        }
        if (ctx.ClipMask is { } outer)
            for (int i = 0; i < cov.Length; i++)
                if (outer[i] == 0) cov[i] = 0;

        // Fill the mask's unit-square quad with the pattern, clipped to the stencil.
        var quad = new System.Collections.Generic.List<PathCommand>
        {
            new(PathOp.MoveTo, 0, 0), new(PathOp.LineTo, 1, 0),
            new(PathOp.LineTo, 1, 1), new(PathOp.LineTo, 0, 1), new(PathOp.LineTo, 0, 0),
        };
        var quadEdges = BuildPathEdgeTable(quad, state.Ctm, ctx);
        var savedClip = ctx.ClipMask;
        ctx.ClipMask = cov;
        try { FillWithPattern(ctx, quadEdges, false, state.FillPatternName, state); }
        finally { ctx.ClipMask = savedClip; }
    }

    /// <summary>Pattern-fill an ImageMask under an arbitrary affine CTM. Builds the coverage
    /// stencil by inverse-mapping each destination pixel back through the CTM into the mask's
    /// unit square (same inverse map as BlitRgbaAffine), then fills the transformed unit
    /// square with the pattern through that stencil.</summary>
    private static void DrawImageMaskWithPatternAffine(RenderContext ctx, byte[] decoded,
        int imgW, int imgH, double[] ctm, GraphicsState state, bool invertDecode)
    {
        if (state.FillPatternName is null || imgW <= 0 || imgH <= 0) return;
        double det = ctm[0] * ctm[3] - ctm[1] * ctm[2];
        if (Math.Abs(det) < 1e-12) return;
        double inv = 1.0 / det;
        long rowBytes = (imgW + 7) / 8;
        int paintBit = invertDecode ? 1 : 0;

        double x0 = ctm[4], x1 = ctm[4] + ctm[0], x2 = ctm[4] + ctm[2], x3 = ctm[4] + ctm[0] + ctm[2];
        double y0 = ctm[5], y1 = ctm[5] + ctm[1], y2 = ctm[5] + ctm[3], y3 = ctm[5] + ctm[1] + ctm[3];
        double minX = Math.Min(Math.Min(x0, x1), Math.Min(x2, x3)), maxX = Math.Max(Math.Max(x0, x1), Math.Max(x2, x3));
        double minY = Math.Min(Math.Min(y0, y1), Math.Min(y2, y3)), maxY = Math.Max(Math.Max(y0, y1), Math.Max(y2, y3));
        int pxMin = Math.Max(0, (int)Math.Floor((minX - ctx.MediaBox.LLX) * ctx.Scale));
        int pxMax = Math.Min(ctx.PixelW, (int)Math.Ceiling((maxX - ctx.MediaBox.LLX) * ctx.Scale));
        int pyMin = Math.Max(0, ctx.PixelH - (int)Math.Ceiling((maxY - ctx.MediaBox.LLY) * ctx.Scale));
        int pyMax = Math.Min(ctx.PixelH, ctx.PixelH - (int)Math.Floor((minY - ctx.MediaBox.LLY) * ctx.Scale));

        var cov = new byte[ctx.PixelW * ctx.PixelH];
        bool any = false;
        for (int dy = pyMin; dy < pyMax; dy++)
        {
            double uy = (ctx.PixelH - dy - 0.5) / ctx.Scale + ctx.MediaBox.LLY;
            for (int dx = pxMin; dx < pxMax; dx++)
            {
                double ux = (dx + 0.5) / ctx.Scale + ctx.MediaBox.LLX;
                double rx = ux - ctm[4], ry = uy - ctm[5];
                double u = (rx * ctm[3] - ry * ctm[2]) * inv;
                double v = (-rx * ctm[1] + ry * ctm[0]) * inv;
                if (u < 0 || u >= 1 || v < 0 || v >= 1) continue;
                int sx = (int)(u * imgW); if (sx >= imgW) sx = imgW - 1;
                int sy = (int)((1.0 - v) * imgH); if (sy >= imgH) sy = imgH - 1; if (sy < 0) sy = 0;
                long bi = (long)sy * rowBytes + (sx >> 3);
                int bit = bi < decoded.Length ? (decoded[(int)bi] >> (7 - (sx & 7))) & 1 : 1 - paintBit;
                if (bit == paintBit) { cov[dy * ctx.PixelW + dx] = 255; any = true; }
            }
        }
        if (!any) return;
        if (ctx.ClipMask is { } outer)
            for (int i = 0; i < cov.Length; i++)
                if (outer[i] == 0) cov[i] = 0;

        var quad = new System.Collections.Generic.List<PathCommand>
        {
            new(PathOp.MoveTo, 0, 0), new(PathOp.LineTo, 1, 0),
            new(PathOp.LineTo, 1, 1), new(PathOp.LineTo, 0, 1), new(PathOp.LineTo, 0, 0),
        };
        var quadEdges = BuildPathEdgeTable(quad, ctm, ctx);
        var savedClip = ctx.ClipMask;
        ctx.ClipMask = cov;
        try { FillWithPattern(ctx, quadEdges, false, state.FillPatternName, state); }
        finally { ctx.ClipMask = savedClip; }
    }

    private static void DrawImageMask(RenderContext ctx, byte[] decoded, int imgW, int imgH,
        int px, int py, int pw, int ph, GraphicsState state, bool invertDecode = false)
    {
        var r = (byte)(state.FillR * 255);
        var g = (byte)(state.FillG * 255);
        var b = (byte)(state.FillB * 255);
        var rowBytes = (imgW + 7) / 8;

        // PDF 32000 §8.9.5.4 + §8.9.5.1: default /Decode [0 1] means bit=0 paints
        // the current fill colour and bit=1 is transparent. /Decode [1 0] flips
        // it. Type 3 fonts in old report PDFs commonly ship glyphs as inline
        // ImageMasks with /D [1 0] (33393's F0 dot-matrix font).
        var paintBit = invertDecode ? 1 : 0;

        // Iterate destination pixels. For each, area-average the source bits that
        // map to it: count paint-bits / total-bits inside the inverse-mapped src
        // rect, use that fraction as the fragment alpha. This is anti-aliased
        // downsampling — without it a 600×144 banner mask drawn at 144×35 pixels
        // (33393's "Invoice" / "Detail" / "Remittance" headers, 4× downscale)
        // hashes between paint and transparent depending on which source pixel
        // each dest happens to land on. Per-pixel area averaging makes the
        // banner stripe (~30% paint bits) appear as a solid colour fill while
        // letters carved into the banner (bit=1 holes) come out as transparent
        // areas — matching the template's white-on-dark Invoice banner.
        if (pw <= 0 || ph <= 0) return;
        for (int dy = 0; dy < ph; dy++)
        {
            var destY = py + dy;
            if (destY < 0 || destY >= ctx.PixelH) continue;

            // Inverse-map this dest row into a [srcY0, srcY1) source span.
            var srcY0 = (long)dy * imgH / ph;
            var srcY1 = (long)(dy + 1) * imgH / ph;
            if (srcY1 == srcY0) srcY1 = srcY0 + 1;

            for (int dx = 0; dx < pw; dx++)
            {
                var destX = px + dx;
                if (destX < 0 || destX >= ctx.PixelW) continue;

                var srcX0 = (long)dx * imgW / pw;
                var srcX1 = (long)(dx + 1) * imgW / pw;
                if (srcX1 == srcX0) srcX1 = srcX0 + 1;

                long paintCount = 0, totalCount = 0;
                for (var sy = srcY0; sy < srcY1; sy++)
                {
                    var rowBase = sy * rowBytes;
                    for (var sx = srcX0; sx < srcX1; sx++)
                    {
                        var bi = rowBase + sx / 8;
                        if (bi < 0 || bi >= decoded.Length) continue;
                        var bit = (decoded[(int)bi] >> (7 - (int)(sx & 7))) & 1;
                        if (bit == paintBit) paintCount++;
                        totalCount++;
                    }
                }
                if (totalCount == 0 || paintCount == 0) continue;
                var alpha = (byte)(paintCount * 255 / totalCount);
                SetPixel(ctx, destX, destY, r, g, b, alpha);
            }
        }
    }

    // ── Annotation rendering ────────────────────────────────────────

    /// <summary>
    /// Paint annotations on top of the page. Currently supports text-markup annotations
    /// (/Highlight, /Underline, /StrikeOut, /Squiggly) because they have a simple geometric
    /// appearance derived from /QuadPoints. Other subtypes carry an /AP appearance stream
    /// which we don't yet rasterise.
    /// </summary>
    private static void DrawAnnotations(RenderContext ctx, PdfDictionary pageDict)
    {
        var annots = ctx.Reader.Resolve(pageDict.Get("Annots")) as PdfArray;
        if (annots is null) return;
        foreach (var item in annots)
        {
            var annot = ctx.Reader.ResolveDict(item);
            if (annot is null) continue;
            // Skip annotations that are hidden (bit 2 of /F). Bits are 1-indexed in the spec.
            var flags = (int)annot.GetInt("F");
            if ((flags & 0x02) != 0) continue;
            var subtype = annot.GetName("Subtype");
            if (subtype == "Highlight")
            {
                DrawHighlightAnnotation(ctx, annot);
                continue;
            }
            // For everything else, first try the /AP /N appearance stream per
            // PDF 32000-1:2008 §12.5.5. Covers /FreeText, /Stamp, /Widget, /Popup,
            // /Ink, /Line, /Polygon and any subtype that ships its appearance
            // as a Form XObject.
            var hasAppearance = ctx.Reader.ResolveDict(annot.Get("AP")) is not null;
            if (hasAppearance)
            {
                DrawAppearanceAnnotation(ctx, annot);
                continue;
            }
            // No /AP → the spec requires we generate a default appearance from
            // the subtype's attributes. /Square and /Circle have a simple
            // default (filled/stroked rectangle or ellipse in page space).
            // PDF 32000-1:2008 §12.5.6.8.
            if (subtype == "Square" || subtype == "Circle")
            {
                DrawSquareCircleDefault(ctx, annot, isCircle: subtype == "Circle");
            }
            // Open /Popup notes carry no /AP (they are interactive UI); synthesise the
            // comment box so "render comments to image" bakes it in. PDF 32000 §12.5.6.14.
            else if (subtype == "Popup")
            {
                var form = Aspose.Pdf.Annotations.PopupAppearance.BuildOpenPopupForm(annot, ctx.Reader);
                if (form is not null) DrawAppearanceForm(ctx, annot, form);
            }
        }
    }

    /// <summary>
    /// Paint an annotation whose visual is expressed as an /AP /N Form XObject.
    /// Computes the CTM that maps the appearance stream's transformed /BBox into the
    /// annotation's page-space /Rect (PDF 32000 §12.5.5), then dispatches to the
    /// regular Form XObject renderer so all normal content-stream operators work
    /// (text, images, paths, nested XObjects).
    /// </summary>
    private static void DrawAppearanceAnnotation(RenderContext ctx, PdfDictionary annot)
    {
        var ap = ctx.Reader.ResolveDict(annot.Get("AP"));
        if (ap is null) return;
        // /N can be either a stream directly (simple markup annotations) or a dict
        // of appearance-state → stream (checkboxes, radio buttons, multi-state Widgets)
        // selected by the annotation's /AS entry (PDF 32000 §12.5.5).
        var nEntry = ctx.Reader.Resolve(ap.Get("N"));
        PdfStream? formStream = null;
        if (nEntry is PdfStream direct)
        {
            formStream = direct;
        }
        else if (nEntry is PdfDictionary stateDict)
        {
            var asName = annot.GetName("AS");
            if (!string.IsNullOrEmpty(asName))
                formStream = ctx.Reader.ResolveStream(stateDict.Get(asName));
        }
        if (formStream is null) return;
        DrawAppearanceForm(ctx, annot, formStream);
    }

    /// <summary>Paint a Form XObject appearance into the annotation's /Rect (PDF 32000
    /// §12.5.5). Shared by the /AP path and synthesised appearances (e.g. open popups).</summary>
    private static void DrawAppearanceForm(RenderContext ctx, PdfDictionary annot, PdfStream formStream)
    {
        if (annot.Get("Rect") is not PdfArray rect || rect.Count < 4) return;

        // Normalise /Rect (some PDFs emit it with corners out of order).
        double rx1 = NumFrom(rect[0]), ry1 = NumFrom(rect[1]);
        double rx2 = NumFrom(rect[2]), ry2 = NumFrom(rect[3]);
        var rMinX = Math.Min(rx1, rx2); var rMaxX = Math.Max(rx1, rx2);
        var rMinY = Math.Min(ry1, ry2); var rMaxY = Math.Max(ry1, ry2);
        var rW = rMaxX - rMinX; var rH = rMaxY - rMinY;
        if (rW <= 0 || rH <= 0) return;

        // Transform the form's /BBox through its /Matrix to get the source rectangle
        // in the form's post-matrix space. DrawFormXObject concatenates /Matrix again,
        // so the outer CTM we build here must operate on the *post-matrix* bbox.
        if (formStream.Dict.Get("BBox") is not PdfArray bbox || bbox.Count < 4) return;
        double bx1 = NumFrom(bbox[0]), by1 = NumFrom(bbox[1]);
        double bx2 = NumFrom(bbox[2]), by2 = NumFrom(bbox[3]);
        var formMatrix = ExtractFormMatrix(formStream.Dict) ?? new double[] { 1, 0, 0, 1, 0, 0 };
        double tMinX = double.PositiveInfinity, tMinY = double.PositiveInfinity;
        double tMaxX = double.NegativeInfinity, tMaxY = double.NegativeInfinity;
        foreach (var (cx, cy) in new[] { (bx1, by1), (bx2, by1), (bx2, by2), (bx1, by2) })
        {
            var tx = formMatrix[0] * cx + formMatrix[2] * cy + formMatrix[4];
            var ty = formMatrix[1] * cx + formMatrix[3] * cy + formMatrix[5];
            if (tx < tMinX) tMinX = tx;
            if (tx > tMaxX) tMaxX = tx;
            if (ty < tMinY) tMinY = ty;
            if (ty > tMaxY) tMaxY = ty;
        }
        var tW = tMaxX - tMinX; var tH = tMaxY - tMinY;
        if (tW <= 0 || tH <= 0) return;

        var sx = rW / tW;
        var sy = rH / tH;
        // Outer CTM maps the form's transformed bbox origin to /Rect's lower-left.
        // DrawFormXObject will left-multiply this with /Matrix internally.
        var outerCtm = new double[]
        {
            sx, 0, 0, sy,
            rMinX - tMinX * sx,
            rMinY - tMinY * sy,
        };
        var state = new GraphicsState();
        state.Ctm = outerCtm;
        // Annotations are painted after page content; any clip mask left over from the
        // content stream would wrongly clip the annotation. Clear before rendering,
        // restore after so subsequent annotations start from a clean state too.
        var savedClip = ctx.ClipMask;
        ctx.ClipMask = null;
        DrawFormXObject(ctx, formStream, state, null);
        ctx.ClipMask = savedClip;
    }

    /// <summary>
    /// Render a /Highlight annotation as a multiply-blended coloured rectangle per QuadPoint
    /// quad, falling back to the single /Rect if QuadPoints is absent. Multiply blending
    /// (PDF §11.3.5.3) preserves any text underneath — which is exactly why Acrobat uses it
    /// for highlights.
    /// </summary>
    private static void DrawHighlightAnnotation(RenderContext ctx, PdfDictionary annot)
    {
        // /C is the annotation colour in the current colour space (1, 3, or 4 components).
        // Default to yellow — Acrobat's factory default for the highlight tool.
        var color = ResolveColorArray(annot.Get("C")) ?? new[] { 1.0, 1.0, 0.0 };
        var (hr, hg, hb) = ColorToRgb(color);

        var quads = ctx.Reader.Resolve(annot.Get("QuadPoints")) as PdfArray;
        if (quads is not null && quads.Count >= 8 && quads.Count % 8 == 0)
        {
            // Each quad is 8 numbers: x1 y1 x2 y2 x3 y3 x4 y4 — the four corners in an
            // implementation-specific order. We compute the axis-aligned bounding box of
            // the four corners and fill it; that matches what Acrobat does visually for
            // axis-aligned text lines (the usual case).
            for (var i = 0; i + 8 <= quads.Count; i += 8)
            {
                double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
                double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
                for (var k = 0; k < 4; k++)
                {
                    var qx = ToDouble(quads[i + k * 2]);
                    var qy = ToDouble(quads[i + k * 2 + 1]);
                    if (qx < minX) minX = qx;
                    if (qx > maxX) maxX = qx;
                    if (qy < minY) minY = qy;
                    if (qy > maxY) maxY = qy;
                }
                // Acrobat / GDI+ shrink the QuadPoints box so the colour only covers from
                // the glyph cap-height down to just below the baseline; the box as written
                // in the annotation typically extends ~25–30% further down into the
                // descender/leading area. Match that visually so our output doesn't spill
                // yellow into blank line-gaps.
                var height = maxY - minY;
                minY += height * 0.28;
                FillMultiplyRect(ctx, minX, minY, maxX, maxY, hr, hg, hb);
            }
            return;
        }

        var rect = ctx.Reader.Resolve(annot.Get("Rect")) as PdfArray;
        if (rect is not null && rect.Count >= 4)
        {
            var x1 = ToDouble(rect[0]);
            var y1 = ToDouble(rect[1]);
            var x2 = ToDouble(rect[2]);
            var y2 = ToDouble(rect[3]);
            FillMultiplyRect(ctx, Math.Min(x1, x2), Math.Min(y1, y2),
                Math.Max(x1, x2), Math.Max(y1, y2), hr, hg, hb);
        }
    }

    /// <summary>
    /// Render the default appearance of a /Square or /Circle annotation when
    /// it lacks an /AP /N stream. PDF 32000 §12.5.6.8: a Square's default
    /// appearance is the /Rect filled with the interior colour /IC and stroked
    /// with /C using /BS's line width (default 1 pt). /Circle is the same
    /// shape fit inside /Rect.
    /// </summary>
    private static void DrawSquareCircleDefault(RenderContext ctx, PdfDictionary annot, bool isCircle)
    {
        if (ctx.Reader.Resolve(annot.Get("Rect")) is not PdfArray rect || rect.Count < 4) return;

        double x1 = ToDouble(rect[0]);
        double y1 = ToDouble(rect[1]);
        double x2 = ToDouble(rect[2]);
        double y2 = ToDouble(rect[3]);
        var minX = Math.Min(x1, x2); var maxX = Math.Max(x1, x2);
        var minY = Math.Min(y1, y2); var maxY = Math.Max(y1, y2);
        if (maxX <= minX || maxY <= minY) return;

        // Interior colour (/IC). Optional — no fill if absent.
        var icColor = ResolveColorArray(annot.Get("IC"));

        // Border colour (/C). Optional — default black when a border is drawn.
        var borderArr = ResolveColorArray(annot.Get("C"));
        byte br = 0, bg = 0, bb = 0;
        var borderColorSet = borderArr is not null;
        if (borderArr is not null)
        {
            var (r, g, b) = ColorToRgb(borderArr);
            br = r; bg = g; bb = b;
        }

        // Border width from /BS /W (preferred) or legacy /Border [hr vr w].
        // Default 1pt per spec.
        double borderW = 1.0;
        if (ctx.Reader.ResolveDict(annot.Get("BS")) is PdfDictionary bs)
        {
            var w = ctx.Reader.Resolve(bs.Get("W"));
            if (w is not null) borderW = ToDouble(w);
        }
        else if (ctx.Reader.Resolve(annot.Get("Border")) is PdfArray legacyBorder && legacyBorder.Count >= 3)
        {
            borderW = ToDouble(legacyBorder[2]);
        }

        // Convert page-space rect to pixel-space. CTM for base-page rendering
        // is identity apart from the DPI scale + the y-flip.
        var pxMinX = (int)Math.Round((minX - ctx.MediaBox.LLX) * ctx.Scale);
        var pxMaxX = (int)Math.Round((maxX - ctx.MediaBox.LLX) * ctx.Scale);
        var pxMinY = (int)Math.Round(ctx.PixelH - (maxY - ctx.MediaBox.LLY) * ctx.Scale);
        var pxMaxY = (int)Math.Round(ctx.PixelH - (minY - ctx.MediaBox.LLY) * ctx.Scale);

        // Fill interior.
        if (icColor is not null)
        {
            var (ir, ig, ib) = ColorToRgb(icColor);
            if (isCircle)
                FillEllipse(ctx, pxMinX, pxMinY, pxMaxX, pxMaxY, ir, ig, ib, 255);
            else
                FillRect(ctx, pxMinX, pxMinY, pxMaxX - pxMinX, pxMaxY - pxMinY, ir, ig, ib, 255);
        }

        // Stroke border only when /C is explicitly specified. Acrobat's
        // default-appearance behavior (verified against GDI+ templates) is:
        // no /C → no visible outline, regardless of /IC or /BS. Without /IC
        // and without /C the annotation collapses to nothing on the page.
        if (borderW > 0 && borderColorSet)
        {
            var lw = (float)(borderW * ctx.Scale);
            if (lw < 1) lw = 1;
            var clip = ctx.ClipMask;
            // Draw 4 line segments forming the rect outline. Circle fallback
            // also uses the rect outline; a proper elliptical stroke can come
            // later. Callers only see Circle here when /IC is set too.
            ScanlineFiller.StrokeLine(ctx.Pixels, ctx.PixelW, ctx.PixelH,
                pxMinX, pxMinY, pxMaxX, pxMinY, br, bg, bb, 255, lw, clip,
                blendMode: ctx.CurrentBlendMode, knockout: ctx.IsKnockoutGroup, softMask: ctx.SoftMaskAlpha);
            ScanlineFiller.StrokeLine(ctx.Pixels, ctx.PixelW, ctx.PixelH,
                pxMaxX, pxMinY, pxMaxX, pxMaxY, br, bg, bb, 255, lw, clip,
                blendMode: ctx.CurrentBlendMode, knockout: ctx.IsKnockoutGroup, softMask: ctx.SoftMaskAlpha);
            ScanlineFiller.StrokeLine(ctx.Pixels, ctx.PixelW, ctx.PixelH,
                pxMaxX, pxMaxY, pxMinX, pxMaxY, br, bg, bb, 255, lw, clip,
                blendMode: ctx.CurrentBlendMode, knockout: ctx.IsKnockoutGroup, softMask: ctx.SoftMaskAlpha);
            ScanlineFiller.StrokeLine(ctx.Pixels, ctx.PixelW, ctx.PixelH,
                pxMinX, pxMaxY, pxMinX, pxMinY, br, bg, bb, 255, lw, clip,
                blendMode: ctx.CurrentBlendMode, knockout: ctx.IsKnockoutGroup, softMask: ctx.SoftMaskAlpha);
        }
    }

    // Simple axis-aligned ellipse fill using the implicit equation — good
    // enough for /Circle annotation defaults. Pixel-space coordinates.
    private static void FillEllipse(RenderContext ctx, int minX, int minY, int maxX, int maxY,
        byte r, byte g, byte b, byte a)
    {
        var cx = (minX + maxX) / 2.0;
        var cy = (minY + maxY) / 2.0;
        var rx = (maxX - minX) / 2.0;
        var ry = (maxY - minY) / 2.0;
        if (rx <= 0 || ry <= 0) return;

        var y0 = Math.Max(0, minY);
        var y1 = Math.Min(ctx.PixelH, maxY);
        var x0 = Math.Max(0, minX);
        var x1 = Math.Min(ctx.PixelW, maxX);
        for (var y = y0; y < y1; y++)
        {
            var dy = (y - cy) / ry;
            for (var x = x0; x < x1; x++)
            {
                var dx = (x - cx) / rx;
                if (dx * dx + dy * dy <= 1.0)
                    SetPixel(ctx, x, y, r, g, b, a);
            }
        }
    }

    /// <summary>Convert a PDF colour array (1/3/4 components in 0..1) to sRGB bytes.</summary>
    private static (byte r, byte g, byte b) ColorToRgb(double[] c)
    {
        if (c.Length == 4)
        {
            // CMYK runs through the embedded ICC LUT — the algebraic
            // (1−C)(1−K) formula gives spec-correct but visually-off
            // results vs. GDI+ / Acrobat. See CmykToRgbLut for the
            // background.
            return CmykToRgbLut.Convert(c[0], c[1], c[2], c[3]);
        }

        double r, g, b;
        if (c.Length == 1)
        {
            r = g = b = c[0];
        }
        else
        {
            r = c[0]; g = c.Length > 1 ? c[1] : c[0]; b = c.Length > 2 ? c[2] : c[0];
        }
        return ((byte)Math.Clamp(r * 255, 0, 255),
                (byte)Math.Clamp(g * 255, 0, 255),
                (byte)Math.Clamp(b * 255, 0, 255));
    }

    private static double[]? ResolveColorArray(PdfObject? obj)
    {
        if (obj is not PdfArray arr) return null;
        var result = new double[arr.Count];
        for (var i = 0; i < arr.Count; i++) result[i] = ToDouble(arr[i]);
        return result;
    }

    private static double ToDouble(PdfObject? obj) => obj switch
    {
        PdfInteger pi => pi.Value,
        PdfReal pr => pr.Value,
        _ => 0,
    };

    /// <summary>
    /// Fill a user-space rectangle into the pixel buffer using PDF Multiply blending
    /// (result = dest × src). Used for translucent text-markup annotations such as
    /// /Highlight, where the colour must not obscure the text it covers.
    /// </summary>
    private static void FillMultiplyRect(RenderContext ctx, double x1, double y1,
        double x2, double y2, byte sr, byte sg, byte sb)
    {
        // 2-pixel outward dilation on each edge: the GDI+ template rasterises the
        // rectangle with a couple of rows/columns of anti-aliased partial-coverage
        // edge pixels (half-yellow). Our hard-edged multiply produces no such
        // fringe, so we paint those edge pixels fully — the test's 6-pixel
        // neighbourhood window then finds a close-enough colour match either way.
        var px1 = (int)Math.Floor((x1 - ctx.MediaBox.LLX) * ctx.Scale) - 2;
        var px2 = (int)Math.Ceiling((x2 - ctx.MediaBox.LLX) * ctx.Scale) + 2;
        // PDF y grows upward; pixel y grows downward, so the lower-left corner becomes the
        // higher pixel-y and vice versa.
        var py1 = (int)Math.Floor(ctx.PixelH - (y2 - ctx.MediaBox.LLY) * ctx.Scale) - 2;
        var py2 = (int)Math.Ceiling(ctx.PixelH - (y1 - ctx.MediaBox.LLY) * ctx.Scale) + 2;

        if (px1 < 0) px1 = 0;
        if (py1 < 0) py1 = 0;
        if (px2 > ctx.PixelW) px2 = ctx.PixelW;
        if (py2 > ctx.PixelH) py2 = ctx.PixelH;
        if (px1 >= px2 || py1 >= py2) return;

        var pix = ctx.Pixels;
        for (var y = py1; y < py2; y++)
        {
            var rowBase = y * ctx.PixelW * 4;
            for (var x = px1; x < px2; x++)
            {
                var p = rowBase + x * 4;
                pix[p]     = (byte)(pix[p]     * sr / 255);
                pix[p + 1] = (byte)(pix[p + 1] * sg / 255);
                pix[p + 2] = (byte)(pix[p + 2] * sb / 255);
                // Alpha stays fully opaque — the highlight doesn't punch through the page.
            }
        }
    }

    // ── Form XObject rendering ──────────────────────────────────────

    // Pathological PDFs can chain Form XObjects cyclically. Cap recursion to protect
    // the renderer from stack exhaustion / infinite loops.
    [ThreadStatic] private static int _formDepth;

    private static void DrawFormXObject(RenderContext ctx, PdfStream formStream, GraphicsState state,
        Dictionary<string, PdfDictionary>? extGStates)
    {
        if (_formDepth > 64) return;
        _formDepth++;
        try
        {
        byte[] formContent;
        try { formContent = ctx.Reader.DecodeStream(formStream); }
        catch { return; }

        // Resolve Form XObject's own resources
        var formResources = ctx.Reader.ResolveDict(formStream.Dict.Get("Resources"));
        var formFontDicts = ResolveFontDicts(formResources, ctx.Reader);
        var formExtGStates = ResolveExtGStates(formResources, ctx.Reader);
        var formXObjects = ResolveAllXObjects(formResources, ctx.Reader);

        // Merge parent resources for fallback
        if (ctx.FontDicts is not null)
            foreach (var kv in ctx.FontDicts)
                formFontDicts.TryAdd(kv.Key, kv.Value);
        if (ctx.AllXObjects is not null)
            foreach (var kv in ctx.AllXObjects)
                formXObjects.TryAdd(kv.Key, kv.Value);

        // PDF 32000 §8.10: `Do` on a Form XObject concatenates the form's /Matrix to
        // the caller's CTM, clips to the form's /BBox, and is bracketed by an implicit
        // q…Q. Propagating the caller CTM × form.Matrix places the form at the
        // caller's user-space position; the BBox clip keeps strokes inside a form
        // from leaking onto surrounding page content.
        var formMatrix = ExtractFormMatrix(formStream.Dict);
        var effectiveCtm = formMatrix is not null
            ? GraphicsState.MultiplyMatrices(formMatrix, state.Ctm)
            : (double[])state.Ctm.Clone();

        var formClipMask = BuildFormBBoxClip(ctx, formStream.Dict, effectiveCtm, ctx.ClipMask);

        // PDF 32000 §11.6.6 Transparency Group: when the form has /Group /S /Transparency,
        // its contents render onto a transparent backdrop in a separate buffer; the buffer
        // is then composited back to the parent using the BlendMode / fill-alpha that were
        // active at the `Do` call. Without this, blend modes like Multiply applied AROUND
        // a form Do (via gs) get reset by the form's own internal `/GS0 gs` (BM=Normal) on
        // each path, producing flat overlays instead of multiplied overlap colours
        // (e.g. blue-on-yellow should compose to green under Multiply).
        var groupDict = ctx.Reader.ResolveDict(formStream.Dict.Get("Group"));
        var isTransparencyGroup = groupDict is not null
            && groupDict.GetName("S") == "Transparency";

        if (isTransparencyGroup)
        {
            // /K true makes the group a knockout group: each draw inside sees only the
            // group's original (transparent) backdrop, not prior accumulated draws —
            // overlapping elements show only the topmost. We emulate that at the
            // pixel-write level via scratchCtx.IsKnockoutGroup; see RenderContext.
            var isKnockout = groupDict!.Get("K") is PdfBoolean kn && kn.Value;

            // Allocate a scratch RGBA buffer same size as the parent, RGBA=(0,0,0,0).
            var scratch = new byte[ctx.Pixels.Length];
            var scratchCtx = new RenderContext(scratch, ctx.PixelW, ctx.PixelH, ctx.Scale, ctx.MediaBox, ctx.Reader)
            {
                AllXObjects = formXObjects,
                FontDicts = formFontDicts,
                Patterns = ctx.Reader.ResolveDict(formResources?.Get("Pattern")) ?? ctx.Patterns,
                Shadings = ctx.Reader.ResolveDict(formResources?.Get("Shading")) ?? ctx.Shadings,
                ColorSpaces = ctx.Reader.ResolveDict(formResources?.Get("ColorSpace")) ?? ctx.ColorSpaces,
                ClipMask = formClipMask,
                CurrentBlendMode = "Normal",
                IsKnockoutGroup = isKnockout,
            };
            RenderContent(formContent, scratchCtx, formExtGStates, effectiveCtm, formClipMask);

            // PDF 32000 §11.6.6: when /CS is a 1-component (gray) space, the group's
            // contents are blended in grayscale and any final composite collapses to
            // luminance. We render in RGB and then post-convert to gray rather than
            // running a CS-aware rendering pipeline — strictly equivalent for Normal
            // blend mode, an approximation for the separable formulas (RGB-then-Y vs
            // Y-then-blend differ only on non-grey sources). /DeviceCMYK groups would
            // need a full CMYK pipeline and stay rendered in RGB for now.
            ConvertScratchForGroupCS(scratch, groupDict, ctx.Reader);

            // Composite scratch back into parent at this Do call's blend mode + alpha.
            CompositeGroupBuffer(ctx, scratch, state.BlendMode, state.FillAlpha);
        }
        else
        {
            var childCtx = new RenderContext(ctx.Pixels, ctx.PixelW, ctx.PixelH, ctx.Scale, ctx.MediaBox, ctx.Reader)
            {
                AllXObjects = formXObjects,
                FontDicts = formFontDicts,
                // Pattern resources and the active clip mask inherit so that a pattern fill
                // inside a Form XObject or an image Do inside a pattern tile stays bounded.
                Patterns = ctx.Reader.ResolveDict(formResources?.Get("Pattern")) ?? ctx.Patterns,
                Shadings = ctx.Reader.ResolveDict(formResources?.Get("Shading")) ?? ctx.Shadings,
                ColorSpaces = ctx.Reader.ResolveDict(formResources?.Get("ColorSpace")) ?? ctx.ColorSpaces,
                ClipMask = formClipMask,
            };

            RenderContent(formContent, childCtx, formExtGStates, effectiveCtm, formClipMask);
        }
        }
        finally { _formDepth--; }
    }

    /// <summary>
    /// Composite a transparency-group's scratch RGBA buffer back into the parent
    /// pixel buffer using the supplied blend mode and group fill-alpha. PDF 32000
    /// §11.6.6: each non-zero scratch pixel multiplies its alpha by the group
    /// fill-alpha and blends with the parent at that effective alpha.
    /// </summary>
    private static void CompositeGroupBuffer(RenderContext ctx, byte[] scratch, string blendMode, double groupAlpha)
    {
        var ga = (int)Math.Round(Math.Clamp(groupAlpha, 0.0, 1.0) * 255);
        var mode = BlendModes.Parse(blendMode);
        var dst = ctx.Pixels;
        for (var i = 0; i < dst.Length; i += 4)
        {
            var sa = scratch[i + 3];
            if (sa == 0) continue;
            int sr = scratch[i], sg = scratch[i + 1], sb = scratch[i + 2];
            int dr = dst[i], dg = dst[i + 1], db = dst[i + 2];

            if (mode != Rasterizer.BlendMode.Normal)
            {
                BlendModes.Blend(mode, dr, dg, db, sr, sg, sb, out sr, out sg, out sb);
            }

            var effA = (sa * ga) / 255;
            if (effA <= 0) continue;
            var inv = 255 - effA;
            dst[i]     = (byte)((sr * effA + dr * inv + 127) / 255);
            dst[i + 1] = (byte)((sg * effA + dg * inv + 127) / 255);
            dst[i + 2] = (byte)((sb * effA + db * inv + 127) / 255);
            dst[i + 3] = 255;
        }
    }

    /// <summary>
    /// Render a soft-mask /SMask group (PDF 32000 §11.6.5.4) into a per-pixel alpha
    /// buffer matching the page-pixel grid. Renders the /G group as a Form XObject
    /// using the snapshotted gs-time CTM, then derives the per-pixel mask: luminance
    /// for /S /Luminosity (multiplied by the rendered alpha), the alpha channel for
    /// /S /Alpha. /BC pre-fills the scratch backdrop for Luminosity (default black =
    /// fully-masked outside the drawn area). Cached per page on the RenderContext
    /// so a single SMask referenced from many paint operations is rendered once.
    /// Returns null if the group can't be resolved.
    /// </summary>
    private static byte[]? ResolveSoftMaskAlpha(RenderContext ctx, SoftMaskInfo smInfo)
    {
        var groupObj = smInfo.Dict.Get("G");
        var cacheKey = groupObj is PdfIndirectRef gr
            ? gr.ObjectNumber
            : -System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(smInfo.Dict);
        if (ctx.SoftMaskCache.TryGetValue(cacheKey, out var cached)) return cached;

        var alphaBuf = RenderSoftMaskAlpha(ctx.Reader, ctx.PixelW, ctx.PixelH, ctx.Scale, ctx.MediaBox, smInfo);
        if (alphaBuf is not null) ctx.SoftMaskCache[cacheKey] = alphaBuf;
        return alphaBuf;
    }

    /// <summary>
    /// Render a soft-mask group to a page-sized 8-bit alpha buffer (§11.6.5.4). The mask
    /// form is rasterised into a scratch buffer at the given page geometry, then reduced
    /// to alpha (Luminosity ⇒ Rec.601 luma × coverage, Alpha ⇒ coverage), with the
    /// optional /TR transfer function applied. Shared by both renderers.
    /// </summary>
    internal static byte[]? RenderSoftMaskAlpha(PdfReader reader, int pixelW, int pixelH,
        double scale, Rectangle mediaBox, SoftMaskInfo smInfo)
    {
        var groupStream = reader.ResolveStream(smInfo.Dict.Get("G"));
        if (groupStream is null || pixelW <= 0 || pixelH <= 0) return null;

        var scratchPixels = new byte[pixelW * pixelH * 4];
        if (smInfo.Subtype == "Luminosity")
        {
            var bc = smInfo.Dict.Get("BC") as PdfArray;
            var (br, bg, bb) = SampleBackdropRgb(bc);
            for (var i = 0; i < scratchPixels.Length; i += 4)
            {
                scratchPixels[i] = br;
                scratchPixels[i + 1] = bg;
                scratchPixels[i + 2] = bb;
                scratchPixels[i + 3] = 255;
            }
        }

        var scratchCtx = new RenderContext(scratchPixels, pixelW, pixelH, scale, mediaBox, reader);
        var maskState = new GraphicsState { Ctm = (double[])smInfo.Ctm.Clone() };
        DrawFormXObject(scratchCtx, groupStream, maskState, null);

        var alphaBuf = new byte[pixelW * pixelH];
        if (smInfo.Subtype == "Alpha")
        {
            for (var i = 0; i < alphaBuf.Length; i++)
                alphaBuf[i] = scratchPixels[i * 4 + 3];
        }
        else
        {
            // Luminosity: Y = 0.299R + 0.587G + 0.114B (Rec.601), multiplied by the
            // rendered alpha so untouched scratch pixels (alpha 0) contribute 0 to
            // the mask. /BC pre-fill keeps "background" alpha at 255 with the
            // backdrop colour, so its luminance becomes the outside-of-content mask.
            for (var i = 0; i < alphaBuf.Length; i++)
            {
                var p = i * 4;
                var y = (scratchPixels[p] * 299 + scratchPixels[p + 1] * 587 + scratchPixels[p + 2] * 114 + 500) / 1000;
                alphaBuf[i] = (byte)((y * scratchPixels[p + 3] + 127) / 255);
            }
        }

        // /TR transfer function (PDF 32000 §11.6.5.4 step e): an optional 1-input
        // function applied to the extracted mask value before it modulates source
        // alpha. Default is the identity if absent. Common in PDFs that want a
        // gamma-style shaping of the mask (e.g. soften a luminosity mask).
        ApplyTransferFunction(alphaBuf, smInfo.Dict.Get("TR"), reader);
        return alphaBuf;
    }

    /// <summary>Apply an /SMask /TR 1-input function to a byte-mask in place.
    /// Function input/output are in [0,1]; we map through byte/255 ↔ value.
    /// /Identity (a PdfName) is a no-op. Anything that fails to parse or
    /// evaluate returns gracefully without touching the buffer.</summary>
    private static void ApplyTransferFunction(byte[] alphaBuf, PdfObject? trObj, IO.PdfReader reader)
    {
        if (trObj is null) return;
        var resolved = reader.Resolve(trObj);
        if (resolved is PdfName n && n.Value == "Identity") return;
        var fn = Functions.PdfFunction.Parse(trObj, reader);
        if (fn is null) return;
        // Precompute the 256-entry LUT — TR is called once per mask byte, so a
        // 256-step table costs 256 function evaluations regardless of buffer
        // size and saves the per-pixel evaluation overhead.
        var lut = new byte[256];
        var input = new double[1];
        for (var i = 0; i < 256; i++)
        {
            input[0] = i / 255.0;
            var output = fn.Evaluate(input);
            if (output is null || output.Length == 0) { lut[i] = (byte)i; continue; }
            var v = output[0];
            if (v < 0) v = 0; else if (v > 1) v = 1;
            lut[i] = (byte)Math.Round(v * 255);
        }
        for (var i = 0; i < alphaBuf.Length; i++)
            alphaBuf[i] = lut[alphaBuf[i]];
    }

    /// <summary>
    /// Sample a soft-mask /BC backdrop colour array as an RGB triple. /BC is in
    /// the group's color space; we approximate by treating 1-component as gray
    /// (R=G=B) and 3-component as RGB. Default per spec: black.
    /// </summary>
    private static (byte R, byte G, byte B) SampleBackdropRgb(PdfArray? bc)
    {
        if (bc is null || bc.Count == 0) return (0, 0, 0);
        if (bc.Count == 1)
        {
            var g = (byte)Math.Clamp(NumFrom(bc[0]) * 255.0, 0, 255);
            return (g, g, g);
        }
        // 3+ components: take first three as R, G, B (PDF /CS DeviceRGB ordering).
        return (
            (byte)Math.Clamp(NumFrom(bc[0]) * 255.0, 0, 255),
            (byte)Math.Clamp(NumFrom(bc[1]) * 255.0, 0, 255),
            (byte)Math.Clamp(NumFrom(bc[2]) * 255.0, 0, 255));
    }

    /// <summary>
    /// Apply the group's /CS to the rendered scratch buffer in place. We only handle
    /// the 1-component grayscale family (DeviceGray / G / CalGray / 1-channel ICC) —
    /// each non-zero-alpha pixel's RGB is collapsed to its Rec.601 luminance so the
    /// composite back to the parent represents the gray-equivalent of what was drawn.
    /// /DeviceCMYK and richer ICC profiles need a real CS-aware pipeline; for those
    /// the scratch stays RGB (visible difference is small for Normal-mode content).
    /// </summary>
    private static void ConvertScratchForGroupCS(byte[] scratch, PdfDictionary? groupDict, PdfReader reader)
    {
        if (groupDict is null) return;
        var csObj = reader.Resolve(groupDict.Get("CS"));
        if (csObj is null) return;
        var csName = ResolveColorSpaceName(csObj, reader);
        if (csName != "DeviceGray" && csName != "G" && csName != "CalGray") return;

        for (var i = 0; i < scratch.Length; i += 4)
        {
            if (scratch[i + 3] == 0) continue;
            // Rec.601 luminance: Y = 0.299R + 0.587G + 0.114B (integer fixed-point).
            var y = (byte)((scratch[i] * 299 + scratch[i + 1] * 587 + scratch[i + 2] * 114 + 500) / 1000);
            scratch[i] = y;
            scratch[i + 1] = y;
            scratch[i + 2] = y;
        }
    }

    private static double[]? ExtractFormMatrix(PdfDictionary formDict)
    {
        if (formDict.Get("Matrix") is not PdfArray arr || arr.Count < 6) return null;
        var m = new double[6];
        for (var i = 0; i < 6; i++) m[i] = NumFrom(arr[i]);
        return m;
    }

    /// <summary>
    /// Build a pixel-space clip mask for the form's /BBox, intersected with any outer
    /// clip. Returns the outer clip unchanged when /BBox is absent or when the BBox
    /// already covers the whole pixel grid (common case — large icons/stamps).
    /// Materialising a per-form full-page byte[] mask costs W·H bytes each call, which
    /// becomes the dominant rendering cost on pages with hundreds of Form XObjects
    /// (e.g. some documents have ~1000 form references). We therefore skip the mask
    /// whenever the axis-aligned projection of the BBox already covers the drawable
    /// viewport: in that case the BBox clip is a no-op. Forms without a BBox are
    /// technically malformed per §8.10 but do appear in the wild.
    /// </summary>
    private static byte[]? BuildFormBBoxClip(RenderContext ctx, PdfDictionary formDict,
        double[] effectiveCtm, byte[]? outer)
    {
        if (formDict.Get("BBox") is not PdfArray bbox || bbox.Count < 4) return outer;

        double x1 = NumFrom(bbox[0]), y1 = NumFrom(bbox[1]);
        double x2 = NumFrom(bbox[2]), y2 = NumFrom(bbox[3]);
        // Normalise: some PDFs write BBox corners in arbitrary order.
        var xMin = Math.Min(x1, x2); var xMax = Math.Max(x1, x2);
        var yMin = Math.Min(y1, y2); var yMax = Math.Max(y1, y2);

        // Compute pixel-space AABB of the four transformed BBox corners.
        double pxLo = double.MaxValue, pxHi = double.MinValue;
        double pyLo = double.MaxValue, pyHi = double.MinValue;
        var corners = new (double x, double y)[]
        { (xMin, yMin), (xMax, yMin), (xMax, yMax), (xMin, yMax) };
        foreach (var (cx, cy) in corners)
        {
            var tx = effectiveCtm[0] * cx + effectiveCtm[2] * cy + effectiveCtm[4];
            var ty = effectiveCtm[1] * cx + effectiveCtm[3] * cy + effectiveCtm[5];
            var px = (tx - ctx.MediaBox.LLX) * ctx.Scale;
            var py = ctx.PixelH - (ty - ctx.MediaBox.LLY) * ctx.Scale;
            if (px < pxLo) pxLo = px; if (px > pxHi) pxHi = px;
            if (py < pyLo) pyLo = py; if (py > pyHi) pyHi = py;
        }
        // If the BBox AABB covers the whole drawable viewport, the form's BBox clip is
        // a no-op and we can skip the expensive per-form mask allocation+fill.
        if (pxLo <= 0 && pxHi >= ctx.PixelW && pyLo <= 0 && pyHi >= ctx.PixelH)
            return outer;

        var segments = new List<PathCommand>
        {
            new(PathOp.MoveTo, xMin, yMin),
            new(PathOp.LineTo, xMax, yMin),
            new(PathOp.LineTo, xMax, yMax),
            new(PathOp.LineTo, xMin, yMax),
            new(PathOp.Close),
        };
        var edgeTable = BuildPathEdgeTable(segments, effectiveCtm, ctx);
        var mask = new byte[ctx.PixelW * ctx.PixelH];
        ScanlineFiller.BuildMask(edgeTable, mask, ctx.PixelW, ctx.PixelH, evenOdd: false);
        if (outer is not null)
        {
            for (var i = 0; i < mask.Length; i++)
                mask[i] = (byte)(mask[i] & outer[i]);
        }
        return mask;
    }

    // ── Shading paint (`sh` operator, PDF 32000 §8.7.4.5) ───────────
    //
    // The `sh` operator paints a named shading dictionary into the current clip
    // region. Shading coordinates are in the coordinate system that was active
    // at the moment of the paint, so the CTM is applied to the shading's
    // endpoints before the per-pixel parameter is computed. Only Type-2 (axial)
    // and Type-3 (radial) shadings are handled here — the cases that show up in
    // real-world PDFs with icon/logo gradients (50501-2 uses only Type-2).

    private static void DrawShading(RenderContext ctx, string name, GraphicsState state)
    {
        if (ctx.Shadings is null) return;
        var shadingObj = ctx.Shadings.Get(name);
        if (shadingObj is null) return;

        var shading = ShadingBase.Parse(shadingObj, ctx.Reader);
        switch (shading)
        {
            case AxialShading axial:
                DrawAxialShading(ctx, axial, state);
                break;
            case RadialShading radial:
                DrawRadialShading(ctx, radial, state);
                break;
            case FreeFormGouraudShading gouraud:
                DrawGouraudMesh(ctx, gouraud.Vertices, gouraud.Triangles, gouraud.ColorSpaceName, state);
                break;
            case LatticeFormGouraudShading lat:
                DrawGouraudMesh(ctx, lat.Vertices, lat.Triangles, lat.ColorSpaceName, state);
                break;
            case CoonsPatchShading coons:
                DrawPatchMesh(ctx, coons.Patches, coons.ColorSpaceName, state);
                break;
            case TensorPatchShading tensor:
                DrawPatchMesh(ctx, tensor.Patches, tensor.ColorSpaceName, state);
                break;
        }
    }

    // ── Mesh shadings (Types 4-7) ───────────────────────────────────
    //
    // Strategy: tessellate the mesh into many small flat-shaded or per-vertex
    // interpolated triangles, then scanline-fill each. Patch types (Coons /
    // tensor) are subdivided into an N×N parameter grid evaluated through the
    // tensor cubic Bézier; each grid cell becomes two triangles with the
    // four corner colours bilinearly interpolated across (u,v).

    private static void DrawGouraudMesh(RenderContext ctx,
        MeshVertex[] verts, (int A, int B, int C)[] tris, string csName,
        GraphicsState state)
    {
        if (verts.Length == 0 || tris.Length == 0) return;
        var ctm = state.Ctm;
        var alpha = (byte)(state.FillAlpha * 255);
        foreach (var (ia, ib, ic) in tris)
        {
            var va = verts[ia]; var vb = verts[ib]; var vc = verts[ic];
            TransformPoint(ctm, va.X, va.Y, out var ax, out var ay);
            TransformPoint(ctm, vb.X, vb.Y, out var bx, out var by);
            TransformPoint(ctm, vc.X, vc.Y, out var cx, out var cy);
            RasterizeColoredTriangle(ctx, csName, alpha,
                ax, ay, va.Color,
                bx, by, vb.Color,
                cx, cy, vc.Color);
        }
    }

    private static void DrawPatchMesh(RenderContext ctx, MeshPatch[] patches,
        string csName, GraphicsState state)
    {
        if (patches.Length == 0) return;
        var ctm = state.Ctm;
        var alpha = (byte)(state.FillAlpha * 255);
        // Sub-grid resolution. 16×16 gives 256 cells per patch — plenty of
        // smoothness even for full-page gradients while keeping rasterisation
        // bounded. Real-world mesh-shading PDFs typically have only a handful
        // of patches per page.
        const int N = 16;
        var px = new double[N + 1, N + 1];
        var py = new double[N + 1, N + 1];
        foreach (var patch in patches)
        {
            // Sample patch positions on the (N+1)×(N+1) grid.
            for (var i = 0; i <= N; i++)
            {
                var u = i / (double)N;
                for (var j = 0; j <= N; j++)
                {
                    var v = j / (double)N;
                    EvalBicubic(patch.Px, u, v, out var x);
                    EvalBicubic(patch.Py, u, v, out var y);
                    TransformPoint(ctm, x, y, out var ux, out var uy);
                    px[i, j] = ux; py[i, j] = uy;
                }
            }
            // For each cell, produce two triangles with bilinear-interpolated colours.
            var ncc = patch.CornerColors[0]?.Length ?? 0;
            if (ncc == 0) continue;
            for (var i = 0; i < N; i++)
            {
                var u0 = i / (double)N; var u1 = (i + 1) / (double)N;
                for (var j = 0; j < N; j++)
                {
                    var v0 = j / (double)N; var v1 = (j + 1) / (double)N;
                    var c00 = BilinearColor(patch.CornerColors, u0, v0, ncc);
                    var c10 = BilinearColor(patch.CornerColors, u1, v0, ncc);
                    var c11 = BilinearColor(patch.CornerColors, u1, v1, ncc);
                    var c01 = BilinearColor(patch.CornerColors, u0, v1, ncc);
                    RasterizeColoredTriangle(ctx, csName, alpha,
                        px[i, j], py[i, j], c00,
                        px[i + 1, j], py[i + 1, j], c10,
                        px[i + 1, j + 1], py[i + 1, j + 1], c11);
                    RasterizeColoredTriangle(ctx, csName, alpha,
                        px[i, j], py[i, j], c00,
                        px[i + 1, j + 1], py[i + 1, j + 1], c11,
                        px[i, j + 1], py[i, j + 1], c01);
                }
            }
        }
    }

    /// <summary>Patch corner-colour storage order: [c00, c30, c33, c03]
    /// (bottom-left, bottom-right, top-right, top-left in (u,v) space).
    /// Bilinearly interpolate at arbitrary (u, v).</summary>
    private static double[] BilinearColor(double[][] cc, double u, double v, int ncc)
    {
        var c00 = cc[0]; var c30 = cc[1]; var c33 = cc[2]; var c03 = cc[3];
        var result = new double[ncc];
        var omu = 1 - u; var omv = 1 - v;
        for (var i = 0; i < ncc; i++)
        {
            var v0 = omu * omv * (c00?[i] ?? 0) + u * omv * (c30?[i] ?? 0);
            var v1 = omu * v * (c03?[i] ?? 0) + u * v * (c33?[i] ?? 0);
            result[i] = v0 + v1;
        }
        return result;
    }

    private static void EvalBicubic(double[,] g, double u, double v, out double r)
    {
        // S(u,v) = sum_{i,j} B_i(u) B_j(v) * g[i,j]
        var bu0 = (1 - u) * (1 - u) * (1 - u);
        var bu1 = 3 * (1 - u) * (1 - u) * u;
        var bu2 = 3 * (1 - u) * u * u;
        var bu3 = u * u * u;
        var bv0 = (1 - v) * (1 - v) * (1 - v);
        var bv1 = 3 * (1 - v) * (1 - v) * v;
        var bv2 = 3 * (1 - v) * v * v;
        var bv3 = v * v * v;
        // Row sums then column dot to limit reads.
        var r0 = bu0 * g[0, 0] + bu1 * g[1, 0] + bu2 * g[2, 0] + bu3 * g[3, 0];
        var r1 = bu0 * g[0, 1] + bu1 * g[1, 1] + bu2 * g[2, 1] + bu3 * g[3, 1];
        var r2 = bu0 * g[0, 2] + bu1 * g[1, 2] + bu2 * g[2, 2] + bu3 * g[3, 2];
        var r3 = bu0 * g[0, 3] + bu1 * g[1, 3] + bu2 * g[2, 3] + bu3 * g[3, 3];
        r = bv0 * r0 + bv1 * r1 + bv2 * r2 + bv3 * r3;
    }

    /// <summary>Fill a triangle in user space with per-vertex colours
    /// barycentrically interpolated. Coordinates are user-space (pre-pixel);
    /// we map to pixel space via the standard MediaBox-relative formula used
    /// by axial/radial shadings.</summary>
    private static void RasterizeColoredTriangle(RenderContext ctx, string csName, byte alpha,
        double x0u, double y0u, double[] c0,
        double x1u, double y1u, double[] c1,
        double x2u, double y2u, double[] c2)
    {
        if (c0 is null || c1 is null || c2 is null) return;
        var scale = ctx.Scale;
        var mbLlx = ctx.MediaBox.LLX;
        var mbLly = ctx.MediaBox.LLY;
        var pixelH = ctx.PixelH;

        // User space → pixel space (origin top-left, y inverted).
        double Px(double xu) => (xu - mbLlx) * scale;
        double Py(double yu) => pixelH - (yu - mbLly) * scale;
        var p0x = Px(x0u); var p0y = Py(y0u);
        var p1x = Px(x1u); var p1y = Py(y1u);
        var p2x = Px(x2u); var p2y = Py(y2u);

        // Bounding box in pixels.
        var minX = (int)Math.Floor(Math.Min(p0x, Math.Min(p1x, p2x)));
        var maxX = (int)Math.Ceiling(Math.Max(p0x, Math.Max(p1x, p2x)));
        var minY = (int)Math.Floor(Math.Min(p0y, Math.Min(p1y, p2y)));
        var maxY = (int)Math.Ceiling(Math.Max(p0y, Math.Max(p1y, p2y)));
        if (minX < 0) minX = 0; if (minY < 0) minY = 0;
        if (maxX > ctx.PixelW) maxX = ctx.PixelW; if (maxY > ctx.PixelH) maxY = ctx.PixelH;
        if (maxX <= minX || maxY <= minY) return;

        // Edge-function denominator (twice signed area).
        var denom = (p1x - p0x) * (p2y - p0y) - (p1y - p0y) * (p2x - p0x);
        if (Math.Abs(denom) < 1e-9) return;
        var invDenom = 1.0 / denom;
        var ncc = c0.Length;
        var col = new double[ncc];

        for (var py = minY; py < maxY; py++)
        {
            var rowBase = py * ctx.PixelW;
            for (var px = minX; px < maxX; px++)
            {
                if (ctx.ClipMask is { } mask && mask[rowBase + px] == 0) continue;
                // Sample at pixel centre.
                var sx = px + 0.5; var sy = py + 0.5;
                // Barycentric coordinates.
                var w0 = ((p1x - sx) * (p2y - sy) - (p1y - sy) * (p2x - sx)) * invDenom;
                var w1 = ((p2x - sx) * (p0y - sy) - (p2y - sy) * (p0x - sx)) * invDenom;
                var w2 = 1 - w0 - w1;
                // Accept points with strictly non-negative weights (with a
                // tiny tolerance to cover shared edges between adjacent
                // patches/triangles).
                if (w0 < -1e-6 || w1 < -1e-6 || w2 < -1e-6) continue;
                for (var k = 0; k < ncc; k++)
                    col[k] = w0 * c0[k] + w1 * c1[k] + w2 * c2[k];
                ComponentsToRgb(col, csName, out var r, out var g, out var b);
                SetPixel(ctx, px, py, r, g, b, alpha);
            }
        }
    }

    private static void DrawAxialShading(RenderContext ctx, AxialShading axial, GraphicsState state)
    {
        if (axial.Function is null) return;

        // The shading's two axis endpoints live in shading-local coordinates; the CTM
        // at the moment of `sh` maps them into user space (§8.7.4.3).
        var ctm = state.Ctm;
        TransformPoint(ctm, axial.X0, axial.Y0, out var x0u, out var y0u);
        TransformPoint(ctm, axial.X1, axial.Y1, out var x1u, out var y1u);

        var dx = x1u - x0u;
        var dy = y1u - y0u;
        var denom = dx * dx + dy * dy;
        if (denom < 1e-12) return; // axis collapsed to a point — nothing to draw

        var domLo = axial.Domain.Length > 0 ? axial.Domain[0] : 0;
        var domHi = axial.Domain.Length > 1 ? axial.Domain[1] : 1;
        var domLen = domHi - domLo;
        var extendBefore = axial.Extend.Length > 0 && axial.Extend[0];
        var extendAfter = axial.Extend.Length > 1 && axial.Extend[1];

        ComputeShadingPixelBounds(ctx, out var xStart, out var xEnd, out var yStart, out var yEnd);

        // PDF 32000 §8.7.4.5.2: a shading's optional /BBox is its bounding box in
        // shading-local coordinates (before CTM). The shading "need not be applied
        // outside that rectangle". Without this, a Form XObject wrapping a small
        // axial gradient (e.g. a thin footer stripe) instead floods the entire
        // Form BBox / page clip, covering everything previously drawn. For
        // arbitrary CTMs we pre-compute the inverse and test each pixel in
        // shading-local space.
        var bboxLocal = axial.BBox;
        double[]? inv = null;
        if (bboxLocal is not null)
            inv = InvertMatrix(ctm);

        var invScale = 1.0 / ctx.Scale;
        var mbLlx = ctx.MediaBox.LLX;
        var mbLly = ctx.MediaBox.LLY;
        var alpha = (byte)(state.FillAlpha * 255);
        var csName = axial.ColorSpaceName;
        var input = new double[1];

        // Sample at pixel centres (+0.5) rather than corners. Sampling at the corner
        // means the pixel covering [0, 1) on the y-axis is probed at exactly y=0 —
        // which for a shading BBox of [0, …, max] with strict inequalities just barely
        // lands on the upper edge and gets excluded. Probing at +0.5 keeps the
        // first/last rows inside their BBoxes, matching Adobe / Aspose.PDF for .NET
        // behaviour.
        for (var py = yStart; py < yEnd; py++)
        {
            var uy = mbLly + (ctx.PixelH - py - 0.5) * invScale;
            var rowBase = py * ctx.PixelW;
            for (var px = xStart; px < xEnd; px++)
            {
                if (ctx.ClipMask is { } mask && mask[rowBase + px] == 0) continue;

                var ux = mbLlx + (px + 0.5) * invScale;

                if (bboxLocal is not null && inv is not null)
                {
                    TransformPoint(inv, ux, uy, out var lx, out var ly);
                    if (lx < bboxLocal[0] || lx > bboxLocal[2] ||
                        ly < bboxLocal[1] || ly > bboxLocal[3])
                        continue;
                }

                var t = ((ux - x0u) * dx + (uy - y0u) * dy) / denom;

                if (t < 0)
                {
                    if (!extendBefore) continue;
                    t = 0;
                }
                else if (t > 1)
                {
                    if (!extendAfter) continue;
                    t = 1;
                }

                input[0] = domLo + t * domLen;
                var col = axial.Function.Evaluate(input);
                if (col is null) continue;

                ComponentsToRgb(col, csName, out var r, out var g, out var b, axial.TintTransform, axial.AltSpaceName);
                SetPixel(ctx, px, py, r, g, b, alpha);
            }
        }
    }

    // 2D affine inverse for shading-BBox transforms. The CTM is
    // a row-vector [a b c d e f] in PDF spec layout, equivalent to
    // the matrix [[a, b, 0], [c, d, 0], [e, f, 1]]. Returns null
    // if singular (callers fall back to the default no-BBox path).
    private static double[]? InvertMatrix(double[] m)
    {
        var det = m[0] * m[3] - m[1] * m[2];
        if (Math.Abs(det) < 1e-12) return null;
        var invDet = 1.0 / det;
        var a = m[3] * invDet;
        var b = -m[1] * invDet;
        var c = -m[2] * invDet;
        var d = m[0] * invDet;
        var e = -(m[4] * a + m[5] * c);
        var f = -(m[4] * b + m[5] * d);
        return new[] { a, b, c, d, e, f };
    }

    private static void DrawRadialShading(RenderContext ctx, RadialShading radial, GraphicsState state)
    {
        if (radial.Function is null) return;

        // Transform circle centres to user space; radii scale by the CTM's uniform
        // component (sqrt(|det|)), which is exact for rotation+uniform-scale CTMs
        // and a best-effort approximation for skewed ones — circles become ellipses
        // only under genuinely asymmetric scale, which real-world logo gradients
        // rarely use.
        var ctm = state.Ctm;
        TransformPoint(ctm, radial.X0, radial.Y0, out var x0u, out var y0u);
        TransformPoint(ctm, radial.X1, radial.Y1, out var x1u, out var y1u);
        var radiusScale = Math.Sqrt(Math.Abs(ctm[0] * ctm[3] - ctm[1] * ctm[2]));
        var r0 = radial.R0 * radiusScale;
        var r1 = radial.R1 * radiusScale;

        var domLo = radial.Domain.Length > 0 ? radial.Domain[0] : 0;
        var domHi = radial.Domain.Length > 1 ? radial.Domain[1] : 1;
        var domLen = domHi - domLo;
        var extendBefore = radial.Extend.Length > 0 && radial.Extend[0];
        var extendAfter = radial.Extend.Length > 1 && radial.Extend[1];

        // Radial shading: for each user-space point p, find the largest t ∈ [0,1]
        // such that the point lies on circle(t) of centre
        // c(t) = c0 + t*(c1-c0), radius r(t) = r0 + t*(r1-r0). Solving the circle
        // equation reduces to a quadratic in t — standard closed-form approach
        // used by all PDF rasterisers.
        var cdx = x1u - x0u;
        var cdy = y1u - y0u;
        var dr = r1 - r0;

        ComputeShadingPixelBounds(ctx, out var xStart, out var xEnd, out var yStart, out var yEnd);

        var bboxLocal = radial.BBox;
        double[]? inv = null;
        if (bboxLocal is not null)
            inv = InvertMatrix(ctm);

        var invScale = 1.0 / ctx.Scale;
        var mbLlx = ctx.MediaBox.LLX;
        var mbLly = ctx.MediaBox.LLY;
        var alpha = (byte)(state.FillAlpha * 255);
        var csName = radial.ColorSpaceName;
        var input = new double[1];

        // Pixel centres (+0.5), same rationale as DrawAxialShading.
        for (var py = yStart; py < yEnd; py++)
        {
            var uy = mbLly + (ctx.PixelH - py - 0.5) * invScale;
            var rowBase = py * ctx.PixelW;
            for (var px = xStart; px < xEnd; px++)
            {
                if (ctx.ClipMask is { } mask && mask[rowBase + px] == 0) continue;

                var ux = mbLlx + (px + 0.5) * invScale;

                if (bboxLocal is not null && inv is not null)
                {
                    TransformPoint(inv, ux, uy, out var lx, out var ly);
                    if (lx < bboxLocal[0] || lx > bboxLocal[2] ||
                        ly < bboxLocal[1] || ly > bboxLocal[3])
                        continue;
                }

                var fx = ux - x0u;
                var fy = uy - y0u;

                // (fx - t*cdx)^2 + (fy - t*cdy)^2 = (r0 + t*dr)^2
                // qa*t^2 - 2*qb*t + qc = 0, pick the larger root in [0, 1].
                var qa = cdx * cdx + cdy * cdy - dr * dr;
                var qb = fx * cdx + fy * cdy + r0 * dr;
                var qc = fx * fx + fy * fy - r0 * r0;

                double t;
                if (Math.Abs(qa) < 1e-12)
                {
                    if (Math.Abs(qb) < 1e-12) continue;
                    t = qc / (2 * qb);
                }
                else
                {
                    var disc = qb * qb - qa * qc;
                    if (disc < 0) continue;
                    var sq = Math.Sqrt(disc);
                    var t1 = (qb + sq) / qa;
                    var t2 = (qb - sq) / qa;
                    // Pick the larger valid root that gives a non-negative radius.
                    t = double.NaN;
                    foreach (var candidate in new[] { t1, t2 })
                    {
                        if (double.IsNaN(candidate)) continue;
                        if (r0 + candidate * dr < 0) continue;
                        if (double.IsNaN(t) || candidate > t) t = candidate;
                    }
                    if (double.IsNaN(t)) continue;
                }

                if (t < 0)
                {
                    if (!extendBefore) continue;
                    t = 0;
                }
                else if (t > 1)
                {
                    if (!extendAfter) continue;
                    t = 1;
                }

                input[0] = domLo + t * domLen;
                var col = radial.Function.Evaluate(input);
                if (col is null) continue;

                ComponentsToRgb(col, csName, out var r, out var g, out var b, radial.TintTransform, radial.AltSpaceName);
                SetPixel(ctx, px, py, r, g, b, alpha);
            }
        }
    }

    /// <summary>Restrict the per-pixel shading loop to the clip mask's bounding box when set.</summary>
    private static void ComputeShadingPixelBounds(RenderContext ctx,
        out int xStart, out int xEnd, out int yStart, out int yEnd)
    {
        xStart = 0; xEnd = ctx.PixelW; yStart = 0; yEnd = ctx.PixelH;
        if (ctx.ClipMask is null) return;

        var mask = ctx.ClipMask;
        int minX = ctx.PixelW, maxX = -1, minY = ctx.PixelH, maxY = -1;
        for (var y = 0; y < ctx.PixelH; y++)
        {
            var rowBase = y * ctx.PixelW;
            for (var x = 0; x < ctx.PixelW; x++)
            {
                if (mask[rowBase + x] == 0) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }
        if (maxX < 0) { xEnd = 0; yEnd = 0; return; }
        xStart = minX; xEnd = maxX + 1;
        yStart = minY; yEnd = maxY + 1;
    }

    /// <summary>Apply an affine matrix [a b c d e f] to a user-space point.</summary>
    private static void TransformPoint(double[] m, double x, double y, out double xo, out double yo)
    {
        xo = m[0] * x + m[2] * y + m[4];
        yo = m[1] * x + m[3] * y + m[5];
    }

    /// <summary>
    /// Convert a shading function's output components to 8-bit RGB according to
    /// the shading's colour space name. Handles the common device colour spaces
    /// (Gray/RGB/CMYK); anything exotic falls back to mid-grey so the gradient
    /// still paints something rather than leaving the region blank.
    /// </summary>
    internal static void ComponentsToRgb(double[] components, string csName,
        out byte r, out byte g, out byte b,
        Functions.PdfFunction? tint = null, string? altName = null)
    {
        // /Separation or /DeviceN output: map the tint components into the alternate
        // device space first, then convert that to RGB.
        if (tint is not null && altName is not null)
        {
            var alt = tint.Evaluate(components);
            if (alt is not null)
            {
                ComponentsToRgb(alt, altName, out r, out g, out b);
                return;
            }
        }

        double rd, gd, bd;
        if (csName == "DeviceGray" || csName == "G" || components.Length == 1)
        {
            rd = gd = bd = components[0];
        }
        else if (csName == "DeviceCMYK" || csName == "CMYK" || components.Length == 4)
        {
            CmykToRgbClamp(components[0], components[1], components[2], components[3],
                out rd, out gd, out bd);
        }
        else if (csName == "Lab" && components.Length >= 3)
        {
            LabColor.ToRgb(components[0], components[1], components[2], out rd, out gd, out bd);
        }
        else if (components.Length >= 3)
        {
            rd = components[0];
            gd = components[1];
            bd = components[2];
        }
        else
        {
            rd = gd = bd = 0.5;
        }

        r = ToByteClamp(rd);
        g = ToByteClamp(gd);
        b = ToByteClamp(bd);
    }

    private static void CmykToRgbClamp(double c, double m, double y, double k,
        out double r, out double g, out double b)
    {
        r = (1 - c) * (1 - k);
        g = (1 - m) * (1 - k);
        b = (1 - y) * (1 - k);
    }

    private static byte ToByteClamp(double v)
    {
        if (v <= 0) return 0;
        if (v >= 1) return 255;
        return (byte)(v * 255);
    }

    // ── Tiling-pattern fill ────────────────────────────────────────
    //
    // PDF 32000 §8.7.3 describes PatternType 1 (tiling) patterns: the colour for a fill
    // operator `f` is produced by executing the pattern's content stream once, then
    // tiling the result at XStep×YStep through the painted region. For the common case
    // where the path fits within a single tile (XStep ≥ BBox width etc.), execution
    // reduces to: run the pattern's content stream with (CTM ← CTM × Pattern.Matrix)
    // into the page buffer, with the filled path acting as a stencil so nothing leaks
    // outside. That's what `FillWithPattern` does — it covers real-world tests that
    // paint an image through a circular clip (38878: PDF uses a PatternType-1 whose
    // content is "q 550 0 0 550 0 0 cm /Image Do Q").

    private static void FillWithPattern(RenderContext ctx, EdgeTable edgeTable, bool evenOdd,
        string patternName, GraphicsState state)
    {
        if (ctx.Patterns?.Get(patternName) is not { } patternObj) return;

        // Tiling patterns (PatternType 1) are streams (the tile content); shading
        // patterns (PatternType 2) are plain dicts that reference a /Shading. Resolve
        // both shapes so the patternType branch below picks the right path.
        PdfStream? patternStream = patternObj switch
        {
            PdfStream s => s,
            _ => ctx.Reader.ResolveStream(patternObj),
        };
        var pdict = patternStream?.Dict ?? ctx.Reader.ResolveDict(patternObj);
        if (pdict is null) return;
        var patternType = (int)pdict.GetInt("PatternType");
        if (patternType is not 1 and not 2) return;

        // Build the clipping stencil from the filled path. Cheap — one pass over the
        // same edge table the solid-fill path uses, writing 0/255 instead of RGBA.
        // When an outer clip is active (e.g. an enclosing W/W*), AND it in so the
        // pattern fill stays within both the path and the outer clip.
        var mask = new byte[ctx.PixelW * ctx.PixelH];
        ScanlineFiller.BuildMask(edgeTable, mask, ctx.PixelW, ctx.PixelH, evenOdd);
        if (ctx.ClipMask is { } outer)
        {
            for (var i = 0; i < mask.Length; i++)
                if (outer[i] == 0) mask[i] = 0;
        }

        if (patternType == 2)
        {
            FillWithShadingPattern(ctx, pdict, state, mask);
            return;
        }

        if (patternStream is null) return;
        byte[] patternContent;
        try { patternContent = ctx.Reader.DecodeStream(patternStream); }
        catch { return; }

        // Pattern's Matrix maps pattern space → user space (PDF 32000 §8.7.3.3).
        var patMatrix = pdict.Get("Matrix") as PdfArray;
        var m = new double[] { 1, 0, 0, 1, 0, 0 };
        if (patMatrix is { Count: >= 6 })
        {
            for (var i = 0; i < 6; i++) m[i] = NumFrom(patMatrix[i]);
        }
        // XStep/YStep drive the tile repetition grid in pattern space.
        var xStep = NumFrom(pdict.Get("XStep"));
        var yStep = NumFrom(pdict.Get("YStep"));
        if (xStep == 0) xStep = 1;
        if (yStep == 0) yStep = 1;

        // Resolve the pattern's own /Resources so Image Do, font lookups etc. inside the
        // pattern content stream find the right objects. Fall back to the page's resources
        // so tiling patterns that reference outer fonts/images still work.
        var patResources = ctx.Reader.ResolveDict(pdict.Get("Resources"));
        var patFonts = ResolveFontDicts(patResources, ctx.Reader);
        var patExtG = ResolveExtGStates(patResources, ctx.Reader);
        var patXObj = ResolveAllXObjects(patResources, ctx.Reader);
        if (ctx.FontDicts is not null)
            foreach (var kv in ctx.FontDicts) patFonts.TryAdd(kv.Key, kv.Value);
        if (ctx.AllXObjects is not null)
            foreach (var kv in ctx.AllXObjects) patXObj.TryAdd(kv.Key, kv.Value);

        var patternContext = new RenderContext(ctx.Pixels, ctx.PixelW, ctx.PixelH, ctx.Scale, ctx.MediaBox, ctx.Reader)
        {
            AllXObjects = patXObj,
            FontDicts = patFonts,
            Patterns = ctx.Reader.ResolveDict(patResources?.Get("Pattern")) ?? ctx.Patterns,
            Shadings = ctx.Reader.ResolveDict(patResources?.Get("Shading")) ?? ctx.Shadings,
            // Install the path stencil so every SetPixel outside the filled shape is a no-op.
            ClipMask = mask,
        };

        // Tile iteration: find which (i, j) tiles cover the filled region in pattern space,
        // then render the pattern content once per tile with its origin offset by
        // (i*XStep, j*YStep). The PDF spec describes the pattern cell as tiling at these
        // steps (§8.7.3.3) — a real-world PDF like 38878 places pattern (0,0) outside the
        // clipped region and relies on tile (0,-1) or similar to cover it.
        ComputePatternTileRange(edgeTable, ctx, m, xStep, yStep,
            out var iMin, out var iMax, out var jMin, out var jMax, out var rawCount);

        // A fine pattern covering a large area would need more tiles than the per-tile
        // loop is capped at, leaving most of the region unpainted. Rasterise one cell to
        // a device-sized tile and stamp it across the masked region instead.
        if (rawCount > 8000 &&
            TryStampTiledPattern(ctx, mask, patternContent, patternContext, patExtG, m, xStep, yStep))
            return;

        for (var j = jMin; j <= jMax; j++)
        {
            for (var i = iMin; i <= iMax; i++)
            {
                // Shift pattern.Matrix's translation so the content stream's native pattern
                // (0,0) lands at user coord corresponding to pattern (i*XStep, j*YStep).
                var tx = i * xStep;
                var ty = j * yStep;
                var tileMatrix = new[]
                {
                    m[0], m[1], m[2], m[3],
                    m[4] + tx * m[0] + ty * m[2],
                    m[5] + tx * m[1] + ty * m[3],
                };
                var tileCtm = GraphicsState.MultiplyMatrices(tileMatrix, state.Ctm);
                RenderContent(patternContent, patternContext, patExtG, tileCtm);
            }
        }
    }

    /// <summary>
    /// Rasterise one tiling-pattern cell to a device-sized tile and stamp it across the
    /// masked region. Used when a fine pattern covers an area too large to execute the
    /// cell per-tile. Handles only axis-aligned, non-flipped pattern matrices (the common
    /// case); returns false to fall back to the per-tile path otherwise.
    /// </summary>
    private static bool TryStampTiledPattern(RenderContext ctx, byte[] mask, byte[] patternContent,
        RenderContext patternContext, Dictionary<string, PdfDictionary>? patExtG,
        double[] m, double xStep, double yStep)
    {
        if (Math.Abs(m[1]) > 1e-9 || Math.Abs(m[2]) > 1e-9) return false; // not axis-aligned
        if (m[0] <= 0 || m[3] <= 0) return false;                         // flipped — let per-tile handle
        double s = m[0] * ctx.Scale;                                      // device px per pattern unit
        int tw = (int)Math.Round(s * xStep), th = (int)Math.Round(s * yStep);
        if (tw < 1 || th < 1 || (long)tw * th > 4_000_000) return false;

        // Render one cell into a tile buffer: the cell content carries its own cm, so an
        // identity CTM plus a tile context whose scale/box map pattern (0,0)…(xStep,yStep)
        // onto [0,tw]×[0,th] places the cell on the tile.
        var tileBuf = new byte[tw * th * 4];
        var tileCtx = new RenderContext(tileBuf, tw, th, s, new Rectangle(0, 0, xStep, yStep), ctx.Reader)
        {
            AllXObjects = patternContext.AllXObjects,
            FontDicts = patternContext.FontDicts,
            Patterns = patternContext.Patterns,
            Shadings = patternContext.Shadings,
        };
        try { RenderContent(patternContent, tileCtx, patExtG, new double[] { 1, 0, 0, 1, 0, 0 }); }
        catch { return false; }

        // Device anchor of pattern point (0,0); the tile's top-left pixel maps to pattern
        // (0, yStep), i.e. device (devX0, devY0 − th). Tiles repeat every tw/th device px.
        double devX0 = (m[4] - ctx.MediaBox.LLX) * ctx.Scale;
        double devY0 = ctx.PixelH - (m[5] - ctx.MediaBox.LLY) * ctx.Scale;
        int offX = (int)Math.Round(devX0), offY = (int)Math.Round(devY0) - th;

        // Masked-region bbox so the stamp loop only touches painted pixels.
        int w = ctx.PixelW, h = ctx.PixelH;
        int xmin = w, xmax = -1, ymin = h, ymax = -1;
        for (var y = 0; y < h; y++)
        {
            var rowOff = y * w;
            for (var x = 0; x < w; x++)
                if (mask[rowOff + x] != 0)
                {
                    if (x < xmin) xmin = x;
                    if (x > xmax) xmax = x;
                    if (y < ymin) ymin = y;
                    if (y > ymax) ymax = y;
                }
        }
        if (xmax < xmin) return true; // nothing to paint, but the fill was "handled"

        for (var y = ymin; y <= ymax; y++)
        {
            int row = (((y - offY) % th) + th) % th;
            var maskRow = y * w;
            for (var x = xmin; x <= xmax; x++)
            {
                if (mask[maskRow + x] == 0) continue;
                int col = (((x - offX) % tw) + tw) % tw;
                int t = (row * tw + col) * 4;
                byte a = tileBuf[t + 3];
                if (a == 0) continue;
                SetPixel(ctx, x, y, tileBuf[t], tileBuf[t + 1], tileBuf[t + 2], a);
            }
        }
        return true;
    }

    /// <summary>
    /// Fill a path with a PatternType-2 shading pattern (PDF 32000 §8.7.3.2). The
    /// pattern's /Matrix maps shading space → user space; we left-multiply it into
    /// the active CTM so DrawAxialShading / DrawRadialShading sample the shading at
    /// the right user-space coordinates. The path's stencil (already AND'd with any
    /// outer clip by the caller) is installed as the active ClipMask so the gradient
    /// only fills pixels inside the path. Restore both on the way out.
    /// </summary>
    private static void FillWithShadingPattern(RenderContext ctx, PdfDictionary pdict,
        GraphicsState state, byte[] mask)
    {
        var shadingObj = ctx.Reader.Resolve(pdict.Get("Shading"));
        if (shadingObj is null) return;
        var shading = ShadingBase.Parse(shadingObj, ctx.Reader);
        if (shading is null) return;

        // Pattern.Matrix · CTM gives the effective shading-space-to-device transform.
        var patMatrix = pdict.Get("Matrix") as PdfArray;
        var savedCtm = state.Ctm;
        if (patMatrix is { Count: >= 6 })
        {
            var m = new double[6];
            for (var i = 0; i < 6; i++) m[i] = NumFrom(patMatrix[i]);
            state.Ctm = GraphicsState.MultiplyMatrices(m, savedCtm);
        }

        var savedClip = ctx.ClipMask;
        ctx.ClipMask = mask;
        try
        {
            switch (shading)
            {
                case AxialShading axial: DrawAxialShading(ctx, axial, state); break;
                case RadialShading radial: DrawRadialShading(ctx, radial, state); break;
                case FreeFormGouraudShading g: DrawGouraudMesh(ctx, g.Vertices, g.Triangles, g.ColorSpaceName, state); break;
                case LatticeFormGouraudShading l: DrawGouraudMesh(ctx, l.Vertices, l.Triangles, l.ColorSpaceName, state); break;
                case CoonsPatchShading c: DrawPatchMesh(ctx, c.Patches, c.ColorSpaceName, state); break;
                case TensorPatchShading t: DrawPatchMesh(ctx, t.Patches, t.ColorSpaceName, state); break;
            }
        }
        finally
        {
            ctx.ClipMask = savedClip;
            state.Ctm = savedCtm;
        }
    }

    /// <summary>
    /// Inverse-map the filled path's pixel bbox into pattern space and derive the tile index
    /// range that can possibly intersect it. Guards: caps the range at ±64 so a near-singular
    /// matrix or tiny step can't trigger a runaway loop. Typical real PDFs need a range of 1–3.
    /// </summary>
    private static void ComputePatternTileRange(EdgeTable edgeTable, RenderContext ctx, double[] m,
        double xStep, double yStep, out int iMin, out int iMax, out int jMin, out int jMax)
        => ComputePatternTileRange(edgeTable, ctx, m, xStep, yStep, out iMin, out iMax, out jMin, out jMax, out _);

    private static void ComputePatternTileRange(EdgeTable edgeTable, RenderContext ctx, double[] m,
        double xStep, double yStep, out int iMin, out int iMax, out int jMin, out int jMax,
        out long rawCount)
    {
        rawCount = 0;
        // Pixel bbox of the filled region (from edge table). Edges now carry fractional
        // Y; floor/ceiling outward to snap to the enclosing integer pixel box.
        int pxMin = int.MaxValue, pxMax = int.MinValue, pyMin = int.MaxValue, pyMax = int.MinValue;
        foreach (var e in edgeTable.Edges)
        {
            var eYMin = (int)Math.Floor(e.YMin);
            var eYMax = (int)Math.Ceiling(e.YMax);
            if (eYMin < pyMin) pyMin = eYMin;
            if (eYMax > pyMax) pyMax = eYMax;
            var xTop = e.XAtYMin;
            var xBot = e.XAtYMin + (e.YMax - e.YMin) * e.InvSlope;
            if (xTop < pxMin) pxMin = (int)Math.Floor(xTop);
            if (xBot < pxMin) pxMin = (int)Math.Floor(xBot);
            if (xTop > pxMax) pxMax = (int)Math.Ceiling(xTop);
            if (xBot > pxMax) pxMax = (int)Math.Ceiling(xBot);
        }
        if (pxMin == int.MaxValue) { iMin = iMax = jMin = jMax = 0; return; }

        // Pixel → user space: inverse of (ctx.PixelH - (user_y - LLY) * Scale).
        double PxToUserX(double px) => px / ctx.Scale + ctx.MediaBox.LLX;
        double PxToUserY(double py) => (ctx.PixelH - py) / ctx.Scale + ctx.MediaBox.LLY;

        // Four corners of the user-space bbox.
        var uxs = new[] { PxToUserX(pxMin), PxToUserX(pxMax) };
        var uys = new[] { PxToUserY(pyMin), PxToUserY(pyMax) };

        // Invert pattern.Matrix (user → pattern). For an affine 2×2 with translation:
        // det=a*d-b*c; inv = [d/det, -b/det, -c/det, a/det, (c*f-d*e)/det, (b*e-a*f)/det].
        var det = m[0] * m[3] - m[1] * m[2];
        if (Math.Abs(det) < 1e-12) { iMin = iMax = jMin = jMax = 0; return; }
        var ia = m[3] / det;
        var ib = -m[1] / det;
        var ic = -m[2] / det;
        var id = m[0] / det;
        var ie = (m[2] * m[5] - m[3] * m[4]) / det;
        var ifv = (m[1] * m[4] - m[0] * m[5]) / det;

        double pxs_min = double.PositiveInfinity, pxs_max = double.NegativeInfinity;
        double pys_min = double.PositiveInfinity, pys_max = double.NegativeInfinity;
        foreach (var ux in uxs)
        {
            foreach (var uy in uys)
            {
                var ppx = ux * ia + uy * ic + ie;
                var ppy = ux * ib + uy * id + ifv;
                if (ppx < pxs_min) pxs_min = ppx;
                if (ppx > pxs_max) pxs_max = ppx;
                if (ppy < pys_min) pys_min = ppy;
                if (ppy > pys_max) pys_max = ppy;
            }
        }

        iMin = (int)Math.Floor(pxs_min / xStep) - 1;
        iMax = (int)Math.Ceiling(pxs_max / xStep) + 1;
        jMin = (int)Math.Floor(pys_min / yStep) - 1;
        jMax = (int)Math.Ceiling(pys_max / yStep) + 1;

        // Unclamped tile count — lets the caller switch to a tile-and-stamp fill when a
        // fine pattern covers a large area (per-tile execution would be capped below and
        // leave most of the region unpainted).
        rawCount = (long)(iMax - iMin + 1) * (jMax - jMin + 1);

        // Guard against runaway (should never trip on real PDFs; XStep of 0 was already handled).
        iMin = Math.Max(iMin, -64); iMax = Math.Min(iMax, 64);
        jMin = Math.Max(jMin, -64); jMax = Math.Min(jMax, 64);
    }

    /// <summary>Read a numeric PdfObject (integer or real) into a double. Zero for other types.</summary>
    private static double NumFrom(PdfObject? o) => o switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0.0,
    };

    // ── Path rendering ──────────────────────────────────────────────

    private static void DrawPath(RenderContext ctx, IReadOnlyList<PathCommand> segments,
        string op, GraphicsState state)
    {
        if (segments.Count == 0) return;

        bool doFill = op is "f" or "F" or "f*" or "B" or "B*" or "b" or "b*";
        bool doStroke = op is "S" or "s" or "B" or "B*" or "b" or "b*";
        bool evenOdd = op is "f*" or "B*" or "b*";

        if (!doFill && !doStroke) return;

        // Inherit the active blend mode for the fill/stroke pixels.
        ctx.CurrentBlendMode = state.BlendMode;
        ctx.SoftMaskAlpha = state.SoftMask is { } sm__ ? ResolveSoftMaskAlpha(ctx, sm__) : null;

        var ctm = state.Ctm;

        // Transform point from PDF user space to pixel coordinates
        void Transform(double x, double y, out double px, out double py)
        {
            var tx = ctm[0] * x + ctm[2] * y + ctm[4];
            var ty = ctm[1] * x + ctm[3] * y + ctm[5];
            px = (tx - ctx.MediaBox.LLX) * ctx.Scale;
            py = ctx.PixelH - (ty - ctx.MediaBox.LLY) * ctx.Scale;
        }

        // Build edge table from path commands
        var edgeTable = new EdgeTable();
        double curX = 0, curY = 0;   // current point (pixel coords)
        double startX = 0, startY = 0; // subpath start

        foreach (var seg in segments)
        {
            switch (seg.Op)
            {
                case PathOp.MoveTo:
                    Transform(seg.X1, seg.Y1, out curX, out curY);
                    startX = curX;
                    startY = curY;
                    break;
                case PathOp.LineTo:
                    Transform(seg.X1, seg.Y1, out var lx, out var ly);
                    edgeTable.AddLine(curX, curY, lx, ly);
                    curX = lx; curY = ly;
                    break;
                case PathOp.CurveTo:
                    Transform(seg.X1, seg.Y1, out var c1x, out var c1y);
                    Transform(seg.X2, seg.Y2, out var c2x, out var c2y);
                    Transform(seg.X3, seg.Y3, out var c3x, out var c3y);
                    edgeTable.AddCubicBezier(curX, curY, c1x, c1y, c2x, c2y, c3x, c3y);
                    curX = c3x; curY = c3y;
                    break;
                case PathOp.CurveToV: // first control point = current point
                    Transform(seg.X1, seg.Y1, out var v2x, out var v2y);
                    Transform(seg.X2, seg.Y2, out var v3x, out var v3y);
                    edgeTable.AddCubicBezier(curX, curY, curX, curY, v2x, v2y, v3x, v3y);
                    curX = v3x; curY = v3y;
                    break;
                case PathOp.CurveToY: // second control point = endpoint
                    Transform(seg.X1, seg.Y1, out var y1x, out var y1y);
                    Transform(seg.X3, seg.Y3, out var y3x, out var y3y);
                    edgeTable.AddCubicBezier(curX, curY, y1x, y1y, y3x, y3y, y3x, y3y);
                    curX = y3x; curY = y3y;
                    break;
                case PathOp.Rect:
                    Transform(seg.X1, seg.Y1, out var rx, out var ry);
                    Transform(seg.X1 + seg.X2, seg.Y1, out var rx2, out var ry2);
                    Transform(seg.X1 + seg.X2, seg.Y1 + seg.Y2, out var rx3, out var ry3);
                    Transform(seg.X1, seg.Y1 + seg.Y2, out var rx4, out var ry4);
                    edgeTable.AddLine(rx, ry, rx2, ry2);
                    edgeTable.AddLine(rx2, ry2, rx3, ry3);
                    edgeTable.AddLine(rx3, ry3, rx4, ry4);
                    edgeTable.AddLine(rx4, ry4, rx, ry);
                    curX = rx; curY = ry;
                    startX = rx; startY = ry;
                    break;
                case PathOp.Close:
                    edgeTable.AddLine(curX, curY, startX, startY);
                    curX = startX; curY = startY;
                    break;
            }
        }

        // Any active W/W* clip constrains the fill / stroke to its stencil.
        var clip = state.ClipMask;

        if (doFill)
        {
            if (state.FillPatternName is { } patName)
            {
                // Pattern fill: build a stencil from the path and paint the pattern's
                // content stream through it instead of a solid RGBA blit.
                FillWithPattern(ctx, edgeTable, evenOdd, patName, state);
            }
            else
            {
                var r = (byte)(state.FillR * 255);
                var g = (byte)(state.FillG * 255);
                var b = (byte)(state.FillB * 255);
                ScanlineFiller.Fill(edgeTable, ctx.Pixels, ctx.PixelW, ctx.PixelH,
                    r, g, b, (byte)(state.FillAlpha * 255), evenOdd, clip, state.BlendMode,
                    knockout: ctx.IsKnockoutGroup, softMask: ctx.SoftMaskAlpha);
            }
        }

        if (doStroke)
        {
            var r = (byte)(state.StrokeR * 255);
            var g = (byte)(state.StrokeG * 255);
            var b = (byte)(state.StrokeB * 255);
            // Line width `w` is in user space (PDF 32000 §8.4.3.2). It must be transformed
            // through the CTM into device space before converting to pixels. Using ctx.Scale
            // alone ignores the CTM and produces grossly thick strokes on content streams
            // with sub-unit `cm` scaling (e.g. ACORD forms with `0.12 cm` → 8× too thick).
            var ctmScale = Math.Sqrt(Math.Abs(ctm[0] * ctm[3] - ctm[1] * ctm[2]));
            var lw = state.LineWidth * ctmScale * ctx.Scale;
            if (lw < 1) lw = 1;

            // Stroke each line segment of the path
            double sx = 0, sy = 0, stX = 0, stY = 0;
            foreach (var seg in segments)
            {
                switch (seg.Op)
                {
                    case PathOp.MoveTo:
                        Transform(seg.X1, seg.Y1, out sx, out sy);
                        stX = sx; stY = sy;
                        break;
                    case PathOp.LineTo:
                        Transform(seg.X1, seg.Y1, out var slx, out var sly);
                        ScanlineFiller.StrokeLine(ctx.Pixels, ctx.PixelW, ctx.PixelH,
                            sx, sy, slx, sly, r, g, b, 255, lw, clip,
                            blendMode: ctx.CurrentBlendMode, knockout: ctx.IsKnockoutGroup, softMask: ctx.SoftMaskAlpha);
                        sx = slx; sy = sly;
                        break;
                    case PathOp.Close:
                        ScanlineFiller.StrokeLine(ctx.Pixels, ctx.PixelW, ctx.PixelH,
                            sx, sy, stX, stY, r, g, b, 255, lw, clip,
                            blendMode: ctx.CurrentBlendMode, knockout: ctx.IsKnockoutGroup, softMask: ctx.SoftMaskAlpha);
                        sx = stX; sy = stY;
                        break;
                    // Cubic bezier control points all live in user space — transform each
                    // through the CTM, then flatten in device space so the per-segment
                    // tolerance is in pixels (not user units, which can be sub-pixel after
                    // a small `cm` scale).
                    case PathOp.CurveTo:
                        Transform(seg.X1, seg.Y1, out var c1x, out var c1y);
                        Transform(seg.X2, seg.Y2, out var c2x, out var c2y);
                        Transform(seg.X3, seg.Y3, out var c3x, out var c3y);
                        StrokeCubic(ctx, sx, sy, c1x, c1y, c2x, c2y, c3x, c3y, r, g, b, lw, clip);
                        sx = c3x; sy = c3y;
                        break;
                    case PathOp.CurveToV:
                        // First control point coincides with current point.
                        Transform(seg.X1, seg.Y1, out var v2x, out var v2y);
                        Transform(seg.X2, seg.Y2, out var v3x, out var v3y);
                        StrokeCubic(ctx, sx, sy, sx, sy, v2x, v2y, v3x, v3y, r, g, b, lw, clip);
                        sx = v3x; sy = v3y;
                        break;
                    case PathOp.CurveToY:
                        // Second control point coincides with endpoint.
                        Transform(seg.X1, seg.Y1, out var y1x, out var y1y);
                        Transform(seg.X3, seg.Y3, out var y3x, out var y3y);
                        StrokeCubic(ctx, sx, sy, y1x, y1y, y3x, y3y, y3x, y3y, r, g, b, lw, clip);
                        sx = y3x; sy = y3y;
                        break;
                    case PathOp.Rect:
                        Transform(seg.X1, seg.Y1, out var sr1, out var sr2);
                        Transform(seg.X1 + seg.X2, seg.Y1, out var sr3, out var sr4);
                        Transform(seg.X1 + seg.X2, seg.Y1 + seg.Y2, out var sr5, out var sr6);
                        Transform(seg.X1, seg.Y1 + seg.Y2, out var sr7, out var sr8);
                        ScanlineFiller.StrokeLine(ctx.Pixels, ctx.PixelW, ctx.PixelH, sr1, sr2, sr3, sr4, r, g, b, 255, lw, clip,
                            blendMode: ctx.CurrentBlendMode, knockout: ctx.IsKnockoutGroup, softMask: ctx.SoftMaskAlpha);
                        ScanlineFiller.StrokeLine(ctx.Pixels, ctx.PixelW, ctx.PixelH, sr3, sr4, sr5, sr6, r, g, b, 255, lw, clip,
                            blendMode: ctx.CurrentBlendMode, knockout: ctx.IsKnockoutGroup, softMask: ctx.SoftMaskAlpha);
                        ScanlineFiller.StrokeLine(ctx.Pixels, ctx.PixelW, ctx.PixelH, sr5, sr6, sr7, sr8, r, g, b, 255, lw, clip,
                            blendMode: ctx.CurrentBlendMode, knockout: ctx.IsKnockoutGroup, softMask: ctx.SoftMaskAlpha);
                        ScanlineFiller.StrokeLine(ctx.Pixels, ctx.PixelW, ctx.PixelH, sr7, sr8, sr1, sr2, r, g, b, 255, lw, clip,
                            blendMode: ctx.CurrentBlendMode, knockout: ctx.IsKnockoutGroup, softMask: ctx.SoftMaskAlpha);
                        sx = sr1; sy = sr2;
                        stX = sr1; stY = sr2;
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Stroke a cubic Bezier by flattening it into short line segments, then drawing
    /// each segment. Without this, strokes of curves rendered as a single line from
    /// start to endpoint (e.g. a 4-curve circle stroked as a diamond). De Casteljau
    /// subdivision at t=0.5; segment tolerance is 0.5px in device space, capped at
    /// depth 16. All inputs are in pixel coords.
    /// </summary>
    private static void StrokeCubic(RenderContext ctx,
        double x0, double y0, double cx1, double cy1, double cx2, double cy2, double x3, double y3,
        byte r, byte g, byte b, double lw, byte[]? clip, int depth = 0)
    {
        // Flatness check: if both control points lie close to the chord (x0,y0)→(x3,y3),
        // treat the bezier as a straight segment. d = perpendicular distance to the chord;
        // squared form avoids sqrt. Threshold 0.25·L² ≡ |d1|+|d2| ≤ 0.5·L which is the same
        // as EdgeTable.FlattenCubic uses for fills, keeping fill and stroke shapes consistent.
        var dx = x3 - x0;
        var dy = y3 - y0;
        var d1 = Math.Abs((cx1 - x3) * dy - (cy1 - y3) * dx);
        var d2 = Math.Abs((cx2 - x3) * dy - (cy2 - y3) * dx);
        var denom = dx * dx + dy * dy;
        if (depth >= 16 || (d1 + d2) * (d1 + d2) <= 0.25 * denom || denom < 0.001)
        {
            ScanlineFiller.StrokeLine(ctx.Pixels, ctx.PixelW, ctx.PixelH,
                x0, y0, x3, y3, r, g, b, 255, lw, clip,
                blendMode: ctx.CurrentBlendMode, knockout: ctx.IsKnockoutGroup, softMask: ctx.SoftMaskAlpha);
            return;
        }

        var mx01 = (x0 + cx1) * 0.5; var my01 = (y0 + cy1) * 0.5;
        var mx12 = (cx1 + cx2) * 0.5; var my12 = (cy1 + cy2) * 0.5;
        var mx23 = (cx2 + x3) * 0.5; var my23 = (cy2 + y3) * 0.5;
        var mx012 = (mx01 + mx12) * 0.5; var my012 = (my01 + my12) * 0.5;
        var mx123 = (mx12 + mx23) * 0.5; var my123 = (my12 + my23) * 0.5;
        var mx0123 = (mx012 + mx123) * 0.5; var my0123 = (my012 + my123) * 0.5;

        StrokeCubic(ctx, x0, y0, mx01, my01, mx012, my012, mx0123, my0123, r, g, b, lw, clip, depth + 1);
        StrokeCubic(ctx, mx0123, my0123, mx123, my123, mx23, my23, x3, y3, r, g, b, lw, clip, depth + 1);
    }

    /// <summary>
    /// Build a clip-path stencil from the current path and AND it into the enclosing
    /// <see cref="GraphicsState.ClipMask"/> — implements PDF 32000 §8.5.4.1 ("the
    /// current clipping path is set to the intersection of the current clipping path
    /// and the current path"). Called for the <c>W</c> / <c>W*</c> operators *after*
    /// the same path has been painted, so the mask only affects subsequent content.
    /// </summary>
    private static void InstallClipFromPath(RenderContext ctx, IReadOnlyList<PathCommand> segments,
        GraphicsState state, bool evenOdd)
    {
        if (segments.Count == 0) return;

        // Fast path: content streams regularly emit viewport-covering clips (e.g. a
        // full-page `W` wrapping every text block, a common PDF-generator idiom).
        // When the path AABB already contains the whole viewport and there's no outer
        // clip, the W operator is a no-op — skip the per-call 8.4MB mask allocation.
        if (state.ClipMask is null && PathBBoxCoversViewport(segments, state.Ctm, ctx))
            return;

        var edgeTable = BuildPathEdgeTable(segments, state.Ctm, ctx);
        var mask = new byte[ctx.PixelW * ctx.PixelH];
        ScanlineFiller.BuildMask(edgeTable, mask, ctx.PixelW, ctx.PixelH, evenOdd);

        // Intersect with any existing clip so nested W / W* keeps tightening rather
        // than replacing the outer clip wholesale.
        if (state.ClipMask is { } outer)
        {
            for (var i = 0; i < mask.Length; i++)
                mask[i] = (byte)(mask[i] & outer[i]);
        }

        state.ClipMask = mask;
        ctx.ClipMask = mask;
    }

    // Compute the pixel-space AABB of a path in the active CTM. Returns true when
    // the AABB contains (0,0)..(PixelW,PixelH) with a small margin — i.e. the clip
    // is a viewport-covering rectangle that would produce an all-255 mask.
    private static bool PathBBoxCoversViewport(IReadOnlyList<PathCommand> segments,
        double[] ctm, RenderContext ctx)
    {
        double pxLo = double.MaxValue, pxHi = double.MinValue;
        double pyLo = double.MaxValue, pyHi = double.MinValue;

        void Visit(double x, double y)
        {
            var tx = ctm[0] * x + ctm[2] * y + ctm[4];
            var ty = ctm[1] * x + ctm[3] * y + ctm[5];
            var px = (tx - ctx.MediaBox.LLX) * ctx.Scale;
            var py = ctx.PixelH - (ty - ctx.MediaBox.LLY) * ctx.Scale;
            if (px < pxLo) pxLo = px; if (px > pxHi) pxHi = px;
            if (py < pyLo) pyLo = py; if (py > pyHi) pyHi = py;
        }

        foreach (var seg in segments)
        {
            switch (seg.Op)
            {
                case PathOp.MoveTo:
                case PathOp.LineTo:
                    Visit(seg.X1, seg.Y1); break;
                case PathOp.CurveTo:
                    Visit(seg.X1, seg.Y1); Visit(seg.X2, seg.Y2); Visit(seg.X3, seg.Y3); break;
                case PathOp.CurveToV:
                case PathOp.CurveToY:
                    Visit(seg.X1, seg.Y1); Visit(seg.X2, seg.Y2); break;
                case PathOp.Rect:
                    Visit(seg.X1, seg.Y1);
                    Visit(seg.X1 + seg.X2, seg.Y1 + seg.Y2); break;
                // Close contributes no new point
            }
        }
        return pxLo <= 0 && pxHi >= ctx.PixelW && pyLo <= 0 && pyHi >= ctx.PixelH;
    }

    /// <summary>
    /// Flatten a content-stream path into an <see cref="EdgeTable"/> in pixel coords.
    /// Shared between <see cref="DrawPath"/> and <see cref="InstallClipFromPath"/> so
    /// clip-path geometry matches the painted-path geometry exactly.
    /// </summary>
    private static EdgeTable BuildPathEdgeTable(IReadOnlyList<PathCommand> segments,
        double[] ctm, RenderContext ctx)
    {
        var et = new EdgeTable();
        double curX = 0, curY = 0, startX = 0, startY = 0;

        void Transform(double x, double y, out double px, out double py)
        {
            var tx = ctm[0] * x + ctm[2] * y + ctm[4];
            var ty = ctm[1] * x + ctm[3] * y + ctm[5];
            px = (tx - ctx.MediaBox.LLX) * ctx.Scale;
            py = ctx.PixelH - (ty - ctx.MediaBox.LLY) * ctx.Scale;
        }

        foreach (var seg in segments)
        {
            switch (seg.Op)
            {
                case PathOp.MoveTo:
                    Transform(seg.X1, seg.Y1, out curX, out curY);
                    startX = curX; startY = curY;
                    break;
                case PathOp.LineTo:
                    Transform(seg.X1, seg.Y1, out var lx, out var ly);
                    et.AddLine(curX, curY, lx, ly);
                    curX = lx; curY = ly;
                    break;
                case PathOp.CurveTo:
                    Transform(seg.X1, seg.Y1, out var c1x, out var c1y);
                    Transform(seg.X2, seg.Y2, out var c2x, out var c2y);
                    Transform(seg.X3, seg.Y3, out var c3x, out var c3y);
                    et.AddCubicBezier(curX, curY, c1x, c1y, c2x, c2y, c3x, c3y);
                    curX = c3x; curY = c3y;
                    break;
                case PathOp.CurveToV:
                    Transform(seg.X1, seg.Y1, out var v2x, out var v2y);
                    Transform(seg.X2, seg.Y2, out var v3x, out var v3y);
                    et.AddCubicBezier(curX, curY, curX, curY, v2x, v2y, v3x, v3y);
                    curX = v3x; curY = v3y;
                    break;
                case PathOp.CurveToY:
                    Transform(seg.X1, seg.Y1, out var y1x, out var y1y);
                    Transform(seg.X3, seg.Y3, out var y3x, out var y3y);
                    et.AddCubicBezier(curX, curY, y1x, y1y, y3x, y3y, y3x, y3y);
                    curX = y3x; curY = y3y;
                    break;
                case PathOp.Rect:
                    Transform(seg.X1, seg.Y1, out var rx, out var ry);
                    Transform(seg.X1 + seg.X2, seg.Y1, out var rx2, out var ry2);
                    Transform(seg.X1 + seg.X2, seg.Y1 + seg.Y2, out var rx3, out var ry3);
                    Transform(seg.X1, seg.Y1 + seg.Y2, out var rx4, out var ry4);
                    et.AddLine(rx, ry, rx2, ry2);
                    et.AddLine(rx2, ry2, rx3, ry3);
                    et.AddLine(rx3, ry3, rx4, ry4);
                    et.AddLine(rx4, ry4, rx, ry);
                    curX = rx; curY = ry;
                    startX = rx; startY = ry;
                    break;
                case PathOp.Close:
                    et.AddLine(curX, curY, startX, startY);
                    curX = startX; curY = startY;
                    break;
            }
        }
        return et;
    }

    // ── Pixel operations ────────────────────────────────────────────

    private static void SetPixel(RenderContext ctx, int x, int y, byte r, byte g, byte b, byte a)
    {
        if (x < 0 || x >= ctx.PixelW || y < 0 || y >= ctx.PixelH) return;
        // Clip-mask gate: zero in the mask means this pixel is outside the current clip
        // region (e.g. outside a pattern-filled path) and must be left untouched.
        var pxIdx = y * ctx.PixelW + x;
        if (ctx.ClipMask is { } mask && mask[pxIdx] == 0) return;
        // Soft mask (PDF 32000 §11.6.5.4): per-pixel alpha multiplied into the
        // fragment's effective alpha. Mask = 0 → pixel is fully masked, skip.
        if (ctx.SoftMaskAlpha is { } sm)
        {
            var m = sm[pxIdx];
            if (m == 0) return;
            a = (byte)((a * m + 127) / 255);
            if (a == 0) return;
        }
        var idx = pxIdx * 4;

        // Knockout group (PDF 32000 §11.4.4): compositing inside the group uses the
        // group's original transparent backdrop, not the accumulated scratch state, so
        // every fragment replaces whatever was there. Blend modes against a transparent
        // backdrop reduce to src·α per the spec's compositing equation, so dispatch is
        // skipped here too — overlapping draws inside a /K group show only the topmost.
        if (ctx.IsKnockoutGroup)
        {
            if (a == 0) return;
            ctx.Pixels[idx]     = r;
            ctx.Pixels[idx + 1] = g;
            ctx.Pixels[idx + 2] = b;
            ctx.Pixels[idx + 3] = a;
            return;
        }

        // PDF 32000 §11.3.5: apply the blend formula B(Cb, Cs) — separable per
        // channel for most modes, full-RGB-triple for HSL — then alpha-blend the
        // blended source with the destination at the source's alpha (Porter-Duff
        // source-over). The Normal-mode fast path below stays as a byte-copy so
        // the common case (no blend mode, no alpha) keeps its speed — only
        // non-Normal modes pay the dispatch cost.
        var mode = BlendModes.Parse(ctx.CurrentBlendMode);
        if (mode != Rasterizer.BlendMode.Normal)
        {
            if (a == 0) return;
            BlendModes.Blend(mode, ctx.Pixels[idx], ctx.Pixels[idx + 1], ctx.Pixels[idx + 2],
                r, g, b, out var ibr, out var ibg, out var ibb);
            byte br = (byte)ibr, bg = (byte)ibg, bb = (byte)ibb;
            if (a == 255)
            {
                ctx.Pixels[idx] = br;
                ctx.Pixels[idx + 1] = bg;
                ctx.Pixels[idx + 2] = bb;
                ctx.Pixels[idx + 3] = 255;
            }
            else
            {
                var srcAm = a / 255.0;
                var dstAm = 1.0 - srcAm;
                ctx.Pixels[idx] = (byte)(br * srcAm + ctx.Pixels[idx] * dstAm);
                ctx.Pixels[idx + 1] = (byte)(bg * srcAm + ctx.Pixels[idx + 1] * dstAm);
                ctx.Pixels[idx + 2] = (byte)(bb * srcAm + ctx.Pixels[idx + 2] * dstAm);
                ctx.Pixels[idx + 3] = 255;
            }
            return;
        }

        if (a == 255)
        {
            ctx.Pixels[idx] = r;
            ctx.Pixels[idx + 1] = g;
            ctx.Pixels[idx + 2] = b;
            ctx.Pixels[idx + 3] = 255;
        }
        else if (a > 0)
        {
            // Alpha blend
            var srcA = a / 255.0;
            var dstA = 1.0 - srcA;
            ctx.Pixels[idx] = (byte)(r * srcA + ctx.Pixels[idx] * dstA);
            ctx.Pixels[idx + 1] = (byte)(g * srcA + ctx.Pixels[idx + 1] * dstA);
            ctx.Pixels[idx + 2] = (byte)(b * srcA + ctx.Pixels[idx + 2] * dstA);
            ctx.Pixels[idx + 3] = 255;
        }
    }

    private static void FillRect(RenderContext ctx, int x, int y, int w, int h,
        byte r, byte g, byte b, byte a)
    {
        var x0 = Math.Max(0, x);
        var y0 = Math.Max(0, y);
        var x1 = Math.Min(ctx.PixelW, x + w);
        var y1 = Math.Min(ctx.PixelH, y + h);

        for (int py = y0; py < y1; py++)
        {
            for (int px = x0; px < x1; px++)
            {
                SetPixel(ctx, px, py, r, g, b, a);
            }
        }
    }

    private static void BlitRGB(RenderContext ctx, byte[] src, int srcW, int srcH,
        int dstX, int dstY, int dstW, int dstH, byte[]? alpha = null, int alphaW = 0, int alphaH = 0,
        double fillAlpha = 1.0, bool flipY = false, bool flipX = false)
    {
        // /CA /ca via the gs ExtGState arrives on state.FillAlpha; multiplied with
        // any SMask per-pixel opacity to yield the final alpha. fillAlpha=1 keeps
        // the old behaviour for callers that don't pass it.
        var fa = (int)Math.Round(fillAlpha * 255);
        if (fa < 0) fa = 0; else if (fa > 255) fa = 255;

        for (int y = 0; y < dstH; y++)
        {
            // PDF 32000 §8.9.4 image data rows are top-down. When the CTM applies
            // a vertical flip (ctm[3] < 0) the caller sets flipY so the source rows
            // are sampled bottom-up — without this, headers like the "Hilmar Lumber"
            // banner in 33341 render upside-down.
            var sy = flipY ? (srcH - 1 - y * srcH / dstH) : y * srcH / dstH;
            var dy = dstY + y;
            if (dy < 0 || dy >= ctx.PixelH) continue;

            for (int x = 0; x < dstW; x++)
            {
                var sx = flipX ? (srcW - 1 - x * srcW / dstW) : x * srcW / dstW;
                var dx = dstX + x;
                if (dx < 0 || dx >= ctx.PixelW) continue;

                var si = (sy * srcW + sx) * 3;
                if (si + 2 >= src.Length) continue;

                // SMask / stencil-Mask alpha (PDF 32000 §11.6.5.3, §8.9.6.3). The mask is
                // a separate image that maps to the same unit square as the base image but
                // may use a very different resolution. Sampling it at the DEST resolution
                // (with area-averaging when it is higher-res than the output) preserves
                // thin strokes that a single point-sample at the low base-image resolution
                // would drop — e.g. 40920's 2480×3507 stencil over a 207×293 photo. For a
                // same-resolution mask this reduces to the previous point sample.
                int a = fa;
                if (alpha is not null && alphaW > 0 && alphaH > 0)
                    a = (a * SampleAlpha(alpha, alphaW, alphaH, x, y, dstW, dstH, flipX, flipY)) / 255;
                SetPixel(ctx, dx, dy, src[si], src[si + 1], src[si + 2], (byte)a);
            }
        }
    }

    private static void BlitGray(RenderContext ctx, byte[] src, int srcW, int srcH,
        int dstX, int dstY, int dstW, int dstH, byte[]? alpha = null, int alphaW = 0, int alphaH = 0,
        double fillAlpha = 1.0)
    {
        var fa = (int)Math.Round(fillAlpha * 255);
        if (fa < 0) fa = 0; else if (fa > 255) fa = 255;

        for (int y = 0; y < dstH; y++)
        {
            var sy = y * srcH / dstH;
            var dy = dstY + y;
            if (dy < 0 || dy >= ctx.PixelH) continue;

            for (int x = 0; x < dstW; x++)
            {
                var sx = x * srcW / dstW;
                var dx = dstX + x;
                if (dx < 0 || dx >= ctx.PixelW) continue;

                var si = sy * srcW + sx;
                if (si >= src.Length) continue;
                var g = src[si];

                int a = fa;
                if (alpha is not null && alphaW > 0 && alphaH > 0)
                    a = (a * SampleAlpha(alpha, alphaW, alphaH, x, y, dstW, dstH, false, false)) / 255;
                SetPixel(ctx, dx, dy, g, g, g, (byte)a);
            }
        }
    }

    /// <summary>
    /// Sample a mask/alpha plane for dest pixel (<paramref name="x"/>,<paramref name="y"/>)
    /// of a <paramref name="dstW"/>×<paramref name="dstH"/> blit. The mask maps to the same
    /// unit square as the base image, so it is sampled in dest space (mirrored when the blit
    /// flips) and area-averaged over the dest pixel's footprint — preserving thin features
    /// of a mask that is higher resolution than the output. A box footprint of at most a few
    /// samples per axis is taken to bound cost. Returns the 0..255 opacity.
    /// </summary>
    private static int SampleAlpha(byte[] alpha, int alphaW, int alphaH, int x, int y,
        int dstW, int dstH, bool flipX, bool flipY)
    {
        // Footprint of this dest pixel in the mask grid, mirrored per flip.
        long ax0 = flipX ? (long)(dstW - 1 - x) * alphaW / dstW : (long)x * alphaW / dstW;
        long ax1 = flipX ? (long)(dstW - x) * alphaW / dstW : (long)(x + 1) * alphaW / dstW;
        long ay0 = flipY ? (long)(dstH - 1 - y) * alphaH / dstH : (long)y * alphaH / dstH;
        long ay1 = flipY ? (long)(dstH - y) * alphaH / dstH : (long)(y + 1) * alphaH / dstH;
        if (ax1 <= ax0) ax1 = ax0 + 1;
        if (ay1 <= ay0) ay1 = ay0 + 1;
        // Cap the averaged footprint so heavy downscales stay cheap (sub-sampled box).
        const int MaxSpan = 8;
        long stepX = Math.Max(1, (ax1 - ax0 + MaxSpan - 1) / MaxSpan);
        long stepY = Math.Max(1, (ay1 - ay0 + MaxSpan - 1) / MaxSpan);
        long sum = 0, cnt = 0;
        for (long ay = ay0; ay < ay1; ay += stepY)
        {
            long rowBase = ay * alphaW;
            for (long ax = ax0; ax < ax1; ax += stepX)
            {
                long ai = rowBase + ax;
                if ((ulong)ai < (ulong)alpha.Length) { sum += alpha[ai]; cnt++; }
            }
        }
        return cnt > 0 ? (int)(sum / cnt) : 255;
    }

    private static void BlitCMYK(RenderContext ctx, byte[] src, int srcW, int srcH,
        int dstX, int dstY, int dstW, int dstH)
    {
        for (int y = 0; y < dstH; y++)
        {
            var sy = y * srcH / dstH;
            var dy = dstY + y;
            if (dy < 0 || dy >= ctx.PixelH) continue;

            for (int x = 0; x < dstW; x++)
            {
                var sx = x * srcW / dstW;
                var dx = dstX + x;
                if (dx < 0 || dx >= ctx.PixelW) continue;

                var si = (sy * srcW + sx) * 4;
                if (si + 3 >= src.Length) continue;
                var c = src[si];
                var m = src[si + 1];
                var yy = src[si + 2];
                var k = src[si + 3];
                // Simple CMYK→RGB: R = 255*(1-C/255)*(1-K/255)
                var r = (byte)(255 * (255 - c) * (255 - k) / 65025);
                var g = (byte)(255 * (255 - m) * (255 - k) / 65025);
                var b = (byte)(255 * (255 - yy) * (255 - k) / 65025);
                SetPixel(ctx, dx, dy, r, g, b, 255);
            }
        }
    }

    private static void BlitBilevel(RenderContext ctx, byte[] src, int srcW, int srcH,
        int dstX, int dstY, int dstW, int dstH)
    {
        if (src.Length == 0 || srcW <= 0 || srcH <= 0) return;
        // Use long for the running offset — sy*rowBytes can overflow int for very
        // large (≥50k-pixel) bilevel images, which flips byteIdx negative and bypasses
        // the `>= src.Length` guard, throwing IndexOutOfRangeException on line 1813.
        // Seen on documents with many (3600) bilevel images in one PDF.
        long rowBytes = (srcW + 7) / 8;
        for (int y = 0; y < dstH; y++)
        {
            var sy = (long)y * srcH / dstH;
            var dy = dstY + y;
            if (dy < 0 || dy >= ctx.PixelH) continue;

            for (int x = 0; x < dstW; x++)
            {
                var sx = (long)x * srcW / dstW;
                var dx = dstX + x;
                if (dx < 0 || dx >= ctx.PixelW) continue;

                var byteIdx = sy * rowBytes + sx / 8;
                if (byteIdx < 0 || byteIdx >= src.Length) continue;
                var bit = (src[byteIdx] >> (7 - (int)(sx & 7))) & 1;
                // Default /Decode [0 1] for 1bpc DeviceGray: bit=1 → 1 → white,
                // bit=0 → 0 → black. (ImageMask uses the opposite convention but
                // takes the DrawImageMask branch before reaching here.) Treating
                // bit=1 as black inverts every B&W background image — 33702 was
                // rendering a form-with-data PDF as light-text-on-black instead
                // of dark-text-on-white.
                var val = (byte)(bit == 1 ? 255 : 0);
                SetPixel(ctx, dx, dy, val, val, val, 255);
            }
        }
    }

    /// <summary>Resolve ColorSpace entry to a simple device color space name.</summary>
    private static string ResolveColorSpaceName(PdfObject? csObj, PdfReader reader)
        => ResolveImageColorSpace(csObj, reader).BaseName;

    /// <summary>Resolved image colour space: base device name and, for Indexed, the palette bytes.</summary>
    internal readonly struct ImageColorSpaceInfo
    {
        public string BaseName { get; init; }
        public byte[]? Palette { get; init; }
        public int PaletteComponents { get; init; } // bytes per palette entry (1=Gray, 3=RGB, 4=CMYK)
        // For a single-component /Separation (or 1-colorant /DeviceN) image: the tint
        // transform plus its alternate-space family, so each sample maps tint →
        // alternate components → RGB. Null for ordinary device spaces.
        public Functions.PdfFunction? TintTransform { get; init; }
        public string? AltSpaceName { get; init; }
    }

    /// <summary>
    /// Resolve a /ColorSpace entry. For Indexed, walks down to the base device space and extracts
    /// the palette lookup bytes (spec §8.6.6.3: [/Indexed base hival lookup], lookup is a string or stream).
    /// </summary>
    internal static ImageColorSpaceInfo ResolveImageColorSpace(PdfObject? csObj, PdfReader reader)
    {
        if (csObj is PdfName name)
            return new ImageColorSpaceInfo { BaseName = name.Value, PaletteComponents = ComponentsForBase(name.Value) };

        csObj = reader.Resolve(csObj);
        if (csObj is PdfName name2)
            return new ImageColorSpaceInfo { BaseName = name2.Value, PaletteComponents = ComponentsForBase(name2.Value) };

        if (csObj is PdfArray arr && arr.Count > 0)
        {
            var first = arr[0] is PdfName fn ? fn.Value : null;
            if (first == "ICCBased" && arr.Count > 1)
            {
                var profileStream = reader.ResolveStream(arr[1]);
                if (profileStream is not null)
                {
                    var n = (int)profileStream.Dict.GetInt("N");
                    var derived = n switch { 1 => "DeviceGray", 4 => "DeviceCMYK", _ => "DeviceRGB" };
                    return new ImageColorSpaceInfo { BaseName = derived, PaletteComponents = ComponentsForBase(derived) };
                }
            }
            if (first == "Indexed" && arr.Count >= 4)
            {
                var baseInfo = ResolveImageColorSpace(arr[1], reader);
                var lookupObj = reader.Resolve(arr[3]);
                var palette = ExtractPaletteBytes(lookupObj, reader);
                return new ImageColorSpaceInfo
                {
                    BaseName = baseInfo.BaseName,
                    Palette = palette,
                    PaletteComponents = baseInfo.PaletteComponents,
                };
            }
            // PDF 32000 §8.6.5.2-3: CalRGB / CalGray are device-space colours with
            // an attached calibration (Gamma/WhitePoint/Matrix). For raster image
            // decode they behave exactly like DeviceRGB / DeviceGray — without this
            // alias the blit dispatch below sees "CalRGB" / "CalGray" and silently
            // drops the image, producing a blank page for screenshot PDFs whose
            // single content op is "/Img0 Do" (e.g. 33306 family, 33307).
            if (first == "CalRGB")
                return new ImageColorSpaceInfo { BaseName = "DeviceRGB", PaletteComponents = 3 };
            if (first == "CalGray")
                return new ImageColorSpaceInfo { BaseName = "DeviceGray", PaletteComponents = 1 };
            // /Separation (1 colorant) and single-colorant /DeviceN images carry a tint
            // transform that turns the stored sample into colour in an alternate space
            // (PDF 32000 §8.6.6.4). Capture it so the spot sample is rendered as the
            // spot colour, not as a raw grayscale plate.
            if ((first == "Separation" && arr.Count >= 4)
                || (first == "DeviceN" && arr.Count >= 4 && reader.Resolve(arr[1]) is PdfArray dn && dn.Count == 1))
            {
                var tint = Functions.PdfFunction.Parse(arr[3], reader);
                var alt = ResolveAltSpaceFamily(arr[2], reader);
                if (tint is not null && alt is not null)
                    return new ImageColorSpaceInfo { BaseName = "Separation", PaletteComponents = 1, TintTransform = tint, AltSpaceName = alt };
            }
            if (first is not null)
                return new ImageColorSpaceInfo { BaseName = first, PaletteComponents = ComponentsForBase(first) };
        }
        return new ImageColorSpaceInfo { BaseName = "DeviceRGB", PaletteComponents = 3 };
    }

    /// <summary>
    /// Resolve the alternate-space family of a /Separation or /DeviceN colorspace's
    /// tint output to DeviceGray/DeviceRGB/DeviceCMYK (ICCBased maps by component
    /// count). Returns null when unrecognised.
    /// </summary>
    internal static string? ResolveAltSpaceFamily(PdfObject? obj, PdfReader reader)
    {
        var resolved = reader.Resolve(obj);
        if (resolved is PdfName n)
        {
            if (n.Value is "DeviceCMYK" or "DeviceRGB" or "DeviceGray") return n.Value;
            if (n.Value == "CalGray") return "DeviceGray";
            if (n.Value == "CalRGB") return "DeviceRGB";
            return null;
        }
        if (resolved is PdfArray a && a.Count > 0 && a[0] is PdfName fam)
        {
            if (fam.Value == "ICCBased" && a.Count > 1 && reader.ResolveStream(a[1]) is { } icc)
            {
                var iccN = (int)icc.Dict.GetInt("N");
                return iccN switch { 1 => "DeviceGray", 3 => "DeviceRGB", 4 => "DeviceCMYK", _ => null };
            }
            if (fam.Value == "CalGray") return "DeviceGray";
            if (fam.Value == "CalRGB") return "DeviceRGB";
            if (fam.Value == "Lab") return "Lab";
        }
        return null;
    }

    /// <summary>
    /// Build a 256-entry RGB lookup table for a single-component /Separation image:
    /// sample byte → tint (0..1) → alternate components → RGB. <paramref name="invert"/>
    /// applies a /Decode [1 0] reversal. Returns 256×3 packed RGB bytes.
    /// </summary>
    /// <summary>
    /// Expand a 1/2/4/8-bpc single-component /Separation (or /DeviceN) image to packed RGB
    /// using a 256-entry tint LUT. Sub-byte samples are scaled to the LUT's 0..255 index
    /// range (so a 1-bpc sample of 1 maps to LUT[255] = full tint).
    /// </summary>
    internal static byte[] SeparationSamplesToRgb(byte[] data, int w, int h, int bpc, byte[] lut256)
    {
        var rgb = new byte[w * h * 3];
        var rowBytes = (w * bpc + 7) / 8;
        var maxv = (1 << bpc) - 1;
        for (int y = 0; y < h; y++)
        {
            int rowBase = y * rowBytes;
            for (int x = 0; x < w; x++)
            {
                int bitPos = x * bpc;
                int bi = rowBase + (bitPos >> 3);
                int sample = bi < data.Length ? (data[bi] >> (8 - bpc - (bitPos & 7))) & maxv : 0;
                int idx = bpc == 8 ? sample : sample * 255 / maxv;
                int o = (y * w + x) * 3;
                rgb[o] = lut256[idx * 3]; rgb[o + 1] = lut256[idx * 3 + 1]; rgb[o + 2] = lut256[idx * 3 + 2];
            }
        }
        return rgb;
    }

    internal static byte[] BuildSeparationLut(ImageColorSpaceInfo cs, bool invert)
    {
        var lut = new byte[256 * 3];
        var input = new double[1];
        for (int i = 0; i < 256; i++)
        {
            input[0] = (invert ? 255 - i : i) / 255.0;
            byte r, g, b;
            var alt = cs.TintTransform!.Evaluate(input);
            if (alt is null) { r = g = b = (byte)(255 - i); }
            else ComponentsToRgb(alt, cs.AltSpaceName ?? "DeviceGray", out r, out g, out b);
            lut[i * 3] = r; lut[i * 3 + 1] = g; lut[i * 3 + 2] = b;
        }
        return lut;
    }

    /// <summary>
    /// Resolve a page's /Resources by walking up the /Parent chain. PDF 32000 §7.7.3.4
    /// lists /Resources as one of the inheritable page attributes — pages routinely
    /// omit their own /Resources when the parent /Pages dict carries the shared font /
    /// pattern / XObject table. The depth cap protects against malformed PDFs whose
    /// /Parent chain loops back on itself.
    /// </summary>
    internal static PdfDictionary? ResolveInheritedPageResources(PdfDictionary pageDict, PdfReader reader)
    {
        var dict = pageDict;
        for (var depth = 0; dict is not null && depth < 32; depth++)
        {
            var res = reader.ResolveDict(dict.Get("Resources"));
            if (res is not null) return res;
            dict = reader.ResolveDict(dict.Get("Parent"));
        }
        return null;
    }

    private static int ComponentsForBase(string baseName) => baseName switch
    {
        "DeviceGray" or "G" or "CalGray" => 1,
        "DeviceCMYK" or "CMYK" => 4,
        _ => 3,
    };

    private static byte[]? ExtractPaletteBytes(PdfObject? lookup, PdfReader reader)
    {
        // Lookup can be a literal PdfString or a referenced stream.
        if (lookup is PdfString str) return str.Value;
        if (lookup is PdfStream ps)
        {
            try { return reader.DecodeStream(ps); }
            catch { return null; }
        }
        return null;
    }

    /// <summary>
    /// Render an Indexed image with bilinear scaling. PDF 32000-1:2008 §8.9.5.3: pixel data is
    /// packed as bpc-bit indices (rows are byte-aligned), each index is looked up in the palette
    /// to get a baseCS tuple. GDI+
    /// uses bilinear by default, so we match that — nearest-neighbour drops colour accuracy at
    /// sub-pixel boundaries and then fails visual comparison even when content is correct.
    /// </summary>
    private static void BlitIndexed(RenderContext ctx, byte[] src, int srcW, int srcH,
        int dstX, int dstY, int dstW, int dstH, int bpc, ImageColorSpaceInfo csInfo)
    {
        if (csInfo.Palette is null || csInfo.PaletteComponents <= 0) return;

        // Decode once into an sRGB buffer; bilinear sampling needs random access to RGB triplets,
        // and paying the decode cost up-front beats redoing palette lookups 4× per dst pixel.
        var rgb = DecodeIndexedToRgb(src, srcW, srcH, bpc, csInfo);
        if (rgb is null) return;

        BlitRgbBilinear(ctx, rgb, srcW, srcH, dstX, dstY, dstW, dstH);
    }

    /// <summary>Unpack a palette-coded image into a flat RGB byte[]. Returns null if the lookup fails.</summary>
    private static byte[]? DecodeIndexedToRgb(byte[] src, int srcW, int srcH, int bpc,
        ImageColorSpaceInfo csInfo)
    {
        var palette = csInfo.Palette!;
        var comps = csInfo.PaletteComponents;
        var isCmyk = csInfo.BaseName == "DeviceCMYK";
        var isGray = comps == 1;
        var rowBytes = (srcW * bpc + 7) / 8;
        var paletteEntries = palette.Length / comps;
        var rgb = new byte[srcW * srcH * 3];

        for (var y = 0; y < srcH; y++)
        {
            var rowStart = y * rowBytes;
            var dstRow = y * srcW * 3;
            for (var x = 0; x < srcW; x++)
            {
                var idx = ReadPackedIndex(src, rowStart, x, bpc);
                if (idx < 0 || idx >= paletteEntries) continue;

                var pbase = idx * comps;
                if (pbase + comps > palette.Length) continue;

                byte r, g, b;
                if (isGray)
                {
                    r = g = b = palette[pbase];
                }
                else if (isCmyk && comps == 4)
                {
                    var c = palette[pbase];
                    var m = palette[pbase + 1];
                    var yy = palette[pbase + 2];
                    var k = palette[pbase + 3];
                    r = (byte)(255 * (255 - c) * (255 - k) / 65025);
                    g = (byte)(255 * (255 - m) * (255 - k) / 65025);
                    b = (byte)(255 * (255 - yy) * (255 - k) / 65025);
                }
                else
                {
                    r = palette[pbase];
                    g = palette[pbase + 1];
                    b = palette[pbase + 2];
                }
                var di = dstRow + x * 3;
                rgb[di] = r; rgb[di + 1] = g; rgb[di + 2] = b;
            }
        }
        return rgb;
    }

    /// <summary>Bilinear-sample a flat-RGB source image into the pixel buffer at dst rect.</summary>
    private static void BlitRgbBilinear(RenderContext ctx, byte[] src, int srcW, int srcH,
        int dstX, int dstY, int dstW, int dstH)
    {
        if (dstW <= 0 || dstH <= 0 || srcW <= 0 || srcH <= 0) return;

        // Sample at pixel centres (+0.5) so the edges of the dst rectangle stay inside the
        // source bounds after rounding. sx ∈ [0, srcW-1] lets us use (ix, ix+1) pairs safely.
        var xScale = (double)srcW / dstW;
        var yScale = (double)srcH / dstH;

        for (var y = 0; y < dstH; y++)
        {
            var dy = dstY + y;
            if (dy < 0 || dy >= ctx.PixelH) continue;

            var sy = (y + 0.5) * yScale - 0.5;
            if (sy < 0) sy = 0;
            if (sy > srcH - 1) sy = srcH - 1;
            var iy = (int)sy;
            var fy = sy - iy;
            var iy2 = Math.Min(iy + 1, srcH - 1);

            for (var x = 0; x < dstW; x++)
            {
                var dx = dstX + x;
                if (dx < 0 || dx >= ctx.PixelW) continue;

                var sx = (x + 0.5) * xScale - 0.5;
                if (sx < 0) sx = 0;
                if (sx > srcW - 1) sx = srcW - 1;
                var ix = (int)sx;
                var fx = sx - ix;
                var ix2 = Math.Min(ix + 1, srcW - 1);

                // Four corner samples — top-left, top-right, bottom-left, bottom-right.
                var p00 = (iy * srcW + ix) * 3;
                var p10 = (iy * srcW + ix2) * 3;
                var p01 = (iy2 * srcW + ix) * 3;
                var p11 = (iy2 * srcW + ix2) * 3;

                var w00 = (1 - fx) * (1 - fy);
                var w10 = fx * (1 - fy);
                var w01 = (1 - fx) * fy;
                var w11 = fx * fy;

                var r = (byte)(src[p00] * w00 + src[p10] * w10 + src[p01] * w01 + src[p11] * w11);
                var g = (byte)(src[p00 + 1] * w00 + src[p10 + 1] * w10 + src[p01 + 1] * w01 + src[p11 + 1] * w11);
                var b = (byte)(src[p00 + 2] * w00 + src[p10 + 2] * w10 + src[p01 + 2] * w01 + src[p11 + 2] * w11);
                SetPixel(ctx, dx, dy, r, g, b, 255);
            }
        }
    }

    /// <summary>Read the nth bpc-bit index from a byte-aligned row in <paramref name="src"/>.</summary>
    private static int ReadPackedIndex(byte[] src, int rowStart, int xIndex, int bpc)
    {
        // Fast paths for the common widths (8, 4, 1 bit) avoid bit-walking per pixel.
        if (bpc == 8)
        {
            var i = rowStart + xIndex;
            return i < src.Length ? src[i] : -1;
        }
        if (bpc == 4)
        {
            var i = rowStart + (xIndex >> 1);
            if (i >= src.Length) return -1;
            return (xIndex & 1) == 0 ? src[i] >> 4 : src[i] & 0x0F;
        }
        if (bpc == 1)
        {
            var i = rowStart + (xIndex >> 3);
            if (i >= src.Length) return -1;
            return (src[i] >> (7 - (xIndex & 7))) & 1;
        }
        if (bpc == 2)
        {
            var i = rowStart + (xIndex >> 2);
            if (i >= src.Length) return -1;
            var shift = 6 - ((xIndex & 3) << 1);
            return (src[i] >> shift) & 0x03;
        }
        return -1;
    }

    // ── Resource resolution ─────────────────────────────────────────

    internal static Dictionary<string, PdfDictionary> ResolveFontDicts(PdfDictionary? resources, PdfReader reader)
    {
        var result = new Dictionary<string, PdfDictionary>(StringComparer.Ordinal);
        if (resources is null) return result;
        var fontDict = reader.ResolveDict(resources.Get("Font"));
        if (fontDict is null) return result;
        foreach (var key in fontDict.Keys)
        {
            var fd = reader.ResolveDict(fontDict.Get(key));
            if (fd is not null) result[key] = fd;
        }
        return result;
    }

    internal static Dictionary<string, PdfDictionary>? ResolveExtGStates(PdfDictionary? resources, PdfReader reader)
    {
        if (resources is null) return null;
        var gsDict = reader.ResolveDict(resources.Get("ExtGState"));
        if (gsDict is null) return null;
        var result = new Dictionary<string, PdfDictionary>(StringComparer.Ordinal);
        foreach (var key in gsDict.Keys)
        {
            var d = reader.ResolveDict(gsDict.Get(key));
            if (d is not null) result[key] = d;
        }
        return result;
    }

    internal static Dictionary<string, PdfStream> ResolveAllXObjects(PdfDictionary? resources, PdfReader reader)
    {
        var result = new Dictionary<string, PdfStream>(StringComparer.Ordinal);
        if (resources is null) return result;
        var xobjectDict = reader.ResolveDict(resources.Get("XObject"));
        if (xobjectDict is null) return result;
        foreach (var key in xobjectDict.Keys)
        {
            var obj = reader.ResolveStream(xobjectDict.Get(key));
            if (obj is not null)
                result[key] = obj;
        }
        return result;
    }

    internal static byte[] GetPageContent(PdfDictionary pageDict, PdfReader reader)
    {
        var obj = reader.Resolve(pageDict.Get("Contents"));
        if (obj is PdfStream stream) return reader.DecodeStream(stream);
        if (obj is PdfArray arr)
        {
            using var ms = new MemoryStream();
            foreach (var item in arr)
            {
                var s = reader.ResolveStream(item);
                if (s is not null)
                {
                    var data = reader.DecodeStream(s);
                    ms.Write(data);
                    ms.WriteByte((byte)'\n');
                }
            }
            return ms.ToArray();
        }
        return [];
    }

    // ── Context ─────────────────────────────────────────────────────

    private sealed class RenderContext(byte[] pixels, int pixelW, int pixelH,
        double scale, Rectangle mediaBox, PdfReader reader)
    {
        public byte[] Pixels => pixels;
        public int PixelW => pixelW;
        public int PixelH => pixelH;
        public double Scale => scale;
        public Rectangle MediaBox => mediaBox;
        public PdfReader Reader => reader;
        public Dictionary<string, PdfStream>? AllXObjects { get; set; }
        public Dictionary<string, PdfDictionary>? FontDicts { get; set; }
        public Dictionary<string, (IGlyphOutlineSource? parser, double hScale)> FontParsers { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, CidFontInfo?> CidFontInfos { get; } = new(StringComparer.Ordinal);
        // Per-font byte→GID map built from the PDF /Encoding /Differences glyph names
        // (resolved through the embedded font's name table). null = no usable map.
        public Dictionary<string, int[]?> EncodingGidMaps { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Page's /Resources/Pattern dict — looked up when resolving a <c>scn</c> pattern name.
        /// Propagated into child contexts (Form XObjects, pattern tiles) so nested fills resolve
        /// correctly. Null when the page has no Pattern resources.
        /// </summary>
        public PdfDictionary? Patterns { get; set; }

        /// <summary>
        /// Page / Form-XObject / Pattern /Resources/Shading dict — looked up by the
        /// <c>sh</c> operator to paint a smooth-gradient fill inside the current clip.
        /// Null means this scope has no shading resources.
        /// </summary>
        public PdfDictionary? Shadings { get; set; }

        /// <summary>
        /// Page resources /ColorSpace dictionary — Separation / DeviceN array
        /// references named in `cs`/`CS` operators. Passed through to the
        /// content stream parser so tint transforms can convert named-spot
        /// colors into the renderer's RGB pipeline. Null when the page has
        /// no named colorspaces (the common case).
        /// </summary>
        public PdfDictionary? ColorSpaces { get; set; }

        /// <summary>
        /// Page resources /Properties dict — referenced by name in BDC's
        /// second operand (<c>/OC /MC0 BDC</c>). Stored on the context so
        /// child renders (Form XObjects, patterns) can pass it through to
        /// their nested content-stream parser as well.
        /// </summary>
        public PdfDictionary? Properties { get; set; }

        /// <summary>
        /// OCG dicts (resolved instances) that the current OC config marks as
        /// hidden — i.e. content inside their <c>/OC /Name BDC … EMC</c> ranges
        /// must NOT be drawn. Built once per page from /OCProperties/D.
        /// Reference equality is fine because PdfReader caches resolved dicts.
        /// </summary>
        public HashSet<PdfDictionary>? OcgHidden { get; set; }

        /// <summary>
        /// Stack of "did this marked-content frame hide its content?". Pushed
        /// on every BMC/BDC, popped on every EMC. <see cref="IsContentHidden"/>
        /// reports true while any frame in the stack is true.
        /// </summary>
        public Stack<bool> OcgHiddenStack { get; } = new();

        /// <summary>True when the current draw operation lies inside a marked-content
        /// range belonging to an OCG flagged invisible by the OC config.</summary>
        public bool IsContentHidden
        {
            get
            {
                foreach (var hidden in OcgHiddenStack)
                    if (hidden) return true;
                return false;
            }
        }

        /// <summary>
        /// Optional 1-byte-per-pixel stencil: non-zero pixels are paintable, zero pixels are
        /// masked out. Used during tiling-pattern fill so the pattern only paints inside the
        /// current path. Null means no clipping (the normal unmasked case).
        /// </summary>
        public byte[]? ClipMask { get; set; }

        /// <summary>
        /// Active PDF 32000 §11.3.5 blend mode for the next pixel write — set by callers
        /// (DrawText / DrawPath / DrawImage / etc.) from <c>state.BlendMode</c> before each
        /// blit, read by SetPixel. "Normal" means straight Porter-Duff source-over alpha;
        /// "Multiply" applies the multiplicative blend separable formula. Other modes fall
        /// back to Normal until they're implemented.
        /// </summary>
        public string CurrentBlendMode { get; set; } = "Normal";

        /// <summary>
        /// True while this context is the scratch buffer of a knockout transparency group
        /// (PDF 32000 §11.4.4 / §11.6.6, /Group dict with /K true). Each new draw inside a
        /// knockout group composites against the group's ORIGINAL backdrop (transparent for
        /// /S /Transparency groups), not against accumulated prior draws — so overlapping
        /// elements show only the topmost. We implement that by switching pixel writes to
        /// "replace with src·alpha" instead of source-over, and by skipping blend-mode
        /// dispatch (a non-Normal blend against a transparent backdrop reduces to src*α
        /// per the spec's compositing equation). Strokes don't currently honour this flag —
        /// rare in practice, parallel to the existing stroke/blend-mode gap.
        /// </summary>
        public bool IsKnockoutGroup { get; set; }

        /// <summary>
        /// Per-pixel soft-mask alpha (PDF 32000 §11.6.5.4) installed by paint sites
        /// from the active <c>state.SoftMask</c>. One byte per pixel (page-sized);
        /// each fragment's effective alpha is multiplied by <c>SoftMaskAlpha[idx]</c>
        /// before blending. Null means no soft mask. Resolved lazily and cached per
        /// page in <see cref="SoftMaskCache"/>.
        /// </summary>
        public byte[]? SoftMaskAlpha { get; set; }

        /// <summary>
        /// Per-page cache keyed by the soft-mask group dict's object number, mapping
        /// to the rendered alpha buffer. A single PDF often references one SMask in
        /// dozens of paint operations; resolving (rendering the group) on each is
        /// prohibitive. Page-scoped because the same SMask dict can be referenced
        /// from multiple paint sites without re-rendering.
        /// </summary>
        public Dictionary<int, byte[]> SoftMaskCache { get; } = new();
    }
}

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
    private static void DrawFormXObject(RenderContext ctx, PdfStream formStream, GraphicsState state,
        Dictionary<string, PdfDictionary>? extGStates)
    {
        // A form hidden by the default optional-content configuration renders as
        // if absent (e.g. a print-only /Background layer wrapping the page scan).
        if (IsOcHidden(formStream.Dict.Get("OC"), ctx.Reader, ctx.OcgHidden)) return;
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
    // One page-sized scratch per thread, reused across mask renders: a document
    // styled with per-element soft masks (hundreds of distinct /SMask gs entries)
    // otherwise allocates a fresh full-page buffer for every mask it renders.
    // The busy flag covers re-entrancy — a mask group whose own content carries
    // a nested /SMask recurses into this method mid-render and must not clobber
    // the outer render's scratch.
    [ThreadStatic] private static byte[]? _softMaskScratch;

    [ThreadStatic] private static bool _softMaskScratchBusy;

    internal static byte[]? RenderSoftMaskAlpha(PdfReader reader, int pixelW, int pixelH,
        double scale, Rectangle mediaBox, SoftMaskInfo smInfo)
    {
        var groupStream = reader.ResolveStream(smInfo.Dict.Get("G"));
        if (groupStream is null || pixelW <= 0 || pixelH <= 0) return null;

        var scratchLen = pixelW * pixelH * 4;
        byte[] scratchPixels;
        var pooled = !_softMaskScratchBusy;
        if (pooled)
        {
            scratchPixels = _softMaskScratch is { } s && s.Length == scratchLen ? s : new byte[scratchLen];
            if (ReferenceEquals(scratchPixels, _softMaskScratch)) Array.Clear(scratchPixels);
            _softMaskScratch = scratchPixels;
            _softMaskScratchBusy = true;
        }
        else
            scratchPixels = new byte[scratchLen];
        try
        {
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
        finally
        {
            if (pooled) _softMaskScratchBusy = false;
        }
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
    // real-world PDFs with icon/logo gradients.

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
        // first/last rows inside their BBoxes, the behaviour mainstream
        // viewers exhibit.
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
            if (Environment.GetEnvironmentVariable("Q_SHLUT") == "0")
            {
                CmykToRgbClamp(components[0], components[1], components[2], components[3],
                    out rd, out gd, out bd);
                r = ToByteClamp(rd); g = ToByteClamp(gd); b = ToByteClamp(bd);
                return;
            }
            // Same ICC-style conversion the content-stream `k`/`K` operators use
            // (CmykToRgbLut): a gradient authored in the same ink as an adjacent flat
            // fill must land on the same RGB, and the naïve subtractive formula
            // lands far off that mark (oversaturated, zero blue for Y=1 inks).
            var (rb, gb, bb) = Aspose.Pdf.Devices.CmykToRgbLut.Convert(
                components[0], components[1], components[2], components[3]);
            r = rb; g = gb; b = bb;
            return;
        }
        else if (csName == "Lab" && components.Length >= 3)
        {
            LabColor.ToRgb(components[0], components[1], components[2], out rd, out gd, out bd);
        }
        else if (csName == "LabEnc" && components.Length >= 3)
        {
            // Lab-encoded scanner-class ICC channels (L/100, (a+128)/255,
            // (b+128)/255 — see ContentStreamParser.IsLabEncodedIcc).
            static double C(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
            LabColor.ToRgb(C(components[0]) * 100.0, C(components[1]) * 255.0 - 128.0,
                C(components[2]) * 255.0 - 128.0, out rd, out gd, out bd);
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
    // outside. That's what `FillWithPattern` does — it covers real-world files that
    // paint an image through a circular clip (a PatternType-1 whose
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
        // steps (§8.7.3.3) — a real-world PDF may place pattern (0,0) outside the
        // clipped region and rely on tile (0,-1) or similar to cover it.
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
}

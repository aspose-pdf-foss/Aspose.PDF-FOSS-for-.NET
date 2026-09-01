using System.Runtime.InteropServices;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Devices.Rasterizer;
using Aspose.Pdf.IO;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Devices;

public sealed partial class SoftwarePageRenderer
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
            // ⚠ The knockout shortcut below (and in SetPixel) rests on the group's INITIAL
            // BACKDROP being transparent, so that "there is nothing for the blend mode to act
            // on". PDF 32000 §11.4.5.2: that is only true of an ISOLATED group. A group with
            // /I false inherits the parent's backdrop as its initial one, so its members must
            // still BLEND against what is underneath — treating them as knockout dropped the
            // blend entirely and a Multiply/Screen/Overlay swatch sheet rendered flat opaque.
            var isKnockout = groupDict!.Get("K") is PdfBoolean kn && kn.Value
                             && groupDict.Get("I") is PdfBoolean iso && iso.Value;

            // PDF 32000 §11.4.5.2: an ISOLATED group starts on a transparent backdrop; a
            // group with /I false inherits the PARENT's content as its initial backdrop, so
            // a blend mode inside it composes against what is already on the page. Seeding
            // the scratch with the parent's pixels is what makes that true - a bare `sh`
            // vignette multiplied over a photo had nothing to multiply against and painted
            // an opaque gradient straight over it instead.
            // §11.4.6 removes that backdrop again before the group is composited, which is
            // a no-op over an OPAQUE backdrop composited Normally at full alpha: there the
            // group's result IS the scratch. Only that case is seeded, so the partial-alpha
            // and blended composites keep the behaviour they were measured with.
            var seedBackdrop = !isKnockout
                               && groupDict!.Get("I") is not PdfBoolean { Value: true }
                               && state.BlendMode == "Normal" && state.FillAlpha >= 1.0
                               && state.SoftMask is null;

            // Allocate a scratch RGBA buffer same size as the parent, RGBA=(0,0,0,0).
            var scratch = new byte[ctx.Pixels.Length];
            if (seedBackdrop) Array.Copy(ctx.Pixels, scratch, ctx.Pixels.Length);
            var scratchCtx = new RenderContext(scratch, ctx.PixelW, ctx.PixelH, ctx.Scale, ctx.MediaBox, ctx.Reader)
            {
                AllXObjects = formXObjects,
                FontDicts = formFontDicts,
                ConvertFontsToUnicodeTtf = ctx.ConvertFontsToUnicodeTtf,
            PdfXOverprintSim = ctx.PdfXOverprintSim,
            PageCtm = ctx.PageCtm,
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

            // Composite scratch back into parent at this Do call's blend mode + alpha,
            // through the soft mask that was active at the Do.
            CompositeGroupBuffer(ctx, scratch, state.BlendMode, state.FillAlpha,
                state.SoftMask is { } gsm ? ResolveSoftMaskAlpha(ctx, gsm) : null);
        }
        else
        {
            var childCtx = new RenderContext(ctx.Pixels, ctx.PixelW, ctx.PixelH, ctx.Scale, ctx.MediaBox, ctx.Reader)
            {
                AllXObjects = formXObjects,
                FontDicts = formFontDicts,
                ConvertFontsToUnicodeTtf = ctx.ConvertFontsToUnicodeTtf,
            PdfXOverprintSim = ctx.PdfXOverprintSim,
            PageCtm = ctx.PageCtm,
                // Pattern resources and the active clip mask inherit so that a pattern fill
                // inside a Form XObject or an image Do inside a pattern tile stays bounded.
                Patterns = ctx.Reader.ResolveDict(formResources?.Get("Pattern")) ?? ctx.Patterns,
                Shadings = ctx.Reader.ResolveDict(formResources?.Get("Shading")) ?? ctx.Shadings,
                ColorSpaces = ctx.Reader.ResolveDict(formResources?.Get("ColorSpace")) ?? ctx.ColorSpaces,
                ClipMask = formClipMask,
                // A form that is NOT itself a transparency group draws straight into the
                // parent’s pixels, so if the parent is a knockout group its members are still
                // knocking each other out and the flag has to travel with the context.
                IsKnockoutGroup = ctx.IsKnockoutGroup,
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
    /// fill-alpha and blends with the parent at that effective alpha — unless the PARENT
    /// is a knockout group, in which case §11.4.5 applies instead and the element replaces
    /// what is under it.
    /// </summary>
    private static void CompositeGroupBuffer(RenderContext ctx, byte[] scratch, string blendMode, double groupAlpha,
        byte[]? groupSoftMask = null)
    {
        var ga = (int)Math.Round(Math.Clamp(groupAlpha, 0.0, 1.0) * 255);
        var mode = BlendModes.Parse(blendMode);
        var dst = ctx.Pixels;
        // PDF 32000 §11.4.5: inside a knockout group every element composites with the
        // group’s INITIAL backdrop rather than with the elements before it, so the last one
        // to cover a pixel is the one that shows. SetPixel already did this for direct
        // draws, but a member that is ITSELF a group arrives here already flattened and was
        // blended in like any other - four circles that should have overlapped opaquely came
        // out as multiplied mud. The initial backdrop is transparent, so there is nothing
        // for the blend mode to act on and the colour passes through unchanged.
        var knockout = ctx.IsKnockoutGroup;
        for (var i = 0; i < dst.Length; i += 4)
        {
            var sa = scratch[i + 3];
            if (sa == 0) continue;
            // The /SMask that was active at the `Do` masks the GROUP as a whole (PDF 32000
            // §11.6.6): the group renders to its own buffer and that buffer composites
            // through the mask. Ignoring it here let a whole group land at full strength —
            // a soft drop-shadow group painted its dark rectangle straight across the
            // artwork it was supposed to sit behind. Direct draws already honour the mask
            // via SetPixel; this loop writes dst itself, so it has to apply it too.
            if (groupSoftMask is not null)
            {
                var gm = groupSoftMask[i >> 2];
                if (gm == 0) continue;
                sa = (byte)((sa * gm) / 255);
                if (sa == 0) continue;
            }
            int sr = scratch[i], sg = scratch[i + 1], sb = scratch[i + 2];
            int dr = dst[i], dg = dst[i + 1], db = dst[i + 2];

            // The destination may itself be transparent - a group nested inside another
            // group composites into a scratch buffer that starts empty, and an empty pixel
            // still carries RGB, namely zero. Weighting the blend by the backdrop’s alpha is
            // what PDF 32000 §11.3.6 asks for and is what stops a Multiply group from coming
            // out solid black over nothing: a page of coloured circles did exactly that.
            var effAko = (sa * ga) / 255;
            if (knockout)
            {
                if (effAko <= 0) continue;
                dst[i] = (byte)sr;
                dst[i + 1] = (byte)sg;
                dst[i + 2] = (byte)sb;
                dst[i + 3] = (byte)effAko;
                continue;
            }

            var da = dst[i + 3] / 255.0;
            if (mode != Rasterizer.BlendMode.Normal)
            {
                BlendModes.Blend(mode, dr, dg, db, sr, sg, sb, out var br, out var bg, out var bb);
                sr = (int)(sr + (br - sr) * da);
                sg = (int)(sg + (bg - sg) * da);
                sb = (int)(sb + (bb - sb) * da);
            }

            var effA = (sa * ga) / 255;
            if (effA <= 0) continue;
            var srcA = effA / 255.0;
            var keep = da * (1.0 - srcA);
            var outA = srcA + keep;
            if (outA <= 0) continue;
            dst[i]     = (byte)((sr * srcA + dr * keep) / outA);
            dst[i + 1] = (byte)((sg * srcA + dg * keep) / outA);
            dst[i + 2] = (byte)((sb * srcA + db * keep) / outA);
            dst[i + 3] = (byte)Math.Round(outA * 255);
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
                // /BC is routinely written as an INDIRECT reference; reading it without
                // resolving silently dropped every backdrop colour and left the mask
                // group flooded with black - fully transparent - outside its own content.
                var bc = reader.Resolve(smInfo.Dict.Get("BC")) as PdfArray;
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
}

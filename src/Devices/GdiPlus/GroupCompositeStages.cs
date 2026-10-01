using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;
using GdiColor = System.Drawing.Color;
using GdiMatrix = System.Drawing.Drawing2D.Matrix;
using GraphicsState = Aspose.Pdf.Content.GraphicsState;
using GdiState = System.Drawing.Drawing2D.GraphicsState;

namespace Aspose.Pdf.Devices;

public sealed partial class GdiPlusPageRenderer
{
    /// <summary>Composites the rendered layer back: a backdrop-copy group with an outer blend re-applies it (raw as-is under coverage, aliased under the binary mask, or after backdrop removal), a knockout element blends against the frozen initial backdrop, any other layer composites with its Do-time mode; the pooled buffers are returned.</summary>
    private void CompositeGroupResult(GroupCompositeState gc)
    {
        gc.compositeBlend = gc.useTransparentLayer && !gc.parentKnockout ? gc.state.BlendMode : "Normal";
        if (gc.backdropCopyOuterBlend)
        {
            if (gc.rawOuterBlend)
            {
                // KD4 rule as measured: blend the layer result AS-IS against the backdrop,
                // weighted by the group's own coverage — no backdrop removal.
                var covF = RentCovFloat(gc.w * gc.h);
                for (int i = 0; i < gc.coverage!.Length; i++) covF[i] = gc.coverage[i] / 255f;
                CompositeGroupLayer(gc.groupBmp, gc.state, gc.state.BlendMode, gc.compRect, covF);
                _covFloatPool.Push(covF);
            }
            else if (gc.aliasCov is not null)
                // Raw-layer blend under the aliased mask: Cs = the layer as painted
                // (backdrop mix included), applied at full strength wherever masked.
                CompositeGroupLayer(gc.groupBmp, gc.state, gc.state.BlendMode, gc.compRect, gc.aliasCov);
            else
            {
                // Remove the backdrop from the backdrop-copy result to recover the group's own
                // colour, tag each pixel with its true coverage, then composite with the outer
                // Do-blend. Uncovered pixels (coverage 0) are naturally skipped; edge pixels are
                // weighted by fractional coverage. Stamped nested-footprint pixels are left
                // as-is by the removal and take replace-semantics in the composite.
                RemoveBackdrop(gc.groupBmp, gc.savedBmp, gc.coverage!, gc.compRect, gc.stampMask);
                CompositeGroupLayer(gc.groupBmp, gc.state, gc.state.BlendMode, gc.compRect, null, gc.stampMask);
            }
        }
        else if (gc.parentKnockout && gc.savedKoBackdrop is not null)
            // Element of a knockout group: blend with MY OWN mode against the group's
            // frozen initial backdrop. Sibling pixels underneath are REPLACED when the
            // group is non-isolated or this element carries partial constant alpha
            // (stacking would double-apply it); an opaque element in an isolated
            // knockout composites over them, keeping sibling AA at shared edges.
            CompositeGroupLayer(gc.groupBmp, gc.state, gc.state.BlendMode, gc.compRect,
                koBackdrop: gc.savedKoBackdrop, koReplace: gc.savedKoReplace || gc.state.FillAlpha < 0.999);
        else
            CompositeGroupLayer(gc.groupBmp, gc.state, gc.compositeBlend, gc.compRect);
        _layerPool.Push(gc.groupBmp); // return to pool for reuse instead of disposing
        if (gc.aliasCov is not null) _covFloatPool.Push(gc.aliasCov);
        if (gc.coverage is not null) _covBytePool.Push(gc.coverage);
        if (gc.stampMask is not null) _covBytePool.Push(gc.stampMask);
    }

    /// <summary>Restores the device after the group's interior rendered: graphics, bitmap, blend scratch, knockout state and the pooled knockout backdrop.</summary>
    private void RestoreGroupDevice(GroupCompositeState gc)
    {
        _g = gc.savedG;
        _bitmap = gc.savedBmp;
        _blendScratch?.Dispose();
        _blendScratch = gc.savedScratch;
        _knockoutGroup = gc.parentKnockout;
        if (_koBackdrop is not null) _layerPool.Push(_koBackdrop);
        _koBackdrop = gc.savedKoBackdrop;
        _koReplace = gc.savedKoReplace;
        gc.gg.Dispose();
        gc.deviceClip?.Dispose();
    }

    /// <summary>Renders the group's content onto its layer: the BBox sub-rect initialised transparent or as a backdrop copy, the device settings mirrored, a knockout group's initial backdrop frozen, then the content stream drawn with the layer as the device.</summary>
    private void RenderGroupInterior(GroupCompositeState gc)
    {
        // Re-initialise only the BBox sub-rect (the pooled bitmap may carry stale
        // content elsewhere, which is never composited). SourceCopy writes the
        // exact pixels, overwriting any leftovers.
        gc.gg.CompositingMode = CompositingMode.SourceCopy;
        if (gc.useTransparentLayer)
            // Transparent backdrop under the BBox — capture the group's own contribution
            // alone, then composite it with the Do-time blend mode.
            using (var clear = new SolidBrush(GdiColor.Transparent))
                gc.gg.FillRectangle(clear, gc.compRect);
        else
            // Non-isolated composite → start from a copy of the current backdrop under the
            // BBox so internal blend modes see it. Copy the raw pixels (not DrawImage, which
            // round-trips through premultiplied alpha and perturbs the values).
            CopyRegion(gc.savedBmp, gc.groupBmp, gc.compRect);
        gc.gg.CompositingMode = CompositingMode.SourceOver;
        gc.gg.SmoothingMode = gc.savedG.SmoothingMode;
        gc.gg.PixelOffsetMode = gc.savedG.PixelOffsetMode;
        gc.gg.InterpolationMode = gc.savedG.InterpolationMode;
        gc.gg.TextRenderingHint = gc.savedG.TextRenderingHint;
        gc.gg.CompositingQuality = gc.savedG.CompositingQuality;
        if (gc.deviceClip is not null) gc.gg.Clip = gc.deviceClip;

        // Knockout: freeze this group's INITIAL backdrop (the just-initialised layer
        // content — transparent, or the backdrop copy). Child-group elements blend
        // against this snapshot with their own blend mode and REPLACE whatever
        // earlier siblings painted (PDF 32000 §11.4.5).
        if (gc.isKnockout && !_inCoveragePass)
        {
            _koBackdrop = RentLayer(gc.w, gc.h);
            CopyRegion(gc.groupBmp, _koBackdrop, gc.compRect);
            _koReplace = KnockoutOverride switch
            {
                "replace" => true,
                "over" => false,
                _ => !gc.isolated,
            };
        }
        else
            _koBackdrop = null;

        _g = gc.gg;
        _bitmap = gc.groupBmp;
        _blendScratch = null; // a fresh same-size scratch is allocated on demand for blended fills inside the group
        _knockoutGroup = gc.isKnockout; // my own elements composite against my initial backdrop
        RenderContentStream(gc.content, gc.effectiveCtm, gc.bboxClip);
        _g.Flush();
    }

    /// <summary>The supersampled outer-blend path: a backdrop-copy group with an outer non-Normal blend renders its coverage and colour at K times resolution and composites under true geometric coverage. True when it handled the group.</summary>
    private bool CompositeSupersampledOuterBlend(GroupCompositeState gc)
    {
        // A backdrop-copy group carrying an outer non-Normal Do-blend also needs the group's
        // true per-pixel COVERAGE (so the outer blend is weighted at anti-aliased edges, not
        // applied at full strength). Capture it with a throwaway transparent pre-pass; its
        // alpha channel is the coverage. (The pass renders the same content in isolation, so
        // its colours are discarded — only alpha is used.)
        // Supersampled outer-blend path (Q_SSCOV): render BOTH the group's coverage and its
        // backdrop-copy colour at K× resolution and box-downsample, so the outer blend is
        // weighted by true geometric coverage (finer than 8-bit, with fractional stroke-edge
        // tails) and the colour layer shares the exact same footprint. Using a supersampled
        // mask against the ordinary 1×-rasterized layer is NOT an option — hairline strokes
        // land on different pixels at 1× than their true geometry, and every disagreement
        // re-applies the blend to an unpainted pixel.
        if (gc.backdropCopyOuterBlend && SsCoverage > 1 && !_inCoveragePass)
        {
            try
            {
                float[] covW = CaptureGroupCoverageSS(gc.content, gc.effectiveCtm, gc.bboxClip, gc.deviceClip, gc.compRect, gc.isKnockout, SsCoverage);
                var ssLayer = RenderGroupBackdropCopySS(gc.content, gc.effectiveCtm, gc.bboxClip, gc.deviceClip, gc.compRect, gc.isKnockout, SsCoverage);
                RemoveBackdropF(ssLayer, _bitmap, covW, gc.compRect);
                CompositeGroupLayer(ssLayer, gc.state, gc.state.BlendMode, gc.compRect, covW);
                _layerPool.Push(ssLayer);
                _covFloatPool.Push(covW);
            }
            finally { gc.deviceClip?.Dispose(); }
            return true;
        }
        return false;
    }

    /// <summary>Resolves the compositing mode (transparent layer or backdrop copy, outer blend) and the device-space BBox and inherited clip; false when the group maps off-page.</summary>
    private bool PrepareGroupComposite(GroupCompositeState gc)
    {
        gc.parentKnockout = _knockoutGroup;

        gc.doBlendNonNormal = !string.IsNullOrEmpty(gc.state.BlendMode) && gc.state.BlendMode != "Normal";
        gc.rawOuterBlend = ObMode == "raw";
        gc.backdropCopy = !gc.isolated && (gc.hasInternalBlend || (gc.rawOuterBlend && gc.doBlendNonNormal)) && !gc.parentKnockout;
        gc.useTransparentLayer = !gc.backdropCopy;
        gc.backdropCopyOuterBlend = gc.backdropCopy && gc.doBlendNonNormal;

        gc.compRect = GroupDeviceBounds(gc.bboxClip, gc.w, gc.h);
        if (gc.compRect.Width <= 0 || gc.compRect.Height <= 0) return false; // group maps off-page

        gc.savedT = _g.Transform;
        _g.ResetTransform();
        gc.deviceClip = _g.Clip;
        _g.Transform = gc.savedT;
        gc.savedT.Dispose();
        return true;
    }
}

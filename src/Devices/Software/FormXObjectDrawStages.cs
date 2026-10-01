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
    /// <summary>The stages of a form XObject draw: the transparency-group path through a scratch buffer, and the direct path.</summary>
    private static void DrawFormXObjectDirect(FormXObjectDrawState fo)
    {
        var childCtx = new RenderContext(fo.ctx.Pixels, fo.ctx.PixelW, fo.ctx.PixelH, fo.ctx.Scale, fo.ctx.MediaBox, fo.ctx.Reader)
        {
            AllXObjects = fo.formXObjects,
            FontDicts = fo.formFontDicts,
            ConvertFontsToUnicodeTtf = fo.ctx.ConvertFontsToUnicodeTtf,
        PdfXOverprintSim = fo.ctx.PdfXOverprintSim,
        PageCtm = fo.ctx.PageCtm,
            // Pattern resources and the active clip mask inherit so that a pattern fill
            // inside a Form XObject or an image Do inside a pattern tile stays bounded.
            Patterns = fo.ctx.Reader.ResolveDict(fo.formResources?.Get("Pattern")) ?? fo.ctx.Patterns,
            Shadings = fo.ctx.Reader.ResolveDict(fo.formResources?.Get("Shading")) ?? fo.ctx.Shadings,
            ColorSpaces = fo.ctx.Reader.ResolveDict(fo.formResources?.Get("ColorSpace")) ?? fo.ctx.ColorSpaces,
            ClipMask = fo.formClipMask,
            // A form that is NOT itself a transparency group draws straight into the
            // parent’s pixels, so if the parent is a knockout group its members are still
            // knocking each other out and the flag has to travel with the context.
            IsKnockoutGroup = fo.ctx.IsKnockoutGroup,
        };

        RenderContent(fo.formContent, childCtx, fo.formExtGStates, fo.effectiveCtm, fo.formClipMask);
    }

    /// <summary></summary>
    private static void DrawTransparencyGroupForm(FormXObjectDrawState fo)
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
        var isKnockout = fo.groupDict!.Get("K") is PdfBoolean kn && kn.Value
                         && fo.groupDict.Get("I") is PdfBoolean iso && iso.Value;

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
                           && fo.groupDict!.Get("I") is not PdfBoolean { Value: true }
                           && fo.state.BlendMode == "Normal" && fo.state.FillAlpha >= 1.0
                           && fo.state.SoftMask is null;

        // Allocate a scratch RGBA buffer same size as the parent, RGBA=(0,0,0,0).
        var scratch = new byte[fo.ctx.Pixels.Length];
        if (seedBackdrop) Array.Copy(fo.ctx.Pixels, scratch, fo.ctx.Pixels.Length);
        var scratchCtx = new RenderContext(scratch, fo.ctx.PixelW, fo.ctx.PixelH, fo.ctx.Scale, fo.ctx.MediaBox, fo.ctx.Reader)
        {
            AllXObjects = fo.formXObjects,
            FontDicts = fo.formFontDicts,
            ConvertFontsToUnicodeTtf = fo.ctx.ConvertFontsToUnicodeTtf,
        PdfXOverprintSim = fo.ctx.PdfXOverprintSim,
        PageCtm = fo.ctx.PageCtm,
            Patterns = fo.ctx.Reader.ResolveDict(fo.formResources?.Get("Pattern")) ?? fo.ctx.Patterns,
            Shadings = fo.ctx.Reader.ResolveDict(fo.formResources?.Get("Shading")) ?? fo.ctx.Shadings,
            ColorSpaces = fo.ctx.Reader.ResolveDict(fo.formResources?.Get("ColorSpace")) ?? fo.ctx.ColorSpaces,
            ClipMask = fo.formClipMask,
            CurrentBlendMode = "Normal",
            IsKnockoutGroup = isKnockout,
        };
        RenderContent(fo.formContent, scratchCtx, fo.formExtGStates, fo.effectiveCtm, fo.formClipMask);

        // PDF 32000 §11.6.6: when /CS is a 1-component (gray) space, the group's
        // contents are blended in grayscale and any final composite collapses to
        // luminance. We render in RGB and then post-convert to gray rather than
        // running a CS-aware rendering pipeline — strictly equivalent for Normal
        // blend mode, an approximation for the separable formulas (RGB-then-Y vs
        // Y-then-blend differ only on non-grey sources). /DeviceCMYK groups would
        // need a full CMYK pipeline and stay rendered in RGB for now.
        ConvertScratchForGroupCS(scratch, fo.groupDict, fo.ctx.Reader);

        // Composite scratch back into parent at this Do call's blend mode + alpha,
        // through the soft mask that was active at the Do.
        CompositeGroupBuffer(fo.ctx, scratch, fo.state.BlendMode, fo.state.FillAlpha,
            fo.state.SoftMask is { } gsm ? ResolveSoftMaskAlpha(fo.ctx, gsm) : null);
    }
}

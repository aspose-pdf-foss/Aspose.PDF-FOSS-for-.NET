using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void SaveStateOp(ContentRenderState ct)
    {
        ct.clipStack.Push((ct.clipD, ct.clipId));
        ct.ctmStack.Push(ct.ctm.Clone());
        // Fill/stroke color are graphics state (PDF 32000 §8.4.2):
        // a color set inside q…Q must not leak to later text.
        ct.colorStack.Push((ct.r, ct.g, ct.b,
            ct.pathState.FillR, ct.pathState.FillG, ct.pathState.FillB,
            ct.pathState.StrokeR, ct.pathState.StrokeG, ct.pathState.StrokeB));
        // The text state - font and size, spacing, leading, rise, rendering mode - is part
        // of the graphics state too (PDF 32000 §9.3.1): a Tf or Tc set inside q…Q (and never
        // reset by the generator) must not leak into the blocks that follow the Q, which draw
        // in the font set before the q.
        ct.textStateStack.Push(SavedTextState.Of(ct));
        ct.blendStack.Push(ct.blendMode);
        ct.alphaStack.Push(ct.fillAlpha);
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void RestoreStateOp(ContentRenderState ct)
    {
        if (ct.clipStack.Count > 0) (ct.clipD, ct.clipId) = ct.clipStack.Pop();
        if (ct.ctmStack.Count > 0)
            ct.ctm = ct.ctmStack.Pop();
        if (ct.colorStack.Count > 0)
        {
            var c9 = ct.colorStack.Pop();
            ct.r = c9.r; ct.g = c9.g; ct.b = c9.b;
            ct.pathState.FillR = c9.fr; ct.pathState.FillG = c9.fg; ct.pathState.FillB = c9.fb;
            ct.pathState.StrokeR = c9.sr; ct.pathState.StrokeG = c9.sg; ct.pathState.StrokeB = c9.sb;
        }
        if (ct.textStateStack.Count > 0) ct.textStateStack.Pop().RestoreTo(ct);
        if (ct.blendStack.Count > 0) ct.blendMode = ct.blendStack.Pop();
        if (ct.alphaStack.Count > 0) ct.fillAlpha = ct.alphaStack.Pop();
        // Close scope-bound effect groups in LIFO order (the group
        // opened at the deeper clip depth closes first, keeping the
        // XML nesting valid when both are open).
        for (var closeRound = 0; closeRound < 2; closeRound++)
        {
            var mDepth = ct.maskGroupOpen ? ct.maskOpenClipDepth : int.MinValue;
            var oDepth = ct.opacityGroupOpen ? ct.opacityOpenClipDepth : int.MinValue;
            if (ct.maskGroupOpen && ct.clipStack.Count < ct.maskOpenClipDepth && mDepth >= oDepth)
            {
                ct.svgPaths.Append("</g>");
                ct.maskGroupOpen = false;
            }
            else if (ct.opacityGroupOpen && ct.clipStack.Count < ct.opacityOpenClipDepth)
            {
                ct.svgPaths.Append("</g>");
                ct.opacityGroupOpen = false;
                ct.opacityGroupValue = 1.0;
            }
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void ApplyGraphicsStateOp(ContentRenderState ct)
    {
        if (ct.resources is null || ct.operands.Count < 1 || ct.operands[0] is not PdfName gsOpName) return;
        var egsDict = ct.reader.ResolveDict(
            ct.reader.ResolveDict(ct.resources.Get("ExtGState"))?.Get(gsOpName.Value));
        // The blend mode decides whether a fill paints over what lies beneath it (the
        // text-layer model's cover test) and rides the painted elements into the SVG;
        // a state without /BM leaves it as it was.
        if (ct.reader.Resolve(egsDict?.Get("BM")) is { } blend)
            ct.blendMode = BlendModeName(blend);
        // The fill alpha paints text as it paints fills: the text layer carries it in its colour.
        if (ct.reader.Resolve(egsDict?.Get("ca")) is PdfReal or PdfInteger)
            ct.fillAlpha = Compat.Clamp(Num(ct.reader.Resolve(egsDict!.Get("ca"))!), 0.0, 1.0);
        if (!ct.textOnly && ct.imageSink is not null)
        {
            var smaskDict = egsDict is not null ? ct.reader.ResolveDict(egsDict.Get("SMask")) : null;
            // Any gs that carries an /SMask entry replaces the
            // soft mask — /None (a name, not a dict) clears it.
            if (egsDict is not null && ct.maskGroupOpen && egsDict.Get("SMask") is not null && smaskDict is null)
            {
                ct.svgPaths.Append("</g>");
                ct.maskGroupOpen = false;
            }
            // Constant fill alpha (/ca < 1) becomes a <g opacity>
            // group around the painted content (the 0.6-alpha
            // photo is written inside one).
            var caVal = egsDict?.Get("ca") switch
            {
                PdfReal cr => cr.Value,
                PdfInteger ci => (double)ci.Value,
                _ => (double?)null,
            };
            if (caVal is { } ca2)
            {
                if (ct.opacityGroupOpen && Math.Abs(ca2 - ct.opacityGroupValue) > 1e-9)
                {
                    ct.svgPaths.Append("</g>");
                    ct.opacityGroupOpen = false;
                    ct.opacityGroupValue = 1.0;
                }
                if (!ct.opacityGroupOpen && ca2 < 1.0 - 1e-9)
                {
                    ct.svgPaths.Append($"<g opacity=\"{F(ca2)}\">");
                    ct.opacityGroupOpen = true;
                    ct.opacityOpenClipDepth = ct.clipStack.Count;
                    ct.opacityGroupValue = ca2;
                }
            }
            var maskG = smaskDict is not null
                && (smaskDict.GetName("S") ?? "Luminosity") == "Luminosity"
                ? ct.reader.ResolveStream(smaskDict.Get("G"))
                : null;
            if (maskG is not null && !ct.maskGroupOpen
                && RenderLuminosityMaskPng(ct.reader, smaskDict!, maskG, ct.ctm, ct.pageWidth, ct.pageHeight) is { } mr)
            {
                var maskUrl = ct.imageSink.AddRawPng(mr.Png);
                var mid = $"svgmask{++ct.imageSink.MaskSeq}";
                var msx = (mr.X1 - mr.X0) / mr.PxW;
                var msy = (mr.Y1 - mr.Y0) / mr.PxH;
                ct.svgPaths.Append(
                    $"<mask id=\"{mid}\" maskUnits=\"userSpaceOnUse\" " +
                    $"x=\"{F(mr.X0)}\" y=\"{F(mr.Y0)}\" " +
                    $"width=\"{F(mr.X1 - mr.X0)}\" height=\"{F(mr.Y1 - mr.Y0)}\">" +
                    $"<image x=\"0\" y=\"0\" width=\"{mr.PxW}\" height=\"{mr.PxH}\" " +
                    $"transform=\"matrix({F(msx)} 0 0 {F(-msy)} {F(mr.X0)} {F(mr.Y1)})\" " +
                    $"xlink:href=\"{maskUrl}\" /></mask>" +
                    $"<g mask=\"url(#{mid})\">");
                ct.maskGroupOpen = true;
                ct.maskOpenClipDepth = ct.clipStack.Count;
            }
        }
    }

    /// <summary>The blend mode a /BM entry names; an array names the first mode a viewer
    /// supports, and Compatible is Normal under another name.</summary>
    private static string BlendModeName(PdfObject blend)
    {
        var name = blend is PdfArray { Count: > 0 } arr ? arr[0] as PdfName : blend as PdfName;
        return name is null || name.Value == "Compatible" ? NormalBlend : name.Value;
    }

    private const string NormalBlend = "Normal";

    /// <summary>Normal is the only blend mode under which a fill replaces the backdrop.</summary>
    private static bool BlendPaintsOver(string blendMode) => blendMode == NormalBlend;

    /// <summary>The CSS mix-blend-mode of a PDF blend mode (Multiply -> multiply, HardLight ->
    /// hard-light), null for Normal, which an element blends by without saying so.</summary>
    private static string? CssBlendMode(string blendMode)
    {
        if (BlendPaintsOver(blendMode)) return null;
        var css = new StringBuilder(blendMode.Length + 1);
        foreach (var c in blendMode)
        {
            if (char.IsUpper(c) && css.Length > 0) css.Append('-');
            css.Append(char.ToLowerInvariant(c));
        }
        return css.ToString();
    }

    /// <summary>The attributes a painted SVG element carries from the graphics state: the clip
    /// path in force and a blend mode other than Normal.</summary>
    private static string SvgStateAttributes(ContentRenderState ct)
    {
        var clip = ct.clipId is null ? "" : $" clip-path=\"url(#{ct.clipId})\"";
        var blend = CssBlendMode(ct.blendMode);
        return blend is null ? clip : $"{clip} style=\"mix-blend-mode:{blend}\"";
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void ConcatMatrixOp(ContentRenderState ct)
    {
        if (ct.operands.Count >= 6)
        {
            ct.ctm.Concat(
                Num(ct.operands[0]), Num(ct.operands[1]),
                Num(ct.operands[2]), Num(ct.operands[3]),
                Num(ct.operands[4]), Num(ct.operands[5]));
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void BeginMarkedContentOp(ContentRenderState ct)
    {
        var hasMcid = ct.operands.Count >= 2
            && ct.operands[1] is PdfDictionary mcDict
            && mcDict.Get("MCID") is not null;
        ct.mcStack.Push(hasMcid);
        if (hasMcid) ct.mcSeq++;
        // An /OC region becomes a layer box when the caller asked
        // for marked content as layers: the div carries the
        // optional-content group's own name, and a group that
        // sits under a titled /Order entry nests a second div
        // for the title. Content-stream nesting is mirrored
        // as-is, so a region marked inside another produces
        // nested boxes.
        var opened = 0;
        if (ct.ocLayers is not null && ct.operands.Count >= 2
            && ct.operands[0] is PdfName { Value: "OC" }
            && ct.operands[1] is PdfName ocName
            && ct.ocLayers.TryGetValue(ocName.Value, out var layer))
        {
            FlushParkedLines(ct, ct.styleReg, ct.sb, ct.pageHeight, ct.pageWidth, ct.textOnly, ct.classNamer, ct.linkTargets, ct.rotReg, ct.pageLLX, ct.yTopRef, ct.zCounter, ct.pageTurnedOver, ct.emCompensation);
            ct.sb.Append($"<div class=\"{ct.classNamer.Cls("layer")}\" " +
                $"data-pdflayer=\"{EscapeHtml(layer.Name)}\">");
            opened++;
            if (layer.GroupTitle is { Length: > 0 } title)
            {
                ct.sb.Append($"<div class=\"{ct.classNamer.Cls("layer")}\" " +
                    $"data-pdflayer=\"{EscapeHtml(title)}\">");
                opened++;
            }
        }
        ct.ocDepth.Push(opened);
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void EndMarkedContentOp(ContentRenderState ct)
    {
        if (ct.mcStack.Count > 0 && ct.mcStack.Pop()) ct.mcSeq++;
        if (ct.ocDepth.Count > 0 && ct.ocDepth.Pop() is var closing and > 0)
        {
            FlushParkedLines(ct, ct.styleReg, ct.sb, ct.pageHeight, ct.pageWidth, ct.textOnly, ct.classNamer, ct.linkTargets, ct.rotReg, ct.pageLLX, ct.yTopRef, ct.zCounter, ct.pageTurnedOver, ct.emCompensation);
            for (var c = 0; c < closing; c++) ct.sb.Append("</div>");
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void SetFontOp(ContentRenderState ct)
    {
        if (ct.operands.Count >= 2)
        {
            ct.currentFontKey = (ct.operands[0] as PdfName)?.Value;
            ct.fontSize = Num(ct.operands[1]);
            if (ct.currentFontKey is not null && ct.fonts.TryGetValue(ct.currentFontKey, out var fi))
            {
                ct.fontFamily = fi.Family;
                ct.fontCssFamily = fi.CssFamily;
                ct.fontWeight = fi.Weight;
                ct.fontStyle = fi.Style;
                ct.fontAscent = fi.AscentFactor;
                ct.fontLineHeight = fi.LineHeightEm;
                ct.fontIsType3 = fi.IsType3;
            }
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void MoveTextLineOp(ContentRenderState ct, string op)
    {
        if (ct.operands.Count >= 2)
        {
            if (op == "TD") { ct.leading = -Num(ct.operands[1]); ct.hasLeading = true; }
            // Text-line matrix is translated in text space, then the
            // text matrix is reset to it.
            ct.tlm.Concat(1, 0, 0, 1, Num(ct.operands[0]), Num(ct.operands[1]));
            ct.tm.CopyFrom(ct.tlm);
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void SetTextMatrixOp(ContentRenderState ct)
    {
        if (ct.operands.Count >= 6)
        {
            ct.tlm.Set(Num(ct.operands[0]), Num(ct.operands[1]), Num(ct.operands[2]),
                Num(ct.operands[3]), Num(ct.operands[4]), Num(ct.operands[5]));
            ct.tm.CopyFrom(ct.tlm);
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void SetWordSpacingOp(ContentRenderState ct)
    {
        if (ct.operands.Count >= 1)
            ct.wordSpacing = Num(ct.operands[0]);
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void SetFillRgbOp(ContentRenderState ct)
    {
        if (ct.operands.Count >= 3)
        {
            ct.r = Num(ct.operands[0]); ct.g = Num(ct.operands[1]); ct.b = Num(ct.operands[2]);
            ct.pathState.FillR = ct.r; ct.pathState.FillG = ct.g; ct.pathState.FillB = ct.b;
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void SetStrokeRgbOp(ContentRenderState ct)
    {
        if (ct.operands.Count >= 3)
        {
            ct.pathState.StrokeR = Num(ct.operands[0]);
            ct.pathState.StrokeG = Num(ct.operands[1]);
            ct.pathState.StrokeB = Num(ct.operands[2]);
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void SetFillGrayOp(ContentRenderState ct)
    {
        if (ct.operands.Count >= 1)
        {
            var gray = Num(ct.operands[0]);
            ct.r = ct.g = ct.b = gray;
            ct.pathState.FillR = ct.pathState.FillG = ct.pathState.FillB = gray;
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void SetStrokeGrayOp(ContentRenderState ct)
    {
        if (ct.operands.Count >= 1)
        {
            var gray = Num(ct.operands[0]);
            ct.pathState.StrokeR = ct.pathState.StrokeG = ct.pathState.StrokeB = gray;
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void SetFillCmykOp(ContentRenderState ct)
    {
        if (ct.operands.Count >= 4)
        {
            // Text colours go through the same colour-managed
            // CMYK profile the render devices use, so the
            // emitted classes match the rasterized ink.
            var (lr, lg, lb) = Devices.CmykToRgbLut.Convert(
                Num(ct.operands[0]), Num(ct.operands[1]),
                Num(ct.operands[2]), Num(ct.operands[3]));
            ct.r = lr / 255.0; ct.g = lg / 255.0; ct.b = lb / 255.0;
            ct.pathState.FillR = ct.r; ct.pathState.FillG = ct.g; ct.pathState.FillB = ct.b;
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void SetStrokeCmykOp(ContentRenderState ct)
    {
        if (ct.operands.Count >= 4)
        {
            var (kr, kg, kb) = CmykToRgb(Num(ct.operands[0]), Num(ct.operands[1]),
                Num(ct.operands[2]), Num(ct.operands[3]));
            ct.pathState.StrokeR = kr; ct.pathState.StrokeG = kg; ct.pathState.StrokeB = kb;
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void SetStrokeColorSpaceOp(ContentRenderState ct)
    {
        ct.strokeTintMap = ct.operands.Count >= 1 && ct.operands[0] is PdfName scs
            ? TryBuildTintMap(ct.resources, scs.Value, ct.reader) : null;
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void SetFillColorOp(ContentRenderState ct)
    {
        if (ct.fillTintMap is not null && ct.operands.Count == 1
            && ct.operands[0] is PdfInteger or PdfReal)
        {
            var (tr, tg, tb) = ct.fillTintMap(Num(ct.operands[0]));
            ct.r = tr; ct.g = tg; ct.b = tb;
            ct.pathState.FillR = tr; ct.pathState.FillG = tg; ct.pathState.FillB = tb;
        }
        else if (TryColorComponents(ct.operands) is (var fr, var fg, var fb))
        {
            ct.r = fr; ct.g = fg; ct.b = fb;
            ct.pathState.FillR = fr; ct.pathState.FillG = fg; ct.pathState.FillB = fb;
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void SetStrokeColorOp(ContentRenderState ct)
    {
        if (ct.strokeTintMap is not null && ct.operands.Count == 1
            && ct.operands[0] is PdfInteger or PdfReal)
        {
            var (tr, tg, tb) = ct.strokeTintMap(Num(ct.operands[0]));
            ct.pathState.StrokeR = tr; ct.pathState.StrokeG = tg; ct.pathState.StrokeB = tb;
        }
        else if (TryColorComponents(ct.operands) is (var sr, var sg, var sbb))
        {
            ct.pathState.StrokeR = sr; ct.pathState.StrokeG = sg; ct.pathState.StrokeB = sbb;
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void SetLineWidthOp(ContentRenderState ct)
    {
        // Stroke width lives in user space: fold in the CTM scale.
        if (ct.operands.Count >= 1)
            ct.pathState.LineWidth = Num(ct.operands[0]) * ct.ctm.Scale;
    }
}

using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void DrawXObjectOp(ContentRenderState ct, string op)
    {
        // An image XObject paints in graphics modes; anything else
        // falls through to the FORM branch. Form XObjects carry
        // their own content (annotation overlays commonly nest
        // text several forms deep — e.g. rotated note text): both
        // the text-only overlay and the SVG-text dialect recurse
        // so that text is not silently dropped. The visited set
        // guards self-referencing forms (an inner /Fm0 resolving
        // to its own stream) and is released after the call so a
        // form invoked at several call sites still renders at each.
        if (!ct.textOnly && ct.operands.Count >= 1 && ct.operands[0] is PdfName xobjName
            && ct.imageXObjects.TryGetValue(xobjName.Value, out var img))
        {
            // An image ends the parking window only when it lands in the
            // TEXT LAYER: under the SVG-referenced and data-URI shapes it
            // goes to the page SVG instead and the layer never sees it, so
            // lines the producer is still adding to must not close on its
            // account.
            var imageEntersTextLayer = ct.imageSink is null
                || !(ct.imageSink.SvgImageRefs || ct.imageSink.EmbedDataUris);
            if (imageEntersTextLayer) FlushParkedLines(ct, ct.styleReg, ct.sb, ct.pageHeight, ct.pageWidth, ct.textOnly, ct.classNamer, ct.linkTargets, ct.rotReg, ct.pageLLX, ct.yTopRef, ct.zCounter, ct.pageTurnedOver, ct.emCompensation);
            if (ct.imageSink is not null) ct.imageSink.Emit(ct.sb, ct.svgPaths, img, ct.ctm, ct.pageHeight);
            else EmitImage(ct.sb, img, ct.ctm, ct.pageHeight);
        }
        // Form XObjects carry their own content (annotation overlays
        // commonly nest text several forms deep — e.g. rotated note
        // text, or a datasheet whose whole body table lives in one
        // form); both the text-only overlay and the full export
        // recurse so that content is not silently dropped. The
        // visited set guards self-referencing forms (an inner /Fm0
        // resolving to its own stream) and is released after the
        // call so a form invoked at several call sites still
        // renders at each.
        else if (ct.resources is not null
            && ct.operands.Count >= 1 && ct.operands[0] is PdfName formName)
        {
            var xoDict = ct.reader.ResolveDict(ct.resources.Get("XObject"));
            var formStream = xoDict is not null ? ct.reader.ResolveStream(xoDict.Get(formName.Value)) : null;
            if (formStream is not null && formStream.Dict.GetName("Subtype") == "Form"
                && (ct.visitedForms ??= new HashSet<PdfStream>()).Add(formStream))
            {
                byte[]? formBytes = null;
                try { formBytes = ct.reader.DecodeStream(formStream); } catch { /* skip undecodable */ }
                if (formBytes is not null)
                {
                    FlushParkedLines(ct, ct.styleReg, ct.sb, ct.pageHeight, ct.pageWidth, ct.textOnly, ct.classNamer, ct.linkTargets, ct.rotReg, ct.pageLLX, ct.yTopRef, ct.zCounter, ct.pageTurnedOver, ct.emCompensation);
                    var formRes = ct.reader.ResolveDict(formStream.Dict.Get("Resources")) ?? ct.resources;
                    var formFonts = ResolveFontsFromResources(formRes, ct.reader, ct.preferFontCmap, ct.substitutors, ct.defaultFontName);
                    if (formFonts.Count == 0) formFonts = ct.fonts;
                    // The form's own /XObject images shadow the page's.
                    var formImages = ct.imageXObjects;
                    var formXo = ct.reader.ResolveDict(formRes.Get("XObject"));
                    if (formXo is not null)
                    {
                        Dictionary<string, ImageXObject>? own = null;
                        foreach (var k in formXo.Keys)
                        {
                            var imgStream = ct.reader.ResolveStream(formXo.Get(k));
                            if (imgStream is not null && imgStream.Dict.GetName("Subtype") == "Image")
                                (own ??= new Dictionary<string, ImageXObject>(ct.imageXObjects, StringComparer.Ordinal))[k]
                                    = new ImageXObject(k, imgStream, ct.reader);
                        }
                        if (own is not null) formImages = own;
                    }
                    var childCtm = ct.ctm.Clone();
                    if (ct.reader.Resolve(formStream.Dict.Get("Matrix")) is PdfArray fm && fm.Count >= 6)
                        childCtm.Concat(
                            Num(ct.reader.Resolve(fm[0])!), Num(ct.reader.Resolve(fm[1])!),
                            Num(ct.reader.Resolve(fm[2])!), Num(ct.reader.Resolve(fm[3])!),
                            Num(ct.reader.Resolve(fm[4])!), Num(ct.reader.Resolve(fm[5])!));
                    RenderContentToHtml(formBytes, formFonts, formImages, ct.reader, ct.sb,
                        ct.pageHeight, ct.pageWidth, ct.saveTransparentTexts,
                        emCompensation: ct.emCompensation,
                        textOnly: ct.textOnly,
                        externalSvgPaths: ct.textOnly ? null : ct.svgPaths,
                        imageSink: ct.textOnly ? null : ct.imageSink,
                        styleReg: ct.styleReg, classNamer: ct.classNamer,
                        linkTargets: ct.linkTargets,
                        resources: formRes, preferFontCmap: ct.preferFontCmap,
                        substitutors: ct.substitutors, initialCtm: childCtm,
                        visitedForms: ct.visitedForms, rotReg: ct.rotReg,
                        pageLLX: ct.pageLLX, yTopRef: ct.yTopRef, zCounter: ct.zCounter,
                        defaultFontName: ct.defaultFontName,
                        authoredPathShape: ct.authoredPathShape);
                }
                ct.visitedForms.Remove(formStream);
            }
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void MoveToOp(ContentRenderState ct, string op)
    {
        if (ct.operands.Count >= 2)
        {
            var (px, py) = Dp(ct, Num(ct.operands[0]), Num(ct.operands[1]));
            ct.pathState.Data.Append($"M{F(px)} {F(py)}");
            ct.cpx = px; ct.cpy = py;
            (ct.curDevX, ct.curDevY) = Dev(ct, Num(ct.operands[0]), Num(ct.operands[1]));
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void LineToOp(ContentRenderState ct, string op)
    {
        if (ct.operands.Count >= 2)
        {
            var (px, py) = Dp(ct, Num(ct.operands[0]), Num(ct.operands[1]));
            ct.pathState.Data.Append($"L{F(px)} {F(py)}");
            ct.cpx = px; ct.cpy = py;
            var (lx, ly) = Dev(ct, Num(ct.operands[0]), Num(ct.operands[1]));
            ct.pathSegs.Add((ct.curDevX, ct.curDevY, lx, ly));
            (ct.curDevX, ct.curDevY) = (lx, ly);
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void CurveToOp(ContentRenderState ct, string op)
    {
        if (ct.operands.Count >= 6)
        {
            var (x1, y1) = Dp(ct, Num(ct.operands[0]), Num(ct.operands[1]));
            var (x2, y2) = Dp(ct, Num(ct.operands[2]), Num(ct.operands[3]));
            var (x3, y3) = Dp(ct, Num(ct.operands[4]), Num(ct.operands[5]));
            ct.pathState.Data.Append($"C{F(x1)} {F(y1)} {F(x2)} {F(y2)} {F(x3)} {F(y3)}");
            ct.cpx = x3; ct.cpy = y3;
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void CurveToInitialOp(ContentRenderState ct, string op)
    {
        if (ct.operands.Count >= 4)
        {
            var (x2, y2) = Dp(ct, Num(ct.operands[0]), Num(ct.operands[1]));
            var (x3, y3) = Dp(ct, Num(ct.operands[2]), Num(ct.operands[3]));
            ct.pathState.Data.Append($"C{F(ct.cpx)} {F(ct.cpy)} {F(x2)} {F(y2)} {F(x3)} {F(y3)}");
            ct.cpx = x3; ct.cpy = y3;
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void CurveToFinalOp(ContentRenderState ct, string op)
    {
        if (ct.operands.Count >= 4)
        {
            var (x1, y1) = Dp(ct, Num(ct.operands[0]), Num(ct.operands[1]));
            var (x3, y3) = Dp(ct, Num(ct.operands[2]), Num(ct.operands[3]));
            ct.pathState.Data.Append($"C{F(x1)} {F(y1)} {F(x3)} {F(y3)} {F(x3)} {F(y3)}");
            ct.cpx = x3; ct.cpy = y3;
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void RectangleOp(ContentRenderState ct, string op)
    {
        if (ct.operands.Count >= 4)
        {
            var rx = Num(ct.operands[0]); var ry = Num(ct.operands[1]);
            var rw = Num(ct.operands[2]); var rh = Num(ct.operands[3]);
            var (ax, ay) = Dp(ct, rx, ry);
            var (bx, by) = Dp(ct, rx + rw, ry);
            var (cx2, cy2) = Dp(ct, rx + rw, ry + rh);
            var (dx, dy) = Dp(ct, rx, ry + rh);
            ct.pathState.Data.Append($"M{F(ax)} {F(ay)}L{F(bx)} {F(by)}L{F(cx2)} {F(cy2)}L{F(dx)} {F(dy)}Z");
            ct.cpx = ax; ct.cpy = ay;
            var (dx0, dy0) = Dev(ct, rx, ry);
            var (dx1, dy1) = Dev(ct, rx + rw, ry + rh);
            ct.pendingRects.Add((dx0, dy0, dx1, dy1));
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void StrokeOp(ContentRenderState ct, string op)
    {
        if (ct.pathState.Data.Length > 0)
        {
            EmitSvgPath(ct.svgPaths, ct.pathState, stroke: true, fill: false, pageHeight: ct.pageHeight, pathId: ct.pathOpenIndex, authoredShape: ct.authoredPathShape);
            ct.pathState.Clear(); ct.pathOpenIndex = -1;
        }
        CollectRuleCandidates(ct, stroked: true, filled: false);
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void CloseStrokeOp(ContentRenderState ct, string op)
    {
        ct.pathState.Data.Append("Z");
        if (ct.pathState.Data.Length > 0)
        {
            EmitSvgPath(ct.svgPaths, ct.pathState, stroke: true, fill: false, pageHeight: ct.pageHeight, pathId: ct.pathOpenIndex, authoredShape: ct.authoredPathShape);
            ct.pathState.Clear(); ct.pathOpenIndex = -1;
        }
        CollectRuleCandidates(ct, stroked: true, filled: false);
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void FillOp(ContentRenderState ct, string op)
    {
        if (ct.pathState.Data.Length > 0)
        {
            EmitSvgPath(ct.svgPaths, ct.pathState, stroke: false, fill: true, pageHeight: ct.pageHeight, pathId: ct.pathOpenIndex, authoredShape: ct.authoredPathShape);
            ct.pathState.Clear(); ct.pathOpenIndex = -1;
        }
        CollectRuleCandidates(ct, stroked: false, filled: true);
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void FillEvenOddOp(ContentRenderState ct, string op)
    {
        if (ct.pathState.Data.Length > 0)
        {
            EmitSvgPath(ct.svgPaths, ct.pathState, stroke: false, fill: true, evenOdd: true, pageHeight: ct.pageHeight, pathId: ct.pathOpenIndex, authoredShape: ct.authoredPathShape);
            ct.pathState.Clear(); ct.pathOpenIndex = -1;
        }
        CollectRuleCandidates(ct, stroked: false, filled: true);
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void FillStrokeOp(ContentRenderState ct, string op)
    {
        if (ct.pathState.Data.Length > 0)
        {
            EmitSvgPath(ct.svgPaths, ct.pathState, stroke: true, fill: true, pageHeight: ct.pageHeight, pathId: ct.pathOpenIndex, authoredShape: ct.authoredPathShape);
            ct.pathState.Clear(); ct.pathOpenIndex = -1;
        }
        CollectRuleCandidates(ct, stroked: true, filled: true);
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void FillStrokeEvenOddOp(ContentRenderState ct, string op)
    {
        if (ct.pathState.Data.Length > 0)
        {
            EmitSvgPath(ct.svgPaths, ct.pathState, stroke: true, fill: true, evenOdd: true, pageHeight: ct.pageHeight, pathId: ct.pathOpenIndex, authoredShape: ct.authoredPathShape);
            ct.pathState.Clear(); ct.pathOpenIndex = -1;
        }
        CollectRuleCandidates(ct, stroked: true, filled: true);
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void CloseFillStrokeOp(ContentRenderState ct, string op)
    {
        ct.pathState.Data.Append("Z");
        if (ct.pathState.Data.Length > 0)
        {
            EmitSvgPath(ct.svgPaths, ct.pathState, stroke: true, fill: true, pageHeight: ct.pageHeight, pathId: ct.pathOpenIndex, authoredShape: ct.authoredPathShape);
            ct.pathState.Clear(); ct.pathOpenIndex = -1;
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void CloseFillStrokeEvenOddOp(ContentRenderState ct, string op)
    {
        ct.pathState.Data.Append("Z");
        if (ct.pathState.Data.Length > 0)
        {
            EmitSvgPath(ct.svgPaths, ct.pathState, stroke: true, fill: true, evenOdd: true, pageHeight: ct.pageHeight, pathId: ct.pathOpenIndex, authoredShape: ct.authoredPathShape);
            ct.pathState.Clear(); ct.pathOpenIndex = -1;
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void ClipOp(ContentRenderState ct, string op)
    {
        // The clip takes the path as it stands; the painting op that
        // ends the path commits it.
        ct.pendingClipD = ct.pathState.Data.ToString().Trim();
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void EndPathOp(ContentRenderState ct, string op)
    {
        if (ct.pendingClipD is { Length: > 0 }) { ct.clipD = ct.pendingClipD; ct.pendingClipD = null; }
        ct.pathState.Clear(); ct.pathOpenIndex = -1;
        ct.pathSegs.Clear();
        ct.pendingRects.Clear();
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void ShadingOp(ContentRenderState ct, string op)
    {
        // A shading painted straight onto the page fills the current
        // clip with the gradient the shading dictionary describes.
        if (!ct.textOnly && ct.resources is not null && ct.operands.Count >= 1
            && ct.operands[0] is PdfName shName
            && ct.reader.ResolveDict(ct.resources.Get("Shading")) is { } shDict)
        {
            var shading = Aspose.Pdf.Shading.ShadingBase.Parse(shDict.Get(shName.Value), ct.reader);
            EmitSvgShading(ct.svgPaths, shading, ct.clipD, ct, ++ct.shadingSeq, ct.pageHeight);
        }
    }
}

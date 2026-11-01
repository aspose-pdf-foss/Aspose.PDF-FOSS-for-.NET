using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void DrawXObjectOp(ContentRenderState ct)
    {
        RecordImageInk(ct);
        RecordOpaqueImageCover(ct);
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
            if (ct.imageSink is not null) ct.imageSink.Emit(ct.sb, ct.svgPaths, img, ct.ctm, ct.pageHeight, SvgStateAttributes(ct));
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
                    var (boxClipD, boxClipId) = ct.textOnly ? (ct.clipD, ct.clipId) : FormBoxClip(ct, formStream, childCtm);
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
                        authoredPathShape: ct.authoredPathShape, perGlyphDivs: ct.perGlyphDivs,
                        model: ct.model, initialClipD: boxClipD, initialClipId: boxClipId,
                        initialFillAlpha: ct.fillAlpha, frameWidth: ct.frameWidth);
                }
                ct.visitedForms.Remove(formStream);
            }
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void MoveToOp(ContentRenderState ct)
    {
        if (ct.operands.Count >= 2)
        {
            var (px, py) = Dp(ct, Num(ct.operands[0]), Num(ct.operands[1]));
            ct.pathState.Data.Append($"M{F(px)} {F(py)}");
            ct.pathState.Extend(px, py);
            ct.singleRectPathLength = -1;
            ct.cpx = px; ct.cpy = py;
            (ct.curDevX, ct.curDevY) = Dev(ct, Num(ct.operands[0]), Num(ct.operands[1]));
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void LineToOp(ContentRenderState ct)
    {
        if (ct.operands.Count >= 2)
        {
            var (px, py) = Dp(ct, Num(ct.operands[0]), Num(ct.operands[1]));
            ct.pathState.Data.Append($"L{F(px)} {F(py)}");
            ct.pathState.Extend(px, py);
            ct.singleRectPathLength = -1;
            ct.cpx = px; ct.cpy = py;
            var (lx, ly) = Dev(ct, Num(ct.operands[0]), Num(ct.operands[1]));
            ct.pathSegs.Add((ct.curDevX, ct.curDevY, lx, ly));
            (ct.curDevX, ct.curDevY) = (lx, ly);
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void CurveToOp(ContentRenderState ct)
    {
        if (ct.operands.Count >= 6)
        {
            var (x1, y1) = Dp(ct, Num(ct.operands[0]), Num(ct.operands[1]));
            var (x2, y2) = Dp(ct, Num(ct.operands[2]), Num(ct.operands[3]));
            var (x3, y3) = Dp(ct, Num(ct.operands[4]), Num(ct.operands[5]));
            ct.pathState.Data.Append($"C{F(x1)} {F(y1)} {F(x2)} {F(y2)} {F(x3)} {F(y3)}");
            ct.pathState.Extend(x1, y1); ct.pathState.Extend(x2, y2); ct.pathState.Extend(x3, y3);
            ct.singleRectPathLength = -1;
            ct.cpx = x3; ct.cpy = y3;
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void CurveToInitialOp(ContentRenderState ct)
    {
        if (ct.operands.Count >= 4)
        {
            var (x2, y2) = Dp(ct, Num(ct.operands[0]), Num(ct.operands[1]));
            var (x3, y3) = Dp(ct, Num(ct.operands[2]), Num(ct.operands[3]));
            ct.pathState.Data.Append($"C{F(ct.cpx)} {F(ct.cpy)} {F(x2)} {F(y2)} {F(x3)} {F(y3)}");
            ct.pathState.Extend(x2, y2); ct.pathState.Extend(x3, y3);
            ct.singleRectPathLength = -1;
            ct.cpx = x3; ct.cpy = y3;
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void CurveToFinalOp(ContentRenderState ct)
    {
        if (ct.operands.Count >= 4)
        {
            var (x1, y1) = Dp(ct, Num(ct.operands[0]), Num(ct.operands[1]));
            var (x3, y3) = Dp(ct, Num(ct.operands[2]), Num(ct.operands[3]));
            ct.pathState.Data.Append($"C{F(x1)} {F(y1)} {F(x3)} {F(y3)} {F(x3)} {F(y3)}");
            ct.pathState.Extend(x1, y1); ct.pathState.Extend(x3, y3);
            ct.singleRectPathLength = -1;
            ct.cpx = x3; ct.cpy = y3;
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void RectangleOp(ContentRenderState ct)
    {
        if (ct.operands.Count >= 4)
        {
            var rx = Num(ct.operands[0]); var ry = Num(ct.operands[1]);
            var rw = Num(ct.operands[2]); var rh = Num(ct.operands[3]);
            var (ax, ay) = Dp(ct, rx, ry);
            var (bx, by) = Dp(ct, rx + rw, ry);
            var (cx2, cy2) = Dp(ct, rx + rw, ry + rh);
            var (dx, dy) = Dp(ct, rx, ry + rh);
            // A path made of this one rectangle has an exact device-space footprint: the box of
            // its four corners, kept for the text-layer model's cover test.
            var alone = ct.pathState.Data.Length == 0;
            ct.pathState.Data.Append($"M{F(ax)} {F(ay)}L{F(bx)} {F(by)}L{F(cx2)} {F(cy2)}L{F(dx)} {F(dy)}Z");
            ct.pathState.Extend(ax, ay); ct.pathState.Extend(bx, by); ct.pathState.Extend(cx2, cy2); ct.pathState.Extend(dx, dy);
            ct.singleRectPathLength = alone ? ct.pathState.Data.Length : -1;
            if (alone)
                ct.singleRectBox = (Math.Min(Math.Min(ax, bx), Math.Min(cx2, dx)), Math.Min(Math.Min(ay, by), Math.Min(cy2, dy)),
                    Math.Max(Math.Max(ax, bx), Math.Max(cx2, dx)), Math.Max(Math.Max(ay, by), Math.Max(cy2, dy)));
            ct.cpx = ax; ct.cpy = ay;
            var (dx0, dy0) = Dev(ct, rx, ry);
            var (dx1, dy1) = Dev(ct, rx + rw, ry + rh);
            ct.pendingRects.Add((dx0, dy0, dx1, dy1));
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void StrokeOp(ContentRenderState ct)
    {
        if (ct.pathState.Data.Length > 0)
        {
            RecordPathInk(ct, stroked: true);
            EmitSvgPath(ct.svgPaths, ct.pathState, stroke: true, fill: false, pageHeight: ct.pageHeight, pathId: ct.pathOpenIndex, authoredShape: ct.authoredPathShape, stateAttrs: SvgStateAttributes(ct));
            ct.pathState.Clear(); ct.pathOpenIndex = -1;
        }
        CollectRuleCandidates(ct, stroked: true, filled: false);
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void CloseStrokeOp(ContentRenderState ct)
    {
        ct.pathState.Data.Append("Z");
        if (ct.pathState.Data.Length > 0)
        {
            RecordPathInk(ct, stroked: true);
            EmitSvgPath(ct.svgPaths, ct.pathState, stroke: true, fill: false, pageHeight: ct.pageHeight, pathId: ct.pathOpenIndex, authoredShape: ct.authoredPathShape, stateAttrs: SvgStateAttributes(ct));
            ct.pathState.Clear(); ct.pathOpenIndex = -1;
        }
        CollectRuleCandidates(ct, stroked: true, filled: false);
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void FillOp(ContentRenderState ct)
    {
        RecordOpaqueCover(ct);
        if (ct.pathState.Data.Length > 0)
        {
            RecordPathInk(ct, stroked: false);
            EmitSvgPath(ct.svgPaths, ct.pathState, stroke: false, fill: true, pageHeight: ct.pageHeight, pathId: ct.pathOpenIndex, authoredShape: ct.authoredPathShape, stateAttrs: SvgStateAttributes(ct));
            ct.pathState.Clear(); ct.pathOpenIndex = -1;
        }
        CollectRuleCandidates(ct, stroked: false, filled: true);
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void FillEvenOddOp(ContentRenderState ct)
    {
        RecordOpaqueCover(ct);
        if (ct.pathState.Data.Length > 0)
        {
            RecordPathInk(ct, stroked: false);
            EmitSvgPath(ct.svgPaths, ct.pathState, stroke: false, fill: true, evenOdd: true, pageHeight: ct.pageHeight, pathId: ct.pathOpenIndex, authoredShape: ct.authoredPathShape, stateAttrs: SvgStateAttributes(ct));
            ct.pathState.Clear(); ct.pathOpenIndex = -1;
        }
        CollectRuleCandidates(ct, stroked: false, filled: true);
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void FillStrokeOp(ContentRenderState ct)
    {
        RecordOpaqueCover(ct);
        if (ct.pathState.Data.Length > 0)
        {
            RecordPathInk(ct, stroked: true);
            EmitSvgPath(ct.svgPaths, ct.pathState, stroke: true, fill: true, pageHeight: ct.pageHeight, pathId: ct.pathOpenIndex, authoredShape: ct.authoredPathShape, stateAttrs: SvgStateAttributes(ct));
            ct.pathState.Clear(); ct.pathOpenIndex = -1;
        }
        CollectRuleCandidates(ct, stroked: true, filled: true);
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void FillStrokeEvenOddOp(ContentRenderState ct)
    {
        RecordOpaqueCover(ct);
        if (ct.pathState.Data.Length > 0)
        {
            RecordPathInk(ct, stroked: true);
            EmitSvgPath(ct.svgPaths, ct.pathState, stroke: true, fill: true, evenOdd: true, pageHeight: ct.pageHeight, pathId: ct.pathOpenIndex, authoredShape: ct.authoredPathShape, stateAttrs: SvgStateAttributes(ct));
            ct.pathState.Clear(); ct.pathOpenIndex = -1;
        }
        CollectRuleCandidates(ct, stroked: true, filled: true);
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void CloseFillStrokeOp(ContentRenderState ct)
    {
        RecordOpaqueCover(ct);
        ct.pathState.Data.Append("Z");
        if (ct.pathState.Data.Length > 0)
        {
            RecordPathInk(ct, stroked: true);
            EmitSvgPath(ct.svgPaths, ct.pathState, stroke: true, fill: true, pageHeight: ct.pageHeight, pathId: ct.pathOpenIndex, authoredShape: ct.authoredPathShape, stateAttrs: SvgStateAttributes(ct));
            ct.pathState.Clear(); ct.pathOpenIndex = -1;
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void CloseFillStrokeEvenOddOp(ContentRenderState ct)
    {
        RecordOpaqueCover(ct);
        ct.pathState.Data.Append("Z");
        if (ct.pathState.Data.Length > 0)
        {
            RecordPathInk(ct, stroked: true);
            EmitSvgPath(ct.svgPaths, ct.pathState, stroke: true, fill: true, evenOdd: true, pageHeight: ct.pageHeight, pathId: ct.pathOpenIndex, authoredShape: ct.authoredPathShape, stateAttrs: SvgStateAttributes(ct));
            ct.pathState.Clear(); ct.pathOpenIndex = -1;
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void ClipOp(ContentRenderState ct, bool evenOdd)
    {
        // The clip takes the path as it stands; the painting op that
        // ends the path commits it.
        ct.pendingClipD = ct.pathState.Data.ToString().Trim();
        ct.pendingClipEvenOdd = evenOdd;
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void EndPathOp(ContentRenderState ct)
    {
        if (ct.pendingClipD is { Length: > 0 } clip)
        {
            ct.clipD = clip;
            ct.clipId = DefineSvgClip(ct, clip, ct.pendingClipEvenOdd);
            ct.pendingClipD = null;
        }
        ct.pathState.Clear(); ct.pathOpenIndex = -1;
        ct.pathSegs.Clear();
        ct.pendingRects.Clear();
    }

    /// <summary>Writes a clip as an SVG clip path nested in the clip already in force and
    /// returns its id, which every element painted under it then carries. A rectangle holding
    /// the whole page clips nothing and keeps the enclosing id, so the page-sized clip most
    /// producers open with costs no definition.</summary>
    private static string? DefineSvgClip(ContentRenderState ct, string d, bool evenOdd)
    {
        if (ct.textOnly) return ct.clipId;
        var (x0, y0, x1, y1) = PageBox(ct);
        if (TryRectOfPath(d) is { } r && r.X0 <= x0 + ClipSlackPt && r.Y0 <= y0 + ClipSlackPt
            && r.X1 >= x1 - ClipSlackPt && r.Y1 >= y1 - ClipSlackPt)
            return ct.clipId;
        var id = $"clip{(ct.imageSink is { } sink ? ++sink.ClipSeq : ++ct.clipSeq)}";
        var outer = ct.clipId is null ? "" : $" clip-path=\"url(#{ct.clipId})\"";
        var rule = evenOdd ? " clip-rule=\"evenodd\"" : "";
        ct.svgPaths.Append($"<clipPath id=\"{id}\"{outer}><path d=\"{d}\"{rule} /></clipPath>");
        return id;
    }

    /// <summary>A clip may fall this far (pt) inside the page edge and still count as the page.</summary>
    private const double ClipSlackPt = 0.5;

    /// <summary>The page box in the device space the paths are written in: left, bottom, right, top.
    /// The top reference, where one is given, is the box's bottom raised by the whole points of its
    /// height (the text grid's origin), so the bottom is that reference less those whole points, not
    /// less the height itself, which put the bottom up to a point below the page and made every
    /// page-sized clip of a page of fractional height look narrower than the page.</summary>
    private static (double X0, double Y0, double X1, double Y1) PageBox(ContentRenderState ct)
    {
        var bottom = double.IsNaN(ct.yTopRef) ? 0 : ct.yTopRef - Math.Floor(ct.pageHeight);
        return (ct.pageLLX, bottom, ct.pageLLX + ct.pageWidth, bottom + ct.pageHeight);
    }

    /// <summary>The clip a form's box imposes on its content, in the page's device space through
    /// the matrix the form is drawn with: defined as a clip path inside the clip in force, so the
    /// form's drawings are cut to its box the way a viewer cuts them.</summary>
    private static (string? D, string? Id) FormBoxClip(ContentRenderState ct, PdfStream form, CtmState childCtm)
    {
        if (ct.reader.Resolve(form.Dict.Get("BBox")) is not PdfArray bb || bb.Count < 4) return (ct.clipD, ct.clipId);
        double n(int k) => ct.reader.Resolve(bb[k]) is { } o ? NumFromObj(o) : 0;
        var sb = new StringBuilder();
        var corners = new[] { (n(0), n(1)), (n(2), n(1)), (n(2), n(3)), (n(0), n(3)) };
        for (var i = 0; i < corners.Length; i++)
        {
            var (cx, cy) = corners[i];
            var px = cx * childCtm.A + cy * childCtm.C + childCtm.E;
            var py = cx * childCtm.B + cy * childCtm.D + childCtm.F;
            sb.Append(i == 0 ? 'M' : 'L').Append(F(px)).Append(' ').Append(F(py));
        }
        sb.Append('Z');
        var d = sb.ToString();
        return (d, DefineSvgClip(ct, d, evenOdd: false));
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void ShadingOp(ContentRenderState ct)
    {
        RecordShadingInk(ct);
        // A shading painted straight onto the page fills the current
        // clip with the gradient the shading dictionary describes.
        if (!ct.textOnly && ct.resources is not null && ct.operands.Count >= 1
            && ct.operands[0] is PdfName shName
            && ct.reader.ResolveDict(ct.resources.Get("Shading")) is { } shDict)
        {
            var shading = Aspose.Pdf.Shading.ShadingBase.Parse(shDict.Get(shName.Value), ct.reader);
            EmitSvgShading(ct.svgPaths, shading, ct.clipD, ct, NextGradientNumber(ct), SvgStateAttributes(ct));
        }
    }

    /// <summary>The number of the next gradient definition. Every form the page invokes renders
    /// through its own state into the one page SVG, so the sequence lives on the sink the
    /// states share; the state's own counter serves the dialects that render without one.</summary>
    private static int NextGradientNumber(ContentRenderState ct) =>
        ct.imageSink is { } sink ? ++sink.GradientSeq : ++ct.shadingSeq;
}

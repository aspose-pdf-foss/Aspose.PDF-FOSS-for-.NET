using System.Collections.Generic;
using Aspose.Pdf.Operators;

namespace Aspose.Pdf.Vector;

public sealed partial class GraphicsAbsorber
{
// The helpers of the graphics walk: the subpath flush, path start, point accumulation, paint, discard and pending clip.
    private static void FlushSubpath(GraphicsWalkState gw)
    {
        if (gw.curOps is { Count: > 0 } && gw.any)
            gw.subpaths.Add((gw.curCtm!, gw.curOps, gw.minX, gw.minY, gw.maxX, gw.maxY, gw.curStart, gw.curEnd));
        gw.curOps = null; gw.any = false;
    }

    private static int PathStart(GraphicsWalkState gw) =>
        gw.subpaths.Count > 0 ? gw.subpaths[0].startIdx : gw.curStart;

    private static void StartSubpath(GraphicsWalkState gw, double ux, double uy, int idx)
    {
        FlushSubpath(gw);
        gw.curOps = new List<Aspose.Pdf.Operator>();
        gw.curCtm = new Aspose.Pdf.Matrix(gw.gs.Ctm);
        gw.any = false; gw.curX = ux; gw.curY = uy;
        gw.curStart = idx; gw.curEnd = idx;
    }

    private static void AddSubpathPoint(GraphicsWalkState gw, double ux, double uy)
    {
        var (px, py) = gw.curCtm!.TransformPoint(ux, uy);
        if (!gw.any) { gw.minX = gw.maxX = px; gw.minY = gw.maxY = py; gw.any = true; }
        else
        {
            if (px < gw.minX) gw.minX = px; else if (px > gw.maxX) gw.maxX = px;
            if (py < gw.minY) gw.minY = py; else if (py > gw.maxY) gw.maxY = py;
        }
    }

    private static void PaintSubpath(GraphicsWalkState gw, Aspose.Pdf.Operator paintOp, bool fill, bool stroke, bool evenOdd)
    {
        FlushSubpath(gw);
        var style = new SubPathStyle(
            fill ? gw.gs.Fill : null,
            stroke ? gw.gs.Stroke : null,
            gw.gs.LineWidth, gw.gs.LineJoin, evenOdd);
        foreach (var sp in gw.subpaths)
        {
            var el = new SubPath(sp.ctm, sp.ops, paintOp,
                new Rectangle(sp.minX, sp.minY, sp.maxX, sp.maxY), style);
            if (gw.depth == 0)
            {
                el.SetSourceRange(sp.startIdx, sp.endIdx);
                el.SourceClip = gw.gs.ActiveClip;
            }
            el.PaintClip = gw.gs.Clip;
            gw.elements.AddInternal(el);
        }
        gw.subpaths.Clear();
        ActivatePendingClip(gw);
    }

    private static void DiscardSubpath(GraphicsWalkState gw)
    {
        FlushSubpath(gw);
        gw.subpaths.Clear();
        ActivatePendingClip(gw);
    }

    private static void ActivatePendingClip(GraphicsWalkState gw)
    {
        if (gw.pendingClip is not null) { gw.gs.ActiveClip = gw.pendingClip; gw.pendingClip = null; }
        if (gw.pendingRegion is not null) { gw.gs.Clip = gw.pendingRegion; gw.pendingRegion = null; }
    }

    /// <summary>The paths of the current path object, finished and in construction, each with its CTM.</summary>
    private static List<(Aspose.Pdf.Matrix Ctm, IReadOnlyList<Aspose.Pdf.Operator> Ops)> CurrentPaths(GraphicsWalkState gw)
    {
        var paths = new List<(Aspose.Pdf.Matrix Ctm, IReadOnlyList<Aspose.Pdf.Operator> Ops)>();
        foreach (var sp in gw.subpaths) paths.Add((sp.ctm, sp.ops));
        if (gw.curOps is { Count: > 0 } && gw.any) paths.Add((gw.curCtm!, new List<Aspose.Pdf.Operator>(gw.curOps)));
        return paths;
    }

    /// <summary></summary>
    private static bool WalkOperator(GraphicsWalkState gw, int i)
    {
        var op = gw.ops[i];
        switch (op)
        {
            case GSave: gw.stack.Push(gw.gs.Clone()); break;
            case GRestore: if (gw.stack.Count > 0) gw.gs = gw.stack.Pop(); break;
            case ConcatenateMatrix cm: gw.gs.Ctm = cm.Matrix.Multiply(gw.gs.Ctm); break;

            case SetRGBColor rgb:
                gw.gs.Fill = (rgb.R, rgb.G, rgb.B);
                break;
            case SetGray or SetCMYKColor or SetColor or SetAdvancedColor:
            {
                var c = ((SetColorOperator)op).getColor();
                gw.gs.Fill = (c.R / 255.0, c.G / 255.0, c.B / 255.0);
                break;
            }
            case SetRGBColorStroke or SetGrayStroke or SetCMYKColorStroke
                or SetColorStroke or SetAdvancedColorStroke:
            {
                var c = ((SetColorOperator)op).getColor();
                gw.gs.Stroke = (c.R / 255.0, c.G / 255.0, c.B / 255.0);
                break;
            }
            case SetLineWidth lw: gw.gs.LineWidth = lw.Width; break;
            case SetLineJoin lj: gw.gs.LineJoin = (int)lj.Join; break;

            case MoveTo m:
                StartSubpath(gw, m.X, m.Y, i); gw.curOps!.Add(m); AddSubpathPoint(gw, m.X, m.Y); break;
            case Re re:
                StartSubpath(gw, re.X, re.Y, i); gw.curOps!.Add(re);
                AddSubpathPoint(gw, re.X, re.Y); AddSubpathPoint(gw, re.X + re.Width, re.Y);
                AddSubpathPoint(gw, re.X + re.Width, re.Y + re.Height); AddSubpathPoint(gw, re.X, re.Y + re.Height);
                break;
            case LineTo l:
                if (gw.curOps is null) StartSubpath(gw, l.X, l.Y, i);
                gw.curOps!.Add(l); AddSubpathPoint(gw, l.X, l.Y); gw.curX = l.X; gw.curY = l.Y; gw.curEnd = i; break;
            case CurveTo c:
                if (gw.curOps is null) StartSubpath(gw, gw.curX, gw.curY, i);
                gw.curOps!.Add(c); AddSubpathPoint(gw, c.X1, c.Y1); AddSubpathPoint(gw, c.X2, c.Y2); AddSubpathPoint(gw, c.X3, c.Y3);
                gw.curX = c.X3; gw.curY = c.Y3; gw.curEnd = i; break;
            case CurveTo1 v:
                if (gw.curOps is null) StartSubpath(gw, gw.curX, gw.curY, i);
                gw.curOps!.Add(v); AddSubpathPoint(gw, v.X2, v.Y2); AddSubpathPoint(gw, v.X3, v.Y3);
                gw.curX = v.X3; gw.curY = v.Y3; gw.curEnd = i; break;
            case CurveTo2 y:
                if (gw.curOps is null) StartSubpath(gw, gw.curX, gw.curY, i);
                gw.curOps!.Add(y); AddSubpathPoint(gw, y.X1, y.Y1); AddSubpathPoint(gw, y.X3, y.Y3);
                gw.curX = y.X3; gw.curY = y.Y3; gw.curEnd = i; break;
            case ClosePath h:
                if (gw.curOps is not null) { gw.curOps.Add(h); gw.curEnd = i; }
                break;

            case Stroke or ClosePathStroke:
                PaintSubpath(gw, op, fill: false, stroke: true, evenOdd: false); break;
            case Fill or ObsoleteFill:
                PaintSubpath(gw, op, fill: true, stroke: false, evenOdd: false); break;
            case EOFill:
                PaintSubpath(gw, op, fill: true, stroke: false, evenOdd: true); break;
            case FillStroke or ClosePathFillStroke:
                PaintSubpath(gw, op, fill: true, stroke: true, evenOdd: false); break;
            case EOFillStroke or ClosePathEOFillStroke:
                PaintSubpath(gw, op, fill: true, stroke: true, evenOdd: true); break;

            case Clip or EOClip:
                // Record the clip path's construction range so a later element
                // move can translate the clip with the element (the clip takes
                // effect at the path-ending op that follows).
                if (gw.depth == 0 && PathStart(gw) >= 0)
                    gw.pendingClip = new GraphicsClipInfo(PathStart(gw), i, new Aspose.Pdf.Matrix(gw.gs.Ctm));
                if (CurrentPaths(gw) is { Count: > 0 } clipPaths)
                    gw.pendingRegion = new ClipRegion(clipPaths, op is EOClip, gw.gs.Clip);
                break;

            case EndPath: DiscardSubpath(gw); break;   // n — clip / no-paint: drop the path

            case Do d when gw.depth < MaxFormDepth:
                VisitForm(d, i, gw.resources, gw.reader, gw.gs, gw.elements, gw.depth, gw.editState);
                break;

            default: break;                    // colour/clip/text/etc. don't affect path geometry
        }
        return true;
    }
}

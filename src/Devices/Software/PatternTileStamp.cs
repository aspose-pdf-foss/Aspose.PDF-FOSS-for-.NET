using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Devices.Rasterizer;

namespace Aspose.Pdf.Devices;

public sealed partial class SoftwarePageRenderer
{
    /// <summary>The most pixels one rasterised pattern period may take; a larger one is painted
    /// cell by cell.</summary>
    private const long MaxPatternTilePixels = 4_000_000;

    /// <summary>Two pattern-space coordinates closer than this are the same.</summary>
    private const double SamePatternUnit = 1e-9;

    /// <summary>How many periods a cell's BBox may span and still be wrapped into the tile.</summary>
    private const int MaxPatternWrapCells = 8;

    /// <summary>
    /// Fill the masked region with a tiling pattern by rasterising ONE period of it - the
    /// pattern space window [0, XStep] x [0, YStep], every cell whose BBox reaches into it
    /// drawn there clipped to its BBox - at the device's own density along each pattern axis,
    /// and sampling that periodic tile for each device pixel through the inverse of the
    /// pattern-to-device map. Cells abut exactly and the lattice never drifts, at any tile
    /// count and under any pattern matrix; composited cell by cell, every anti-aliased cell
    /// edge left a light seam over the fill. False when the period is too large to rasterise,
    /// so the caller paints cell by cell.
    /// </summary>
    private static bool TryStampPeriodicTile(PatternFillState pf, RenderContext patternContext, GraphicsState? cellState)
    {
        var ctx = pf.ctx;
        var m = pf.m;
        // Pattern space to device pixels: (x, y) -> (a x + c y + e, b x + d y + f).
        double a = m[0] * ctx.Scale, b = -m[1] * ctx.Scale, c = m[2] * ctx.Scale, d = -m[3] * ctx.Scale;
        double e = (m[4] - ctx.MediaBox.LLX) * ctx.Scale, f = ctx.PixelH - (m[5] - ctx.MediaBox.LLY) * ctx.Scale;
        var det = a * d - b * c;
        if (Math.Abs(det) < SingularCtmDeterminant || pf.xStep <= 0 || pf.yStep <= 0) return false;
        var tw = (int)Math.Ceiling(pf.xStep * Math.Sqrt(a * a + b * b));
        var th = (int)Math.Ceiling(pf.yStep * Math.Sqrt(c * c + d * d));
        if (tw < 1) tw = 1;
        if (th < 1) th = 1;
        if ((long)tw * th > MaxPatternTilePixels) return false;

        var tile = RenderPatternPeriod(pf, patternContext, cellState, tw, th);
        if (tile is null) return false;

        // Device to pattern: the inverse map, stepped per pixel.
        double ia = d / det, ib = -b / det, ic = -c / det, id = a / det;
        double ie = -(e * ia + f * ic), iff = -(e * ib + f * id);
        var (x0, y0, x1, y1) = MaskBounds(pf.edgeTable, ctx);
        double cols = tw / pf.xStep, rows = th / pf.yStep;
        for (var y = y0; y < y1; y++)
        {
            var maskRow = y * ctx.PixelW;
            var py = y + 0.5;
            for (var x = x0; x < x1; x++)
            {
                var coverage = pf.mask[maskRow + x];
                if (coverage == 0) continue;
                var px = x + 0.5;
                var u = Wrap((px * ia + py * ic + ie) * cols, tw);
                var v = Wrap((px * ib + py * id + iff) * rows, th);
                var t = ((th - 1 - v) * tw + u) * 4;
                var alpha = tile[t + 3] * coverage / byte.MaxValue;
                if (alpha == 0) continue;
                SetPixel(ctx, x, y, tile[t], tile[t + 1], tile[t + 2], (byte)alpha);
            }
        }
        return true;
    }

    /// <summary>The tile pixel a pattern-space coordinate (already scaled to tile pixels) falls in, one period wrapped.</summary>
    private static int Wrap(double value, int period)
    {
        var i = (int)Math.Floor(value - Math.Floor(value / period) * period);
        return i >= period ? period - 1 : i;
    }

    /// <summary>One period of the pattern rasterised to <paramref name="tw"/> x <paramref name="th"/> RGBA
    /// pixels, pattern y up; null when a cell fails to draw.</summary>
    private static byte[]? RenderPatternPeriod(PatternFillState pf, RenderContext patternContext,
        GraphicsState? cellState, int tw, int th)
    {
        var tile = new byte[tw * th * 4];
        var tileCtx = new RenderContext(tile, tw, th, 1, new Rectangle(0, 0, tw, th), pf.ctx.Reader)
        {
            AllXObjects = patternContext.AllXObjects,
            FontDicts = patternContext.FontDicts,
            ConvertFontsToUnicodeTtf = patternContext.ConvertFontsToUnicodeTtf,
            Patterns = patternContext.Patterns,
            Shadings = patternContext.Shadings,
        };
        double sx = tw / pf.xStep, sy = th / pf.yStep;
        var box = pf.cellBBox ?? [0, 0, pf.xStep, pf.yStep];
        double bx0 = Math.Min(box[0], box[2]), bx1 = Math.Max(box[0], box[2]);
        double by0 = Math.Min(box[1], box[3]), by1 = Math.Max(box[1], box[3]);
        // The cells whose BBox, shifted by whole periods, reaches into the window, wherever the BBox
        // sits; a cell reaching across more periods than the wrap takes is painted cell by cell.
        double iFirst = Math.Floor(-bx1 / pf.xStep), iLast = Math.Ceiling((pf.xStep - bx0) / pf.xStep);
        double jFirst = Math.Floor(-by1 / pf.yStep), jLast = Math.Ceiling((pf.yStep - by0) / pf.yStep);
        if (iLast - iFirst > MaxPatternWrapCells || jLast - jFirst > MaxPatternWrapCells) return null;
        long iLo = (long)iFirst, iHi = (long)iLast, jLo = (long)jFirst, jHi = (long)jLast;
        var exactPeriod = Math.Abs(bx0) < SamePatternUnit && Math.Abs(by0) < SamePatternUnit
            && Math.Abs(bx1 - pf.xStep) < SamePatternUnit && Math.Abs(by1 - pf.yStep) < SamePatternUnit;
        try
        {
            for (var j = jLo; j <= jHi; j++)
            {
                for (var i = iLo; i <= iHi; i++)
                {
                    double ox = i * pf.xStep, oy = j * pf.yStep;
                    if (bx1 + ox <= 0 || bx0 + ox >= pf.xStep || by1 + oy <= 0 || by0 + oy >= pf.yStep) continue;
                    var clip = exactPeriod ? null : CellClip(tw, th, (bx0 + ox) * sx, (by0 + oy) * sy, (bx1 + ox) * sx, (by1 + oy) * sy);
                    RenderContent(pf.patternContent, tileCtx, pf.patExtG, [sx, 0, 0, sy, ox * sx, oy * sy], clip, inheritState: cellState);
                }
            }
        }
        catch
        {
            return null;
        }
        return tile;
    }

    /// <summary>A tile-sized stencil of a cell's BBox, given in tile pixels with y up.</summary>
    private static byte[] CellClip(int tw, int th, double x0, double y0, double x1, double y1)
    {
        var et = new EdgeTable();
        et.AddLine(x0, th - y0, x1, th - y0);
        et.AddLine(x1, th - y0, x1, th - y1);
        et.AddLine(x1, th - y1, x0, th - y1);
        et.AddLine(x0, th - y1, x0, th - y0);
        var mask = new byte[tw * th];
        ScanlineFiller.BuildMask(et, mask, tw, th, false);
        return mask;
    }

    /// <summary>The device pixel box of a path's edges, clamped to the page.</summary>
    private static (int X0, int Y0, int X1, int Y1) MaskBounds(EdgeTable edgeTable, RenderContext ctx)
    {
        double lx = double.MaxValue, ly = double.MaxValue, hx = double.MinValue, hy = double.MinValue;
        foreach (var edge in edgeTable.Edges)
        {
            var xEnd = edge.XAtYMin + (edge.YMax - edge.YMin) * edge.InvSlope;
            lx = Math.Min(lx, Math.Min(edge.XAtYMin, xEnd));
            hx = Math.Max(hx, Math.Max(edge.XAtYMin, xEnd));
            ly = Math.Min(ly, edge.YMin);
            hy = Math.Max(hy, edge.YMax);
        }
        if (hx < lx) return (0, 0, 0, 0);
        return (Compat.Clamp((int)Math.Floor(lx), 0, ctx.PixelW), Compat.Clamp((int)Math.Floor(ly), 0, ctx.PixelH),
            Compat.Clamp((int)Math.Ceiling(hx) + 1, 0, ctx.PixelW), Compat.Clamp((int)Math.Ceiling(hy) + 1, 0, ctx.PixelH));
    }
}

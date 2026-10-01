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
    private static void FillWithPattern(RenderContext ctx, EdgeTable edgeTable, bool evenOdd, string patternName, GraphicsState state)
    {
        var pf = new PatternFillState();
        pf.ctx = ctx;
        pf.edgeTable = edgeTable;
        pf.evenOdd = evenOdd;
        pf.patternName = patternName;
        pf.state = state;
        if (pf.ctx.Patterns?.Get(pf.patternName) is not { } patternObj) return;

        if (!ResolvePatternProgram(pf, patternObj)) return;
        pf.xStep = NumFrom(pf.pdict!.Get("XStep"));
        pf.yStep = NumFrom(pf.pdict.Get("YStep"));
        if (pf.xStep == 0) pf.xStep = 1;
        if (pf.yStep == 0) pf.yStep = 1;

        pf.patResources = pf.ctx.Reader.ResolveDict(pf.pdict.Get("Resources"));
        pf.patFonts = ResolveFontDicts(pf.patResources, pf.ctx.Reader);
        pf.patExtG = ResolveExtGStates(pf.patResources, pf.ctx.Reader);
        pf.patXObj = ResolveAllXObjects(pf.patResources, pf.ctx.Reader);
        if (pf.ctx.FontDicts is not null)
            foreach (var kv in pf.ctx.FontDicts) pf.patFonts.TryAdd(kv.Key, kv.Value);
        if (pf.ctx.AllXObjects is not null)
            foreach (var kv in pf.ctx.AllXObjects) pf.patXObj.TryAdd(kv.Key, kv.Value);

        if (!PaintPatternTiles(pf)) return;
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
            ConvertFontsToUnicodeTtf = patternContext.ConvertFontsToUnicodeTtf,
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

        // A pattern’s /Matrix maps pattern space to the page’s DEFAULT user space, not to
        // whatever CTM is in force at the fill (PDF 32000 §8.7.3.1). Composing the current
        // CTM applied every enclosing cm a SECOND time, which threw the shading right off
        // the page and left the fill blank - an SVG gradient converted to PDF paints under
        // three nested cm operators and vanished completely. The tiling branch had already
        // been corrected the same way; this is the shading half of it.
        var patMatrix = pdict.Get("Matrix") as PdfArray;
        var savedCtm = state.Ctm;
        var pageCtm = ctx.PageCtm ?? new double[] { 1, 0, 0, 1, 0, 0 };
        if (patMatrix is { Count: >= 6 })
        {
            var m = new double[6];
            for (var i = 0; i < 6; i++) m[i] = NumFrom(patMatrix[i]);
            state.Ctm = GraphicsState.MultiplyMatrices(m, pageCtm);
        }
        else
        {
            state.Ctm = pageCtm;
        }

        var savedClip = ctx.ClipMask;
        ctx.ClipMask = mask;
        try
        {
            switch (shading)
            {
                case FunctionBasedShading fn: DrawFunctionShading(ctx, fn, state); break;
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
    /// <summary>The tile index range a pattern fill must walk, and the uncapped tile count
    /// the caller uses to decide whether to stamp a rasterised tile instead.</summary>
    private readonly record struct PatternTileRange(int IMin, int IMax, int JMin, int JMax, long RawCount);

    private static PatternTileRange ComputePatternTileRange(EdgeTable edgeTable, RenderContext ctx, double[] m,
        double xStep, double yStep, double[]? cellBBox = null)
    {
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
        if (pxMin == int.MaxValue) return default;

        // Pixel → user space: inverse of (ctx.PixelH - (user_y - LLY) * Scale).
        double PxToUserX(double px) => px / ctx.Scale + ctx.MediaBox.LLX;
        double PxToUserY(double py) => (ctx.PixelH - py) / ctx.Scale + ctx.MediaBox.LLY;

        // Four corners of the user-space bbox.
        var uxs = new[] { PxToUserX(pxMin), PxToUserX(pxMax) };
        var uys = new[] { PxToUserY(pyMin), PxToUserY(pyMax) };

        // Invert pattern.Matrix (user → pattern). For an affine 2×2 with translation:
        // det=a*d-b*c; inv = [d/det, -b/det, -c/det, a/det, (c*f-d*e)/det, (b*e-a*f)/det].
        var det = m[0] * m[3] - m[1] * m[2];
        if (Math.Abs(det) < 1e-12) return default;
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

        int iMin, iMax, jMin, jMax;
        if (cellBBox is not null)
        {
            // Tile (i, j) paints the cell's BBox shifted by (i*XStep, j*YStep), so it can
            // reach the region when that shifted box overlaps it. Solving the overlap for i
            // gives the exact range; it degenerates to the old +/-1 window for the ordinary
            // cell that sits at the origin and is exactly one step across.
            var bx0 = Math.Min(cellBBox[0], cellBBox[2]);
            var bx1 = Math.Max(cellBBox[0], cellBBox[2]);
            var by0 = Math.Min(cellBBox[1], cellBBox[3]);
            var by1 = Math.Max(cellBBox[1], cellBBox[3]);
            iMin = (int)Math.Floor((pxs_min - bx1) / xStep);
            iMax = (int)Math.Ceiling((pxs_max - bx0) / xStep);
            jMin = (int)Math.Floor((pys_min - by1) / yStep);
            jMax = (int)Math.Ceiling((pys_max - by0) / yStep);
        }
        else
        {
            iMin = (int)Math.Floor(pxs_min / xStep) - 1;
            iMax = (int)Math.Ceiling(pxs_max / xStep) + 1;
            jMin = (int)Math.Floor(pys_min / yStep) - 1;
            jMax = (int)Math.Ceiling(pys_max / yStep) + 1;
        }

        // Unclamped tile count — lets the caller switch to a tile-and-stamp fill when a
        // fine pattern covers a large area (per-tile execution would be capped below and
        // leave most of the region unpainted).
        var rawCount = (long)(iMax - iMin + 1) * (jMax - jMin + 1);

        // Guard against runaway. This caps HOW MANY tiles are executed, not WHERE they
        // are: clamping the indices themselves to a fixed window round zero silently
        // inverted the range whenever the filled region lay outside it (a chart bar 600 pt
        // from the origin with an 8-unit step wants tiles 73..90, and Max(73,-64)=73 against
        // Min(90,64)=64 is an empty loop) - the fill then painted nothing at all. The pattern
        // origin is wherever the file puts it, so the budget has to travel with the region.
        const int MaxTilesPerAxis = 129;
        if (iMax - iMin + 1 > MaxTilesPerAxis) iMax = iMin + MaxTilesPerAxis - 1;
        if (jMax - jMin + 1 > MaxTilesPerAxis) jMax = jMin + MaxTilesPerAxis - 1;
        return new PatternTileRange(iMin, iMax, jMin, jMax, rawCount);
    }

    /// <summary>Read a numeric PdfObject (integer or real) into a double. Zero for other types.</summary>
    private static double NumFrom(PdfObject? o) => o switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0.0,
    };
}

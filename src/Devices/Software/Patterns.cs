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
            ConvertFontsToUnicodeTtf = ctx.ConvertFontsToUnicodeTtf,
            PdfXOverprintSim = ctx.PdfXOverprintSim,
            PageCtm = ctx.PageCtm,
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
        // The cell's /BBox is what a tile actually paints, and it need not sit at the
        // pattern origin: an SVG pattern in objectBoundingBox units converts to a cell
        // whose BBox starts 100 units out, and one whose BBox is WIDER than its step so
        // the tiles overlap. Deriving the index range from the region alone assumed a cell
        // at the origin no bigger than its step, and every index it produced painted
        // outside the filled square - the whole pattern came out blank.
        var cellBBox = pdict.Get("BBox") is PdfArray bbArr && bbArr.Count >= 4
            ? new[] { NumFrom(bbArr[0]), NumFrom(bbArr[1]), NumFrom(bbArr[2]), NumFrom(bbArr[3]) }
            : null;
        ComputePatternTileRange(edgeTable, ctx, m, xStep, yStep,
            out var iMin, out var iMax, out var jMin, out var jMax, out var rawCount, cellBBox);

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
                // The pattern matrix maps pattern space to the page's DEFAULT user
                // space (PDF 32000 §8.7.3.1) — it is independent of the CTM in force
                // when the fill runs. Composing state.Ctm here double-applied every
                // content transform (the stamp path above already treats the matrix
                // as default-space).
                // The stencil has to be handed in as the tile content's STARTING clip, not
                // just parked on the context: every draw hook re-reads the clip off the
                // graphics state, so a context-only mask is overwritten with null by the
                // first painting operator inside the cell and the tiles then spill past
                // the filled path (chart bars grew until they touched each other).
                RenderContent(patternContent, patternContext, patExtG, tileMatrix, mask);
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
    private static void ComputePatternTileRange(EdgeTable edgeTable, RenderContext ctx, double[] m,
        double xStep, double yStep, out int iMin, out int iMax, out int jMin, out int jMax)
        => ComputePatternTileRange(edgeTable, ctx, m, xStep, yStep, out iMin, out iMax, out jMin, out jMax, out _);

    private static void ComputePatternTileRange(EdgeTable edgeTable, RenderContext ctx, double[] m,
        double xStep, double yStep, out int iMin, out int iMax, out int jMin, out int jMax,
        out long rawCount, double[]? cellBBox = null)
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
        rawCount = (long)(iMax - iMin + 1) * (jMax - jMin + 1);

        // Guard against runaway. This caps HOW MANY tiles are executed, not WHERE they
        // are: clamping the indices themselves to a fixed window round zero silently
        // inverted the range whenever the filled region lay outside it (a chart bar 600 pt
        // from the origin with an 8-unit step wants tiles 73..90, and Max(73,-64)=73 against
        // Min(90,64)=64 is an empty loop) - the fill then painted nothing at all. The pattern
        // origin is wherever the file puts it, so the budget has to travel with the region.
        const int MaxTilesPerAxis = 129;
        if (iMax - iMin + 1 > MaxTilesPerAxis) iMax = iMin + MaxTilesPerAxis - 1;
        if (jMax - jMin + 1 > MaxTilesPerAxis) jMax = jMin + MaxTilesPerAxis - 1;
    }

    /// <summary>Read a numeric PdfObject (integer or real) into a double. Zero for other types.</summary>
    private static double NumFrom(PdfObject? o) => o switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0.0,
    };
}

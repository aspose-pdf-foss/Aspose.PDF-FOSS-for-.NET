using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using GdiColor = System.Drawing.Color;
using GdiMatrix = System.Drawing.Drawing2D.Matrix;
using GraphicsState = Aspose.Pdf.Content.GraphicsState;

namespace Aspose.Pdf.Devices;

public sealed partial class GdiPlusPageRenderer
{
    /// <summary>The most pixels one rasterised pattern period may take; a larger one is painted
    /// cell by cell.</summary>
    private const long MaxPatternTilePixels = 4_000_000;

    /// <summary>How many periods a cell's BBox may span and still be wrapped into the tile.</summary>
    private const int MaxPatternWrapCells = 8;

    /// <summary>Two pattern-space coordinates closer than this are the same.</summary>
    private const double SamePatternUnit = 1e-9;

    /// <summary>
    /// Fill the path with a tiling pattern by rasterising ONE period of it - the pattern-space
    /// window [0, XStep] x [0, YStep], every cell whose BBox reaches into it drawn there clipped
    /// to its BBox - at the device's own density along each pattern axis, and filling the path
    /// with a texture brush of that tile carried to the page through the pattern matrix. Cells
    /// abut exactly and the lattice never drifts, at any tile count and under any pattern
    /// matrix; composited cell by cell, every anti-aliased cell edge left a light seam over the
    /// fill, and a printer received one image draw per cell. False when the period is too large
    /// to rasterise, so the caller paints cell by cell.
    /// </summary>
    private bool TryFillWithPeriodicTile(TilingPatternFillState tl, GraphicsState? cellState)
    {
        if (tl.xstep <= 0 || tl.ystep <= 0) return false;
        using var patWorld = WorldMatrix(tl.patMatrix);
        var el = patWorld.Elements;
        double a = el[0], b = el[1], c = el[2], d = el[3];
        if (Math.Abs(a * d - b * c) < SamePatternUnit) return false;
        var tw = Math.Max(1, (int)Math.Ceiling(tl.xstep * Math.Sqrt(a * a + b * b)));
        var th = Math.Max(1, (int)Math.Ceiling(tl.ystep * Math.Sqrt(c * c + d * d)));
        if ((long)tw * th > MaxPatternTilePixels) return false;

        using var tile = new Bitmap(tw, th, PixelFormat.Format32bppArgb);
        if (!RenderPatternPeriod(tl, cellState, tile)) return false;

        // Tile pixel (u, v) is pattern (u * XStep / tw, YStep - v * YStep / th); the pattern
        // matrix carries that to the page.
        using var toPage = new GdiMatrix((float)(tl.xstep / tw), 0f, 0f, (float)(-tl.ystep / th), 0f, (float)tl.ystep);
        toPage.Multiply(patWorld, MatrixOrder.Append);
        using var devicePath = (GraphicsPath)tl.path.Clone();
        devicePath.Transform(tl.world);
        var savedTransform = _g.Transform;
        var savedInterpolation = _g.InterpolationMode;
        try
        {
            using var brush = new TextureBrush(tile) { WrapMode = WrapMode.Tile, Transform = toPage };
            _g.ResetTransform();
            // Each page pixel takes the one tile pixel it lands in, as the cell drew it.
            _g.InterpolationMode = InterpolationMode.NearestNeighbor;
            _g.FillPath(brush, devicePath);
        }
        finally
        {
            _g.InterpolationMode = savedInterpolation;
            _g.Transform = savedTransform;
            savedTransform.Dispose();
        }
        return true;
    }

    /// <summary>How far, in device pixels, a cell may run into the fill without reaching it: the
    /// neighbours of the cell under the fill share its edges.</summary>
    private const double CellEdgeSlack = 0.5;

    /// <summary>
    /// The one cell of the lattice that reaches the fill, as when a pattern carries one image to
    /// fill a shape with - the fill's outline, carried into pattern space, meets that cell's box
    /// alone - or null when none or several do. On a printer such a fill goes as that cell's own
    /// drawing commands. There is no abutting cell to leave a seam, and a texture brush reaches the
    /// printer as the period rasterised at the printer's density - an image cell resampled to
    /// several times its pixels for the driver to encode - where the cell sends its image once at
    /// its own size.
    /// </summary>
    private (int I, int J)? LoneReachingCell(TilingPatternFillState tl)
    {
        if (tl.xstep <= 0 || tl.ystep <= 0) return null;
        using var patWorld = WorldMatrix(tl.patMatrix);
        using var toPattern = patWorld.Clone();
        if (!toPattern.IsInvertible) return null;
        toPattern.Invert();
        using var pathToPattern = tl.world.Clone();
        pathToPattern.Multiply(toPattern, MatrixOrder.Append);
        var bounds = tl.path.GetBounds(pathToPattern);
        var el = patWorld.Elements;
        var slackX = Math.Min(CellEdgeSlack / Math.Sqrt(el[0] * el[0] + el[1] * el[1]), bounds.Width / 2.0);
        var slackY = Math.Min(CellEdgeSlack / Math.Sqrt(el[2] * el[2] + el[3] * el[3]), bounds.Height / 2.0);
        double bx0 = Math.Min(tl.bx0, tl.bx1), bx1 = Math.Max(tl.bx0, tl.bx1);
        double by0 = Math.Min(tl.by0, tl.by1), by1 = Math.Max(tl.by0, tl.by1);
        // Cell i spans [bx0 + i XStep, bx1 + i XStep]; it reaches the outline's extent when it
        // overlaps it by more than the slack, and likewise along y.
        var iLo = Math.Floor((bounds.Left + slackX - bx1) / tl.xstep) + 1;
        var iHi = Math.Ceiling((bounds.Right - slackX - bx0) / tl.xstep) - 1;
        var jLo = Math.Floor((bounds.Top + slackY - by1) / tl.ystep) + 1;
        var jHi = Math.Ceiling((bounds.Bottom - slackY - by0) / tl.ystep) - 1;
        if (iLo != iHi || jLo != jHi || iLo < tl.iMin || iLo > tl.iMax || jLo < tl.jMin || jLo > tl.jMax) return null;
        return ((int)iLo, (int)jLo);
    }

    /// <summary>Draw one period of the pattern into <paramref name="tile"/>, pattern y up; false
    /// when a cell fails to draw.</summary>
    private bool RenderPatternPeriod(TilingPatternFillState tl, GraphicsState? cellState, Bitmap tile)
    {
        double bx0 = Math.Min(tl.bx0, tl.bx1), bx1 = Math.Max(tl.bx0, tl.bx1);
        double by0 = Math.Min(tl.by0, tl.by1), by1 = Math.Max(tl.by0, tl.by1);
        // The cells whose BBox, shifted by whole periods, reaches into the window, wherever the BBox
        // sits; a cell reaching across more periods than the wrap takes is painted cell by cell.
        double iFirst = Math.Floor(-bx1 / tl.xstep), iLast = Math.Ceiling((tl.xstep - bx0) / tl.xstep);
        double jFirst = Math.Floor(-by1 / tl.ystep), jLast = Math.Ceiling((tl.ystep - by0) / tl.ystep);
        if (iLast - iFirst > MaxPatternWrapCells || jLast - jFirst > MaxPatternWrapCells) return false;
        long iLo = (long)iFirst, iHi = (long)iLast, jLo = (long)jFirst, jHi = (long)jLast;
        var exactPeriod = Math.Abs(bx0) < SamePatternUnit && Math.Abs(by0) < SamePatternUnit
            && Math.Abs(bx1 - tl.xstep) < SamePatternUnit && Math.Abs(by1 - tl.ystep) < SamePatternUnit;

        var savedG = _g; var savedBitmap = _bitmap; var savedScope = _scope;
        var savedScale = _scale; var savedScaleY = _scaleY; var savedPixelH = _pixelH;
        var savedMediaBox = _mediaBox; var savedAnnotCtm = _annotBaseCtm;
        var savedStack = _gdiStateStack.ToArray();
        _gdiStateStack.Clear();
        _formDepth++;
        try
        {
            using var tg = Graphics.FromImage(tile);
            tg.Clear(GdiColor.Transparent);
            tg.SmoothingMode = SmoothingMode.AntiAlias;
            tg.InterpolationMode = InterpolationMode.HighQualityBicubic;
            tg.PixelOffsetMode = PagePom;
            _g = tg; _bitmap = tile; _annotBaseCtm = null;
            _scale = tile.Width / tl.xstep; _scaleY = tile.Height / tl.ystep; _pixelH = tile.Height;
            _mediaBox = new Rectangle(0, 0, tl.xstep, tl.ystep);
            _scope = PatternScope(tl.pd, savedScope);
            for (var j = jLo; j <= jHi; j++)
            {
                for (var i = iLo; i <= iHi; i++)
                {
                    double ox = i * tl.xstep, oy = j * tl.ystep;
                    if (bx1 + ox <= 0 || bx0 + ox >= tl.xstep || by1 + oy <= 0 || by0 + oy >= tl.ystep) continue;
                    double[] cellCtm = [1, 0, 0, 1, ox, oy];
                    using var cellClip = exactPeriod ? null : BuildBBoxClip(tl.pd, cellCtm);
                    var cellGdi = SaveSurface();
                    try { RenderContentStream(tl.content, cellCtm, cellClip, cellState); }
                    finally { _gdiStateStack.Clear(); RestoreSurface(cellGdi); }
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            _g = savedG; _bitmap = savedBitmap; _scope = savedScope;
            _scale = savedScale; _scaleY = savedScaleY; _pixelH = savedPixelH;
            _mediaBox = savedMediaBox; _annotBaseCtm = savedAnnotCtm;
            _gdiStateStack.Clear();
            for (int k = savedStack.Length - 1; k >= 0; k--) _gdiStateStack.Push(savedStack[k]);
            _formDepth--;
        }
    }
}

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

public sealed partial class GdiPlusPageRenderer : IPageRenderer
{
    // ── Paths ───────────────────────────────────────────────────────

    private void DrawPath(string op, GraphicsState state, IReadOnlyList<PathCommand> segments)
    {
        if (segments.Count == 0) return;
        bool doFill = op is "f" or "F" or "f*" or "B" or "B*" or "b" or "b*";
        bool doStroke = op is "S" or "s" or "B" or "B*" or "b" or "b*";
        bool evenOdd = op is "f*" or "B*" or "b*";
        if (!doFill && !doStroke) return;

        using var path = BuildPath(segments, evenOdd);
        var saved = _g.Transform;
        using var world = WorldMatrix(state.Ctm);
        _g.Transform = world;
        try
        {
            // Corrupt-geometry tolerance: a path whose device bounds are non-finite
            // or astronomically beyond the page (damaged content streams carry
            // run-together coordinates like "4787938" or 5.7e17) overflows GDI+'s
            // scan conversion and smears garbage across the page. Skip the paint —
            // treat such ops as if they were absent.
            var pathBounds = path.GetBounds(world);
            var pageSpan = Math.Max(_bitmap.Width, _bitmap.Height);
            var sanity = Math.Max(1e5f, pageSpan * 64f);
            if (!float.IsFinite(pathBounds.X) || !float.IsFinite(pathBounds.Y)
                || !float.IsFinite(pathBounds.Width) || !float.IsFinite(pathBounds.Height)
                || pathBounds.Width > sanity || pathBounds.Height > sanity
                || Math.Abs(pathBounds.X) > sanity || Math.Abs(pathBounds.Y) > sanity)
                return;

            if (doFill && state.FillPatternName is null)
            {
                var blend = Rasterizer.BlendModes.Parse(state.BlendMode);
                var softMask = state.SoftMask is { } sm ? GetSoftMaskAlpha(sm) : null;
                if (softMask is not null)
                {
                    // An ExtGState soft mask modulates this fill's coverage per pixel
                    // (PDF 32000 §11.6.5.4); composite by hand through the mask's alpha.
                    FillPathBlended(path, world, blend, state, softMask);
                }
                else if (blend != Rasterizer.BlendMode.Normal)
                {
                    // GDI+ has no per-pixel PDF blend modes, so composite the fill into
                    // the backing bitmap by hand (PDF 32000 §11.3.5). Scoped to the rare
                    // non-Normal case; Normal fills keep the fast native path below.
                    FillPathBlended(path, world, blend, state);
                }
                else
                {
                    using var brush = new SolidBrush(ColorFrom(state.FillR, state.FillG, state.FillB, state.FillAlpha));
                    // A fill whose device extent is thinner than a pixel in one axis (e.g. a
                    // 0.06pt rectangle used as a form-field underline) dissolves into faint
                    // anti-aliasing and reads as "missing". Draw it as a solid >=1px bar so
                    // hairline rules stay visible, the way print/GDI rendering draws them.
                    var db = pathBounds;
                    if (db.Width > 0f && db.Height > 0f && (db.Width < 1f || db.Height < 1f))
                    {
                        var cur = _g.Transform;
                        _g.ResetTransform();
                        _g.FillRectangle(brush, db.X, db.Y, Math.Max(db.Width, 1f), Math.Max(db.Height, 1f));
                        _g.Transform = cur;
                        cur.Dispose();
                    }
                    else
                    {
                        // Blend a semi-transparent fill in straight sRGB.
                        // The page keeps gamma-corrected compositing for text AA;
                        // applying it to /ca fills composites them visibly lighter than the
                        // platform convention. Scoped to the shape-fill call so glyph
                        // rendering is unaffected.
                        // A near-white opaque fill is composited the same way: gamma-corrected
                        // coverage blending darkens even a white-over-white anti-aliased edge
                        // by one level (a white redaction rect over a white scan reads 0xFEFEFE
                        // at its border); a straight-sRGB blend of coverage α is α·255+(1−α)·255
                        // = 255 exactly, so the halo disappears.
                        bool nearWhiteFill = state.FillR > 0.99 && state.FillG > 0.99 && state.FillB > 0.99;
                        var savedCq = _g.CompositingQuality;
                        if (state.FillAlpha < 0.999 || nearWhiteFill) _g.CompositingQuality = CompositingQuality.AssumeLinear;
                        if (AliasedVectorFills)
                        {
                            // Aliased fill rule: run start = ceil(edge·s) inclusive-on-
                            // exact, run end = ceil(edge·s) exclusive, at the EXACT
                            // dpi/72 device mapping (AliasedWorldMatrix). GDI+'s non-AA
                            // rasterization under PixelOffsetMode.None implements that
                            // corner-lattice rule natively (calibrated on a 220-dpi bar
                            // page: 43/43 edges + the vertical runs land exactly); the
                            // Q_BCSHIFT/Q_BCPOM knobs exist to recalibrate the rule
                            // if a counter-example shows up.
                            var savedSm2 = _g.SmoothingMode;
                            var savedPom2 = _g.PixelOffsetMode;
                            var savedTx2 = _g.Transform;
                            _g.SmoothingMode = SmoothingMode.None;
                            _g.PixelOffsetMode = BcPomNone ? PixelOffsetMode.None : PixelOffsetMode.Half;
                            using (var exactWorld = AliasedWorldMatrix(state.Ctm))
                            {
                                exactWorld.Translate(BcShift.dx, BcShift.dy, MatrixOrder.Append);
                                _g.Transform = exactWorld;
                                _g.FillPath(brush, path);
                            }
                            _g.Transform = savedTx2;
                            savedTx2.Dispose();
                            _g.SmoothingMode = savedSm2;
                            _g.PixelOffsetMode = savedPom2;
                        }
                        else
                            _g.FillPath(brush, path);
                        _g.CompositingQuality = savedCq;
                    }
                }
            }
            else if (doFill && state.FillPatternName is not null)
            {
                FillWithTilingPattern(path, state, world);
            }

            if (doStroke && state.StrokePatternName is null)
            {
                // Corrupt-stroke tolerance: a damaged stream can turn "0.35 w" into
                // "735 w"; a pen wider than half the page blots out everything it
                // touches. No real design strokes with such a pen — skip the
                // damaged op entirely.
                var ctmScale = Math.Sqrt(Math.Abs(
                    state.Ctm[0] * state.Ctm[3] - state.Ctm[1] * state.Ctm[2])) * _scale;
                var devPen = state.LineWidth * (ctmScale > 0 ? ctmScale : _scale);
                if (devPen > 0.5 * pageSpan)
                    return;

                using var pen = BuildPen(state);
                // On transparency pages, composite the stroke in straight sRGB
                // — the page default (HighQuality = gamma-corrected) blends
                // partial-coverage stroke pixels visibly lighter over a painted backdrop, the
                // same reason the semi-transparent fill branch already forces AssumeLinear.
                var savedScq = _g.CompositingQuality;
                if (StrokeLinear) _g.CompositingQuality = CompositingQuality.AssumeLinear;
                // PDF stroking widens the path in user space, so under a non-uniform CTM
                // the pen is elliptical in device space: a vertical line under
                // `3.4 0 0 0.29 0 0 cm` must come out 3.4× wider than its nominal width
                // (manual-gradient bands drawn as adjacent stroked lines rely on this to
                // tile into a solid fill). GDI+ scales a pen by a single factor only, so
                // such strokes render several times too thin. For strongly anisotropic
                // CTMs, widen the path in user space ourselves and fill the outline —
                // the world transform then maps it exactly as the CTM dictates.
                bool drawnWidened = false;
                if (Environment.GetEnvironmentVariable("Q_ANISO") != "0"
                    && state.LineWidth > 0 && CtmAnisotropy(state.Ctm) > 1.5)
                {
                    using var widened = (GraphicsPath)path.Clone();
                    try
                    {
                        widened.Widen(pen);
                        using var sb = new SolidBrush(pen.Color);
                        _g.FillPath(sb, widened);
                        drawnWidened = true;
                    }
                    catch
                    {
                        // Widen can reject degenerate subpaths — fall back to the pen.
                    }
                }
                if (!drawnWidened)
                    _g.DrawPath(pen, path);
                _g.CompositingQuality = savedScq;
            }
        }
        finally { _g.Transform = saved; }
    }

    /// <summary>
    /// Fill <paramref name="path"/> with a Type-1 tiling pattern (PDF 32000 §8.7.3.1):
    /// clip to the path, then execute the pattern cell's content stream at every tile
    /// position (stepped by XStep/YStep, placed by the pattern matrix in the page's
    /// default coordinate system). Uncoloured (PaintType 2) patterns paint with the
    /// current fill colour; coloured ones carry their own colour operators.
    /// </summary>
    private void FillWithTilingPattern(GraphicsPath path, GraphicsState state, GdiMatrix world)
    {
        if (_formDepth > 24 || _scope.Patterns is null || state.FillPatternName is null) return;
        var patObj = _reader.Resolve(_scope.Patterns.Get(state.FillPatternName));
        // PatternType 2 (shading pattern, PDF 32000 §8.7.4.3) is a plain dict referencing a
        // /Shading — fill the path with that shading under the pattern matrix.
        if (patObj is PdfDictionary spd && (int)spd.GetInt("PatternType") == 2)
        {
            FillWithShadingPattern(path, state, spd);
            return;
        }
        if (patObj is not PdfStream patStream) return;
        var pd = patStream.Dict;
        if ((int)pd.GetInt("PatternType") != 1) return;
        if (pd.Get("BBox") is not PdfArray bbox || bbox.Count < 4) return;
        double bx0 = NumFrom(bbox[0]), by0 = NumFrom(bbox[1]), bx1 = NumFrom(bbox[2]), by1 = NumFrom(bbox[3]);
        double xstep = NumFrom(pd.Get("XStep")), ystep = NumFrom(pd.Get("YStep"));
        if (Math.Abs(xstep) < 1e-6) xstep = bx1 - bx0;
        if (Math.Abs(ystep) < 1e-6) ystep = by1 - by0;
        if (Math.Abs(xstep) < 1e-6 || Math.Abs(ystep) < 1e-6) return;
        var patMatrix = ExtractFormMatrix(pd) ?? new double[] { 1, 0, 0, 1, 0, 0 };

        byte[] content;
        try { content = _reader.DecodeStream(patStream); } catch { return; }
        if (content.Length == 0) return;

        using var patWorld = WorldMatrix(patMatrix);
        using var inv = patWorld.Clone();
        if (!inv.IsInvertible) return;
        inv.Invert();

        // Bound the tiling loop: map the fill region's device bounds into pattern space.
        var db = path.GetBounds(world);
        var corners = new[]
        {
            new PointF(db.Left, db.Top), new PointF(db.Right, db.Top),
            new PointF(db.Left, db.Bottom), new PointF(db.Right, db.Bottom),
        };
        inv.TransformPoints(corners);
        float pMinX = corners[0].X, pMaxX = corners[0].X, pMinY = corners[0].Y, pMaxY = corners[0].Y;
        foreach (var c in corners)
        {
            pMinX = Math.Min(pMinX, c.X); pMaxX = Math.Max(pMaxX, c.X);
            pMinY = Math.Min(pMinY, c.Y); pMaxY = Math.Max(pMaxY, c.Y);
        }
        int iMin = (int)Math.Floor((pMinX - bx1) / xstep), iMax = (int)Math.Ceiling((pMaxX - bx0) / xstep);
        int jMin = (int)Math.Floor((pMinY - by1) / ystep), jMax = (int)Math.Ceiling((pMaxY - by0) / ystep);
        if (iMax < iMin || jMax < jMin) return;
        if ((long)(iMax - iMin + 1) * (jMax - jMin + 1) > 8000)
        {
            // Too many tiles to execute the cell per-tile (a fine screen/dither over a
            // large area). Rasterise one cell to a device-sized tile and let GDI+ repeat
            // it with a TextureBrush instead of bailing (which would leave the region
            // blank). Scoped to the over-guard case, so the exact per-tile path for
            // normal-sized fills is unchanged.
            FillWithTiledBrush(path, pd, content, bx0, by0, bx1, by1, xstep, ystep, patMatrix, world);
            return;
        }

        // A pattern fill that carries transparency — group fill-alpha (/ca < 1), an active
        // ExtGState soft mask, or a non-Normal blend mode — must composite as a unit
        // (PDF 32000 §11.6.5-6): render the tiles onto a transparent layer, then blend that
        // layer onto the page once at the fill alpha / mask / blend. Painting the cells
        // straight onto the page (the common opaque case) ignores the alpha and over-inks
        // the region — e.g. a faded content panel drawn as an opaque dark overlay.
        bool needsComposite = state.FillAlpha < 0.999
            || state.SoftMask is not null
            || (!string.IsNullOrEmpty(state.BlendMode) && state.BlendMode != "Normal");

        if (!needsComposite)
        {
            RenderTilingCells(path, pd, content, patMatrix, xstep, ystep, iMin, iMax, jMin, jMax);
            return;
        }

        int pw = _bitmap.Width, ph = _bitmap.Height;
        int rx0 = Math.Max(0, (int)Math.Floor(db.Left)), ry0 = Math.Max(0, (int)Math.Floor(db.Top));
        int rx1 = Math.Min(pw, (int)Math.Ceiling(db.Right)), ry1 = Math.Min(ph, (int)Math.Ceiling(db.Bottom));
        if (rx1 <= rx0 || ry1 <= ry0) return; // fill maps off-page
        var compRect = new System.Drawing.Rectangle(rx0, ry0, rx1 - rx0, ry1 - ry0);

        // Capture the inherited device-space clip so the pattern fill stays bounded.
        var savedClipT = _g.Transform;
        _g.ResetTransform();
        var deviceClip = _g.Clip;
        _g.Transform = savedClipT;
        savedClipT.Dispose();

        var layer = RentLayer(pw, ph);
        var savedLG = _g; var savedLBmp = _bitmap; var savedLScratch = _blendScratch;
        var lg = Graphics.FromImage(layer);
        try
        {
            // Isolated source: start from a transparent backdrop under the fill rect.
            lg.CompositingMode = CompositingMode.SourceCopy;
            using (var clear = new SolidBrush(GdiColor.Transparent))
                lg.FillRectangle(clear, compRect);
            lg.CompositingMode = CompositingMode.SourceOver;
            lg.SmoothingMode = savedLG.SmoothingMode;
            lg.PixelOffsetMode = savedLG.PixelOffsetMode;
            lg.InterpolationMode = savedLG.InterpolationMode;
            lg.TextRenderingHint = savedLG.TextRenderingHint;
            lg.CompositingQuality = savedLG.CompositingQuality;
            if (deviceClip is not null) lg.Clip = deviceClip;
            // RenderTilingCells reads `path` in user space then drops to identity, so the
            // layer transform must be the fill CTM (= world) when it sets the clip.
            using (var wclone = world.Clone()) lg.Transform = wclone;

            _g = lg; _bitmap = layer; _blendScratch = null;
            RenderTilingCells(path, pd, content, patMatrix, xstep, ystep, iMin, iMax, jMin, jMax);
            _g.Flush();
        }
        finally
        {
            _g = savedLG; _bitmap = savedLBmp;
            _blendScratch?.Dispose(); _blendScratch = savedLScratch;
            lg.Dispose(); deviceClip?.Dispose();
        }

        CompositeGroupLayer(layer, state, state.BlendMode, compRect);
        _layerPool.Push(layer);
    }

    /// <summary>
    /// Execute a Type-1 tiling-pattern cell at every tile offset within the fill region,
    /// painting onto the current <see cref="_g"/> (the page, or a transparency layer for the
    /// alpha/soft-mask/blend path). On entry the device transform is the fill's CTM and
    /// <paramref name="path"/> is in user space, so the clip is set first (mapping the path
    /// to device space) and the transform then dropped to identity — each tile carries its
    /// own placement through the pattern coordinate system. One Save/Restore brackets the
    /// whole fill and the graphics-state stack is swapped out for the duration, so an
    /// unbalanced q/Q or a stray clip inside a cell cannot leak out.
    /// </summary>
    private void RenderTilingCells(GraphicsPath path, PdfDictionary pd, byte[] content,
        double[] patMatrix, double xstep, double ystep, int iMin, int iMax, int jMin, int jMax)
    {
        _formDepth++;
        var savedScope = _scope;
        var savedGdi = _g.Save();
        var savedStack = _gdiStateStack.ToArray(); // top-first
        _gdiStateStack.Clear();
        var savedSm = _g.SmoothingMode;
        try
        {
            if (Environment.GetEnvironmentVariable("Q_PATALIAS") == "1")
                _g.SmoothingMode = SmoothingMode.None;
            _g.SetClip(path, CombineMode.Intersect);
            _g.ResetTransform();

            var patScope = BuildScope(_reader.ResolveDict(pd.Get("Resources")));
            MergeInto(patScope.XObjects, savedScope.XObjects);
            MergeInto(patScope.Fonts, savedScope.Fonts);
            MergeInto(patScope.ExtGStates, savedScope.ExtGStates);
            patScope.Patterns ??= savedScope.Patterns;
            patScope.Shadings ??= savedScope.Shadings;
            patScope.ColorSpaces ??= savedScope.ColorSpaces;
            patScope.Properties ??= savedScope.Properties;
            _scope = patScope;

            for (int j = jMin; j <= jMax; j++)
            {
                for (int i = iMin; i <= iMax; i++)
                {
                    var cellCtm = GraphicsState.MultiplyMatrices(
                        new double[] { 1, 0, 0, 1, i * xstep, j * ystep }, patMatrix);
                    var cellClip = BuildBBoxClip(pd, cellCtm);
                    var cellGdi = _g.Save();
                    try { RenderContentStream(content, cellCtm, cellClip); }
                    catch { /* one bad tile must not abort the fill */ }
                    finally { _gdiStateStack.Clear(); _g.Restore(cellGdi); cellClip?.Dispose(); }
                }
            }
        }
        finally
        {
            _scope = savedScope;
            _gdiStateStack.Clear();
            for (int k = savedStack.Length - 1; k >= 0; k--) _gdiStateStack.Push(savedStack[k]);
            _g.Restore(savedGdi);
            _g.SmoothingMode = savedSm;
            _formDepth--;
        }
    }

    /// <summary>
    /// Rasterise a single tiling-pattern cell to a device-sized bitmap and fill
    /// <paramref name="path"/> by repeating it with a GDI+ <see cref="TextureBrush"/>.
    /// Used for fills whose tile count is too large to execute the cell per-tile.
    /// Only axis-aligned pattern matrices are handled (the common case); a rotated or
    /// skewed pattern falls through to no fill, exactly as the per-tile guard did.
    /// </summary>
    private void FillWithTiledBrush(GraphicsPath path, PdfDictionary pd, byte[] content,
        double bx0, double by0, double bx1, double by1, double xstep, double ystep,
        double[] patMatrix, GdiMatrix world)
    {
        using var patWorld = WorldMatrix(patMatrix);
        var e = patWorld.Elements; // [m11, m12, m21, m22, dx, dy]
        if (Math.Abs(e[1]) > 1e-3 || Math.Abs(e[2]) > 1e-3) return; // not axis-aligned
        double txDev = Math.Abs(e[0]) * xstep, tyDev = Math.Abs(e[3]) * ystep;
        int tw = (int)Math.Round(txDev), th = (int)Math.Round(tyDev);
        if (tw < 1) tw = 1;
        if (th < 1) th = 1;
        if ((long)tw * th > 4_000_000) return; // tile itself too large — give up safely

        using var tile = new Bitmap(tw, th, PixelFormat.Format32bppArgb);

        // Render one cell into the tile via a field swap: map the pattern cell's BBox
        // onto the whole tile bitmap (device Y-down), then restore everything.
        var savedG = _g; var savedBitmap = _bitmap; var savedScope = _scope;
        var savedScale = _scale; var savedScaleY = _scaleY; var savedPixelH = _pixelH;
        var savedMediaBox = _mediaBox; var savedStack = _gdiStateStack.ToArray();
        _gdiStateStack.Clear();
        _formDepth++;
        try
        {
            using var tg = Graphics.FromImage(tile);
            tg.Clear(GdiColor.Transparent);
            tg.SmoothingMode = SmoothingMode.AntiAlias;
            tg.InterpolationMode = InterpolationMode.HighQualityBicubic;
            tg.PixelOffsetMode = PagePom;
            _g = tg; _bitmap = tile;
            _scale = tw / xstep; _scaleY = th / ystep; _pixelH = th;
            _mediaBox = new Rectangle(bx0, by0, bx0 + xstep, by0 + ystep);

            var patScope = BuildScope(_reader.ResolveDict(pd.Get("Resources")));
            MergeInto(patScope.XObjects, savedScope.XObjects);
            MergeInto(patScope.Fonts, savedScope.Fonts);
            MergeInto(patScope.ExtGStates, savedScope.ExtGStates);
            patScope.Patterns ??= savedScope.Patterns;
            patScope.Shadings ??= savedScope.Shadings;
            patScope.ColorSpaces ??= savedScope.ColorSpaces;
            patScope.Properties ??= savedScope.Properties;
            _scope = patScope;

            try { RenderContentStream(content, new double[] { 1, 0, 0, 1, 0, 0 }, null); }
            catch { return; }
        }
        finally
        {
            _g = savedG; _bitmap = savedBitmap; _scope = savedScope;
            _scale = savedScale; _scaleY = savedScaleY; _pixelH = savedPixelH;
            _mediaBox = savedMediaBox;
            _gdiStateStack.Clear();
            for (int k = savedStack.Length - 1; k >= 0; k--) _gdiStateStack.Push(savedStack[k]);
            _formDepth--;
        }

        // Device point of the cell's top-left corner (pattern (bx0, by1)); the tile's
        // pixel (0,0) maps there, so anchor the brush at that lattice origin and tile.
        var anchor = new[] { new PointF((float)bx0, (float)by1) };
        patWorld.TransformPoints(anchor);

        using var devicePath = (GraphicsPath)path.Clone();
        devicePath.Transform(world);
        var savedTransform = _g.Transform;
        try
        {
            using var brush = new TextureBrush(tile) { WrapMode = WrapMode.Tile };
            brush.TranslateTransform(anchor[0].X, anchor[0].Y);
            _g.ResetTransform();
            _g.FillPath(brush, devicePath);
        }
        finally { _g.Transform = savedTransform; }
    }

    /// <summary>
    /// Fill <paramref name="path"/> with a PatternType 2 shading pattern (PDF 32000
    /// §8.7.4.3): clip to the path and paint the pattern's /Shading under the pattern
    /// matrix, which maps shading space to the page's default coordinate system. Reuses
    /// the axial/radial/mesh shading painters; function-based shadings are skipped.
    /// </summary>
    private void FillWithShadingPattern(GraphicsPath path, GraphicsState state, PdfDictionary pd)
    {
        var shadingObj = pd.Get("Shading");
        if (shadingObj is null) return;
        var shading = ShadingBase.Parse(shadingObj, _reader);
        if (shading is not (AxialShading or RadialShading or FreeFormGouraudShading
            or LatticeFormGouraudShading or CoonsPatchShading or TensorPatchShading)) return;

        var patMatrix = ExtractFormMatrix(pd) ?? new double[] { 1, 0, 0, 1, 0, 0 };

        // A shading fill under transparency — /ca < 1, an ExtGState soft mask, or a
        // non-Normal blend — must composite as a unit (PDF 32000 §11.6.5-6), exactly
        // like the tiling-pattern path above: paint the shading opaquely into a scratch
        // layer, then blend the layer onto the page through the alpha / mask / blend.
        // Painting it straight onto the page ignores the mask — e.g. a full-page
        // gradient overlay whose luminosity mask makes it near-invisible would white
        // out everything painted before it.
        bool needsComposite = state.FillAlpha < 0.999
            || state.SoftMask is not null
            || (!string.IsNullOrEmpty(state.BlendMode) && state.BlendMode != "Normal");
        if (needsComposite)
        {
            FillShadingComposited(path, state, shading, patMatrix);
            return;
        }

        var ps = new GraphicsState { FillAlpha = state.FillAlpha };
        ps.Ctm = patMatrix;

        // On entry the device transform is the fill's CTM and `path` is in user space, so
        // SetClip maps it to device space. The shading painters set their own transform
        // (the pattern matrix) and fill the clip bounds, which GDI+ intersects with this
        // path clip. One Save/Restore keeps the page clip/transform untouched.
        var savedGdi = _g.Save();
        try
        {
            _g.SetClip(path, CombineMode.Intersect);
            if (shading is AxialShading ax) DrawAxialShading(ax, ps, fillRegion: true);
            else if (shading is RadialShading ra) DrawRadialShading(ra, ps);
            else DrawMeshShading(shading, ps);
        }
        finally { _g.Restore(savedGdi); }
    }

    /// <summary>Render a shading-pattern fill into a transparent layer and composite it
    /// onto the page through the state's fill alpha / soft mask / blend mode.</summary>
    private void FillShadingComposited(GraphicsPath path, GraphicsState state,
        ShadingBase shading, double[] patMatrix)
    {
        int pw = _bitmap.Width, ph = _bitmap.Height;
        using var world = _g.Transform; // fill CTM (installed by DrawPath)
        var db = path.GetBounds(world);
        int rx0 = Math.Max(0, (int)Math.Floor(db.Left)), ry0 = Math.Max(0, (int)Math.Floor(db.Top));
        int rx1 = Math.Min(pw, (int)Math.Ceiling(db.Right)), ry1 = Math.Min(ph, (int)Math.Ceiling(db.Bottom));
        if (rx1 <= rx0 || ry1 <= ry0) return; // fill maps off-page
        var compRect = new System.Drawing.Rectangle(rx0, ry0, rx1 - rx0, ry1 - ry0);

        // Capture the inherited device-space clip so the fill stays bounded.
        var savedClipT = _g.Transform;
        _g.ResetTransform();
        var deviceClip = _g.Clip;
        _g.Transform = savedClipT;
        savedClipT.Dispose();

        // The layer is painted opaquely; CompositeGroupLayer applies the alpha and mask.
        var ps = new GraphicsState { FillAlpha = 1.0 };
        ps.Ctm = patMatrix;

        var layer = RentLayer(pw, ph);
        var savedLG = _g; var savedLBmp = _bitmap; var savedLScratch = _blendScratch;
        var lg = Graphics.FromImage(layer);
        try
        {
            lg.CompositingMode = CompositingMode.SourceCopy;
            using (var clear = new SolidBrush(GdiColor.Transparent))
                lg.FillRectangle(clear, compRect);
            lg.CompositingMode = CompositingMode.SourceOver;
            lg.SmoothingMode = savedLG.SmoothingMode;
            lg.PixelOffsetMode = savedLG.PixelOffsetMode;
            lg.InterpolationMode = savedLG.InterpolationMode;
            lg.TextRenderingHint = savedLG.TextRenderingHint;
            lg.CompositingQuality = savedLG.CompositingQuality;
            if (deviceClip is not null) lg.Clip = deviceClip;
            using (var wclone = world.Clone()) lg.Transform = wclone;

            _g = lg; _bitmap = layer; _blendScratch = null;
            _g.SetClip(path, CombineMode.Intersect);
            if (shading is AxialShading ax) DrawAxialShading(ax, ps, fillRegion: true);
            else if (shading is RadialShading ra) DrawRadialShading(ra, ps);
            else DrawMeshShading(shading, ps);
            _g.Flush();
        }
        finally
        {
            _g = savedLG; _bitmap = savedLBmp;
            _blendScratch?.Dispose(); _blendScratch = savedLScratch;
            lg.Dispose(); deviceClip?.Dispose();
        }

        CompositeGroupLayer(layer, state, state.BlendMode, compRect);
        _layerPool.Push(layer);
    }

    /// <summary>
    /// Composite a path fill under a non-Normal PDF blend mode (PDF 32000 §11.3.5).
    /// GDI+ brushes can't express Multiply/Screen/Darken/etc., so the shape is painted
    /// opaquely into a scratch layer (honouring the current transform and clip, so its
    /// anti-aliased coverage matches a native fill), then composited into the backing
    /// bitmap per pixel: out = dst·(1−a) + B(dst,src)·a, where a = coverage·/ca.
    /// </summary>
    /// <summary>
    /// Render an ExtGState soft-mask group to a page-sized 8-bit alpha buffer (cached by
    /// mask-group object), reusing the software renderer's mask rasteriser. The buffer is
    /// aligned to this renderer's pixel grid so it can modulate fills/images per pixel.
    /// </summary>
    private byte[]? GetSoftMaskAlpha(Aspose.Pdf.Content.SoftMaskInfo sm)
    {
        var g = sm.Dict.Get("G");
        int key = g is PdfIndirectRef gr
            ? gr.ObjectNumber
            : -System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(sm.Dict);
        if (_softMaskCache.TryGetValue(key, out var cached)) return cached;
        byte[]? alpha = null;
        try { alpha = SoftwarePageRenderer.RenderSoftMaskAlpha(_reader, _bitmap.Width, _bitmap.Height, _scale, _mediaBox, sm); }
        catch { alpha = null; }
        _softMaskCache[key] = alpha;
        return alpha;
    }

    private void FillPathBlended(GraphicsPath path, GdiMatrix world, Rasterizer.BlendMode mode, GraphicsState state,
        byte[]? softMask = null)
    {
        int w = _bitmap.Width, h = _bitmap.Height;
        var rb = path.GetBounds(world);
        int x0 = System.Math.Max(0, (int)System.Math.Floor(rb.Left));
        int y0 = System.Math.Max(0, (int)System.Math.Floor(rb.Top));
        int x1 = System.Math.Min(w, (int)System.Math.Ceiling(rb.Right));
        int y1 = System.Math.Min(h, (int)System.Math.Ceiling(rb.Bottom));
        if (x1 <= x0 || y1 <= y0) return;

        int sr = Clamp255(state.FillR), sg = Clamp255(state.FillG), sb = Clamp255(state.FillB);
        double ca = state.FillAlpha;
        if (ca <= 0.0) return;

        // Paint the shape opaquely into the scratch layer with the same transform/clip.
        _blendScratch ??= new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var sgfx = Graphics.FromImage(_blendScratch))
        {
            sgfx.Clear(GdiColor.Transparent);
            sgfx.SmoothingMode = SmoothingMode.AntiAlias;
            sgfx.PixelOffsetMode = PagePom;
            sgfx.Transform = world;
            sgfx.Clip = _g.Clip;
            using var brush = new SolidBrush(GdiColor.FromArgb(255, sr, sg, sb));
            sgfx.FillPath(brush, path);
        }

        _g.Flush();
        var rect = new System.Drawing.Rectangle(x0, y0, x1 - x0, y1 - y0);
        var dst = _bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var src = _blendScratch.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            // LockBits with a sub-rectangle returns Scan0 at the rect origin but the full
            // image stride, so copy only the rect's row width (not the whole stride) or a
            // row that starts past column 0 overruns the buffer on the final rows.
            int rowBytes = rect.Width * 4;
            var drow = new byte[rowBytes];
            var srow = new byte[rowBytes];
            for (int y = 0; y < rect.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(dst.Scan0 + y * dst.Stride, drow, 0, rowBytes);
                System.Runtime.InteropServices.Marshal.Copy(src.Scan0 + y * src.Stride, srow, 0, rowBytes);
                bool dirty = false;
                for (int x = 0; x < rect.Width; x++)
                {
                    int i = x * 4;
                    int cov = srow[i + 3];           // BGRA: scratch coverage
                    if (cov == 0) continue;
                    double a = cov / 255.0 * ca;
                    if (softMask is not null) a *= softMask[(y0 + y) * w + (x0 + x)] / 255.0;
                    if (a <= 0.0) continue;
                    int db = drow[i], dg = drow[i + 1], dr = drow[i + 2];
                    double dn = drow[i + 3] / 255.0; // backdrop coverage (0 = bare paper)
                    // General "over" with blend weighted by backdrop alpha (PDF 32000 §11.3.6),
                    // same as CompositeGroupLayer. The blend only acts where a real backdrop
                    // exists (dn>0); over bare paper (dn=0) the fill paints its own colour.
                    // Crucially the alpha channel is written too — a blended fill over paper
                    // must raise coverage, or it stays transparent and vanishes at flatten.
                    double bbr = sr, bbg = sg, bbb = sb;
                    if (dn > 0.0)
                    {
                        Rasterizer.BlendModes.Blend(mode, dr, dg, db, sr, sg, sb, out int ibr, out int ibg, out int ibb);
                        bbr = (1 - dn) * sr + dn * ibr;
                        bbg = (1 - dn) * sg + dn * ibg;
                        bbb = (1 - dn) * sb + dn * ibb;
                    }
                    double outA = a + dn * (1 - a);
                    if (outA <= 0.0) continue;
                    double inv = dn * (1 - a);
                    drow[i]     = (byte)((bbb * a + db * inv) / outA + 0.5);
                    drow[i + 1] = (byte)((bbg * a + dg * inv) / outA + 0.5);
                    drow[i + 2] = (byte)((bbr * a + dr * inv) / outA + 0.5);
                    drow[i + 3] = (byte)(outA * 255 + 0.5);
                    dirty = true;
                }
                if (dirty)
                    System.Runtime.InteropServices.Marshal.Copy(drow, 0, dst.Scan0 + y * dst.Stride, rowBytes);
            }
        }
        finally
        {
            _bitmap.UnlockBits(dst);
            _blendScratch.UnlockBits(src);
        }
    }

    private void ApplyClip(bool evenOdd, GraphicsState state, IReadOnlyList<PathCommand> segments)
    {
        if (segments.Count == 0) return;
        using var path = BuildPath(segments, evenOdd);
        var saved = _g.Transform;
        using var world = WorldMatrix(state.Ctm);
        _g.Transform = world;
        try
        {
            // Some producers emit a clip rectangle with coordinates ±~2^28 to mean
            // "clip to everything" (an effective no-op). Passing such a giant path to
            // GDI+ produces a broken/empty region (its scan conversion overflows),
            // which silently clips away all subsequent drawing. Treat a clip whose
            // device-space bounds dwarf the page as no clip at all.
            var db = path.GetBounds(world);
            if (db.Width > _bitmap.Width * 64f || db.Height > _bitmap.Height * 64f)
                return;
            _g.SetClip(path, CombineMode.Intersect);
        }
        finally { _g.Transform = saved; }
    }

    /// <summary>Build a <see cref="GraphicsPath"/> in PDF user space from path commands.</summary>
    private static GraphicsPath BuildPath(IReadOnlyList<PathCommand> segments, bool evenOdd)
    {
        var path = new GraphicsPath(evenOdd ? FillMode.Alternate : FillMode.Winding);
        float curX = 0, curY = 0, startX = 0, startY = 0;
        bool figureOpen = false;

        foreach (var seg in segments)
        {
            switch (seg.Op)
            {
                case PathOp.MoveTo:
                    path.StartFigure();
                    curX = (float)seg.X1; curY = (float)seg.Y1;
                    startX = curX; startY = curY;
                    figureOpen = true;
                    break;
                case PathOp.LineTo:
                    if (!figureOpen) { path.StartFigure(); figureOpen = true; startX = curX; startY = curY; }
                    path.AddLine(curX, curY, (float)seg.X1, (float)seg.Y1);
                    curX = (float)seg.X1; curY = (float)seg.Y1;
                    break;
                case PathOp.CurveTo:
                    path.AddBezier(curX, curY, (float)seg.X1, (float)seg.Y1,
                        (float)seg.X2, (float)seg.Y2, (float)seg.X3, (float)seg.Y3);
                    curX = (float)seg.X3; curY = (float)seg.Y3;
                    break;
                case PathOp.CurveToV: // control1 = current point
                    path.AddBezier(curX, curY, curX, curY,
                        (float)seg.X1, (float)seg.Y1, (float)seg.X2, (float)seg.Y2);
                    curX = (float)seg.X2; curY = (float)seg.Y2;
                    break;
                case PathOp.CurveToY: // control2 = endpoint; the `y` operator's endpoint
                                      // is stored in X2/Y2 (X3/Y3 are unused for this op)
                    path.AddBezier(curX, curY, (float)seg.X1, (float)seg.Y1,
                        (float)seg.X2, (float)seg.Y2, (float)seg.X2, (float)seg.Y2);
                    curX = (float)seg.X2; curY = (float)seg.Y2;
                    break;
                case PathOp.Rect:
                    path.StartFigure();
                    var rx = (float)seg.X1; var ry = (float)seg.Y1;
                    var rw = (float)seg.X2; var rh = (float)seg.Y2;
                    path.AddLines(new[]
                    {
                        new PointF(rx, ry), new PointF(rx + rw, ry),
                        new PointF(rx + rw, ry + rh), new PointF(rx, ry + rh),
                    });
                    path.CloseFigure();
                    curX = rx; curY = ry; startX = rx; startY = ry;
                    figureOpen = false;
                    break;
                case PathOp.Close:
                    path.CloseFigure();
                    curX = startX; curY = startY;
                    figureOpen = false;
                    break;
            }
        }
        return path;
    }

    /// <summary>
    /// Ratio of the CTM's singular values (max/min stretch). 1 for uniform scale and
    /// rotation; grows as the matrix squashes one axis relative to the other.
    /// Degenerate matrices report a huge ratio (callers treat them like "very anisotropic",
    /// where the widen-and-fill path still produces the right geometry).
    /// </summary>
    private static double CtmAnisotropy(double[] m)
    {
        double a = m[0], b = m[1], c = m[2], d = m[3];
        double e = a * a + b * b + c * c + d * d;
        double det = Math.Abs(a * d - b * c);
        // σmax² = (E + √(E²−4·det²))/2, σmin = det/σmax
        double disc = Math.Sqrt(Math.Max(0, e * e - 4 * det * det));
        double sMax = Math.Sqrt((e + disc) / 2);
        if (sMax <= 0) return 1;
        double sMin = det / sMax;
        return sMin > 1e-9 ? sMax / sMin : 1e9;
    }

    private Pen BuildPen(GraphicsState state)
    {
        // Line width is in user units; the active world transform scales it into
        // device pixels exactly as the CTM dictates. Width 0 means "thinnest
        // renderable line" — GDI+ draws a 1-device-pixel hairline for width 0.
        var pen = new Pen(ColorFrom(state.StrokeR, state.StrokeG, state.StrokeB, state.StrokeAlpha),
            (float)state.LineWidth);
        pen.StartCap = pen.EndCap = state.LineCap switch
        {
            1 => LineCap.Round,
            2 => LineCap.Square,
            _ => LineCap.Flat,
        };
        pen.LineJoin = state.LineJoin switch
        {
            1 => LineJoin.Round,
            2 => LineJoin.Bevel,
            _ => LineJoin.Miter,
        };
        if (state.MiterLimit > 0) pen.MiterLimit = (float)state.MiterLimit;
        if (state.DashArray.Length > 0)
        {
            var w = (float)state.LineWidth;
            if (w <= 0) w = 1;
            var pattern = new float[state.DashArray.Length];
            var allZero = true;
            for (int i = 0; i < pattern.Length; i++)
            {
                pattern[i] = (float)(state.DashArray[i] / w);
                if (pattern[i] > 0) allZero = false;
                if (pattern[i] <= 0) pattern[i] = 0.01f; // GDI+ rejects zero dash entries
            }
            if (!allZero)
            {
                pen.DashPattern = pattern;
                pen.DashOffset = (float)(state.DashPhase / w);
                // PDF dash segments take the stroke's line cap (§8.4.3.6), but GDI+
                // caps dash segments with Pen.DashCap, not Start/EndCap. Without it a
                // zero-length dash entry — a round DOT in every dotted-border PDF —
                // gets flat caps and paints nothing.
                if (state.LineCap == 1) pen.DashCap = DashCap.Round;
            }
        }
        return pen;
    }

    private static GdiColor ColorFrom(double r, double g, double b, double alpha)
    {
        return GdiColor.FromArgb(Clamp255(alpha), Clamp255(r), Clamp255(g), Clamp255(b));
    }

    private static int Clamp255(double v)
    {
        var i = (int)(v * 255.0 + 0.5);
        return i < 0 ? 0 : i > 255 ? 255 : i;
    }

    // ── Text ────────────────────────────────────────────────────────

    private void DrawText(string text, byte[] rawBytes, GraphicsState state)
    {
        if (state.RenderingMode == 3) return; // invisible
        if (PageRenderFlags.SuppressText) return; // HTML PNG-background: graphics only
        if (string.IsNullOrEmpty(text) && (rawBytes is null || rawBytes.Length == 0)) return;
        // Modes 4-7 add glyphs to the clip path (accumulated in PaintGlyph); mode 7 is
        // clip-only (no paint). PaintGlyph reads this to decide accumulate vs. fill.
        _curTextMode = state.RenderingMode;
        if (_curTextMode >= 4) _textClipPending = true;

        // Type 3 fonts define each glyph as its own PDF content stream (/CharProcs).
        if (rawBytes is { Length: > 0 } && state.FontName is { } fn3 && _scope.Fonts is not null
            && _scope.Fonts.TryGetValue(fn3, out var fd3) && fd3.GetName("Subtype") == "Type3")
        {
            DrawType3Text(rawBytes, state, fd3);
            return;
        }

        var parser = ResolveParser(state.FontName, out var hScale);
        var metrics = ResolveMetrics(state.FontName);
        var cid = ResolveCid(state.FontName);
        var fill = ColorFrom(state.FillR, state.FillG, state.FillB, state.FillAlpha);

        // An active ExtGState soft mask modulates GLYPH fills per pixel too
        // (PDF 32000 §11.6.5.4) — without this, text drawn under a luminosity mask
        // (e.g. artwork the mask hides) painted at full coverage as a visible ghost.
        _curTextSoftMask = state.SoftMask is { } smText ? GetSoftMaskAlpha(smText) : null;
        _curTextState = state;

        // A Type0 font with a 1-byte custom CMap (codespace <00> <FF>) still shows
        // CIDs, not byte-encoded characters. When its embedded program is a CID-keyed
        // CFF the simple-font path can never resolve a glyph (bare CFFs carry no
        // cmap), so route it through the CID path too — DrawCidText steps 1 byte per
        // code for these. Type0 fonts with TrueType descendants keep the old routing.
        if (cid is not null && rawBytes is not null
            && (cid.IsTwoByteEncoding || parser is CffGlyphSource { IsCidKeyed: true }))
            DrawCidText(rawBytes, cid, parser, metrics, state, hScale, fill);
        else
            DrawSimpleText(text, rawBytes, parser, metrics, state, hScale, fill, EncGidMap(state.FontName, parser));
    }

    // Per-show-op soft-mask context consumed by PaintGlyph (set in DrawText).
    private byte[]? _curTextSoftMask;

    private GraphicsState? _curTextState;

    private int[]? EncGidMap(string? fontName, IGlyphOutlineSource? parser)
    {
        if (fontName is null) return null;
        if (_encGidMaps.TryGetValue(fontName, out var cached)) return cached;
        var map = SoftwarePageRenderer.BuildEncodingGidMap(_scope.Fonts, _reader, fontName, parser);
        _encGidMaps[fontName] = map;
        return map;
    }

    private void DrawSimpleText(string text, byte[]? rawBytes, IGlyphOutlineSource? parser,
        FontMetrics? metrics, GraphicsState state, double hScale, GdiColor fill, int[]? encGidMap)
    {
        var tm = (double[])state.TextMatrix.Clone();
        var ctm = state.Ctm;
        var tfs = state.FontSize;
        var th = state.HorizontalScaling / 100.0;
        // A simple-font run whose own program can't be resolved would otherwise draw
        // nothing (gid stays 0). Substitute a host font (DefaultFontName / BaseFont /
        // Arial) and look glyphs up by Unicode through its cmap. Only reached when the
        // real parser is null, so embedded-font runs are unaffected.
        bool useFallback = false;
        if (parser is null)
        {
            parser = ResolveSimpleFallback(state.FontName);
            useFallback = parser is not null;
            encGidMap = null; // /Differences GIDs are for the original program, not the substitute
        }
        var upm = parser is not null && parser.UnitsPerEm > 0 ? parser.UnitsPerEm : 1000;
        // A simple font shows exactly one glyph per BYTE. When a /ToUnicode entry
        // expands one code to several chars (an Arabic ligature, "fi", …) the decoded
        // text is LONGER than the byte string; iterating chars would then look every
        // glyph up by Unicode — which a code-keyed subset cmap (Mac (1,0) format 0)
        // can't resolve — and drop the run's glyphs. Re-key the loop on the raw codes
        // when the lengths disagree, drawing the OWN-font run per byte (the ligature
        // code draws its single ligature glyph).
        if (rawBytes is not null && rawBytes.Length != text.Length && !useFallback)
        {
            var chars = new char[rawBytes.Length];
            for (var bi = 0; bi < rawBytes.Length; bi++) chars[bi] = (char)rawBytes[bi];
            text = new string(chars);
        }
        bool useBytes = rawBytes is not null && rawBytes.Length == text.Length;
        using var brush = new SolidBrush(fill);

        for (int i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            int gid = 0;
            if (useFallback && parser is not null)
            {
                // Host substitute: map the decoded Unicode char straight through its cmap.
                if (!parser.CMap.TryGetValue(ch, out gid)) gid = 0;
            }
            else if (parser is not null)
            {
                // An explicit /Encoding /Differences is authoritative for a simple font
                // (PDF 32000 §9.6.6.1): the code→glyph-name mapping must override the
                // embedded program's own byte cmap. Otherwise a code like 0x39 whose
                // Differences name is "t" wrongly draws the embedded "nine" glyph
                // encGidMap is non-null only when /Differences exists,
                // and a code with no resolvable name yields 0 → fall back to the cmap.
                if (encGidMap is not null && rawBytes is not null && i < rawBytes.Length)
                    gid = encGidMap[rawBytes[i]];

                if (gid == 0)
                {
                    if (useBytes && parser.CMap.TryGetValue(rawBytes![i], out gid) && gid > 0) { }
                    else if (parser.CMap.TryGetValue(ch, out gid) && gid > 0) { }
                    else gid = 0;
                }
            }
            if (parser is not null && gid > 0)
                PaintGlyph(parser, gid, tm, ctm, tfs, th, state.Rise, hScale, upm, brush);

            int charWidth = 500;
            if (useBytes && metrics is not null) charWidth = metrics.GetWidth(rawBytes![i]);
            else if (metrics is not null) charWidth = metrics.GetWidth(ch);
            // With a host substitute and no PDF /Widths, take the advance from the
            // substitute program so the run doesn't collapse to uniform 500-unit steps.
            // Same when the font dict carries NO explicit width for this code (no
            // /Widths, not standard-14 — e.g. an unembedded /Verdana appearance font):
            // the metrics' constant default would spread every glyph to the same step.
            if ((charWidth == 0 || (ch > 0xFF && (charWidth == 500 || charWidth <= 0))
                 || (useFallback && metrics is null)
                 || (metrics is not null && !metrics.HasExplicitWidth(useBytes ? rawBytes![i] : ch)))
                && parser is not null && gid > 0)
            {
                var adv = parser.GetAdvanceWidth(gid);
                if (adv > 0 && parser.UnitsPerEm > 0) charWidth = adv * 1000 / parser.UnitsPerEm;
            }
            double tx = (charWidth / 1000.0 * tfs + state.CharSpacing + (ch == ' ' ? state.WordSpacing : 0)) * th;
            tm = GraphicsState.MultiplyMatrices(new double[] { 1, 0, 0, 1, tx, 0 }, tm);
        }
    }

    private void DrawCidText(byte[] rawBytes, CidFontInfo cid, IGlyphOutlineSource? parser,
        FontMetrics? metrics, GraphicsState state, double hScale, GdiColor fill)
    {
        var tm = (double[])state.TextMatrix.Clone();
        var ctm = state.Ctm;
        var tfs = state.FontSize;
        var th = state.HorizontalScaling / 100.0;
        var upm = parser is not null && parser.UnitsPerEm > 0 ? parser.UnitsPerEm : 1000;
        using var brush = new SolidBrush(fill);

        // Predefined legacy national CMaps (GBK-EUC-H, ETen-B5-H, …) encode their
        // show-strings in a national multi-byte charset (mixed 1-/2-byte), not as
        // Adobe CIDs. Decode and render them separately from the 2-byte CID path.
        if (cid.LegacyCodepage != 0)
        {
            DrawLegacyCjkText(rawBytes, cid, parser, metrics, tm, ctm, tfs, th, state.Rise, hScale,
                state.CharSpacing, state.WordSpacing, brush);
            return;
        }

        // Non-embedded predefined CJK CIDFonts have no /FontFile*, so parser is null.
        // PDF 32000 §9.6.6 expects the reader to supply a system font matching the
        // /CIDSystemInfo. Mirror SoftwarePageRenderer.DrawCidText: route glyph lookup
        // through a broad-coverage system CJK font (CID/Unicode → cmap).
        Text.GlyphOutlineParser? fallback = null;
        if (parser is null)
        {
            var canFallback = cid.IsUnicodeEncoding
                              || (cid.Ordering is not null && cid.Ordering != "Identity");
            // Resolve a system font by the CID ordering/base name (Korea1 -> Malgun,
            // GB1 -> SimSun, Japan1 -> MS Mincho), not the single generic broad-coverage
            // font: that one covers Han but not Hangul, so non-embedded Korean text
            // (UniKS-UTF16-H) was dropped while Chinese on the same page rendered.
            // ResolveNamed falls back to the generic font itself.
            if (canFallback) fallback = Text.CjkFallbackFont.ResolveNamed(cid.CjkBaseFont, cid.Ordering);
        }
        var fbUpm = fallback is not null && fallback.UnitsPerEm > 0 ? fallback.UnitsPerEm : 1000;

        var vertical = cid.IsVertical;
        // 1-byte custom CMaps (codespace <00> <FF>) show one CID per byte.
        var step = cid.IsTwoByteEncoding ? 2 : 1;
        for (int i = 0; i + step <= rawBytes.Length; i += step)
        {
            int code = step == 2 ? (rawBytes[i] << 8) | rawBytes[i + 1] : rawBytes[i];
            int c = cid.CodeToCid(code);
            int charWidth = metrics?.GetWidth(c) ?? 1000;

            // Vertical writing (-V CMap, PDF 32000 §9.7.4.3): the pen runs DOWN the
            // column. Each glyph's origin is displaced by the position vector
            // v = (vx, vy) — default (w0/2, /DW2 vy) — so the glyph centres on the
            // column axis with its body below the pen; the pen then advances by the
            // vertical displacement w1 (default /DW2, per-CID /W2 override).
            var glyphTm = tm;
            double w1y = 0;
            if (vertical)
            {
                var (w1, vx, vy) = cid.VerticalMetrics(c, charWidth);
                w1y = w1;
                glyphTm = GraphicsState.MultiplyMatrices(
                    new double[] { 1, 0, 0, 1, -vx / 1000.0 * tfs, -vy / 1000.0 * tfs }, tm);
            }

            if (parser is not null)
            {
                int gid = parser is CffGlyphSource cff && cff.IsCidKeyed ? cff.CidToGid(c) : cid.ResolveGid(c);
                // Some producers show CIDs the embedded CID-keyed CFF never defines
                // (a constant high byte over a small identity charset). Paint the
                // low-byte glyph instead; only reached when the charset
                // lookup missed, so valid CIDs are untouched.
                if (gid == 0 && c > 0xFF && parser is CffGlyphSource cffLow && cffLow.IsCidKeyed)
                    gid = cffLow.CidToGid(c & 0xFF);
                if (gid > 0)
                    PaintGlyph(parser, gid, glyphTm, ctm, tfs, th, state.Rise, hScale, upm, brush);
            }
            else if (fallback is not null)
            {
                int fbGid;
                if (cid.IsUnicodeEncoding)
                    fallback.CMap.TryGetValue(c, out fbGid);
                else
                    fbGid = Text.CjkFallbackFont.ResolveFallbackGid(cid.Ordering, c);
                if (fbGid > 0)
                    PaintGlyph(fallback, fbGid, glyphTm, ctm, tfs, th, state.Rise, hScale, fbUpm, brush);
            }

            if (vertical)
            {
                // Advance down: w1 is negative (downward) in glyph space; Tc adds to
                // the travel. Tz applies to horizontal displacements only (§9.3.4).
                double ty = w1y / 1000.0 * tfs - state.CharSpacing;
                tm = GraphicsState.MultiplyMatrices(new double[] { 1, 0, 0, 1, 0, ty }, tm);
            }
            else
            {
                double tx = (charWidth / 1000.0 * tfs + state.CharSpacing + (c == 32 ? state.WordSpacing : 0)) * th;
                tm = GraphicsState.MultiplyMatrices(new double[] { 1, 0, 0, 1, tx, 0 }, tm);
            }
        }
    }

    /// <summary>
    /// Render a show-string from a non-embedded predefined legacy national CMap
    /// (GBK-EUC-H, ETen-B5-H, 90ms-RKSJ-H, KSCms-UHC-H, …). Decode the bytes to
    /// Unicode through the CMap's national codepage, then draw each character with
    /// the resolved system font (Latin runs) or a broad CJK fallback (SimSun).
    /// Advances come from the chosen font's own hmtx (the PDF /W is keyed by Adobe
    /// CIDs we never resolve; its /DW is commonly 500 and would crush full-width CJK).
    /// </summary>
    private void DrawLegacyCjkText(byte[] rawBytes, CidFontInfo cid, IGlyphOutlineSource? parser,
        FontMetrics? metrics, double[] tm, double[] ctm, double tfs, double th, double rise, double hScale,
        double charSpacing, double wordSpacing, SolidBrush brush)
    {
        var fallback = Text.CjkFallbackFont.ResolveNamed(cid.CjkBaseFont, cid.Ordering);
        var i = 0;
        while (i < rawBytes.Length)
        {
            // Mixed-width national charset: lead byte 0x81-0xFE starts a 2-byte code.
            var step = cid.LegacyByteLength(rawBytes[i]);
            if (step == 2 && i + 1 >= rawBytes.Length) step = 1;
            var code = step == 2 ? ((rawBytes[i] << 8) | rawBytes[i + 1]) : rawBytes[i];
            i += step;

            var uni = cid.LegacyToUnicode(code) ?? -1;
            IGlyphOutlineSource? src = null;
            var gid = 0;
            if (uni >= 0)
            {
                if (parser is not null && parser.CMap.TryGetValue(uni, out var g1) && g1 > 0)
                { src = parser; gid = g1; }
                else if (fallback is not null && fallback.CMap.TryGetValue(uni, out var g2) && g2 > 0)
                { src = fallback; gid = g2; }
            }

            var upm = src is not null && src.UnitsPerEm > 0 ? src.UnitsPerEm : 1000;
            if (src is not null && gid > 0)
                PaintGlyph(src, gid, tm, ctm, tfs, th, rise, hScale, upm, brush);

            // Nominal full-/half-width advance (must match ContentStreamParser's cursor
            // advance for these CMaps, else glyphs and the next show-string drift apart).
            // Vertical (-V) text advances one em down the page per full-width glyph.
            if (cid.IsVertical)
            {
                double ty = -(tfs + charSpacing);
                tm = GraphicsState.MultiplyMatrices(new double[] { 1, 0, 0, 1, 0, ty }, tm);
            }
            else
            {
                var charWidth = Text.CjkFallbackFont.AdvanceEm(cid, metrics, code, step);
                double tx = (charWidth / 1000.0 * tfs + charSpacing + (uni == ' ' ? wordSpacing : 0)) * th;
                tm = GraphicsState.MultiplyMatrices(new double[] { 1, 0, 0, 1, tx, 0 }, tm);
            }
        }
    }

    /// <summary>
    /// Fill one glyph: build its outline in font units and transform by the full
    /// glyph→device chain (font-scale · text-param · Tm · CTM · page matrix).
    /// </summary>
    private void PaintGlyph(IGlyphOutlineSource parser, int gid, double[] tm, double[] ctm,
        double tfs, double th, double rise, double hScale, int upm, SolidBrush brush)
    {
        var outline = parser.GetOutline(gid);
        if (outline is null || outline.Contours.Length == 0) return;
        using var path = BuildGlyphPath(outline);
        if (path.PointCount == 0) return;

        var s = new double[] { hScale / upm, 0, 0, 1.0 / upm, 0, 0 };
        var param = new double[] { tfs * th, 0, 0, tfs, 0, rise };
        var m = GraphicsState.MultiplyMatrices(s, param);
        m = GraphicsState.MultiplyMatrices(m, tm);
        m = GraphicsState.MultiplyMatrices(m, ctm);

        var saved = _g.Transform;
        using var world = WorldMatrix(m);

        // Clip modes (4-7): collect the glyph outline in device space for the text clip.
        if (_curTextMode >= 4)
        {
            _textClip ??= new GraphicsPath(FillMode.Winding);
            using var devGlyph = (GraphicsPath)path.Clone();
            devGlyph.Transform(world);
            if (devGlyph.PointCount > 0) _textClip.AddPath(devGlyph, false);
        }

        if (_curTextMode == 7) return; // clip-only, no paint

        // Active ExtGState soft mask: composite the glyph per pixel through the mask's
        // alpha (same path as masked shape fills) instead of a plain opaque fill.
        if (_curTextSoftMask is not null && _curTextState is not null)
        {
            var blend = Rasterizer.BlendModes.Parse(_curTextState.BlendMode);
            FillPathBlended(path, world, blend, _curTextState, _curTextSoftMask);
            saved.Dispose();
            return;
        }

        // A /Pattern fill selected for the run (… /Pattern cs /P0 scn … Tj) paints the
        // glyphs with the pattern, not the RGB brush: clip to the glyph outline and run
        // the shared shading-pattern fill. Tiling patterns fall through to the solid fill.
        if (_curTextState is { FillPatternName: not null } pts && _scope.Patterns is not null)
        {
            var patObj = _reader.Resolve(_scope.Patterns.Get(pts.FillPatternName));
            if (patObj is PdfDictionary spd && (int)spd.GetInt("PatternType") == 2)
            {
                _g.Transform = world;
                try { FillWithShadingPattern(path, pts, spd); }
                finally { _g.Transform = saved; }
                saved.Dispose();
                return;
            }
        }

        if (TextOpaque)
        {
            PaintGlyphOpaque(path, world, brush.Color);
            saved.Dispose();
            return;
        }

        _g.Transform = world;
        var savedCq = _g.CompositingQuality;
        // Straight-sRGB compositing applies to SMALL text only — the same
        // size-dependence as rasterisers' stem darkening. Measured witnesses:
        // an 11-16 px-em CJK body must render HEAVY (gamma blending
        // halved its ink), while a ~54 px-em hairline script headline renders
        // LIGHT (straight sRGB overshoots its strict pixel gate). The boundary
        // sits in the unobserved gap between those witnesses.
        var we = world.Elements;
        var emPx = Math.Sqrt(Math.Abs(we[0] * we[3] - we[1] * we[2])) * upm;
        if (TextLinear && emPx < TextLinearMaxEmPx)
            _g.CompositingQuality = CompositingQuality.AssumeLinear;
        try
        {
            _g.FillPath(brush, path);
            if (TextBold > 0)
            {
                // Device-space pen: divide by the world scale so the stroke stays
                // TextBold pixels wide regardless of the glyph's user-space units.
                var e = world.Elements;
                var sc = Math.Sqrt(Math.Abs(e[0] * e[3] - e[1] * e[2]));
                if (sc > 1e-9)
                {
                    using var bp = new Pen(brush.Color, (float)(TextBold / sc));
                    _g.DrawPath(bp, path);
                }
            }
        }
        finally { _g.Transform = saved; _g.CompositingQuality = savedCq; }
    }

    /// <summary>
    /// Composite one glyph the way a GDI text run does: the glyph's
    /// coverage is blended in straight sRGB against the surface pre-flattened onto
    /// white paper, and every touched pixel becomes opaque. Bare-paper alpha under
    /// text therefore stops being "transparent backdrop" for later blend modes —
    /// the text op cannot write alpha.
    /// </summary>
    private void PaintGlyphOpaque(GraphicsPath path, GdiMatrix world, GdiColor color)
    {
        var db = path.GetBounds(world);
        int x0 = Math.Max(0, (int)Math.Floor(db.Left) - 1), y0 = Math.Max(0, (int)Math.Floor(db.Top) - 1);
        int x1 = Math.Min(_bitmap.Width, (int)Math.Ceiling(db.Right) + 2), y1 = Math.Min(_bitmap.Height, (int)Math.Ceiling(db.Bottom) + 2);
        int w = x1 - x0, h = y1 - y0;
        if (w <= 0 || h <= 0) return;

        using var mask = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var mg = Graphics.FromImage(mask))
        {
            mg.SmoothingMode = SmoothingMode.AntiAlias;
            mg.PixelOffsetMode = PagePom;
            using var m2 = world.Clone();
            m2.Translate(-x0, -y0, MatrixOrder.Append);
            mg.Transform = m2;
            using var wb = new SolidBrush(GdiColor.White);
            mg.FillPath(wb, path);
        }

        var mr = mask.LockBits(new System.Drawing.Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var dr = _bitmap.LockBits(new System.Drawing.Rectangle(x0, y0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            // LockBits with a sub-rectangle returns Scan0 at the rect origin but the FULL
            // bitmap stride — copy only the rect's own w*4 bytes per row, or the last row's
            // write runs past the end of the native buffer (heap corruption).
            int rowBytes = w * 4;
            var mrow = new byte[rowBytes];
            var drow = new byte[rowBytes];
            int kb = color.B, kg = color.G, kr = color.R, ka = color.A;
            for (int y = 0; y < h; y++)
            {
                var mPtr = (IntPtr)(mr.Scan0.ToInt64() + (long)y * mr.Stride);
                var dPtr = (IntPtr)(dr.Scan0.ToInt64() + (long)y * dr.Stride);
                System.Runtime.InteropServices.Marshal.Copy(mPtr, mrow, 0, rowBytes);
                System.Runtime.InteropServices.Marshal.Copy(dPtr, drow, 0, rowBytes);
                bool touched = false;
                for (int x = 0; x < w; x++)
                {
                    int o = x * 4;
                    int t = mrow[o + 3];
                    if (t == 0) continue;
                    touched = true;
                    int te = t * ka / 255;                    // coverage × fill alpha
                    int ad = drow[o + 3];
                    // pre-flatten dst onto white paper, then lerp toward the text colour
                    int bB = (drow[o] * ad + 255 * (255 - ad) + 127) / 255;
                    int bG = (drow[o + 1] * ad + 255 * (255 - ad) + 127) / 255;
                    int bR = (drow[o + 2] * ad + 255 * (255 - ad) + 127) / 255;
                    drow[o]     = (byte)((kb * te + bB * (255 - te) + 127) / 255);
                    drow[o + 1] = (byte)((kg * te + bG * (255 - te) + 127) / 255);
                    drow[o + 2] = (byte)((kr * te + bR * (255 - te) + 127) / 255);
                    drow[o + 3] = 255;
                }
                if (touched) System.Runtime.InteropServices.Marshal.Copy(drow, 0, dPtr, rowBytes);
            }
        }
        finally { mask.UnlockBits(mr); _bitmap.UnlockBits(dr); }
    }

    /// <summary>Convert a glyph outline (font units, Y-up, quadratic contours) to a path.</summary>
    private static GraphicsPath BuildGlyphPath(GlyphOutline outline)
    {
        var path = new GraphicsPath(FillMode.Winding);
        foreach (var contour in outline.Contours)
        {
            if (contour.Length < 2) continue;

            // Insert implied on-curve midpoints between consecutive off-curve points.
            var pts = new List<ContourPoint>(contour.Length + 4);
            int n = contour.Length;
            for (int i = 0; i < n; i++)
            {
                var cur = contour[i];
                var nxt = contour[(i + 1) % n];
                pts.Add(cur);
                if (!cur.OnCurve && !nxt.OnCurve)
                    pts.Add(new ContourPoint((cur.X + nxt.X) * 0.5, (cur.Y + nxt.Y) * 0.5, true));
            }

            var onIdx = new List<int>();
            for (int i = 0; i < pts.Count; i++) if (pts[i].OnCurve) onIdx.Add(i);
            if (onIdx.Count < 2) continue;

            path.StartFigure();
            int count = pts.Count;
            for (int k = 0; k < onIdx.Count; k++)
            {
                int i0 = onIdx[k];
                int i1 = onIdx[(k + 1) % onIdx.Count];
                var p0 = pts[i0];
                var p1 = pts[i1];
                int steps = (i1 - i0 + count) % count;
                if (steps == 1)
                {
                    path.AddLine((float)p0.X, (float)p0.Y, (float)p1.X, (float)p1.Y);
                }
                else
                {
                    // One off-curve control point between the two on-curve points.
                    var ctrl = pts[(i0 + 1) % count];
                    float c1x = (float)(p0.X + 2.0 / 3.0 * (ctrl.X - p0.X));
                    float c1y = (float)(p0.Y + 2.0 / 3.0 * (ctrl.Y - p0.Y));
                    float c2x = (float)(p1.X + 2.0 / 3.0 * (ctrl.X - p1.X));
                    float c2y = (float)(p1.Y + 2.0 / 3.0 * (ctrl.Y - p1.Y));
                    path.AddBezier((float)p0.X, (float)p0.Y, c1x, c1y, c2x, c2y, (float)p1.X, (float)p1.Y);
                }
            }
            path.CloseFigure();
        }
        return path;
    }

    private void DrawType3Text(byte[] rawBytes, GraphicsState state, PdfDictionary fontDict)
    {
        var fontMatrix = SoftwarePageRenderer.ExtractFontMatrix(fontDict);
        var encoding = SoftwarePageRenderer.ResolveEncoding(fontDict, _reader);
        var charProcs = _reader.ResolveDict(fontDict.Get("CharProcs"));
        if (charProcs is null) return;
        var widths = _reader.Resolve(fontDict.Get("Widths")) as PdfArray;
        int firstChar = (int)fontDict.GetInt("FirstChar");
        double hScale = state.HorizontalScaling / 100.0;
        var fontSizeMatrix = new[] { state.FontSize * hScale, 0.0, 0.0, state.FontSize, 0.0, 0.0 };

        var fontResources = _reader.ResolveDict(fontDict.Get("Resources"));
        var glyphScope = BuildScope(fontResources);
        MergeInto(glyphScope.XObjects, _scope.XObjects);
        MergeInto(glyphScope.Fonts, _scope.Fonts);
        MergeInto(glyphScope.ExtGStates, _scope.ExtGStates);
        glyphScope.Patterns ??= _scope.Patterns;
        glyphScope.Shadings ??= _scope.Shadings;
        glyphScope.ColorSpaces ??= _scope.ColorSpaces;
        glyphScope.Properties ??= _scope.Properties;

        var tm = (double[])state.TextMatrix.Clone();
        foreach (var b in rawBytes)
        {
            var glyphName = encoding[b];
            double widthUnits = 0;
            if (widths is not null)
            {
                int idx = b - firstChar;
                if (idx >= 0 && idx < widths.Count) widthUnits = NumFrom(widths[idx]);
            }
            double advanceTextSpace = widthUnits * fontMatrix[0];

            if (glyphName is not null && glyphName != ".notdef"
                && charProcs.Get(glyphName) is { } cpObj
                && _reader.ResolveStream(cpObj) is { } cpStream)
            {
                byte[] cp;
                try { cp = _reader.DecodeStream(cpStream); } catch { cp = System.Array.Empty<byte>(); }
                if (cp.Length > 0)
                {
                    var tmCtm = GraphicsState.MultiplyMatrices(tm, state.Ctm);
                    var sizeTmCtm = GraphicsState.MultiplyMatrices(fontSizeMatrix, tmCtm);
                    var glyphCtm = GraphicsState.MultiplyMatrices(fontMatrix, sizeTmCtm);
                    var savedScope = _scope;
                    var savedGdi = _g.Save();
                    _scope = glyphScope;
                    try { RenderContentStream(cp, glyphCtm, null); }
                    finally { _scope = savedScope; _g.Restore(savedGdi); }
                }
            }

            double dx = advanceTextSpace * state.FontSize * hScale;
            tm = GraphicsState.MultiplyMatrices(new double[] { 1, 0, 0, 1, dx, 0 }, tm);
        }
    }

    private IGlyphOutlineSource? ResolveParser(string? fontName, out double hScale)
    {
        hScale = 1.0;
        if (fontName is null || _scope.Fonts is null || !_scope.Fonts.TryGetValue(fontName, out var fd))
            return null;
        if (_glyphCache.TryGetValue(fd, out var c)) { hScale = c.hScale; return c.parser; }
        var scratch = new Dictionary<string, (IGlyphOutlineSource? parser, double hScale)>();
        var p = SoftwarePageRenderer.GetGlyphParser(_scope.Fonts, _reader, scratch, fontName, out hScale);
        _glyphCache[fd] = (p, hScale);
        return p;
    }

    /// <summary>Resolve a host-font glyph source to draw a simple-font run whose own
    /// program is unavailable. Prefers <see cref="DefaultFontName"/>, then the run's
    /// /BaseFont (subset prefix stripped), then Arial. Cached per resolved name.</summary>
    private Text.GlyphOutlineParser? ResolveSimpleFallback(string? fontName)
    {
        PdfDictionary? fd = null;
        var baseFont = fontName is not null && _scope.Fonts is not null
            && _scope.Fonts.TryGetValue(fontName, out fd)
            ? (_reader.Resolve(fd.Get("BaseFont")) as Core.PdfName)?.Value
            : null;
        // Strip a subset tag ("ABCDEF+Foo" -> "Foo").
        if (baseFont is { Length: > 7 } && baseFont[6] == '+') baseFont = baseFont.Substring(7);

        // Which host face substitutes this run: DefaultFontName, else the /BaseFont, else
        // Arial. The choice is STICKY per font dict for the document's lifetime — the first
        // render of a font pins its substitute so re-rendering the same Document with a
        // different DefaultFontName reuses the original (rendering one document twice
        // must give identical output). A fresh Document gets a fresh choice.
        string Pick() => !string.IsNullOrEmpty(DefaultFontName) ? DefaultFontName!
            : !string.IsNullOrEmpty(baseFont) ? baseFont!
            : "Arial";
        var name = _reader is not null
            ? _stickyFallback.GetValue(_reader, _ => new()).GetOrAdd(baseFont ?? fontName ?? "", _ => Pick())
            : Pick();
        if (_fallbackParsers.TryGetValue(name, out var cached)) return cached;

        Text.GlyphOutlineParser? parser = null;
        var ttf = Text.SystemFontResolver.Resolve(name) ?? Text.SystemFontResolver.Resolve("Arial");
        if (ttf is { Length: > 0 })
        {
            try { parser = new Text.GlyphOutlineParser(ttf); } catch { parser = null; }
        }
        _fallbackParsers[name] = parser;
        return parser;
    }

    private FontMetrics? ResolveMetrics(string? fontName)
    {
        if (fontName is null || _scope.Fonts is null || !_scope.Fonts.TryGetValue(fontName, out var fd)) return null;
        if (_metricsCache.TryGetValue(fd, out var m)) return m;
        var r = SoftwarePageRenderer.GetFontMetrics(_scope.Fonts, _reader, fontName);
        _metricsCache[fd] = r;
        return r;
    }

    private CidFontInfo? ResolveCid(string? fontName)
    {
        if (fontName is null || _scope.Fonts is null || !_scope.Fonts.TryGetValue(fontName, out var fd)) return null;
        if (_cidCache.TryGetValue(fd, out var c)) return c;
        var scratch = new Dictionary<string, CidFontInfo?>();
        var r = SoftwarePageRenderer.GetCidFontInfo(_scope.Fonts, _reader, scratch, fontName);
        _cidCache[fd] = r;
        return r;
    }

    // ── Shadings (`sh` operator) ────────────────────────────────────

    private void DrawShading(string name, GraphicsState state)
    {
        if (_scope.Shadings is null) return;
        var obj = _scope.Shadings.Get(name);
        if (obj is null) return;
        var shading = ShadingBase.Parse(obj, _reader);

        // A shading dictionary's optional /BBox (PDF 32000 §8.7.4.3, Table 78) is applied as a
        // temporary clipping boundary — expressed in the shading's target (current user) space —
        // while the shading is painted. The bare `sh` operator otherwise fills the whole current
        // clip; without honouring this BBox a banner gradient defined for a narrow band floods
        // the entire page and obscures all content drawn beneath it.
        System.Drawing.Region? savedClip = null;
        if (shading?.BBox is { Length: >= 4 } bb)
        {
            savedClip = _g.Clip;
            var savedT = _g.Transform;
            using (var world = WorldMatrix(state.Ctm))
            {
                _g.Transform = world;
                float bx = (float)Math.Min(bb[0], bb[2]), by = (float)Math.Min(bb[1], bb[3]);
                float bw = (float)Math.Abs(bb[2] - bb[0]), bh = (float)Math.Abs(bb[3] - bb[1]);
                _g.IntersectClip(new RectangleF(bx, by, bw, bh));
            }
            _g.Transform = savedT;
        }
        try
        {
            // The bare `sh` operator paints the current clip region with the shading. Honour it
            // for axial gradients (e.g. a panel filled by `… re W n … /Sh0 sh`) unless a soft
            // mask is active — those overlays depend on the mask's transparency, which this
            // brush path can't compose, so leave them skipped rather than paint an opaque fill
            // over the content beneath.
            if (shading is AxialShading ax) DrawAxialShading(ax, state, fillRegion: state.SoftMask is null, bareSh: true);
            else if (shading is RadialShading ra) DrawRadialShading(ra, state);
            // Mesh shadings (Types 4-7): tessellate the triangle/patch geometry and flat-fill
            // each cell with its interpolated colour. The bare `sh` paints the current clip,
            // so an active soft mask is left to compose elsewhere rather than painted opaque.
            else if (state.SoftMask is null
                && shading is FreeFormGouraudShading or LatticeFormGouraudShading
                            or CoonsPatchShading or TensorPatchShading)
                DrawMeshShading(shading, state);
            // Function-based (Type 1) shadings are not yet translated to GDI+ brushes.
        }
        finally
        {
            if (savedClip is not null) { _g.Clip = savedClip; savedClip.Dispose(); }
        }
    }

    private const int ShadingStops = 64;

    private GdiColor[]? SampleShading(Functions.PdfFunction? fn, double[] domain, string cs, byte alpha,
        Functions.PdfFunction? tint = null, string? altName = null)
    {
        if (fn is null) return null;
        double lo = domain.Length > 0 ? domain[0] : 0;
        double hi = domain.Length > 1 ? domain[1] : 1;
        var colors = new GdiColor[ShadingStops];
        var input = new double[1];
        for (int i = 0; i < ShadingStops; i++)
        {
            double t = i / (double)(ShadingStops - 1);
            input[0] = lo + t * (hi - lo);
            var col = fn.Evaluate(input);
            if (col is null) return null;
            SoftwarePageRenderer.ComponentsToRgb(col, cs, out var r, out var g, out var b, tint, altName);
            colors[i] = GdiColor.FromArgb(alpha, r, g, b);
        }
        return colors;
    }

    private void DrawAxialShading(AxialShading ax, GraphicsState state, bool fillRegion = false, bool bareSh = false)
    {
        // Only paint when filling a defined region (a shading-pattern fill clipped to its
        // path). The bare `sh` operator paints the whole current clip; some documents use it
        // for opaque field-highlight overlays that, without the transparency they carry,
        // would cover content drawn under them. Leave that path until soft masks compose.
        if (!fillRegion) return;
        // A bare `sh` shading in a MULTI-SPOT ink space (DeviceN, or another tint space
        // resolving to CMYK) is ink laid over the page — a `sh` vignette painted over a
        // photo, say. Composite it with an overprint Multiply so its no-ink end (which
        // converts to white) leaves the content beneath unchanged instead of knocking it
        // out; over bare paper Multiply equals an opaque paint. A SPOT-colour
        // (/Separation) shading is the opposite case: a decorative panel whose plate
        // replaces whatever sits under it, so it keeps the opaque paint. Plain
        // process-CMYK (DeviceCMYK) bare shadings are opaque paint too: a gradient bar
        // authored directly in process inks replaces the flat fill beneath it — its
        // to-white end must land WHITE, not "flat fill showing through" (Multiply would
        // preserve the base ink and shift the whole panel's colour).
        bool subtractive = bareSh && (ax.ColorSpaceName is "DeviceN"
                                      || (ax.ColorSpaceName is not "Separation" and not "DeviceCMYK"
                                          && ax.AltSpaceName is "DeviceCMYK"));
        var alpha = (byte)Clamp255(state.FillAlpha);
        var colors = SampleShading(ax.Function, ax.Domain, ax.ColorSpaceName, alpha, ax.TintTransform, ax.AltSpaceName);
        if (colors is null) return;

        var saved = _g.Transform;
        using var world = WorldMatrix(state.Ctm);
        _g.Transform = world;
        try
        {
            var p0 = new PointF((float)ax.X0, (float)ax.Y0);
            var p1 = new PointF((float)ax.X1, (float)ax.Y1);
            var dx = p1.X - p0.X; var dy = p1.Y - p0.Y;
            if (dx * dx + dy * dy < 1e-6) return;
            // GDI+ LinearGradientBrush throws "Parameter is not valid" for a perfectly
            // axis-aligned line (zero-width/height brush rectangle). Nudge the end point a
            // hair off-axis; far below a pixel after scaling, so the gradient is unchanged.
            if (Math.Abs(dx) < 1e-3) p1.X += 1e-2f;
            if (Math.Abs(dy) < 1e-3) p1.Y += 1e-2f;

            var bounds = _g.ClipBounds;
            if (bounds.Width <= 0 || bounds.Height <= 0 || bounds.Width > 100000 || bounds.Height > 100000) return;

            // Emulate /Extend (hold edge colours beyond the axis). GDI+ LinearGradientBrush
            // has no Clamp wrap mode (setting it throws), so extend the gradient line to
            // span the fill bounds' projection onto the axis and pad the stops with the end
            // colours. Then the default tiling never shows: the [0,1] gradient occupies its
            // sub-range and the flat edge colours fill the rest.
            float ux = p1.X - p0.X, uy = p1.Y - p0.Y;
            float len2 = ux * ux + uy * uy;
            float tmin = 0f, tmax = 1f;
            foreach (var c in new[] { new PointF(bounds.Left, bounds.Top), new PointF(bounds.Right, bounds.Top),
                                       new PointF(bounds.Left, bounds.Bottom), new PointF(bounds.Right, bounds.Bottom) })
            {
                float t = ((c.X - p0.X) * ux + (c.Y - p0.Y) * uy) / len2;
                if (t < tmin) tmin = t;
                if (t > tmax) tmax = t;
            }
            var ep0 = new PointF(p0.X + tmin * ux, p0.Y + tmin * uy);
            var ep1 = new PointF(p0.X + tmax * ux, p0.Y + tmax * uy);
            float span = tmax - tmin;
            using var brush = new LinearGradientBrush(ep0, ep1, colors[0], colors[^1]);
            var bcol = new List<GdiColor>();
            var bpos = new List<float>();
            float prev = -1f;
            void Add(float pp, GdiColor cc) { pp = Math.Clamp(pp, 0f, 1f); if (pp <= prev) pp = prev + 1e-4f; if (pp > 1f) pp = 1f; if (pp <= prev) return; bpos.Add(pp); bcol.Add(cc); prev = pp; }
            // /Extend controls whether the area beyond each axis end is painted: hold the
            // edge colour where true, leave transparent (hard stop) where false.
            var clear = GdiColor.FromArgb(0, 0, 0, 0);
            float t0 = -tmin / span, t1 = (1f - tmin) / span; // mapped positions of axis ends
            if (ax.Extend[0]) Add(0f, colors[0]);
            else { Add(0f, clear); if (t0 > 2e-3f) Add(t0 - 1e-3f, clear); }
            for (int i = 0; i < colors.Length; i++)
                Add((i / (float)(colors.Length - 1) - tmin) / span, colors[i]);
            if (ax.Extend[1]) Add(1f, colors[^1]);
            else { if (t1 < 1f - 2e-3f) Add(t1 + 1e-3f, clear); Add(1f, clear); }
            brush.InterpolationColors = new ColorBlend(bcol.Count) { Colors = bcol.ToArray(), Positions = bpos.ToArray() };
            if (subtractive)
                MultiplyBrushFill(brush, bounds, world);
            else
                _g.FillRectangle(brush, bounds);
        }
        catch { /* GDI+ rejects some degenerate gradients */ }
        finally { _g.Transform = saved; }
    }

    /// <summary>Fill the current clip with <paramref name="brush"/> through an overprint-style
    /// Multiply (out = dst·src/255) rather than an opaque paint, so a subtractive `sh` overlay
    /// tints the content beneath instead of knocking it out. Mirrors <see cref="BlitImageMultiply"/>
    /// but for a brush fill; <paramref name="boundsWorld"/> is in the current (world) space and
    /// <paramref name="world"/> is the active device transform.</summary>
    private void MultiplyBrushFill(System.Drawing.Brush brush, RectangleF boundsWorld, GdiMatrix world)
    {
        int w = _bitmap.Width, h = _bitmap.Height;
        var corners = new[]
        {
            new PointF(boundsWorld.Left, boundsWorld.Top), new PointF(boundsWorld.Right, boundsWorld.Top),
            new PointF(boundsWorld.Left, boundsWorld.Bottom), new PointF(boundsWorld.Right, boundsWorld.Bottom),
        };
        world.TransformPoints(corners);
        float fminX = corners[0].X, fminY = corners[0].Y, fmaxX = corners[0].X, fmaxY = corners[0].Y;
        foreach (var c in corners) { fminX = Math.Min(fminX, c.X); fminY = Math.Min(fminY, c.Y); fmaxX = Math.Max(fmaxX, c.X); fmaxY = Math.Max(fmaxY, c.Y); }
        int x0 = Math.Max(0, (int)Math.Floor(fminX)), y0 = Math.Max(0, (int)Math.Floor(fminY));
        int x1 = Math.Min(w, (int)Math.Ceiling(fmaxX)), y1 = Math.Min(h, (int)Math.Ceiling(fmaxY));
        if (x1 <= x0 || y1 <= y0) return;

        _blendScratch ??= new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var sg = Graphics.FromImage(_blendScratch))
        {
            sg.Clear(GdiColor.Transparent);
            sg.SmoothingMode = SmoothingMode.AntiAlias;
            sg.PixelOffsetMode = PagePom;
            sg.Transform = world;
            sg.Clip = _g.Clip;
            sg.FillRectangle(brush, boundsWorld);
        }

        _g.Flush();
        var rect = new System.Drawing.Rectangle(x0, y0, x1 - x0, y1 - y0);
        var dst = _bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var src = _blendScratch.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int rowBytes = rect.Width * 4;
            var drow = new byte[rowBytes];
            var srow = new byte[rowBytes];
            for (int y = 0; y < rect.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(dst.Scan0 + y * dst.Stride, drow, 0, rowBytes);
                System.Runtime.InteropServices.Marshal.Copy(src.Scan0 + y * src.Stride, srow, 0, rowBytes);
                bool dirty = false;
                for (int x = 0; x < rect.Width; x++)
                {
                    int i = x * 4;
                    int sa = srow[i + 3];
                    if (sa == 0) continue;
                    double a = sa / 255.0;
                    double dn = drow[i + 3] / 255.0;
                    double outA = a + dn * (1 - a);
                    if (outA <= 0.0) continue;
                    double inv = dn * (1 - a);
                    for (int c = 0; c < 3; c++)
                    {
                        int s = srow[i + c];
                        double bb = dn > 0.0 ? (1 - dn) * s + dn * (drow[i + c] * s / 255.0) : s;
                        drow[i + c] = (byte)((bb * a + drow[i + c] * inv) / outA + 0.5);
                    }
                    drow[i + 3] = (byte)(outA * 255 + 0.5);
                    dirty = true;
                }
                if (dirty)
                    System.Runtime.InteropServices.Marshal.Copy(drow, 0, dst.Scan0 + y * dst.Stride, rowBytes);
            }
        }
        finally
        {
            _bitmap.UnlockBits(dst);
            _blendScratch.UnlockBits(src);
        }
    }

    private void DrawRadialShading(RadialShading ra, GraphicsState state)
    {
        var alpha = (byte)Clamp255(state.FillAlpha);
        var colors = SampleShading(ra.Function, ra.Domain, ra.ColorSpaceName, alpha, ra.TintTransform, ra.AltSpaceName);
        if (colors is null) return;

        // Approximate with the larger of the two circles; center colour = domain start.
        bool outerIsOne = ra.R1 >= ra.R0;
        double cx = outerIsOne ? ra.X1 : ra.X0;
        double cy = outerIsOne ? ra.Y1 : ra.Y0;
        double rr = Math.Max(ra.R0, ra.R1);
        if (rr <= 1e-6) return;

        var saved = _g.Transform;
        using var world = WorldMatrix(state.Ctm);
        _g.Transform = world;
        try
        {
            var centerColor = outerIsOne ? colors[0] : colors[^1];
            var edgeColor = outerIsOne ? colors[^1] : colors[0];

            // /Extend beyond the outer circle pads with the edge colour (PDF 32000
            // §8.7.4.5.4; SVG-derived gradients rely on it). Without the pad the
            // region outside r is left unpainted — a "white ring" artifact.
            var extendOuter = ra.Extend is { Length: >= 2 } && (outerIsOne ? ra.Extend[1] : ra.Extend[0]);
            if (extendOuter)
            {
                var bounds = _g.ClipBounds;
                if (bounds.Width > 0 && bounds.Height > 0 && bounds.Width < 100000 && bounds.Height < 100000)
                {
                    using var pad = new SolidBrush(edgeColor);
                    _g.FillRectangle(pad, bounds);
                }
            }

            using var ellipse = new GraphicsPath();
            ellipse.AddEllipse((float)(cx - rr), (float)(cy - rr), (float)(2 * rr), (float)(2 * rr));
            // Focal point = the smaller circle's center (usually the highlight).
            var focal = outerIsOne ? new PointF((float)ra.X0, (float)ra.Y0) : new PointF((float)ra.X1, (float)ra.Y1);
            using var brush = new PathGradientBrush(ellipse) { CenterPoint = focal };
            brush.CenterColor = centerColor;
            brush.SurroundColors = new[] { edgeColor };
            if (colors.Length > 2)
            {
                // Multi-stop fidelity: PathGradientBrush interpolation positions run
                // from the boundary (0) to the center (1).
                var blend = new ColorBlend(colors.Length);
                for (var i = 0; i < colors.Length; i++)
                {
                    blend.Colors[i] = outerIsOne ? colors[colors.Length - 1 - i] : colors[i];
                    blend.Positions[i] = i / (float)(colors.Length - 1);
                }
                brush.InterpolationColors = blend;
            }
            _g.FillPath(brush, ellipse);
        }
        catch { /* degenerate radial */ }
        finally { _g.Transform = saved; }
    }

    // ── Mesh shadings (Types 4-7, §8.7.4.5) ─────────────────────────
    //
    // GDI+ has no Gouraud/patch gradient primitive, so the mesh geometry is
    // tessellated and each small cell is flat-filled with its (bilinearly /
    // averaged) colour. At the cell sizes used here the facet steps are visually
    // indistinguishable from a smooth gradient. The current GDI clip (installed by the
    // `… W n … sh` idiom) and the shading /BBox bound the paint, so cells that
    // fall outside the shape are clipped away by GDI+.
    private void DrawMeshShading(ShadingBase shading, GraphicsState state)
    {
        var saved = _g.Transform;
        var savedSmoothing = _g.SmoothingMode;
        using var world = WorldMatrix(state.Ctm);
        _g.Transform = world;
        // Flat-filled adjacent cells share exact edges; anti-aliasing those edges
        // would leave hairline seams, so disable it for the duration of the mesh.
        _g.SmoothingMode = SmoothingMode.None;
        var alpha = (byte)Clamp255(state.FillAlpha);
        try
        {
            switch (shading)
            {
                case FreeFormGouraudShading g: DrawTriangleMesh(g.Vertices, g.Triangles, shading, alpha); break;
                case LatticeFormGouraudShading l: DrawTriangleMesh(l.Vertices, l.Triangles, shading, alpha); break;
                case CoonsPatchShading c: DrawPatchMesh(c.Patches, shading, alpha); break;
                case TensorPatchShading t: DrawPatchMesh(t.Patches, shading, alpha); break;
            }
        }
        catch { /* malformed mesh — skip rather than abort the page */ }
        finally { _g.SmoothingMode = savedSmoothing; _g.Transform = saved; }
    }

    private GdiColor MeshColor(double[]? comp, ShadingBase shading, byte alpha)
    {
        if (comp is null || comp.Length == 0) return GdiColor.FromArgb(alpha, 0, 0, 0);
        SoftwarePageRenderer.ComponentsToRgb(comp, shading.ColorSpaceName, out var r, out var g, out var b,
            shading.TintTransform, shading.AltSpaceName);
        return GdiColor.FromArgb(alpha, r, g, b);
    }

    private void DrawTriangleMesh(MeshVertex[] verts, (int A, int B, int C)[] tris, ShadingBase shading, byte alpha)
    {
        var pts = new PointF[3];
        foreach (var (a, b, c) in tris)
        {
            if (a < 0 || b < 0 || c < 0 || a >= verts.Length || b >= verts.Length || c >= verts.Length) continue;
            var va = verts[a]; var vb = verts[b]; var vc = verts[c];
            var avg = AvgColor(va.Color, vb.Color, vc.Color);
            pts[0] = new PointF((float)va.X, (float)va.Y);
            pts[1] = new PointF((float)vb.X, (float)vb.Y);
            pts[2] = new PointF((float)vc.X, (float)vc.Y);
            using var brush = new SolidBrush(MeshColor(avg, shading, alpha));
            _g.FillPolygon(brush, pts);
        }
    }

    private void DrawPatchMesh(MeshPatch[] patches, ShadingBase shading, byte alpha)
    {
        const int N = 8; // tessellation grid per patch (N×N cells)
        var quad = new PointF[4];
        foreach (var p in patches)
        {
            for (var i = 0; i < N; i++)
            {
                double u0 = (double)i / N, u1 = (double)(i + 1) / N;
                for (var j = 0; j < N; j++)
                {
                    double v0 = (double)j / N, v1 = (double)(j + 1) / N;
                    EvalPatch(p, u0, v0, out var x00, out var y00);
                    EvalPatch(p, u1, v0, out var x10, out var y10);
                    EvalPatch(p, u1, v1, out var x11, out var y11);
                    EvalPatch(p, u0, v1, out var x01, out var y01);
                    var col = BilinearColor(p.CornerColors, (u0 + u1) * 0.5, (v0 + v1) * 0.5);
                    quad[0] = new PointF((float)x00, (float)y00);
                    quad[1] = new PointF((float)x10, (float)y10);
                    quad[2] = new PointF((float)x11, (float)y11);
                    quad[3] = new PointF((float)x01, (float)y01);
                    using var brush = new SolidBrush(MeshColor(col, shading, alpha));
                    _g.FillPolygon(brush, quad);
                }
            }
        }
    }

    // Tensor-product surface S(u,v) = ΣΣ B_i(u) B_j(v) P[i,j] over the 4×4 control net.
    private static void EvalPatch(MeshPatch p, double u, double v, out double x, out double y)
    {
        double sx = 0, sy = 0;
        for (var i = 0; i < 4; i++)
        {
            var bu = Bernstein(i, u);
            for (var j = 0; j < 4; j++)
            {
                var w = bu * Bernstein(j, v);
                sx += w * p.Px[i, j];
                sy += w * p.Py[i, j];
            }
        }
        x = sx; y = sy;
    }

    private static double Bernstein(int i, double t) => i switch
    {
        0 => (1 - t) * (1 - t) * (1 - t),
        1 => 3 * (1 - t) * (1 - t) * t,
        2 => 3 * (1 - t) * t * t,
        3 => t * t * t,
        _ => 0,
    };

    // Corner colours are stored in UV order 00, 03, 33, 30 (MeshPatch); bilinear-blend
    // them across the patch parameter square.
    private static double[]? BilinearColor(double[][] corners, double u, double v)
    {
        var c00 = corners[0]; var c01 = corners[1]; var c11 = corners[2]; var c10 = corners[3];
        if (c00 is null || c01 is null || c11 is null || c10 is null) return c00 ?? c01 ?? c11 ?? c10;
        var n = c00.Length;
        var outp = new double[n];
        double w00 = (1 - u) * (1 - v), w01 = (1 - u) * v, w11 = u * v, w10 = u * (1 - v);
        for (var k = 0; k < n; k++)
            outp[k] = w00 * c00[k] + w01 * c01[k] + w11 * c11[k] + w10 * c10[k];
        return outp;
    }

    private static double[]? AvgColor(double[] a, double[] b, double[] c)
    {
        if (a is null || b is null || c is null) return a ?? b ?? c;
        var n = Math.Min(a.Length, Math.Min(b.Length, c.Length));
        var outp = new double[n];
        for (var k = 0; k < n; k++) outp[k] = (a[k] + b[k] + c[k]) / 3.0;
        return outp;
    }

    // ── XObjects (images + forms) ───────────────────────────────────

    private void DrawXObject(string name, GraphicsState state)
    {
        if (_scope.XObjects is null || !_scope.XObjects.TryGetValue(name, out var xobj)) return;
        var subtype = xobj.Dict.GetName("Subtype");
        if (subtype == "Image") DrawImageXObject(xobj, state);
        else if (subtype == "Form") DrawFormXObject(xobj, state);
    }

    private void DrawImageXObject(PdfStream xobj, GraphicsState state)
    {
        // Skip images hidden by the default optional-content configuration.
        if (SoftwarePageRenderer.IsOcHidden(xobj.Dict.Get("OC"), _reader, _ocgHidden)) return;

        // An ImageMask painted while a /Pattern fill is selected (e.g. PowerPoint
        // exports a gradient as `/Pattern cs /P scn … /Mask Do`) is a stencil through
        // which the pattern shows — not a solid colour. Paint the pattern clipped to
        // the stencil; otherwise the mask renders with the stale solid fill colour
        // (which is dark/black: "squares render black"), grossly over-inking the page.
        if (state.FillPatternName is not null
            && xobj.Dict.Get("ImageMask") is PdfBoolean imb && imb.Value)
        {
            DrawPatternMaskedImage(xobj, state);
            return;
        }

        // Very large plain-gray/bilevel scans: the generic decode path expands to a
        // W×H×4 BGRA buffer (a 740-megapixel fax scan would need ~3 GB) and dies with
        // OutOfMemory — swallowed by SafeDraw, so the image simply vanished from the
        // page. Decode the packed samples straight into a device-sized box-averaged
        // bitmap instead: correct area-averaged appearance (a halftone screen reduces
        // to smooth grey, as it should), bounded memory, and far faster
        // than resampling the full-resolution expansion.
        {
            var iw = (int)xobj.Dict.GetInt("Width");
            var ih = (int)xobj.Dict.GetInt("Height");
            var ibpc = (int)xobj.Dict.GetInt("BitsPerComponent");
            if (Environment.GetEnvironmentVariable("Q_HUGEGRAY") != "0"
                && (long)iw * ih > 100_000_000 && (ibpc == 1 || ibpc == 8))
            {
                var csi = SoftwarePageRenderer.ResolveImageColorSpace(xobj.Dict.Get("ColorSpace"), _reader);
                if (csi.BaseName == "DeviceGray" && csi.Palette is null && csi.TintTransform is null)
                {
                    using var small = DecodeHugeGrayDownsampled(xobj, iw, ih, ibpc, state);
                    if (small is not null)
                    {
                        var sm2 = state.SoftMask is { } smk ? GetSoftMaskAlpha(smk) : null;
                        BlitImage(small, state.Ctm, overprint: false, state.FillAlpha, sm2);
                        return;
                    }
                }
            }
        }

        using var bmp = ImageDecoder.TryDecode(xobj, state, _reader);
        if (bmp is null) return;
        var softMask = state.SoftMask is { } sm ? GetSoftMaskAlpha(sm) : null;
        BlitImage(bmp, state.Ctm, state.OverprintFill && IsSubtractiveImage(xobj.Dict), state.FillAlpha, softMask);
    }

    /// <summary>Paint an ImageMask whose current fill is a pattern: build a clip from
    /// the stencil's painted pixels and fill it with the pattern (tiling or shading),
    /// so the pattern shows through the mask instead of a flat colour.</summary>
    private void DrawPatternMaskedImage(PdfStream xobj, GraphicsState state)
    {
        var w = (int)xobj.Dict.GetInt("Width");
        var h = (int)xobj.Dict.GetInt("Height");
        if (w <= 0 || h <= 0) return;
        byte[] bits;
        try { bits = _reader.DecodeStream(xobj); } catch { return; }
        var rowBytes = (w + 7) / 8;
        // Default /Decode [0 1]: bit 0 paints, bit 1 is transparent; [1 0] flips it.
        var invert = xobj.Dict.Get("Decode") is PdfArray dec && dec.Count >= 2 && NumFrom(dec[0]) > NumFrom(dec[1]);
        var paintBit = invert ? 1 : 0;

        // Build the stencil as a path in the image unit square (top row -> v=1, matching
        // the blit convention), coalescing horizontal runs of painted pixels per row.
        using var stencil = new GraphicsPath();
        for (int y = 0; y < h; y++)
        {
            var rb = y * rowBytes;
            int x = 0;
            while (x < w)
            {
                var bi = rb + (x >> 3);
                var bit = bi < bits.Length ? (bits[bi] >> (7 - (x & 7))) & 1 : 1 - paintBit;
                if (bit != paintBit) { x++; continue; }
                int start = x;
                while (x < w)
                {
                    var b2 = rb + (x >> 3);
                    if (b2 >= bits.Length || ((bits[b2] >> (7 - (x & 7))) & 1) != paintBit) break;
                    x++;
                }
                stencil.AddRectangle(new RectangleF((float)start / w, 1f - (float)(y + 1) / h,
                    (float)(x - start) / w, 1f / h));
            }
        }
        if (stencil.PointCount == 0) return;

        var gs = _g.Save();
        try
        {
            using var world = WorldMatrix(state.Ctm);
            _g.Transform = world;
            _g.SetClip(stencil, CombineMode.Intersect);
            using var quad = new GraphicsPath();
            quad.AddRectangle(new RectangleF(0f, 0f, 1f, 1f));
            FillWithTilingPattern(quad, state, world);
        }
        finally { _g.Restore(gs); }
    }

    private void DrawInlineImage(PdfDictionary dict, byte[] data, GraphicsState state)
    {
        using var bmp = ImageDecoder.TryDecodeInline(dict, data, state, _reader);
        if (bmp is null) return;
        var softMask = state.SoftMask is { } sm ? GetSoftMaskAlpha(sm) : null;
        BlitImage(bmp, state.Ctm, state.OverprintFill && IsSubtractiveImage(dict), state.FillAlpha, softMask);
    }

    /// <summary>
    /// True when an image is painted in a subtractive colour space (DeviceCMYK or a
    /// /Separation / /DeviceN spot space). Overprint (PDF 32000 §8.6.7) only changes
    /// the result for such spaces — an overprinted spot plate composites onto, rather
    /// than knocking out, the process colour underneath.
    /// </summary>
    private bool IsSubtractiveImage(PdfDictionary dict)
    {
        var cs = SoftwarePageRenderer.ResolveImageColorSpace(dict.Get("ColorSpace"), _reader);
        return cs.TintTransform is not null || cs.BaseName == "DeviceCMYK";
    }

    /// <summary>
    /// Place a decoded bitmap into the PDF unit square via the supplied CTM. The
    /// destination parallelogram (upper-left, upper-right, lower-left) in user space
    /// maps the bitmap's top row to unit-square y=1, so GDI+ resamples and orients
    /// the image — handling any CTM rotation/flip/skew natively.
    /// </summary>
    private void BlitImage(Bitmap bmp, double[] ctm, bool overprint = false, double alpha = 1.0, byte[]? softMask = null)
    {
        // Heavy-downscale prefilter: a very large source mapped onto a much smaller
        // device area (a 300+ MP 1-bit halftone scan on an A4 page) must be AREA-
        // AVERAGED — GDI+'s bicubic samples a fixed window, not the full footprint of
        // each destination pixel, so a 25× decimation of a dot screen comes out as
        // binary moiré instead of the smooth grey area averaging produces (and takes
        // minutes on the way). Box-average into a device-sized intermediate first;
        // the normal high-quality blit then only resamples by a small factor.
        Bitmap? shrunk = null;
        using (var worldProbe = WorldMatrix(ctm))
        {
            var ep = worldProbe.Elements;
            var pdW = Math.Sqrt(ep[0] * ep[0] + ep[1] * ep[1]);
            var pdH = Math.Sqrt(ep[2] * ep[2] + ep[3] * ep[3]);
            if (Environment.GetEnvironmentVariable("Q_BOXPRE") != "0"
                && pdW >= 1 && pdH >= 1
                && bmp.Width > pdW * 3 && bmp.Height > pdH * 3
                && (long)bmp.Width * bmp.Height > 4_000_000)
            {
                shrunk = BoxDownsample(bmp, (int)Math.Ceiling(pdW), (int)Math.Ceiling(pdH));
            }
        }
        if (shrunk is not null) bmp = shrunk;
        try
        {
            BlitImageCore(bmp, ctm, overprint, alpha, softMask);
        }
        finally { shrunk?.Dispose(); }
    }

    private void BlitImageCore(Bitmap bmp, double[] ctm, bool overprint, double alpha, byte[]? softMask)
    {
        var saved = _g.Transform;
        using var world = WorldMatrix(ctm);
        _g.Transform = world;
        // Composite a semi-transparent image in straight sRGB (no gamma) so its alpha
        // blend matches the platform renderer — the same reason the shape-fill path
        // forces AssumeLinear (PDF §11.3.6 composites in the device colour space, not
        // linear light). Without this a soft-masked overlay (e.g. a slide's translucent
        // blue/photo panels) composites a few levels too light. Opaque images are
        // unaffected (src fully replaces dst).
        var savedCq = _g.CompositingQuality;
        _g.CompositingQuality = CompositingQuality.AssumeLinear;
        try
        {
            // Device-space extent of the unit square under the world transform.
            // Elements = [m11, m12, m21, m22, dx, dy]; the u-edge (1,0) and v-edge
            // (0,1) map to (m11,m12) and (m21,m22).
            var e = world.Elements;
            var devW = Math.Sqrt(e[0] * e[0] + e[1] * e[1]);
            var devH = Math.Sqrt(e[2] * e[2] + e[3] * e[3]);
            // Sub-pixel-thin blits (e.g. a gradient or raster logo sliced into
            // 1-row scanline strips, each mapped to a fraction of a pixel) average
            // away to nothing under high-quality resampling. Grow such a strip to
            // cover at least one device pixel, centred on its band, so stacked
            // strips accumulate into the intended image instead of vanishing.
            float x0 = 0f, x1 = 1f, y0 = 0f, y1 = 1f;
            if (devW > 1e-6 && devW < 1f) { var f = (float)(1.0 / devW); x0 = 0.5f - f / 2f; x1 = 0.5f + f / 2f; }
            if (devH > 1e-6 && devH < 1f) { var f = (float)(1.0 / devH); y0 = 0.5f - f / 2f; y1 = 0.5f + f / 2f; }

            var dest = new[]
            {
                new PointF(x0, y1), // upper-left  → image top-left
                new PointF(x1, y1), // upper-right → image top-right
                new PointF(x0, y0), // lower-left  → image bottom-left
            };
            if (softMask is not null) BlitImageMasked(bmp, world, dest, alpha, softMask);
            else if (overprint) BlitImageMultiply(bmp, world, dest);
            else
            {
                // WrapMode.TileFlipXY: at the image boundary a high-quality (bicubic)
                // resample otherwise samples the pixels *outside* the source — which are
                // transparent since the page backdrop is bare paper — bleeding partial
                // alpha and a darkened colour into the edge row/column. Over the former
                // opaque-white backdrop this went unnoticed; on the coverage-alpha page it
                // flattens to off-white (e.g. an opaque white scan edge lands at 254
                // not 255). Clamping the sampler to the edge texel keeps the border
                // exact. The alpha branch also carries the /ca image opacity via a matrix.
                using var ia = new ImageAttributes();
                ia.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);
                if (alpha < 0.999)
                {
                    var cm = new ColorMatrix { Matrix33 = (float)Math.Max(0.0, Math.Min(1.0, alpha)) };
                    ia.SetColorMatrix(cm);
                }
                _g.DrawImage(bmp, dest, new RectangleF(0, 0, bmp.Width, bmp.Height),
                    GraphicsUnit.Pixel, ia);
            }
        }
        finally { _g.Transform = saved; _g.CompositingQuality = savedCq; }
    }

    /// <summary>Decode a very large packed DeviceGray image (1 or 8 bpc) directly into a
    /// device-sized box-averaged 32bpp bitmap, without ever materialising the full
    /// W×H×4 expansion. Returns null when the decode fails or the image maps to a
    /// larger-than-source device area (no downsample needed — the generic path can
    /// handle it). Honours /Decode [1 0] inversion.</summary>
    private Bitmap? DecodeHugeGrayDownsampled(PdfStream xobj, int w, int h, int bpc, GraphicsState state)
    {
        byte[] data;
        try { data = _reader.DecodeStream(xobj); } catch { return null; }
        if (data.Length == 0) return null;

        int dw, dh;
        using (var worldProbe = WorldMatrix(state.Ctm))
        {
            var ep = worldProbe.Elements;
            dw = (int)Math.Ceiling(Math.Sqrt(ep[0] * ep[0] + ep[1] * ep[1]));
            dh = (int)Math.Ceiling(Math.Sqrt(ep[2] * ep[2] + ep[3] * ep[3]));
        }
        if (dw < 1 || dh < 1 || dw >= w || dh >= h) return null;

        bool invert = xobj.Dict.Get("Decode") is PdfArray dec && dec.Count >= 2
            && NumFrom(dec[0]) > NumFrom(dec[1]);
        int inv = invert ? 1 : 0;

        var sum = new long[dw * dh];
        var cnt = new long[dw * dh];
        int rowBytes = bpc == 1 ? (w + 7) / 8 : w;
        for (int y = 0; y < h; y++)
        {
            long rowBase = (long)y * rowBytes;
            if (rowBase >= data.Length) break;
            int dy = (int)((long)y * dh / h);
            int db = dy * dw;
            if (bpc == 1)
            {
                for (int x = 0; x < w; x++)
                {
                    long bi = rowBase + (x >> 3);
                    int v = bi < data.Length ? ((((data[bi] >> (7 - (x & 7))) & 1) ^ inv) == 1 ? 255 : 0) : 255;
                    int di = db + (int)((long)x * dw / w);
                    sum[di] += v; cnt[di]++;
                }
            }
            else
            {
                for (int x = 0; x < w; x++)
                {
                    long bi = rowBase + x;
                    int v = bi < data.Length ? (invert ? 255 - data[bi] : data[bi]) : 255;
                    int di = db + (int)((long)x * dw / w);
                    sum[di] += v; cnt[di]++;
                }
            }
        }

        var dst = new Bitmap(dw, dh, PixelFormat.Format32bppArgb);
        var ddata = dst.LockBits(new System.Drawing.Rectangle(0, 0, dw, dh), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var drow = new byte[dw * 4];
            for (int y = 0; y < dh; y++)
            {
                int b = y * dw;
                for (int x = 0; x < dw; x++)
                {
                    var g = (byte)(sum[b + x] / Math.Max(1, cnt[b + x]));
                    int o = x * 4;
                    drow[o] = g; drow[o + 1] = g; drow[o + 2] = g; drow[o + 3] = 255;
                }
                System.Runtime.InteropServices.Marshal.Copy(drow, 0, ddata.Scan0 + (nint)y * ddata.Stride, drow.Length);
            }
        }
        finally { dst.UnlockBits(ddata); }
        return dst;
    }
}

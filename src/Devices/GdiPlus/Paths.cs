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
                    // A sub-pixel fill renders at its TRUE geometric coverage — the
                    // reference rasterizer draws a 0.24pt frame rule at 150 dpi as the
                    // 159/223 coverage split, never as a solid 1px bar (probed with a
                    // 0.03..1pt bar ladder at 150 and 300 dpi: coverage is exact, with
                    // a floor of ~1/8 px so a vanishingly thin rule stays faintly
                    // visible instead of dissolving to nothing).
                    var db = pathBounds;
                    var thinnest = Math.Min(db.Width, db.Height);
                    if (db.Width > 0f && db.Height > 0f && thinnest < 1f)
                    {
                        // GDI+'s own AA is unreliable below one device pixel (a
                        // 0.2px-tall rule can dissolve to nothing), so sub-pixel
                        // fills draw as a deterministic >=1px bar whose ALPHA is the
                        // geometric coverage (floored at ~1/8 so a vanishingly thin
                        // rule stays faintly visible) - the probed reference law is
                        // true coverage, never a solid bump.
                        using var faint = new SolidBrush(ColorFrom(state.FillR, state.FillG, state.FillB,
                            state.FillAlpha * Math.Max(thinnest, 0.125f)));
                        var cur = _g.Transform;
                        _g.ResetTransform();
                        _g.FillRectangle(faint, db.X, db.Y, Math.Max(db.Width, 1f), Math.Max(db.Height, 1f));
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
                FillWithTilingPattern(path, state, world, state.FillPatternName);
            }

            if (doStroke && state.StrokePatternName is not null)
            {
                // A pattern-stroked path (`/Pattern CS /P0 SCN … S`) paints the
                // stroke REGION with the pattern (PDF 32000 §8.7.3.3): widen the
                // path by the pen in user space and run the widened outline
                // through the same pattern machinery a fill uses.
                using var spen = BuildPen(state);
                GraphicsPath? widenedStroke = null;
                try
                {
                    widenedStroke = (GraphicsPath)path.Clone();
                    widenedStroke.Widen(spen);
                }
                catch { widenedStroke?.Dispose(); widenedStroke = null; }
                if (ShDebug2) Console.WriteLine($"[pstroke] pat={state.StrokePatternName} widened={(widenedStroke is not null)} pts={widenedStroke?.PointCount ?? -1}");
                if (widenedStroke is not null)
                {
                    using (widenedStroke)
                        FillWithTilingPattern(widenedStroke, state, world, state.StrokePatternName);
                }
            }
            else if (doStroke)
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
    private void FillWithTilingPattern(GraphicsPath path, GraphicsState state, GdiMatrix world, string patName)
    {
        if (_formDepth > 24 || _scope.Patterns is null) return;
        var patObj = _reader.Resolve(_scope.Patterns.Get(patName));
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
        // Under a NON-NORMAL blend mode the constant alpha counts TWICE. Measured on the
        // expected render over every blend mode, three backdrops, two source colours and
        // four alpha values: the result is exactly (1 - ca^2)*Cb + ca^2*B(Cb, Cs), while
        // Normal keeps the plain (1 - ca)*Cb + ca*Cs. Only the CONSTANT alpha is squared -
        // the shape's anti-alias coverage below stays linear.
        double ca = state.FillAlpha;
        if (mode != Rasterizer.BlendMode.Normal) ca *= ca;
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
                // ★ Each dash element is at least the LINE WIDTH. Measured on the
                // expected render over nine (pattern, width) combinations — the
                // rendered period is max(on,w) + max(off,w) every time, and the duty
                // cycle agrees: [3 2] at w3 draws as [3 3] (period 6, not 5), [3 2] at
                // w5 as [5 5] (period 10), while [6 4] at w3 is already above the floor
                // and renders nominally. Taking the array at face value makes every
                // narrow-gap dashed stroke too dense.
                // GDI+ counts the pattern in PEN WIDTHS, so the floor is 1.
                pattern[i] = (float)Math.Max(state.DashArray[i] / w, 1.0);
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

    // Per-show-op soft-mask context consumed by PaintGlyph (set in DrawText).
    private byte[]? _curTextSoftMask;

    private GraphicsState? _curTextState;

    // ── Shadings (`sh` operator) ────────────────────────────────────

    // ── XObjects (images + forms) ───────────────────────────────────

}

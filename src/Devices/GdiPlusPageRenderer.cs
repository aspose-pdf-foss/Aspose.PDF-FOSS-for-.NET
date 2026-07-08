using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;
// Aspose.Pdf mirrors several System.Drawing type names. Enclosing-namespace types
// (Aspose.Pdf.Color/Matrix/Rectangle) win over compilation-unit aliases, so the GDI+
// equivalents use distinct alias names; unqualified Rectangle resolves to Aspose.Pdf.Rectangle.
using GdiColor = System.Drawing.Color;
using GdiMatrix = System.Drawing.Drawing2D.Matrix;
using GraphicsState = Aspose.Pdf.Content.GraphicsState;
using GdiState = System.Drawing.Drawing2D.GraphicsState;

namespace Aspose.Pdf.Devices;

/// <summary>
/// PDF page renderer that rasterizes through GDI+ (<see cref="System.Drawing.Graphics"/>).
/// On Windows this is the rasterizer used to produce comparison
/// templates: anti-aliased vector fills/strokes, high-quality bicubic image
/// resampling, and native glyph outlines. Windows-only — callers must fall back
/// to <see cref="SoftwarePageRenderer"/> on other platforms (GDI+ drawing throws
/// <see cref="System.PlatformNotSupportedException"/> off Windows).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GdiPlusPageRenderer : IPageRenderer
{
    // ── Per-render state (single-threaded; one render at a time per instance) ──
    private Graphics _g = null!;
    private Bitmap _bitmap = null!;        // backing bitmap for _g (for manual blend compositing)
    private Bitmap? _blendScratch;         // reusable layer for non-Normal /BM fills
    // Pool of page-sized ARGB layer bitmaps reused across transparency-group
    // composites. A page with hundreds of groups (some documents have ~890) otherwise
    // allocates+zeroes a full-page bitmap per group — the dominant cost on a large
    // page. The pool keeps only ~nesting-depth bitmaps alive and reuses them; each
    // group re-initialises just its BBox sub-rect, so stale content elsewhere is
    // never read (the composite is BBox-bound).
    private readonly Stack<Bitmap> _layerPool = new();
    private PdfReader _reader = null!;
    private readonly Dictionary<string, int[]?> _encGidMaps = new(StringComparer.Ordinal);
    private double _scale;          // horizontal pixels per PDF point
    private double _scaleY;         // vertical pixels per PDF point (differs from _scale only when the caller pins a non-proportional target size)
    private int _pixelH;            // canvas height in pixels (for Y flip)
    private Rectangle _mediaBox = null!; // effective (rotation-adjusted) media box
    private Scope _scope = null!;   // current resource scope (swapped on form recursion)
    private int _formDepth;
    private readonly Dictionary<int, byte[]?> _softMaskCache = new(); // page-sized soft-mask alpha, by group obj

    /// <summary>Resolved resource tables for the current content scope.</summary>
    private sealed class Scope
    {
        public Dictionary<string, PdfStream>? XObjects;
        public Dictionary<string, PdfDictionary>? Fonts;
        public Dictionary<string, PdfDictionary>? ExtGStates;
        public PdfDictionary? Patterns;
        public PdfDictionary? Shadings;
        public PdfDictionary? ColorSpaces;
        public PdfDictionary? Properties;
    }

    /// <inheritdoc/>
    public RgbaBuffer RenderPage(byte[] pdfBytes, int pageNumber, int dpi)
    {
        using var doc = Document.Open(pdfBytes);
        return RenderPage(doc.Pages.At(pageNumber), dpi, dpi);
    }

    /// <summary>Render a page using its existing reader, avoiding a re-parse.</summary>
    internal RgbaBuffer RenderPage(Page page, int dpi) => RenderPage(page, dpi, dpi);

    internal RgbaBuffer RenderPage(Page page, int xDpi, int yDpi)
    {
        // Size to the crop box (clipped to the media box), not the media box: the
        // rasterizer presents only the cropped region. Rotation swaps the
        // visible dimensions exactly as it does for the media box.
        var crop = SoftwarePageRenderer.EffectiveCropRect(page);
        var rot = ((page.RotateDegrees % 360) + 360) % 360;
        var visW = (rot == 90 || rot == 270) ? crop.Height : crop.Width;
        var visH = (rot == 90 || rot == 270) ? crop.Width : crop.Height;
        var pixelW = SoftwarePageRenderer.FloorSnap(visW * xDpi / 72.0);
        var pixelH = SoftwarePageRenderer.FloorSnap(visH * yDpi / 72.0);
        return RenderPageAtPixelSize(page, pixelW, pixelH);
    }

    internal RgbaBuffer RenderPageAtPixelSize(Page page, int pixelW, int pixelH)
    {
        if (pixelW <= 0) pixelW = 1;
        if (pixelH <= 0) pixelH = 1;

        _reader = page.Reader;
        var rawMb = page.MediaBox;
        // Visible region = crop box clipped to media box; content is sized and offset
        // to it so anything outside the crop area is excluded.
        var crop = SoftwarePageRenderer.EffectiveCropRect(page);

        // /Rotate (PDF 32000 §14.8.2.7) composes into the initial CTM exactly as the
        // software renderer does, so a 90°/270° page swaps its pixel dimensions and
        // the content swings clockwise into the visible canvas.
        var rot = ((page.RotateDegrees % 360) + 360) % 360;
        Rectangle effectiveMb;
        double[]? initialPageCtm = null;
        if (rot is 90 or 180 or 270)
        {
            var w = rawMb.Width;
            var h = rawMb.Height;
            effectiveMb = rot == 180
                ? new Rectangle(0, 0, w, h)
                : new Rectangle(0, 0, h, w);
            initialPageCtm = rot switch
            {
                90 => new[] { 0.0, -1.0, 1.0, 0.0, 0.0, w },
                180 => new[] { -1.0, 0.0, 0.0, -1.0, w, h },
                270 => new[] { 0.0, 1.0, -1.0, 0.0, h, 0.0 },
                _ => null,
            };
        }
        else
        {
            // Unrotated: the device box is the crop rectangle, so its lower-left maps
            // to the bottom-left pixel and cropped content is positioned correctly.
            effectiveMb = crop;
        }

        _mediaBox = effectiveMb;
        // X and Y are scaled independently so an explicitly pinned target size (e.g.
        // SaveAsTIFF(file, 1000, 2000, …)) stretches the page to fill it exactly. For the
        // DPI-driven path the pixel grid is proportional to the page box, so the two
        // scales coincide and the result is identical to a single uniform scale.
        _scale = pixelW / effectiveMb.Width;
        _scaleY = pixelH / effectiveMb.Height;
        _pixelH = pixelH;
        _formDepth = 0;
        _glyphCache = new(ReferenceEqualityComparer.Instance);
        _cidCache = new(ReferenceEqualityComparer.Instance);
        _metricsCache = new(ReferenceEqualityComparer.Instance);
        _gdiStateStack.Clear();
        _ocgHiddenStack.Clear();
        _ocgHidden = SoftwarePageRenderer.ResolveHiddenOcgs(_reader);

        using var bitmap = new Bitmap(pixelW, pixelH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(GdiColor.White);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            _g = g;
            _bitmap = bitmap;

            var resources = SoftwarePageRenderer.ResolveInheritedPageResources(page.Dict, _reader);
            _scope = BuildScope(resources);

            var contentBytes = SoftwarePageRenderer.GetPageContent(page.Dict, _reader);
            RenderContentStream(contentBytes, initialPageCtm, null);

            // Annotations paint on top of page content (PDF 32000 §12.5).
            _g.ResetClip();
            SafeDraw(() => DrawAnnotations(page.Dict));
        }

        _reader.ClearCache();
        var result = ToRgbaBuffer(bitmap, pixelW, pixelH);
        _blendScratch?.Dispose();
        _blendScratch = null;
        while (_layerPool.Count > 0) _layerPool.Pop().Dispose();
        _bitmap = null!;
        return result;
    }

    private Scope BuildScope(PdfDictionary? resources)
    {
        return new Scope
        {
            XObjects = SoftwarePageRenderer.ResolveAllXObjects(resources, _reader),
            Fonts = SoftwarePageRenderer.ResolveFontDicts(resources, _reader),
            ExtGStates = SoftwarePageRenderer.ResolveExtGStates(resources, _reader),
            Patterns = _reader.ResolveDict(resources?.Get("Pattern")),
            Shadings = _reader.ResolveDict(resources?.Get("Shading")),
            ColorSpaces = _reader.ResolveDict(resources?.Get("ColorSpace")),
            Properties = _reader.ResolveDict(resources?.Get("Properties")),
        };
    }

    /// <summary>Parse a content stream, dispatching draw events to the GDI+ handlers.</summary>
    private void RenderContentStream(byte[] content, double[]? initialCtm, GraphicsPath? initialClip)
    {
        var parser = new ContentStreamParser(_reader);

        parser.OnOperator += (op, _, state) =>
        {
            // q/Q save & restore the GDI+ clip and transform alongside the parser's
            // own graphics-state stack so clipping round-trips correctly.
            if (op == "q") _gdiStateStack.Push(_g.Save());
            else if (op == "Q" && _gdiStateStack.Count > 0) _g.Restore(_gdiStateStack.Pop());
            // Text rendering modes 4-7 (PDF 32000 §9.3.3) add the shown glyphs to the
            // clipping path. The glyph outlines are accumulated (in device space) across
            // the text object and intersected into the clip at ET, so subsequent content
            // (commonly a fill or image painted as the text colour) shows only through the
            // glyph shapes. The clip is undone by the enclosing Q like any other clip.
            else if (op == "BT") { _textClip?.Dispose(); _textClip = null; }
            else if (op == "ET" && _textClip is not null)
            {
                if (_textClip.PointCount > 0)
                {
                    var savedT = _g.Transform;
                    _g.ResetTransform();
                    try { _g.SetClip(_textClip, CombineMode.Intersect); }
                    finally { _g.Transform = savedT; savedT.Dispose(); }
                }
                else
                {
                    // A clip-mode text object that produced no glyph outline clips to nothing.
                    _g.SetClip(System.Drawing.RectangleF.Empty);
                }
                _textClip.Dispose(); _textClip = null;
            }
        };

        parser.OnTextShown += (text, rawBytes, state) => { if (!IsContentHidden) SafeDraw(() => DrawText(text, rawBytes, state)); };
        parser.OnPathPainted += (op, state, segments) => { if (!IsContentHidden) SafeDraw(() => DrawPath(op, state, segments)); };
        // Clips always apply (must round-trip through q/Q) even inside hidden layers.
        parser.OnPathClipped += (evenOdd, state, segments) => SafeDraw(() => ApplyClip(evenOdd, state, segments));
        parser.OnImageDrawn += (name, state) => { if (!IsContentHidden) SafeDraw(() => DrawXObject(name, state)); };
        parser.OnInlineImage += (dict, data) => { if (!IsContentHidden) SafeDraw(() => DrawInlineImage(dict, data, parser.State)); };
        parser.OnShadingPainted += (name, state) => { if (!IsContentHidden) SafeDraw(() => DrawShading(name, state)); };

        parser.OnMarkedContentBegin += (tag, props) =>
        {
            var hide = tag == "OC" && props is not null && _ocgHidden is not null && _ocgHidden.Contains(props);
            _ocgHiddenStack.Push(hide);
        };
        parser.OnMarkedContentEnd += () => { if (_ocgHiddenStack.Count > 0) _ocgHiddenStack.Pop(); };

        if (initialCtm is not null) parser.State.Ctm = (double[])initialCtm.Clone();
        if (initialClip is not null) _g.SetClip(initialClip, CombineMode.Intersect);

        parser.Parse(content, _scope.Fonts, null, _scope.ExtGStates, _scope.ColorSpaces, _scope.Properties, _scope.Patterns);
    }

    private readonly Stack<GdiState> _gdiStateStack = new();

    // Text rendering mode of the current show operation (PDF 32000 §9.3.3); modes 4-7
    // add glyphs to the clip path. _textClip accumulates those outlines (device space)
    // for the current text object, applied at ET.
    private int _curTextMode;
    private GraphicsPath? _textClip;

    // Per-page font caches, keyed by font-dict identity so form scopes that reuse a
    // resource name for a different font don't collide.
    private Dictionary<PdfDictionary, (IGlyphOutlineSource? parser, double hScale)> _glyphCache = new(ReferenceEqualityComparer.Instance);
    private Dictionary<PdfDictionary, CidFontInfo?> _cidCache = new(ReferenceEqualityComparer.Instance);
    private Dictionary<PdfDictionary, FontMetrics?> _metricsCache = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, Text.GlyphOutlineParser?> _fallbackParsers = new();
    // Sticky substitute name so re-rendering one Document with a different DefaultFontName
    // reuses the first render's choice. Keyed by the reader (document
    // lifetime — stable across Process calls, unlike the per-render font-dict wrappers),
    // then by /BaseFont. Never serialized.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IO.PdfReader,
        System.Collections.Concurrent.ConcurrentDictionary<string, string>> _stickyFallback = new();

    /// <summary>Font name used to render simple-font runs whose own program can't be
    /// resolved (not embedded and no host match). Wired from
    /// <see cref="RenderingOptions.DefaultFontName"/>; when null a system font matching
    /// the run's /BaseFont (or Arial) is used. Without this such text is dropped entirely.</summary>
    internal string? DefaultFontName { get; set; }

    // Optional-content (layer) visibility: OCGs the default config marks hidden, plus a
    // stack tracking whether the current marked-content scope is inside a hidden layer.
    private HashSet<PdfDictionary>? _ocgHidden;
    private readonly Stack<bool> _ocgHiddenStack = new();
    private bool IsContentHidden
    {
        get { foreach (var h in _ocgHiddenStack) if (h) return true; return false; }
    }

    private static void SafeDraw(System.Action a)
    {
        try { a(); }
        catch { /* one malformed op must not abort the whole page */ }
    }

    // ── Coordinate transform ────────────────────────────────────────

    /// <summary>
    /// Build the world transform mapping PDF user space (after the supplied CTM) to
    /// device pixels: user → device via CTM, device → pixel via the page matrix
    /// (scale + Y-flip). Setting this on the Graphics lets GDI+ transform geometry,
    /// scale pen widths, and place images natively.
    /// </summary>
    private GdiMatrix WorldMatrix(double[] ctm)
    {
        var m = new GdiMatrix((float)ctm[0], (float)ctm[1], (float)ctm[2], (float)ctm[3], (float)ctm[4], (float)ctm[5]);
        // device(tx,ty) → pixel: px = scale*tx - scale*LLX; py = pixelH - scaleY*ty + scaleY*LLY
        var page = new GdiMatrix((float)_scale, 0f, 0f, (float)-_scaleY,
            (float)(-_scale * _mediaBox.LLX), (float)(_pixelH + _scaleY * _mediaBox.LLY));
        m.Multiply(page, MatrixOrder.Append);
        return m;
    }

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
                    var db = path.GetBounds(world);
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
                        // Blend a semi-transparent fill in straight sRGB so it matches the
                        // reference. The page keeps gamma-corrected compositing for text AA;
                        // applying it to /ca fills composites them a few levels lighter and
                        // drifts them across the visual comparator's threshold. Scoped to the
                        // shape-fill call so glyph rendering is unaffected.
                        var savedCq = _g.CompositingQuality;
                        if (state.FillAlpha < 0.999) _g.CompositingQuality = CompositingQuality.AssumeLinear;
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
                using var pen = BuildPen(state);
                _g.DrawPath(pen, path);
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
        try
        {
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
            tg.PixelOffsetMode = PixelOffsetMode.HighQuality;
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
    /// the axial/radial shading painters; mesh and function-based shadings are skipped.
    /// </summary>
    private void FillWithShadingPattern(GraphicsPath path, GraphicsState state, PdfDictionary pd)
    {
        var shadingObj = pd.Get("Shading");
        if (shadingObj is null) return;
        var shading = ShadingBase.Parse(shadingObj, _reader);
        if (shading is not AxialShading && shading is not RadialShading) return;

        var patMatrix = ExtractFormMatrix(pd) ?? new double[] { 1, 0, 0, 1, 0, 0 };
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
        }
        finally { _g.Restore(savedGdi); }
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
            sgfx.PixelOffsetMode = PixelOffsetMode.HighQuality;
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
                    Rasterizer.BlendModes.Blend(mode, dr, dg, db, sr, sg, sb, out int br, out int bg, out int bb);
                    drow[i]     = (byte)(db + (bb - db) * a + 0.5);
                    drow[i + 1] = (byte)(dg + (bg - dg) * a + 0.5);
                    drow[i + 2] = (byte)(dr + (br - dr) * a + 0.5);
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
        if (string.IsNullOrEmpty(text) && (rawBytes is null || rawBytes.Length == 0)) return;
        // Modes 4-7 add glyphs to the clip path (accumulated in PaintGlyph); mode 7 is
        // clip-only (no paint). PaintGlyph reads this to decide accumulate vs. fill.
        _curTextMode = state.RenderingMode;

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

        if (cid is not null && cid.IsTwoByteEncoding && rawBytes is not null)
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
            if ((charWidth == 0 || (ch > 0xFF && (charWidth == 500 || charWidth <= 0))
                 || (useFallback && metrics is null))
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
        for (int i = 0; i + 1 < rawBytes.Length; i += 2)
        {
            int code = (rawBytes[i] << 8) | rawBytes[i + 1];
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

        _g.Transform = world;
        try { _g.FillPath(brush, path); }
        finally { _g.Transform = saved; }
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
            if (shading is AxialShading ax) DrawAxialShading(ax, state, fillRegion: state.SoftMask is null);
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

    private void DrawAxialShading(AxialShading ax, GraphicsState state, bool fillRegion = false)
    {
        // Only paint when filling a defined region (a shading-pattern fill clipped to its
        // path). The bare `sh` operator paints the whole current clip; some documents use it
        // for opaque field-highlight overlays that, without the transparency they carry,
        // would cover content drawn under them. Leave that path until soft masks compose.
        if (!fillRegion) return;
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
            _g.FillRectangle(brush, bounds);
        }
        catch { /* GDI+ rejects some degenerate gradients */ }
        finally { _g.Transform = saved; }
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
            using var ellipse = new GraphicsPath();
            ellipse.AddEllipse((float)(cx - rr), (float)(cy - rr), (float)(2 * rr), (float)(2 * rr));
            using var brush = new PathGradientBrush(ellipse) { CenterPoint = new PointF((float)cx, (float)cy) };
            // Center = the smaller-radius end colour, surround = larger-radius end colour.
            var centerColor = outerIsOne ? colors[0] : colors[^1];
            var edgeColor = outerIsOne ? colors[^1] : colors[0];
            brush.CenterColor = centerColor;
            brush.SurroundColors = new[] { edgeColor };
            _g.FillPath(brush, ellipse);
        }
        catch { /* degenerate radial */ }
        finally { _g.Transform = saved; }
    }

    // ── Mesh shadings (Types 4-7, §8.7.4.5) ─────────────────────────
    //
    // GDI+ has no Gouraud/patch gradient primitive, so the mesh geometry is
    // tessellated and each small cell is flat-filled with its (bilinearly /
    // averaged) colour. At the cell sizes used here the facet steps fall below
    // the visual-comparison threshold. The current GDI clip (installed by the
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
        var saved = _g.Transform;
        using var world = WorldMatrix(ctm);
        _g.Transform = world;
        // Composite a semi-transparent image in straight sRGB (no gamma) so its alpha
        // blend matches the platform renderer — the same reason the shape-fill path
        // forces AssumeLinear (PDF §11.3.6 composites in the device colour space, not
        // linear light). Without this a soft-masked overlay (e.g. a slide's translucent
        // blue/photo panels) composites a few levels too light and drifts off the
        // visual comparator. Opaque images are unaffected (src fully replaces dst).
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
            else if (alpha < 0.999)
            {
                // Apply the /ca fill alpha to the whole image via an alpha-scaling
                // colour matrix (e.g. a 0.5-opacity image watermark).
                using var ia = new ImageAttributes();
                var cm = new ColorMatrix { Matrix33 = (float)Math.Max(0.0, Math.Min(1.0, alpha)) };
                ia.SetColorMatrix(cm);
                _g.DrawImage(bmp, dest, new RectangleF(0, 0, bmp.Width, bmp.Height),
                    GraphicsUnit.Pixel, ia);
            }
            else _g.DrawImage(bmp, dest);
        }
        finally { _g.Transform = saved; _g.CompositingQuality = savedCq; }
    }

    /// <summary>
    /// Composite an image onto the page with the Multiply blend used to approximate
    /// overprint (PDF 32000 §8.6.7): out = dst·src/255. A "white" (no-ink) source
    /// pixel leaves the destination unchanged, so an overprinted spot plate tints the
    /// process colour beneath it instead of knocking it out. The image is rasterised
    /// into a scratch layer (honouring the active transform and clip, matching the
    /// native blit) then multiplied into the backing bitmap per pixel.
    /// </summary>
    private void BlitImageMultiply(Bitmap bmp, GdiMatrix world, PointF[] dest)
    {
        int w = _bitmap.Width, h = _bitmap.Height;

        // Device-space bounds of the destination parallelogram (3 given corners plus
        // the implied fourth), clamped to the canvas.
        var corners = new[] { dest[0], dest[1], dest[2], new PointF(dest[1].X + dest[2].X - dest[0].X, dest[1].Y + dest[2].Y - dest[0].Y) };
        world.TransformPoints(corners);
        float fminX = corners[0].X, fminY = corners[0].Y, fmaxX = corners[0].X, fmaxY = corners[0].Y;
        foreach (var c in corners) { fminX = Math.Min(fminX, c.X); fminY = Math.Min(fminY, c.Y); fmaxX = Math.Max(fmaxX, c.X); fmaxY = Math.Max(fmaxY, c.Y); }
        int x0 = Math.Max(0, (int)Math.Floor(fminX)), y0 = Math.Max(0, (int)Math.Floor(fminY));
        int x1 = Math.Min(w, (int)Math.Ceiling(fmaxX)), y1 = Math.Min(h, (int)Math.Ceiling(fmaxY));
        if (x1 <= x0 || y1 <= y0) return;

        // Rasterise the image into the scratch layer with the same transform and clip.
        _blendScratch ??= new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var sgfx = Graphics.FromImage(_blendScratch))
        {
            sgfx.Clear(GdiColor.Transparent);
            sgfx.InterpolationMode = InterpolationMode.HighQualityBicubic;
            sgfx.PixelOffsetMode = PixelOffsetMode.HighQuality;
            sgfx.CompositingQuality = CompositingQuality.HighQuality;
            sgfx.Transform = world;
            sgfx.Clip = _g.Clip;
            sgfx.DrawImage(bmp, dest);
        }

        _g.Flush();
        var rect = new System.Drawing.Rectangle(x0, y0, x1 - x0, y1 - y0);
        var dst = _bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var src = _blendScratch.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            // LockBits with a sub-rectangle returns Scan0 at the rect origin but the full
            // image stride, so copy only the rect's row width to avoid overrunning the row.
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
                    int sa = srow[i + 3]; // BGRA: scratch coverage/alpha
                    if (sa == 0) continue;
                    double a = sa / 255.0;
                    for (int c = 0; c < 3; c++)
                    {
                        int d = drow[i + c];
                        int m = d * srow[i + c] / 255; // Multiply
                        drow[i + c] = (byte)(d + (m - d) * a + 0.5);
                    }
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

    /// <summary>
    /// Blit an image while modulating its coverage by an ExtGState soft mask (and the /ca
    /// fill alpha) per pixel — the image source-over the backing bitmap, scaled by the
    /// page-aligned mask alpha. Mirrors <see cref="BlitImageMultiply"/>'s scratch approach.
    /// </summary>
    private void BlitImageMasked(Bitmap bmp, GdiMatrix world, PointF[] dest, double alpha, byte[] softMask)
    {
        int w = _bitmap.Width, h = _bitmap.Height;
        var corners = new[] { dest[0], dest[1], dest[2], new PointF(dest[1].X + dest[2].X - dest[0].X, dest[1].Y + dest[2].Y - dest[0].Y) };
        world.TransformPoints(corners);
        float fminX = corners[0].X, fminY = corners[0].Y, fmaxX = corners[0].X, fmaxY = corners[0].Y;
        foreach (var c in corners) { fminX = Math.Min(fminX, c.X); fminY = Math.Min(fminY, c.Y); fmaxX = Math.Max(fmaxX, c.X); fmaxY = Math.Max(fmaxY, c.Y); }
        int x0 = Math.Max(0, (int)Math.Floor(fminX)), y0 = Math.Max(0, (int)Math.Floor(fminY));
        int x1 = Math.Min(w, (int)Math.Ceiling(fmaxX)), y1 = Math.Min(h, (int)Math.Ceiling(fmaxY));
        if (x1 <= x0 || y1 <= y0) return;

        _blendScratch ??= new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var sgfx = Graphics.FromImage(_blendScratch))
        {
            sgfx.Clear(GdiColor.Transparent);
            sgfx.InterpolationMode = InterpolationMode.HighQualityBicubic;
            sgfx.PixelOffsetMode = PixelOffsetMode.HighQuality;
            sgfx.CompositingQuality = CompositingQuality.HighQuality;
            sgfx.Transform = world;
            sgfx.Clip = _g.Clip;
            sgfx.DrawImage(bmp, dest);
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
                    int sa = srow[i + 3];
                    if (sa == 0) continue;
                    double a = sa / 255.0 * alpha * (softMask[(y0 + y) * w + (x0 + x)] / 255.0);
                    if (a <= 0.0) continue;
                    for (int c = 0; c < 3; c++)
                        drow[i + c] = (byte)(drow[i + c] + (srow[i + c] - drow[i + c]) * a + 0.5);
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

    private void DrawFormXObject(PdfStream formStream, GraphicsState state)
    {
        if (_formDepth > 64) return;
        _formDepth++;
        var savedScope = _scope;
        var savedGdi = _g.Save();
        try
        {
            byte[] content;
            try { content = _reader.DecodeStream(formStream); }
            catch { return; }

            var formResources = _reader.ResolveDict(formStream.Dict.Get("Resources"));
            var formScope = BuildScope(formResources);
            // Merge parent resources for fallback lookups (PDF 32000 §8.10 forms may
            // reference names defined only in the enclosing scope).
            MergeInto(formScope.XObjects, savedScope.XObjects);
            MergeInto(formScope.Fonts, savedScope.Fonts);
            MergeInto(formScope.ExtGStates, savedScope.ExtGStates);
            formScope.Patterns ??= savedScope.Patterns;
            formScope.Shadings ??= savedScope.Shadings;
            formScope.ColorSpaces ??= savedScope.ColorSpaces;
            formScope.Properties ??= savedScope.Properties;
            _scope = formScope;

            var formMatrix = ExtractFormMatrix(formStream.Dict);
            var effectiveCtm = formMatrix is not null
                ? GraphicsState.MultiplyMatrices(formMatrix, state.Ctm)
                : (double[])state.Ctm.Clone();

            var bboxClip = BuildBBoxClip(formStream.Dict, effectiveCtm);

            // Transparency group compositing (PDF 32000 §11.6.6): when the form is a
            // transparency group invoked with a non-trivial composite — group fill-alpha
            // (ca via /gs at the Do) below 1, a non-Normal blend mode, or an active soft
            // mask — its contents must render onto a transparent backdrop in a separate
            // layer, which is then composited back to the page at the Do-time alpha /
            // blend / mask. Drawing the contents straight onto the page (the else branch)
            // ignores the group alpha entirely, producing opaque overlays where the
            // reference shows blended, semi-transparent overlap.
            var groupDict = _reader.ResolveDict(formStream.Dict.Get("Group"));
            bool isTransparencyGroup = groupDict is not null && groupDict.GetName("S") == "Transparency";
            bool needsComposite = isTransparencyGroup &&
                (state.FillAlpha < 0.999
                 || (!string.IsNullOrEmpty(state.BlendMode) && state.BlendMode != "Normal")
                 || state.SoftMask is not null);

            if (needsComposite)
            {
                // /I true = isolated group: contents blend against a transparent backdrop.
                // Default (/I false) = non-isolated: contents blend against the page backdrop,
                // so backdrop-dependent blend modes (e.g. Difference vs the white page) resolve
                // correctly only if the group renders onto a copy of that backdrop.
                bool isolated = groupDict!.Get("I") is PdfBoolean iso && iso.Value;
                RenderGroupComposited(content, effectiveCtm, bboxClip, state, isolated);
            }
            else
                RenderContentStream(content, effectiveCtm, bboxClip);
            bboxClip?.Dispose();
        }
        finally
        {
            _scope = savedScope;
            _g.Restore(savedGdi);
            _formDepth--;
        }
    }

    /// <summary>
    /// Render a transparency-group form into a transparent page-sized layer, then
    /// composite that layer back onto the backing bitmap at the Do-time group
    /// alpha / blend mode / soft mask. Mirrors the software renderer's scratch-buffer
    /// group path (CompositeGroupBuffer). The layer renders with a fresh graphics state
    /// (group alpha applies once, at composite time — not inherited into the contents),
    /// inheriting only the outer device-space clip so the group stays bounded.
    /// </summary>
    // Device-space pixel rectangle a transparency group can actually touch: its
    // /BBox (forms clip their content to it) transformed to device space, clamped
    // to the page. Used to bound the backdrop copy and the composite loop so a
    // small group on a huge page costs O(group area), not O(page area) — without
    // this, a 4362×3622pt page at 300 dpi with many groups composites ~274M
    // pixels per group and effectively never finishes.
    private static System.Drawing.Rectangle GroupDeviceBounds(GraphicsPath? bboxClip, int w, int h)
    {
        if (bboxClip is null) return new System.Drawing.Rectangle(0, 0, w, h);
        var b = bboxClip.GetBounds();
        int x0 = Math.Max(0, (int)Math.Floor(b.Left));
        int y0 = Math.Max(0, (int)Math.Floor(b.Top));
        int x1 = Math.Min(w, (int)Math.Ceiling(b.Right));
        int y1 = Math.Min(h, (int)Math.Ceiling(b.Bottom));
        return (x1 > x0 && y1 > y0) ? new System.Drawing.Rectangle(x0, y0, x1 - x0, y1 - y0)
                                    : System.Drawing.Rectangle.Empty;
    }

    // Rent a page-sized ARGB layer bitmap from the pool, or allocate one if the pool
    // is empty (or holds a stale-sized bitmap from a different page). Pooled bitmaps
    // carry stale pixels; callers must re-initialise the region they composite.
    private Bitmap RentLayer(int w, int h)
    {
        while (_layerPool.Count > 0)
        {
            var b = _layerPool.Pop();
            if (b.Width == w && b.Height == h) return b;
            b.Dispose();
        }
        return new Bitmap(w, h, PixelFormat.Format32bppArgb);
    }

    private void RenderGroupComposited(byte[] content, double[] effectiveCtm, GraphicsPath? bboxClip, GraphicsState state, bool isolated)
    {
        int w = _bitmap.Width, h = _bitmap.Height;

        // The group only paints within its /BBox; compositing (and the non-isolated
        // backdrop copy) outside that rect is wasted work on large pages.
        var compRect = GroupDeviceBounds(bboxClip, w, h);
        if (compRect.Width <= 0 || compRect.Height <= 0) return; // group maps off-page

        // Capture the inherited clip in device space so the group respects any clip
        // active at the Do (e.g. a page-level `re W n`). GDI+ stores the clip in device
        // coordinates; read it with an identity transform to get device-space geometry.
        var savedT = _g.Transform;
        _g.ResetTransform();
        var deviceClip = _g.Clip;
        _g.Transform = savedT;
        savedT.Dispose();

        var groupBmp = RentLayer(w, h);
        var savedG = _g;
        var savedBmp = _bitmap;
        var savedScratch = _blendScratch;
        var gg = Graphics.FromImage(groupBmp);
        try
        {
            // Re-initialise only the BBox sub-rect (the pooled bitmap may carry stale
            // content elsewhere, which is never composited). SourceCopy writes the
            // exact pixels, overwriting any leftovers.
            gg.CompositingMode = CompositingMode.SourceCopy;
            if (isolated)
                // Isolated → transparent backdrop under the BBox.
                using (var clear = new SolidBrush(GdiColor.Transparent))
                    gg.FillRectangle(clear, compRect);
            else
                // Non-isolated → start from a copy of the current page backdrop under the
                // BBox so internal blend modes see it (the layer is then lerped back by the
                // group alpha, leaving untouched pixels equal to backdrop).
                gg.DrawImage(savedBmp, compRect, compRect, GraphicsUnit.Pixel);
            gg.CompositingMode = CompositingMode.SourceOver;
            gg.SmoothingMode = savedG.SmoothingMode;
            gg.PixelOffsetMode = savedG.PixelOffsetMode;
            gg.InterpolationMode = savedG.InterpolationMode;
            gg.TextRenderingHint = savedG.TextRenderingHint;
            gg.CompositingQuality = savedG.CompositingQuality;
            if (deviceClip is not null) gg.Clip = deviceClip;

            _g = gg;
            _bitmap = groupBmp;
            _blendScratch = null; // a fresh same-size scratch is allocated on demand for blended fills inside the group
            RenderContentStream(content, effectiveCtm, bboxClip);
            _g.Flush();
        }
        finally
        {
            _g = savedG;
            _bitmap = savedBmp;
            _blendScratch?.Dispose();
            _blendScratch = savedScratch;
            gg.Dispose();
            deviceClip?.Dispose();
        }

        // Isolated layers carry the Do-time blend mode (they composite onto the page like
        // any source). Non-isolated layers already blended internally against the backdrop
        // copy, so they composite back with a plain Normal lerp (untouched pixels equal the
        // backdrop and stay unchanged).
        CompositeGroupLayer(groupBmp, state, isolated ? state.BlendMode : "Normal", compRect);
        _layerPool.Push(groupBmp); // return to pool for reuse instead of disposing
    }

    /// <summary>
    /// Per-pixel composite of a rendered group layer onto the backing bitmap using the
    /// general PDF "over" formula with backdrop alpha (so a layer composited onto another
    /// transparent group layer is not darkened toward black). a = srcAlpha·groupAlpha·softMask.
    /// </summary>
    private void CompositeGroupLayer(Bitmap layer, GraphicsState state, string blendMode, System.Drawing.Rectangle bounds)
    {
        int w = _bitmap.Width, h = _bitmap.Height;
        double ga = System.Math.Clamp(state.FillAlpha, 0.0, 1.0);
        if (ga <= 0.0) return;
        // Clamp the work region to the page; only this rectangle (the group's BBox)
        // can contain non-transparent layer pixels.
        bounds = System.Drawing.Rectangle.Intersect(bounds, new System.Drawing.Rectangle(0, 0, w, h));
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var mode = Rasterizer.BlendModes.Parse(blendMode);
        byte[]? softMask = state.SoftMask is { } sm ? GetSoftMaskAlpha(sm) : null;

        _g.Flush();
        var rect = new System.Drawing.Rectangle(0, 0, w, h);
        var dst = _bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var src = layer.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int x0 = bounds.Left, x1 = bounds.Right;
            int segBytes = (x1 - x0) * 4;
            var drow = new byte[segBytes];
            var srow = new byte[segBytes];
            for (int y = bounds.Top; y < bounds.Bottom; y++)
            {
                long dRow = dst.Scan0.ToInt64() + (long)y * dst.Stride + (long)x0 * 4;
                long sRow = src.Scan0.ToInt64() + (long)y * src.Stride + (long)x0 * 4;
                System.Runtime.InteropServices.Marshal.Copy((IntPtr)dRow, drow, 0, segBytes);
                System.Runtime.InteropServices.Marshal.Copy((IntPtr)sRow, srow, 0, segBytes);
                bool dirty = false;
                for (int x = x0; x < x1; x++)
                {
                    int i = (x - x0) * 4;
                    int sa = srow[i + 3]; // BGRA straight alpha (group coverage)
                    if (sa == 0) continue;
                    double a = sa / 255.0 * ga; // effective source alpha
                    if (softMask is not null) a *= softMask[y * w + x] / 255.0;
                    if (a <= 0.0) continue;
                    int sb = srow[i], sg = srow[i + 1], sr = srow[i + 2];
                    int db = drow[i], dg = drow[i + 1], dr = drow[i + 2];
                    double dn = drow[i + 3] / 255.0; // backdrop alpha (0 for a transparent group layer, 1 for the page)

                    // PDF 32000 §11.3.6 general "over" with blend and backdrop alpha:
                    //   Cs' = (1-αb)·Cs + αb·B(Cb,Cs)          (blend only acts where a backdrop exists)
                    //   αr  = a + αb·(1-a)
                    //   Cr  = (Cs'·a + Cb·αb·(1-a)) / αr        (straight, un-premultiplied)
                    // With αb=1 (opaque page) this reduces to Cr = B·a + Cb·(1-a); with
                    // αb=0 (transparent nested layer) to Cr = Cs — no darkening toward black.
                    double bbr = sr, bbg = sg, bbb = sb;
                    if (mode != Rasterizer.BlendMode.Normal && dn > 0.0)
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
                    System.Runtime.InteropServices.Marshal.Copy(drow, 0, (IntPtr)dRow, segBytes);
            }
        }
        finally
        {
            _bitmap.UnlockBits(dst);
            layer.UnlockBits(src);
        }
    }

    private static void MergeInto<T>(Dictionary<string, T>? target, Dictionary<string, T>? source)
    {
        if (target is null || source is null) return;
        foreach (var kv in source) target.TryAdd(kv.Key, kv.Value);
    }

    private static double[]? ExtractFormMatrix(PdfDictionary dict)
    {
        if (dict.Get("Matrix") is not PdfArray arr || arr.Count < 6) return null;
        var m = new double[6];
        for (int i = 0; i < 6; i++) m[i] = NumFrom(arr[i]);
        return m;
    }

    private GraphicsPath? BuildBBoxClip(PdfDictionary dict, double[] ctm)
    {
        if (dict.Get("BBox") is not PdfArray arr || arr.Count < 4) return null;
        double x0 = NumFrom(arr[0]), y0 = NumFrom(arr[1]), x1 = NumFrom(arr[2]), y1 = NumFrom(arr[3]);
        var segs = new[]
        {
            new PathCommand(PathOp.MoveTo, x0, y0),
            new PathCommand(PathOp.LineTo, x1, y0),
            new PathCommand(PathOp.LineTo, x1, y1),
            new PathCommand(PathOp.LineTo, x0, y1),
            new PathCommand(PathOp.Close),
        };
        var path = BuildPath(segs, evenOdd: false);
        using var world = WorldMatrix(ctm);
        path.Transform(world);
        return path;
    }

    internal static double NumFrom(PdfObject? o) => o switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0.0,
    };

    // ── Annotations ─────────────────────────────────────────────────

    private void DrawAnnotations(PdfDictionary pageDict)
    {
        if (_reader.Resolve(pageDict.Get("Annots")) is not PdfArray annots) return;
        foreach (var item in annots)
        {
            var annot = _reader.ResolveDict(item);
            if (annot is null) continue;
            var flags = (int)annot.GetInt("F");
            if ((flags & 0x02) != 0) continue; // Hidden
            var subtype = annot.GetName("Subtype");

            if (_reader.ResolveDict(annot.Get("AP")) is not null)
            {
                SafeDraw(() => DrawAppearanceAnnotation(annot));
                continue;
            }
            if (subtype == "Square" || subtype == "Circle")
                SafeDraw(() => DrawSquareCircleDefault(annot, subtype == "Circle"));
            else if (subtype == "Highlight")
                SafeDraw(() => DrawHighlightDefault(annot));
            else if (subtype == "Popup")
            {
                var form = Aspose.Pdf.Annotations.PopupAppearance.BuildOpenPopupForm(annot, _reader);
                if (form is not null) SafeDraw(() => DrawAppearanceForm(annot, form));
            }
        }
    }

    private void DrawAppearanceAnnotation(PdfDictionary annot)
    {
        var ap = _reader.ResolveDict(annot.Get("AP"));
        if (ap is null) return;
        var nEntry = _reader.Resolve(ap.Get("N"));
        PdfStream? formStream = null;
        if (nEntry is PdfStream direct) formStream = direct;
        else if (nEntry is PdfDictionary stateDict)
        {
            var asName = annot.GetName("AS");
            if (!string.IsNullOrEmpty(asName)) formStream = _reader.ResolveStream(stateDict.Get(asName));
        }
        if (formStream is null) return;
        DrawAppearanceForm(annot, formStream);
    }

    /// <summary>Paint a Form XObject appearance into the annotation's /Rect, mapping the
    /// form's transformed /BBox onto the rectangle (PDF 32000 §12.5.5). Shared by the
    /// /AP path and by synthesised appearances (e.g. open popups).</summary>
    private void DrawAppearanceForm(PdfDictionary annot, PdfStream formStream)
    {
        if (annot.Get("Rect") is not PdfArray rect || rect.Count < 4) return;

        double rx1 = NumFrom(rect[0]), ry1 = NumFrom(rect[1]), rx2 = NumFrom(rect[2]), ry2 = NumFrom(rect[3]);
        double rMinX = Math.Min(rx1, rx2), rMaxX = Math.Max(rx1, rx2);
        double rMinY = Math.Min(ry1, ry2), rMaxY = Math.Max(ry1, ry2);
        double rW = rMaxX - rMinX, rH = rMaxY - rMinY;
        if (rW <= 0 || rH <= 0) return;

        if (formStream.Dict.Get("BBox") is not PdfArray bbox || bbox.Count < 4) return;
        double bx1 = NumFrom(bbox[0]), by1 = NumFrom(bbox[1]), bx2 = NumFrom(bbox[2]), by2 = NumFrom(bbox[3]);
        var fm = ExtractFormMatrix(formStream.Dict) ?? new double[] { 1, 0, 0, 1, 0, 0 };
        double tMinX = double.PositiveInfinity, tMinY = double.PositiveInfinity, tMaxX = double.NegativeInfinity, tMaxY = double.NegativeInfinity;
        foreach (var (cx, cy) in new[] { (bx1, by1), (bx2, by1), (bx2, by2), (bx1, by2) })
        {
            var tx = fm[0] * cx + fm[2] * cy + fm[4];
            var ty = fm[1] * cx + fm[3] * cy + fm[5];
            if (tx < tMinX) tMinX = tx; if (tx > tMaxX) tMaxX = tx;
            if (ty < tMinY) tMinY = ty; if (ty > tMaxY) tMaxY = ty;
        }
        double tW = tMaxX - tMinX, tH = tMaxY - tMinY;
        if (tW <= 0 || tH <= 0) return;
        double sx = rW / tW, sy = rH / tH;
        var outerCtm = new double[] { sx, 0, 0, sy, rMinX - tMinX * sx, rMinY - tMinY * sy };

        DrawFormXObject(formStream, new GraphicsState { Ctm = outerCtm });
    }

    private void DrawSquareCircleDefault(PdfDictionary annot, bool isCircle)
    {
        if (annot.Get("Rect") is not PdfArray rect || rect.Count < 4) return;
        double rx1 = NumFrom(rect[0]), ry1 = NumFrom(rect[1]), rx2 = NumFrom(rect[2]), ry2 = NumFrom(rect[3]);
        float x = (float)Math.Min(rx1, rx2), y = (float)Math.Min(ry1, ry2);
        float w = (float)Math.Abs(rx2 - rx1), h = (float)Math.Abs(ry2 - ry1);
        if (w <= 0 || h <= 0) return;

        var saved = _g.Transform;
        using var world = WorldMatrix(new double[] { 1, 0, 0, 1, 0, 0 });
        _g.Transform = world;
        try
        {
            var ic = ParseAnnotColor(annot.Get("IC"));
            var bc = ParseAnnotColor(annot.Get("C"));
            if (ic is { } fillCol)
            {
                using var b = new SolidBrush(fillCol);
                if (isCircle) _g.FillEllipse(b, x, y, w, h); else _g.FillRectangle(b, x, y, w, h);
            }
            float bw = 1f;
            if (_reader.ResolveDict(annot.Get("BS")) is { } bs) bw = (float)NumFrom(bs.Get("W"));
            if (bw > 0 && bc is { } borderCol)
            {
                using var p = new Pen(borderCol, bw);
                if (isCircle) _g.DrawEllipse(p, x, y, w, h); else _g.DrawRectangle(p, x, y, w, h);
            }
        }
        finally { _g.Transform = saved; }
    }

    /// <summary>Default appearance for a text-markup Highlight annotation that
    /// carries no /AP stream: paint each QuadPoints quadrilateral (or the /Rect when
    /// QuadPoints is absent) in the annotation colour using the Multiply blend mode
    /// (PDF 32000 §12.5.6.10), so underlying text shows through.</summary>
    private void DrawHighlightDefault(PdfDictionary annot)
    {
        var col = ParseAnnotColor(annot.Get("C"));
        if (col is not { } c) return;

        using var path = new GraphicsPath { FillMode = FillMode.Winding };
        if (_reader.Resolve(annot.Get("QuadPoints")) is PdfArray qp && qp.Count >= 8 && qp.Count % 8 == 0)
        {
            for (int i = 0; i + 7 < qp.Count; i += 8)
            {
                // Each quad: (x1,y1)(x2,y2)(x3,y3)(x4,y4). Use the quad's bounding box.
                double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
                double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
                for (int j = 0; j < 8; j += 2)
                {
                    double px = NumFrom(qp[i + j]), py = NumFrom(qp[i + j + 1]);
                    if (px < minX) minX = px; if (px > maxX) maxX = px;
                    if (py < minY) minY = py; if (py > maxY) maxY = py;
                }
                float w = (float)(maxX - minX), h = (float)(maxY - minY);
                if (w > 0 && h > 0) path.AddRectangle(new RectangleF((float)minX, (float)minY, w, h));
            }
        }
        else if (annot.Get("Rect") is PdfArray rect && rect.Count >= 4)
        {
            double rx1 = NumFrom(rect[0]), ry1 = NumFrom(rect[1]), rx2 = NumFrom(rect[2]), ry2 = NumFrom(rect[3]);
            float x = (float)Math.Min(rx1, rx2), y = (float)Math.Min(ry1, ry2);
            float w = (float)Math.Abs(rx2 - rx1), h = (float)Math.Abs(ry2 - ry1);
            if (w <= 0 || h <= 0) return;
            path.AddRectangle(new RectangleF(x, y, w, h));
        }
        if (path.PointCount == 0) return;

        using var world = WorldMatrix(new double[] { 1, 0, 0, 1, 0, 0 });
        FillPathBlended(path, world, Rasterizer.BlendMode.Multiply,
            new GraphicsState { FillR = c.R / 255.0, FillG = c.G / 255.0, FillB = c.B / 255.0, FillAlpha = 1.0 });
    }

    private GdiColor? ParseAnnotColor(PdfObject? o)
    {
        if (_reader.Resolve(o) is not PdfArray arr) return null;
        switch (arr.Count)
        {
            case 1: { int v = Clamp255(NumFrom(arr[0])); return GdiColor.FromArgb(v, v, v); }
            case 3: return GdiColor.FromArgb(Clamp255(NumFrom(arr[0])), Clamp255(NumFrom(arr[1])), Clamp255(NumFrom(arr[2])));
            case 4:
                double c = NumFrom(arr[0]), m = NumFrom(arr[1]), yv = NumFrom(arr[2]), k = NumFrom(arr[3]);
                return GdiColor.FromArgb(Clamp255((1 - c) * (1 - k)), Clamp255((1 - m) * (1 - k)), Clamp255((1 - yv) * (1 - k)));
            default: return null; // empty array = transparent / no colour
        }
    }

    // ── Output ──────────────────────────────────────────────────────

    /// <summary>Convert a 32bpp ARGB GDI+ bitmap (BGRA byte order) to an RGBA buffer.</summary>
    private static RgbaBuffer ToRgbaBuffer(Bitmap bmp, int w, int h)
    {
        var data = new byte[w * h * 4];
        var rect = new System.Drawing.Rectangle(0, 0, w, h);
        var bits = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var stride = bits.Stride;
            var scan = bits.Scan0;
            var row = new byte[stride];
            for (int y = 0; y < h; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(scan + y * stride, row, 0, stride);
                int di = y * w * 4;
                for (int x = 0; x < w; x++)
                {
                    int si = x * 4;
                    // GDI+ Format32bppArgb is little-endian BGRA in memory.
                    data[di + 0] = row[si + 2]; // R
                    data[di + 1] = row[si + 1]; // G
                    data[di + 2] = row[si + 0]; // B
                    data[di + 3] = row[si + 3]; // A
                    di += 4;
                }
            }
        }
        finally { bmp.UnlockBits(bits); }
        return new RgbaBuffer(data, w, h);
    }

    // ── Image decoding (PDF image XObject / inline → GDI+ bitmap) ────

    /// <summary>
    /// Decodes PDF image streams into 32bpp ARGB GDI+ bitmaps oriented top-row-first.
    /// Image masks bake the current fill colour and per-pixel opacity into the alpha
    /// channel; soft masks (/SMask) are sampled into alpha. GDI+ then resamples with
    /// high-quality bicubic interpolation when the bitmap is placed.
    /// </summary>
    private static class ImageDecoder
    {
        public static Bitmap? TryDecode(PdfStream xobj, GraphicsState state, PdfReader reader)
        {
            var dict = xobj.Dict;
            byte[] decoded;
            try { decoded = reader.DecodeStream(xobj); }
            catch { return null; }
            return Build(dict, decoded, state, reader);
        }

        public static Bitmap? TryDecodeInline(PdfDictionary dict, byte[] data, GraphicsState state, PdfReader reader)
        {
            byte[] decoded;
            try { decoded = Aspose.Pdf.IO.Filters.StreamFilter.Decode(data, dict); }
            catch { return null; }
            return Build(dict, decoded, state, reader);
        }

        private static Bitmap? Build(PdfDictionary dict, byte[] decoded, GraphicsState state, PdfReader reader)
        {
            var w = (int)dict.GetInt("Width");
            var h = (int)dict.GetInt("Height");
            if (w <= 0 || h <= 0) return null;

            if (dict.Get("ImageMask") is PdfBoolean imb && imb.Value)
            {
                var invert = false;
                if (dict.Get("Decode") is PdfArray dec && dec.Count >= 2)
                    invert = NumFrom(dec[0]) > NumFrom(dec[1]);
                return BuildMask(decoded, w, h, state, invert);
            }

            var bpc = (int)dict.GetInt("BitsPerComponent");
            if (bpc == 0) bpc = 8;
            var csInfo = SoftwarePageRenderer.ResolveImageColorSpace(dict.Get("ColorSpace"), reader);

            // JPEG carried verbatim through DCTDecode.
            if (decoded.Length > 2 && decoded[0] == 0xFF && decoded[1] == 0xD8)
            {
                try
                {
                    var (pixels, jw, jh, comps) = Aspose.Pdf.IO.Filters.JpegDecoder.Decode(decoded);
                    byte[] bgraJ;
                    if (comps == 1 && csInfo.TintTransform is not null)
                        bgraJ = SeparationToBgra(pixels, jw, jh,
                            SoftwarePageRenderer.BuildSeparationLut(csInfo, DecodeInverts(dict)));
                    else
                        bgraJ = comps == 1 ? GrayToBgra(pixels, jw, jh) : RgbToBgra(pixels, jw, jh);
                    var (mb, mw, mh) = ApplyMasks(dict, reader, bgraJ, jw, jh);
                    return FromBgra(mb, mw, mh);
                }
                catch { return null; }
            }

            // JPEG 2000 (JPXDecode): raw codestream (FF4F) or JP2 box wrapper.
            bool isJ2k = (decoded.Length > 3 && decoded[0] == 0xFF && decoded[1] == 0x4F)
                || (decoded.Length > 12 && decoded[0] == 0x00 && decoded[1] == 0x00 && decoded[2] == 0x00 && decoded[3] == 0x0C
                    && decoded[4] == 0x6A && decoded[5] == 0x50);
            if (isJ2k)
            {
                if (Aspose.Pdf.IO.Filters.JpxDecoder.TryDecode(decoded, out var jp, out var jw, out var jh, out var jc))
                {
                    var bgraJ = jc >= 3 ? RgbToBgra(jp, jw, jh) : GrayToBgra(jp, jw, jh);
                    var (mb, mw, mh) = ApplyMasks(dict, reader, bgraJ, jw, jh);
                    return FromBgra(mb, mw, mh);
                }
                return null;
            }

            byte[]? bgra = null;
            if (csInfo.Palette is not null)
                bgra = IndexedToBgra(decoded, w, h, bpc, csInfo);
            else if (csInfo.TintTransform is not null && bpc == 8 && decoded.Length >= w * h)
                bgra = SeparationToBgra(decoded, w, h,
                    SoftwarePageRenderer.BuildSeparationLut(csInfo, DecodeInverts(dict)));
            else if (csInfo.TintTransform is not null && (bpc == 1 || bpc == 2 || bpc == 4))
                // Sub-byte /Separation (or /DeviceN) image: map each sample through the tint
                // transform LUT. Without this a 1-bpc spot image falls to BilevelToBgra, which
                // ignores the colorant (e.g. 35751_2's hanger renders inverted black-on-white).
                bgra = RgbToBgra(SoftwarePageRenderer.SeparationSamplesToRgb(decoded, w, h, bpc,
                    SoftwarePageRenderer.BuildSeparationLut(csInfo, SoftwarePageRenderer.GrayDecodeInverts(dict))), w, h);
            else if (csInfo.BaseName == "DeviceRGB" && bpc == 8 && decoded.Length >= w * h * 3)
                bgra = RgbToBgra(decoded, w, h);
            else if (csInfo.BaseName == "DeviceRGB" && (bpc == 1 || bpc == 2 || bpc == 4))
                // Sub-byte (3·bpc bits/pixel) DeviceRGB: unpack each component. Without this a
                // 1-bpc RGB image falls to BilevelToBgra (1 bit/pixel) and the rows desync.
                bgra = RgbToBgra(UnpackRgbSamples(decoded, w, h, bpc), w, h);
            else if (csInfo.BaseName == "DeviceGray" && bpc == 8 && decoded.Length >= w * h)
                bgra = GrayToBgra(decoded, w, h);
            else if (csInfo.BaseName == "DeviceGray" && (bpc == 2 || bpc == 4))
                bgra = GrayToBgra(SoftwarePageRenderer.UnpackGraySamples(decoded, w, h, bpc, SoftwarePageRenderer.GrayDecodeInverts(dict)), w, h);
            else if (csInfo.BaseName == "DeviceCMYK" && bpc == 8 && decoded.Length >= w * h * 4)
                bgra = CmykToBgra(decoded, w, h);
            else if (bpc == 1)
                // /Decode [1 0] (common on BlackIs1 CCITT scans) reverses the default
                // bit → gray mapping; without it such scans render white-on-black.
                bgra = BilevelToBgra(decoded, w, h, DecodeInverts(dict));

            if (bgra is null) return null;
            var (mbgra, mw2, mh2) = ApplyMasks(dict, reader, bgra, w, h);
            return FromBgra(mbgra, mw2, mh2);
        }

        // Unpack a sub-byte (1/2/4 bpc) three-component DeviceRGB image into packed 8-bit RGB.
        // Each pixel is 3·bpc bits; rows are byte-aligned. Without this a 1-bpc RGB image
        // (3 bits/pixel) is mis-read as 1-bit bilevel (1 bit/pixel), desyncing the rows.
        private static byte[] UnpackRgbSamples(byte[] data, int w, int h, int bpc)
        {
            var outp = new byte[w * h * 3];
            var rowBytes = (w * 3 * bpc + 7) / 8;
            var maxv = (1 << bpc) - 1;
            for (int y = 0; y < h; y++)
            {
                int rowBase = y * rowBytes;
                for (int x = 0; x < w; x++)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        int bitPos = (x * 3 + c) * bpc;
                        int bi = rowBase + (bitPos >> 3);
                        int shift = 8 - bpc - (bitPos & 7);
                        int sample = bi < data.Length ? (data[bi] >> shift) & maxv : 0;
                        outp[(y * w + x) * 3 + c] = (byte)(sample * 255 / maxv);
                    }
                }
            }
            return outp;
        }

        private static Bitmap BuildMask(byte[] decoded, int w, int h, GraphicsState state, bool invert)
        {
            byte r = (byte)Clamp255(state.FillR), g = (byte)Clamp255(state.FillG), b = (byte)Clamp255(state.FillB);
            byte paintAlpha = (byte)Clamp255(state.FillAlpha);
            // Default /Decode [0 1]: bit 0 paints the fill colour, bit 1 is transparent.
            int paintBit = invert ? 1 : 0;
            var rowBytes = (w + 7) / 8;
            var bgra = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                var rowBase = y * rowBytes;
                for (int x = 0; x < w; x++)
                {
                    var bi = rowBase + x / 8;
                    if (bi >= decoded.Length) continue;
                    var bit = (decoded[bi] >> (7 - (x & 7))) & 1;
                    if (bit != paintBit) continue; // transparent
                    var o = (y * w + x) * 4;
                    bgra[o + 0] = b; bgra[o + 1] = g; bgra[o + 2] = r; bgra[o + 3] = paintAlpha;
                }
            }
            return FromBgra(bgra, w, h);
        }

        private static byte[] RgbToBgra(byte[] rgb, int w, int h)
        {
            var bgra = new byte[w * h * 4];
            for (int i = 0, j = 0; i < w * h; i++, j += 4)
            {
                var s = i * 3;
                bgra[j + 0] = rgb[s + 2]; bgra[j + 1] = rgb[s + 1]; bgra[j + 2] = rgb[s + 0]; bgra[j + 3] = 255;
            }
            return bgra;
        }

        private static byte[] GrayToBgra(byte[] gray, int w, int h)
        {
            var bgra = new byte[w * h * 4];
            for (int i = 0, j = 0; i < w * h; i++, j += 4)
            {
                var v = gray[i];
                bgra[j + 0] = v; bgra[j + 1] = v; bgra[j + 2] = v; bgra[j + 3] = 255;
            }
            return bgra;
        }

        // Map a single-component (Separation/DeviceN) sample plane to BGRA via the
        // precomputed tint LUT (sample → spot tint → alternate space → RGB).
        private static byte[] SeparationToBgra(byte[] samples, int w, int h, byte[] lut)
        {
            var bgra = new byte[w * h * 4];
            int n = Math.Min(w * h, samples.Length);
            for (int i = 0, j = 0; i < n; i++, j += 4)
            {
                int l = samples[i] * 3;
                bgra[j + 0] = lut[l + 2]; bgra[j + 1] = lut[l + 1]; bgra[j + 2] = lut[l + 0]; bgra[j + 3] = 255;
            }
            return bgra;
        }

        // /Decode [1 0] on a 1-component image reverses the sample → tint mapping.
        private static bool DecodeInverts(PdfDictionary dict)
            => dict.Get("Decode") is PdfArray dec && dec.Count >= 2 && NumFrom(dec[0]) > NumFrom(dec[1]);

        private static byte[] CmykToBgra(byte[] cmyk, int w, int h)
        {
            var bgra = new byte[w * h * 4];
            for (int i = 0, j = 0; i < w * h; i++, j += 4)
            {
                var s = i * 4;
                double c = cmyk[s] / 255.0, m = cmyk[s + 1] / 255.0, yv = cmyk[s + 2] / 255.0, k = cmyk[s + 3] / 255.0;
                bgra[j + 0] = (byte)(255 * (1 - yv) * (1 - k));
                bgra[j + 1] = (byte)(255 * (1 - m) * (1 - k));
                bgra[j + 2] = (byte)(255 * (1 - c) * (1 - k));
                bgra[j + 3] = 255;
            }
            return bgra;
        }

        private static byte[] BilevelToBgra(byte[] data, int w, int h, bool invert = false)
        {
            var bgra = new byte[w * h * 4];
            var rowBytes = (w + 7) / 8;
            var inv = invert ? 1 : 0;
            for (int y = 0; y < h; y++)
            {
                var rowBase = y * rowBytes;
                for (int x = 0; x < w; x++)
                {
                    var bi = rowBase + x / 8;
                    byte v = 0;
                    if (bi < data.Length) v = (((data[bi] >> (7 - (x & 7))) & 1) ^ inv) == 1 ? (byte)255 : (byte)0;
                    var o = (y * w + x) * 4;
                    bgra[o + 0] = v; bgra[o + 1] = v; bgra[o + 2] = v; bgra[o + 3] = 255;
                }
            }
            return bgra;
        }

        private static byte[] IndexedToBgra(byte[] data, int w, int h, int bpc, SoftwarePageRenderer.ImageColorSpaceInfo csInfo)
        {
            var palette = csInfo.Palette!;
            var pc = csInfo.PaletteComponents;
            var bgra = new byte[w * h * 4];
            var rowBits = w * bpc;
            var rowBytes = (rowBits + 7) / 8;
            var maxIndex = pc > 0 ? palette.Length / pc - 1 : 0;
            for (int y = 0; y < h; y++)
            {
                var rowBase = y * rowBytes;
                for (int x = 0; x < w; x++)
                {
                    int idx = ReadBits(data, rowBase, x * bpc, bpc);
                    if (idx > maxIndex) idx = maxIndex;
                    PaletteRgb(palette, pc, csInfo.BaseName, idx, out byte r, out byte g, out byte b);
                    var o = (y * w + x) * 4;
                    bgra[o + 0] = b; bgra[o + 1] = g; bgra[o + 2] = r; bgra[o + 3] = 255;
                }
            }
            return bgra;
        }

        private static int ReadBits(byte[] data, int rowBase, int bitOffset, int bpc)
        {
            int value = 0;
            for (int i = 0; i < bpc; i++)
            {
                int bit = bitOffset + i;
                int bi = rowBase + bit / 8;
                int b = bi < data.Length ? (data[bi] >> (7 - (bit & 7))) & 1 : 0;
                value = (value << 1) | b;
            }
            return value;
        }

        private static void PaletteRgb(byte[] palette, int pc, string baseName, int idx, out byte r, out byte g, out byte b)
        {
            var p = idx * pc;
            if (p < 0 || p + pc > palette.Length) { r = g = b = 0; return; }
            switch (baseName)
            {
                case "DeviceGray":
                    r = g = b = palette[p];
                    break;
                case "DeviceCMYK":
                    double c = palette[p] / 255.0, m = palette[p + 1] / 255.0, yv = palette[p + 2] / 255.0, k = palette[p + 3] / 255.0;
                    r = (byte)(255 * (1 - c) * (1 - k));
                    g = (byte)(255 * (1 - m) * (1 - k));
                    b = (byte)(255 * (1 - yv) * (1 - k));
                    break;
                default: // DeviceRGB / Cal / ICC fallback
                    r = palette[p]; g = palette[p + 1]; b = palette[p + 2];
                    break;
            }
        }

        // Apply the /SMask soft mask and explicit /Mask stencil to a base-image BGRA buffer,
        // returning the (possibly resized) result. When the stencil is markedly higher
        // resolution than the base image the result is rebuilt at the stencil resolution so
        // its sharp edges survive the bicubic scale to the page — a low-res photo gated by a
        // high-res text stencil (40920) would otherwise lose ~half its strokes to point
        // sampling. Behaviour for soft-mask-only / equal-or-lower-res masks is unchanged.
        private static (byte[] bgra, int w, int h) ApplyMasks(PdfDictionary dict, PdfReader reader, byte[] bgra, int w, int h)
        {
            var alpha = SoftwarePageRenderer.ResolveSMaskAlpha(dict.Get("SMask"), reader, out var sw, out var sh);
            var stencil = SoftwarePageRenderer.ResolveStencilMaskAlpha(dict.Get("Mask"), reader, out var stw, out var sth);
            bool haveSMask = alpha is not null && sw > 0 && sh > 0;
            bool haveStencil = stencil is not null && stw > 0 && sth > 0;

            // Colour-key (chroma-key) masking: /Mask as a PdfArray of [min max] sample
            // ranges, one pair per colour component. A pixel whose samples all fall in
            // their range is fully transparent (PDF §8.9.6.4). Matched against the
            // post-conversion RGB buffer, so it is only applied to the 3-component (RGB)
            // form, where the buffer's R/G/B equal the raw samples for the device RGB
            // spaces. A 6-entry key cannot occur on a 1-component Indexed space, so this
            // avoids mis-masking Indexed/CMYK images whose buffer no longer holds samples.
            int[]? colorKey = null;
            if (reader.Resolve(dict.Get("Mask")) is PdfArray ck && ck.Count == 6)
            {
                colorKey = new int[ck.Count];
                for (int i = 0; i < ck.Count; i++) colorKey[i] = (int)NumFrom(ck[i]);
            }
            bool haveColorKey = colorKey is not null;

            if (!haveSMask && !haveStencil && !haveColorKey) return (bgra, w, h);

            // A /Matte entry means the colour samples are pre-blended against the matte
            // colour (premultiplied). The true colour is recovered per PDF §11.6.5.3:
            // c = m + (c' - m) / alpha. We only un-premultiply when the mask is (near-)
            // uniform — a flat translucent overlay (e.g. a tiled background texture)
            // where leaving the samples pre-blended visibly darkens the result. Shaped
            // masks (varying coverage, e.g. a vignetted photo) are left untouched: their
            // opaque interior needs no correction and dividing thin edges by tiny alpha
            // only blows highlights out to white.
            byte mB = 0, mG = 0, mR = 0;
            bool unmatte = false;
            if (haveSMask && reader.ResolveStream(dict.Get("SMask"))?.Dict.Get("Matte") is PdfArray matte && matte.Count > 0)
            {
                int amin = 255, amax = 0;
                foreach (var a in alpha!) { if (a < amin) amin = a; if (a > amax) amax = a; }
                if (amax - amin <= 8 && amax > 0)
                {
                    unmatte = true;
                    double M(int i) => i < matte.Count ? NumFrom(matte[i]) : 0;
                    if (matte.Count >= 3) { mR = (byte)Clamp255(M(0) * 255); mG = (byte)Clamp255(M(1) * 255); mB = (byte)Clamp255(M(2) * 255); }
                    else { var v = (byte)Clamp255(M(0) * 255); mR = mG = mB = v; }
                }
            }

            // Output grid: the stencil resolution when it is finer than the base image,
            // otherwise the base image itself.
            bool upscale = haveStencil && (long)stw * sth > (long)w * h;
            int outW = upscale ? stw : w;
            int outH = upscale ? sth : h;
            var outBgra = upscale ? new byte[outW * outH * 4] : bgra;

            for (int y = 0; y < outH; y++)
            {
                for (int x = 0; x < outW; x++)
                {
                    var o = (y * outW + x) * 4;
                    if (upscale)
                    {
                        var bo = ((y * h / outH) * w + (x * w / outW)) * 4;
                        outBgra[o + 0] = bgra[bo + 0];
                        outBgra[o + 1] = bgra[bo + 1];
                        outBgra[o + 2] = bgra[bo + 2];
                    }
                    int a = 255;
                    if (haveSMask)
                    {
                        var sy = sh == outH ? y : (int)((long)y * sh / outH);
                        var sx = sw == outW ? x : (int)((long)x * sw / outW);
                        var ai = sy * sw + sx;
                        a = ai < alpha!.Length ? alpha[ai] : 255;
                    }
                    if (haveStencil)
                    {
                        var ty = sth == outH ? y : (int)((long)y * sth / outH);
                        var tx = stw == outW ? x : (int)((long)x * stw / outW);
                        var ti = ty * stw + tx;
                        if (ti < stencil!.Length) a = a * stencil[ti] / 255;
                    }
                    if (haveColorKey && a > 0)
                    {
                        // outBgra holds B,G,R at o..o+2 (post-conversion device colour).
                        int pb = outBgra[o + 0], pg = outBgra[o + 1], pr = outBgra[o + 2];
                        if (pr >= colorKey![0] && pr <= colorKey[1]
                            && pg >= colorKey[2] && pg <= colorKey[3]
                            && pb >= colorKey[4] && pb <= colorKey[5])
                            a = 0;
                    }
                    if (unmatte && a > 0)
                    {
                        outBgra[o + 0] = Unmatte(outBgra[o + 0], mB, (byte)a);
                        outBgra[o + 1] = Unmatte(outBgra[o + 1], mG, (byte)a);
                        outBgra[o + 2] = Unmatte(outBgra[o + 2], mR, (byte)a);
                    }
                    outBgra[o + 3] = (byte)a;
                }
            }
            return (outBgra, outW, outH);
        }

        private static byte Unmatte(byte cPrime, byte matte, byte alpha)
        {
            double v = matte + (cPrime - matte) * 255.0 / alpha;
            return (byte)Clamp255(v);
        }

        private static Bitmap FromBgra(byte[] bgra, int w, int h)
        {
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var rect = new System.Drawing.Rectangle(0, 0, w, h);
            var bits = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < h; y++)
                    System.Runtime.InteropServices.Marshal.Copy(bgra, y * w * 4, bits.Scan0 + y * bits.Stride, w * 4);
            }
            finally { bmp.UnlockBits(bits); }
            return bmp;
        }
    }
}

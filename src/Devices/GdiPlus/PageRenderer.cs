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
/// On Windows this is the primary
/// rasterizer: anti-aliased vector fills/strokes, high-quality bicubic image
/// resampling, and native glyph outlines. Windows-only — callers must fall back
/// to <see cref="SoftwarePageRenderer"/> on other platforms (GDI+ drawing throws
/// <see cref="System.PlatformNotSupportedException"/> off Windows).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class GdiPlusPageRenderer : IPageRenderer
{
    // ── Exact-compositing mode (transparency-group pages) ───────────────────────
    // A page that declares a transparency group is composited in exact mode:
    // PixelOffsetMode.None (pixel-corner sample grid), straight-sRGB
    // stroke compositing, and opaque glyph writes. These settings are right for
    // transparency content but perturb ordinary anti-aliased edges, so they
    // are scoped to transparency pages only — plain page content keeps the
    // gamma-corrected HighQuality path. Env overrides (grp/Q experiments): Q_EXACT=1
    // forces the mode on for every page, Q_EXACT=0 forces it off.
    private bool _exactRender;

    // Q_POM=none forces the pixel-corner sample grid (PixelOffsetMode.None) on every
    // page WITHOUT the rest of exact mode — isolates the half-pixel placement shift
    // for A/B against output rasterised with GDI+ default alignment.
    private static readonly bool PomNone = Environment.GetEnvironmentVariable("Q_POM") == "none";

    private PixelOffsetMode PagePom => _exactRender || PomNone ? PixelOffsetMode.None : PixelOffsetMode.HighQuality;

    /// <summary>How many device pixels this render puts on each OUTPUT pixel. A caller
    /// that renders large and averages down (the bilevel TIFF path supersamples 2×) sets
    /// it, so rules stated about the output scale — which sample grid an image resamples
    /// on — are judged at the scale the viewer actually sees, not the intermediate one.</summary>
    internal int OutputSupersample { get; set; } = 1;

    // Strokes composite in straight sRGB on every page: stroke AA blends
    // linearly in byte space — a K-black stroke edge over
    // yellow lands at the linear lerp, ~40 levels darker than the gamma-corrected mix.
    private bool StrokeLinear => true;

    // Glyph fills composite in straight sRGB (like strokes) instead of the page's
    // gamma-corrected blend. Gamma-corrected coverage renders sub-pixel stems at
    // roughly HALF the expected ink (measured on a small-size CJK newspaper
    // page: 6.6% ink under the gamma blend vs 14.3% and 12.2% from two
    // independent rasterisers of the SAME imported document; per-inked-pixel
    // luma equal) — the classic pale-text anemia of linear-light blending;
    // mature rasterisers blend in byte space. Straight sRGB lands that page at
    // 13.0%. Q_TEXTLIN=0 restores the gamma blend for A/B. (An earlier
    // measurement had linear text losing on scanned-book exact-mode pages, but
    // exact-mode pages now take the PaintGlyphOpaque route above this switch.)
    private static readonly bool TextLinear = Environment.GetEnvironmentVariable("Q_TEXTLIN") != "0";

    // Device em size (px) below which the straight-sRGB text blend applies; at and
    // above it glyphs keep the gamma-corrected blend. Between the measured
    // witnesses (16 px-em heavy / 54 px-em light — see the fill site).
    private const double TextLinearMaxEmPx = 40.0;

    // Q_TEXTBOLD=<w>: after filling a glyph outline, stroke it with a pen of width
    // <w> (device px) in the same colour — stem darkening for low-DPI text, matching
    // rasterisers that apply stroke-adjust. 0/unset = off.
    private static readonly double TextBold =
        double.TryParse(Environment.GetEnvironmentVariable("Q_TEXTBOLD"),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tb) ? tb : 0.0;

    // Q_TEXTOP=1 routes glyphs through the GDI-run compositor (PaintGlyphOpaque:
    // 33-level AntiAlias coverage + straight-sRGB blend) WITHOUT the rest of exact
    // mode — isolates the text compositing rule on its own for A/B.
    private bool TextOpaque => _exactRender || Environment.GetEnvironmentVariable("Q_TEXTOP") == "1";

    private static readonly string? ExactOverride = Environment.GetEnvironmentVariable("Q_EXACT");

    // Supersample factor for the outer-blend coverage mask of a non-isolated
    // backdrop-copy group (Q_SSCOV experiment knob; 0/1 = off). When on, the group's
    // coverage is captured by rendering its content at K× resolution and
    // box-downsampling the alpha, giving geometric coverage finer than 8 bits with
    // tails GDI+'s 1× hairline rasterization misses.
    private static readonly int SsCoverage = int.TryParse(Environment.GetEnvironmentVariable("Q_SSCOV"), out var ssk) ? ssk : 0;

    // Outer-blend mask mode for backdrop-copy groups. Default "bin2": nested-group
    // footprints stamp binary center-cell coverage and composite with replace-semantics;
    // direct content keeps fractional
    // anti-aliased coverage with backdrop removal. Q_OBM overrides for experiments
    // ("off" = pre-stamp behaviour; "bin"/"nal"/"alias"/"raw" = earlier variants).
    private static readonly string? ObMode = Environment.GetEnvironmentVariable("Q_OBM") ?? "bin2";

    // Minimum scratch alpha for a nested-group pixel to count as covered when
    // binarizing (Q_BTH, default 1 = any non-zero).
    private static readonly int BinThreshold = int.TryParse(Environment.GetEnvironmentVariable("Q_BTH"), out var bth) ? Math.Max(1, bth) : 1;

    // Knockout sibling handling override (Q_KO: "replace"/"over"); default is per-group —
    // a NON-isolated knockout group replaces sibling pixels (strict spec knockout), an
    // isolated one composites over them, keeping sibling AA at fractional-coverage edges
    // (verified on the isolated-knockout corpus files).
    private static readonly string? KnockoutOverride = Environment.GetEnvironmentVariable("Q_KO");

    private bool _inCoveragePass;          // true inside a coverage pre-pass (suppresses nested supersampling)

    // Page-indexed record of which pre-pass pixels were stamped by a NESTED-group
    // footprint (vs fractional direct-content coverage). Stamped pixels take
    // replace-semantics in the outer-blend composite: the blend applies to the layer
    // value AS-IS at full weight, keeping the layer's own alpha — no backdrop removal.
    private byte[]? _stampMask;

    // ── Per-render state (single-threaded; one render at a time per instance) ──
    private Graphics _g = null!;

    private Bitmap _bitmap = null!;        // backing bitmap for _g (for manual blend compositing)

    private bool _knockoutGroup;           // true while rendering a /K knockout group's own content

    private Bitmap? _koBackdrop;           // frozen initial backdrop of the innermost knockout group (elements blend against THIS)

    private bool _koReplace;               // innermost knockout group's sibling semantics (true = replace, non-isolated)

    private Bitmap? _blendScratch;         // reusable layer for non-Normal /BM fills

    // Pool of page-sized ARGB layer bitmaps reused across transparency-group
    // composites. A page with hundreds of groups (some documents have ~890) otherwise
    // allocates+zeroes a full-page bitmap per group — the dominant cost on a large
    // page. The pool keeps only ~nesting-depth bitmaps alive and reuses them; each
    // group re-initialises just its BBox sub-rect, so stale content elsewhere is
    // never read (the composite is BBox-bound).
    private readonly Stack<Bitmap> _layerPool = new();

    private PdfReader _reader = null!;

    // Document-level PDF/X marker (see RenderPageAtPixelSize) — gates the
    // spot-plate overprint simulation in the image decode path.
    private bool _pdfxOverprintSim;

    private readonly Dictionary<PdfDictionary, int[]?> _encGidMaps = new(ReferenceEqualityComparer.Instance);

    private double _scale;          // horizontal pixels per PDF point

    private double _scaleY;         // vertical pixels per PDF point (differs from _scale only when the caller pins a non-proportional target size)

    private int _pixelH;            // canvas height in pixels (for Y flip)

    private Rectangle _mediaBox = null!; // effective (rotation-adjusted) media box

    // Page /Rotate CTM, applied to annotation coordinates only. Page CONTENT gets this
    // baked into its initial CTM; annotations are authored in unrotated page space, so
    // during the annotation phase WorldMatrix composes it in (null the rest of the time).
    private double[]? _annotBaseCtm;

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

    internal RgbaBuffer RenderPage(Page page, int xDpi, int yDpi) => RenderPage(page, xDpi, yDpi, 1);

    /// <summary>Render the page's natural pixel grid for the given resolution.
    /// <paramref name="superFactor"/> tells the renderer it is drawing an INTERMEDIATE
    /// that the caller will average down by that factor.</summary>
    internal RgbaBuffer RenderPage(Page page, int xDpi, int yDpi, int superFactor)
    {
        // Size to the crop box (clipped to the media box), not the media box: the
        // rasterizer presents only the cropped region. Rotation swaps the
        // visible dimensions exactly as it does for the media box.
        var crop = SoftwarePageRenderer.EffectiveCropRect(page);
        var rot = ((page.RotateDegrees % 360) + 360) % 360;
        var visW = (rot == 90 || rot == 270) ? crop.Height : crop.Width;
        var visH = (rot == 90 || rot == 270) ? crop.Width : crop.Height;
        var pixelW = SoftwarePageRenderer.PagePixels(visW, xDpi);
        var pixelH = SoftwarePageRenderer.PagePixels(visH, yDpi);
        if (AliasedVectorFills)
        {
            _aliasedSx = xDpi / 72.0;
            _aliasedSy = yDpi / 72.0;
            // Y anchors on the INTEGER canvas height — the floored canvas crops the
            // page's partial pixel at the TOP, so every y edge carries a constant
            // −frac(H·s) bias relative to the unfloored top anchor. (Bar rows whose
            // unfloored edge fraction equals exactly that bias land ON the integer
            // lattice and stay inclusive; that inclusivity pins this anchoring.)
            _aliasedFlipH = pixelH;
        }
        else
            _aliasedFlipH = 0;
        // Hand the REQUESTED resolution down rather than letting the canvas imply it:
        // PagePixels truncates, so re-deriving the scale from the rounded canvas shrinks
        // the page by up to a pixel across its width. See the scale assignment below.
        //
        // ⚠ Only for a DIRECT render. A SUPERSAMPLED intermediate (superFactor > 1, the
        // bilevel TIFF path) keeps the canvas-derived fit: that path area-averages the
        // intermediate down and packs the result against the OUTPUT canvas, and its
        // aliased-fill Y anchoring is already calibrated on the floored canvas cropping
        // the page's partial pixel — handing it the true scale as well double-corrects,
        // and a bilevel threshold turns that into flipped pixels along every edge
        // (measured: it breaks two CCITT4 compares that the fit keeps exact).
        return superFactor > 1
            ? RenderPageAtPixelSize(page, pixelW, pixelH)
            : RenderPageAtPixelSize(page, pixelW, pixelH, xDpi / 72.0, yDpi / 72.0);
    }

    internal RgbaBuffer RenderPageAtPixelSize(Page page, int pixelW, int pixelH,
        double? xScale = null, double? yScale = null)
    {
        if (pixelW <= 0) pixelW = 1;
        if (pixelH <= 0) pixelH = 1;

        // Live-document render: unsaved Contents edits must be visible to the
        // stream read below, as a live document is expected to render them.
        page.FlushPendingContents();

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
            // The rotation swings the CROP rectangle (the visible region), not the
            // media box — its dimensions AND lower-left offset anchor the swing
            // (mirrors SoftwarePageRenderer; a media-box anchor shifted a
            // 270°-rotated cropped page by the media/crop height difference).
            var w = crop.Width;
            var h = crop.Height;
            effectiveMb = rot == 180
                ? new Rectangle(0, 0, w, h)
                : new Rectangle(0, 0, h, w);
            initialPageCtm = rot switch
            {
                90 => new[] { 0.0, -1.0, 1.0, 0.0, -crop.LLY, w + crop.LLX },
                180 => new[] { -1.0, 0.0, 0.0, -1.0, w + crop.LLX, h + crop.LLY },
                270 => new[] { 0.0, 1.0, -1.0, 0.0, h + crop.LLY, -crop.LLX },
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
        // SaveAsTIFF(file, 1000, 2000, …)) stretches the page to fill it exactly — that
        // caller passes no scale and gets the canvas-derived fit.
        //
        // The DPI-driven path passes the resolution it was asked for, and must: the
        // canvas is TRUNCATED from the page's exact pixel extent (PagePixels floors), so
        // re-deriving the scale from it renders the page very slightly SMALL. A 595×793
        // page at 300 dpi is 2479.17 × 3304.17 px and the canvas is 2479 × 3304, which
        // fits at 4.16639 px/pt instead of the requested 4.16667 — a shrink that reaches
        // a sixth of a pixel at the far edge and drags every glyph left of and below
        // where it belongs. Measured against the expected 300 dpi
        // render of a scanned page: a systematic −0.09 px x and +0.07 px y stem phase,
        // which is this and nothing else. Honouring the requested scale lets the page's
        // partial last pixel fall outside the canvas, which is what truncating it means.
        _scale = xScale ?? pixelW / effectiveMb.Width;
        _scaleY = yScale ?? pixelH / effectiveMb.Height;
        _pixelH = pixelH;
        _formDepth = 0;
        _glyphCache = new(ReferenceEqualityComparer.Instance);
        _cidCache = new(ReferenceEqualityComparer.Instance);
        _metricsCache = new(ReferenceEqualityComparer.Instance);
        _gdiStateStack.Clear();
        _ocgHiddenStack.Clear();
        _ocgHidden = SoftwarePageRenderer.ResolveHiddenOcgs(_reader);
        // PDF/X documents get overprint SIMULATION for spot-plate images (a
        // 0-tint overprinting plate leaves the backdrop untouched); plain
        // documents composite the plate's alternate colour.
        _pdfxOverprintSim = SoftwarePageRenderer.HasPdfXOutputIntent(_reader);
        // An XFA-bearing document is sampled on the PIXEL-CORNER grid
        // (measured on the LiveCycle hybrid form: its 0.5 pt shared table edge
        // rasterises 99/36 at 150 dpi = two overlapped corner-grid strokes, and
        // the glyph-edge greys only reconcile under the same lattice — the Half
        // offset leaves ~2k pixels outside even a delta-80 match window). The
        // corpus' NON-XFA expected renders were regenerated from half-offset
        // renders over the years, so the corner grid stays scoped to the XFA
        // family — the same per-content switching the transparency-group
        // exact-render path already does.
        _exactRender = ExactOverride is null ? PageUsesTransparencyGroup(page.Dict) || DocumentHasXfa()
                                             : ExactOverride == "1";

        using var bitmap = new Bitmap(pixelW, pixelH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            // Start from transparent WHITE paper (RGB white, alpha 0). The alpha channel then
            // records COVERAGE — a pixel still at alpha 0 is bare paper; any painted pixel
            // (even a white fill) has alpha > 0. Transparency-group blend modes read that
            // coverage as the backdrop alpha, so a blend over bare paper paints the group's
            // own colour (PDF composites the page onto paper only at output) while a blend
            // over painted content blends correctly. RGB stays white so a colour read of paper
            // is white. Flattened onto opaque white at output (FlattenAlphaOntoWhite below).
            g.Clear(GdiColor.FromArgb(0, 255, 255, 255));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PagePom;
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
            _annotBaseCtm = initialPageCtm;
            SafeDraw(() => DrawAnnotations(page.Dict));
            _annotBaseCtm = null;
        }

        _reader.ClearCache();
        // Composite the coverage-carrying page (RGB with alpha = coverage) onto opaque white
        // paper: bare-paper pixels (alpha 0) become white, partially-covered pixels blend over
        // white, then every pixel is forced opaque. Straight (un-premultiplied) sRGB bytes,
        // matching the manual composites elsewhere.
        FlattenAlphaOntoWhite(bitmap, pixelW, pixelH);
        var result = ToRgbaBuffer(bitmap, pixelW, pixelH);
        _blendScratch?.Dispose();
        _blendScratch = null;
        while (_layerPool.Count > 0) _layerPool.Pop().Dispose();
        _bitmap = null!;
        return result;
    }

    /// <summary>Composite a coverage-carrying page bitmap (RGB with alpha = coverage) onto
    /// opaque white, in straight sRGB bytes to match the renderer's other manual composites,
    /// then set every pixel opaque. A pixel with alpha a and colour C becomes C·a + 255·(1−a).</summary>
    private static void FlattenAlphaOntoWhite(Bitmap bmp, int w, int h)
    {
        var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[data.Stride];
            for (int y = 0; y < h; y++)
            {
                long rowPtr = data.Scan0.ToInt64() + (long)y * data.Stride;
                System.Runtime.InteropServices.Marshal.Copy((IntPtr)rowPtr, row, 0, data.Stride);
                for (int x = 0; x < w; x++)
                {
                    int i = x * 4;
                    int a = row[i + 3];
                    if (a == 255) continue;
                    int inv = 255 - a;
                    row[i]     = (byte)((row[i]     * a + 255 * inv + 127) / 255);
                    row[i + 1] = (byte)((row[i + 1] * a + 255 * inv + 127) / 255);
                    row[i + 2] = (byte)((row[i + 2] * a + 255 * inv + 127) / 255);
                    row[i + 3] = 255;
                }
                System.Runtime.InteropServices.Marshal.Copy(row, 0, (IntPtr)rowPtr, data.Stride);
            }
        }
        finally { bmp.UnlockBits(data); }
    }

    // A page participates in transparency compositing when it declares a transparency
    // group (PDF 32000 §11.6.6): /Group with /S /Transparency on the page dict. The
    // exact-render settings (see _exactRender) are scoped to such pages so ordinary
    // opaque content keeps the gamma-corrected anti-aliased path unchanged.
    // Declaring the group is not enough on its own — many fully opaque producers stamp a
    // bare /Group /S /Transparency /CS /DeviceRGB on every page — so two more signals
    // are required. (1) The page's group must look AUTHORED rather than stamped: an
    // explicit isolation/knockout flag (/I or /K, any value) or a colour space given as
    // an actual object (ICC stream or array) instead of a bare device name. Tools that
    // mean transparency-group compositing write these; the boilerplate never does.
    // (2) The content must actually be able to composite: an ExtGState with a
    // non-Normal blend mode or constant alpha below 1, on the page or inside any
    // nested form XObject.
    /// <summary>True when the document carries an XFA form (Catalog → AcroForm →
    /// /XFA) — the LiveCycle family whose reference raster uses the exact
    /// pixel-corner sampling (see the _exactRender assignment).</summary>
    private bool DocumentHasXfa()
    {
        try
        {
            return _reader.ResolveDict(_reader.Catalog.Get("AcroForm")) is { } af
                && af.ContainsKey("XFA");
        }
        catch { return false; }
    }

    private bool PageUsesTransparencyGroup(PdfDictionary pageDict)
    {
        if (_reader.ResolveDict(pageDict.Get("Group")) is not { } grp
            || grp.GetName("S") != "Transparency")
            return false;
        bool authored = grp.ContainsKey("I") || grp.ContainsKey("K")
            || (grp.Get("CS") is { } cs && cs is not PdfName);
        return authored
            && ResourcesCanComposite(_reader.ResolveDict(pageDict.Get("Resources")), 0, new HashSet<PdfDictionary>());
    }

    private bool ResourcesCanComposite(PdfDictionary? resources, int depth, HashSet<PdfDictionary> visited)
    {
        if (resources is null || depth > 8 || !visited.Add(resources)) return false;
        if (SoftwarePageRenderer.ResolveExtGStates(resources, _reader) is { } gss)
            foreach (var eg in gss.Values)
            {
                if (eg.GetName("BM") is { } bm && bm != "Normal" && bm != "Compatible") return true;
                if (eg.Get("ca") is { } ca && NumFrom(ca) < 1) return true;
                if (eg.Get("CA") is { } sa && NumFrom(sa) < 1) return true;
            }
        foreach (var xo in SoftwarePageRenderer.ResolveAllXObjects(resources, _reader).Values)
            if (xo.Dict.GetName("Subtype") == "Form"
                && ResourcesCanComposite(_reader.ResolveDict(xo.Dict.Get("Resources")), depth + 1, visited))
                return true;
        return false;
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
    private void RenderContentStream(byte[] content, double[]? initialCtm, GraphicsPath? initialClip,
        GraphicsState? inheritState = null)
    {
        var parser = new ContentStreamParser(_reader);

        // A non-group Form XObject inherits the graphics state in effect at its Do
        // (PDF 32000 §8.10.1) — most visibly the constant alphas a page-level
        // "/GSn gs" sets just before invoking the form. Colours and blend mode ride
        // along for content that paints without selecting its own.
        if (inheritState is not null)
        {
            var st = parser.State;
            st.FillAlpha = inheritState.FillAlpha;
            st.StrokeAlpha = inheritState.StrokeAlpha;
            st.BlendMode = inheritState.BlendMode;
            st.FillR = inheritState.FillR; st.FillG = inheritState.FillG; st.FillB = inheritState.FillB;
            st.StrokeR = inheritState.StrokeR; st.StrokeG = inheritState.StrokeG; st.StrokeB = inheritState.StrokeB;
        }

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
            else if (op == "BT") { _textClip?.Dispose(); _textClip = null; _textClipPending = false; }
            else if (op == "ET")
            {
                if (_textClip is { PointCount: > 0 })
                {
                    var savedT = _g.Transform;
                    _g.ResetTransform();
                    try { _g.SetClip(_textClip, CombineMode.Intersect); }
                    finally { _g.Transform = savedT; savedT.Dispose(); }
                }
                else if (_textClipPending)
                {
                    // A clip-mode text object that produced no glyph outline — including
                    // one whose font could not be resolved at all — clips to nothing.
                    // Leaving the clip open instead lets the follow-up paint (typically a
                    // full-page image supplying the glyph pixels) cover the whole page.
                    _g.SetClip(System.Drawing.RectangleF.Empty);
                }
                _textClip?.Dispose(); _textClip = null; _textClipPending = false;
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
    // for the current text object, applied at ET. _textClipPending records that the
    // object DID show text in a clip mode, so an empty accumulation still clips to
    // nothing rather than leaving the clip open.
    private int _curTextMode;

    private GraphicsPath? _textClip;

    private bool _textClipPending;

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

    /// <summary>Rasterize vector path FILLS aliased (no AA): SmoothingMode.None with the
    /// pixel-corner sample grid, colors preserved exactly. Wired from
    /// <see cref="RenderingOptions.BarcodeOptimization"/> — barcode modules come out as
    /// hard-edged runs (a pixel is inked iff its corner lattice point falls inside the
    /// half-open device rect) instead of AA-feathered edges that blur module widths.
    /// Text, images, and strokes render exactly as without the flag.</summary>
    internal bool AliasedVectorFills { get; set; }

    /// <summary>Draw charstring-outline embedded fonts (CFF /FontFile3, Adobe Type 1
    /// /FontFile) through a TrueType sfnt converted from the program, rather than
    /// straight from the charstring interpreter. Wired from
    /// <see cref="RenderingOptions.ConvertFontsToUnicodeTTF"/>.</summary>
    internal bool ConvertFontsToUnicodeTtf { get; set; }

    // Aliased-fill device mapping: the flag's render keeps the EXACT dpi/72 scale with
    // the canvas floored per dimension (content's last partial pixel row/column cropped,
    // top-anchored) instead of squeezing content onto the floored canvas. Captured by
    // RenderPage(dpi) for use in the fill branch's world matrix.
    private double _aliasedSx, _aliasedSy, _aliasedFlipH;

    // Empirical calibration knobs for the aliased fill rule (device-space shift in px
    // appended to the fill transform, and the pixel-offset mode): Q_BCSHIFT="dx,dy",
    // Q_BCPOM=half|none. Defaults: POM.None (native corner-lattice ceil rule, 43/43
    // bar edges on a 220-dpi barcode page) with a −1/128 px bias — an edge that lands
    // EXACTLY on an integer device coordinate must include that lattice row/column,
    // but float32 matrix arithmetic can land it a few 1e-4 above the integer and
    // exclude it (a whole barcode shifted one row down); the bias is far above that
    // noise and far below the 1/18 px coordinate quantum, so fractional edges keep
    // their ceil.
    private static readonly (float dx, float dy) BcShift = ParseBcShift();

    private static readonly bool BcPomNone = Environment.GetEnvironmentVariable("Q_BCPOM") != "half";

    private static (float, float) ParseBcShift()
    {
        var v = Environment.GetEnvironmentVariable("Q_BCSHIFT");
        if (v is not null)
        {
            var p = v.Split(',');
            if (p.Length == 2
                && float.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dx)
                && float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dy))
                return (dx, dy);
        }
        return (-1f / 128f, -1f / 128f);
    }

    /// <summary>World matrix for aliased fills: identical to <see cref="WorldMatrix"/> but
    /// at the exact dpi/72 scale, top-anchored on the unfloored page height.</summary>
    private GdiMatrix AliasedWorldMatrix(double[] ctm)
    {
        if (_aliasedFlipH <= 0) return WorldMatrix(ctm);
        var m = new GdiMatrix((float)ctm[0], (float)ctm[1], (float)ctm[2], (float)ctm[3], (float)ctm[4], (float)ctm[5]);
        var page = new GdiMatrix((float)_aliasedSx, 0f, 0f, (float)-_aliasedSy,
            (float)(-_aliasedSx * _mediaBox.LLX), (float)(_aliasedFlipH + _aliasedSy * _mediaBox.LLY));
        m.Multiply(page, MatrixOrder.Append);
        return m;
    }

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
        // Annotations are authored in unrotated page space; compose the page /Rotate CTM
        // so they swing into the rotated canvas exactly as the page content does.
        if (_annotBaseCtm is not null)
            ctm = GraphicsState.MultiplyMatrices(ctm, _annotBaseCtm);
        var m = new GdiMatrix((float)ctm[0], (float)ctm[1], (float)ctm[2], (float)ctm[3], (float)ctm[4], (float)ctm[5]);
        // device(tx,ty) → pixel: px = scale*tx - scale*LLX; py = pixelH - scaleY*ty + scaleY*LLY
        var page = new GdiMatrix((float)_scale, 0f, 0f, (float)-_scaleY,
            (float)(-_scale * _mediaBox.LLX), (float)(_pixelH + _scaleY * _mediaBox.LLY));
        m.Multiply(page, MatrixOrder.Append);
        return m;
    }
}

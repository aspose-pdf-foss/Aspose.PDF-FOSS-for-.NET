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

public sealed partial class GdiPlusPageRenderer
{
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
            if (_vectorTarget)
            {
                // Printed as an image instead: see TryRenderPageToGraphics.
                _vectorNeedsImage = true;
                return;
            }
            DrawType3Text(rawBytes, state, fd3);
            return;
        }

        (var parser, var hScale) = ResolveParser(state.FontName);
        var metrics = ResolveMetrics(state.FontName);
        var cid = ResolveCid(state.FontName);
        var fill = ColorFrom(state.FillR, state.FillG, state.FillB, state.FillAlpha);

        // An active ExtGState soft mask modulates GLYPH fills per pixel too
        // (PDF 32000 §11.6.5.4) — without this, text drawn under a luminosity mask
        // (e.g. artwork the mask hides) painted at full coverage as a visible ghost.
        _curTextSoftMask = state.SoftMask is { } smText ? GetSoftMaskAlpha(smText) : null;
        _curTextState = state;

        // A Type0 font with a 1-byte custom CMap (codespace <00> <FF>) still shows
        // CIDs, not byte-encoded characters. The simple-font path resolves glyphs
        // through the program's cmap, which a CID-keyed program (bare CFF, or a
        // subset whose codes are CIDs — a one-byte identity CMap) does not have —
        // so a font whose CMap declares a FIXED single-byte codespace routes through
        // the CID path, which steps 1 byte per code and resolves CID→GID directly.
        // A mixed-width CMap (a UTF-8 one declares 1- to 4-byte ranges) cannot be
        // walked at a constant step and keeps the simple routing, as does a
        // CMap-less 1-byte Type0 with a TrueType descendant.
        if (cid is not null && rawBytes is not null
            && (cid.IsTwoByteEncoding || (cid.CMapCodeToCid is not null && cid.HasFixedSingleByteCMap)
                || parser is CffGlyphSource { IsCidKeyed: true }))
            DrawCidText(rawBytes, cid, parser, metrics, state, hScale, fill);
        else
            DrawSimpleText(text, rawBytes, parser, metrics, state, hScale, fill,
                EncGidMap(state.FontName, parser), UndefinedCodes(state.FontName));
    }

    private int[]? EncGidMap(string? fontName, IGlyphOutlineSource? parser)
    {
        // Key the cache by the FONT DICT, never the resource name: a form XObject's
        // /T1_0 is routinely a different font than the page's /T1_0, and a
        // name-keyed entry hands the form the page font's map (a header once drew
        // its semibold subset through the regular subset's GIDs).
        if (fontName is null || _scope.Fonts is null || !_scope.Fonts.TryGetValue(fontName, out var fd))
            return null;
        if (_encGidMaps.TryGetValue(fd, out var cached)) return cached;
        var map = SoftwarePageRenderer.BuildEncodingGidMap(_scope.Fonts, _reader, fontName, parser);
        _encGidMaps[fd] = map;
        return map;
    }

    /// <summary>The codes this run's font blanks - the shared rule in
    /// <see cref="SoftwarePageRenderer.BuildUndefinedCodes"/>, cached per font dict.</summary>
    private bool[]? UndefinedCodes(string? fontName)
    {
        if (fontName is null || _scope.Fonts is null || !_scope.Fonts.TryGetValue(fontName, out var fd))
            return null;
        if (_undefinedCodes.TryGetValue(fd, out var cached)) return cached;
        var map = SoftwarePageRenderer.BuildUndefinedCodes(fd, _reader);
        _undefinedCodes[fd] = map;
        return map;
    }

    /// <summary>For a StandardEncoding quote code, the character it shows; null for any other code.</summary>
    /// <remarks>A form whose Helvetica apostrophes were 0x27 printed straight quotes where the
    /// reference prints quoteright.</remarks>
    private static char? StandardQuote(byte code) => code switch
    {
        StandardQuoteRightCode => '\u2019',
        StandardQuoteLeftCode => '\u2018',
        _ => null,
    };

    private const byte StandardQuoteRightCode = 0x27;
    private const byte StandardQuoteLeftCode = 0x60;

    private void DrawSimpleText(string text, byte[]? rawBytes, IGlyphOutlineSource? parser,
        FontMetrics? metrics, GraphicsState state, double hScale, GdiColor fill, int[]? encGidMap,
        bool[]? undefined)
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
        var builtInStandard = useBytes && metrics is { ShowsStandardEncodingQuotes: true };
        var dingbats = useFallback && useBytes && parser == _dingbatsParser;
        using var brush = new SolidBrush(fill);

        for (int i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            int gid = 0;
            // The two quote codes StandardEncoding and WinAnsi disagree on in the ASCII range.
            var standardQuote = builtInStandard ? StandardQuote(rawBytes![i]) : null;
            if (standardQuote is { } quote) ch = quote;
            if (dingbats) ch = TextAbsorber.DingbatCharacter(rawBytes![i]);
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
                    if (useBytes && standardQuote is null && parser.CMap.TryGetValue(rawBytes![i], out gid) && gid > 0) { }
                    else if (parser.CMap.TryGetValue(ch, out gid) && gid > 0) { }
                    else gid = 0;
                }
            }
            var blank = undefined is not null && rawBytes is not null && i < rawBytes.Length
                && rawBytes[i] < undefined.Length && undefined[rawBytes[i]];
            if (parser is not null && gid > 0 && !blank)
                PaintGlyph(parser, gid, dingbats ? SeatDingbat(parser, gid, rawBytes![i], tm, tfs, th, upm) : tm,
                    ctm, tfs, th, state.Rise, hScale, upm, brush);

            int charWidth = 500;
            int widthCode = useBytes ? rawBytes![i] : ch;
            if (metrics is not null) charWidth = metrics.GetWidth(widthCode);
            double advance = charWidth;
            if (PrintsReferenceAdvances && metrics is not null) advance = metrics.GetWidthExact(widthCode);
            // With a host substitute and no PDF /Widths, take the advance from the
            // substitute program so the run doesn't collapse to uniform 500-unit steps.
            // Same when the font dict carries NO explicit width for this code (no
            // /Widths, not standard-14 — e.g. an unembedded /Verdana appearance font):
            // the metrics' constant default would spread every glyph to the same step.
            if ((charWidth == 0 || (ch > 0xFF && (charWidth == 500 || charWidth <= 0))
                 || (useFallback && metrics is null)
                 || (metrics is not null && !metrics.HasExplicitWidth(widthCode)))
                && parser is not null && gid > 0)
            {
                var adv = parser.GetAdvanceWidth(gid);
                if (adv > 0 && parser.UnitsPerEm > 0) advance = charWidth = adv * 1000 / parser.UnitsPerEm;
            }
            double tx = (advance / 1000.0 * tfs + state.CharSpacing + (ch == ' ' ? state.WordSpacing : 0)) * th;
            tm = GraphicsState.MultiplyMatrices(new double[] { 1, 0, 0, 1, tx, 0 }, tm);
        }
    }

    /// <summary>Whether a glyph advances by its font's width as written, fraction and all, rather
    /// than by that width rounded to a whole thousandth of an em.</summary>
    /// <remarks>
    /// So the reference prints: a run of Times New Roman whose /Widths are 722.168 drifted 0.014
    /// printer pixels right of ours per glyph at 10 points, until the stems of the fifth and later
    /// glyphs crossed onto the next pixel. Advanced by the written widths, the run's outlines land
    /// on the reference's pixels. Scoped to print output, whose reference is measured; the raster
    /// renders' templates were not re-measured against it.
    /// </remarks>
    private bool PrintsReferenceAdvances => _vectorTarget || PrintedPageImage;

    private void DrawCidText(byte[] rawBytes, CidFontInfo cid, IGlyphOutlineSource? parser, FontMetrics? metrics, GraphicsState state, double hScale, GdiColor fill)
    {
        var ct = new GdiCidTextDrawState();
        ct.rawBytes = rawBytes;
        ct.cid = cid;
        ct.parser = parser;
        ct.metrics = metrics;
        ct.state = state;
        ct.hScale = hScale;
        ct.fill = fill;
        ct.tm = (double[])ct.state.TextMatrix.Clone();
        ct.ctm = ct.state.Ctm;
        ct.tfs = ct.state.FontSize;
        ct.th = ct.state.HorizontalScaling / 100.0;
        ct.upm = ct.parser is not null && ct.parser.UnitsPerEm > 0 ? ct.parser.UnitsPerEm : 1000;
        using var brush = new SolidBrush(ct.fill);

        // Predefined legacy national CMaps (GBK-EUC-H, ETen-B5-H, …) encode their
        // show-strings in a national multi-byte charset (mixed 1-/2-byte), not as
        // Adobe CIDs. Decode and render them separately from the 2-byte CID path.
        if (ct.cid.LegacyCodepage != 0)
        {
            DrawLegacyCjkText(ct.rawBytes, ct.cid, ct.parser, ct.metrics, ct.tm, ct.ctm, ct.tfs, ct.th, ct.state.Rise, ct.hScale,
                ct.state.CharSpacing, ct.state.WordSpacing, brush);
            return;
        }

        ct.fallback = null;
        if (ct.parser is null)
        {
            var canFallback = ct.cid.IsUnicodeEncoding
                              || (ct.cid.Ordering is not null && ct.cid.Ordering != "Identity");
            // Resolve a system font by the CID ordering/base name (Korea1 -> Malgun,
            // GB1 -> SimSun, Japan1 -> MS Mincho), not the single generic broad-coverage
            // font: that one covers Han but not Hangul, so non-embedded Korean text
            // (UniKS-UTF16-H) was dropped while Chinese on the same page rendered.
            // ResolveNamed falls back to the generic font itself.
            if (canFallback) ct.fallback = Text.CjkFallbackFont.ResolveNamed(ct.cid.CjkBaseFont, ct.cid.Ordering);
        }
        ct.fbUpm = ct.fallback is not null && ct.fallback.UnitsPerEm > 0 ? ct.fallback.UnitsPerEm : 1000;

        ct.vertical = ct.cid.IsVertical;
        ct.step = ct.cid.IsTwoByteEncoding ? 2 : 1;
        for (int i = 0; i + ct.step <= ct.rawBytes.Length; i += ct.step)
        {
            DrawCidGlyph(ct, i, brush);
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
        var curveSource = PrintedPageImage ? parser as CffGlyphSource : null;
        var outline = curveSource is not null ? curveSource.GetCurveOutline(gid) : parser.GetOutline(gid);
        if (outline is null || outline.Contours.Length == 0) return;
        using var path = _vectorTarget ? BuildPrintedGlyphPath(outline)
            : curveSource is not null ? BuildCubicGlyphPath(outline)
            : BuildGlyphPath(outline);
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
            if (!StrokesOnly(_curTextMode)) _g.FillPath(brush, path);
            if (Strokes(_curTextMode) && _curTextState is { } strokeState)
                StrokeGlyph(path, GraphicsState.MultiplyMatrices(GraphicsState.MultiplyMatrices(s, param), tm), strokeState);
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

    /// <summary>The width a glyph outline is stroked with, in user space.</summary>
    /// <remarks>
    /// Sent to a printer, never thinner than one of the printer surface's own units, a hundredth of
    /// an inch: the reference stroked text given 0.5 and 0.25 point lines 0.96 XPS units wide, that
    /// hundredth exactly, where its lines of the same widths kept them. So widened, a bold heading
    /// drawn as filled and stroked text left 0 pixels outside its template's match window, not 3,033.
    /// </remarks>
    private double GlyphStrokeWidth(double lineWidth)
    {
        if (!_vectorTarget) return lineWidth;
        var e = _layoutToSheet!.Elements;
        var sheetUnitsPerLayoutPixel = Math.Sqrt(Math.Abs(e[0] * e[3] - e[1] * e[2]));
        return Math.Max(lineWidth, SheetUnit / sheetUnitsPerLayoutPixel / _scale);
    }

    /// <summary>One of a printer surface's own units, in those units.</summary>
    private const double SheetUnit = 1.0;

    /// <summary>Text rendering modes that stroke the glyph outline (PDF 32000 §9.3.6): stroke,
    /// fill then stroke, and their clipping twins.</summary>
    private static bool Strokes(int mode) => mode is 1 or 2 or 5 or 6;

    /// <summary>Text rendering modes that stroke the outline without filling it.</summary>
    private static bool StrokesOnly(int mode) => mode is 1 or 5;

    /// <summary>
    /// Stroke a glyph outline with the graphics state's pen. The outline is in font units and
    /// the line width in user space, so the width is divided by the scale text space takes on its
    /// way to user space - the text matrix, the font size and the 1/units-per-em - and the pen
    /// then scales with the glyph like any user-space stroke.
    /// </summary>
    /// <remarks>
    /// A producer's faux bold is often exactly this: a regular face drawn in mode 2 with a half
    /// point stroke. Filled only, a printed "TradeGothicCondEighteen,Bold" heading came out with
    /// 6 pixel stems where the reference's are 9.
    /// </remarks>
    private void StrokeGlyph(GraphicsPath path, double[] glyphToUser, GraphicsState state)
    {
        var scale = Math.Sqrt(Math.Abs(glyphToUser[0] * glyphToUser[3] - glyphToUser[1] * glyphToUser[2]));
        if (scale < 1e-12) return;
        using var pen = new Pen(ColorFrom(state.StrokeR, state.StrokeG, state.StrokeB, state.StrokeAlpha),
            (float)(GlyphStrokeWidth(state.LineWidth) / scale))
        {
            LineJoin = LineJoin.Miter,
        };
        _g.DrawPath(pen, path);
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

    /// <summary>
    /// Convert a glyph outline to the path a PRINTER receives. It differs from
    /// <see cref="BuildGlyphPath"/> in two places, both the reference's, and both invisible in a
    /// fill of the outline but not in the device-rounded outline a printer driver stores:
    /// </summary>
    /// <remarks>
    /// <para>An on-curve point, two off-curve points and an on-curve point make ONE cubic whose
    /// control points lie 4/3 of the way from each end to its neighbouring off-curve point -
    /// not the two quadratics split at the implied midpoint. Every other run converts as usual.</para>
    /// <para>A contour's closing edge back to its first point is not drawn: closing the figure
    /// draws it.</para>
    /// <para>Measured on 300 pt Calibri glyphs through the XPS Document Writer: all six outlines
    /// byte-identical to the reference's print, where the usual conversion matched three. On the
    /// corpus: a fitted A4 print 2,242 -> 247 pixels outside its template's match window (the
    /// reference's own print: 229), a half-scale Type1 page 1,615 -> 167.</para>
    /// </remarks>
    private static GraphicsPath BuildPrintedGlyphPath(GlyphOutline outline)
    {
        var path = new GraphicsPath(FillMode.Winding);
        var offCurve = new List<ContourPoint>(PairedOffCurvePoints);
        foreach (var contour in outline.Contours)
        {
            var start = Array.FindIndex(contour, c => c.OnCurve);
            if (contour.Length < 2 || start < 0)
            {
                // No on-curve anchor to start from: the usual conversion handles it.
                using var usual = BuildGlyphPath(new GlyphOutline(new[] { contour }, 0, 0, 0, 0));
                if (usual.PointCount > 0) path.AddPath(usual, false);
                continue;
            }

            path.StartFigure();
            var from = contour[start];
            for (var k = 1; k <= contour.Length; k++)
            {
                var point = contour[(start + k) % contour.Length];
                if (!point.OnCurve)
                {
                    offCurve.Add(point);
                    continue;
                }
                AddPrintedSegment(path, from, offCurve, point, closing: k == contour.Length);
                offCurve.Clear();
                from = point;
            }
            path.CloseFigure();
        }
        return path;
    }

    /// <summary>How many off-curve points in a row the reference turns into one cubic.</summary>
    private const int PairedOffCurvePoints = 2;

    /// <summary>Where a cubic's control point sits along its end's tangent to the off-curve
    /// point, for a pair of off-curve points drawn as one cubic.</summary>
    private const double PairedControlReach = 4.0 / 3.0;

    /// <summary>Where a cubic's control point sits for an exact quadratic (degree elevation).</summary>
    private const double QuadraticControlReach = 2.0 / 3.0;

    /// <summary>One on-curve-to-on-curve stretch of a printed glyph contour.</summary>
    private static void AddPrintedSegment(GraphicsPath path, ContourPoint from, List<ContourPoint> offCurve,
        ContourPoint to, bool closing)
    {
        if (offCurve.Count == 0)
        {
            if (!closing) path.AddLine((float)from.X, (float)from.Y, (float)to.X, (float)to.Y);
            return;
        }
        if (offCurve.Count == PairedOffCurvePoints)
        {
            AddCubic(path, from, offCurve[0], offCurve[1], to, PairedControlReach);
            return;
        }
        for (var q = 0; q < offCurve.Count; q++)
        {
            var control = offCurve[q];
            var end = q + 1 < offCurve.Count
                ? new ContourPoint((control.X + offCurve[q + 1].X) * 0.5, (control.Y + offCurve[q + 1].Y) * 0.5, true)
                : to;
            AddCubic(path, from, control, control, end, QuadraticControlReach);
            from = end;
        }
    }

    /// <summary>A cubic from <paramref name="from"/> to <paramref name="to"/> whose control points
    /// lie <paramref name="reach"/> of the way towards <paramref name="nearFrom"/> and
    /// <paramref name="nearTo"/>.</summary>
    private static void AddCubic(GraphicsPath path, ContourPoint from, ContourPoint nearFrom, ContourPoint nearTo,
        ContourPoint to, double reach)
    {
        path.AddBezier(
            (float)from.X, (float)from.Y,
            (float)(from.X + reach * (nearFrom.X - from.X)), (float)(from.Y + reach * (nearFrom.Y - from.Y)),
            (float)(to.X + reach * (nearTo.X - to.X)), (float)(to.Y + reach * (nearTo.Y - to.Y)),
            (float)to.X, (float)to.Y);
    }

    /// <summary>Convert a glyph outline whose curves are cubic (each an on-curve point, two
    /// off-curve control points and an on-curve point, as a CFF program draws them) to a path.</summary>
    private static GraphicsPath BuildCubicGlyphPath(GlyphOutline outline)
    {
        var path = new GraphicsPath(FillMode.Winding);
        foreach (var contour in outline.Contours)
        {
            if (contour.Length < 2 || !contour[0].OnCurve) continue;
            path.StartFigure();
            var i = 0;
            while (i + 1 < contour.Length)
            {
                var from = contour[i];
                if (contour[i + 1].OnCurve)
                {
                    path.AddLine((float)from.X, (float)from.Y, (float)contour[i + 1].X, (float)contour[i + 1].Y);
                    i++;
                    continue;
                }
                if (i + 3 >= contour.Length) break;
                var c1 = contour[i + 1];
                var c2 = contour[i + 2];
                var to = contour[i + 3];
                path.AddBezier((float)from.X, (float)from.Y, (float)c1.X, (float)c1.Y,
                    (float)c2.X, (float)c2.Y, (float)to.X, (float)to.Y);
                i += 3;
            }
            path.CloseFigure();
        }
        return path;
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
                    // A glyph opening with `d1` describes a shape and no colour: it is painted in
                    // the colour the text is set in (PDF 32000 §9.6.5), so the char proc
                    // starts from the state that shows it.
                    try { RenderContentStream(cp, glyphCtm, null, state); }
                    finally { _scope = savedScope; _g.Restore(savedGdi); }
                }
            }

            double dx = advanceTextSpace * state.FontSize * hScale;
            tm = GraphicsState.MultiplyMatrices(new double[] { 1, 0, 0, 1, dx, 0 }, tm);
        }
    }

    private (IGlyphOutlineSource? result, double hScale) ResolveParser(string? fontName)
    {
        double hScale = default;
        hScale = 1.0;
        if (fontName is null || _scope.Fonts is null || !_scope.Fonts.TryGetValue(fontName, out var fd))
            return (null, hScale);
        if (_glyphCache.TryGetValue(fd, out var c)) { hScale = c.hScale; return (c.parser, hScale); }
        var scratch = new Dictionary<string, (IGlyphOutlineSource? parser, double hScale)>();
        var resolved = SoftwarePageRenderer.GetGlyphParser(_scope.Fonts, _reader, scratch, fontName,
            ConvertFontsToUnicodeTtf);
        _glyphCache[fd] = resolved;
        return resolved;
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
        if (name == DingbatsFontName)
        {
            _dingbatsParser ??= NewParser(FontRepository.DjVuDingbatsProgram);
            if (_dingbatsParser is not null) return _fallbackParsers[name] = _dingbatsParser;
        }
        var ttf = Text.SystemFontResolver.Resolve(name) ?? Text.SystemFontResolver.Resolve("Arial");
        if (ttf is { Length: > 0 })
        {
            try { parser = new Text.GlyphOutlineParser(ttf); } catch { parser = null; }
        }
        _fallbackParsers[name] = parser;
        return parser;
    }

    /// <summary>The standard face whose codes are dingbats rather than letters.</summary>
    /// <remarks>No host carries it. The reference draws an unembedded ZapfDingbats code as the
    /// dingbat its encoding names, in a DejaVu face: a form's checked box, code 0x38, shows the
    /// heavy ballot X, stroke for stroke that face's U+2718, where a Latin substitute shows "8".
    /// </remarks>
    private const string DingbatsFontName = "ZapfDingbats";

    /// <summary>The built-in dingbats face, once parsed.</summary>
    private Text.GlyphOutlineParser? _dingbatsParser;

    /// <summary>The text matrix that seats a dingbat's outline where the reference draws it: the
    /// ink's left edge at <see cref="DingbatInkLeft"/> of the em past the pen, whatever the
    /// substitute face's own left bearing.</summary>
    private static double[] SeatDingbat(IGlyphOutlineSource parser, int gid, byte code, double[] tm,
        double tfs, double th, int upm)
    {
        if (parser.GetOutline(gid) is not { } outline) return tm;
        var shift = (DingbatInkLeft(code) * upm - outline.XMin) / upm * tfs * th;
        return GraphicsState.MultiplyMatrices(new double[] { 1, 0, 0, 1, shift, 0 }, tm);
    }

    /// <summary>Where the reference puts a dingbat's ink, as a share of the em past the pen.</summary>
    /// <remarks>
    /// Measured on every code printed at 48 points, from the outlines the XPS writer received:
    /// each glyph is the substitute face's own outline, not scaled and not moved vertically, only
    /// slid sideways. For the codes listed here its ink starts at the pen; for the rest at 35
    /// thousandths of the em, the left bearing ITC Zapf Dingbats gives nearly all its glyphs. No
    /// property of the outlines, the Zapf metrics or the Unicode blocks separates the two sets.
    /// </remarks>
    private static double DingbatInkLeft(byte code) => code switch
    {
        >= 0x26 and <= 0x28 or 0x2C or >= 0x33 and <= 0x34 or >= 0x37 and <= 0x38
            or >= 0x41 and <= 0x50 or >= 0x54 and <= 0x59 or >= 0x5B and <= 0x63
            or >= 0x67 and <= 0x6D or >= 0x74 and <= 0x77 or >= 0xA1 and <= 0xA3
            or >= 0xA5 and <= 0xA7 or >= 0xA9 and <= 0xAA or >= 0xAC and <= 0xD3
            or 0xD7 or 0xE7 or 0xF7 or 0xF9 => 0,
        _ => DingbatBearing,
    };

    /// <summary>The left bearing, in ems, of nearly every ITC Zapf Dingbats glyph (35/1000).</summary>
    private const double DingbatBearing = 0.035;

    private static Text.GlyphOutlineParser? NewParser(byte[]? program)
    {
        if (program is not { Length: > 0 }) return null;
        try { return new Text.GlyphOutlineParser(program); } catch { return null; }
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
}

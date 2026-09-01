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
            DrawSimpleText(text, rawBytes, parser, metrics, state, hScale, fill, EncGidMap(state.FontName, parser));
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
        Text.IGlyphOutlineSource? fallback = null;
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
            // The /W table is keyed by Adobe CIDs. A Unicode CMap (Uni*-UTF16/UCS2)
            // hands back the codepoint, so map it to the collection's real CID for
            // the width lookup — the authored half-width Latin runs (/W 1..96 = 500
            // in a Korea1 invoice) are otherwise missed and set at /DW 1000.
            int widthKey = c;
            if (cid.IsUnicodeEncoding && cid.Ordering is not null && cid.Ordering != "Identity"
                && Text.AdobeCidTables.UnicodeToCid(cid.Ordering, c) is int realCid)
                widthKey = realCid;
            int charWidth = metrics?.GetWidth(widthKey) ?? 1000;

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
                    fbGid = Text.CjkFallbackFont.ResolveFallbackGid(cid.Ordering, c, fallback);
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
                // Tw applies only to the SINGLE-BYTE code 32 (PDF 32000 §9.3.3) —
                // a 2-byte <0020> space in a UTF16/UCS2 CMap never takes it.
                double tx = (charWidth / 1000.0 * tfs + state.CharSpacing + (step == 1 && code == 32 ? state.WordSpacing : 0)) * th;
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
        var p = SoftwarePageRenderer.GetGlyphParser(_scope.Fonts, _reader, scratch, fontName, out hScale,
            ConvertFontsToUnicodeTtf);
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
}

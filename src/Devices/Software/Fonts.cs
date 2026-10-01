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
    /// <summary>Draw charstring-outline embedded fonts (CFF /FontFile3, Adobe Type 1
    /// /FontFile) through a TrueType sfnt converted from the program, rather than
    /// straight from the charstring interpreter. Wired from
    /// <see cref="RenderingOptions.ConvertFontsToUnicodeTTF"/>.</summary>
    internal bool ConvertFontsToUnicodeTtf { get; set; }

    /// <summary>Leave bare paper transparent instead of painting the page white first
    /// (<see cref="PngDevice.TransparentBackground"/>).</summary>
    internal bool TransparentBackground { get; set; }

    private static (IGlyphOutlineSource? parser, double hScale) GetGlyphParser(RenderContext ctx, string? fontName)
        => GetGlyphParser(ctx.FontDicts, ctx.Reader, ctx.FontParsers, fontName, ctx.ConvertFontsToUnicodeTtf);

    /// <summary>True when the bytes begin with an sfnt signature (TrueType, OpenType, or
    /// a TrueType collection). Used to detect a TrueType/OpenType program embedded under
    /// the /FontFile key instead of the standard /FontFile2 or /FontFile3. The second
    /// element is true for an 'OTTO' (CFF-outline) container.</summary>
    private static (bool isSfnt, bool isOpenTypeCff) LooksLikeSfnt(byte[] d)
    {
        if (d.Length < 4) return (false, false);
        uint tag = (uint)(d[0] << 24 | d[1] << 16 | d[2] << 8 | d[3]);
        bool isOpenTypeCff = tag == 0x4F54544Fu;            // 'OTTO' — OpenType with CFF outlines
        bool isSfnt = tag == 0x00010000u                    // TrueType outlines
            || tag == 0x74727565u                           // 'true'
            || tag == 0x74746366u                           // 'ttcf' — TrueType Collection
            || isOpenTypeCff;
        return (isSfnt, isOpenTypeCff);
    }

    /// <summary>
    /// Resolve a glyph-outline source for a font resource, independent of any render
    /// context, so alternate renderers (e.g. <see cref="GdiPlusPageRenderer"/>) can
    /// reuse the embedded-font/system-font resolution. <paramref name="cache"/> is a
    /// caller-owned per-render cache keyed by font resource name.
    /// </summary>
    internal static (IGlyphOutlineSource? parser, double hScale) GetGlyphParser(
        Dictionary<string, PdfDictionary>? fontDicts, IO.PdfReader reader,
        Dictionary<string, (IGlyphOutlineSource? parser, double hScale)> cache,
        string? fontName, bool convertFontsToUnicodeTtf = false)
    {
        if (fontName is null) return (null, 1.0);
        // A Tf naming a font the resources do not define. That is a malformed file, and
        // a common one: generators ship pages whose /Resources carry an EMPTY /Font dict
        // while the content stream still says "/F0 8 Tf". Returning nothing here dropped
        // every glyph on such a page - the vector art rendered and all the text vanished.
        // Viewers substitute a default face instead, and so does the GDI+ renderer for a
        // font whose program it cannot use, so take the same host face here.
        if (fontDicts is null || !fontDicts.TryGetValue(fontName, out var fontDict))
            return SubstituteForUndefinedFont(cache, fontName);

        if (cache.TryGetValue(fontName, out var cached)) return cached;

        IGlyphOutlineSource? parser = null;
        var hScale = 1.0;
        try
        {
            // Look for embedded font data in FontDescriptor. For Type0 fonts the
            // descriptor lives on DescendantFonts[0], which is usually an indirect
            // reference — resolve it before casting.
            var descriptor = reader.ResolveDict(fontDict.Get("FontDescriptor"));
            if (descriptor is null)
            {
                var descendants = reader.Resolve(fontDict.Get("DescendantFonts")) as PdfArray;
                if (descendants is not null && descendants.Count > 0)
                {
                    var desc0 = reader.ResolveDict(descendants[0]);
                    descriptor = desc0 is not null ? reader.ResolveDict(desc0.Get("FontDescriptor")) : null;
                }
            }

            if (descriptor is not null)
            {
                parser = LoadEmbeddedFontProgram(reader, descriptor, parser, convertFontsToUnicodeTtf);
            }

            // Fallback: try to resolve host font by BaseFont name for non-subset
            // embeddings. For subset embeddings the encoding wouldn't match the
            // system font's CMap anyway, so don't even try.
            if (parser is null)
            {
                (parser, hScale) = ResolveHostFontFallback(fontDict, descriptor, reader, parser, hScale);
            }
        }
        catch
        {
            // Failed to parse font — will use fallback (or render nothing)
        }

        cache[fontName] = (parser, hScale);
        return (parser, hScale);
    }

    /// <summary>The host face that stands in for a font name the resources never
    /// defined. Arial, the same last resort the GDI+ renderer falls back to, so the two
    /// rasterisers substitute alike; cached under the missing name so the lookup and the
    /// parse happen once per document.</summary>
    private static (IGlyphOutlineSource? result, double horizontalScale) SubstituteForUndefinedFont(Dictionary<string, (IGlyphOutlineSource? parser, double hScale)> cache, string fontName)
    {
        double horizontalScale = default;
        if (cache.TryGetValue(fontName, out var cached))
        {
            horizontalScale = cached.hScale;
            return (cached.parser, horizontalScale);
        }
        IGlyphOutlineSource? parser = null;
        var hScale = 1.0;
        try
        {
            var ttf = SystemFontResolver.Resolve("Arial");
            if (ttf is not null) parser = new GlyphOutlineParser(ttf);
        }
        catch { parser = null; }
        cache[fontName] = (parser, hScale);
        horizontalScale = hScale;
        return (parser, horizontalScale);
    }

    /// <summary>Load a charstring-outline program (bare CFF or an OpenType/CFF sfnt).
    /// With <paramref name="convertToUnicodeTtf"/> the glyphs are served the way their
    /// TrueType conversion renders — outline vertices quantized to the whole font units
    /// a glyf record stores — everything else unchanged. The
    /// <see cref="RenderingOptions.ConvertFontsToUnicodeTTF"/> pipeline.</summary>
    private static IGlyphOutlineSource? LoadCharstringFont(byte[] data, bool convertToUnicodeTtf)
    {
        var cff = CffGlyphSource.TryLoad(data);
        if (cff is not null && convertToUnicodeTtf) cff.QuantizeToFontUnits = true;
        return cff;
    }

    private static FontMetrics? GetFontMetrics(RenderContext ctx, string? fontName)
        => GetFontMetrics(ctx.FontDicts, ctx.Reader, fontName);

    internal static FontMetrics? GetFontMetrics(
        Dictionary<string, PdfDictionary>? fontDicts, IO.PdfReader reader, string? fontName)
    {
        if (fontName is null || fontDicts is null) return null;
        if (!fontDicts.TryGetValue(fontName, out var fontDict)) return null;
        try
        {
            return FontMetrics.FromFontDict(fontDict, reader);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Rasterise one glyph and put it on the page. Upright runs keep the cheap
    /// axis-aligned path and land at the pen exactly as before; a run whose text matrix
    /// rotates or skews goes through the 2x2 map and walks its own baseline, so
    /// <paramref name="penX"/> is read as a distance along that baseline rather than as a
    /// raster x. Every glyph the renderer draws comes through here.</summary>
    private static void BlitGlyph(RenderContext ctx, GlyphOutline outline, int unitsPerEm,
        double effectiveSize, double hScale, double penX, double penY,
        byte r, byte g, byte b, byte a)
    {
        if (unitsPerEm <= 0) return;
        if (ctx.GlyphEmMatrix is { } em)
        {
            if (GlyphRasterizer.RasterizeTransformed(outline,
                    em[0] / unitsPerEm, em[1] / unitsPerEm, em[2] / unitsPerEm, em[3] / unitsPerEm)
                is not (var mask, var rw, var rh, var rbx, var rby)) return;
            var dist = penX - ctx.GlyphOriginX;
            var ox = ctx.GlyphOriginX + dist * ctx.BaselineUx;
            var oy = ctx.GlyphOriginY + dist * ctx.BaselineUy;
            BlitAlphaMask(ctx, mask, rw, rh, (int)Math.Round(ox) + rbx, (int)Math.Round(oy) + rby,
                r, g, b, a);
            return;
        }

        if (GlyphRasterizer.Rasterize(outline, unitsPerEm, effectiveSize, ctx.Scale, hScale)
            is (var alphaMask, var gw, var gh, var bx, var by))
            BlitAlphaMask(ctx, alphaMask, gw, gh,
                (int)penX + (int)(bx * hScale), (int)penY + by, r, g, b, a);
    }

    /// <summary>Blit a single-channel alpha mask onto the RGBA pixel buffer with the given color.
    /// Every glyph the renderer draws goes through here, which is also where a text CLIP
    /// (Tr 4-7) collects its shape: while <see cref="RenderContext.TextClipAccum"/> is open the
    /// glyph coverage is unioned into it, and Tr 7 collects WITHOUT painting.</summary>
    private static void BlitAlphaMask(RenderContext ctx, byte[] alpha, int maskW, int maskH,
        int dstX, int dstY, byte r, byte g, byte b, byte a)
    {
        var clipAccum = ctx.TextClipAccum;
        var paints = clipAccum is null || ctx.TextClipPaints;
        for (var my = 0; my < maskH; my++)
        {
            var dy = dstY + my;
            if (dy < 0 || dy >= ctx.PixelH) continue;
            for (var mx = 0; mx < maskW; mx++)
            {
                var dx = dstX + mx;
                if (dx < 0 || dx >= ctx.PixelW) continue;

                var maskVal = alpha[my * maskW + mx];
                if (maskVal == 0) continue;

                if (clipAccum is not null)
                {
                    // Union, not sum: overlapping glyphs (a script face, an accent) must not
                    // saturate one another into a heavier shape than either alone.
                    var idx = dy * ctx.PixelW + dx;
                    if (maskVal > clipAccum[idx]) clipAccum[idx] = maskVal;
                    if (!paints) continue;
                }

                var effectiveA = (byte)((maskVal * a) / 255);
                SetPixel(ctx, dx, dy, r, g, b, effectiveA);
            }
        }
    }
}

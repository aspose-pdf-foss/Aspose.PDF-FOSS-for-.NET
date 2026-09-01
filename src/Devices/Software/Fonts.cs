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

    private static IGlyphOutlineSource? GetGlyphParser(RenderContext ctx, string? fontName,
        out double horizontalScale)
        => GetGlyphParser(ctx.FontDicts, ctx.Reader, ctx.FontParsers, fontName, out horizontalScale,
            ctx.ConvertFontsToUnicodeTtf);

    /// <summary>True when the bytes begin with an sfnt signature (TrueType, OpenType, or
    /// a TrueType collection). Used to detect a TrueType/OpenType program embedded under
    /// the /FontFile key instead of the standard /FontFile2 or /FontFile3. Sets
    /// <paramref name="isOpenTypeCff"/> for an 'OTTO' (CFF-outline) container.</summary>
    private static bool LooksLikeSfnt(byte[] d, out bool isOpenTypeCff)
    {
        isOpenTypeCff = false;
        if (d.Length < 4) return false;
        uint tag = (uint)(d[0] << 24 | d[1] << 16 | d[2] << 8 | d[3]);
        isOpenTypeCff = tag == 0x4F54544Fu;                 // 'OTTO' — OpenType with CFF outlines
        return tag == 0x00010000u                           // TrueType outlines
            || tag == 0x74727565u                           // 'true'
            || tag == 0x74746366u                           // 'ttcf' — TrueType Collection
            || isOpenTypeCff;
    }

    /// <summary>
    /// Resolve a glyph-outline source for a font resource, independent of any render
    /// context, so alternate renderers (e.g. <see cref="GdiPlusPageRenderer"/>) can
    /// reuse the embedded-font/system-font resolution. <paramref name="cache"/> is a
    /// caller-owned per-render cache keyed by font resource name.
    /// </summary>
    internal static IGlyphOutlineSource? GetGlyphParser(
        Dictionary<string, PdfDictionary>? fontDicts, IO.PdfReader reader,
        Dictionary<string, (IGlyphOutlineSource? parser, double hScale)> cache,
        string? fontName, out double horizontalScale,
        bool convertFontsToUnicodeTtf = false)
    {
        horizontalScale = 1.0;
        if (fontName is null) return null;
        // A Tf naming a font the resources do not define. That is a malformed file, and
        // a common one: generators ship pages whose /Resources carry an EMPTY /Font dict
        // while the content stream still says "/F0 8 Tf". Returning nothing here dropped
        // every glyph on such a page - the vector art rendered and all the text vanished.
        // Viewers substitute a default face instead, and so does the GDI+ renderer for a
        // font whose program it cannot use, so take the same host face here.
        if (fontDicts is null || !fontDicts.TryGetValue(fontName, out var fontDict))
            return SubstituteForUndefinedFont(cache, fontName, out horizontalScale);

        if (cache.TryGetValue(fontName, out var cached))
        {
            horizontalScale = cached.hScale;
            return cached.parser;
        }

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
                // TrueType embedding: /FontFile2 (TrueType) — the common CIDFontType2 path.
                var fontFile2 = reader.ResolveStream(descriptor.Get("FontFile2"));
                if (fontFile2 is not null)
                {
                    var ttfData = reader.DecodeStream(fontFile2);
                    // Some generators park a BARE CFF program under /FontFile2 (a
                    // CIDFontType2 whose "TrueType" bytes begin with the CFF header
                    // 01 00 hdrSize offSize). A real sfnt starts 00 01 00 00 / 'true' /
                    // 'ttcf', so the two are unambiguous — route the CFF to its parser
                    // instead of reading garbage table offsets and painting nothing.
                    // An 'OTTO' container under /FontFile2 has CFF outlines too (no
                    // glyf table) — same rerouting.
                    if (ttfData.Length > 4 && ttfData[0] == 0x01 && ttfData[1] == 0x00)
                        parser = LoadCharstringFont(ttfData, convertFontsToUnicodeTtf);
                    else if (LooksLikeSfnt(ttfData, out var ff2IsOtto) && ff2IsOtto)
                        parser = LoadCharstringFont(ttfData, convertFontsToUnicodeTtf);
                    else if (ttfData.Length > 0)
                        parser = new GlyphOutlineParser(ttfData);
                }

                // CFF embedding: /FontFile3 with /Subtype /Type1C, /CIDFontType0C, or
                // /OpenType. CffGlyphSource unwraps the OpenType SFNT container if
                // present before parsing the CFF structure.
                if (parser is null)
                {
                    var fontFile3 = reader.ResolveStream(descriptor.Get("FontFile3"));
                    if (fontFile3 is not null)
                    {
                        var cffData = reader.DecodeStream(fontFile3);
                        if (cffData.Length > 0)
                            parser = LoadCharstringFont(cffData, convertFontsToUnicodeTtf);
                    }
                }

                // PostScript Type 1 embedding: /FontFile (no number). The stream
                // dict carries /Length1 (ASCII header) and /Length2 (eexec
                // encrypted body) byte counts that Type1GlyphSource needs to
                // know where to split. Falls through to system-font lookup when
                // the stream is missing or unparseable.
                if (parser is null)
                {
                    var fontFile1 = reader.ResolveStream(descriptor.Get("FontFile"));
                    if (fontFile1 is not null)
                    {
                        var t1Data = reader.DecodeStream(fontFile1);
                        if (LooksLikeSfnt(t1Data, out var isOpenTypeCff))
                        {
                            // Some PDFs embed a CIDFontType2 (TrueType) program under the
                            // /FontFile key instead of the standard /FontFile2.
                            // Parse the sfnt as TrueType/OpenType, not as Type1.
                            parser = isOpenTypeCff
                                ? LoadCharstringFont(t1Data, convertFontsToUnicodeTtf)
                                : new GlyphOutlineParser(t1Data);
                        }
                        else if (t1Data.Length > 0)
                        {
                            var len1 = (int)fontFile1.Dict.GetInt("Length1");
                            var len2 = (int)fontFile1.Dict.GetInt("Length2");
                            var t1 = Type1GlyphSource.TryLoad(t1Data, len1, len2);
                            if (t1 is not null && convertFontsToUnicodeTtf)
                                t1.QuantizeToFontUnits = true;
                            parser = t1;
                        }
                    }
                }
            }

            // Fallback: try to resolve host font by BaseFont name for non-subset
            // embeddings. For subset embeddings the encoding wouldn't match the
            // system font's CMap anyway, so don't even try.
            if (parser is null)
            {
                // Prefer the font dict's /BaseFont. Some non-embedded TrueType fonts
                // ship with no BaseFont — the host-font name (Arial / Arial,Bold) lives
                // only on FontDescriptor./FontName, and that's where Adobe Reader picks
                // it up too. Fall back to FontDescriptor./FontName, resolving the
                // indirect ref explicitly rather than relying on GetName.
                var baseFont = fontDict.GetName("BaseFont");
                if (baseFont is null && descriptor is not null
                    && reader.Resolve(descriptor.Get("FontName")) is PdfName descFontName)
                {
                    baseFont = descFontName.Value;
                }
                if (baseFont is not null)
                {
                    var isClassicSubset = baseFont.Length > 7 && baseFont[6] == '+' &&
                        baseFont[..6].All(c => c >= 'A' && c <= 'Z');
                    // A Type0 font with an Adobe REGISTRY ordering (GB1, Japan1, ...) and no
                    // embedded program must NOT borrow a system face here. Its show-string
                    // carries Adobe CIDs, and the CID draw path would index them straight
                    // into that face's own glyph order - a different ordering entirely - so
                    // the page came out as real but WRONG Chinese. Leaving the parser null
                    // routes it to the CID fallback below, which maps CID to Unicode and
                    // then through the resolved face's cmap. An Identity ordering keeps the
                    // system face: its codes carry no registry meaning, so reading them as
                    // glyph ids is the only thing available.
                    if (!isClassicSubset && RegistryOrderingOf(fontDict, reader) is null)
                    {
                        var systemTtf = SystemFontResolver.Resolve(baseFont, out hScale);
                        if (systemTtf is not null)
                            parser = new GlyphOutlineParser(systemTtf);
                    }
                }

                // Last resort: the BaseFont name matched no installed family (an obfuscated
                // name like "HE108E", or a Multiple-Master Type1 with no embedded program).
                // Substitute a Standard-14 face chosen from the FontDescriptor's flags/style
                // so the text still renders — the simple font's /Encoding maps each code to a
                // glyph name, which the substitute's cmap resolves. CID (Type0) fonts have
                // their own CJK fallback, so this applies only to simple fonts.
                if (parser is null && descriptor is not null && fontDict.GetName("Subtype") != "Type0")
                {
                    var sub = SystemFontResolver.ResolveDescriptorSubstitute(fontDict, descriptor, reader, out hScale);
                    if (sub is not null)
                        parser = new GlyphOutlineParser(sub);
                }
            }
        }
        catch
        {
            // Failed to parse font — will use fallback (or render nothing)
        }

        cache[fontName] = (parser, hScale);
        horizontalScale = hScale;
        return parser;
    }

    /// <summary>The host face that stands in for a font name the resources never
    /// defined. Arial, the same last resort the GDI+ renderer falls back to, so the two
    /// rasterisers substitute alike; cached under the missing name so the lookup and the
    /// parse happen once per document.</summary>
    private static IGlyphOutlineSource? SubstituteForUndefinedFont(
        Dictionary<string, (IGlyphOutlineSource? parser, double hScale)> cache,
        string fontName, out double horizontalScale)
    {
        if (cache.TryGetValue(fontName, out var cached))
        {
            horizontalScale = cached.hScale;
            return cached.parser;
        }
        IGlyphOutlineSource? parser = null;
        var hScale = 1.0;
        try
        {
            var ttf = SystemFontResolver.Resolve("Arial", out hScale);
            if (ttf is not null) parser = new GlyphOutlineParser(ttf);
        }
        catch { parser = null; }
        cache[fontName] = (parser, hScale);
        horizontalScale = hScale;
        return parser;
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
            var mask = GlyphRasterizer.RasterizeTransformed(outline,
                em[0] / unitsPerEm, em[1] / unitsPerEm, em[2] / unitsPerEm, em[3] / unitsPerEm,
                out var rw, out var rh, out var rbx, out var rby);
            if (mask is null) return;
            var dist = penX - ctx.GlyphOriginX;
            var ox = ctx.GlyphOriginX + dist * ctx.BaselineUx;
            var oy = ctx.GlyphOriginY + dist * ctx.BaselineUy;
            BlitAlphaMask(ctx, mask, rw, rh, (int)Math.Round(ox) + rbx, (int)Math.Round(oy) + rby,
                r, g, b, a);
            return;
        }

        var alphaMask = GlyphRasterizer.Rasterize(outline, unitsPerEm,
            effectiveSize, ctx.Scale, out var gw, out var gh, out var bx, out var by, hScale);
        if (alphaMask is not null)
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

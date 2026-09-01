using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

internal sealed partial class FontMetrics
{
    private static FontMetrics BuildSimpleMetrics(PdfDictionary fontDict, PdfReader reader,
        string normalizedBase, bool isStandard14)
    {
        var firstChar = (int)fontDict.GetInt("FirstChar", 0);
        var lastChar = (int)fontDict.GetInt("LastChar", 255);

        // Type 3 fonts express glyph widths in glyph space; they become text-space
        // advances only after the horizontal component of /FontMatrix is applied
        // (PDF 32000 §9.6.5). The shared advance formula divides the stored width
        // by 1000, so pre-scale Type 3 widths by FontMatrix[0]·1000 to land in the
        // same 1/1000-text-space unit the formula expects. Other simple fonts already
        // store /Widths in 1/1000 units, so they keep a unit scale.
        var isType3 = fontDict.GetName("Subtype") == "Type3";
        double widthScale = 1.0;
        if (isType3)
        {
            double fontMatrix0 = 0.001;
            if (reader?.Resolve(fontDict.Get("FontMatrix")) is PdfArray fmArr && fmArr.Count >= 1)
            {
                fontMatrix0 = (reader.Resolve(fmArr[0]) ?? fmArr[0]) switch
                {
                    PdfInteger pi => pi.Value,
                    PdfReal pr => pr.Value,
                    _ => 0.001,
                };
            }
            widthScale = fontMatrix0 * 1000.0;
        }

        int[]? widths = null;
        // A /Widths entry written as a real is the face's own advance: a face drawn on
        // a 2048-unit em has advances that are not whole 1000ths, and dropping the
        // fraction shortens a re-measured paragraph by a fifth of a point. Keep those
        // entries alongside the integer table, which every all-integer array — very
        // nearly every real document — leaves exactly as it was.
        Dictionary<int, double>? exactWidths = null;
        var widthsObj = reader?.Resolve(fontDict.Get("Widths"));
        if (widthsObj is PdfArray widthsArr && widthsArr.Count > 0)
        {
            widths = new int[widthsArr.Count];
            for (var i = 0; i < widthsArr.Count; i++)
            {
                var w = reader?.Resolve(widthsArr[i]) ?? widthsArr[i];
                if (!isType3 && w is PdfReal exact && exact.Value != System.Math.Floor(exact.Value))
                    (exactWidths ??= new Dictionary<int, double>())[firstChar + i] = exact.Value;
                if (isType3)
                {
                    double raw = w switch
                    {
                        PdfInteger pi => pi.Value,
                        PdfReal pr => pr.Value,
                        _ => 0,
                    };
                    widths[i] = (int)System.Math.Round(raw * widthScale);
                }
                else
                {
                    // Non-Type3 simple fonts: /Widths are already in 1/1000 text space.
                    // An integer entry casts exactly as it always did; a real one rounds
                    // to its nearest whole width rather than losing its fraction outright,
                    // and the exact value above is what the extraction measure reads.
                    widths[i] = w switch
                    {
                        PdfInteger pi => (int)pi.Value,
                        PdfReal pr => (int)System.Math.Round(pr.Value),
                        _ => 0,
                    };
                }
            }
        }

        // If no /Widths array, try to extract from embedded font program
        if (widths is null && reader is not null)
        {
            widths = ExtractEmbeddedTrueTypeWidths(fontDict, reader, firstChar, lastChar);
        }

        // Try CFF (FontFile3) if TrueType extraction didn't yield results
        if (widths is null && reader is not null)
        {
            widths = ExtractEmbeddedCffWidths(fontDict, reader, firstChar, lastChar);
        }

        // A non-embedded, non-Standard-14 simple font without /Widths: every code
        // would otherwise measure at the 1000/1000 default — up to double the real
        // advance — and the word-gap heuristics downstream (space synthesis in
        // search/extraction) misfire on the phantom gaps. The BaseFont names a real
        // face; resolve it through the repository/system sources and take the
        // face's own advances, exactly as the producing layout measured them.
        if (widths is null && !isStandard14)
        {
            widths = SystemFaceWidths(normalizedBase, firstChar, lastChar);
        }

        var defaultWidth = reader is not null ? GetMissingWidth(fontDict, reader) : 0;
        if (defaultWidth == 0 && isStandard14)
            defaultWidth = Standard14Fonts.GetDefaultWidth(normalizedBase);
        if (defaultWidth == 0)
            defaultWidth = 1000; // absolute fallback

        var metrics = new FontMetrics(widths, firstChar, lastChar, null, defaultWidth,
            normalizedBase, isStandard14, isCid: false);
        metrics._cidWidthsExact = exactWidths;
        // MacRoman-encoded Standard-14 fonts remap codes before the (WinAnsi-shaped)
        // built-in width lookup — see MacRomanToWinAnsiCode.
        var encObj = reader?.Resolve(fontDict.Get("Encoding")) ?? fontDict.Get("Encoding");
        metrics._macRomanEncoding = encObj is PdfName encName
            ? encName.Value == "MacRomanEncoding"
            : encObj is PdfDictionary encDict && encDict.GetName("BaseEncoding") == "MacRomanEncoding";
        if (reader is not null)
            PopulateDescriptorMetrics(metrics, fontDict, reader);
        return metrics;
    }

    private static FontMetrics BuildCidMetrics(PdfDictionary fontDict, PdfReader reader,
        string normalizedBase, bool isStandard14)
    {
        // Type0 → DescendantFonts[0] → CIDFont dictionary
        var descendantsObj = reader.Resolve(fontDict.Get("DescendantFonts"));
        PdfDictionary? cidFontDict = null;
        if (descendantsObj is PdfArray descArr && descArr.Count > 0)
            cidFontDict = reader.ResolveDict(descArr[0]);

        var defaultWidth = 1000;
        Dictionary<int, int>? cidWidths = null;

        if (cidFontDict is not null)
        {
            defaultWidth = (int)cidFontDict.GetInt("DW", 1000);
            cidWidths = ParseCidWidths(cidFontDict, reader);

            // If no /W table, try to extract widths from embedded CFF font program (FontFile3)
            if (cidWidths is null || cidWidths.Count == 0)
            {
                cidWidths = ExtractEmbeddedCffCidWidths(cidFontDict, reader) ?? cidWidths;
            }

            // The descriptor's MissingWidth is a SIMPLE-font key; for a CID font an
            // explicit /DW wins (a font carries /DW 1000 next to /MissingWidth 500 and
            // its ideographs — outside the /W ranges — advance a full em). Only when
            // the CIDFont declares no /DW does MissingWidth fill the default in.
            if (cidFontDict.Get("DW") is null)
            {
                var mw = GetMissingWidth(cidFontDict, reader);
                if (mw > 0) defaultWidth = mw;
            }
        }

        var metrics = new FontMetrics(null, 0, 0, cidWidths, defaultWidth,
            normalizedBase, isStandard14, isCid: true);
        if (cidFontDict is not null)
        {
            metrics._type0Dict = fontDict;
            metrics._reader = reader;
            // Keep the embedded program for the Unicode-measure char->GID lookup.
            try
            {
                var mDesc = reader.ResolveDict(cidFontDict.Get("FontDescriptor"));
                if (mDesc is not null && reader.ResolveStream(mDesc.Get("FontFile2")) is { } mFf2)
                    metrics._embeddedProgram = reader.DecodeStream(mFf2);
            }
            catch { }
            PopulateDescriptorMetrics(metrics, cidFontDict, reader);
            if (cidWidths is not null && cidWidths.Count > 0)
                metrics._cidWidthsExact = BuildCidExactWidths(cidFontDict, reader, cidWidths);
        }
        return metrics;
    }

    /// <summary>
    /// Read unrounded per-CID glyph advances from the embedded TrueType (/FontFile2)
    /// hmtx table. The PDF /W array stores these advances rounded to integers; the
    /// font program keeps the full precision, which text extraction needs to measure
    /// fragment rectangles the way the original layout engine did.
    ///
    /// Only CIDs already present in <paramref name="cidWidths"/> are mapped, and a
    /// value is accepted only when it rounds to within 1 unit of the /W width — so a
    /// subset whose CID→GID order doesn't match (which would otherwise pull a wrong
    /// glyph's advance) silently keeps the integer width instead of corrupting it.
    /// </summary>
    private static Dictionary<int, double>? BuildCidExactWidths(
        PdfDictionary cidFontDict, PdfReader reader, Dictionary<int, int> cidWidths)
    {
        var descriptor = reader.ResolveDict(cidFontDict.Get("FontDescriptor"));
        var fontFileStream = descriptor is null ? null : reader.ResolveStream(descriptor.Get("FontFile2"));
        if (fontFileStream is null) return null;

        TrueTypeParser ttf;
        try
        {
            var fontData = reader.DecodeStream(fontFileStream);
            ttf = new TrueTypeParser(fontData);
            ttf.Parse();
        }
        catch { return null; }

        if (ttf.GlyphWidths.Length == 0 || ttf.UnitsPerEm == 0) return null;

        var cidToGid = ReadCidToGidMap(cidFontDict, reader);
        var scale = 1000.0 / ttf.UnitsPerEm;
        var exact = new Dictionary<int, double>(cidWidths.Count);

        foreach (var (cid, intWidth) in cidWidths)
        {
            // .notdef (CID 0) advance is left to the integer path: a 2-byte code's
            // zero high byte resolves to CID 0, and overriding it would perturb every
            // glyph's measured advance rather than restore a real glyph's precision.
            if (cid == 0) continue;
            var gid = cidToGid is null ? cid
                : (cid >= 0 && cid < cidToGid.Length ? cidToGid[cid] : 0);
            if (gid <= 0 || gid >= ttf.GlyphWidths.Length) continue;
            var w = ttf.GlyphWidths[gid] * scale;
            if (Math.Abs(w - intWidth) <= 1.0)
                exact[cid] = w;
        }

        return exact.Count > 0 ? exact : null;
    }

    /// <summary>
    /// Read the /CIDToGIDMap. Returns null for Identity (GID == CID) or when absent.
    /// </summary>
    private static int[]? ReadCidToGidMap(PdfDictionary cidFontDict, PdfReader reader)
    {
        var mapObj = cidFontDict.Get("CIDToGIDMap");
        if (mapObj is null) return null;
        if (mapObj is PdfName pn && pn.Value == "Identity") return null;

        var stream = reader.ResolveStream(mapObj);
        if (stream is null) return null;

        byte[] bytes;
        try { bytes = reader.DecodeStream(stream); }
        catch { return null; }

        var count = bytes.Length / 2;
        var map = new int[count];
        for (var i = 0; i < count; i++)
            map[i] = (bytes[i * 2] << 8) | bytes[i * 2 + 1];
        return map;
    }

    /// <summary>
    /// Parse the /W entry of a CIDFont dictionary.
    /// Format: [ cid [w1 w2 .] | cfirst clast w ]
    /// </summary>
    private static Dictionary<int, int>? ParseCidWidths(PdfDictionary cidFontDict, PdfReader reader)
    {
        var wObj = reader.Resolve(cidFontDict.Get("W"));
        if (wObj is not PdfArray wArr || wArr.Count == 0)
            return null;

        var widths = new Dictionary<int, int>();
        var i = 0;
        while (i < wArr.Count)
        {
            var first = GetInt(reader.Resolve(wArr[i]));
            i++;
            if (i >= wArr.Count) break;

            var next = reader.Resolve(wArr[i]);
            if (next is PdfArray subArr)
            {
                // Format: cid [w1 w2 w3 .]
                for (var j = 0; j < subArr.Count; j++)
                {
                    widths[first + j] = GetInt(reader.Resolve(subArr[j]));
                }
                i++;
            }
            else
            {
                // Format: cfirst clast w
                var last = GetInt(next);
                i++;
                if (i >= wArr.Count) break;
                var w = GetInt(reader.Resolve(wArr[i]));
                i++;
                for (var cid = first; cid <= last; cid++)
                    widths[cid] = w;
            }
        }

        return widths.Count > 0 ? widths : null;
    }

    /// <summary>
    /// Read ascent, descent, and cap height from the font descriptor.
    /// </summary>
    private static void PopulateDescriptorMetrics(FontMetrics metrics, PdfDictionary fontDict, PdfReader reader)
    {
        var descriptor = reader.ResolveDict(fontDict.Get("FontDescriptor"));
        if (descriptor is null) return;

        var rawAscent = (int)descriptor.GetInt("Ascent", 0);
        var rawDescent = (int)descriptor.GetInt("Descent", 0);
        var rawCapHeight = (int)descriptor.GetInt("CapHeight", 0);

        // PDF spec requires Descent to be negative (PDF 32000-1:2008 §9.8, Table 122).
        // Some font descriptors incorrectly use positive values — always negate them.
        if (rawDescent > 0)
            rawDescent = -rawDescent;

        // NOTE on Position.YIndent accuracy (heuristic, not spec-mandated):
        // The exact YIndent value depends on font metrics (descent, ascent) which come
        // from different sources depending on platform and font availability:
        //   - Windows: the public API may resolve system-installed fonts (e.g. TimesNewRoman)
        //     and read their OS/2 or hhea metrics directly, even when not embedded in the PDF.
        //   - macOS/Linux: those same system fonts may be absent, so the lib must rely solely
        //     on the PDF FontDescriptor values.
        // This means YIndent can differ by ~0.01–0.1 points across platforms for the same PDF.

        // For non-embedded fonts only: detect metrics in font design units instead of 1/1000
        // glyph space (common with non-embedded TrueType fonts whose PDF creator wrote raw
        // 2048-unit values). When Ascent > 1200 the metrics are clearly not in 1000-unit space.
        // Skip this for embedded fonts — the TrueType parser will provide the actual unitsPerEm.
        bool hasEmbeddedFont = reader.ResolveStream(descriptor.Get("FontFile2")) is not null
                            || reader.ResolveStream(descriptor.Get("FontFile")) is not null
                            || reader.ResolveStream(descriptor.Get("FontFile3")) is not null;
        if (rawAscent > 1200 && !hasEmbeddedFont)
        {
            // Estimate unitsPerEm as smallest power-of-2 ≥ rawAscent (covers 2048, 4096).
            int estimatedUpem = 1024;
            while (estimatedUpem < rawAscent) estimatedUpem *= 2;
            double normScale = 1000.0 / estimatedUpem;
            metrics._ascent = rawAscent * normScale;
            metrics._descent = rawDescent * normScale;
            metrics._capHeight = rawCapHeight * normScale;
        }
        else
        {
            metrics._ascent = rawAscent;
            metrics._descent = rawDescent;
            metrics._capHeight = rawCapHeight;
        }

        // Compute WinLineHeight for background rectangle sizing.
        // When the font descriptor has a CapHeight, use CapHeight + |Descent| —
        // this matches the .NET the public API's bg-rect height precisely.
        // Otherwise fall back to Ascent + |Descent|, then Standard-14 BBox.
        if (metrics._capHeight > 0 && metrics._descent != 0)
        {
            metrics._winLineHeight = (int)Math.Round(metrics._capHeight - metrics._descent);
        }
        else if (metrics._ascent != 0 || metrics._descent != 0)
        {
            metrics._winLineHeight = (int)Math.Round(metrics._ascent - metrics._descent);
        }
        else if (metrics._isStandard14 && metrics._baseFontName is not null)
        {
            int bboxH = Standard14Fonts.GetFontBBoxHeight(metrics._baseFontName);
            if (bboxH > 0) metrics._winLineHeight = bboxH;
        }

        // Use hhea ascent from embedded TrueType for the ascent component of the
        // text rectangle. The PDF descriptor Ascent often stores sTypoAscender (OS/2)
        // which is smaller than the actual glyph bounding ascent. The hhea ascent
        // (= usWinAscent in most TrueType fonts) matches what .NET uses for rectangles.
        // Keep the PDF descriptor descent for position computation.
        var fontFileStream = reader.ResolveStream(descriptor.Get("FontFile2"));
        if (fontFileStream is not null)
        {
            try
            {
                var fontData = reader.DecodeStream(fontFileStream);
                var ttf = new TrueTypeParser(fontData);
                ttf.Parse();
                if (ttf.UnitsPerEm > 0 && ttf.Ascent > 0)
                {
                    var scale = 1000.0 / ttf.UnitsPerEm;
                    // Use GDI+-style cell ascent for the text rectangle.
                    // .NET uses em-height = usWinAscent + usWinDescent as the em square,
                    // and cell ascent = usWinAscent scaled by (1000 / em-height-in-design-units).
                    // Use GDI+-style cell ascent when usWin metrics define a significantly
                    // larger em-square than upem (common in Calibri, Arial, etc. where
                    // usWinAsc+usWinDesc > upem). This matches .NET's text rectangle.
                    // For fonts where usWin metrics are close to upem, use hhea ascent
                    // directly (standard upem scaling).
                    var emDesignUnits = ttf.UsWinAscent + ttf.UsWinDescent;
                    if (ttf.UsWinAscent > 0 && emDesignUnits > ttf.UnitsPerEm * 1.2)
                    {
                        metrics._ascent = (int)Math.Round(ttf.UsWinAscent * 1000.0 / emDesignUnits);
                    }
                    else
                    {
                        var lineGapHalf = ttf.LineGap > 0 ? ttf.LineGap / 2 : 0;
                        metrics._ascent = (int)Math.Round((ttf.Ascent + lineGapHalf) * scale);
                    }
                    // Override descent from embedded TrueType only when the descriptor value
                    // is clearly in non-standard units (|Descent| > 1000 in 1000-unit space).
                    // E.g., Cambria: descriptor=-2463, sTypoDescender=-455 (2048 upem).
                    // For fonts where the descriptor Descent is already in ~1000-unit range
                    // (e.g., ArialMT Descent=-325), keep the descriptor value as-is.
                    if (ttf.STypoDescender != 0 && Math.Abs(metrics._descent) > 1000)
                        metrics._descent = ttf.STypoDescender * scale;

                    // OS/2 usWinAscent + usWinDescent gives the visual line height.
                    // Used for background rectangle sizing where the full cell height matters.
                    if (ttf.UsWinAscent > 0)
                        metrics._winLineHeight = (int)Math.Round((ttf.UsWinAscent + ttf.UsWinDescent) * scale);

                    // Underline metrics from the post table
                    if (ttf.UnderlinePosition != 0)
                        metrics._underlinePosition = (int)Math.Round(ttf.UnderlinePosition * scale);
                    if (ttf.UnderlineThickness != 0)
                        metrics._underlineThickness = (int)Math.Round(ttf.UnderlineThickness * scale);
                }
            }
            catch
            {
                // Fall back to descriptor values
            }
        }
    }

    /// <summary>Widths (1/1000 units, WinAnsi/CP1252 code space) for a non-embedded
    /// simple font whose BaseFont names an installed face — "Verdana",
    /// "VerdanaBold", "Arial,Bold", "TimesNewRoman". Null when no face resolves.</summary>
    private static int[]? SystemFaceWidths(string normalizedBase, int firstChar, int lastChar)
    {
        if (string.IsNullOrEmpty(normalizedBase)) return null;
        int[]? full;
        lock (_systemFaceWidthsCache)
        {
            if (!_systemFaceWidthsCache.TryGetValue(normalizedBase, out full))
            {
                full = BuildSystemFaceWidths(normalizedBase);
                _systemFaceWidthsCache[normalizedBase] = full;
            }
        }
        if (full is null) return null;
        var count = lastChar - firstChar + 1;
        if (count <= 0 || firstChar < 0 || lastChar > 255) return null;
        if (firstChar == 0 && lastChar == 255) return full;
        var widths = new int[count];
        System.Array.Copy(full, firstChar, widths, 0, count);
        return widths;
    }

    private static int[]? BuildSystemFaceWidths(string baseName)
    {
        byte[]? ttf = null;
        foreach (var candidate in SystemFaceNameCandidates(baseName))
        {
            try { ttf = FontRepository.GetTtfData(candidate); } catch { ttf = null; }
            if (ttf is not null) break;
        }
        if (ttf is null) return null;
        try
        {
            var tp = new TrueTypeParser(ttf);
            tp.Parse();
            if (tp.UnitsPerEm <= 0) return null;
            var widths = new int[256];
            var any = false;
            for (var code = 0; code < 256; code++)
            {
                var uni = Cp1252ToUnicode(code);
                // Only really-mapped glyphs: GetCharWidth would hand unmapped codes
                // the notdef advance, which is exactly the guess this path replaces.
                if (uni == 0 || !tp.CMap.TryGetValue(uni, out var gid) || gid == 0
                    || gid >= tp.GlyphWidths.Length) continue;
                var w = tp.GlyphWidths[gid];
                if (w <= 0) continue;
                widths[code] = (int)System.Math.Round(w * 1000.0 / tp.UnitsPerEm);
                any = true;
            }
            return any ? widths : null;
        }
        catch { return null; }
    }

    /// <summary>Lookup spellings for a PDF base-font name: as written, the comma
    /// style split ("Arial,Bold"), and camel-case splits ("VerdanaBold" →
    /// "Verdana Bold", "TimesNewRoman" → "Times New Roman").</summary>
    private static System.Collections.Generic.IEnumerable<string> SystemFaceNameCandidates(string baseName)
    {
        yield return baseName;
        var comma = baseName.Replace(',', ' ').Replace('-', ' ');
        if (comma != baseName) yield return comma;
        var camel = System.Text.RegularExpressions.Regex.Replace(
            comma, "(?<=[a-z])(?=[A-Z])", " ");
        if (camel != comma) yield return camel;
    }

    /// <summary>WinAnsi (CP1252) code → Unicode. 0 for the undefined 0x81/0x8D/0x8F/0x90/0x9D.</summary>
    private static int Cp1252ToUnicode(int code) => code switch
    {
        0x80 => 0x20AC, 0x82 => 0x201A, 0x83 => 0x0192, 0x84 => 0x201E,
        0x85 => 0x2026, 0x86 => 0x2020, 0x87 => 0x2021, 0x88 => 0x02C6,
        0x89 => 0x2030, 0x8A => 0x0160, 0x8B => 0x2039, 0x8C => 0x0152,
        0x8E => 0x017D, 0x91 => 0x2018, 0x92 => 0x2019, 0x93 => 0x201C,
        0x94 => 0x201D, 0x95 => 0x2022, 0x96 => 0x2013, 0x97 => 0x2014,
        0x98 => 0x02DC, 0x99 => 0x2122, 0x9A => 0x0161, 0x9B => 0x203A,
        0x9C => 0x0153, 0x9E => 0x017E, 0x9F => 0x0178,
        0x81 or 0x8D or 0x8F or 0x90 or 0x9D => 0,
        _ => code,
    };

    private static int[]? ExtractEmbeddedTrueTypeWidths(PdfDictionary fontDict, PdfReader reader,
        int firstChar, int lastChar)
    {
        var descriptor = reader.ResolveDict(fontDict.Get("FontDescriptor"));
        if (descriptor is null) return null;

        // /FontFile2 → TrueType font program
        var fontFileStream = reader.ResolveStream(descriptor.Get("FontFile2"));
        if (fontFileStream is null) return null;

        try
        {
            var fontData = reader.DecodeStream(fontFileStream);
            var ttf = new TrueTypeParser(fontData);
            ttf.Parse();

            if (ttf.GlyphWidths.Length == 0 || ttf.UnitsPerEm == 0) return null;

            var count = lastChar - firstChar + 1;
            if (count <= 0) count = 256;
            var widths = new int[count];
            var scale = 1000.0 / ttf.UnitsPerEm;

            for (var charCode = firstChar; charCode <= lastChar && charCode - firstChar < widths.Length; charCode++)
            {
                var w = ttf.GetCharWidth(charCode);
                widths[charCode - firstChar] = (int)(w * scale);
            }

            return widths;
        }
        catch
        {
            return null; // If TTF parsing fails, fall back to defaults
        }
    }

    /// <summary>
    /// Try to extract glyph widths from an embedded CFF font program (FontFile3)
    /// for simple (non-CID) fonts. Returns a widths array indexed by (charCode - firstChar).
    /// </summary>
    private static int[]? ExtractEmbeddedCffWidths(PdfDictionary fontDict, PdfReader reader,
        int firstChar, int lastChar)
    {
        var descriptor = reader.ResolveDict(fontDict.Get("FontDescriptor"));
        if (descriptor is null) return null;

        // /FontFile3 → CFF font program (Type1C or CIDFontType0C)
        var fontFileStream = reader.ResolveStream(descriptor.Get("FontFile3"));
        if (fontFileStream is null) return null;

        try
        {
            var cffData = reader.DecodeStream(fontFileStream);
            var parser = new CffParser(cffData);
            var glyphWidths = parser.ExtractWidths();

            if (glyphWidths.Count == 0) return null;

            var count = lastChar - firstChar + 1;
            if (count <= 0) count = 256;
            var widths = new int[count];

            // For simple CFF fonts, glyph indices generally map directly to char codes
            // (via the charset). As a practical approximation, use sequential mapping.
            for (var charCode = firstChar; charCode <= lastChar && charCode - firstChar < widths.Length; charCode++)
            {
                var glyphIdx = charCode; // simplified mapping for Type1C
                if (glyphWidths.TryGetValue(glyphIdx, out var w))
                    widths[charCode - firstChar] = w;
                else if (glyphWidths.TryGetValue(0, out var defW))
                    widths[charCode - firstChar] = defW;
            }

            return widths;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Try to extract glyph widths from an embedded CFF font program (FontFile3)
    /// for CID fonts. Returns a CID-to-width dictionary.
    /// </summary>
    private static Dictionary<int, int>? ExtractEmbeddedCffCidWidths(PdfDictionary cidFontDict, PdfReader reader)
    {
        var descriptor = reader.ResolveDict(cidFontDict.Get("FontDescriptor"));
        if (descriptor is null) return null;

        var fontFileStream = reader.ResolveStream(descriptor.Get("FontFile3"));
        if (fontFileStream is null) return null;

        try
        {
            var cffData = reader.DecodeStream(fontFileStream);
            var parser = new CffParser(cffData);
            var glyphWidths = parser.ExtractWidths();

            if (glyphWidths.Count == 0) return null;

            // For CIDFontType0C, glyph indices map directly to CIDs
            var cidWidths = new Dictionary<int, int>(glyphWidths.Count);
            foreach (var (gid, width) in glyphWidths)
            {
                cidWidths[gid] = width;
            }

            return cidWidths.Count > 0 ? cidWidths : null;
        }
        catch
        {
            return null;
        }
    }
}

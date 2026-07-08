using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

/// <summary>
/// Resolves glyph widths from PDF font dictionaries.
/// Supports /Widths arrays (simple fonts), /W + /DW entries (CIDFonts),
/// Standard 14 font fallback, and /MissingWidth from font descriptors.
/// Widths are in 1/1000 text space units.
/// </summary>
internal sealed class FontMetrics
{
    private readonly int[]? _simpleWidths; // indexed by (charCode - _firstChar)
    private readonly int _firstChar;
    private readonly int _lastChar;
    private readonly Dictionary<int, int>? _cidWidths; // CID → width
    // CID → unrounded glyph advance (1/1000 units) read from the embedded TrueType
    // hmtx. The PDF /W array stores integer advances, but the embedded font program
    // carries the full-precision values; using them for text-extraction measurement
    // avoids the sub-point drift that accumulates when integer /W widths are summed
    // across a run. Populated only for CIDFontType2 with an embedded /FontFile2.
    private Dictionary<int, double>? _cidWidthsExact; // CID → fractional width
    private readonly int _defaultWidth; // /DW or /MissingWidth or Standard14 default
    private readonly string? _baseFontName;
    private readonly bool _isStandard14;
    private readonly bool _isCid;

    private double _ascent;
    private double _descent;
    private double _capHeight;
    private int _winLineHeight; // (usWinAscent + usWinDescent) scaled to 1/1000 units
    private int _underlinePosition; // in 1/1000 units (negative = below baseline)
    private int _underlineThickness; // in 1/1000 units

    private FontMetrics(int[]? simpleWidths, int firstChar, int lastChar,
        Dictionary<int, int>? cidWidths, int defaultWidth,
        string? baseFontName, bool isStandard14, bool isCid)
    {
        _simpleWidths = simpleWidths;
        _firstChar = firstChar;
        _lastChar = lastChar;
        _cidWidths = cidWidths;
        _defaultWidth = defaultWidth;
        _baseFontName = baseFontName;
        _isStandard14 = isStandard14;
        _isCid = isCid;
    }

    /// <summary>Font ascent in 1/1000 text space units.</summary>
    public double Ascent => _ascent;

    /// <summary>Font descent in 1/1000 text space units (typically negative).</summary>
    public double Descent => _descent;

    /// <summary>Cap height in 1/1000 text space units.</summary>
    public double CapHeight => _capHeight;

    /// <summary>
    /// Line height based on OS/2 usWinAscent + usWinDescent (1/1000 units).
    /// More representative of the visual line height than Ascent-Descent.
    /// Returns 0 when Win metrics aren't available.
    /// </summary>
    public int WinLineHeight => _winLineHeight;

    /// <summary>Underline position below baseline in 1/1000 units (negative).</summary>
    public int UnderlinePosition => _underlinePosition;

    /// <summary>Underline thickness in 1/1000 units.</summary>
    public int UnderlineThickness => _underlineThickness;

    /// <summary>
    /// Build font metrics from a PDF font dictionary.
    /// </summary>
    public static FontMetrics FromFontDict(PdfDictionary fontDict, PdfReader reader)
    {
        var subtype = fontDict.GetName("Subtype");
        var baseFont = fontDict.GetName("BaseFont") ?? "";

        // Strip subset prefix (e.g. "ABCDEF+Helvetica" → "Helvetica")
        var normalizedBase = NormalizeFontName(baseFont);
        var isStandard14 = Standard14Fonts.IsStandard14(normalizedBase);

        if (subtype == "Type0")
            return BuildCidMetrics(fontDict, reader, normalizedBase, isStandard14);

        return BuildSimpleMetrics(fontDict, reader, normalizedBase, isStandard14);
    }

    /// <summary>
    /// <summary>
    /// The width explicitly listed in the CIDFont /W table for this CID, or null
    /// when the CID isn't in /W (so the caller can decide whether to use /DW or its
    /// own default rather than silently getting the default).
    /// </summary>
    public int? ExplicitCidWidth(int cid) =>
        _cidWidths is not null && _cidWidths.TryGetValue(cid, out var w) ? w : null;

    /// <summary>
    /// True for composite (Type0/CID) fonts, where character codes are read as
    /// two bytes rather than one. Lets callers iterate a show string code-by-code
    /// with the same code width unit <see cref="MeasureString(byte[], double)"/> uses.
    /// </summary>
    public bool IsCid => _isCid;

    /// <summary>
    /// Get the width of a character code in 1/1000 text space units.
    /// </summary>
    public int GetWidth(int charCode)
    {
        // 1. Simple font /Widths array
        if (_simpleWidths is not null)
        {
            var idx = charCode - _firstChar;
            if (idx >= 0 && idx < _simpleWidths.Length && _simpleWidths[idx] > 0)
                return _simpleWidths[idx];
        }

        // 2. CIDFont /W table
        if (_cidWidths is not null && _cidWidths.TryGetValue(charCode, out var cidW))
            return cidW;

        // 3. Standard 14 built-in widths
        if (_isStandard14 && _baseFontName is not null)
        {
            var w = Standard14Fonts.GetWidth(_baseFontName, charCode);
            if (w >= 0) return w;
        }

        // 4. Default width
        return _defaultWidth;
    }

    /// <summary>True when the font carries an explicit width for the character code —
    /// from the /Widths array, the CIDFont /W table, or Standard-14 built-ins. False
    /// means <see cref="GetWidth"/> would fall back to the default width, i.e. the code
    /// is (almost certainly) outside a subset's glyph coverage.</summary>
    public bool HasExplicitWidth(int charCode)
    {
        if (_simpleWidths is not null)
        {
            var idx = charCode - _firstChar;
            if (idx >= 0 && idx < _simpleWidths.Length && _simpleWidths[idx] > 0)
                return true;
        }
        if (_cidWidths is not null && _cidWidths.ContainsKey(charCode))
            return true;
        if (_isStandard14 && _baseFontName is not null &&
            Standard14Fonts.GetWidth(_baseFontName, charCode) >= 0)
            return true;
        return false;
    }

    /// <summary>
    /// Measure the width of a string in points, given a font size.
    /// For simple fonts, each byte is a character code.
    /// For CID fonts, pairs of bytes form character codes.
    /// </summary>
    public double MeasureString(byte[] bytes, double fontSize)
    {
        double total = 0;
        if (_isCid)
        {
            for (var i = 0; i + 1 < bytes.Length; i += 2)
            {
                var code = (bytes[i] << 8) | bytes[i + 1];
                total += GetWidth(code);
            }
        }
        else
        {
            foreach (var b in bytes)
                total += GetWidth(b);
        }

        return total * fontSize / 1000.0;
    }

    /// <summary>
    /// Measure the width of a Unicode string in points, given a font size.
    /// Uses Latin1 encoding for simple fonts, CID for Type0.
    /// </summary>
    public double MeasureString(string text, double fontSize)
    {
        double total = 0;
        if (_isCid)
        {
            foreach (var ch in text)
                total += GetWidth(ch);
        }
        else
        {
            foreach (var ch in text)
            {
                var code = ch < 256 ? ch : '?';
                total += GetWidth(code);
            }
        }

        return total * fontSize / 1000.0;
    }

    /// <summary>
    /// True when full-precision (unrounded) glyph advances are available from the
    /// embedded font program, so <see cref="MeasureStringExact(string, double)"/>
    /// improves on the integer /W widths.
    /// </summary>
    public bool HasExactWidths => _cidWidthsExact is not null;

    /// <summary>
    /// Width of a character code in 1/1000 units, preferring the unrounded embedded
    /// advance over the integer /W value. Falls back to <see cref="GetWidth"/> when no
    /// fractional value is available, so the result never diverges structurally from
    /// the integer path — it only restores the fractional part the /W array dropped.
    /// </summary>
    private double GetWidthExact(int charCode)
    {
        if (_cidWidthsExact is not null
            && _cidWidthsExact.TryGetValue(charCode, out var w))
            return w;
        return GetWidth(charCode);
    }

    /// <summary>
    /// Like <see cref="MeasureString(byte[], double)"/> but uses the unrounded
    /// embedded glyph advances when available. Used by text extraction so fragment
    /// rectangles match the precision of the original font, not the rounded /W copy.
    /// </summary>
    public double MeasureStringExact(byte[] bytes, double fontSize)
    {
        if (_cidWidthsExact is null)
            return MeasureString(bytes, fontSize);

        double total = 0;
        if (_isCid)
        {
            for (var i = 0; i + 1 < bytes.Length; i += 2)
            {
                var code = (bytes[i] << 8) | bytes[i + 1];
                total += GetWidthExact(code);
            }
        }
        else
        {
            foreach (var b in bytes)
                total += GetWidthExact(b);
        }

        return total * fontSize / 1000.0;
    }

    // ── Simple fonts (Type1, TrueType, Type3) ─────────────────────────

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
        var widthsObj = reader?.Resolve(fontDict.Get("Widths"));
        if (widthsObj is PdfArray widthsArr && widthsArr.Count > 0)
        {
            widths = new int[widthsArr.Count];
            for (var i = 0; i < widthsArr.Count; i++)
            {
                var w = reader?.Resolve(widthsArr[i]) ?? widthsArr[i];
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
                    // Preserve the original truncating cast so this change is a no-op here.
                    widths[i] = w switch
                    {
                        PdfInteger pi => (int)pi.Value,
                        PdfReal pr => (int)pr.Value,
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

        var defaultWidth = reader is not null ? GetMissingWidth(fontDict, reader) : 0;
        if (defaultWidth == 0 && isStandard14)
            defaultWidth = Standard14Fonts.GetDefaultWidth(normalizedBase);
        if (defaultWidth == 0)
            defaultWidth = 1000; // absolute fallback

        var metrics = new FontMetrics(widths, firstChar, lastChar, null, defaultWidth,
            normalizedBase, isStandard14, isCid: false);
        if (reader is not null)
            PopulateDescriptorMetrics(metrics, fontDict, reader);
        return metrics;
    }

    // ── CID fonts (Type0 composite fonts) ────────────────────────────

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

            // Also check the CIDFont's MissingWidth
            var mw = GetMissingWidth(cidFontDict, reader);
            if (mw > 0) defaultWidth = mw;
        }

        var metrics = new FontMetrics(null, 0, 0, cidWidths, defaultWidth,
            normalizedBase, isStandard14, isCid: true);
        if (cidFontDict is not null)
        {
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
                    // This matches .NET Aspose.PDF's text rectangle computation.
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

    // ── Embedded font extraction ────────────────────────────────────

    /// <summary>
    /// Try to extract glyph widths from an embedded TrueType font program (FontFile2).
    /// Returns null if no embedded font is found.
    /// </summary>
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

    // ── Embedded CFF font extraction ──────────────────────────────────

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

    // ── Utilities ─────────────────────────────────────────────────────

    private static int GetMissingWidth(PdfDictionary fontDict, PdfReader reader)
    {
        var descriptor = reader.ResolveDict(fontDict.Get("FontDescriptor"));
        if (descriptor is null) return 0;
        return (int)descriptor.GetInt("MissingWidth", 0);
    }

    private static int GetInt(PdfObject? obj) => obj switch
    {
        PdfInteger pi => (int)pi.Value,
        PdfReal pr => (int)pr.Value,
        _ => 0,
    };

    internal static string NormalizeFontName(string baseFont)
    {
        // Strip subset prefix: "ABCDEF+Helvetica" → "Helvetica"
        if (baseFont.Length > 7 && baseFont[6] == '+')
            return baseFont[7..];
        return baseFont;
    }
}

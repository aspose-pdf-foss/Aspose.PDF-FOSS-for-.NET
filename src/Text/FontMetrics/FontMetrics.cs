using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

/// <summary>
/// Resolves glyph widths from PDF font dictionaries.
/// Supports /Widths arrays (simple fonts), /W + /DW entries (CIDFonts),
/// Standard 14 font fallback, and /MissingWidth from font descriptors.
/// Widths are in 1/1000 text space units.
/// </summary>
internal sealed partial class FontMetrics
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

    // The font's /Encoding (or /BaseEncoding) is MacRomanEncoding: the
    // Standard-14 width table is laid out by WinAnsi CODE, so a MacRoman code
    // must be remapped before the lookup (MacRoman 0xD0 is the endash, whose
    // WinAnsi slot 0xD0 holds Eth's 722 — 166/1000 em too wide per dash).
    private bool _macRomanEncoding;

    // Embedded font program (FontFile2), for the lazy char->GID lookup that lets a
    // Unicode-text measure hit the subset's GID-keyed /W with the file's OWN advances.
    private byte[]? _embeddedProgram;
    private GlyphOutlineParser? _cmapParser;
    private bool _cmapTried;

    // Reverse /ToUnicode (unicode code point -> character CODE), the mapping a
    // subset with no cmap still carries; built lazily from the Type0 dict.
    private PdfDictionary? _type0Dict;
    private PdfReader? _reader;
    private Dictionary<int, int>? _reverseToUnicode;
    private bool _reverseTried;

    private int? CodeOfCp(int cp)
    {
        if (!_reverseTried)
        {
            _reverseTried = true;
            try
            {
                if (_type0Dict is not null && _reader is not null
                    && TextAbsorber.ParseToUnicodeFromDict(_type0Dict, _reader) is { } tu)
                {
                    _reverseToUnicode = new Dictionary<int, int>();
                    foreach (var (code, uni) in tu)
                    {
                        if (uni.Length == 0) continue;
                        int u = char.IsHighSurrogate(uni[0]) && uni.Length >= 2
                            ? char.ConvertToUtf32(uni[0], uni[1])
                            : uni[0];
                        _reverseToUnicode.TryAdd(u, code);
                    }
                }
            }
            catch { _reverseToUnicode = null; }
        }
        return _reverseToUnicode is not null && _reverseToUnicode.TryGetValue(cp, out var c) ? c : null;
    }

    // System face of the same family, for chars a subset's /W and /ToUnicode never
    // saw (a replacement's fresh glyphs) — they wrap by the REAL family
    // advances, not the Helvetica AFM.
    private int[]? _sysRawWidths;
    private int _sysUpm;
    private bool _sysTried;

    private double? SystemAdvance(int ch)
    {
        if (!_sysTried)
        {
            _sysTried = true;
            try
            {
                if (_baseFontName is { Length: > 0 }
                    && FontRepository.TryFindFont(_baseFontName, ignoreCase: true) is { } sysF
                    && sysF.SourceFontData?.TtfData is { Length: > 12 } ttfRaw)
                {
                    var (w, upm) = FontRepository.ReadTtfRawMetrics(ttfRaw);
                    _sysRawWidths = w;
                    _sysUpm = upm;
                }
            }
            catch { }
        }
        if (_sysRawWidths is null || _sysUpm <= 0) return null;
        var idx = ch < 256 ? ch : '?';
        if (idx < 0 || idx >= _sysRawWidths.Length) return null;
        var raw = _sysRawWidths[idx];
        return raw > 0 ? raw * 1000.0 / _sysUpm : null;
    }

    private int? GidOf(int cp)
    {
        if (!_cmapTried)
        {
            _cmapTried = true;
            try
            {
                if (_embeddedProgram is { Length: > 12 })
                    _cmapParser = new GlyphOutlineParser(_embeddedProgram);
            }
            catch { _cmapParser = null; }
        }
        if (_cmapParser is null) return null;
        try
        {
            var g = _cmapParser.GlyphIdOrLookAlike(cp);
            return g > 0 ? g : null;
        }
        catch { return null; }
    }

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
    // Metrics are built ONCE per font dictionary instance: extraction re-states the
    // same font at every Tf (hundreds of times on a dense page), and rebuilding the
    // /Widths → /W → descriptor → program chain each time dominated absorb time.
    // Keyed by the DICT instance (never a resource name — a form's /T1_0 is not the
    // page's /T1_0); an edit that swaps in a new font dict gets a new key.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PdfDictionary, FontMetrics>
        _metricsCache = new();

    public static FontMetrics FromFontDict(PdfDictionary fontDict, PdfReader reader)
    {
        if (_metricsCache.TryGetValue(fontDict, out var cached)) return cached;

        var subtype = fontDict.GetName("Subtype");
        var baseFont = fontDict.GetName("BaseFont") ?? "";

        // Strip subset prefix (e.g. "ABCDEF+Helvetica" → "Helvetica")
        var normalizedBase = NormalizeFontName(baseFont);
        var isStandard14 = Standard14Fonts.IsStandard14(normalizedBase);

        var metrics = subtype == "Type0"
            ? BuildCidMetrics(fontDict, reader, normalizedBase, isStandard14)
            : BuildSimpleMetrics(fontDict, reader, normalizedBase, isStandard14);
        _metricsCache.AddOrUpdate(fontDict, metrics);
        return metrics;
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

        // 3. Standard 14 built-in widths — simple fonts only. A CID font's codes are
        // CIDs, not standard-encoding character codes, so the Standard-14 table would
        // return an unrelated glyph's width (CID 58 is not ':'); a CID absent from /W
        // takes the /DW default per PDF 32000 §9.7.4.3.
        if (_isStandard14 && !_isCid && _baseFontName is not null)
        {
            var lookup = _macRomanEncoding ? MacRomanToWinAnsiCode(charCode) : charCode;
            if (lookup >= 0)
            {
                var w = Standard14Fonts.GetWidth(_baseFontName, lookup);
                if (w >= 0) return w;
            }
        }

        // 4. Default width
        return _defaultWidth;
    }

    /// <summary>Translate a MacRomanEncoding code to the WinAnsi code the
    /// Standard-14 width table is indexed by (same glyph, different slot).
    /// ASCII passes through; a MacRoman character WinAnsi cannot express
    /// returns -1 (the caller falls to the default width).</summary>
    private static int MacRomanToWinAnsiCode(int code)
    {
        if (code < 0x80) return code;
        var uni = TextAbsorber.MacRomanCodeToUnicode(code);
        if (uni == 0) return -1;
        // WinAnsi is CP1252: Latin-1 codes map to themselves except the
        // 0x80-0x9F window, which holds the CP1252 specials.
        if (uni < 0x100 && uni is not (>= 0x80 and <= 0x9F)) return uni;
        return uni switch
        {
            0x20AC => 0x80, 0x201A => 0x82, 0x0192 => 0x83, 0x201E => 0x84,
            0x2026 => 0x85, 0x2020 => 0x86, 0x2021 => 0x87, 0x02C6 => 0x88,
            0x2030 => 0x89, 0x0160 => 0x8A, 0x2039 => 0x8B, 0x0152 => 0x8C,
            0x017D => 0x8E, 0x2018 => 0x91, 0x2019 => 0x92, 0x201C => 0x93,
            0x201D => 0x94, 0x2022 => 0x95, 0x2013 => 0x96, 0x2014 => 0x97,
            0x02DC => 0x98, 0x2122 => 0x99, 0x0161 => 0x9A, 0x203A => 0x9B,
            0x0153 => 0x9C, 0x017E => 0x9E, 0x0178 => 0x9F,
            _ => -1,
        };
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
        if (_isStandard14 && !_isCid && _baseFontName is not null)
        {
            var lookup = _macRomanEncoding ? MacRomanToWinAnsiCode(charCode) : charCode;
            if (lookup >= 0 && Standard14Fonts.GetWidth(_baseFontName, lookup) >= 0)
                return true;
        }
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
            // The argument is UNICODE text, not a decoded show string, so its chars are
            // not this font's CIDs — /W (keyed by CID) only matches by coincidence, and
            // the misses fall to /DW (typically 1000, ~2× too wide) and inflate the
            // measure. For a standard face (Arial/Helvetica/Times/Courier) the base-face
            // Standard-14 advance IS the right width for the character; use it so a
            // caller wrapping fresh text into a box (post-replace reflow) gets a faithful
            // line count. Non-standard CID faces keep the CID /W → /DW path.
            var useStd14 = _isStandard14 && _baseFontName is not null;
            for (var ci = 0; ci < text.Length; ci++)
            {
                var ch = text[ci];
                // A surrogate PAIR is one glyph: a supplementary-plane ideograph
                // (CJK Ext-B) measured per UTF-16 unit would count /DW twice — a
                // 2 em advance that halves every re-flowed line. One full-width
                // default advance per pair (its CID is unreachable without the
                // cmap, and a supplementary CJK glyph advances a full em).
                if (char.IsHighSurrogate(ch) && ci + 1 < text.Length && char.IsLowSurrogate(text[ci + 1]))
                {
                    var scp = char.ConvertToUtf32(ch, text[ci + 1]);
                    ci++;
                    // The reversed /ToUnicode names the exact CODE the file draws this
                    // code point with — the key its /W advances are stored under; a
                    // subset with a surviving cmap resolves through the GID instead.
                    if (CodeOfCp(scp) is { } sc && _cidWidths is not null
                        && _cidWidths.TryGetValue(sc, out var sw1) && sw1 > 0)
                        total += sw1;
                    else if (GidOf(scp) is { } sg && _cidWidths is not null
                        && _cidWidths.TryGetValue(sg, out var sw2) && sw2 > 0)
                        total += sw2;
                    else
                        total += _defaultWidth;
                    continue;
                }
                // The file's own advance first: the reversed /ToUnicode (or a
                // surviving cmap) maps the char to the CODE its /W advance is keyed
                // by, so an ArialMT subset measures with Arial's true widths rather
                // than the Helvetica AFM — replacement text wraps by these.
                if (CodeOfCp(ch) is { } c0 && _cidWidths is not null
                    && _cidWidths.TryGetValue(c0, out var cw0) && cw0 > 0)
                {
                    total += cw0;
                    continue;
                }
                if (GidOf(ch) is { } g0 && _cidWidths is not null
                    && _cidWidths.TryGetValue(g0, out var gw) && gw > 0)
                {
                    total += gw;
                    continue;
                }
                if (useStd14)
                {
                    // Prefer a /W entry that happens to be keyed by this char code (a
                    // Latin subset commonly numbers CIDs by the original code, so 'E'→69
                    // hits its true advance), then the FAMILY's own system face (the
                    // reference wraps an ArialMT subset's fresh chars by Arial's true
                    // advances, not the Helvetica AFM), then the Standard-14 width,
                    // then /DW.
                    if (_cidWidths is not null && _cidWidths.TryGetValue(ch, out var cw) && cw > 0)
                        total += cw;
                    else if (SystemAdvance(ch) is { } sysW)
                        total += sysW;
                    else
                    {
                        var sw = Standard14Fonts.GetWidth(_baseFontName!, ch < 256 ? ch : '?');
                        total += sw >= 0 ? sw : _defaultWidth;
                    }
                }
                else if (ch < 0x100 && (_cidWidths is null || !_cidWidths.ContainsKey(ch)))
                {
                    // Non-standard CID face (a CJK subset): a LATIN char absent from
                    // the CID-keyed /W falls to /DW — a full CJK em, ~2× too wide —
                    // and a fresh-text measure (post-replace re-flow packing Latin
                    // words through the paragraph's CJK face) breaks its lines twice
                    // as early as it should. Take the Helvetica Standard-14
                    // advance for such chars; real /W hits and CJK text keep /W → /DW.
                    var sw = Standard14Fonts.GetWidth("Helvetica", ch);
                    total += sw >= 0 ? sw : GetWidth(ch);
                }
                else
                    total += GetWidth(ch);
            }
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

    // ── CID fonts (Type0 composite fonts) ────────────────────────────

    // ── Embedded font extraction ────────────────────────────────────

    /// <summary>
    /// Try to extract glyph widths from an embedded TrueType font program (FontFile2).
    /// Returns null if no embedded font is found.
    /// </summary>
    // Cache: BaseFont name → widths for codes 0..255 (null = the name resolved to
    // nothing usable). One resolution per face name per process, not per Tf switch.
    private static readonly System.Collections.Generic.Dictionary<string, int[]?> _systemFaceWidthsCache =
        new(System.StringComparer.OrdinalIgnoreCase);

    // ── Embedded CFF font extraction ──────────────────────────────────

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

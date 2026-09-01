using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
    /// <summary>True when the font's /Encoding /Differences carries glyph names that
    /// aren't Adobe-Glyph-List-resolvable (custom "G12"-style or arbitrary tags) — the
    /// decoded text for such runs is best-effort, and with
    /// <see cref="TextSearchOptions.LogTextExtractionErrors"/> each affected show
    /// operator is reported through <see cref="Errors"/>.</summary>
    internal static bool DifferencesNotAglCompliant(PdfDictionary? fontDict, PdfReader reader)
    {
        if (fontDict is null) return false;
        var encodingObj = fontDict.Get("Encoding");
        var encodingDict = encodingObj as PdfDictionary ?? (encodingObj is null ? null : reader.ResolveDict(encodingObj));
        if (encodingDict is null) return false;
        var diffObj = reader.Resolve(encodingDict.Get("Differences"));
        if (diffObj is not PdfArray diffArray || diffArray.Count == 0) return false;
        foreach (var item in diffArray)
        {
            if (item is not PdfName nameVal) continue;
            var name = nameVal.Value;
            if (name.Length == 1) continue;
            if (GlyphNameToUnicode.ContainsKey(name)) continue;
            if (name.StartsWith("uni", StringComparison.Ordinal) && name.Length >= 7 && IsAllHex(name.Substring(3))) continue;
            if (name.Length >= 5 && name.Length <= 7 && name[0] == 'u' && IsAllHex(name.Substring(1))) continue;
            return true; // e.g. "G12" glyph-index names, producer-specific tags
        }
        return false;
    }

    private void RecordAglError(string? fontKey, string extracted, double x, double y)
    {
        var key = fontKey ?? "?";
        var summary = $"Font {key} contains glyphs notification that isn't compliant with Adobe Glyph List.";
        var description = "The font has Differences array. It is used for glyph to Unicode mapping. "
            + "But font's glyphs notification isn't compliant with Adobe Glyph List. "
            + string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "The text on position {{X={0:0.###},Y={1:0.###}}} may be extracted incorrectly.", x, y);
        Errors.Add(new TextExtractionError
        {
            PageIndex = _currentPageNumber,
            Message = summary,
            Summary = summary,
            Description = description,
            ExtractedText = extracted,
            FontKey = key,
            Location = new TextExtractionErrorLocation
            {
                PageNumber = _currentPageNumber,
                FontUsedKey = key,
                TextStartPoint = new Aspose.Pdf.Point(x, y),
            },
        });
    }

    /// <summary>
    /// Decode a byte string using the font's encoding. Used by both TextAbsorber and TextFragmentAbsorber.
    /// </summary>
    internal static string DecodeStringPublic(byte[] bytes, Dictionary<int, string>? toUnicode,
        PdfDictionary? fontDict, PdfReader reader, bool useFontEngineEncoding = false,
        bool foldNbsp = true)
        => NormalizeDecoded(DecodeString(bytes, toUnicode, fontDict, reader, useFontEngineEncoding), foldNbsp);

    /// <summary>
    /// Normalize a decoded text string for extraction. Full-page plain extraction
    /// folds U+00A0 (non-breaking space) to a regular
    /// space, but RECT-LIMITED extraction preserves NBSP verbatim: the
    /// windowed output keeps the source's NBSP glyphs, and phrase
    /// asserts depend on it. Fragment extraction (TextFragmentAbsorber) always
    /// preserves it (foldNbsp=false).
    /// </summary>
    private static string NormalizeDecoded(string s, bool foldNbsp = true)
    {
        if (foldNbsp && s.IndexOf('\u00a0') >= 0) s = s.Replace('\u00a0', ' ');
        // Some PDFs ship a buggy ToUnicode CMap that maps a whitespace glyph to a
        // sequence containing CR/LF (e.g. the space glyph -> "\t\r  "). Those are
        // glyph text, not line structure (the absorber emits its own line breaks
        // from Td/T*/' positioning), so a stray CR/LF inside decoded glyph text
        // would corrupt extraction. Collapse any whitespace run that contains a
        // CR or LF into a single space; tab-only and normal spacing are untouched.
        // This holds in RAW mode too: the raw output of a document whose space
        // glyph decodes to CR/LF keeps its words on one line.
        if (s.IndexOf('\r') >= 0 || s.IndexOf('\n') >= 0)
            s = CollapseControlWhitespace(s);
        return s;
    }

    private static string CollapseControlWhitespace(string s)
    {
        static bool IsWs(char c) => c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\f' || c == '\v';
        var sb = new System.Text.StringBuilder(s.Length);
        int i = 0;
        while (i < s.Length)
        {
            if (IsWs(s[i]))
            {
                int j = i;
                bool hasBreak = false;
                while (j < s.Length && IsWs(s[j]))
                {
                    if (s[j] == '\r' || s[j] == '\n') hasBreak = true;
                    j++;
                }
                if (hasBreak) sb.Append(' ');          // whitespace run with CR/LF -> single space
                else sb.Append(s, i, j - i);            // no CR/LF -> leave untouched
                i = j;
            }
            else
            {
                sb.Append(s[i]);
                i++;
            }
        }
        return sb.ToString();
    }

    private static string DecodeString(byte[] bytes, Dictionary<int, string>? toUnicode,
        PdfDictionary? fontDict, PdfReader reader, bool useFontEngineEncoding = false)
    {
        // Resolve Differences encoding upfront (used as fallback below)
        Dictionary<int, string>? differences = null;
        string? baseEncodingName = null;

        var encodingObj = fontDict?.Get("Encoding");
        PdfDictionary? encodingDict = null;
        if (encodingObj is PdfDictionary ed)
            encodingDict = ed;
        else if (encodingObj is not null)
            encodingDict = reader.ResolveDict(encodingObj);

        if (encodingDict is not null)
        {
            differences = ParseDifferencesEncoding(encodingDict, reader);
            baseEncodingName = encodingDict.GetName("BaseEncoding");
        }

        // 1. ToUnicode CMap — highest priority; Differences used as fallback for unmapped codes
        if (toUnicode is not null)
        {
            return DecodeWithToUnicode(bytes, toUnicode, fontDict, reader, differences, baseEncodingName);
        }

        // Identity-H / Identity-V — 2-byte CID encoding
        // Also handle Uni*-UCS2-* / Uni*-UTF16-* predefined CMaps (2-byte big-endian → Unicode codepoint)
        if (fontDict?.GetName("Subtype") == "Type0")
        {
            var cidEncoding = fontDict.GetName("Encoding");
            if (cidEncoding is not null && (
                cidEncoding == "Identity-H" || cidEncoding == "Identity-V" ||
                cidEncoding.Contains("-UCS2-") || cidEncoding.Contains("-UTF16-")))
            {
                // A Uni*-UCS2-* / Uni*-UTF16-* CMap emits UNICODE, not Adobe CIDs: the
                // 2-byte code IS the codepoint, so neither the collection's CID table nor
                // a glyph-id inversion applies to it — both would substitute an unrelated
                // character for every code that happens to be a valid CID in the
                // ordering. Same distinction the renderers draw
                // (CidFontInfo.IsUnicodeEncoding); only Identity-H/V has code == CID.
                var isUnicodeCMap = cidEncoding.Contains("-UCS2-") || cidEncoding.Contains("-UTF16-");
                // Try to get Adobe CID collection ordering for predefined table lookup
                var cidOrdering = isUnicodeCMap ? null : GetCidOrdering(fontDict, reader);
                // A CID font without /ToUnicode: for a NON-embedded font, recover Unicode by
                // inverting the installed system face's cmap — the producer assigned glyph
                // ids from that same face, so these documents stay decodable. For an
                // EMBEDDED program the raw-code fallback is kept (the
                // "NoToUnicode_UseRawCode" behaviour), so cmap inversion there stays opt-in
                // via TextSearchOptions.UseFontEngineEncoding.
                var gidToUnicode = isUnicodeCMap
                    ? null
                    : GetGidToUnicode(fontDict, reader, allowEmbedded: useFontEngineEncoding);
                return DecodeCidString(bytes, toUnicode, cidOrdering, gidToUnicode);
            }

            // Predefined legacy national CMap (GBK-EUC-H, 90ms-RKSJ-H, KSC-EUC-H, …):
            // the show-string bytes are a national multi-byte charset (mixed 1-/2-byte
            // codes), NOT Adobe CIDs. Without this branch the bytes fell through to the
            // per-byte WinAnsi default and Chinese/Japanese/Korean text extracted as
            // Latin-1 mojibake ("由 扫描全能王" → "ÓÉ É¨Ãè…"). Decode through the same
            // codepage tables the renderer already uses (GbkTable/SjisTable/KscTable).
            if (cidEncoding is not null && GetLegacyCidInfo(fontDict, reader) is { } legacy)
            {
                var sb = new StringBuilder();
                var i = 0;
                while (i < bytes.Length)
                {
                    var step = legacy.LegacyByteLength(bytes[i]);
                    if (step == 2 && i + 1 >= bytes.Length) step = 1;
                    if (step == 1)
                    {
                        sb.Append((char)bytes[i]);
                    }
                    else
                    {
                        var code = (bytes[i] << 8) | bytes[i + 1];
                        if (legacy.LegacyToUnicode(code) is int u)
                            sb.Append(char.ConvertFromUtf32(u));
                        else
                            sb.Append('�');
                    }
                    i += step;
                }
                return sb.ToString();
            }
        }

        // 2. Differences from Encoding dict
        if (differences is not null)
        {
            var sb = new StringBuilder();
            foreach (var b in bytes)
            {
                if (differences.TryGetValue(b, out var mapped))
                    sb.Append(mapped);
                else
                    sb.Append(DecodeByteWithEncoding(b, baseEncodingName));
            }
            return sb.ToString();
        }

        // 3. BaseEncoding from Encoding dict (no Differences)
        if (baseEncodingName is not null)
            return DecodeWithNamedEncoding(bytes, baseEncodingName);

        // 4. Encoding is a name
        var encoding = fontDict?.GetName("Encoding");
        if (encoding is not null)
            return DecodeWithNamedEncoding(bytes, encoding);

        // 4b. No /ToUnicode and no /Encoding at all: a symbolic embedded subset font
        // (FirstChar 1, custom glyph order) whose only Unicode signal is its own program.
        // Recover code → Unicode from the embedded cmap + post glyph names (Adobe Glyph
        // List). Without this the bytes fall through to WinAnsi and Cyrillic/Greek subsets
        // decode as control-char mojibake.
        if (encodingObj is null && fontDict is not null
            && fontDict.GetName("Subtype") != "Type0")
        {
            var postMap = GetPostNameCodeToUnicode(fontDict, reader);
            if (postMap is not null)
            {
                var sb = new StringBuilder(bytes.Length);
                foreach (var b in bytes)
                    sb.Append(postMap.TryGetValue(b, out var u) ? u : DecodeByteWithEncoding(b, null).ToString());
                return sb.ToString();
            }

            // 4c. Not even post names (format-3 post, PUA-only cmap): zero Unicode
            // semantics anywhere. Fall back to recognising each
            // glyph's OUTLINE SHAPE, locked on for the font once a code below
            // 0x20 proves it is not character-coded (gate machine —
            // sequential-by-first-use Ghostscript subsets start at 0x01).
            if (reader is not null)
            {
                var shaped = GlyphShapeDecoder.TryDecode(bytes, fontDict, reader);
                if (shaped is not null) return shaped;
            }

            // 4d. A non-embedded Standard-14 Type 1 font with no /Encoding entry
            // uses the font program's BUILT-IN encoding — StandardEncoding, not
            // WinAnsi. The two agree on printable ASCII but diverge completely in
            // the high range (0xC1 is the grave ACCENT in Standard, Á in WinAnsi),
            // so only bytes in Standard's high range take this path; the ASCII
            // range keeps the established default below.
            if (IsStandardLatinType1(fontDict) && bytes.Any(b => b >= 0xA1))
            {
                var sb = new StringBuilder(bytes.Length);
                foreach (var b in bytes)
                {
                    string? uni = null;
                    if (b >= 0xA1 && Type1StandardEncoding.GetName(b) is { } gname
                        && GlyphNameToUnicode.TryGetValue(gname, out var mapped))
                        uni = mapped;
                    if (uni is not null) sb.Append(uni);
                    else sb.Append(DecodeByteWithEncoding(b, "WinAnsiEncoding"));
                }
                return sb.ToString();
            }
        }

        // 5. Check for Symbol or ZapfDingbats built-in font encoding
        var baseFont = fontDict?.GetName("BaseFont");
        if (baseFont is not null)
        {
            var cleanName = baseFont.Contains('+') ? baseFont.Substring(baseFont.IndexOf('+') + 1) : baseFont;
            if (cleanName == "Symbol")
            {
                var sb = new StringBuilder(bytes.Length);
                foreach (var b in bytes)
                    sb.Append(SymbolEncoding.TryGetValue(b, out var ch) ? ch : (char)b);
                return sb.ToString();
            }
            if (cleanName == "ZapfDingbats")
            {
                var sb = new StringBuilder(bytes.Length);
                foreach (var b in bytes)
                    sb.Append(ZapfDingbatsEncoding.TryGetValue(b, out var ch) ? ch : (char)b);
                return sb.ToString();
            }
        }

        // 6. Default: WinAnsiEncoding
        return DecodeWithNamedEncoding(bytes, null);
    }

    /// <summary>True for a non-embedded Latin Standard-14 Type 1 font (Helvetica /
    /// Times / Courier families) — the fonts whose built-in encoding is Adobe
    /// StandardEncoding. Symbol and ZapfDingbats have their own built-ins and are
    /// handled separately; an embedded program carries its own encoding.</summary>
    private static bool IsStandardLatinType1(PdfDictionary fontDict)
    {
        if (fontDict.GetName("Subtype") != "Type1") return false;
        var baseFont = fontDict.GetName("BaseFont");
        if (baseFont is null) return false;
        var clean = baseFont.Contains('+') ? baseFont[(baseFont.IndexOf('+') + 1)..] : baseFont;
        return clean is "Helvetica" or "Helvetica-Bold" or "Helvetica-Oblique" or "Helvetica-BoldOblique"
            or "Times-Roman" or "Times-Bold" or "Times-Italic" or "Times-BoldItalic"
            or "Courier" or "Courier-Bold" or "Courier-Oblique" or "Courier-BoldOblique";
    }

    private static string DecodeWithNamedEncoding(byte[] bytes, string? encoding)
    {
        if (encoding == "MacRomanEncoding")
        {
            var sb = new StringBuilder(bytes.Length);
            foreach (var b in bytes)
                sb.Append(DecodeByteWithEncoding(b, "MacRomanEncoding"));
            return sb.ToString();
        }

        // WinAnsiEncoding or null (default)
        if (encoding == "WinAnsiEncoding" || encoding is null)
        {
            var sb = new StringBuilder(bytes.Length);
            foreach (var b in bytes)
                sb.Append(DecodeByteWithEncoding(b, "WinAnsiEncoding"));
            return sb.ToString();
        }

        // Identity-H / Identity-V and other 2-byte predefined CJK CMaps
        if (encoding == "Identity-H" || encoding == "Identity-V" ||
            encoding.Contains("-UCS2-") || encoding.Contains("-UTF16-"))
            return DecodeCidString(bytes, null);

        // Unknown encoding — treat as WinAnsi
        var sb2 = new StringBuilder(bytes.Length);
        foreach (var b in bytes)
            sb2.Append(DecodeByteWithEncoding(b, "WinAnsiEncoding"));
        return sb2.ToString();
    }

    private static char DecodeByteWithEncoding(byte b, string? encoding)
    {
        if (encoding == "MacRomanEncoding")
        {
            if (b < 128)
                return (char)b;
            return MacRomanEncoding.TryGetValue(b, out var ch) ? ch : (char)b;
        }

        // WinAnsiEncoding (default)
        if (b < 128)
            return (char)b;
        return WinAnsiEncoding.TryGetValue(b, out var wch) ? wch : (char)b;
    }

    /// <summary>
    /// Parse the /Differences array from an encoding dictionary.
    /// Returns a map from byte code to Unicode string, or null if no Differences found.
    /// </summary>
    internal static Dictionary<int, string>? ParseDifferencesEncoding(PdfDictionary encodingDict, PdfReader reader)
    {
        var diffObj = encodingDict.Get("Differences");
        PdfArray? diffArray = null;

        if (diffObj is PdfArray arr)
            diffArray = arr;
        else if (diffObj is not null)
        {
            // Could be an indirect reference
            var resolved = reader.Resolve(diffObj);
            if (resolved is PdfArray resolvedArr)
                diffArray = resolvedArr;
        }

        if (diffArray is null || diffArray.Count == 0)
            return null;

        var map = new Dictionary<int, string>();
        var currentCode = 0;

        foreach (var item in diffArray)
        {
            if (item is PdfInteger intVal)
            {
                currentCode = (int)intVal.Value;
            }
            else if (item is PdfName nameVal)
            {
                var glyphName = nameVal.Value;
                var resolved = ResolveGlyphName(glyphName);
                if (resolved is not null)
                    map[currentCode] = resolved;
                else
                    map[currentCode] = ((char)currentCode).ToString(); // fallback to code point
                currentCode++;
            }
        }

        return map.Count > 0 ? map : null;
    }

    /// <summary>
    /// Resolve an Adobe glyph name to its Unicode string representation.
    /// Supports dictionary lookup, uni&lt;XXXX&gt; and u&lt;XXXX&gt; patterns.
    /// </summary>
    internal static string? ResolveGlyphName(string name)
    {
        // Single ASCII character — return as-is
        if (name.Length == 1) return name;

        // Dictionary lookup
        if (GlyphNameToUnicode.TryGetValue(name, out var unicode))
            return unicode;

        // uni<XXXX> form — explicit Unicode codepoint(s), groups of 4 hex digits
        if (name.Length >= 7 && name.StartsWith("uni", StringComparison.Ordinal))
        {
            var hex = name.Substring(3);
            if (hex.Length % 4 == 0 && IsAllHex(hex))
            {
                var sb = new StringBuilder();
                for (int i = 0; i < hex.Length; i += 4)
                    sb.Append((char)Convert.ToInt32(hex.Substring(i, 4), 16));
                return sb.Length > 0 ? sb.ToString() : null;
            }
        }

        // u<XXXX> form — single codepoint, 4-6 hex digits
        if (name.Length >= 5 && name.Length <= 7 && name[0] == 'u' && IsAllHex(name.Substring(1)))
            return char.ConvertFromUtf32(Convert.ToInt32(name.Substring(1), 16));

        // AGL underscore ligatures — components joined with '_' (/f_i, /f_f_i):
        // the joined component NAME is looked up first so the standard ligature
        // codepoints apply ("f"+"i" → "fi" → U+FB01), else the components'
        // resolutions concatenate.
        if (name.IndexOf('_') > 0)
        {
            var parts = name.Split('_');
            var joinedName = string.Concat(parts);
            if (GlyphNameToUnicode.TryGetValue(joinedName, out var lig))
                return lig;
            var sb = new StringBuilder();
            foreach (var part in parts)
            {
                if (part.Length == 0) continue;
                var comp = ResolveGlyphName(part);
                if (comp is null) return null;
                sb.Append(comp);
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }

        // G<number> form — glyph index used as character code (common in subset fonts)
        // e.g. /G65 → 'A', /G32 → ' ', /G147 → U+201C via WinAnsi
        if (name.Length >= 2 && name[0] == 'G')
        {
            var suffix = name.Substring(1);
            if (suffix.Length > 0 && suffix.All(char.IsAsciiDigit))
            {
                var code = int.Parse(suffix);
                if (code < 128)
                    return ((char)code).ToString();
                if (code < 256)
                {
                    // Map through WinAnsiEncoding for 128-255
                    if (WinAnsiEncoding.TryGetValue((byte)code, out var wch))
                        return wch.ToString();
                }
                return char.ConvertFromUtf32(code);
            }
        }

        return null;
    }

    private static bool IsAllHex(string s)
    {
        foreach (var c in s)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f')))
                return false;
        }
        return s.Length > 0;
    }

    /// <summary>A ToUnicode destination of a lone noncharacter (U+FFFF/U+FFFE)
    /// marks "unicode unknown", not a mapping.</summary>
    internal static bool IsUnknownToUnicodeDst(string s)
        => s.Length == 1 && (s[0] == '￿' || s[0] == '￾');

    /// <summary>The characters that ARE ligatures: the Latin ij/IJ pair, the f-ligature
    /// block, and the ae/oe letters. A glyph the encoding names as one of these keeps it
    /// even when the CMap spells the ligature out letter by letter.</summary>
    private static bool IsLigatureChar(char c) =>
        c is '\u0132' or '\u0133'                       // IJ, ij
          or '\u00C6' or '\u00E6'                       // AE, ae
          or '\u0152' or '\u0153'                       // OE, oe
          or >= '\uFB00' and <= '\uFB06';               // ff, fi, fl, ffi, ffl, st

    private static string DecodeWithToUnicode(byte[] bytes, Dictionary<int, string> map,
        PdfDictionary? fontDict, PdfReader reader,
        Dictionary<int, string>? differences = null, string? baseEncodingName = null)
    {
        var isCid = fontDict?.GetName("Subtype") == "Type0";
        var sb = new StringBuilder();
        var i = 0;

        while (i < bytes.Length)
        {
            // Try 2-byte lookup first (handles CIDFonts and mixed encodings)
            if (i + 1 < bytes.Length)
            {
                var code2 = (bytes[i] << 8) | bytes[i + 1];
                // Bypass a U+FFFF "unicode unknown" destination only when there is a
                // /Differences glyph name to fall through to (pdfTeX ligature codes in a
                // simple font). A CID font has no Differences, so its U+FFFF separators
                // must be kept — they get stripped downstream and replaced with the
                // U+A880 placeholder; the raw-code fallback below would corrupt them.
                if (map.TryGetValue(code2, out var mapped2) &&
                    !(IsUnknownToUnicodeDst(mapped2) && differences is not null && differences.ContainsKey(code2)))
                {
                    sb.Append(mapped2);
                    i += 2;
                    continue;
                }
            }

            // Try 1-byte lookup. A U+FFFF/U+FFFE destination is the producer
            // saying "unicode unknown" (pdfTeX writes it for ligature glyphs),
            // not a real mapping — those codes fall through to the Differences
            // glyph names, which DO resolve (/f_i → U+FB01).
            var code1 = bytes[i];
            if (map.TryGetValue(code1, out var mapped1) &&
                !(IsUnknownToUnicodeDst(mapped1) && differences is not null && differences.ContainsKey(code1)))
            {
                // A LIGATURE keeps its own character. When the CMap spells the glyph out
                // as the letters it is made of (a producer writing "ij" so the text can be
                // copied as separate letters) but the encoding names the glyph as a real
                // ligature character, the named one wins — that single character is what
                // the page draws and reads as. The test is deliberately narrow: only a
                // NAMED ligature qualifies, so a code whose CMap value merely carries a
                // trailing line break, or whose subset name resolves through a numeric
                // convention, keeps every character the CMap gave it.
                if (!isCid && mapped1.Length > 1 && !char.IsSurrogate(mapped1[0])
                    && differences is not null && differences.TryGetValue(code1, out var ligature)
                    && ligature.Length == 1 && IsLigatureChar(ligature[0]))
                    sb.Append(ligature);
                else
                    sb.Append(mapped1);
                i++;
                continue;
            }

            // Try Differences encoding as fallback (single byte)
            if (differences is not null && differences.TryGetValue(code1, out var diffMapped))
            {
                sb.Append(diffMapped);
                i++;
                continue;
            }

            // Fallback for CID fonts: interpret 2-byte value as direct Unicode (UCS-2/UTF-16)
            if (isCid && i + 1 < bytes.Length)
            {
                var code = (bytes[i] << 8) | bytes[i + 1];
                if (code is > 0 and < 0xD800 or > 0xDFFF and <= 0xFFFF)
                    sb.Append((char)code);
                else
                    sb.Append('\uFFFD');
                i += 2;
            }
            else
            {
                sb.Append(DecodeByteWithEncoding(bytes[i], baseEncodingName));
                i++;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Extracts the CIDSystemInfo /Ordering from the first DescendantFont of a Type0 font.
    /// Returns null if not available or if Registry is not "Adobe".
    /// </summary>
    private static string? GetCidOrdering(PdfDictionary type0FontDict, PdfReader reader)
    {
        if (reader is null) return null;
        var descObj = reader.Resolve(type0FontDict.Get("DescendantFonts"));
        if (descObj is not PdfArray descArr || descArr.Count == 0) return null;
        var cidFontDict = reader.ResolveDict(descArr[0]);
        if (cidFontDict is null) return null;
        var cidSystemInfo = reader.ResolveDict(cidFontDict.Get("CIDSystemInfo"));
        if (cidSystemInfo is null) return null;
        // Registry and Ordering are PDF strings (not names)
        var registryObj = cidSystemInfo.Get("Registry");
        var registry = registryObj is PdfString rs ? rs.ToText() : (registryObj is PdfName rn ? rn.Value : null);
        if (registry != "Adobe") return null;
        var orderingObj = cidSystemInfo.Get("Ordering");
        return orderingObj is PdfString os ? os.ToText() : (orderingObj is PdfName on2 ? on2.Value : null);
    }

    private static string DecodeCidString(byte[] bytes, Dictionary<int, string>? toUnicode,
        string? cidOrdering = null, Dictionary<int, int>? gidToUnicode = null)
    {
        if (toUnicode is not null)
            return DecodeWithToUnicode(bytes, toUnicode, null, null!);

        var sb = new StringBuilder();
        for (var i = 0; i + 1 < bytes.Length; i += 2)
        {
            var code = (bytes[i] << 8) | bytes[i + 1];
            // Try Adobe predefined CID collection lookup first
            if (cidOrdering is not null)
            {
                var unicode = AdobeCidTables.LookupCid(cidOrdering, code);
                if (unicode is not null)
                {
                    sb.Append(char.ConvertFromUtf32(unicode.Value));
                    continue;
                }
            }
            // Identity ordering with no ToUnicode: reverse-map the glyph id to Unicode
            // through the embedded font program's cmap (built once per font).
            if (gidToUnicode is not null && gidToUnicode.TryGetValue(code, out var u))
            {
                sb.Append(char.ConvertFromUtf32(u));
                continue;
            }
            sb.Append((char)code);
        }
        return sb.ToString();
    }

    /// <summary>Cache entry for a font's inverted gid → Unicode map, remembering whether the
    /// inversion source was the font's own embedded program (opt-in for decoding) or the
    /// installed system face (used by default for non-embedded fonts).</summary>
    private sealed class GidToUnicodeEntry
    {
        public Dictionary<int, int> Map = new();
        public bool FromEmbedded;
    }

    private sealed class PostNameMapEntry { public Dictionary<int, string>? Map; }

    /// <summary>
    /// Recover a byte-code → Unicode map for a simple (non-Type0) embedded TrueType font
    /// that carries neither /ToUnicode nor an /Encoding: walk the embedded program's cmap
    /// (code → glyph id) then its version-2.0 post table (glyph id → PostScript name) and
    /// resolve each name to Unicode through the Adobe Glyph List. This is how
    /// Cyrillic/Greek subset fonts (FirstChar 1, symbolic flag) that would otherwise
    /// decode as WinAnsi mojibake are read. Returns null when the font lacks embedded post names.
    /// Cached per font dictionary.
    /// </summary>
    private static Dictionary<int, string>? GetPostNameCodeToUnicode(PdfDictionary fontDict, PdfReader reader)
    {
        if (_postNameCache.TryGetValue(fontDict, out var cached)) return cached.Map;
        var entry = new PostNameMapEntry();
        try
        {
            var fd = reader.ResolveDict(fontDict.Get("FontDescriptor"));
            var ff2 = fd?.Get("FontFile2");
            var stream = ff2 is null ? null : reader.ResolveStream(ff2);
            var data = stream is not null ? reader.DecodeStream(stream) : null;
            if (data is not null)
            {
                var parser = new TrueTypeParser(data);
                parser.Parse();
                if (parser.GlyphNames.Count > 0 && parser.CMap.Count > 0)
                {
                    var map = new Dictionary<int, string>(parser.CMap.Count);
                    foreach (var kv in parser.CMap)
                    {
                        if (kv.Key > 0xFF) continue; // simple fonts use single-byte codes
                        if (parser.GlyphNames.TryGetValue(kv.Value, out var gname)
                            && ResolveGlyphName(gname) is { Length: > 0 } u)
                            map[kv.Key] = u;
                    }
                    if (map.Count > 0) entry.Map = map;
                }
            }
        }
        catch { entry.Map = null; }
        _postNameCache.AddOrUpdate(fontDict, entry);
        return entry.Map;
    }

    /// <summary>CidFontInfo for a Type0 font whose /Encoding is a predefined legacy
    /// national CMap (LegacyCodepage != 0); null for every other font. Cached per dict.</summary>
    private static CidFontInfo? GetLegacyCidInfo(PdfDictionary fontDict, PdfReader reader)
    {
        if (_legacyCidCache.TryGetValue(fontDict, out var cached)) return cached;
        CidFontInfo? info = null;
        try
        {
            var built = CidFontInfo.TryBuild(fontDict, reader);
            if (built is { LegacyCodepage: not 0 }) info = built;
        }
        catch { info = null; }
        _legacyCidCache.Add(fontDict, info);
        return info;
    }

    /// <summary>
    /// Build a glyph-id → Unicode map for an Identity-encoded Type0 font that lacks a
    /// /ToUnicode CMap, by inverting a TrueType cmap (threading the CID→GID mapping when
    /// /CIDToGIDMap is a stream). The inversion source is the embedded program when present
    /// (returned only when <paramref name="allowEmbedded"/> — raw codes are kept
    /// for embedded programs unless font-engine decoding is requested), otherwise the
    /// installed system face named by /BaseFont. Cached per font dictionary.
    /// </summary>
    private static Dictionary<int, int>? GetGidToUnicode(PdfDictionary fontDict, PdfReader reader,
        bool allowEmbedded)
    {
        if (_gidToUnicodeCache.TryGetValue(fontDict, out var cached))
            return cached.Map.Count > 0 && (allowEmbedded || !cached.FromEmbedded) ? cached.Map : null;

        var entry = new GidToUnicodeEntry();
        var map = entry.Map;
        try
        {
            var descArr = reader.Resolve(fontDict.Get("DescendantFonts")) as PdfArray;
            var descendant = descArr is { Count: > 0 } ? reader.ResolveDict(descArr[0]) : null;
            var fd = descendant is null ? null : reader.ResolveDict(descendant.Get("FontDescriptor"));
            var ff2 = fd?.Get("FontFile2") ?? fd?.Get("FontFile3");
            var stream = ff2 is null ? null : reader.ResolveStream(ff2);
            byte[]? data = stream is not null ? reader.DecodeStream(stream) : null;
            entry.FromEmbedded = data is not null;
            // A NON-embedded Identity CID font draws with the glyph ids of the real face
            // it names, so the installed system font's cmap carries the same gid → Unicode
            // relation the producer used. Resolve it as the inversion source.
            if (data is null && fontDict.GetName("BaseFont") is { } nonEmbeddedBase)
                data = SystemFontResolver.Resolve(nonEmbeddedBase);
            if (data is not null)
            {
                var parser = new TrueTypeParser(data);
                parser.Parse();
                // parser.CMap is Unicode → glyph id; invert it. When several codepoints map
                // to the SAME glyph (e.g. a hyphen glyph reachable from both U+002D
                // hyphen-minus and U+00AD soft-hyphen), prefer the SMALLEST codepoint — the
                // canonical ASCII/base character — instead of letting iteration order decide.
                // EXCEPT Arabic: shaped fonts map a contextual glyph from both its base
                // letter and its presentation form(s), and font-engine extraction reports
                // the PRESENTATION FORM (Arabic FE-block first, then FB-block Farsi/ligature
                // forms, then the base letter) in font-engine extraction.
                var gidToUni = new Dictionary<int, int>(parser.CMap.Count);
                static int ArabicRank(int c) =>
                    c is >= 0xFE70 and <= 0xFEFF ? 3
                    : c is >= 0xFB50 and <= 0xFDFF ? 2
                    : c is >= 0x0600 and <= 0x06FF ? 1
                    : 0;
                foreach (var kv in parser.CMap)
                {
                    if (!gidToUni.TryGetValue(kv.Value, out var existing))
                    {
                        gidToUni[kv.Value] = kv.Key;
                        continue;
                    }
                    var ra = ArabicRank(existing);
                    var rb = ArabicRank(kv.Key);
                    if (rb > ra || (rb == ra && kv.Key < existing))
                        gidToUni[kv.Value] = kv.Key;
                }

                // CIDToGIDMap: Identity (default) means CID == GID, so the 2-byte code is
                // already the glyph id. A stream maps CID → GID as packed big-endian uint16s.
                var c2g = descendant!.Get("CIDToGIDMap");
                var c2gStream = c2g is not null ? reader.ResolveStream(c2g) : null;
                if (c2gStream is not null)
                {
                    var cg = reader.DecodeStream(c2gStream);
                    for (int cid = 0; cid * 2 + 1 < cg.Length; cid++)
                    {
                        int gid = (cg[cid * 2] << 8) | cg[cid * 2 + 1];
                        if (gid != 0 && gidToUni.TryGetValue(gid, out var u)) map[cid] = u;
                    }
                }
                else
                {
                    foreach (var kv in gidToUni) map[kv.Key] = kv.Value;
                }
            }
        }
        catch { /* best-effort: leave the map empty so the caller falls back */ }

        _gidToUnicodeCache.AddOrUpdate(fontDict, entry);
        return map.Count > 0 && (allowEmbedded || !entry.FromEmbedded) ? map : null;
    }

    /// <summary>Arabic lam-alef is drawn by ONE glyph, and a CMap spells its
    /// destination out as the two letters it stands for (&lt;03F7&gt; →
    /// &lt;06440623&gt;). Taken literally that turns one glyph into two characters,
    /// and the visual-to-logical reorder then splits the pair and emits the alef
    /// ahead of the lam. Fold such a destination back to the single Presentation
    /// Forms-B ligature so one glyph stays one character (measured:
    /// the isolated form, never the final one).</summary>
    private static void FoldLamAlefLigatures(Dictionary<int, string> map)
    {
        List<int>? codes = null;
        foreach (var (code, dest) in map)
            if (dest.Length == 2 && dest[0] == ArabicLam && LamAlefLigature(dest[1]) != '\0')
                (codes ??= []).Add(code);
        if (codes is null) return;
        foreach (var code in codes)
            map[code] = LamAlefLigature(map[code][1]).ToString();
    }

    /// <summary>U+0644 ARABIC LETTER LAM.</summary>
    private const char ArabicLam = 'ل';

    /// <summary>The isolated lam-alef ligature standing for lam plus this alef
    /// variant, or NUL when the character is not an alef.</summary>
    private static char LamAlefLigature(char alef) => alef switch
    {
        'آ' => 'ﻵ', // alef with madda above
        'أ' => 'ﻷ', // alef with hamza above
        'إ' => 'ﻹ', // alef with hamza below
        'ا' => 'ﻻ', // plain alef
        _ => '\0',
    };

    /// <summary>A single glyph mapped (via ToUnicode or a marked-content
    /// /ActualText) to a TWO-letter ligature decomposition surfaces as the
    /// ligature codepoint — a "fi"-mapped glyph and an ActualText("fi") span both
    /// surface as U+FB01. A THREE-letter decomposition
    /// stays as its letters (e.g. "Effizent" stays searchable, not
    /// E+U+FB03+zent).</summary>
    private static string CollapseTwoCharLigature(string result) => result switch
    {
        "fi" => "ﬁ",
        "fl" => "ﬂ",
        "ff" => "ﬀ",
        _ => result,
    };

    /// <summary>
    /// Decode a PDF text string (handles BOM for UTF-16BE, otherwise Latin1).
    /// </summary>
    private static string DecodeTextString(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        return Encoding.Latin1.GetString(bytes);
    }
}

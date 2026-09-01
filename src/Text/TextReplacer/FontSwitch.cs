using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
    private static string EnsureStandardFont(PdfDictionary pageDict, PdfReader reader)
    {
        const string fallbackName = "_AsposePdfHlv";
        var resources = pageDict.Get("Resources") as PdfDictionary;
        resources ??= reader.ResolveDict(pageDict.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            pageDict.Set("Resources", resources);
        }
        var fonts = resources.Get("Font") as PdfDictionary;
        fonts ??= reader.ResolveDict(resources.Get("Font"));
        if (fonts is null)
        {
            fonts = new PdfDictionary();
            resources.Set("Font", fonts);
        }
        if (fonts.Get(fallbackName) is null)
        {
            var fontDict = new PdfDictionary();
            fontDict.Set("Type", new PdfName("Font"));
            fontDict.Set("Subtype", new PdfName("Type1"));
            fontDict.Set("BaseFont", new PdfName("Helvetica"));
            fontDict.Set("Encoding", new PdfName("WinAnsiEncoding"));
            fonts.Set(fallbackName, fontDict);
        }
        return fallbackName;
    }

    /// <summary>Resolve (creating if absent) the page/XObject's own /Resources /Font
    /// dictionary so a fallback font can be registered locally.</summary>
    private static PdfDictionary GetOrCreatePageFontDict(PdfDictionary pageDict, PdfReader reader)
    {
        var resources = pageDict.Get("Resources") as PdfDictionary;
        resources ??= reader.ResolveDict(pageDict.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            pageDict.Set("Resources", resources);
        }
        var fonts = resources.Get("Font") as PdfDictionary;
        fonts ??= reader.ResolveDict(resources.Get("Font"));
        if (fonts is null)
        {
            fonts = new PdfDictionary();
            resources.Set("Font", fonts);
        }
        return fonts;
    }

    /// <summary>Family name usable for a font lookup, derived from a /BaseFont by
    /// stripping a 6-char subset tag ("ABCDEF+Name").</summary>
    private static string? SourceFontFamily(PdfDictionary? fontDict)
    {
        var bf = fontDict?.GetName("BaseFont");
        if (string.IsNullOrEmpty(bf)) return null;
        var plus = bf!.IndexOf('+');
        if (plus == 6) bf = bf.Substring(plus + 1);
        return bf;
    }

    private static bool ContainsCjk(string text)
    {
        foreach (var ch in text)
            if (ch >= '　' && ch <= '鿿') return true;
        return false;
    }

    /// <summary>
    /// Build a reverse map from Unicode characters to CID codes, including NFKD-decomposed
    /// variants so base Arabic characters (e.g., U+0627 Alef) can map to presentation form
    /// codes (e.g., U+FE8E → code N) that exist in the font's ToUnicode CMap.
    /// </summary>
    /// <remarks>
    /// Two-pass approach: first adds single-character NFKD decompositions (e.g., U+FEF3 → U+064A),
    /// then multi-character ones (e.g., U+FE8B → U+064A + U+0654). This ensures that plain
    /// presentation forms (like Yeh U+FEF1-FEF4) are preferred over compound forms
    /// (like Yeh-with-Hamza U+FE89-FE8C) when both decompose to the same base character.
    /// </remarks>
    private static Dictionary<string, int> BuildReverseMap(Dictionary<int, string> toUnicode)
    {
        var reverseMap = new Dictionary<string, int>();

        // Pass 0: direct Unicode string → code (no decomposition)
        foreach (var (code, unicode) in toUnicode)
            reverseMap.TryAdd(unicode, code);

        // Pass 1: single-char NFKD decompositions (plain presentation forms, e.g. U+FEF3 → U+064A)
        foreach (var (code, unicode) in toUnicode)
        {
            if (unicode.Length != 1) continue;
            var ch = unicode[0];
            if ((ch < '\uFB50' || ch > '\uFDFF') && (ch < '\uFE70' || ch > '\uFEFF')) continue;

            var decomposed = unicode.Normalize(System.Text.NormalizationForm.FormKD);
            if (decomposed.Length == 1)
                reverseMap.TryAdd(decomposed, code);
        }

        // Pass 2: multi-char NFKD decompositions (compound forms, e.g. U+FE8B → U+064A + U+0654)
        // Only adds base characters that weren't already mapped in pass 1.
        foreach (var (code, unicode) in toUnicode)
        {
            if (unicode.Length != 1) continue;
            var ch = unicode[0];
            if ((ch < '\uFB50' || ch > '\uFDFF') && (ch < '\uFE70' || ch > '\uFEFF')) continue;

            var decomposed = unicode.Normalize(System.Text.NormalizationForm.FormKD);
            if (decomposed.Length > 1)
            {
                foreach (var dc in decomposed)
                    reverseMap.TryAdd(dc.ToString(), code);
            }
        }

        return reverseMap;
    }

    private static bool NeedsFontSwitch(string text, Dictionary<int, string>? toUnicode,
        PdfDictionary? fontDict, PdfReader? reader = null, bool allowGlyphFallback = false)
    {
        var isCid = fontDict?.GetName("Subtype") == "Type0";

        // CID/Type0 fonts use 2-byte character codes.  If there is no ToUnicode
        // map we cannot build a reverse map, so we must switch to a standard font
        // for any replacement text.
        if (isCid && toUnicode is null)
            return true;

        // A simple (non-CID) font with NO ToUnicode is single-byte WinAnsi/Latin1:
        // it physically cannot encode a character outside Latin-1 (> 0xFF), so a
        // Cyrillic/Hebrew/CJK replacement must switch fonts (→ CID fallback in
        // WriteFontSwitchedReplacement). Without this, the reverse-map check below
        // is skipped and EncodeString silently Latin1-encodes the char to '?'.
        if (!isCid && toUnicode is null && text.Any(ch => ch > 0xFF))
            return true;

        if (toUnicode is not null)
        {
            var reverseMap = BuildReverseMap(toUnicode);

            if (text.Any(ch => !reverseMap.ContainsKey(ch.ToString())))
                return true;
        }

        // Base-encoded simple subset that lacks an embedded glyph for a replacement char
        // (a Type1/TrueType subset embeds only the glyphs it draws; the /Widths entry is 0
        // for the rest). Without a switch the missing glyphs render blank. Fires only for a
        // plain base encoding, so /Differences fonts fall through to the remap check below.
        // Gated to the facade ReplaceText path (allowGlyphFallback) — the TextFragment.Text
        // setter manages the font itself, so an auto-switch there would shift following text.
        if (allowGlyphFallback && !isCid && SimpleFontMissingGlyphChars(fontDict, reader, text).Length > 0)
            return true;

        // Non-CID fonts with /Encoding containing /Differences: if any replacement
        // character's Latin1 byte value is remapped by the Differences array, the
        // round-trip will produce wrong glyphs — switch to a standard font.
        if (!isCid && fontDict is not null && reader is not null)
        {
            var encodingObj = fontDict.Get("Encoding");
            PdfDictionary? encodingDict = null;
            if (encodingObj is PdfDictionary ed) encodingDict = ed;
            else if (encodingObj is not null) encodingDict = reader.ResolveDict(encodingObj);

            if (encodingDict is not null)
            {
                var diffsArr = encodingDict.Get("Differences") as PdfArray;
                if (diffsArr is null)
                {
                    var resolved = reader.Resolve(encodingDict.Get("Differences"));
                    diffsArr = resolved as PdfArray;
                }
                if (diffsArr is not null)
                {
                    // Build set of byte codes that are remapped by Differences
                    var remappedCodes = new HashSet<int>();
                    var code = 0;
                    for (var i = 0; i < diffsArr.Count; i++)
                    {
                        if (diffsArr[i] is PdfInteger pi)
                            code = (int)pi.Value;
                        else if (diffsArr[i] is PdfName)
                            remappedCodes.Add(code++);
                    }

                    // Check if any replacement character's Latin1 byte is remapped
                    foreach (var ch in text)
                    {
                        var b = (int)(ch <= 0xFF ? ch : 0x3F);
                        if (remappedCodes.Contains(b))
                            return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Characters in <paramref name="text"/> for which a base-encoded simple (non-CID)
    /// subset font has NO embedded glyph. A subset embeds only the glyphs it draws and
    /// zeroes the /Widths entry (or omits the code from /FirstChar../LastChar) for the
    /// rest — so a width of 0 / an out-of-range code marks an absent glyph. Only applied
    /// to a plain base encoding (WinAnsi/Standard/MacRoman name, no /Differences); a
    /// /Differences font is left to the remap check in <see cref="NeedsFontSwitch"/> so
    /// this never over-fires. Returns empty when coverage can't be judged (no /Widths,
    /// Type0, unknown encoding) — never guess a switch. Space is ignored (word gap, not
    /// a drawn glyph).
    /// </summary>
    /// <summary>
    /// True when the font is an EMBEDDED (FontFile/2/3) simple (non-Type0) font — a subset
    /// that embeds only the glyphs it draws. When such a font can't faithfully show a
    /// replacement char, the default no-character behaviour substitutes and REPORTS a
    /// fallback face. Used only to gate that report (not the rendering), so it deliberately
    /// covers /Differences subsets too; a non-embedded system-font reference is excluded
    /// (its real installed face has the glyph, so no substitution is reported).
    /// </summary>
    private static bool IsEmbeddedSimpleFont(PdfDictionary? fontDict, PdfReader? reader)
    {
        if (fontDict is null || reader is null) return false;
        if (fontDict.GetName("Subtype") == "Type0") return false;
        var descriptor = reader.ResolveDict(fontDict.Get("FontDescriptor"));
        return descriptor is not null &&
            (descriptor.Get("FontFile") is not null || descriptor.Get("FontFile2") is not null
             || descriptor.Get("FontFile3") is not null);
    }

    /// <summary>The family the fragment should REPORT after a default no-character
    /// substitution: the source font's own family when it's installed (kept, like an
    /// Arial subset → Arial), else Times New Roman (source not available to expand).</summary>
    private static string ResolveReportedFallbackFamily(PdfDictionary? fontDict)
    {
        var src = SourceFontFamily(fontDict);
        if (!string.IsNullOrEmpty(src))
        {
            try { var t = FontRepository.GetTtfData(src!); if (t is { Length: > 12 }) return src!; }
            catch { /* not installed → fall through to Times */ }
        }
        return "TimesNewRoman";
    }

    /// <summary>The glyph names a Type 1 subset states it actually carries, from the
    /// descriptor's /CharSet, or null when the font does not declare one. A subsetter
    /// routinely keeps the ORIGINAL full /Widths table while embedding only the glyphs
    /// the page drew, so a non-zero width proves nothing; /CharSet is the explicit
    /// contents list and is believed ahead of the widths when present.</summary>
    private static HashSet<string>? CharSetGlyphNames(PdfDictionary? descriptor, PdfReader? reader)
    {
        if (descriptor is null || reader is null) return null;
        if (reader.Resolve(descriptor.Get("CharSet")) is not PdfString cs) return null;
        var text = cs.ToText();
        if (string.IsNullOrEmpty(text)) return null;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in text.Split('/'))
            if (name.Length > 0) names.Add(name.Trim());
        return names.Count > 0 ? names : null;
    }

    private static char[] SimpleFontMissingGlyphChars(PdfDictionary? fontDict, PdfReader? reader, string text)
    {
        if (fontDict is null || reader is null) return Array.Empty<char>();
        if (fontDict.GetName("Subtype") == "Type0") return Array.Empty<char>();

        // Only an EMBEDDED font's /Widths tell the truth about glyph presence: a subset
        // embeds only the glyphs it draws (0-width for the rest). A NON-embedded font
        // (a system-font reference like "Arial,Bold") often ships /Widths only for the
        // codes it happens to use, but the real installed face still has every glyph — a
        // 0 width there is missing metadata, not a missing glyph. So gate on an embedded
        // FontFile/FontFile2/FontFile3; otherwise never treat a 0 width as absent.
        var descriptor = reader.ResolveDict(fontDict.Get("FontDescriptor"));
        bool embedded = descriptor is not null &&
            (descriptor.Get("FontFile") is not null || descriptor.Get("FontFile2") is not null
             || descriptor.Get("FontFile3") is not null);
        if (!embedded) return Array.Empty<char>();

        // Only a base encoding has a code==WinAnsi-byte mapping we can trust here. A
        // /Differences array re-points individual codes (a ligature at code 31, say): those
        // codes are judged by the Differences check in NeedsFontSwitch, every other code still
        // maps through the base encoding and is judged by its width below.
        var enc = fontDict.Get("Encoding");
        var encDict = enc as PdfName is null ? reader.ResolveDict(enc) : null;
        string? encName = enc as PdfName is { } pn ? pn.Value : encDict?.GetName("BaseEncoding");
        var remapped = new HashSet<int>();
        var diffNames = new HashSet<string>(StringComparer.Ordinal);
        if (encDict is not null && reader.Resolve(encDict.Get("Differences")) is PdfArray diffs)
        {
            int next = 0;
            foreach (var item in diffs)
            {
                var d = reader.Resolve(item);
                if (d is PdfInteger di) next = (int)di.Value;
                else if (d is PdfReal dr) next = (int)dr.Value;
                else if (d is PdfName dn) { remapped.Add(next++); diffNames.Add(dn.Value); }
            }
        }
        // With no /BaseEncoding, the /Differences array IS the font's whole encoding: a
        // glyph it does not name has no code the replacement could be written as, so the
        // name list is the font's entire repertoire. Judging such a font by the base
        // encoding is meaningless — and skipping its codes as "remapped" (below) would
        // clear every character it has, which is why these fonts never reported a gap.
        bool differencesOnly = encName is null && diffNames.Count > 0;
        if (!differencesOnly
            && encName is not ("WinAnsiEncoding" or "StandardEncoding" or "MacRomanEncoding"))
            return Array.Empty<char>();

        var charSet = CharSetGlyphNames(descriptor, reader);
        var widths = reader.Resolve(fontDict.Get("Widths")) as PdfArray;
        if (widths is null && charSet is null) return Array.Empty<char>();
        var fc = reader.Resolve(fontDict.Get("FirstChar")) as PdfInteger;
        if (fc is null && charSet is null) return Array.Empty<char>();
        int firstChar = fc is null ? 0 : (int)fc.Value;
        int lastChar = widths is null ? -1 : firstChar + widths.Count - 1;

        var missing = new List<char>();
        foreach (var ch in text.Distinct())
        {
            if (ch == ' ') continue;
            // Map char → single-byte code. ASCII (0x20-0x7E) is identity under WinAnsi/
            // Standard/MacRoman; Latin-1 (0xA0-0xFF) ≈ WinAnsi. Anything else can't be a
            // single-byte code here, so treat as absent-from-this-font.
            if (ch >= 0x100) { missing.Add(ch); continue; }
            int code = ch;
            var stdName = encName switch
            {
                "MacRomanEncoding" => PdfEncodings.MacRomanName(code),
                "StandardEncoding" => Type1StandardEncoding.GetName(code),
                _ => PdfEncodings.WinAnsiName(code),
            };
            if (differencesOnly)
            {
                if (stdName is null || !diffNames.Contains(stdName)
                    || (charSet is not null && !charSet.Contains(stdName)))
                    missing.Add(ch);
                continue;
            }
            if (remapped.Contains(code)) continue;
            // /CharSet, when declared, is the subset's own statement of what it embeds:
            // a base-encoded code whose glyph name is absent from it has no outline to
            // draw, however plausible the width table looks.
            if (charSet is not null)
            {
                if (stdName is null || !charSet.Contains(stdName)) missing.Add(ch);
                continue;
            }
            // No /CharSet to go on: the widths are the only evidence left, and a font
            // carrying neither was rejected above.
            if (widths is null || code < firstChar || code > lastChar) { missing.Add(ch); continue; }
            var w = reader.Resolve(widths[code - firstChar]);
            double width = w is PdfInteger wi ? wi.Value : w is PdfReal wr ? wr.Value : 0;
            if (width == 0) missing.Add(ch);
        }
        return missing.ToArray();
    }

    /// <summary>The weight/slope the stand-in must copy from the run it replaces: a bold
    /// source is re-dressed in the bold face, an italic one in the italic face. The face
    /// NAME carries this most reliably for a subset ("TheSansBold-Plain"); the descriptor's
    /// /FontWeight, /ItalicAngle and /Flags answer for the faces that do not say it.</summary>
    private static string TimesStyleSuffix(PdfDictionary? fontDict, PdfReader? reader)
    {
        var name = SourceFontFamily(fontDict) ?? "";
        bool bold = name.Contains("Bold", StringComparison.OrdinalIgnoreCase);
        bool italic = name.Contains("Italic", StringComparison.OrdinalIgnoreCase)
                   || name.Contains("Oblique", StringComparison.OrdinalIgnoreCase);
        var descriptor = reader?.ResolveDict(fontDict?.Get("FontDescriptor"));
        if (descriptor is not null)
        {
            if (reader!.Resolve(descriptor.Get("FontWeight")) is PdfInteger fw && fw.Value >= BoldFontWeight)
                bold = true;
            if (reader.Resolve(descriptor.Get("Flags")) is PdfInteger fl)
            {
                if ((fl.Value & ItalicFlagBit) != 0) italic = true;
                if ((fl.Value & ForceBoldFlagBit) != 0) bold = true;
            }
            var ia = reader.Resolve(descriptor.Get("ItalicAngle"));
            var angle = ia is PdfInteger ii ? ii.Value : ia is PdfReal ir ? ir.Value : 0;
            if (angle != 0) italic = true;
        }
        return bold ? (italic ? "BoldItalic" : "Bold") : (italic ? "Italic" : "");
    }

    /// <summary>/FontWeight at or above which a face counts as bold (PDF 32000 Table 122
    /// gives 400 as regular and 700 as bold; 600 is the semibold boundary between them).</summary>
    private const int BoldFontWeight = 600;

    /// <summary>/Flags bit 7 (value 64) — the descriptor's Italic flag (PDF 32000 Table 121).</summary>
    private const int ItalicFlagBit = 1 << 6;

    /// <summary>/Flags bit 19 (value 262144) — ForceBold (PDF 32000 Table 121).</summary>
    private const int ForceBoldFlagBit = 1 << 18;
}

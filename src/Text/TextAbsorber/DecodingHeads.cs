using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
    /// <summary>A Symbol or ZapfDingbats base font decodes through its built-in encoding table; null for any other face.</summary>
    private static string? DecodeSymbolFontString(byte[] bytes, PdfDictionary? fontDict)
    {
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
        return null;
    }

    /// <summary>A Symbol face embedded as a CID font maps its codes through Windows' symbol
    /// private-use block (U+F000 + the Symbol code) in its /ToUnicode, and that is what the
    /// map hands back; the text is the Symbol character behind the code (U+F06D is the micro
    /// sign, not a private-use mark). Any other face keeps its text as decoded.</summary>
    private static string MapSymbolPrivateUse(string text, PdfDictionary? fontDict)
    {
        var baseFont = fontDict?.GetName("BaseFont");
        if (baseFont is null) return text;
        var cleanName = baseFont.Contains('+') ? baseFont.Substring(baseFont.IndexOf('+') + 1) : baseFont;
        if (cleanName != "Symbol") return text;
        StringBuilder? sb = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c >= 0xF000 && c <= 0xF0FF && SymbolEncoding.TryGetValue((byte)(c & 0xFF), out var mapped))
            {
                sb ??= new StringBuilder(text, 0, i, text.Length);
                sb.Append(mapped);
            }
            else sb?.Append(c);
        }
        return sb?.ToString() ?? text;
    }

    /// <summary>A simple font with no Encoding entry: the embedded program's post names, the glyph-shape decoder, or a standard Latin Type 1's high codes through its standard encoding; null when none applies.</summary>
    private static string? DecodeWithoutEncodingEntry(byte[] bytes, PdfObject? encodingObj, PdfDictionary? fontDict, PdfReader reader)
    {
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
            // sequential-by-first-use subsets start at 0x01).
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
        return null;
    }

    /// <summary>A Type0 font: Identity and UCS2/UTF16 CMaps decode as CIDs through the ordering or the glyph map, a legacy CJK CMap through its byte-length and code tables; null for other encodings.</summary>
    private static string? DecodeType0String(byte[] bytes, Dictionary<int, string>? toUnicode, PdfDictionary? fontDict, PdfReader reader, bool useFontEngineEncoding)
    {
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
        return null;
    }
}

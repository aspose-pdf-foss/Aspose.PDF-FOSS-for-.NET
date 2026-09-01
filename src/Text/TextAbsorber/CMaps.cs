using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
    private void EnsureFontSet(bool fontSet, string op)
    {
        if (fontSet) return;
        if (TextSearchOptions?.IgnoreResourceFontErrors ?? false) return;
        throw new IncorrectFontUsageException(
            $"Document error: {op} operator without preceding Tf - no font set for the text segment");
    }

    /// <summary>
    /// Resolve XObject resources by walking up the page tree hierarchy.
    /// Returns the first XObject dict found (page-level takes priority over parent).
    /// </summary>
    internal static PdfDictionary? ResolveXObjects(PdfDictionary dict, PdfReader reader)
    {
        var current = dict;
        int depth = 0;
        while (current is not null && depth < 6)
        {
            var resources = reader.ResolveDict(current.Get("Resources"));
            if (resources is not null)
            {
                var xobjs = reader.ResolveDict(resources.Get("XObject"));
                if (xobjs is not null) return xobjs;
            }
            current = reader.ResolveDict(current.Get("Parent"));
            depth++;
        }
        return null;
    }

    internal static Dictionary<string, PdfDictionary> ResolveFonts(PdfDictionary pageDict, PdfReader reader)
    {
        var result = new Dictionary<string, PdfDictionary>(StringComparer.Ordinal);
        CollectFontsFromHierarchy(pageDict, reader, result, depth: 0);
        return result;
    }

    /// <summary>
    /// Collect fonts by walking up the page tree, allowing parent Resources to
    /// provide fonts not defined in the page's own Resources dict.
    /// Page-level fonts override parent fonts of the same name.
    /// </summary>
    private static void CollectFontsFromHierarchy(PdfDictionary dict, PdfReader reader,
        Dictionary<string, PdfDictionary> result, int depth)
    {
        if (depth > 6) return; // guard against infinite loops

        // Walk parent first (lower priority), then overlay with this node's fonts
        var parentRef = dict.Get("Parent");
        if (parentRef is not null)
        {
            var parentDict = reader.ResolveDict(parentRef);
            if (parentDict is not null)
                CollectFontsFromHierarchy(parentDict, reader, result, depth + 1);
        }

        var resources = reader.ResolveDict(dict.Get("Resources"));
        if (resources is null) return;

        var fontDict = reader.ResolveDict(resources.Get("Font"));
        if (fontDict is null) return;

        foreach (var key in fontDict.Keys)
        {
            var font = reader.ResolveDict(fontDict.Get(key));
            if (font is not null)
                result[key] = font; // page-level overrides parent
        }
    }

    /// <summary>Whether the /Resources/Font hierarchy CONTAINS an entry under
    /// <paramref name="key"/>, resolvable or not. A key that is present but whose
    /// target cannot be resolved in-memory (a just-registered replacement font) is
    /// NOT "absent from page Resources" — callers treat that differently from a
    /// genuinely missing key.</summary>
    internal static bool FontResourceKeyExists(PdfDictionary dict, PdfReader reader, string key, int depth = 0)
    {
        if (depth > 6) return false;
        var parentRef = dict.Get("Parent");
        if (parentRef is not null && reader.ResolveDict(parentRef) is { } parentDict
            && FontResourceKeyExists(parentDict, reader, key, depth + 1))
            return true;
        var resources = reader.ResolveDict(dict.Get("Resources"));
        var fontDict = resources is null ? null : reader.ResolveDict(resources.Get("Font"));
        return fontDict?.Get(key) is not null;
    }

    internal static Dictionary<int, string>? ParseToUnicodeFromDict(PdfDictionary fontDict, PdfReader reader) =>
        ParseToUnicode(fontDict, reader);

    /// <summary>For diagnostics only: expose ParseCMap publicly.</summary>
    internal static Dictionary<int, string> ParseCMapPublic(string cmapText) => ParseCMap(cmapText);

    private static Dictionary<int, string>? ParseToUnicode(PdfDictionary fontDict, PdfReader reader)
    {
        var toUnicodeObj = fontDict.Get("ToUnicode");
        if (toUnicodeObj is null) return null;

        var stream = reader.ResolveStream(toUnicodeObj);
        if (stream is null) return null;

        if (_toUnicodeCache.TryGetValue(stream, out var cached)) return cached;

        var decoded = reader.DecodeStream(stream);
        var text = Encoding.ASCII.GetString(decoded);

        var map = ParseCMap(text);
        _toUnicodeCache.AddOrUpdate(stream, map);
        return map;
    }

    internal static Dictionary<int, string> ParseCMap(string cmapText)
    {
        var map = new Dictionary<int, string>();
        // Normalize: ensure section markers are on their own lines.
        // This handles CMaps where all content is on a single line (space-separated).
        cmapText = Regex.Replace(cmapText,
            @"(begin|end)(bfchar|bfrange)",
            "\n$1$2\n",
            RegexOptions.IgnoreCase);
        var lines = cmapText.Split('\n');

        var inBfChar = false;
        var inBfRange = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (line.Contains("beginbfchar", StringComparison.Ordinal))
            {
                inBfChar = true;
                continue;
            }
            if (line.Contains("endbfchar", StringComparison.Ordinal))
            {
                inBfChar = false;
                continue;
            }
            if (line.Contains("beginbfrange", StringComparison.Ordinal))
            {
                inBfRange = true;
                continue;
            }
            if (line.Contains("endbfrange", StringComparison.Ordinal))
            {
                inBfRange = false;
                continue;
            }

            if (inBfChar)
            {
                // A line may contain multiple pairs: <code> <unicode> <code> <unicode> .
                var tokens = ExtractHexTokens(line);
                for (var k = 0; k + 1 < tokens.Count; k += 2)
                {
                    var code = ParseHexInt(tokens[k]);
                    var unicode = HexToString(tokens[k + 1]);
                    map[code] = unicode;
                }
            }
            else if (inBfRange)
            {
                var tokens = ExtractHexTokens(line);
                if (tokens.Count >= 3)
                {
                    // Check if line contains array form: <start> <end> [<d0> <d1> .]
                    var arrayStart = line.IndexOf('[');
                    if (arrayStart >= 0)
                    {
                        var start = ParseHexInt(tokens[0]);
                        var end = ParseHexInt(tokens[1]);
                        // Array form: each code maps to successive array entries
                        var arrayTokens = tokens.Skip(2).ToList(); // tokens from inside array
                        for (var code = start; code <= end; code++)
                        {
                            var idx = code - start;
                            if (idx < arrayTokens.Count)
                                map[code] = HexToString(arrayTokens[idx]);
                        }
                    }
                    else
                    {
                        // Sequential form: start code maps to startUnicode, next codes
                        // increment. A one-line CMap packs EVERY range onto this line
                        // (<00> <00> <fffd><01> <01> <00ad>…), so consume triples, not
                        // just the first. The destination is UTF-16BE and may be a
                        // surrogate pair (plane-1 math alphanumerics, emoji):
                        // <16> <49> <D835DC34>. Decode it to codepoints first — parsing
                        // 8 hex digits as one integer lands above 0x10FFFF and would
                        // drop the whole range.
                        for (var k = 0; k + 2 < tokens.Count; k += 3)
                        {
                            var start = ParseHexInt(tokens[k]);
                            var end = ParseHexInt(tokens[k + 1]);
                            var destStr = HexToString(tokens[k + 2]);
                            if (destStr.Length == 0) continue;
                            // The LAST codepoint of the destination carries the increment;
                            // any preceding codepoints (multi-char ligature dest) are a
                            // constant prefix.
                            var lastCpStart = destStr.Length >= 2 && char.IsSurrogatePair(destStr[^2], destStr[^1])
                                ? destStr.Length - 2 : destStr.Length - 1;
                            // A malformed CMap can leave an UNPAIRED surrogate here (a
                            // 4-digit dest like <D835> survives via HexToString's raw
                            // fallback); ConvertToUtf32 would throw — drop the range like
                            // the pre-surrogate parser did.
                            if (char.IsSurrogate(destStr[lastCpStart])
                                && !(destStr.Length - lastCpStart == 2
                                     && char.IsSurrogatePair(destStr[lastCpStart], destStr[lastCpStart + 1])))
                                continue;
                            var prefix = destStr[..lastCpStart];
                            var lastCp = char.ConvertToUtf32(destStr, lastCpStart);
                            for (var code = start; code <= end; code++)
                            {
                                var cp = lastCp + (code - start);
                                if (cp is >= 0xD800 and <= 0xDFFF || cp > 0x10FFFF)
                                    continue; // skip invalid surrogate codepoints
                                map[code] = prefix.Length == 0 ? char.ConvertFromUtf32(cp) : prefix + char.ConvertFromUtf32(cp);
                            }
                        }
                    }
                }
            }
        }

        FoldLamAlefLigatures(map);
        return map;
    }

    private static List<string> ExtractHexTokens(string line)
    {
        var tokens = new List<string>();
        var i = 0;
        while (i < line.Length)
        {
            if (line[i] == '<')
            {
                var end = line.IndexOf('>', i);
                if (end > i)
                {
                    tokens.Add(line[(i + 1)..end].Replace(" ", ""));
                    i = end + 1;
                    continue;
                }
            }
            i++;
        }
        return tokens;
    }

    private static int ParseHexInt(string hex)
    {
        if (long.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var val))
            return val > int.MaxValue ? 0 : (int)val;
        return 0;
    }

    private static string HexToString(string hex)
    {
        var sb = new StringBuilder();
        for (var i = 0; i + 3 < hex.Length; i += 4)
        {
            var codePoint = ParseHexInt(hex[i..(i + 4)]);
            // UTF-16BE surrogate pair (emoji / CJK Ext-B): combine with the next unit.
            if (codePoint is >= 0xD800 and <= 0xDBFF && i + 7 < hex.Length)
            {
                var low = ParseHexInt(hex[(i + 4)..(i + 8)]);
                if (low is >= 0xDC00 and <= 0xDFFF)
                {
                    sb.Append(char.ConvertFromUtf32(char.ConvertToUtf32((char)codePoint, (char)low)));
                    i += 4;
                    continue;
                }
            }
            if (codePoint is >= 0xD800 and <= 0xDFFF || codePoint > 0x10FFFF)
                continue; // skip unpaired surrogate units
            sb.Append(char.ConvertFromUtf32(codePoint));
        }
        if (sb.Length == 0 && hex.Length >= 2)
        {
            // 2-digit hex = single byte
            sb.Append((char)ParseHexInt(hex));
        }
        return CollapseTwoCharLigature(sb.ToString());
    }

    private static PdfDictionary ParseContentDict(PdfLexer lexer)
    {
        var dict = new PdfDictionary();
        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.DictEnd || t.Kind == TokenKind.Eof) break;
            if (t.Kind != TokenKind.Name) continue;
            var key = t.StringValue!;
            var val = lexer.NextToken();
            if (val.Kind == TokenKind.DictEnd) break;
            PdfObject value = val.Kind switch
            {
                TokenKind.Integer => new PdfInteger(val.IntValue),
                TokenKind.Real => new PdfReal(val.RealValue),
                TokenKind.Name => new PdfName(val.StringValue!),
                TokenKind.LiteralString => new PdfString(val.BytesValue!),
                TokenKind.HexString => new PdfString(val.BytesValue!, isHex: true),
                TokenKind.Boolean => val.BoolValue ? PdfBoolean.True : PdfBoolean.False,
                _ => PdfNull.Instance,
            };
            dict.Set(key, value);
        }
        return dict;
    }

    private static PdfArray ParseContentArray(PdfLexer lexer)
    {
        var array = new PdfArray();
        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.ArrayEnd || t.Kind == TokenKind.Eof) break;
            switch (t.Kind)
            {
                case TokenKind.Integer:
                    array.Add(new PdfInteger(t.IntValue));
                    break;
                case TokenKind.Real:
                    array.Add(new PdfReal(t.RealValue));
                    break;
                case TokenKind.LiteralString:
                    array.Add(new PdfString(t.BytesValue!));
                    break;
                case TokenKind.HexString:
                    array.Add(new PdfString(t.BytesValue!, isHex: true));
                    break;
                case TokenKind.Name:
                    array.Add(new PdfName(t.StringValue!));
                    break;
            }
        }
        return array;
    }

    private static List<byte[]> GetContentStreams(Page page, PdfReader reader)
    {
        var result = new List<byte[]>();
        var contentsObj = reader.Resolve(page.Dict.Get("Contents"));

        if (contentsObj is PdfStream stream)
        {
            result.Add(reader.DecodeStream(stream));
        }
        else if (contentsObj is PdfArray arr)
        {
            foreach (var item in arr)
            {
                var s = reader.ResolveStream(item);
                if (s is not null)
                    result.Add(reader.DecodeStream(s));
            }
        }

        return result;
    }

    /// <summary>
    /// Skip inline image data (BI . ID &lt;data&gt; EI) per PDF spec §8.9.7.
    /// </summary>
    internal static void SkipInlineImage(PdfLexer lexer)
    {
        // Consume tokens until the ID keyword (image data start), capturing the
        // dictionary keys needed to size the data.
        int imgW = 0, imgH = 0, imgBpc = 8, imgColors = 1; bool imgFlate = false;
        string? key = null, firstFilter = null;
        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.Eof) return;
            if (t.Kind == TokenKind.Keyword && t.StringValue == "ID") break;
            if (t.Kind == TokenKind.Name)
            {
                var n = t.StringValue!;
                if (key is "F" or "Filter" && firstFilter is null) firstFilter = n;
                switch (n)
                {
                    case "RGB": case "DeviceRGB": if (key is "CS" or "ColorSpace") imgColors = 3; break;
                    case "CMYK": case "DeviceCMYK": if (key is "CS" or "ColorSpace") imgColors = 4; break;
                    case "G": case "DeviceGray": if (key is "CS" or "ColorSpace") imgColors = 1; break;
                    case "Fl": case "FlateDecode": if (key is "F" or "Filter") imgFlate = true; break;
                }
                key = n;
            }
            else if (t.Kind == TokenKind.Integer)
            {
                int v = (int)t.IntValue;
                switch (key) { case "W": case "Width": imgW = v; break; case "H": case "Height": imgH = v; break;
                    case "BPC": case "BitsPerComponent": imgBpc = v; break; case "Colors": imgColors = v; break; }
                key = null;
            }
            // A filter array (/F [/A85 /Fl]) keeps the key alive so its first element
            // is still attributed to F/Filter.
            else if (t.Kind != TokenKind.ArrayStart) key = null;
        }

        long dataStart0 = lexer.Position + 1; // one whitespace byte after ID
        long lenAll = lexer.Length;

        // ASCII85/ASCIIHex data self-terminates with an explicit EOD marker ("~>" / ">").
        // Locate it directly: such data is printable text where 'E','I' are ordinary
        // digits and line breaks supply whitespace, so the "EI" byte scan below finds
        // false terminators inside the payload and desyncs the lexer into image bytes.
        if (firstFilter is "A85" or "ASCII85Decode" or "AHx" or "ASCIIHexDecode")
        {
            bool a85 = firstFilter is "A85" or "ASCII85Decode";
            byte eod = a85 ? (byte)'~' : (byte)'>';
            for (long p = dataStart0; p < lenAll; p++)
            {
                if (lexer.ByteAt(p) != eod) continue;
                if (a85 && (p + 1 >= lenAll || lexer.ByteAt(p + 1) != (byte)'>')) continue;
                long q = p + (a85 ? 2 : 1);
                while (q < lenAll && IsWhitespace(lexer.ByteAt(q))) q++;
                if (q + 1 < lenAll && lexer.ByteAt(q) == (byte)'E' && lexer.ByteAt(q + 1) == (byte)'I')
                    q += 2;
                lexer.Position = q;
                return;
            }
        }

        // Preferred for Flate-compressed data: probe each whitespace-delimited "EI"
        // candidate by inflating ID..candidate; the real EI is the earliest position
        // whose data inflates to the full raw image size. A stray "EI" byte pair inside
        // the compressed stream truncates the deflate stream → inflate fails, so it's
        // skipped. This stops the lexer desyncing and dropping every operator after the
        // image (nested-table grid lines were all lost after an inline image).
        if (imgFlate && imgW > 0 && imgH > 0)
        {
            int bytesPerRow = (imgW * imgColors * imgBpc + 7) / 8;
            int expected = imgH * bytesPerRow; // lower bound (a row predictor only adds bytes)
            int tailLen = (int)Math.Max(0, lenAll - dataStart0);
            var tail = new byte[tailLen];
            for (int i = 0; i < tailLen; i++) tail[i] = lexer.ByteAt(dataStart0 + i);
            for (int p = 1; p < tailLen - 1; p++)
            {
                if (tail[p] != (byte)'E' || tail[p + 1] != (byte)'I') continue;
                if (p + 2 < tailLen && !IsWhitespace(tail[p + 2])) continue;
                var slice = new byte[p];
                Array.Copy(tail, 0, slice, 0, p);
                try
                {
                    var inflated = Aspose.Pdf.IO.Filters.FlateDecodeFilter.Decode(slice, null);
                    if (inflated.Length >= expected) { lexer.Position = dataStart0 + p + 2; return; }
                }
                catch { /* truncated deflate at this candidate — keep scanning */ }
            }
        }

        // After ID, spec mandates one whitespace byte before raw data.
        // Scan raw bytes for 'E' 'I' followed by whitespace/EOF.
        // Many real-world PDFs don't have whitespace BEFORE "EI" (the image data
        // ends immediately before the E), so we check both patterns:
        //   1. Standard: whitespace + EI + whitespace (spec-compliant)
        //   2. Relaxed: any-byte + EI + whitespace (common in practice)
        var pos = lexer.Position + 1; // skip the whitespace byte after ID
        var len = lexer.Length;

        while (pos < len - 1)
        {
            if (lexer.ByteAt(pos) == (byte)'E' && lexer.ByteAt(pos + 1) == (byte)'I')
            {
                var after = pos + 2;
                if (after >= len || IsWhitespace(lexer.ByteAt(after)))
                {
                    // Verify this is the real EI by checking that what follows
                    // looks like valid PDF operators (not random image data).
                    // A valid operator context after EI would be: Q, BT, numbers, /, etc.
                    if (after < len)
                    {
                        // Skip whitespace after EI
                        var checkPos = after;
                        while (checkPos < len && IsWhitespace(lexer.ByteAt(checkPos)))
                            checkPos++;
                        if (checkPos < len)
                        {
                            var nextByte = lexer.ByteAt(checkPos);
                            // Valid PDF operator starts: letter, number, /, (, <, [
                            bool looksValid = (nextByte >= (byte)'A' && nextByte <= (byte)'Z')
                                || (nextByte >= (byte)'a' && nextByte <= (byte)'z')
                                || (nextByte >= (byte)'0' && nextByte <= (byte)'9')
                                || nextByte == (byte)'/' || nextByte == (byte)'('
                                || nextByte == (byte)'<' || nextByte == (byte)'['
                                || nextByte == (byte)'-' || nextByte == (byte)'.';
                            if (!looksValid) { pos++; continue; }
                        }
                    }
                    lexer.Position = after;
                    return;
                }
            }
            pos++;
        }
        lexer.Position = len; // consume everything if EI not found

        static bool IsWhitespace(byte b) =>
            b == 0x00 || b == 0x09 || b == 0x0A || b == 0x0C || b == 0x0D || b == 0x20;
    }

    private static double GetNumber(PdfObject obj) => obj switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0,
    };
}

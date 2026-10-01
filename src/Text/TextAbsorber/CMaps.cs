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
        var cm = new CMapParseState();
        cm.cmapText = cmapText;
        cm.map = new Dictionary<int, string>();
        // Normalize: ensure section markers are on their own lines.
        // This handles CMaps where all content is on a single line (space-separated).
        cm.cmapText = Regex.Replace(cm.cmapText,
            @"(begin|end)(bfchar|bfrange)",
            "\n$1$2\n",
            RegexOptions.IgnoreCase);
        cm.lines = cm.cmapText.Split('\n');

        cm.inBfChar = false;
        cm.inBfRange = false;

        foreach (var rawLine in cm.lines)
        {
            if (!ParseCMapLine(cm, rawLine)) break;
        }

        FoldLamAlefLigatures(cm.map);
        return cm.map;
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
        var im = new InlineImageSkipState();
        im.lexer = lexer;
        im.imgW = 0;
        im.imgH = 0;
        im.imgBpc = 8;
        im.imgColors = 1; im.imgFlate = false;
        im.key = null;
        im.firstFilter = null;
        if (!ScanInlineImageDictionary(im)) return;

        im.dataStart0 = im.lexer.Position + 1; // one whitespace byte after ID
        im.lenAll = im.lexer.Length;

        // ASCII85/ASCIIHex data self-terminates with an explicit EOD marker ("~>" / ">").
        // Locate it directly: such data is printable text where 'E','I' are ordinary
        // digits and line breaks supply whitespace, so the "EI" byte scan below finds
        // false terminators inside the payload and desyncs the lexer into image bytes.
        if (im.firstFilter is "A85" or "ASCII85Decode" or "AHx" or "ASCIIHexDecode")
        {
            bool a85 = im.firstFilter is "A85" or "ASCII85Decode";
            byte eod = a85 ? (byte)'~' : (byte)'>';
            for (long p = im.dataStart0; p < im.lenAll; p++)
            {
                if (im.lexer.ByteAt(p) != eod) continue;
                if (a85 && (p + 1 >= im.lenAll || im.lexer.ByteAt(p + 1) != (byte)'>')) continue;
                long q = p + (a85 ? 2 : 1);
                while (q < im.lenAll && IsInlineImageWhitespace(im.lexer.ByteAt(q))) q++;
                if (q + 1 < im.lenAll && im.lexer.ByteAt(q) == (byte)'E' && im.lexer.ByteAt(q + 1) == (byte)'I')
                    q += 2;
                im.lexer.Position = q;
                return;
            }
        }

        // Preferred for Flate-compressed data: probe each whitespace-delimited "EI"
        // candidate by inflating ID..candidate; the real EI is the earliest position
        // whose data inflates to the full raw image size. A stray "EI" byte pair inside
        // the compressed stream truncates the deflate stream → inflate fails, so it's
        // skipped. This stops the lexer desyncing and dropping every operator after the
        // image (nested-table grid lines were all lost after an inline image).
        if (!SkipFlateInlineImageData(im)) return;

        im.pos = im.lexer.Position + 1; // skip the whitespace byte after ID
        im.len = im.lexer.Length;

        if (!FindInlineImageEnd(im)) return;
        im.lexer.Position = im.len; // consume everything if EI not found
    }

    private static double GetNumber(PdfObject obj) => obj switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0,
    };
}

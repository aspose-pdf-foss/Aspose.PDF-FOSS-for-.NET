using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
    /// <summary>Parses one CMap line: codespace ranges set the byte width, bfchar and bfrange entries (single, ranged, and array-ranged) map codes to their Unicode strings.</summary>
    private static bool ParseCMapLine(CMapParseState cm, string rawLine)
    {
        cm.line = rawLine.Trim();

        if (cm.line.Contains("beginbfchar", StringComparison.Ordinal))
        {
            cm.inBfChar = true;
            return true;
        }
        if (cm.line.Contains("endbfchar", StringComparison.Ordinal))
        {
            cm.inBfChar = false;
            return true;
        }
        if (cm.line.Contains("beginbfrange", StringComparison.Ordinal))
        {
            cm.inBfRange = true;
            cm.bfRange.Clear();
            return true;
        }
        if (cm.line.Contains("endbfrange", StringComparison.Ordinal))
        {
            cm.inBfRange = false;
            ParseCMapBfRangeSection(cm, cm.bfRange.ToString());
            return true;
        }

        if (cm.inBfChar)
        {
            // A line may contain multiple pairs: <code> <unicode> <code> <unicode> .
            var tokens = ExtractHexTokens(cm.line);
            for (var k = 0; k + 1 < tokens.Count; k += 2)
            {
                var code = ParseHexInt(tokens[k]);
                var unicode = HexToString(tokens[k + 1]);
                cm.map[code] = unicode;
            }
        }
        else if (cm.inBfRange)
        {
            cm.bfRange.Append(cm.line).Append('\n');
        }
        return true;
    }

    /// <summary>A code range longer than this is no range of a font's codes (a 2-byte code space
    /// holds 65536): it is a mis-read entry, and mapping it would shadow every 2-byte code.</summary>
    private const int MaxBfRangeLength = 0x10000;

    /// <summary>The entries of a bfrange section, in order: a low and high source code with either one
    /// destination string (incremented across the range) or a bracketed array of destination strings,
    /// each mapped in turn. The section is one token stream: an array wraps over lines as its writer
    /// pleases (<c>&lt;01&gt;&lt;49&gt;[&lt;0050&gt;…</c> then rows of seven), and a one-line CMap packs
    /// every range onto one line, so neither a line nor an entry count is a unit here.</summary>
    private static void ParseCMapBfRangeSection(CMapParseState cm, string section)
    {
        var tokens = TokenizeBfRange(section);
        var k = 0;
        while (k + 2 < tokens.Count)
        {
            if (tokens[k] == "[" || tokens[k] == "]") { k++; continue; }
            var start = ParseHexInt(tokens[k]);
            var end = ParseHexInt(tokens[k + 1]);
            if (tokens[k + 2] == "[")
            {
                // Array form: each code maps to the next array entry.
                k += 3;
                for (var code = start; k < tokens.Count && tokens[k] != "]"; k++, code++)
                    if (code <= end && tokens[k] != "[") cm.map[code] = HexToString(tokens[k]);
                k++;
                continue;
            }
            MapSequentialBfRange(cm.map, start, end, HexToString(tokens[k + 2]));
            k += 3;
        }
    }

    /// <summary>The hex strings and brackets of a bfrange section, in order; everything else is dropped.</summary>
    private static List<string> TokenizeBfRange(string section)
    {
        var tokens = new List<string>();
        var i = 0;
        while (i < section.Length)
        {
            var c = section[i];
            if (c == '<')
            {
                var end = section.IndexOf('>', i);
                if (end > i)
                {
                    tokens.Add(section[(i + 1)..end].Replace(" ", ""));
                    i = end + 1;
                    continue;
                }
            }
            else if (c == '[' || c == ']') tokens.Add(c.ToString());
            i++;
        }
        return tokens;
    }

    /// <summary>Sequential form: the start code maps to the destination, the following codes to the
    /// following characters. The destination is UTF-16BE and may be a surrogate pair (plane-1 math
    /// alphanumerics, emoji): <c>&lt;16&gt; &lt;49&gt; &lt;D835DC34&gt;</c>; it is decoded to codepoints
    /// first, as 8 hex digits parsed as one integer land above 0x10FFFF and would drop the range.</summary>
    private static void MapSequentialBfRange(Dictionary<int, string> map, int start, int end, string destStr)
    {
        if (destStr.Length == 0 || end < start || end - start >= MaxBfRangeLength) return;
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
            return;
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

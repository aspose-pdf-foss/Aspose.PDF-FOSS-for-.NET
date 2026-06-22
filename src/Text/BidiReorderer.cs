namespace Aspose.Pdf.Text;

/// <summary>
/// Implements a simplified Unicode Bidi Algorithm (UAX #9) for reordering
/// visual-order RTL text to logical order during PDF text extraction.
/// </summary>
internal static class BidiReorderer
{
    /// <summary>
    /// Reorder a string from visual order to logical order if it contains RTL characters.
    /// Returns the original string unchanged if no RTL characters are present.
    /// </summary>
    public static string ReorderIfNeeded(string text)
    {
        if (string.IsNullOrEmpty(text) || !ContainsRtl(text))
            return text;

        return Reorder(text);
    }

    /// <summary>
    /// Reorder a string from visual order to logical order if it contains RTL characters,
    /// and return a permutation array where <c>perm[reorderedPos]</c> = original position.
    /// When no reordering is needed, <paramref name="perm"/> is null.
    /// </summary>
    public static string ReorderIfNeeded(string text, out int[]? perm)
    {
        perm = null;
        if (string.IsNullOrEmpty(text) || !ContainsRtl(text))
            return text;

        return ReorderWithPerm(text, out perm);
    }

    /// <summary>
    /// Check if a string contains any RTL characters (Hebrew, Arabic, etc.).
    /// </summary>
    public static bool ContainsRtl(string text)
    {
        foreach (var c in text)
        {
            if (IsRtlChar(c))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Check if a character is in an RTL Unicode range.
    /// </summary>
    internal static bool IsRtlChar(char c)
    {
        return (c >= 0x0590 && c <= 0x05FF)   // Hebrew
            || (c >= 0x0600 && c <= 0x06FF)    // Arabic
            || (c >= 0x0700 && c <= 0x074F)    // Syriac
            || (c >= 0x0750 && c <= 0x077F)    // Arabic Supplement
            || (c >= 0x0780 && c <= 0x07BF)    // Thaana
            || (c >= 0x07C0 && c <= 0x07FF)    // NKo
            || (c >= 0x0800 && c <= 0x083F)    // Samaritan
            || (c >= 0xFB1D && c <= 0xFB4F)    // Hebrew Presentation Forms
            || (c >= 0xFB50 && c <= 0xFDFF)    // Arabic Presentation Forms-A
            || (c >= 0xFE70 && c <= 0xFEFF);   // Arabic Presentation Forms-B
    }

    /// <summary>
    /// Get the bidi character type for embedding level resolution.
    /// Simplified from full UAX #9 — covers the main categories needed for text extraction.
    /// </summary>
    private static BidiType GetBidiType(char c)
    {
        // Explicit directional formatting characters
        if (c == 0x200F) return BidiType.R;  // RLM
        if (c == 0x200E) return BidiType.L;  // LRM
        if (c >= 0x202A && c <= 0x202E) return BidiType.Format; // LRE, RLE, PDF, LRO, RLO
        if (c >= 0x2066 && c <= 0x2069) return BidiType.Format; // LRI, RLI, FSI, PDI

        // Strong RTL
        if (IsRtlChar(c)) return BidiType.R;

        // European numbers
        if (c >= '0' && c <= '9') return BidiType.EN;

        // European number separators
        if (c == '+' || c == '-') return BidiType.ES;

        // European number terminators
        if (c == '#' || c == '$' || c == '%' || c == 0x00B0 || c == 0x00A2 || c == 0x00A3 || c == 0x00A5)
            return BidiType.ET;

        // Common separators
        if (c == '/' || c == ':') return BidiType.CS;

        // Whitespace / segment separator
        if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == 0x00A0)
            return BidiType.WS;

        // Other neutrals (punctuation, symbols)
        if (c < 0x0041) return BidiType.ON; // ASCII controls and punctuation before 'A'
        if (c > 0x005A && c < 0x0061) return BidiType.ON; // [\]^_`
        if (c > 0x007A && c < 0x00C0) return BidiType.ON; // {|}~ through extended Latin start

        // Mirrored punctuation
        if (c == '(' || c == ')' || c == '[' || c == ']' || c == '{' || c == '}' ||
            c == 0x00AB || c == 0x00BB || c == 0x2018 || c == 0x2019 ||
            c == 0x201C || c == 0x201D || c == 0x2039 || c == 0x203A)
            return BidiType.ON;

        // Default: strong LTR (Latin, Cyrillic, Greek, CJK, etc.)
        return BidiType.L;
    }

    /// <summary>
    /// Perform bidi reordering and return a permutation array where perm[i] = original index.
    /// </summary>
    private static string ReorderWithPerm(string text, out int[] perm)
    {
        var len = text.Length;
        var types = new BidiType[len];
        var levels = new byte[len];
        perm = new int[len];
        for (var i = 0; i < len; i++) perm[i] = i;

        for (var i = 0; i < len; i++)
            types[i] = GetBidiType(text[i]);

        byte paragraphLevel = 0;
        for (var i = 0; i < len; i++)
        {
            if (types[i] == BidiType.R) { paragraphLevel = 1; break; }
            if (types[i] == BidiType.L) break;
        }

        for (var i = 0; i < len; i++)
        {
            switch (types[i])
            {
                case BidiType.R:
                    levels[i] = (byte)(paragraphLevel % 2 == 1 ? paragraphLevel : paragraphLevel + 1);
                    break;
                case BidiType.L:
                    levels[i] = (byte)(paragraphLevel % 2 == 0 ? paragraphLevel : paragraphLevel + 1);
                    break;
                case BidiType.EN: case BidiType.ES: case BidiType.ET: case BidiType.CS:
                    levels[i] = paragraphLevel == 1 ? (byte)2 : (byte)0;
                    break;
                case BidiType.WS: case BidiType.ON: case BidiType.Format:
                    levels[i] = ResolveNeutralLevel(types, levels, i, len, paragraphLevel);
                    break;
            }
        }

        byte maxLevel = 0;
        foreach (var level in levels) if (level > maxLevel) maxLevel = level;

        var result = text.ToCharArray();
        for (var level = maxLevel; level >= 1; level--)
        {
            var i = 0;
            while (i < len)
            {
                if (levels[i] >= level)
                {
                    var start = i;
                    while (i < len && levels[i] >= level) i++;
                    ReverseRange(result, start, i - 1);
                    ReverseRange(perm, start, i - 1);
                }
                else i++;
            }
        }

        for (var i = 0; i < len; i++)
            if (levels[i] % 2 == 1) result[i] = MirrorChar(result[i]);

        var reordered = RemoveFormatChars(new string(result));
        // If format chars were removed, the perm array may be longer than the result — trim it
        if (reordered.Length < len)
        {
            // Rebuild perm without format-char positions
            var filteredPerm = new int[reordered.Length];
            var outIdx = 0;
            for (var i = 0; i < len && outIdx < reordered.Length; i++)
            {
                var c = result[i];
                if (c is not ('\u200B' or '\u200C' or '\u200D' or '\u200E' or '\u200F'
                    or '\u202A' or '\u202B' or '\u202C' or '\u202D' or '\u202E'
                    or '\u2066' or '\u2067' or '\u2068' or '\u2069' or '\uFEFF'))
                {
                    filteredPerm[outIdx++] = perm[i];
                }
            }
            perm = filteredPerm;
        }

        return reordered;
    }

    private static void ReverseRange(int[] arr, int start, int end)
    {
        while (start < end)
        {
            (arr[start], arr[end]) = (arr[end], arr[start]);
            start++;
            end--;
        }
    }

    /// <summary>
    /// Perform bidi reordering on a string containing RTL characters.
    /// Implements a simplified version of the Unicode Bidi Algorithm.
    /// </summary>
    private static string Reorder(string text)
    {
        var len = text.Length;
        var types = new BidiType[len];
        var levels = new byte[len];

        // Step 1: Assign initial bidi types
        for (var i = 0; i < len; i++)
            types[i] = GetBidiType(text[i]);

        // Step 2: Determine paragraph embedding level
        // For PDF text extraction, the paragraph level is determined by the first strong character
        byte paragraphLevel = 0;
        for (var i = 0; i < len; i++)
        {
            if (types[i] == BidiType.R)
            {
                paragraphLevel = 1;
                break;
            }
            if (types[i] == BidiType.L)
                break;
        }

        // Step 3: Assign embedding levels
        // Simplified: all strong characters get the paragraph level or paragraph level + 1
        for (var i = 0; i < len; i++)
        {
            switch (types[i])
            {
                case BidiType.R:
                    levels[i] = (byte)(paragraphLevel % 2 == 1 ? paragraphLevel : paragraphLevel + 1);
                    break;
                case BidiType.L:
                    levels[i] = (byte)(paragraphLevel % 2 == 0 ? paragraphLevel : paragraphLevel + 1);
                    break;
                case BidiType.EN:
                case BidiType.ES:
                case BidiType.ET:
                case BidiType.CS:
                    // Numbers in RTL context get level 2 (still LTR but inside RTL)
                    levels[i] = paragraphLevel == 1 ? (byte)2 : (byte)0;
                    break;
                case BidiType.WS:
                case BidiType.ON:
                case BidiType.Format:
                    // Neutrals: resolve based on surrounding strong types
                    levels[i] = ResolveNeutralLevel(types, levels, i, len, paragraphLevel);
                    break;
            }
        }

        // Step 4: Reverse characters at each level
        // Find the maximum embedding level
        byte maxLevel = 0;
        foreach (var level in levels)
        {
            if (level > maxLevel) maxLevel = level;
        }

        var result = text.ToCharArray();

        // Reverse from max level down to 1
        for (var level = maxLevel; level >= 1; level--)
        {
            // Find contiguous runs at this level or higher
            var i = 0;
            while (i < len)
            {
                if (levels[i] >= level)
                {
                    var start = i;
                    while (i < len && levels[i] >= level) i++;
                    // Reverse the run [start, i)
                    ReverseRange(result, start, i - 1);
                }
                else
                {
                    i++;
                }
            }
        }

        // Mirror bracket/punctuation characters in RTL runs
        for (var i = 0; i < len; i++)
        {
            if (levels[i] % 2 == 1)
            {
                result[i] = MirrorChar(result[i]);
            }
        }

        // Remove directional formatting characters
        return RemoveFormatChars(new string(result));
    }

    private static byte ResolveNeutralLevel(BidiType[] types, byte[] levels, int pos, int len, byte paragraphLevel)
    {
        // Look left for the nearest strong type
        var leftType = BidiType.ON;
        for (var i = pos - 1; i >= 0; i--)
        {
            if (types[i] == BidiType.L || types[i] == BidiType.R)
            {
                leftType = types[i];
                break;
            }
            if (types[i] == BidiType.EN)
            {
                leftType = BidiType.R; // EN treated as R for neutral resolution in RTL
                break;
            }
        }

        // Look right for the nearest strong type
        var rightType = BidiType.ON;
        for (var i = pos + 1; i < len; i++)
        {
            if (types[i] == BidiType.L || types[i] == BidiType.R)
            {
                rightType = types[i];
                break;
            }
            if (types[i] == BidiType.EN)
            {
                rightType = BidiType.R;
                break;
            }
        }

        // If both sides agree, use that level
        if (leftType == BidiType.R && rightType == BidiType.R)
            return (byte)(paragraphLevel % 2 == 1 ? paragraphLevel : paragraphLevel + 1);
        if (leftType == BidiType.L && rightType == BidiType.L)
            return (byte)(paragraphLevel % 2 == 0 ? paragraphLevel : paragraphLevel + 1);

        // Otherwise, use paragraph level
        return paragraphLevel;
    }

    private static void ReverseRange(char[] arr, int start, int end)
    {
        while (start < end)
        {
            (arr[start], arr[end]) = (arr[end], arr[start]);
            start++;
            end--;
        }
    }

    private static char MirrorChar(char c)
    {
        return c switch
        {
            '(' => ')',
            ')' => '(',
            '[' => ']',
            ']' => '[',
            '{' => '}',
            '}' => '{',
            '<' => '>',
            '>' => '<',
            '\u00AB' => '\u00BB', // «»
            '\u00BB' => '\u00AB',
            '\u2039' => '\u203A', // ‹›
            '\u203A' => '\u2039',
            '\u2018' => '\u2019', // ''
            '\u2019' => '\u2018',
            '\u201C' => '\u201D', // ""
            '\u201D' => '\u201C',
            _ => c,
        };
    }

    private static string RemoveFormatChars(string text)
    {
        // Remove zero-width and directional format characters
        var hasFormat = false;
        foreach (var c in text)
        {
            if (c is '\u200B' or '\u200C' or '\u200D' or '\u200E' or '\u200F'
                or '\u202A' or '\u202B' or '\u202C' or '\u202D' or '\u202E'
                or '\u2066' or '\u2067' or '\u2068' or '\u2069'
                or '\uFEFF')
            {
                hasFormat = true;
                break;
            }
        }

        if (!hasFormat) return text;

        var chars = new char[text.Length];
        var pos = 0;
        foreach (var c in text)
        {
            if (c is not ('\u200B' or '\u200C' or '\u200D' or '\u200E' or '\u200F'
                or '\u202A' or '\u202B' or '\u202C' or '\u202D' or '\u202E'
                or '\u2066' or '\u2067' or '\u2068' or '\u2069'
                or '\uFEFF'))
            {
                chars[pos++] = c;
            }
        }
        return new string(chars, 0, pos);
    }

    private enum BidiType
    {
        L,      // Left-to-Right
        R,      // Right-to-Left
        EN,     // European Number
        ES,     // European Number Separator
        ET,     // European Number Terminator
        CS,     // Common Number Separator
        WS,     // Whitespace
        ON,     // Other Neutral
        Format, // Directional formatting characters
    }
}

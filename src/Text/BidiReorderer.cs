namespace Aspose.Pdf.Text;

/// <summary>
/// Implements the Unicode Bidi Algorithm (UAX #9) for reordering visual-order RTL
/// text to logical order during PDF text extraction. Levels are computed on the
/// input string (rules W1–W7, N1–N2, I1–I2, L1), then maximal runs at each level
/// from the highest down to the lowest odd level are flipped (L2). Applying this
/// to a PDF's physically (visually) ordered string yields the logical string.
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
        return ReorderCore(text, out _);
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
        return ReorderCore(text, out perm);
    }

    /// <summary>
    /// Logical-order text to the VISUAL (left-to-right drawing) order a PDF
    /// producer stores it in. UAX #9 L2 is its own inverse on the resolved
    /// levels — the same run flips that take a drawn string to logical order
    /// take a logical string to drawn order — so the extraction reorderer
    /// serves both ways. The writer stores an RTL replacement this way
    /// ("1114ש42לום" is written ם ו ל 4 2 ש 1 1 1 4), which is what the
    /// reader then extracts back as the logical string.
    /// </summary>
    public static string ToVisualIfRtl(string text) => ReorderIfNeeded(text);

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

    // Bidi character types (UAX #9 table 4).
    private const sbyte L = 0;    // Left-to-Right
    private const sbyte R = 1;    // Right-to-Left
    private const sbyte AL = 2;   // Right-to-Left Arabic
    private const sbyte EN = 3;   // European Number
    private const sbyte ES = 4;   // European Number Separator
    private const sbyte ET = 5;   // European Number Terminator
    private const sbyte AN = 6;   // Arabic Number
    private const sbyte CS = 7;   // Common Number Separator
    private const sbyte NSM = 8;  // Non-Spacing Mark
    private const sbyte BN = 9;   // Boundary Neutral (formatting / zero-width)
    private const sbyte S = 10;   // Segment Separator
    private const sbyte WS = 11;  // Whitespace
    private const sbyte ON = 12;  // Other Neutral

    private static sbyte GetBidiType(char c)
    {
        // Directional marks / zero-width formatting characters.
        if (c == 0x200F) return R;   // RLM
        if (c == 0x200E) return L;   // LRM
        if (c is (>= (char)0x200B and <= (char)0x200D) or (>= (char)0x202A and <= (char)0x202E)
              or (>= (char)0x2066 and <= (char)0x2069) or (char)0xFEFF)
            return BN;

        // Non-spacing marks (combining ranges relevant to extraction).
        if (c is (>= (char)0x0300 and <= (char)0x036F)     // combining diacriticals
              or (>= (char)0x0591 and <= (char)0x05BD)     // Hebrew points
              or (char)0x05BF or (char)0x05C1 or (char)0x05C2 or (char)0x05C4 or (char)0x05C5 or (char)0x05C7
              or (>= (char)0x0610 and <= (char)0x061A)     // Arabic marks
              or (>= (char)0x064B and <= (char)0x065F) or (char)0x0670
              or (>= (char)0x06D6 and <= (char)0x06DC) or (>= (char)0x06DF and <= (char)0x06E4)
              or (char)0x06E7 or (char)0x06E8 or (>= (char)0x06EA and <= (char)0x06ED))
            return NSM;

        // Strong RTL: Arabic-script ranges are AL, the rest (Hebrew etc.) R.
        if ((c >= 0x0600 && c <= 0x06FF) || (c >= 0x0750 && c <= 0x077F)
            || (c >= 0xFB50 && c <= 0xFDFF) || (c >= 0xFE70 && c <= 0xFEFF))
            return AL;
        if (IsRtlChar(c)) return R;

        // Numbers.
        if (c >= '0' && c <= '9') return EN;
        if (c >= 0x0660 && c <= 0x0669) return AN;  // Arabic-Indic digits

        // Number separators / terminators.
        if (c == '+' || c == '-') return ES;
        if (c == '#' || c == '$' || c == '%' || c == 0x00B0 || c == 0x00A2 || c == 0x00A3 || c == 0x00A5)
            return ET;
        if (c == '/' || c == ':' || c == ',' || c == '.' || c == 0x00A0) return CS;

        // Whitespace / segment separators. U+E000 is TextAbsorber's masked
        // line-end glyph space (EolShowSpaceSentinel) — it stands for a space.
        if (c == '\t') return S;
        if (c == ' ' || c == '\n' || c == '\r' || c == 0x000C || c == 0xE000) return WS;

        // Neutral punctuation and symbols.
        if (c < 0x0041) return ON;
        if (c > 0x005A && c < 0x0061) return ON;
        if (c > 0x007A && c < 0x00C0) return ON;
        if (c == 0x2018 || c == 0x2019 || c == 0x201C || c == 0x201D
            || c == 0x2039 || c == 0x203A || c == 0x00AB || c == 0x00BB
            || (c >= 0x2010 && c <= 0x2015)) return ON;

        // Default: strong LTR (Latin, Cyrillic, Greek, CJK, etc.)
        return L;
    }

    /// <summary>
    /// Core UBA pass: compute levels on the input string, reorder, mirror, strip formats.
    /// perm[outPos] = index in the original string.
    /// </summary>
    private static string ReorderCore(string text, out int[] perm)
    {
        var len = text.Length;
        var initialTypes = new sbyte[len];
        for (var i = 0; i < len; i++)
            initialTypes[i] = GetBidiType(text[i]);

        // P2/P3: paragraph embedding level from the first strong character.
        sbyte para = 0;
        for (var i = 0; i < len; i++)
        {
            var t = initialTypes[i];
            if (t == L) break;
            if (t == R || t == AL) { para = 1; break; }
        }

        var types = (sbyte[])initialTypes.Clone();
        var levels = new sbyte[len];
        for (var i = 0; i < len; i++) levels[i] = para;

        // The whole line is one level run (no explicit embedding codes are honoured
        // during extraction); sor/eor take the paragraph direction.
        var sor = (sbyte)((para & 1) == 1 ? R : L);
        var eor = sor;

        ResolveWeakTypes(types, 0, len, sor);
        ResolveNeutralTypes(types, levels, 0, len, para, sor, eor);
        ResolveImplicitLevels(types, levels, 0, len);

        // L1: segment separators and trailing whitespace reset to the paragraph level.
        for (var i = 0; i < len; i++)
        {
            var t = initialTypes[i];
            if (t != S) continue;
            levels[i] = para;
            for (var j = i - 1; j >= 0 && IsWhitespaceType(initialTypes[j]); j--)
                levels[j] = para;
        }
        for (var j = len - 1; j >= 0 && IsWhitespaceType(initialTypes[j]); j--)
            levels[j] = para;

        // L2: from the highest level down to the lowest odd level, flip maximal runs.
        sbyte highest = 0, lowestOdd = 63;
        foreach (var lv in levels)
        {
            if (lv > highest) highest = lv;
            if ((lv & 1) == 1 && lv < lowestOdd) lowestOdd = lv;
        }
        var order = new int[len];
        for (var i = 0; i < len; i++) order[i] = i;
        for (var level = highest; level >= lowestOdd && lowestOdd != 63; level--)
        {
            var i = 0;
            while (i < len)
            {
                if (levels[i] < level) { i++; continue; }
                var start = i;
                while (i < len && levels[i] >= level) i++;
                var a = start;
                var b = i - 1;
                while (a < b)
                {
                    (order[a], order[b]) = (order[b], order[a]);
                    a++;
                    b--;
                }
            }
        }

        // L4: mirror characters at odd levels (indexed by ORIGINAL position), then emit
        // in reordered sequence, skipping zero-width / directional formatting characters.
        var outChars = new char[len];
        var outPerm = new int[len];
        var pos = 0;
        for (var k = 0; k < len; k++)
        {
            var idx = order[k];
            var c = text[idx];
            if (IsFormatChar(c)) continue;
            if ((levels[idx] & 1) == 1) c = MirrorChar(c);
            outChars[pos] = c;
            outPerm[pos] = idx;
            pos++;
        }
        perm = pos == len ? outPerm : outPerm[..pos];
        return new string(outChars, 0, pos);
    }

    private static bool IsWhitespaceType(sbyte t) => t == WS || t == BN;

    // W1–W7 (UAX #9), operating on one level run [start, limit).
    private static void ResolveWeakTypes(sbyte[] types, int start, int limit, sbyte sor)
    {
        // W1: NSM takes the type of the previous character.
        var prev = sor;
        for (var i = start; i < limit; i++)
        {
            if (types[i] == NSM) types[i] = prev;
            else prev = types[i];
        }

        // W2: EN → AN when the last strong type before it is AL.
        for (var i = start; i < limit; i++)
        {
            if (types[i] != EN) continue;
            for (var j = i - 1; j >= start; j--)
            {
                var t = types[j];
                if (t != L && t != R && t != AL) continue;
                if (t == AL) types[i] = AN;
                break;
            }
        }

        // W3: AL → R.
        for (var i = start; i < limit; i++)
            if (types[i] == AL) types[i] = R;

        // W4: single ES between two ENs → EN; single CS between EN/EN or AN/AN → that number type.
        for (var i = start + 1; i < limit - 1; i++)
        {
            if (types[i] != ES && types[i] != CS) continue;
            var prevType = types[i - 1];
            var nextType = types[i + 1];
            if (prevType == EN && nextType == EN) types[i] = EN;
            else if (types[i] == CS && prevType == AN && nextType == AN) types[i] = AN;
        }

        // W5: a run of ETs adjacent to an EN becomes EN.
        for (var i = start; i < limit; i++)
        {
            if (types[i] != ET) continue;
            var runStart = i;
            var runLimit = i;
            while (runLimit < limit && types[runLimit] == ET) runLimit++;
            var before = runStart > start ? types[runStart - 1] : sor;
            var after = runLimit < limit ? types[runLimit] : sor;
            if (before == EN || after == EN)
                for (var j = runStart; j < runLimit; j++) types[j] = EN;
            i = runLimit - 1;
        }

        // W6: remaining separators/terminators → ON.
        for (var i = start; i < limit; i++)
            if (types[i] == ES || types[i] == ET || types[i] == CS) types[i] = ON;

        // W7: EN → L when the last strong type before it is L.
        for (var i = start; i < limit; i++)
        {
            if (types[i] != EN) continue;
            var prevStrong = sor;
            for (var j = i - 1; j >= start; j--)
            {
                var t = types[j];
                if (t == L || t == R) { prevStrong = t; break; }
            }
            if (prevStrong == L) types[i] = L;
        }
    }

    // N1–N2: neutrals take the surrounding direction when both sides agree
    // (numbers count as R), otherwise the embedding direction.
    private static void ResolveNeutralTypes(sbyte[] types, sbyte[] levels, int start, int limit,
        sbyte para, sbyte sor, sbyte eor)
    {
        for (var i = start; i < limit; i++)
        {
            var t = types[i];
            if (t != WS && t != ON && t != S && t != BN) continue;
            var runStart = i;
            var runLimit = i;
            while (runLimit < limit)
            {
                var rt = types[runLimit];
                if (rt != WS && rt != ON && rt != S && rt != BN) break;
                runLimit++;
            }

            var leading = runStart == start ? sor : types[runStart - 1];
            if (leading == EN || leading == AN) leading = R;
            var trailing = runLimit == limit ? eor : types[runLimit];
            if (trailing == EN || trailing == AN) trailing = R;

            var resolved = leading == trailing ? leading : (sbyte)((para & 1) == 1 ? R : L);
            for (var j = runStart; j < runLimit; j++) types[j] = resolved;
            i = runLimit - 1;
        }
    }

    // I1–I2: resolved types to implicit levels.
    private static void ResolveImplicitLevels(sbyte[] types, sbyte[] levels, int start, int limit)
    {
        for (var i = start; i < limit; i++)
        {
            var level = levels[i];
            var t = types[i];
            if ((level & 1) == 0)
            {
                // Even (LTR) level: R goes up one, numbers go up two.
                if (t == R) levels[i] = (sbyte)(level + 1);
                else if (t == AN || t == EN) levels[i] = (sbyte)(level + 2);
            }
            else
            {
                // Odd (RTL) level: L and numbers go up one.
                if (t == L || t == EN || t == AN) levels[i] = (sbyte)(level + 1);
            }
        }
    }

    private static bool IsFormatChar(char c) =>
        (c >= 0x200B && c <= 0x200F) || (c >= 0x202A && c <= 0x202E)
        || (c >= 0x2066 && c <= 0x2069) || c == 0xFEFF;

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
            '«' => '»', // «»
            '»' => '«',
            '‹' => '›', // ‹›
            '›' => '‹',
            '⁅' => '⁆', // ⁅⁆
            '⁆' => '⁅',
            '⌈' => '⌉', // ⌈⌉
            '⌉' => '⌈',
            '⌊' => '⌋', // ⌊⌋
            '⌋' => '⌊',
            _ => c,
        };
    }
}

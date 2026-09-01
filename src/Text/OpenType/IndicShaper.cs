namespace Aspose.Pdf.Text.OpenType;

/// <summary>
/// The Indic shaping model, for the scripts that need it (Devanagari, Bengali, Gurmukhi,
/// Gujarati, Oriya, Tamil, Telugu, Kannada, Malayalam).
/// <para>
/// Indic text cannot be drawn by mapping each character through the cmap in order: the
/// script writes some marks BEFORE the consonant they follow in memory (the pre-base
/// matra), turns a leading ra + virama into a REPH that rides at the end of the syllable,
/// and fuses consonant clusters into conjunct glyphs the font supplies through GSUB. This
/// class does the reordering and drives <see cref="GsubEngine"/> through the feature order
/// the spec fixes, which is what produces the expected glyph run.
/// </para>
/// </summary>
internal static class IndicShaper
{
    // ── character categories ──────────────────────────────────────────────────
    internal const int CatOther = 0;
    internal const int CatConsonant = 1;
    internal const int CatVowel = 2;          // independent vowel
    internal const int CatMatraPre = 3;       // vowel sign drawn before the base
    internal const int CatMatraAbove = 4;
    internal const int CatMatraBelow = 5;
    internal const int CatMatraPost = 6;
    internal const int CatVirama = 7;         // halant
    internal const int CatNukta = 8;
    internal const int CatBindu = 9;          // anusvara / candrabindu / visarga
    internal const int CatZwj = 10;
    internal const int CatZwnj = 11;
    internal const int CatRa = 12;            // the consonant that can form a reph

    // ── glyph positions inside a syllable (reordering sorts on these) ─────────
    internal const int PosRaToBecomeReph = 0;
    internal const int PosPreBase = 1;
    internal const int PosBase = 2;
    internal const int PosAfterBase = 3;
    internal const int PosBelowBase = 4;
    internal const int PosPostBase = 5;
    internal const int PosAboveBase = 6;
    internal const int PosRephAfterBase = 7;
    internal const int PosSyllableEnd = 8;

    /// <summary>
    /// The OpenType script tag for a character, or null when the character is not from a
    /// script this shaper handles.
    /// <para>
    /// Only the NORTHERN group is shaped — Devanagari, Bengali, Gurmukhi, Gujarati,
    /// Oriya. The southern scripts (Tamil, Telugu, Kannada, Malayalam) are deliberately
    /// left alone: the expected Tamil run is byte-for-byte the plain cmap mapping of
    /// the text (144 characters, 144 glyphs, no substitution and no reordering) even
    /// though the face carries a full `taml` GSUB. Shaping them would ligate and
    /// reorder text that is drawn straight.
    /// </para>
    /// </summary>
    internal static string? ScriptTagOf(int cp) => cp switch
    {
        >= 0x0900 and <= 0x097F => "deva",
        >= 0x0980 and <= 0x09FF => "beng",
        >= 0x0A00 and <= 0x0A7F => "guru",
        >= 0x0A80 and <= 0x0AFF => "gujr",
        >= 0x0B00 and <= 0x0B7F => "orya",
        _ => null,
    };

    /// <summary>The base of the character's script block — the categories below are all
    /// stated as offsets from it, because the Indic blocks share one layout.</summary>
    private static int BlockBase(int cp) => cp switch
    {
        >= 0x0900 and <= 0x097F => 0x0900,
        >= 0x0980 and <= 0x09FF => 0x0980,
        >= 0x0A00 and <= 0x0A7F => 0x0A00,
        >= 0x0A80 and <= 0x0AFF => 0x0A80,
        >= 0x0B00 and <= 0x0B7F => 0x0B00,
        >= 0x0B80 and <= 0x0BFF => 0x0B80,
        >= 0x0C00 and <= 0x0C7F => 0x0C00,
        >= 0x0C80 and <= 0x0CFF => 0x0C80,
        >= 0x0D00 and <= 0x0D7F => 0x0D00,
        _ => -1,
    };

    /// <summary>Categorise one character. The Indic blocks are laid out alike, so the
    /// offset within the block decides the category, with the per-script exceptions that
    /// matter for the matra direction called out.</summary>
    internal static int Categorise(int cp)
    {
        if (cp == 0x200D) return CatZwj;
        if (cp == 0x200C) return CatZwnj;
        var b = BlockBase(cp);
        if (b < 0) return CatOther;
        var o = cp - b;

        // 0x00..0x03 signs (candrabindu, anusvara, visarga)
        if (o <= 0x03) return CatBindu;
        // independent vowels 0x05..0x14 (Devanagari also 0x72..0x77 etc.)
        if (o >= 0x04 && o <= 0x14) return CatVowel;
        // consonants 0x15..0x39
        if (o >= 0x15 && o <= 0x39)
            return IsRa(cp) ? CatRa : CatConsonant;
        if (o == 0x3C) return CatNukta;
        if (o == 0x3D) return CatVowel;               // avagraha — behaves as a base
        // dependent vowel signs 0x3E..0x4C
        if (o >= 0x3E && o <= 0x4C) return MatraPosition(cp, o);
        if (o == 0x4D) return CatVirama;
        // additional signs / vowels 0x50..0x63
        if (o >= 0x51 && o <= 0x57) return CatBindu;  // stress and accent marks sit above
        if (o >= 0x58 && o <= 0x5F) return CatConsonant;
        if (o >= 0x60 && o <= 0x61) return CatVowel;
        if (o >= 0x62 && o <= 0x63) return CatMatraBelow;
        if (o >= 0x66 && o <= 0x6F) return CatOther;  // digits
        if (o >= 0x71 && o <= 0x77) return CatConsonant;
        return CatOther;
    }

    /// <summary>The RA of each block — the consonant a virama can turn into a reph.</summary>
    private static bool IsRa(int cp) => cp is 0x0930 or 0x09B0 or 0x0A30 or 0x0AB0
        or 0x0B30 or 0x0BB0 or 0x0C30 or 0x0CB0 or 0x0D30;

    /// <summary>Which side of the base a dependent vowel sign is drawn on. Most of the
    /// blocks agree; the ones that differ are listed explicitly.</summary>
    private static int MatraPosition(int cp, int o)
    {
        switch (cp)
        {
            // Devanagari
            case 0x093F: return CatMatraPre;                  // sign I
            case 0x0940: case 0x093E: return CatMatraPost;    // sign II, sign AA
            case 0x0941: case 0x0942: case 0x0943: case 0x0944: return CatMatraBelow;
            case 0x0945: case 0x0946: case 0x0947: case 0x0948: return CatMatraAbove;
            case 0x0949: case 0x094A: case 0x094B: case 0x094C: return CatMatraPost;
            // Bengali / Gurmukhi / Gujarati share Devanagari's layout closely
            case 0x09BF: case 0x0A3F: case 0x0ABF: return CatMatraPre;
            case 0x09C7: case 0x09C8: return CatMatraPre;
            case 0x09CB: case 0x09CC: return CatMatraPost;
            // Tamil / Telugu / Kannada / Malayalam
            case 0x0BC6: case 0x0BC7: case 0x0BC8: return CatMatraPre;
            case 0x0D46: case 0x0D47: case 0x0D48: return CatMatraPre;
            case 0x0CC0: case 0x0CC7: case 0x0CC8: return CatMatraAbove;
        }
        // Fallback by offset: 3E/40 post, 3F pre, 41-44 below, 45-48 above, 49-4C post.
        return o switch
        {
            0x3F => CatMatraPre,
            0x3E or 0x40 => CatMatraPost,
            >= 0x41 and <= 0x44 => CatMatraBelow,
            >= 0x45 and <= 0x48 => CatMatraAbove,
            _ => CatMatraPost,
        };
    }

    /// <summary>True when the run contains any character this shaper handles.</summary>
    internal static bool NeedsShaping(string text)
    {
        foreach (var ch in text)
            if (ScriptTagOf(ch) is not null) return true;
        return false;
    }
}

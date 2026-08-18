using System.Text;

namespace Aspose.Pdf.Engine.Security.Impl.Sasl;

/// <summary>
/// SASLprep (RFC 4013) string preparation over the stringprep framework
/// (RFC 3454). Used to prepare passwords for the AES-256 (ISO 32000-2 / PDF 2.0,
/// security handler revision 6) key-derivation, so that visually-equivalent
/// passwords hash identically regardless of Unicode representation.
///
/// Processing order (RFC 3454 §2, RFC 4013 §2):
///   1. Map    — "commonly mapped to nothing" (Table B.1) are removed;
///               non-ASCII spaces (Table C.1.2) become SPACE (U+0020).
///   2. Normalize — Unicode NFKC.
///   3. Prohibit — reject strings containing prohibited code points
///               (Tables C.2.1, C.2.2, C.3–C.9).
///   4. Bidi    — enforce the RFC 3454 §6 bidirectional rules.
///
/// Unassigned code points (Table A.1) are not enforced: this is the
/// query-string behaviour of SASLprep and avoids embedding the full Unicode
/// 3.2 unassigned table. Input bytes/streams are decoded as UTF-8.
/// </summary>
internal class Stringprep
{
    private readonly string _input;

    public Stringprep() => _input = string.Empty;

    public Stringprep(string input) => _input = input ?? string.Empty;

    public Stringprep(byte[] input) => _input = Decode(input);

    public Stringprep(Stream input)
    {
        if (input is null) { _input = string.Empty; return; }
        using var ms = new MemoryStream();
        input.CopyTo(ms);
        _input = Decode(ms.ToArray());
    }

    /// <summary>The prepared string; populated by <see cref="Process"/>.</summary>
    public string Result { get; private set; } = string.Empty;

    /// <summary>Run the SASLprep profile over the input, storing the prepared
    /// value in <see cref="Result"/>. Throws <see cref="StringprepException"/>
    /// on a prohibited code point or a bidirectional-rule violation.</summary>
    public void Process() => Result = SaslPrep(_input);

    /// <summary>UTF-8 bytes of the prepared <see cref="Result"/>.</summary>
    public byte[] ToBytes() => Encoding.UTF8.GetBytes(Result);

    /// <summary>Prepare a password for AES-256 (R6) key derivation. Applies
    /// SASLprep, but falls back to the original string when SASLprep rejects it,
    /// matching the lenient behaviour PDF processors use so a password that is
    /// not SASLprep-valid still round-trips.</summary>
    internal static string PrepareForKeyDerivation(string password)
    {
        if (string.IsNullOrEmpty(password)) return password ?? string.Empty;
        try { return SaslPrep(password); }
        catch (StringprepException) { return password; }
    }

    private static string Decode(byte[] input)
        => input is null || input.Length == 0 ? string.Empty : Encoding.UTF8.GetString(input);

    /// <summary>Apply the full SASLprep profile, returning the prepared string
    /// or throwing <see cref="StringprepException"/>.</summary>
    internal static string SaslPrep(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        // Step 1 — map.
        var mapped = new StringBuilder(input.Length);
        foreach (var cp in CodePoints(input))
        {
            if (IsMappedToNothing(cp)) continue;          // Table B.1
            mapped.Append(char.ConvertFromUtf32(IsNonAsciiSpace(cp) ? 0x20 : cp)); // Table C.1.2
        }

        // Step 2 — normalize (NFKC).
        var normalized = NormalizationFormKC.Normalize(mapped.ToString());

        // Step 3 — prohibit, and gather the bidi character classes for step 4.
        var hasRandAl = false;
        var hasL = false;
        int firstCp = -1, lastCp = -1;
        foreach (var cp in CodePoints(normalized))
        {
            var table = ProhibitedTable(cp);
            if (table is not null)
                throw new StringprepException(
                    $"SASLprep: prohibited character U+{cp:X4} (RFC 3454 table {table}).");

            if (firstCp < 0) firstCp = cp;
            lastCp = cp;
            if (IsRandAlCat(cp)) hasRandAl = true;
            else if (IsLCat(cp)) hasL = true;
        }

        // Step 4 — bidirectional check (RFC 3454 §6).
        if (hasRandAl)
        {
            if (hasL)
                throw new StringprepException(
                    "SASLprep: string mixes right-to-left and left-to-right characters (RFC 3454 §6).");
            if (firstCp < 0 || !IsRandAlCat(firstCp) || !IsRandAlCat(lastCp))
                throw new StringprepException(
                    "SASLprep: a right-to-left string must start and end with a RandALCat character (RFC 3454 §6).");
        }

        return normalized;
    }

    // ── Code-point enumeration (surrogate-pair aware) ──────────────────

    private static IEnumerable<int> CodePoints(string s)
    {
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                yield return char.ConvertToUtf32(c, s[i + 1]);
                i++;
            }
            else
            {
                // Lone surrogates fall through as their raw value (Table C.5 catches them).
                yield return c;
            }
        }
    }

    // ── Mapping tables ─────────────────────────────────────────────────

    /// <summary>RFC 3454 Table B.1 — "commonly mapped to nothing".</summary>
    private static bool IsMappedToNothing(int cp) => cp switch
    {
        0x00AD or 0x034F or 0x1806 or 0x180B or 0x180C or 0x180D
            or 0x200B or 0x200C or 0x200D or 0x2060 or 0xFEFF => true,
        _ => cp is >= 0xFE00 and <= 0xFE0F,
    };

    /// <summary>RFC 3454 Table C.1.2 — non-ASCII space characters (mapped to SPACE).</summary>
    private static bool IsNonAsciiSpace(int cp) => cp switch
    {
        0x00A0 or 0x1680 or 0x202F or 0x205F or 0x3000 => true,
        _ => cp is >= 0x2000 and <= 0x200B,
    };

    // ── Prohibited tables (RFC 3454 C.2.1, C.2.2, C.3–C.9) ─────────────

    /// <summary>Return the RFC 3454 table name if <paramref name="cp"/> is a
    /// prohibited output character, else null.</summary>
    private static string? ProhibitedTable(int cp)
    {
        // C.2.1 ASCII control characters.
        if (cp <= 0x001F || cp == 0x007F) return "C.2.1";
        // C.2.2 non-ASCII control characters.
        if ((cp is >= 0x0080 and <= 0x009F) || cp == 0x06DD || cp == 0x070F || cp == 0x180E
            || (cp is >= 0x200C and <= 0x200F) || (cp is >= 0x2028 and <= 0x202E)
            || (cp is >= 0x2060 and <= 0x2063) || (cp is >= 0x206A and <= 0x206F)
            || cp == 0xFEFF || (cp is >= 0xFFF9 and <= 0xFFFC)
            || (cp is >= 0x1D173 and <= 0x1D17A)) return "C.2.2";
        // C.3 private use.
        if ((cp is >= 0xE000 and <= 0xF8FF) || (cp is >= 0xF0000 and <= 0xFFFFD)
            || (cp is >= 0x100000 and <= 0x10FFFD)) return "C.3";
        // C.4 non-character code points.
        if ((cp is >= 0xFDD0 and <= 0xFDEF) || (cp & 0xFFFE) == 0xFFFE) return "C.4";
        // C.5 surrogate code points.
        if (cp is >= 0xD800 and <= 0xDFFF) return "C.5";
        // C.6 inappropriate for plain text.
        if (cp is >= 0xFFF9 and <= 0xFFFD) return "C.6";
        // C.7 inappropriate for canonical representation.
        if (cp is >= 0x2FF0 and <= 0x2FFB) return "C.7";
        // C.8 change display properties / deprecated.
        if (cp == 0x0340 || cp == 0x0341 || cp == 0x200E || cp == 0x200F
            || (cp is >= 0x202A and <= 0x202E) || (cp is >= 0x206A and <= 0x206F)) return "C.8";
        // C.9 tagging characters.
        if (cp == 0xE0001 || (cp is >= 0xE0020 and <= 0xE007F)) return "C.9";
        return null;
    }

    // ── Bidirectional character classes (RFC 3454 §6) ──────────────────

    /// <summary>RFC 3454 Table D.1 — characters with bidirectional property
    /// "R" or "AL" (RandALCat).</summary>
    private static bool IsRandAlCat(int cp) =>
        cp == 0x05BE || cp == 0x05C0 || cp == 0x05C3 || (cp is >= 0x05D0 and <= 0x05EA)
        || (cp is >= 0x05F0 and <= 0x05F4) || cp == 0x061B || cp == 0x061F
        || (cp is >= 0x0621 and <= 0x063A) || (cp is >= 0x0640 and <= 0x064A)
        || (cp is >= 0x066D and <= 0x066F) || (cp is >= 0x0671 and <= 0x06D5)
        || cp == 0x06DD || (cp is >= 0x06E5 and <= 0x06E6) || (cp is >= 0x06FA and <= 0x06FE)
        || (cp is >= 0x0700 and <= 0x070D) || cp == 0x0710 || (cp is >= 0x0712 and <= 0x072C)
        || (cp is >= 0x0780 and <= 0x07A5) || cp == 0x07B1 || cp == 0x200F
        || cp == 0xFB1D || (cp is >= 0xFB1F and <= 0xFB28) || (cp is >= 0xFB2A and <= 0xFB36)
        || (cp is >= 0xFB38 and <= 0xFB3C) || cp == 0xFB3E || (cp is >= 0xFB40 and <= 0xFB41)
        || (cp is >= 0xFB43 and <= 0xFB44) || (cp is >= 0xFB46 and <= 0xFBB1)
        || (cp is >= 0xFBD3 and <= 0xFD3D) || (cp is >= 0xFD50 and <= 0xFD8F)
        || (cp is >= 0xFD92 and <= 0xFDC7) || (cp is >= 0xFDF0 and <= 0xFDFC)
        || (cp is >= 0xFE70 and <= 0xFE74) || (cp is >= 0xFE76 and <= 0xFEFC);

    /// <summary>RFC 3454 Table D.2 — characters with bidirectional property "L".
    /// Approximated by the Unicode letter categories minus RandALCat, which
    /// covers the SASLprep bidi rules without embedding the full Unicode 3.2
    /// property table.</summary>
    private static bool IsLCat(int cp)
    {
        if (cp is >= 0xD800 and <= 0xDFFF || cp > 0x10FFFF) return false;
        var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(cp), 0);
        return cat is System.Globalization.UnicodeCategory.UppercaseLetter
            or System.Globalization.UnicodeCategory.LowercaseLetter
            or System.Globalization.UnicodeCategory.TitlecaseLetter
            or System.Globalization.UnicodeCategory.ModifierLetter
            or System.Globalization.UnicodeCategory.OtherLetter
            && !IsRandAlCat(cp);
    }
}

using System.Text;

namespace Aspose.Pdf.Text;

/// <summary>
/// Normalizes Arabic text extracted from PDFs by decomposing Arabic Presentation Forms
/// (U+FB50–U+FDFF, U+FE70–U+FEFF) to their base Arabic characters and applying
/// Unicode NFC normalization to compose combining sequences.
/// </summary>
internal static class ArabicTextNormalizer
{
    /// <summary>
    /// Normalize Arabic text: decompose presentation forms and apply NFC.
    /// </summary>
    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // Check if any Arabic presentation form characters exist
        var hasPresentationForms = false;
        foreach (var c in text)
        {
            if ((c >= 0xFB50 && c <= 0xFDFF) || (c >= 0xFE70 && c <= 0xFEFF))
            {
                hasPresentationForms = true;
                break;
            }
        }

        var result = text;

        // Decompose Arabic Presentation Forms-B to base Arabic
        if (hasPresentationForms)
        {
            var sb = new StringBuilder(text.Length);
            foreach (var c in result)
            {
                var mapped = MapPresentationForm(c);
                sb.Append(mapped);
            }
            result = sb.ToString();
        }

        // Apply NFC normalization to compose combining character sequences
        // This handles cases like U+0627 + U+0654 → U+0623 (alef + combining hamza → alef-hamza)
        if (!result.IsNormalized(NormalizationForm.FormC))
            result = result.Normalize(NormalizationForm.FormC);

        // Fix incorrectly decomposed Lam-Alef ligatures.
        // Some PDFs map Lam-Alef glyph to U+0627 (alef) + alef-variant instead of
        // U+0644 (lam) + alef-variant. Detect and fix these sequences.
        result = FixLamAlefLigatures(result);

        return result;
    }

    /// <summary>
    /// Fix Lam-Alef ligature decomposition issues.
    /// When a Lam-Alef ligature glyph is mapped via ToUnicode, some CMaps produce
    /// U+0627 + alef-variant instead of U+0644 + alef-variant. This method detects
    /// these patterns and corrects them.
    /// </summary>
    private static string FixLamAlefLigatures(string text)
    {
        // The problematic sequences are:
        // U+0627 U+0623 (alef, alef-hamza-above) → U+0644 U+0623 (lam, alef-hamza-above)
        // U+0627 U+0627 (alef, alef) → U+0644 U+0627 (lam, alef)
        // U+0627 U+0625 (alef, alef-hamza-below) → U+0644 U+0625 (lam, alef-hamza-below)
        // U+0627 U+0622 (alef, alef-madda) → U+0644 U+0622 (lam, alef-madda)
        // These only make sense when the alef is preceded by a connecting Arabic letter
        // (which would connect to Lam, not to bare Alef).

        var hasPattern = false;
        for (var i = 0; i < text.Length - 1; i++)
        {
            if (text[i] == '\u0627' && IsAlefVariant(text[i + 1]))
            {
                hasPattern = true;
                break;
            }
        }
        if (!hasPattern) return text;

        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (i < text.Length - 1 && text[i] == '\u0627' && IsAlefVariant(text[i + 1]))
            {
                // Replace alef with lam to form proper Lam-Alef ligature
                sb.Append('\u0644'); // Lam
            }
            else
            {
                sb.Append(text[i]);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Check if a character is an Alef variant that could be the second part of a Lam-Alef ligature.
    /// </summary>
    private static bool IsAlefVariant(char c)
    {
        return c == '\u0622'  // Alef with Madda Above
            || c == '\u0623'  // Alef with Hamza Above
            || c == '\u0625'  // Alef with Hamza Below
            || c == '\u0627'; // Plain Alef
    }

    /// <summary>
    /// Map Arabic Presentation Forms-B (U+FE70–U+FEFF) characters to their
    /// base Arabic equivalents. Returns the original character if no mapping exists.
    /// </summary>
    private static string MapPresentationForm(char c)
    {
        // Arabic Presentation Forms-B: contextual forms (isolated/final/initial/medial)
        // Each group of 2 or 4 characters maps to one base Arabic letter
        return c switch
        {
            // Hamza forms
            '\uFE80' => "\u0621", // HAMZA ISOLATED
            // Alef with Madda
            '\uFE81' or '\uFE82' => "\u0622",
            // Alef with Hamza Above
            '\uFE83' or '\uFE84' => "\u0623",
            // Waw with Hamza
            '\uFE85' or '\uFE86' => "\u0624",
            // Alef with Hamza Below
            '\uFE87' or '\uFE88' => "\u0625",
            // Yeh with Hamza
            '\uFE89' or '\uFE8A' or '\uFE8B' or '\uFE8C' => "\u0626",
            // Alef
            '\uFE8D' or '\uFE8E' => "\u0627",
            // Beh
            '\uFE8F' or '\uFE90' or '\uFE91' or '\uFE92' => "\u0628",
            // Teh Marbuta
            '\uFE93' or '\uFE94' => "\u0629",
            // Teh
            '\uFE95' or '\uFE96' or '\uFE97' or '\uFE98' => "\u062A",
            // Theh
            '\uFE99' or '\uFE9A' or '\uFE9B' or '\uFE9C' => "\u062B",
            // Jeem
            '\uFE9D' or '\uFE9E' or '\uFE9F' or '\uFEA0' => "\u062C",
            // Hah
            '\uFEA1' or '\uFEA2' or '\uFEA3' or '\uFEA4' => "\u062D",
            // Khah
            '\uFEA5' or '\uFEA6' or '\uFEA7' or '\uFEA8' => "\u062E",
            // Dal
            '\uFEA9' or '\uFEAA' => "\u062F",
            // Thal
            '\uFEAB' or '\uFEAC' => "\u0630",
            // Reh
            '\uFEAD' or '\uFEAE' => "\u0631",
            // Zain
            '\uFEAF' or '\uFEB0' => "\u0632",
            // Seen
            '\uFEB1' or '\uFEB2' or '\uFEB3' or '\uFEB4' => "\u0633",
            // Sheen
            '\uFEB5' or '\uFEB6' or '\uFEB7' or '\uFEB8' => "\u0634",
            // Sad
            '\uFEB9' or '\uFEBA' or '\uFEBB' or '\uFEBC' => "\u0635",
            // Dad
            '\uFEBD' or '\uFEBE' or '\uFEBF' or '\uFEC0' => "\u0636",
            // Tah
            '\uFEC1' or '\uFEC2' or '\uFEC3' or '\uFEC4' => "\u0637",
            // Zah
            '\uFEC5' or '\uFEC6' or '\uFEC7' or '\uFEC8' => "\u0638",
            // Ain
            '\uFEC9' or '\uFECA' or '\uFECB' or '\uFECC' => "\u0639",
            // Ghain
            '\uFECD' or '\uFECE' or '\uFECF' or '\uFED0' => "\u063A",
            // Feh
            '\uFED1' or '\uFED2' or '\uFED3' or '\uFED4' => "\u0641",
            // Qaf
            '\uFED5' or '\uFED6' or '\uFED7' or '\uFED8' => "\u0642",
            // Kaf
            '\uFED9' or '\uFEDA' or '\uFEDB' or '\uFEDC' => "\u0643",
            // Lam
            '\uFEDD' or '\uFEDE' or '\uFEDF' or '\uFEE0' => "\u0644",
            // Meem
            '\uFEE1' or '\uFEE2' or '\uFEE3' or '\uFEE4' => "\u0645",
            // Noon
            '\uFEE5' or '\uFEE6' or '\uFEE7' or '\uFEE8' => "\u0646",
            // Heh
            '\uFEE9' or '\uFEEA' or '\uFEEB' or '\uFEEC' => "\u0647",
            // Waw
            '\uFEED' or '\uFEEE' => "\u0648",
            // Alef Maksura
            '\uFEEF' or '\uFEF0' => "\u0649",
            // Yeh
            '\uFEF1' or '\uFEF2' or '\uFEF3' or '\uFEF4' => "\u064A",
            // Lam-Alef ligatures (decompose to two characters)
            '\uFEF5' or '\uFEF6' => "\u0644\u0622", // Lam + Alef with Madda
            '\uFEF7' or '\uFEF8' => "\u0644\u0623", // Lam + Alef with Hamza Above
            '\uFEF9' or '\uFEFA' => "\u0644\u0625", // Lam + Alef with Hamza Below
            '\uFEFB' or '\uFEFC' => "\u0644\u0627", // Lam + Alef

            // Tatweel / kashida
            '\uFE70' => "\u064B", // Fathatan isolated
            '\uFE71' => "\u0640\u064B", // Tatweel + Fathatan
            '\uFE72' => "\u064C", // Dammatan isolated
            '\uFE74' => "\u064D", // Kasratan isolated
            '\uFE76' => "\u064E", // Fatha isolated
            '\uFE77' => "\u0640\u064E", // Tatweel + Fatha
            '\uFE78' => "\u064F", // Damma isolated
            '\uFE79' => "\u0640\u064F", // Tatweel + Damma
            '\uFE7A' => "\u0650", // Kasra isolated
            '\uFE7B' => "\u0640\u0650", // Tatweel + Kasra
            '\uFE7C' => "\u0651", // Shadda isolated
            '\uFE7D' => "\u0640\u0651", // Tatweel + Shadda
            '\uFE7E' => "\u0652", // Sukun isolated
            '\uFE7F' => "\u0640\u0652", // Tatweel + Sukun

            _ => c.ToString(),
        };
    }
}

using System.Text;

namespace Aspose.Pdf.Text;

public sealed partial class ParagraphAbsorber
{
    // Paragraph-lead classifiers of the line grouper: what a line starts with decides
    // whether it can open a paragraph (a capital, a mark before it, a bullet, a numeral).
    private static string LineText(TextLine l) => string.Concat(l.Fragments.Select(f => f.Text));

    /// <returns>The index of the capital (or ideograph) the line leads with, or null when a lower-case letter or a digit comes first.</returns>
    private static int? LeadsWithCapital(string s)
    {
        // Skip whitespace AND non-letter marks (bullets, dashes) — a bullet
        // item "• ESRI shape…" leads with the capital E. Digits stop the scan
        // (T-numeric owns numbered lines). A Han ideograph lead counts as a
        // capital: a CJK paragraph opens on its 2-em first-line indent
        // (measured on a two-column paper — every split lands on
        // an ideograph-led indented line, and same-edge CJK lines never split).
        for (var k = 0; k < s.Length; k++)
        {
            var c = s[k];
            if (char.IsUpper(c) || c is >= '一' and <= '鿿' or >= '㐀' and <= '䶿') return k;
            if (char.IsLetter(c) || char.IsDigit(c)) return null;
        }
        return null;
    }

    private static bool LeadsWithLowercase(string s)
    {
        foreach (var c in s)
        {
            if (char.IsLetter(c)) return char.IsLower(c);
            if (char.IsDigit(c)) return false;
        }
        return true; // no letter or digit at all: never a paragraph lead
    }

    // The lead before the capital is a MARK - an opening quotation mark,
    // bracket, guillemet or apostrophe - and nothing else: a dash ("-Asp.Net")
    // or a bullet glyph leads a list item, and consecutive items stay in one
    // paragraph.
    private static bool LeadsWithMark(string s, int capIdx)
    {
        var any = false;
        for (var k = 0; k < capIdx; k++)
        {
            var c = s[k];
            if (char.IsWhiteSpace(c)) continue;
            var cat = char.GetUnicodeCategory(c);
            var isMark = cat is System.Globalization.UnicodeCategory.OpenPunctuation
                or System.Globalization.UnicodeCategory.InitialQuotePunctuation
                or System.Globalization.UnicodeCategory.FinalQuotePunctuation
                || c is '"' or '\'';
            if (!isMark) return false;
            any = true;
        }
        return any;
    }

    // A bullet-led line opens a hanging list item: its continuation lines sit
    // at the text indent, so the usual first-line-indent trigger does not
    // apply inside it, and leaving the item is itself a paragraph boundary.
    private static bool BulletLead(string s)
    {
        foreach (var c in s)
        {
            if (char.IsWhiteSpace(c)) continue;
            return "■□▪▫●○•‣◦·★☆►▶".IndexOf(c) >= 0;
        }
        return false;
    }

    // First token is a pure number ("1", "12", "1.2", "1,2") and a capital
    // letter follows it on the line.
    private static bool NumericLead(string s)
    {
        var t = s.TrimStart();
        var k = 0;
        while (k < t.Length && (char.IsDigit(t[k]) || ((t[k] == '.' || t[k] == ',')
               && k + 1 < t.Length && char.IsDigit(t[k + 1])))) k++;
        if (k == 0 || (k < t.Length && !char.IsWhiteSpace(t[k]))) return false;
        while (k < t.Length && char.IsWhiteSpace(t[k])) k++;
        return k < t.Length && char.IsUpper(t[k]);
    }
}

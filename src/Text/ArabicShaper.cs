using System.Collections.Generic;
using System.Text;

namespace Aspose.Pdf.Text;

/// <summary>
/// Forward Arabic shaping for text generation: maps base Arabic letters to their
/// contextual presentation forms (Arabic Presentation Forms-B, U+FE70–U+FEFF)
/// according to cursive joining, and forms the lam-alef ligatures. This is the
/// legacy presentation-forms approach (no OpenType GSUB): the resulting code
/// points are present in standard Arabic fonts' cmaps (e.g. Arial), so the CID
/// embedding path can resolve them to the connected glyphs. The reverse of
/// <see cref="ArabicTextNormalizer"/> (which folds forms back to base for extraction).
/// </summary>
internal static class ArabicShaper
{
    // Joining behaviour of a base letter.
    private enum Joining { None, Right, Dual, Causing }

    // Per base char: joining type and the four presentation forms
    // [isolated, final, initial, medial]. Right-joining letters only connect on
    // their right, so they have no initial/medial form — those entries repeat the
    // isolated/final form so lookups are uniform.
    private readonly record struct Forms(Joining Join, char Isolated, char Final, char Initial, char Medial);

    private static readonly Dictionary<char, Forms> Table = new()
    {
        // Hamza — non-joining.
        ['ء'] = new(Joining.None, 'ﺀ', 'ﺀ', 'ﺀ', 'ﺀ'),
        // Alef family — right-joining (connect only to a preceding letter).
        ['آ'] = new(Joining.Right, 'ﺁ', 'ﺂ', 'ﺁ', 'ﺂ'),
        ['أ'] = new(Joining.Right, 'ﺃ', 'ﺄ', 'ﺃ', 'ﺄ'),
        ['ؤ'] = new(Joining.Right, 'ﺅ', 'ﺆ', 'ﺅ', 'ﺆ'),
        ['إ'] = new(Joining.Right, 'ﺇ', 'ﺈ', 'ﺇ', 'ﺈ'),
        ['ئ'] = new(Joining.Dual,  'ﺉ', 'ﺊ', 'ﺋ', 'ﺌ'),
        ['ا'] = new(Joining.Right, 'ﺍ', 'ﺎ', 'ﺍ', 'ﺎ'),
        // Dual-joining letters.
        ['ب'] = new(Joining.Dual,  'ﺏ', 'ﺐ', 'ﺑ', 'ﺒ'), // beh
        ['ة'] = new(Joining.Right, 'ﺓ', 'ﺔ', 'ﺓ', 'ﺔ'), // teh marbuta
        ['ت'] = new(Joining.Dual,  'ﺕ', 'ﺖ', 'ﺗ', 'ﺘ'), // teh
        ['ث'] = new(Joining.Dual,  'ﺙ', 'ﺚ', 'ﺛ', 'ﺜ'), // theh
        ['ج'] = new(Joining.Dual,  'ﺝ', 'ﺞ', 'ﺟ', 'ﺠ'), // jeem
        ['ح'] = new(Joining.Dual,  'ﺡ', 'ﺢ', 'ﺣ', 'ﺤ'), // hah
        ['خ'] = new(Joining.Dual,  'ﺥ', 'ﺦ', 'ﺧ', 'ﺨ'), // khah
        ['د'] = new(Joining.Right, 'ﺩ', 'ﺪ', 'ﺩ', 'ﺪ'), // dal
        ['ذ'] = new(Joining.Right, 'ﺫ', 'ﺬ', 'ﺫ', 'ﺬ'), // thal
        ['ر'] = new(Joining.Right, 'ﺭ', 'ﺮ', 'ﺭ', 'ﺮ'), // reh
        ['ز'] = new(Joining.Right, 'ﺯ', 'ﺰ', 'ﺯ', 'ﺰ'), // zain
        ['س'] = new(Joining.Dual,  'ﺱ', 'ﺲ', 'ﺳ', 'ﺴ'), // seen
        ['ش'] = new(Joining.Dual,  'ﺵ', 'ﺶ', 'ﺷ', 'ﺸ'), // sheen
        ['ص'] = new(Joining.Dual,  'ﺹ', 'ﺺ', 'ﺻ', 'ﺼ'), // sad
        ['ض'] = new(Joining.Dual,  'ﺽ', 'ﺾ', 'ﺿ', 'ﻀ'), // dad
        ['ط'] = new(Joining.Dual,  'ﻁ', 'ﻂ', 'ﻃ', 'ﻄ'), // tah
        ['ظ'] = new(Joining.Dual,  'ﻅ', 'ﻆ', 'ﻇ', 'ﻈ'), // zah
        ['ع'] = new(Joining.Dual,  'ﻉ', 'ﻊ', 'ﻋ', 'ﻌ'), // ain
        ['غ'] = new(Joining.Dual,  'ﻍ', 'ﻎ', 'ﻏ', 'ﻐ'), // ghain
        ['ف'] = new(Joining.Dual,  'ﻑ', 'ﻒ', 'ﻓ', 'ﻔ'), // feh
        ['ق'] = new(Joining.Dual,  'ﻕ', 'ﻖ', 'ﻗ', 'ﻘ'), // qaf
        ['ك'] = new(Joining.Dual,  'ﻙ', 'ﻚ', 'ﻛ', 'ﻜ'), // kaf
        ['ل'] = new(Joining.Dual,  'ﻝ', 'ﻞ', 'ﻟ', 'ﻠ'), // lam
        ['م'] = new(Joining.Dual,  'ﻡ', 'ﻢ', 'ﻣ', 'ﻤ'), // meem
        ['ن'] = new(Joining.Dual,  'ﻥ', 'ﻦ', 'ﻧ', 'ﻨ'), // noon
        ['ه'] = new(Joining.Dual,  'ﻩ', 'ﻪ', 'ﻫ', 'ﻬ'), // heh
        ['و'] = new(Joining.Right, 'ﻭ', 'ﻮ', 'ﻭ', 'ﻮ'), // waw
        ['ى'] = new(Joining.Dual,  'ﻯ', 'ﻰ', 'ﯨ', 'ﯩ'), // alef maksura
        ['ي'] = new(Joining.Dual,  'ﻱ', 'ﻲ', 'ﻳ', 'ﻴ'), // yeh
    };

    // Lam (0644) + Alef variant → ligature [isolated, final].
    private static readonly Dictionary<char, (char iso, char fin)> LamAlef = new()
    {
        ['آ'] = ('ﻵ', 'ﻶ'),
        ['أ'] = ('ﻷ', 'ﻸ'),
        ['إ'] = ('ﻹ', 'ﻺ'),
        ['ا'] = ('ﻻ', 'ﻼ'),
    };

    // Isolated presentation form -> the letter it is the isolated shape of.
    private static readonly Dictionary<char, char> IsolatedBase = BuildIsolatedBase();

    private static Dictionary<char, char> BuildIsolatedBase()
    {
        var map = new Dictionary<char, char>();
        foreach (var entry in Table) map[entry.Value.Isolated] = entry.Key;
        return map;
    }

    /// <summary>The base letter an isolated presentation form stands for, or NUL. A face's
    /// nominal glyph for a letter is its isolated shape, so a face that maps only the base
    /// letters still draws the isolated form exactly.</summary>
    internal static char BaseOfIsolated(char c) => IsolatedBase.TryGetValue(c, out var b) ? b : '\0';

    /// <summary>The contextual shapes of Arabic Presentation Forms-B in the order the AFII glyph names number
    /// them: letter by letter in the order of their isolated forms, each letter's initial, medial and final
    /// shapes the block holds (its isolated shape is the letter's own name), then the isolated and final shape
    /// of each lam-alef ligature.</summary>
    internal static IEnumerable<char> ContextualFormsInAfiiOrder()
    {
        foreach (var f in Table.Values.OrderBy(f => f.Isolated))
            foreach (var form in new[] { f.Initial, f.Medial, f.Final }.Distinct())
                if (form != f.Isolated && form is >= PresentationFormsBFirst and <= PresentationFormsBLast) yield return form;
        foreach (var (iso, fin) in LamAlef.Values.OrderBy(l => l.iso))
        {
            yield return iso;
            yield return fin;
        }
    }

    private const char PresentationFormsBFirst = '\uFE70';
    private const char PresentationFormsBLast = '\uFEFF';

    /// <summary>True when the string contains any base Arabic letter that needs shaping.</summary>
    public static bool ContainsArabic(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        return text.Any(c => c >= '؀' && c <= 'ۿ');
    }

    /// <summary>Shape Arabic to its contextual presentation forms and reorder the
    /// result into visual (left-to-right display) order, ready to be encoded as glyphs
    /// in a generated content stream. Shaping runs first, in logical order, so cursive
    /// joining is resolved from each letter's logical neighbours; the shaped run is then
    /// reordered with <see cref="BidiReorderer"/> so the connected glyphs are emitted in
    /// the order they appear on the page. Non-Arabic text passes through unchanged.</summary>
    public static string ShapeForDisplay(string text)
    {
        if (string.IsNullOrEmpty(text) || !ContainsArabic(text)) return text;
        return BidiReorderer.ReorderIfNeeded(Shape(text));
    }

    /// <summary>Like <see cref="ShapeForDisplay"/> for a paragraph that reads left to
    /// right whatever it starts with: a generated fragment keeps its segments in order,
    /// each Arabic one flipped in place.</summary>
    public static string ShapeForLtrParagraph(string text)
    {
        if (string.IsNullOrEmpty(text) || !ContainsArabic(text)) return text;
        return BidiReorderer.ReorderOnLtrParagraph(Shape(text));
    }

    /// <summary>Replace base Arabic letters with their contextual presentation forms
    /// and form lam-alef ligatures. Non-Arabic characters pass through unchanged.</summary>
    public static string Shape(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var chars = text.ToCharArray();
        var n = chars.Length;
        var sb = new StringBuilder(n);

        int i = 0;
        while (i < n)
        {
            char c = chars[i];
            if (!Table.TryGetValue(c, out var f)) { sb.Append(c); i++; continue; }

            // Lam-alef ligature: a lam (dual) immediately followed by an alef variant.
            if (c == 'ل' && i + 1 < n && LamAlef.TryGetValue(chars[i + 1], out var lig))
            {
                bool connectsBeforeLig = JoinsLeftToRight(PrevJoining(chars, i));
                sb.Append(connectsBeforeLig ? lig.fin : lig.iso);
                i += 2; // the lam and the alef make one ligature
                continue;
            }

            bool joinPrev = f.Join is Joining.Dual or Joining.Right && JoinsLeftToRight(PrevJoining(chars, i));
            bool joinNext = f.Join is Joining.Dual && NextJoinsRight(chars, i);

            char shaped;
            if (joinPrev && joinNext) shaped = f.Medial;
            else if (joinNext) shaped = f.Initial;
            else if (joinPrev) shaped = f.Final;
            else shaped = f.Isolated;
            sb.Append(shaped);
            i++;
        }
        return sb.ToString();
    }

    // The joining type of the previous shaped letter (skipping marks).
    private static Joining PrevJoining(char[] chars, int i)
    {
        for (int j = i - 1; j >= 0; j--)
        {
            if (IsTransparent(chars[j])) continue;
            return Table.TryGetValue(chars[j], out var pf) ? pf.Join : Joining.None;
        }
        return Joining.None;
    }

    // Whether the next non-mark letter can join to its right (dual/right-joining),
    // i.e. it accepts a connection from the current letter on the current letter's left.
    private static bool NextJoinsRight(char[] chars, int i)
    {
        for (int j = i + 1; j < chars.Length; j++)
        {
            if (IsTransparent(chars[j])) continue;
            return Table.TryGetValue(chars[j], out var nf) && nf.Join is Joining.Dual or Joining.Right;
        }
        return false;
    }

    // A preceding letter contributes a left-connection when it is dual-joining
    // (right/dual letters connect on their right edge to the following letter).
    private static bool JoinsLeftToRight(Joining prev) => prev == Joining.Dual;

    // Combining marks (harakat) are transparent to joining.
    private static bool IsTransparent(char c) => c >= 'ً' && c <= 'ْ';
}

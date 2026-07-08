using System.Collections.Generic;
using System.Text;

namespace Aspose.Pdf.Text;

/// <summary>
/// Minimal Arabic text shaper. PDF content streams carry already-positioned glyphs (the
/// renderer applies no OpenType GSUB), so Arabic written as plain Unicode base letters would
/// render as disconnected isolated forms in logical (left-to-right) order. This shaper turns a
/// run of Arabic base letters (U+0600 block) into the contextual presentation forms
/// (U+FE70–U+FEFF, Arabic Presentation Forms-B) that an Arabic-capable font maps through its
/// cmap, applies the required lam-alef ligatures, and reverses the run to visual right-to-left
/// order. Non-Arabic characters are passed through; embedded Latin/digit runs keep their order.
/// </summary>
internal static class ArabicTextShaper
{
    /// <summary>Joining behaviour of an Arabic letter.</summary>
    private enum Join { None, Right, Dual }

    /// <summary>Per base-letter shaping data: its joining behaviour and the
    /// [isolated, final, initial, medial] presentation-form code points (0 = no such form).</summary>
    private readonly struct LetterShape
    {
        public readonly Join Join;
        public readonly char Iso, Fin, Ini, Med;
        public LetterShape(Join join, int iso, int fin, int ini, int med)
        {
            Join = join; Iso = (char)iso; Fin = (char)fin; Ini = (char)ini; Med = (char)med;
        }
    }

    // Arabic base letter (U+0621..U+064A) → shaping data. Right-joining letters carry only
    // isolated + final forms (initial/medial = 0); dual-joining letters carry all four.
    private static readonly Dictionary<char, LetterShape> Letters = new()
    {
        ['ء'] = new LetterShape(Join.None, 0xFE80, 0, 0, 0),                       // HAMZA
        ['آ'] = new LetterShape(Join.Right, 0xFE81, 0xFE82, 0, 0),                 // ALEF MADDA
        ['أ'] = new LetterShape(Join.Right, 0xFE83, 0xFE84, 0, 0),                 // ALEF HAMZA ABOVE
        ['ؤ'] = new LetterShape(Join.Right, 0xFE85, 0xFE86, 0, 0),                 // WAW HAMZA
        ['إ'] = new LetterShape(Join.Right, 0xFE87, 0xFE88, 0, 0),                 // ALEF HAMZA BELOW
        ['ئ'] = new LetterShape(Join.Dual, 0xFE89, 0xFE8A, 0xFE8B, 0xFE8C),        // YEH HAMZA
        ['ا'] = new LetterShape(Join.Right, 0xFE8D, 0xFE8E, 0, 0),                 // ALEF
        ['ب'] = new LetterShape(Join.Dual, 0xFE8F, 0xFE90, 0xFE91, 0xFE92),        // BEH
        ['ة'] = new LetterShape(Join.Right, 0xFE93, 0xFE94, 0, 0),                 // TEH MARBUTA
        ['ت'] = new LetterShape(Join.Dual, 0xFE95, 0xFE96, 0xFE97, 0xFE98),        // TEH
        ['ث'] = new LetterShape(Join.Dual, 0xFE99, 0xFE9A, 0xFE9B, 0xFE9C),        // THEH
        ['ج'] = new LetterShape(Join.Dual, 0xFE9D, 0xFE9E, 0xFE9F, 0xFEA0),        // JEEM
        ['ح'] = new LetterShape(Join.Dual, 0xFEA1, 0xFEA2, 0xFEA3, 0xFEA4),        // HAH
        ['خ'] = new LetterShape(Join.Dual, 0xFEA5, 0xFEA6, 0xFEA7, 0xFEA8),        // KHAH
        ['د'] = new LetterShape(Join.Right, 0xFEA9, 0xFEAA, 0, 0),                 // DAL
        ['ذ'] = new LetterShape(Join.Right, 0xFEAB, 0xFEAC, 0, 0),                 // THAL
        ['ر'] = new LetterShape(Join.Right, 0xFEAD, 0xFEAE, 0, 0),                 // REH
        ['ز'] = new LetterShape(Join.Right, 0xFEAF, 0xFEB0, 0, 0),                 // ZAIN
        ['س'] = new LetterShape(Join.Dual, 0xFEB1, 0xFEB2, 0xFEB3, 0xFEB4),        // SEEN
        ['ش'] = new LetterShape(Join.Dual, 0xFEB5, 0xFEB6, 0xFEB7, 0xFEB8),        // SHEEN
        ['ص'] = new LetterShape(Join.Dual, 0xFEB9, 0xFEBA, 0xFEBB, 0xFEBC),        // SAD
        ['ض'] = new LetterShape(Join.Dual, 0xFEBD, 0xFEBE, 0xFEBF, 0xFEC0),        // DAD
        ['ط'] = new LetterShape(Join.Dual, 0xFEC1, 0xFEC2, 0xFEC3, 0xFEC4),        // TAH
        ['ظ'] = new LetterShape(Join.Dual, 0xFEC5, 0xFEC6, 0xFEC7, 0xFEC8),        // ZAH
        ['ع'] = new LetterShape(Join.Dual, 0xFEC9, 0xFECA, 0xFECB, 0xFECC),        // AIN
        ['غ'] = new LetterShape(Join.Dual, 0xFECD, 0xFECE, 0xFECF, 0xFED0),        // GHAIN
        ['ف'] = new LetterShape(Join.Dual, 0xFED1, 0xFED2, 0xFED3, 0xFED4),        // FEH
        ['ق'] = new LetterShape(Join.Dual, 0xFED5, 0xFED6, 0xFED7, 0xFED8),        // QAF
        ['ك'] = new LetterShape(Join.Dual, 0xFED9, 0xFEDA, 0xFEDB, 0xFEDC),        // KAF
        ['ل'] = new LetterShape(Join.Dual, 0xFEDD, 0xFEDE, 0xFEDF, 0xFEE0),        // LAM
        ['م'] = new LetterShape(Join.Dual, 0xFEE1, 0xFEE2, 0xFEE3, 0xFEE4),        // MEEM
        ['ن'] = new LetterShape(Join.Dual, 0xFEE5, 0xFEE6, 0xFEE7, 0xFEE8),        // NOON
        ['ه'] = new LetterShape(Join.Dual, 0xFEE9, 0xFEEA, 0xFEEB, 0xFEEC),        // HEH
        ['و'] = new LetterShape(Join.Right, 0xFEED, 0xFEEE, 0, 0),                 // WAW
        ['ى'] = new LetterShape(Join.Right, 0xFEEF, 0xFEF0, 0, 0),                 // ALEF MAKSURA
        ['ي'] = new LetterShape(Join.Dual, 0xFEF1, 0xFEF2, 0xFEF3, 0xFEF4),        // YEH
    };

    // LAM + ALEF-variant → [isolated ligature, final ligature].
    private static readonly Dictionary<char, (char iso, char fin)> LamAlef = new()
    {
        ['آ'] = ('ﻵ', 'ﻶ'), // LAM + ALEF MADDA
        ['أ'] = ('ﻷ', 'ﻸ'), // LAM + ALEF HAMZA ABOVE
        ['إ'] = ('ﻹ', 'ﻺ'), // LAM + ALEF HAMZA BELOW
        ['ا'] = ('ﻻ', 'ﻼ'), // LAM + ALEF
    };

    /// <summary>Arabic combining marks (harakat) are transparent to joining.</summary>
    private static bool IsTransparent(char c) =>
        (c >= 'ً' && c <= 'ٟ') || c == 'ٰ' || (c >= 'ۖ' && c <= 'ۭ');

    private static bool IsArabicLetter(char c) => Letters.ContainsKey(c);

    /// <summary>True if the run contains at least one shapeable Arabic letter.</summary>
    public static bool ContainsArabic(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (var c in text)
            if (IsArabicLetter(c)) return true;
        return false;
    }

    /// <summary>
    /// Shape <paramref name="text"/>: replace Arabic base letters with their contextual
    /// presentation forms, fuse lam-alef pairs, and emit the result in visual right-to-left
    /// order. Runs of non-Arabic characters (Latin, digits, punctuation) keep their logical
    /// order but are repositioned as a unit within the RTL flow.
    /// </summary>
    public static string Shape(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // 1) Resolve each Arabic letter to its contextual presentation form (logical order),
        //    fusing lam + alef into a single ligature. Non-Arabic chars pass through.
        var forms = new List<char>(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (!IsArabicLetter(c)) { forms.Add(c); continue; }

            // lam-alef ligature: a LAM directly followed by an ALEF variant.
            if (c == 'ل' && i + 1 < text.Length && LamAlef.TryGetValue(text[i + 1], out var lig))
            {
                var prevJoinsLig = JoinsToPrev(text, i);
                forms.Add(prevJoinsLig ? lig.fin : lig.iso);
                i++; // consume the alef
                continue;
            }

            LetterShape sh = Letters[c];
            var joinPrev = JoinsToPrev(text, i);
            var joinNext = JoinsToNext(text, i);
            char form = (joinPrev, joinNext) switch
            {
                (true, true) when sh.Med != 0 => sh.Med,
                (true, false) when sh.Fin != 0 => sh.Fin,
                (false, true) when sh.Ini != 0 => sh.Ini,
                (true, _) when sh.Fin != 0 => sh.Fin,   // right-joining: final when attached
                _ => sh.Iso,
            };
            forms.Add(form);
        }

        // 2) Reorder logical → visual with a 2-level bidi pass (Unicode rule L2). The base
        //    direction is taken from the first strong character: an all-Arabic / Arabic-first
        //    run lays out right-to-left, while Arabic embedded in English (base LTR) keeps the
        //    English in order and only mirrors the Arabic sub-run in place.
        return ReorderBidi(forms);
    }

    // The previous (towards lower index) non-transparent letter can connect on its left side,
    // and the current letter can connect on its right side (dual or right-joining).
    private static bool JoinsToPrev(string text, int i)
    {
        var sh = Letters[text[i]];
        if (sh.Join != Join.Dual && sh.Join != Join.Right) return false;
        for (var j = i - 1; j >= 0; j--)
        {
            if (IsTransparent(text[j])) continue;
            return Letters.TryGetValue(text[j], out var p) && p.Join == Join.Dual;
        }
        return false;
    }

    // The next (towards higher index) non-transparent letter can connect on its right side,
    // and the current letter can connect on its left side (dual-joining only).
    private static bool JoinsToNext(string text, int i)
    {
        var sh = Letters[text[i]];
        if (sh.Join != Join.Dual) return false;
        for (var j = i + 1; j < text.Length; j++)
        {
            if (IsTransparent(text[j])) continue;
            if (!Letters.TryGetValue(text[j], out var n)) return false;
            return n.Join == Join.Dual || n.Join == Join.Right;
        }
        return false;
    }

    /// <summary>Reorder a logical-order glyph list to visual order with a 2-level bidi pass
    /// (Unicode rule L2). Strong directions: Arabic = R, Latin/digit = L; neutrals resolve to
    /// the surrounding strong direction (or the base when they straddle a boundary). Levels: in
    /// an LTR base, L=0/R=1; in an RTL base, R=1/L=2. Then each maximal run at level ≥ k is
    /// reversed for k from the highest level down to 1.</summary>
    private static string ReorderBidi(List<char> forms)
    {
        var n = forms.Count;
        if (n == 0) return string.Empty;

        // Strong direction: +1 = R (Arabic), -1 = L (Latin/digit), 0 = neutral.
        var dir = new int[n];
        for (var i = 0; i < n; i++)
            dir[i] = IsArabicVisual(forms[i]) ? 1 : IsLatinOrDigit(forms[i]) ? -1 : 0;

        // Base direction from the first strong char (default LTR).
        var baseDir = -1;
        for (var i = 0; i < n; i++) if (dir[i] != 0) { baseDir = dir[i]; break; }

        // Resolve neutrals (N1/N2): same strong on both sides → that direction, else base.
        var level = new int[n];
        for (var i = 0; i < n; i++)
        {
            var d = dir[i];
            if (d == 0)
            {
                var prev = 0; for (var j = i - 1; j >= 0; j--) if (dir[j] != 0) { prev = dir[j]; break; }
                var next = 0; for (var j = i + 1; j < n; j++) if (dir[j] != 0) { next = dir[j]; break; }
                d = (prev != 0 && prev == next) ? prev : baseDir;
            }
            // LTR base: L→0, R→1.  RTL base: R→1, L→2.
            level[i] = baseDir == 1 ? (d == 1 ? 1 : 2) : (d == 1 ? 1 : 0);
        }

        var visual = new List<char>(forms);
        var maxLevel = 0; foreach (var l in level) if (l > maxLevel) maxLevel = l;
        for (var lvl = maxLevel; lvl >= 1; lvl--)
        {
            var i = 0;
            while (i < n)
            {
                if (level[i] < lvl) { i++; continue; }
                var s = i;
                while (i < n && level[i] >= lvl) i++;
                visual.Reverse(s, i - s);
            }
        }
        return new string(visual.ToArray());
    }

    private static bool IsLatinOrDigit(char c) =>
        (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

    /// <summary>True for a character that lays out right-to-left in the visual pass: any
    /// Arabic-block code point (letters, harakat, Arabic punctuation) or an Arabic
    /// presentation form (Forms-A / Forms-B).</summary>
    private static bool IsArabicVisual(char c)
    {
        int u = c;
        return (u >= 0x0600 && u <= 0x06FF)   // Arabic block (letters, harakat, punctuation)
            || (u >= 0xFB50 && u <= 0xFDFF)   // Arabic Presentation Forms-A
            || (u >= 0xFE70 && u <= 0xFEFF);  // Arabic Presentation Forms-B
    }
}

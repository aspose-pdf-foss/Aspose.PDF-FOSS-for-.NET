using System;
using System.Collections.Generic;

namespace Aspose.Pdf.Converters.PsToPdf;

/// <summary>
/// Composite fonts: a root font that carries no glyphs of its own but a vector of
/// descendant fonts and a rule for deciding which one each character belongs to. The
/// escape rule, the one this corpus uses, reads a designated byte followed by an index
/// to switch descendants, and every byte after that is a character in the chosen one.
/// </summary>
internal static class PsCompositeFonts
{
    /// <summary>The font type whose glyphs live in descendant fonts.</summary>
    private const int CompositeFontType = 0;

    /// <summary>The mapping rule that switches descendants with an escape byte.</summary>
    private const int EscapeMapType = 3;

    /// <summary>The escape byte a font uses when it names none.</summary>
    private const int DefaultEscapeChar = 255;

    /// <summary>Whether this font delegates its glyphs to descendants.</summary>
    public static bool Applies(PsFontInstance font)
    {
        if (font?.Dict is null) return false;
        if (!font.Dict.TryGet("FontType", out var type) || type.ToInt() != CompositeFontType)
            return false;
        return font.Dict.TryGet("FDepVector", out var v) && v.AsArray != null;
    }

    /// <summary>Show a string through a composite font, returning the advance it
    /// consumed. Each character is drawn by the descendant it belongs to, with the
    /// ROOT font on hand, because a descendant's own procedure commonly reaches back
    /// to the root for the drawing it shares with its siblings.</summary>
    public static double Show(PsInterpreter i, PsFontInstance font, PsString text,
        double startX, double startY)
    {
        var descendants = font.Dict.TryGet("FDepVector", out var v) ? v.AsArray : null;
        if (descendants is null || descendants.Length == 0) return 0;
        var previousRoot = i.RootFont;
        i.RootFont = font.Dict;
        try
        {
            return ShowMapped(i, font, text, descendants, startX, startY);
        }
        finally
        {
            i.RootFont = previousRoot;
        }
    }

    private static double ShowMapped(PsInterpreter i, PsFontInstance font, PsString text,
        PsArray descendants, double startX, double startY)
    {
        var mapType = font.Dict.TryGet("FMapType", out var m) && m.IsNumber ? m.ToInt() : EscapeMapType;
        var encoding = font.Dict.TryGet("Encoding", out var enc) ? enc.AsArray : null;
        return mapType == PairMapType
            ? ShowPairs(i, font, text, descendants, encoding, startX, startY)
            : ShowEscaped(i, font, text, descendants, encoding, startX, startY);
    }

    /// <summary>The escape rule: a marker byte and an index switch descendants, and
    /// every byte after that is a character in the one chosen.</summary>
    private static double ShowEscaped(PsInterpreter i, PsFontInstance font, PsString text,
        PsArray descendants, PsArray? encoding, double startX, double startY)
    {
        var escape = font.Dict.TryGet("EscChar", out var e) && e.IsNumber
            ? e.ToInt()
            : DefaultEscapeChar;
        var pen = 0.0;
        var chosen = 0;
        for (var k = 0; k < text.Length; k++)
        {
            if (text[k] == escape && k + 1 < text.Length)
            {
                chosen = IndexFor(encoding, text[++k], descendants.Length);
                continue;
            }

            pen += DrawWith(i, font, descendants, chosen, text[k], startX + pen, startY);
        }

        return pen;
    }

    /// <summary>The two-byte rule: the first byte of each pair selects the descendant
    /// through the root's encoding and the second is the character in it.</summary>
    private static double ShowPairs(PsInterpreter i, PsFontInstance font, PsString text,
        PsArray descendants, PsArray? encoding, double startX, double startY)
    {
        var pen = 0.0;
        for (var k = 0; k + 1 < text.Length; k += 2)
        {
            var chosen = IndexFor(encoding, text[k], descendants.Length);
            pen += DrawWith(i, font, descendants, chosen, text[k + 1], startX + pen, startY);
        }

        return pen;
    }

    private static double DrawWith(PsInterpreter i, PsFontInstance font, PsArray descendants,
        int chosen, byte code, double x, double y)
    {
        var descendant = Descendant(i, font, descendants, chosen);
        return descendant is null ? 0 : DrawOne(i, descendant, code, x, y);
    }

    /// <summary>The mapping rule that reads the string as two-byte pairs.</summary>
    private const int PairMapType = 2;

    /// <summary>The descendant a selector byte names, through the root's own encoding
    /// where it has one.</summary>
    private static int IndexFor(PsArray? encoding, int selector, int count)
    {
        var index = selector;
        if (encoding != null && selector >= 0 && selector < encoding.Length &&
            encoding[selector].IsNumber)
            index = encoding[selector].ToInt();
        return index >= 0 && index < count ? index : 0;
    }

    /// <summary>One descendant, carrying the root's scale so the character is drawn at
    /// the size the program set on the composite.</summary>
    private static PsFontInstance? Descendant(PsInterpreter i, PsFontInstance root,
        PsArray descendants, int index)
    {
        var dict = descendants[index].AsDictionary;
        if (dict is null) return null;
        var encoding = dict.TryGet("Encoding", out var e) ? e.AsArray : null;
        var name = dict.TryGet("FontName", out var n) ? n.Text : root.Name;
        var face = dict.TryGet(PsInterpreter.FaceKey, out var f) ? f.Text : null;
        return new PsFontInstance(name, dict, root.Matrix, encoding, face);
    }

    /// <summary>Draw one character with a descendant, whichever way it carries its
    /// glyphs, and report the advance in user space.</summary>
    private static double DrawOne(PsInterpreter i, PsFontInstance descendant, byte code,
        double x, double y)
    {
        var one = new PsString(new[] { code });
        if (PsBuiltGlyphs.Applies(descendant))
            return PsBuiltGlyphs.Show(i, descendant, one, x, y);
        return PsInterpreter.PaintString(i, descendant, one, x, y);
    }
}

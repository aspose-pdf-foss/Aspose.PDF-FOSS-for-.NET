using System;

namespace Aspose.Pdf.Converters.PsToPdf;

/// <summary>
/// Fonts the PostScript program builds itself. A type-3 font carries a procedure that
/// draws each glyph with ordinary graphics operators, so a character is shown by
/// running that procedure under the right transformation and letting the marks it
/// makes land on the page like any other painting — which is also what the reference
/// converter does for these faces.
/// </summary>
internal static class PsBuiltGlyphs
{
    /// <summary>The font type whose glyphs are procedures.</summary>
    private const int ProcedureFontType = 3;

    /// <summary>How deep a glyph procedure may nest before it is refused, so a font
    /// that shows text from inside its own glyph cannot recurse without end.</summary>
    private const int MaxDepth = 4;

    /// <summary>Whether this font draws its glyphs with a procedure.</summary>
    public static bool Applies(PsFontInstance font)
    {
        if (font?.Dict is null) return false;
        if (!font.Dict.TryGet("FontType", out var type) || type.ToInt() != ProcedureFontType)
            return false;
        return Procedure(font, out _, out _);
    }

    /// <summary>The drawing procedure and whether it is given a glyph name rather
    /// than a character code. The level-2 spelling is preferred when both exist.</summary>
    private static bool Procedure(PsFontInstance font, out PsValue proc, out bool byName)
    {
        if (font.Dict.TryGet("BuildGlyph", out proc) && proc.AsArray != null)
        {
            byName = true;
            return true;
        }

        byName = false;
        return font.Dict.TryGet("BuildChar", out proc) && proc.AsArray != null;
    }

    /// <summary>Draw a string with a procedure-built font, returning the advance it
    /// consumed in user space.</summary>
    public static double Show(PsInterpreter i, PsFontInstance font, PsString text,
        double startX, double startY)
    {
        if (!Procedure(font, out var proc, out var byName)) return 0;
        if (i.GlyphDepth >= MaxDepth) return 0;
        var glyphToUser = FontMatrix(font).Concat(font.Matrix);
        var pen = 0.0;
        for (var k = 0; k < text.Length; k++)
        {
            pen += DrawOne(i, font, proc, byName, text[k], glyphToUser,
                startX + pen, startY);
        }

        return pen;
    }

    /// <summary>Run one glyph's procedure and report the advance it declared.</summary>
    private static double DrawOne(PsInterpreter i, PsFontInstance font, PsValue proc,
        bool byName, byte code, PsMatrix glyphToUser, double x, double y)
    {
        var graphics = i.Graphics;
        var savedCtm = graphics.State.Ctm;
        var savedPath = graphics.Path;
        graphics.GSave();
        graphics.State.Ctm = glyphToUser
            .Concat(PsMatrix.Translation(x, y))
            .Concat(savedCtm);
        graphics.SetPath(new PsPath());
        i.GlyphAdvance = 0;
        i.GlyphDepth++;
        try
        {
            i.Push(PsValue.Font(font.Dict));
            i.Push(byName
                ? PsValue.LiteralName(PsEncodings.Lookup(font.Encoding, code))
                : PsValue.Int(code));
            i.Invoke(proc);
        }
        catch (PsErrorSignal e)
        {
            i.RecordError(e.ErrorName);
        }
        finally
        {
            i.GlyphDepth--;
            graphics.GRestore();
            graphics.State.Ctm = savedCtm;
            graphics.SetPath(savedPath);
        }

        // The declared advance is in glyph space; the pen moves in user space.
        glyphToUser.TransformDelta(i.GlyphAdvance, 0, out var dx, out _);
        return dx;
    }

    /// <summary>The font's own matrix, mapping glyph space to the font's text space.
    /// A font that declares none uses the thousandth-of-an-em convention.</summary>
    private static PsMatrix FontMatrix(PsFontInstance font)
    {
        if (font.Dict.TryGet("FontMatrix", out var m) && m.AsArray != null &&
            m.AsArray.Length >= PsMatrix.ElementCount)
            return PsInterpreter.MatrixFromArray(m.AsArray);
        const double DefaultUnitsPerEm = 1000.0;
        return PsMatrix.Scaling(1 / DefaultUnitsPerEm, 1 / DefaultUnitsPerEm);
    }
}

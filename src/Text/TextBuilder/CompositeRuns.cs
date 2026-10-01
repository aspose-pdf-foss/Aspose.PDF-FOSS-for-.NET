using Aspose.Pdf.Content;

namespace Aspose.Pdf.Text;

public sealed partial class TextBuilder
{
    /// <summary>Thousandths of the em: the unit of a TJ adjustment.</summary>
    private const double TjUnitsPerEm = 1000;

    /// <summary>The TJ adjustments that give a composite (Type0) font its word spacing. The
    /// Tw operator reaches only the single-byte code 32, so a run shown as two-byte glyph ids
    /// carries the spacing as a move after every space glyph, in thousandths of the em
    /// (negative moves the pen on), the trailing space's included. Null when there is no
    /// spacing to carry or when the glyphs are not one per character (a shaped run), which
    /// keeps the plain show.</summary>
    internal static double[]? CompositeWordSpacing(string text, byte[] glyphIds, double wordSpacing, double fontSize)
    {
        if (wordSpacing == 0 || fontSize <= 0 || text.Length == 0) return null;
        var adjustments = new List<double>(text.Length);
        var any = false;
        for (var i = 0; i < text.Length; i++)
        {
            var space = text[i] == ' ';
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++;
            adjustments.Add(space ? -wordSpacing * TjUnitsPerEm / fontSize : 0);
            any |= space;
        }
        if (!any || adjustments.Count * 2 != glyphIds.Length) return null;
        return adjustments.ToArray();
    }

    /// <summary>Shows a composite run: as one hex string, or as a TJ array when it carries
    /// its word spacing glyph by glyph.</summary>
    private static void ShowComposite(ContentStreamBuilder builder, byte[] glyphIds, double[]? wordSpacing)
    {
        if (wordSpacing is null) builder.ShowTextHex(glyphIds);
        else builder.ShowTextHexKerned(glyphIds, wordSpacing);
    }

    /// <summary>The text matrix's shear a state asks for: <c>b</c> from its slope, <c>c</c>
    /// from its skew and its synthetic slant (<see cref="TextFormattingOptions.Skew"/>).</summary>
    internal static (double Slope, double Skew) ShearOf(TextState state) =>
        state.FormattingOptions is { } o ? (o.Slope, o.Skew + o.SyntheticItalicLean) : (0, 0);
}

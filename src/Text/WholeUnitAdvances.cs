using System.Runtime.CompilerServices;

namespace Aspose.Pdf.Text;

/// <summary>The face programs whose advances are taken in WHOLE thousandths of an em,
/// truncated -- the convention of a producer that keeps a face's metrics as integers per
/// mille. Such a face is measured, and its /W written, in those whole units, so the layout
/// and every reader place its glyphs alike. Every other face keeps its exact advances (see
/// <c>Type0FontEmbedder.PdfWidth</c> for why the fraction is worth carrying).</summary>
internal static class WholeUnitAdvances
{
    private static readonly object Marker = new();
    private static readonly ConditionalWeakTable<byte[], object> Programs = new();

    /// <summary>Takes <paramref name="program"/>'s advances in whole units from now on.</summary>
    internal static void Mark(byte[] program) => Programs.GetValue(program, static _ => Marker);

    /// <summary>Whether <paramref name="program"/>'s advances are taken in whole units.</summary>
    internal static bool Holds(byte[]? program) => program is not null && Programs.TryGetValue(program, out _);

    /// <summary>An advance of <paramref name="advance"/> font units in thousandths of an em:
    /// exact, or truncated to a whole unit when <paramref name="whole"/>.</summary>
    internal static double PerMille(double advance, double unitsPerEm, bool whole)
    {
        var perMille = advance * 1000.0 / unitsPerEm;
        return whole ? System.Math.Truncate(perMille) : perMille;
    }
}

namespace Aspose.Pdf.Security;

/// <summary>
/// Small integer math helpers shared by the security primitives.
/// </summary>
public static class MathExtensions
{
    /// <summary>
    /// Floored modulo: returns a result with the sign of <paramref name="modulus"/>,
    /// so <c>(-11).Mod(5) == 4</c> rather than C#'s remainder <c>-1</c>. Assumes a
    /// positive <paramref name="modulus"/>.
    /// </summary>
    public static int Mod(this int value, int modulus)
    {
        return ((value % modulus) + modulus) % modulus;
    }
}

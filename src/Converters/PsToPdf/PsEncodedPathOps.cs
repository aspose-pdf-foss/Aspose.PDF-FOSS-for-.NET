namespace Aspose.Pdf.Converters.PsToPdf;

/// <summary>
/// The operator numbers an encoded user path uses in place of names. A path written
/// that way carries its operands in an array and its operators in a string of these
/// numbers, so a program can build one without the scanner ever seeing a name.
/// </summary>
internal sealed class PsEncodedPathOps
{
    private PsEncodedPathOps(string name, int operands)
    {
        Name = name;
        Operands = operands;
    }

    /// <summary>The operator this number stands for.</summary>
    public string Name { get; }

    /// <summary>How many numbers it consumes.</summary>
    public int Operands { get; }

    private static readonly PsEncodedPathOps[] ByCode =
    {
        new("setbbox", 4),      // 0
        new("moveto", 2),       // 1
        new("rmoveto", 2),      // 2
        new("lineto", 2),       // 3
        new("rlineto", 2),      // 4
        new("curveto", 6),      // 5
        new("rcurveto", 6),     // 6
        new("arc", 5),          // 7
        new("arcn", 5),         // 8
        new("arct", 5),         // 9
        new("closepath", 0),    // 10
        new("ucache", 0),       // 11
    };

    /// <summary>What an operator number means, or null when it names nothing.</summary>
    public static PsEncodedPathOps? For(int code) =>
        code >= 0 && code < ByCode.Length ? ByCode[code] : null;
}

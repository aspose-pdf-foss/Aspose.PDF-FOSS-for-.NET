namespace Aspose.Pdf.Text;

/// <summary>
/// How the lowest Y coordinate of a text fragment is interpreted in
/// positioning APIs (<see cref="TextState.CoordinateOrigin"/> and
/// per-segment SetPosition overloads).
/// </summary>
public enum CoordinateOrigin
{
    /// <summary>Y is the text baseline.</summary>
    BaseLine = 0,
    /// <summary>Y is the lowest descender of the glyphs.</summary>
    Descender = 1,
}

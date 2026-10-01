namespace Aspose.Pdf.Facades;

/// <summary>
/// Horizontal-alignment selector for legacy facade APIs (e.g. <see cref="PdfPageEditor"/>).
/// Type-safe enum: only the static instances <see cref="Left"/>, <see cref="Center"/>,
/// <see cref="Right"/> are valid. New code
/// should prefer <see cref="Aspose.Pdf.HorizontalAlignment"/>.
/// </summary>
public sealed class AlignmentType
{
    /// <summary>Gets the alignment name, such as <c>Left</c>, <c>Center</c> or <c>Right</c>.</summary>
    public string Name { get; }

    /// <summary>Creates an alignment value with the given name; a null name becomes an empty string.</summary>
    public AlignmentType(string name) => Name = name ?? "";

    public static readonly AlignmentType Left = new("Left");
    public static readonly AlignmentType Center = new("Center");
    public static readonly AlignmentType Right = new("Right");

    public override string ToString() => Name;
}

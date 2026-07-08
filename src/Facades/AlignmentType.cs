namespace Aspose.Pdf.Facades;

/// <summary>
/// Horizontal-alignment selector for legacy facade APIs (e.g. <see cref="PdfPageEditor"/>).
/// Type-safe enum: only the static instances <see cref="Left"/>, <see cref="Center"/>,
/// <see cref="Right"/> are valid. Marked obsolete in Aspose.Pdf — new code
/// should prefer <see cref="Aspose.Pdf.HorizontalAlignment"/>.
/// </summary>
public sealed class AlignmentType
{
    public string Name { get; }

    public AlignmentType(string name) => Name = name ?? "";

    public static readonly AlignmentType Left = new("Left");
    public static readonly AlignmentType Center = new("Center");
    public static readonly AlignmentType Right = new("Right");

    public override string ToString() => Name;
}

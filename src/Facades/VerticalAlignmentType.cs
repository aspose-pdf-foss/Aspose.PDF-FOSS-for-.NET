namespace Aspose.Pdf.Facades;

/// <summary>
/// Vertical-alignment selector for legacy facade APIs (e.g. <see cref="PdfPageEditor"/>).
/// Type-safe enum: only the static instances <see cref="Top"/>, <see cref="Center"/>,
/// <see cref="Bottom"/> are valid. Marked obsolete in Aspose.Pdf — new code
/// should prefer <see cref="Aspose.Pdf.VerticalAlignment"/>.
/// </summary>
public sealed class VerticalAlignmentType
{
    public string Name { get; }

    public VerticalAlignmentType(string name) => Name = name ?? "";

    public static readonly VerticalAlignmentType Top = new("Top");
    public static readonly VerticalAlignmentType Center = new("Center");
    public static readonly VerticalAlignmentType Bottom = new("Bottom");

    public override string ToString() => Name;
}

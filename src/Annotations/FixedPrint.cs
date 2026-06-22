namespace Aspose.Pdf.Annotations;

public sealed class FixedPrint
{
    public Matrix Matrix { get; set; } = new Matrix(1, 0, 0, 1, 0, 0);
    public double HorizontalTranslation { get; set; }
    public double VerticalTranslation { get; set; }
}

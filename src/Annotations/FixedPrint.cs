namespace Aspose.Pdf.Annotations;

/// <summary>A watermark annotation's fixed-print placement (/FixedPrint): the matrix and the
/// horizontal / vertical translations (fractions of the page size) it is printed with.</summary>
public sealed class FixedPrint
{
    private Matrix _matrix = new Matrix(1, 0, 0, 1, 0, 0);
    private double _horizontalTranslation;
    private double _verticalTranslation;

    /// <summary>Called when a value is set, so the owning annotation writes its /FixedPrint.</summary>
    internal System.Action? Changed;

    public Matrix Matrix
    {
        get => _matrix;
        set { _matrix = value; Changed?.Invoke(); }
    }

    public double HorizontalTranslation
    {
        get => _horizontalTranslation;
        set { _horizontalTranslation = value; Changed?.Invoke(); }
    }

    public double VerticalTranslation
    {
        get => _verticalTranslation;
        set { _verticalTranslation = value; Changed?.Invoke(); }
    }

    /// <summary>Takes the values a read /FixedPrint dictionary holds, without writing them back.</summary>
    internal void Load(Matrix matrix, double horizontal, double vertical)
    {
        _matrix = matrix;
        _horizontalTranslation = horizontal;
        _verticalTranslation = vertical;
    }
}

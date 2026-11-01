namespace Aspose.Pdf.Devices.Rasterizer;

/// <summary>
/// The area and perimeter of a closed figure in device pixels, gathered as it is walked. Their
/// ratio is the mean width of the shape: twice the area over the perimeter is the width of a long
/// thin band, whatever way it runs or bends.
/// </summary>
internal struct OutlineMeasure
{
    private double _twiceArea;
    private double _perimeter;
    private double _startX, _startY, _x, _y;

    /// <summary>A mean width at or under this is a figure that encloses nothing: a line, a point.</summary>
    public const double DegenerateWidth = 1e-3;

    /// <summary>A shape narrower than this passes between pixel centres.</summary>
    public const double ThinWidth = 1.0;

    public void MoveTo(double x, double y)
    {
        _twiceArea = 0;
        _perimeter = 0;
        _startX = _x = x;
        _startY = _y = y;
    }

    public void LineTo(double x, double y)
    {
        _twiceArea += _x * y - x * _y;
        _perimeter += Math.Sqrt((x - _x) * (x - _x) + (y - _y) * (y - _y));
        _x = x;
        _y = y;
    }

    /// <summary>The mean width of the figure walked since the last move, closed back to its
    /// start: a fill and a clip close every figure, drawn closed or not.</summary>
    public double CloseFigure()
    {
        LineTo(_startX, _startY);
        return _perimeter > 0 ? Math.Abs(_twiceArea) / _perimeter : 0;
    }

    /// <summary>True when the width is that of a figure which encloses something and still
    /// passes between pixel centres.</summary>
    public static bool IsThin(double meanWidth) => meanWidth > DegenerateWidth && meanWidth < ThinWidth;
}

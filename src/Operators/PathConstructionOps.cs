// PDF content stream operators — PDF32000_2008 §8–9
using System.Globalization;
using System.Text;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Operators;

/// <summary>m — Begin new subpath at (X, Y).</summary>
public sealed class MoveTo : Operator
{
    /// <summary>Gets or sets the x coordinate of the start of the new subpath, in user space units.</summary>
    public double X { get; set; }
    /// <summary>Gets or sets the y coordinate of the start of the new subpath, in user space units.</summary>
    public double Y { get; set; }
    /// <summary>Creates an <c>m</c> operator that begins a new subpath at (<c>x</c>, <c>y</c>) in user space.</summary>
    public MoveTo(double x, double y) { X = x; Y = y; }
    public override string ToPdf() => $"{Fmt(X)} {Fmt(Y)} m";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>l — Append straight line segment to (X, Y).</summary>
public sealed class LineTo : Operator
{
    /// <summary>Gets or sets the x coordinate of the end of the line, in user space units.</summary>
    public double X { get; set; }
    /// <summary>Gets or sets the y coordinate of the end of the line, in user space units.</summary>
    public double Y { get; set; }
    /// <summary>Creates an <c>l</c> operator that appends a straight line from the current point to (<c>x</c>, <c>y</c>) in user space.</summary>
    public LineTo(double x, double y) { X = x; Y = y; }
    public override string ToPdf() => $"{Fmt(X)} {Fmt(Y)} l";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>c — Append cubic Bézier curve with three control points.</summary>
public sealed class CurveTo : Operator
{
    public double X1;
    public double Y1;
    public double X2;
    public double Y2;
    public double X3;
    public double Y3;
    /// <summary>Creates a <c>c</c> operator that appends a cubic Bézier curve from the current point to (<c>x3</c>, <c>y3</c>), with (<c>x1</c>, <c>y1</c>) and (<c>x2</c>, <c>y2</c>) as its control points.</summary>
    public CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
    { X1 = x1; Y1 = y1; X2 = x2; Y2 = y2; X3 = x3; Y3 = y3; }
    public override string ToPdf() =>
        $"{Fmt(X1)} {Fmt(Y1)} {Fmt(X2)} {Fmt(Y2)} {Fmt(X3)} {Fmt(Y3)} c";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>v — Append cubic Bézier curve; first control point is current point.</summary>
public sealed class CurveTo1 : Operator
{
    public double X2 { get; }
    public double Y2 { get; }
    public double X3 { get; }
    public double Y3 { get; }
    /// <summary>Creates a <c>v</c> operator that appends a cubic Bézier curve to (<c>x3</c>, <c>y3</c>); the current point is the first control point and (<c>x2</c>, <c>y2</c>) the second.</summary>
    public CurveTo1(double x2, double y2, double x3, double y3)
    { X2 = x2; Y2 = y2; X3 = x3; Y3 = y3; }
    /// <summary>Gets the second control point and the end point of the curve, in that order.</summary>
    public Aspose.Pdf.Point[] Points => new[] { new Aspose.Pdf.Point(X2, Y2), new Aspose.Pdf.Point(X3, Y3) };
    public override string ToPdf() => $"{Fmt(X2)} {Fmt(Y2)} {Fmt(X3)} {Fmt(Y3)} v";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>y — Append cubic Bézier curve; second control point coincides with final point.</summary>
public sealed class CurveTo2 : Operator
{
    public double X1 { get; }
    public double Y1 { get; }
    public double X3 { get; }
    public double Y3 { get; }
    /// <summary>Creates a <c>y</c> operator that appends a cubic Bézier curve to (<c>x3</c>, <c>y3</c>) with (<c>x1</c>, <c>y1</c>) as the first control point; the end point is also the second control point.</summary>
    public CurveTo2(double x1, double y1, double x3, double y3)
    { X1 = x1; Y1 = y1; X3 = x3; Y3 = y3; }
    /// <summary>Gets the first control point and the end point of the curve, in that order.</summary>
    public Aspose.Pdf.Point[] Points => new[] { new Aspose.Pdf.Point(X1, Y1), new Aspose.Pdf.Point(X3, Y3) };
    public override string ToPdf() => $"{Fmt(X1)} {Fmt(Y1)} {Fmt(X3)} {Fmt(Y3)} y";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>re — Append rectangle (X, Y, Width, Height) as a complete subpath.</summary>
public sealed class Re : Operator
{
    /// <summary>Gets or sets the x coordinate of the rectangle's lower-left corner, in user space units.</summary>
    public double X { get; set; }
    /// <summary>Gets or sets the y coordinate of the rectangle's lower-left corner, in user space units.</summary>
    public double Y { get; set; }
    /// <summary>Gets or sets the width of the rectangle, in user space units.</summary>
    public double Width { get; set; }
    /// <summary>Gets or sets the height of the rectangle, in user space units.</summary>
    public double Height { get; set; }
    /// <summary>Creates an <c>re</c> operator with a zero-sized rectangle at the origin.</summary>
    public Re() { }
    /// <summary>Creates an <c>re</c> operator that appends a rectangle as a complete subpath, with its lower-left corner at (<c>x</c>, <c>y</c>) and the given width and height, in user space units.</summary>
    public Re(double x, double y, double width, double height)
    { X = x; Y = y; Width = width; Height = height; }
    public override string ToPdf() =>
        $"{Fmt(X)} {Fmt(Y)} {Fmt(Width)} {Fmt(Height)} re";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>h — Close subpath.</summary>
public sealed class ClosePath : Operator
{
    /// <summary>Creates an <c>h</c> operator, which closes the current subpath with a straight line back to its starting point.</summary>
    public ClosePath() { }
    public override string ToPdf() => "h";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

// =====================================================================
// Path painting operators (PDF 32000-1 §8.5.3).
// =====================================================================

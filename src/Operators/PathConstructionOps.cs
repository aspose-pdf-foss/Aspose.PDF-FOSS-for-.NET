// PDF content stream operators — PDF32000_2008 §8–9
using System.Globalization;
using System.Text;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Operators;

/// <summary>m — Begin new subpath at (X, Y).</summary>
public sealed class MoveTo : Operator
{
    public double X { get; set; }
    public double Y { get; set; }
    public MoveTo(double x, double y) { X = x; Y = y; }
    public override string ToPdf() => $"{Fmt(X)} {Fmt(Y)} m";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>l — Append straight line segment to (X, Y).</summary>
public sealed class LineTo : Operator
{
    public double X { get; set; }
    public double Y { get; set; }
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
    public CurveTo1(double x2, double y2, double x3, double y3)
    { X2 = x2; Y2 = y2; X3 = x3; Y3 = y3; }
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
    public CurveTo2(double x1, double y1, double x3, double y3)
    { X1 = x1; Y1 = y1; X3 = x3; Y3 = y3; }
    public Aspose.Pdf.Point[] Points => new[] { new Aspose.Pdf.Point(X1, Y1), new Aspose.Pdf.Point(X3, Y3) };
    public override string ToPdf() => $"{Fmt(X1)} {Fmt(Y1)} {Fmt(X3)} {Fmt(Y3)} y";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>re — Append rectangle (X, Y, Width, Height) as a complete subpath.</summary>
public sealed class Re : Operator
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public Re() { }
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
    public override string ToPdf() => "h";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

// =====================================================================
// Path painting operators (PDF 32000-1 §8.5.3).
// =====================================================================

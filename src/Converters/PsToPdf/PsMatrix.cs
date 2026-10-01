using System;

namespace Aspose.Pdf.Converters.PsToPdf;

/// <summary>
/// A PostScript transformation matrix <c>[a b c d tx ty]</c>. PostScript and PDF
/// share this representation and the same bottom-left origin, so a CTM travels from
/// one to the other unchanged — no flip is applied anywhere in this converter.
/// </summary>
internal readonly struct PsMatrix
{
    /// <summary>How many numbers a matrix array holds.</summary>
    public const int ElementCount = 6;

    /// <summary>The identity matrix.</summary>
    public static readonly PsMatrix Identity = new(1, 0, 0, 1, 0, 0);

    /// <summary>Build a matrix from its six elements.</summary>
    public PsMatrix(double a, double b, double c, double d, double e, double f)
    {
        A = a;
        B = b;
        C = c;
        D = d;
        E = e;
        F = f;
    }

    /// <summary>Element a.</summary>
    public double A { get; }

    /// <summary>Element b.</summary>
    public double B { get; }

    /// <summary>Element c.</summary>
    public double C { get; }

    /// <summary>Element d.</summary>
    public double D { get; }

    /// <summary>Element tx.</summary>
    public double E { get; }

    /// <summary>Element ty.</summary>
    public double F { get; }

    /// <summary>A translation.</summary>
    public static PsMatrix Translation(double x, double y) => new(1, 0, 0, 1, x, y);

    /// <summary>A scale.</summary>
    public static PsMatrix Scaling(double x, double y) => new(x, 0, 0, y, 0, 0);

    /// <summary>A rotation by an angle in degrees, counter-clockwise.</summary>
    public static PsMatrix Rotation(double degrees)
    {
        var radians = degrees * Math.PI / DegreesPerHalfTurn;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return new PsMatrix(cos, sin, -sin, cos, 0, 0);
    }

    private const double DegreesPerHalfTurn = 180.0;

    /// <summary>This matrix followed by <paramref name="other"/> — the product
    /// <c>this × other</c>, which is what <c>concat</c> computes.</summary>
    public PsMatrix Concat(PsMatrix other) => new(
        A * other.A + B * other.C,
        A * other.B + B * other.D,
        C * other.A + D * other.C,
        C * other.B + D * other.D,
        E * other.A + F * other.C + other.E,
        E * other.B + F * other.D + other.F);

    /// <summary>Map a point through this matrix.</summary>
    public void Transform(double x, double y, out double outX, out double outY)
    {
        outX = A * x + C * y + E;
        outY = B * x + D * y + F;
    }

    /// <summary>Map a distance vector, ignoring the translation.</summary>
    public void TransformDelta(double dx, double dy, out double outX, out double outY)
    {
        outX = A * dx + C * dy;
        outY = B * dx + D * dy;
    }

    /// <summary>The determinant.</summary>
    public double Determinant => A * D - B * C;

    /// <summary>The inverse, or the identity when this matrix is singular.</summary>
    public PsMatrix Invert()
    {
        var det = Determinant;
        if (Math.Abs(det) < Epsilon) return Identity;
        var ia = D / det;
        var ib = -B / det;
        var ic = -C / det;
        var id = A / det;
        return new PsMatrix(ia, ib, ic, id, -(E * ia + F * ic), -(E * ib + F * id));
    }

    /// <summary>Below this, a determinant counts as zero.</summary>
    private const double Epsilon = 1e-12;

    /// <summary>An approximate uniform scale factor, used to decide how finely a
    /// curve must be flattened to look smooth at device resolution. A singular
    /// matrix has no area, so fall back to the largest row sum.</summary>
    /// <summary>How far this matrix carries a unit along the x axis. A font matrix
    /// that scales the two axes differently advances a string by THIS, not by the
    /// uniform size the text object is set at.</summary>
    public double HorizontalScale => Math.Sqrt(A * A + B * B);

    /// <summary>The uniform scale a matrix carries, apart from any rotation, shear or
    /// mirror in it.</summary>
    public double ApproximateScale
    {
        get
        {
            var area = Math.Sqrt(Math.Abs(Determinant));
            if (area > 0) return area;
            return Math.Max(Math.Abs(A) + Math.Abs(C), Math.Abs(B) + Math.Abs(D));
        }
    }

    /// <summary>The six elements in array order.</summary>
    public double[] ToElements() => new[] { A, B, C, D, E, F };
}

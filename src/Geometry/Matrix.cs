namespace Aspose.Pdf;

/// <summary>
/// Represents a 3x3 transformation matrix [a b 0; c d 0; e f 1].
/// </summary>
public sealed class Matrix
{
    public double A { get; set; }
    public double B { get; set; }
    public double C { get; set; }
    public double D { get; set; }
    public double E { get; set; }
    public double F { get; set; }

    /// <summary>Default constructor — produces the identity matrix.</summary>
    public Matrix()
    {
        A = 1; B = 0; C = 0; D = 1; E = 0; F = 0;
    }

    public Matrix(double a, double b, double c, double d, double e, double f)
    {
        A = a; B = b; C = c; D = d; E = e; F = f;
    }

    /// <summary>Copy constructor.</summary>
    public Matrix(Matrix matrix)
    {
        if (matrix is null) throw new ArgumentNullException(nameof(matrix));
        A = matrix.A; B = matrix.B; C = matrix.C; D = matrix.D; E = matrix.E; F = matrix.F;
    }

    /// <summary>Create a matrix from a 6-element array [a, b, c, d, e, f].</summary>
    public Matrix(double[] matrixArray)
    {
        if (matrixArray is null || matrixArray.Length < 6)
            throw new ArgumentException("matrixArray must have at least 6 elements", nameof(matrixArray));
        A = matrixArray[0]; B = matrixArray[1]; C = matrixArray[2];
        D = matrixArray[3]; E = matrixArray[4]; F = matrixArray[5];
    }

    /// <summary>Create a matrix from a 6-element float array.</summary>
    public Matrix(float[] matrixArray)
    {
        if (matrixArray is null || matrixArray.Length < 6)
            throw new ArgumentException("matrixArray must have at least 6 elements", nameof(matrixArray));
        A = matrixArray[0]; B = matrixArray[1]; C = matrixArray[2];
        D = matrixArray[3]; E = matrixArray[4]; F = matrixArray[5];
    }

    /// <summary>The matrix elements as a 6-element double array.</summary>
    public double[] Data => new[] { A, B, C, D, E, F };

    /// <summary>The matrix elements as a 6-element float array.</summary>
    public float[] Elements => new[] { (float)A, (float)B, (float)C, (float)D, (float)E, (float)F };

    /// <summary>Identity matrix.</summary>
    public static Matrix Identity => new(1, 0, 0, 1, 0, 0);

    // ── Static factories ──────────────────────────────────────────────────

    /// <summary>Create a translation matrix.</summary>
    public static Matrix Translate(double tx, double ty) => new(1, 0, 0, 1, tx, ty);

    /// <summary>Create a scaling matrix.</summary>
    public static Matrix Scale(double sx, double sy) => new(sx, 0, 0, sy, 0, 0);

    /// <summary>Create a rotation matrix (angle in degrees).</summary>
    public static Matrix Rotate(double degrees)
    {
        var rad = degrees * Math.PI / 180;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        return new Matrix(cos, sin, -sin, cos, 0, 0);
    }

    // ── Composition factories ─────────────────────────────────────────────

    /// <summary>Compose a translation onto <paramref name="source"/>: returns source × translate(dx, dy).</summary>
    public static Matrix Translate(double dx, double dy, Matrix source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        return source.Multiply(Translate(dx, dy));
    }

    /// <summary>Compose a scaling onto <paramref name="source"/>: returns source × scale(sx, sy).</summary>
    public static Matrix Scale(double sx, double sy, Matrix source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        return source.Multiply(Scale(sx, sy));
    }

    /// <summary>Create a rotation matrix; <paramref name="alpha"/> is in radians.</summary>
    public static Matrix Rotation(double alpha)
    {
        var cos = Math.Cos(alpha);
        var sin = Math.Sin(alpha);
        return new Matrix(cos, sin, -sin, cos, 0, 0);
    }

    /// <summary>Create a rotation matrix for one of the four standard PDF rotations.</summary>
    public static Matrix Rotation(Rotation rotation) => Rotation(GetAngle(rotation));

    /// <summary>Create a skew/shear matrix; <paramref name="alpha"/> and <paramref name="beta"/> are in radians.</summary>
    public static Matrix Skew(double alpha, double beta)
        => new(1, Math.Tan(alpha), Math.Tan(beta), 1, 0, 0);

    /// <summary>Convert a <see cref="Rotation"/> value to its angle in radians.</summary>
    public static double GetAngle(Rotation rotation) => rotation switch
    {
        Aspose.Pdf.Rotation.on90 => Math.PI / 2,
        Aspose.Pdf.Rotation.on180 => Math.PI,
        Aspose.Pdf.Rotation.on270 => 3 * Math.PI / 2,
        _ => 0,
    };

    /// <summary>A matrix that flips the Y axis (vertical mirror).</summary>
    public static Matrix GetFlipMatrix() => new(1, 0, 0, -1, 0, 0);

    // ── Arithmetic ────────────────────────────────────────────────────────

    /// <summary>Element-wise addition.</summary>
    public Matrix Add(Matrix other)
    {
        if (other is null) throw new ArgumentNullException(nameof(other));
        return new Matrix(A + other.A, B + other.B, C + other.C, D + other.D, E + other.E, F + other.F);
    }

    /// <summary>Multiply this matrix by another: this × other.</summary>
    public Matrix Multiply(Matrix other)
    {
        if (other is null) throw new ArgumentNullException(nameof(other));
        return new Matrix(
            A * other.A + B * other.C,
            A * other.B + B * other.D,
            C * other.A + D * other.C,
            C * other.B + D * other.D,
            E * other.A + F * other.C + other.E,
            E * other.B + F * other.D + other.F
        );
    }

    // ── Transform helpers ────────────────────────────────────────────────

    /// <summary>Transform a point (returns the result via tuple — convenience).</summary>
    public (double x, double y) TransformPoint(double x, double y)
        => (A * x + C * y + E, B * x + D * y + F);

    /// <summary>Transform a point and return the result via out parameters.</summary>
    public void Transform(double x, double y, out double x1, out double y1)
    {
        x1 = A * x + C * y + E;
        y1 = B * x + D * y + F;
    }

    /// <summary>Transform a point by the inverse of this matrix (returns the result via tuple).</summary>
    public (double x, double y) InverseTransformPoint(double x, double y)
        => Inverse().TransformPoint(x, y);

    /// <summary>Inverse-transform a point and return the result via out parameters.</summary>
    public void UnTransform(double x1, double y1, out double x, out double y)
    {
        (x, y) = InverseTransformPoint(x1, y1);
    }

    /// <summary>Apply only the scale portion of this matrix (drops the translation component).</summary>
    public void Scale(double x, double y, out double x1, out double y1)
    {
        x1 = A * x + C * y;
        y1 = B * x + D * y;
    }

    /// <summary>Apply the inverse scale (drops translation).</summary>
    public void UnScale(double x1, double y1, out double x, out double y)
    {
        var det = A * D - B * C;
        if (Math.Abs(det) < 1e-15) { x = 0; y = 0; return; }
        var invDet = 1.0 / det;
        x = (D * x1 - C * y1) * invDet;
        y = (-B * x1 + A * y1) * invDet;
    }

    /// <summary>Compute the inverse matrix.</summary>
    public Matrix Inverse()
    {
        var det = A * D - B * C;
        if (Math.Abs(det) < 1e-15)
            throw new InvalidOperationException("Matrix is not invertible");

        var invDet = 1.0 / det;
        return new Matrix(
            D * invDet,
            -B * invDet,
            -C * invDet,
            A * invDet,
            (C * F - D * E) * invDet,
            (B * E - A * F) * invDet
        );
    }

    /// <summary>Calculates reverse (inverse) matrix.</summary>
    public Matrix Reverse() => Inverse();

    /// <summary>Transform a point.</summary>
    public Point Transform(Point p)
    {
        var (x, y) = TransformPoint(p.X, p.Y);
        return new Point(x, y);
    }

    /// <summary>
    /// Transform a rectangle. Returns the axis-aligned bounding rectangle of the four
    /// transformed corners.
    /// </summary>
    public Rectangle Transform(Rectangle rect)
    {
        var (x1, y1) = TransformPoint(rect.LLX, rect.LLY);
        var (x2, y2) = TransformPoint(rect.URX, rect.LLY);
        var (x3, y3) = TransformPoint(rect.URX, rect.URY);
        var (x4, y4) = TransformPoint(rect.LLX, rect.URY);
        var minX = Math.Min(Math.Min(x1, x2), Math.Min(x3, x4));
        var minY = Math.Min(Math.Min(y1, y2), Math.Min(y3, y4));
        var maxX = Math.Max(Math.Max(x1, x2), Math.Max(x3, x4));
        var maxY = Math.Max(Math.Max(y1, y2), Math.Max(y3, y4));
        return new Rectangle(minX, minY, maxX, maxY);
    }

    public override bool Equals(object? obj)
    {
        if (obj is not Matrix m) return false;
        return A == m.A && B == m.B && C == m.C && D == m.D && E == m.E && F == m.F;
    }

    public override int GetHashCode() => HashCode.Combine(A, B, C, D, E, F);

    public override string ToString() => $"[{A:G} {B:G} {C:G} {D:G} {E:G} {F:G}]";
}

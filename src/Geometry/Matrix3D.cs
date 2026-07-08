namespace Aspose.Pdf;

/// <summary>4×3 affine 3D matrix (Aspose.Pdf shape for PDF 3D camera
/// positioning). Stored only — the FOSS build does not yet render 3D
/// content streams.</summary>
public sealed class Matrix3D
{
    public Matrix3D() { }

    public Matrix3D(Matrix3D matrix)
    {
        if (matrix is null) return;
        A = matrix.A; B = matrix.B; C = matrix.C;
        D = matrix.D; E = matrix.E; F = matrix.F;
        G = matrix.G; H = matrix.H; I = matrix.I;
        Tx = matrix.Tx; Ty = matrix.Ty; Tz = matrix.Tz;
    }

    public Matrix3D(
        double a, double b, double c,
        double d, double e, double f,
        double g, double h, double i,
        double tx, double ty, double tz)
    {
        A = a; B = b; C = c;
        D = d; E = e; F = f;
        G = g; H = h; I = i;
        Tx = tx; Ty = ty; Tz = tz;
    }

    public Matrix3D(double[] matrix3DArray)
    {
        if (matrix3DArray is null) return;
        if (matrix3DArray.Length > 0) A = matrix3DArray[0];
        if (matrix3DArray.Length > 1) B = matrix3DArray[1];
        if (matrix3DArray.Length > 2) C = matrix3DArray[2];
        if (matrix3DArray.Length > 3) D = matrix3DArray[3];
        if (matrix3DArray.Length > 4) E = matrix3DArray[4];
        if (matrix3DArray.Length > 5) F = matrix3DArray[5];
        if (matrix3DArray.Length > 6) G = matrix3DArray[6];
        if (matrix3DArray.Length > 7) H = matrix3DArray[7];
        if (matrix3DArray.Length > 8) I = matrix3DArray[8];
        if (matrix3DArray.Length > 9) Tx = matrix3DArray[9];
        if (matrix3DArray.Length > 10) Ty = matrix3DArray[10];
        if (matrix3DArray.Length > 11) Tz = matrix3DArray[11];
    }

    public double A { get; set; } = 1.0;
    public double B { get; set; }
    public double C { get; set; }
    public double D { get; set; }
    public double E { get; set; } = 1.0;
    public double F { get; set; }
    public double G { get; set; }
    public double H { get; set; }
    public double I { get; set; } = 1.0;
    public double Tx { get; set; }
    public double Ty { get; set; }
    public double Tz { get; set; }

    /// <summary>Componentwise sum of this matrix and <paramref name="other"/>.
    /// Returns a fresh instance; the operands are left untouched.</summary>
    public Matrix3D Add(Matrix3D other)
    {
        if (other is null) return new Matrix3D(this);
        return new Matrix3D(
            A + other.A, B + other.B, C + other.C,
            D + other.D, E + other.E, F + other.F,
            G + other.G, H + other.H, I + other.I,
            Tx + other.Tx, Ty + other.Ty, Tz + other.Tz);
    }

    /// <summary>Convert a rotation enum value (None/on90/on180/on270/on360)
    /// to radians. Returns 0 when the value is not one of the recognised
    /// quarter-turns.</summary>
    public double GetAngle(Rotation rotation) => rotation switch
    {
        Rotation.on90 => Math.PI / 2,
        Rotation.on180 => Math.PI,
        Rotation.on270 => 3 * Math.PI / 2,
        Rotation.on360 => 2 * Math.PI,
        _ => 0.0,
    };

    public override bool Equals(object? obj)
    {
        if (obj is not Matrix3D m) return false;
        return A == m.A && B == m.B && C == m.C
            && D == m.D && E == m.E && F == m.F
            && G == m.G && H == m.H && I == m.I
            && Tx == m.Tx && Ty == m.Ty && Tz == m.Tz;
    }

    public override int GetHashCode()
    {
        var h1 = HashCode.Combine(A, B, C, D, E, F);
        var h2 = HashCode.Combine(G, H, I, Tx, Ty, Tz);
        return HashCode.Combine(h1, h2);
    }

    public override string ToString()
    {
        var c = System.Globalization.CultureInfo.InvariantCulture;
        return $"[{A.ToString(c)}, {B.ToString(c)}, {C.ToString(c)}, {D.ToString(c)}, "
             + $"{E.ToString(c)}, {F.ToString(c)}, {G.ToString(c)}, {H.ToString(c)}, "
             + $"{I.ToString(c)}, {Tx.ToString(c)}, {Ty.ToString(c)}, {Tz.ToString(c)}]";
    }
}

// PDF content stream operators — PDF32000_2008 §8–9
using System.Globalization;
using System.Text;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Operators;

/// <summary>q — Save graphics state.</summary>
public sealed class GSave : Operator
{
    public override string ToPdf() => "q";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>Q — Restore graphics state.</summary>
public sealed class GRestore : Operator
{
    public override string ToPdf() => "Q";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>cm — Concatenate matrix to CTM.</summary>
public sealed class ConcatenateMatrix : Operator
{
    /// <summary>
    /// The transformation matrix. Returns an <see cref="Aspose.Pdf.Matrix"/>
    /// (the `cm` operator
    /// exposes a Matrix object with `.A`/`.B`/`.C`/`.D`/`.E`/`.F` accessors).
    /// </summary>
    public Aspose.Pdf.Matrix Matrix { get; set; }

    public ConcatenateMatrix(double[] matrix)
    {
        if (matrix.Length != 6)
            throw new ArgumentException("Matrix must have exactly 6 elements.");
        Matrix = new Aspose.Pdf.Matrix(matrix);
    }

    public ConcatenateMatrix(double a, double b, double c, double d, double e, double f)
        : this(new[] { a, b, c, d, e, f }) { }

    /// <summary>Build from an Aspose.Pdf.Matrix.</summary>
    public ConcatenateMatrix(Aspose.Pdf.Matrix m)
    {
        if (m is null) throw new ArgumentNullException(nameof(m));
        Matrix = m;
    }

    public override string ToPdf() =>
        $"{FmtMatrix(Matrix.A)} {FmtMatrix(Matrix.B)} {FmtMatrix(Matrix.C)} {FmtMatrix(Matrix.D)} {FmtMatrix(Matrix.E)} {FmtMatrix(Matrix.F)} cm";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);

    /// <summary>
    /// Format a <c>cm</c> operand. Differs from the shared 6-fraction-digit
    /// <see cref="Operator.Fmt"/> in two ways specific to transformation
    /// matrices:
    ///  - it preserves the value's full round-trip precision (an 8-significant-digit
    ///    scale such as <c>8.41314506</c> keeps all its digits instead of being
    ///    truncated to <c>8.413145</c>), while still emitting the short clean form for
    ///    the common values that already fit in 6 fraction digits (0.5, 100, …);
    ///  - a translation whose magnitude exceeds the 16-bit coordinate range
    ///    (|v| &gt; 32768) is written as an integer. Sub-unit precision is meaningless
    ///    that far out (0.5 unit in ~40000 is ~1e-5) and the PDF/A normaliser rounds
    ///    such coordinates, so e.g. <c>-39247.3217</c> serialises as <c>-39247</c>.
    /// </summary>
    internal static string FmtMatrix(double v)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        if (Math.Abs(v) > 32768.0)
            return Math.Round(v).ToString("0", ci);
        // Keep the short 6-fraction-digit form when it already represents v exactly
        // (no churn for the overwhelming majority of matrices); otherwise fall back to
        // the shortest round-trippable decimal, never in exponent form (invalid in a
        // content stream).
        var s6 = v.ToString("0.######", ci);
        if (double.TryParse(s6, System.Globalization.NumberStyles.Float, ci, out var back) && back == v)
            return s6;
        var r = v.ToString("R", ci);
        if (r.IndexOf('E') >= 0 || r.IndexOf('e') >= 0)
            r = v.ToString("0.#################", ci);
        return r;
    }
}

/// <summary>Line cap style. PDF 32000-1 §8.4.3.3.</summary>
public enum LineCap { ButtCap = 0, RoundCap = 1, ProjectingSquareCap = 2, SquareCap = ProjectingSquareCap }

/// <summary>Line join style. PDF 32000-1 §8.4.3.4.</summary>
public enum LineJoin { MiterJoin = 0, RoundJoin = 1, BevelJoin = 2 }

// =====================================================================
// Path construction operators (PDF 32000-1 §8.5.2).
// =====================================================================

/// <summary>gs — Set parameters from named ExtGState resource.</summary>
public sealed class GS : Operator
{
    public string Name { get; set; }
    public GS(string name) { Name = name; }
    public override string ToPdf() => $"/{Name} gs";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>w — Set line width.</summary>
public sealed class SetLineWidth : Operator
{
    public double LineWidth { get; set; }
    /// <summary>Public-API-shape alias for <see cref="LineWidth"/>.</summary>
    public double Width { get => LineWidth; set => LineWidth = value; }
    public SetLineWidth(double width) { LineWidth = width; }
    public override string ToPdf() => $"{Fmt(LineWidth)} w";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>J — Set line cap style.</summary>
public sealed class SetLineCap : Operator
{
    public LineCap Cap { get; set; }
    public SetLineCap(LineCap cap) { Cap = cap; }
    public SetLineCap(int cap) { Cap = (LineCap)cap; }
    public override string ToPdf() => $"{(int)Cap} J";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>j — Set line join style.</summary>
public sealed class SetLineJoin : Operator
{
    public LineJoin Join { get; set; }
    public SetLineJoin() { Join = LineJoin.MiterJoin; }
    public SetLineJoin(LineJoin join) { Join = join; }
    public SetLineJoin(int join) { Join = (LineJoin)join; }
    public override string ToPdf() => $"{(int)Join} j";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>M — Set miter limit.</summary>
public sealed class SetMiterLimit : Operator
{
    public double MiterLimit { get; set; }
    public SetMiterLimit(double miterLimit) { MiterLimit = miterLimit; }
    public override string ToPdf() => $"{Fmt(MiterLimit)} M";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>d — Set dash pattern.</summary>
public sealed class SetDash : Operator
{
    public int[] DashArray { get; set; }
    public int DashPhase { get; set; }
    /// <summary>Public-API-shape alias for <see cref="DashArray"/>.</summary>
    public int[] Pattern { get => DashArray; set => DashArray = value; }
    /// <summary>Public-API-shape alias for <see cref="DashPhase"/>.</summary>
    public int Phase { get => DashPhase; set => DashPhase = value; }
    public SetDash(int[] pattern, int phase)
    { DashArray = pattern ?? Array.Empty<int>(); DashPhase = phase; }
    public override string ToPdf()
    {
        var sb = new StringBuilder("[");
        for (int i = 0; i < DashArray.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(DashArray[i].ToString(CultureInfo.InvariantCulture));
        }
        sb.Append("] ").Append(DashPhase.ToString(CultureInfo.InvariantCulture)).Append(" d");
        return sb.ToString();
    }
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>i — Set flatness tolerance.</summary>
public sealed class SetFlat : Operator
{
    public double Flatness { get; set; }
    public SetFlat(double flatness) { Flatness = flatness; }
    public override string ToPdf() => $"{Fmt(Flatness)} i";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

// =====================================================================
// Color operators (PDF 32000-1 §8.6).
// =====================================================================

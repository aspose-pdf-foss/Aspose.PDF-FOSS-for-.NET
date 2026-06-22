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

/// <summary>W — Set clipping path (nonzero winding rule).</summary>
public sealed class Clip : Operator
{
    public override string ToPdf() => "W";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>W* — Set clipping path (even-odd rule).</summary>
public sealed class EOClip : Operator
{
    public override string ToPdf() => "W*";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>BT — Begin text object.</summary>
public sealed class BT : Operator
{
    public override string ToPdf() => "BT";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>ET — End text object.</summary>
public sealed class ET : Operator
{
    public override string ToPdf() => "ET";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>EMC — End marked content.</summary>
public sealed class EMC : Operator
{
    public override string ToPdf() => "EMC";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>BDC — Begin marked content with properties.</summary>
public sealed class BDC : Operator
{
    public string Tag { get; set; }
    public Aspose.Pdf.Facades.BDCProperties? Properties { get; }

    public BDC(string tag) { Tag = tag; }
    public BDC(string tag, Aspose.Pdf.Facades.BDCProperties properties) { Tag = tag; Properties = properties; }

    public override string ToPdf() =>
        Properties is null ? $"/{Tag} BDC" : $"/{Tag} {Properties.ToPdf()} BDC";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>rg — Set RGB fill color.</summary>
public sealed class SetRGBColor : Operator
{
    public double R { get; set; }
    public double G { get; set; }
    public double B { get; set; }

    public SetRGBColor(double r, double g, double b) { R = r; G = g; B = b; }
    public SetRGBColor(System.Drawing.Color color)
        : this(color.R / 255.0, color.G / 255.0, color.B / 255.0) { }
    public override string ToPdf() => $"{FmtColor(R)} {FmtColor(G)} {FmtColor(B)} rg";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
    public System.Drawing.Color getColor() => SetColorOperator.ToSystemColor(new[] { R, G, B });
}

/// <summary>Tf — Select font and size.</summary>
public sealed class SelectFont : Operator
{
    public string FontName { get; }
    public double Size { get; }
    /// <summary>Aspose.PDF for .NET-shape alias for <see cref="FontName"/>.</summary>
    public string Name => FontName;

    public SelectFont(string resName, double size) { FontName = resName; Size = size; }
    public override string ToPdf() => $"/{FontName} {Fmt(Size)} Tf";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>Tm — Set text matrix.</summary>
public sealed class SetTextMatrix : TextPlaceOperator
{
    public double A { get; }
    public double B { get; }
    public double C { get; }
    public double D { get; }
    public double E { get; }
    public double F { get; }

    private Aspose.Pdf.Matrix _matrix;

    /// <summary>The transform as a <see cref="Aspose.Pdf.Matrix"/>. Setting it
    /// replaces the cached matrix only; A..F field values are unchanged because
    /// they're declared get-only at the type level.</summary>
    public Aspose.Pdf.Matrix Matrix
    {
        get => _matrix ?? new Aspose.Pdf.Matrix(A, B, C, D, E, F);
        set => _matrix = value;
    }

    public SetTextMatrix(double a, double b, double c, double d, double e, double f)
    {
        A = a; B = b; C = c; D = d; E = e; F = f;
        _matrix = new Aspose.Pdf.Matrix(a, b, c, d, e, f);
    }

    public SetTextMatrix(Aspose.Pdf.Matrix m)
        : this(m.A, m.B, m.C, m.D, m.E, m.F) { }

    public override string ToPdf() => $"{Fmt(A)} {Fmt(B)} {Fmt(C)} {Fmt(D)} {Fmt(E)} {Fmt(F)} Tm";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>Td — Move text position: translate text origin by (X, Y).</summary>
public sealed class MoveTextPosition : TextPlaceOperator
{
    public double X { get; set; }
    public double Y { get; set; }

    public MoveTextPosition(double x, double y) { X = x; Y = y; }

    public override string ToPdf() => $"{Fmt(X)} {Fmt(Y)} Td";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>TD — Move text position and set leading: translate by (X, Y) and set leading to -Y.</summary>
public sealed class MoveTextPositionSetLeading : TextPlaceOperator
{
    public double X { get; set; }
    public double Y { get; set; }

    public MoveTextPositionSetLeading(double x, double y) { X = x; Y = y; }

    public override string ToPdf() => $"{Fmt(X)} {Fmt(Y)} TD";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>T* — Move to next line using current leading.</summary>
public sealed class MoveToNextLine : TextPlaceOperator
{
    public override string ToPdf() => "T*";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>Common base for text-showing operators (Tj, TJ, ', ").
/// Lets callers pattern-match on a single type when iterating an
/// <see cref="OperatorCollection"/>:
/// <code>foreach (Operator op in ops) if (op is TextShowOperator t) total += t.Text;</code>
/// </summary>
public abstract class TextShowOperator : Operator
{
    /// <summary>The text content shown by this operator (best-effort —
    /// for TJ the array's string parts are concatenated).</summary>
    public virtual string Text { get; set; } = string.Empty;

    public TextShowOperator() { }
    public TextShowOperator(Aspose.Pdf.Facades.TextProperties textProperties) { TextProperties = textProperties; }

    /// <summary>Optional appearance metadata for the text shown by this operator.</summary>
    public Aspose.Pdf.Facades.TextProperties? TextProperties { get; }
}

/// <summary>' — Move to next line and show text.</summary>
public sealed class MoveToNextLineShowText : TextShowOperator
{
    // Store in the base Text so polymorphic access through TextShowOperator
    // returns the shown text (was a `new` shadow that read empty via the base).
    public MoveToNextLineShowText() { }
    public MoveToNextLineShowText(string text) { base.Text = text ?? string.Empty; }
    public override string ToPdf() => $"({EscapeText(Text)}) '";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
    private static string EscapeText(string s) =>
        s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
}

/// <summary>" — Set word/char spacing, move to next line, and show text.</summary>
public sealed class SetSpacingMoveToNextLineShowText : TextShowOperator
{
    public double WordSpacing { get; }
    public double CharSpacing { get; }
    /// <summary>Aspose.PDF for .NET-shape alias for <see cref="WordSpacing"/>.</summary>
    public double Aw => WordSpacing;
    /// <summary>Aspose.PDF for .NET-shape alias for <see cref="CharSpacing"/>.</summary>
    public double Ac => CharSpacing;
    public SetSpacingMoveToNextLineShowText(double aw, double ac, string text)
    { WordSpacing = aw; CharSpacing = ac; base.Text = text ?? string.Empty; }
    public override string ToPdf() => $"{Fmt(WordSpacing)} {Fmt(CharSpacing)} ({EscapeText(Text)}) \"";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
    private static string EscapeText(string s) =>
        s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
}

/// <summary>TJ — Show text with individual glyph positioning (array of strings and numeric adjustments).</summary>
public sealed class SetGlyphsPositionShowText : TextShowOperator
{
    /// <summary>Mixed array of strings (text runs) and doubles (position adjustments in 1/1000 text units).</summary>
    public object[] Items { get; }

    /// <summary>Aspose.PDF for .NET-shape projection over <see cref="Items"/>: paired text-run / numeric-position
    /// entries surfaced as <see cref="GlyphPosition"/> instances.</summary>
    public System.Collections.Generic.IEnumerable<GlyphPosition> GlyphPositions
    {
        get
        {
            for (int i = 0; i < Items.Length; i++)
            {
                if (Items[i] is string s)
                {
                    if (i + 1 < Items.Length && Items[i + 1] is double d)
                    {
                        yield return new GlyphPosition(s, d);
                        i++;
                    }
                    else if (i + 1 < Items.Length && Items[i + 1] is int n)
                    {
                        yield return new GlyphPosition(s, n);
                        i++;
                    }
                    else
                    {
                        yield return new GlyphPosition(s);
                    }
                }
            }
        }
    }

    public SetGlyphsPositionShowText(object[] items) { Items = items ?? Array.Empty<object>(); }

    public SetGlyphsPositionShowText(System.Collections.Generic.IEnumerable<GlyphPosition> glyphPositions)
    {
        var list = new System.Collections.Generic.List<object>();
        if (glyphPositions is not null)
        {
            foreach (var gp in glyphPositions)
            {
                list.Add(gp.Text);
                if (gp.HasPosition) list.Add(gp.Position);
            }
        }
        Items = list.ToArray();
    }

    /// <summary>Concatenated string parts (numeric adjustments dropped).
    /// Overrides the base so polymorphic access through TextShowOperator
    /// returns the TJ text (was a `new` shadow that read empty via the base).</summary>
    public override string Text
    {
        get
        {
            var sb = new System.Text.StringBuilder();
            foreach (var it in Items)
                if (it is string s) sb.Append(s);
            return sb.ToString();
        }
    }

    public override string ToPdf()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append('[');
        foreach (var it in Items)
        {
            if (it is string s) sb.Append('(').Append(EscapeText(s)).Append(')');
            else if (it is double d) sb.Append(Fmt(d)).Append(' ');
            else if (it is int i) sb.Append(Fmt(i)).Append(' ');
        }
        sb.Append("] TJ");
        return sb.ToString();
    }
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
    private static string EscapeText(string s) =>
        s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
}

/// <summary>cm — Concatenate matrix to CTM.</summary>
public sealed class ConcatenateMatrix : Operator
{
    /// <summary>
    /// The transformation matrix. Returns an <see cref="Aspose.Pdf.Matrix"/>
    /// (public API parity with Aspose.PDF for .NET, whose `cm` operator
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
        $"{Fmt(Matrix.A)} {Fmt(Matrix.B)} {Fmt(Matrix.C)} {Fmt(Matrix.D)} {Fmt(Matrix.E)} {Fmt(Matrix.F)} cm";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>Do — Invoke named XObject.</summary>
public sealed class Do : Operator
{
    public string Name { get; set; }

    public Do() { Name = string.Empty; }
    public Do(string name) { Name = name; }

    public override string ToPdf() => $"/{Name} Do";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>Tj — Show text string.</summary>
public sealed class ShowText : TextShowOperator
{
    private string _text;
    private readonly FontInfo? _font;

    public override string Text
    {
        get => _text;
        set => _text = value ?? string.Empty;
    }

    public ShowText() { _text = string.Empty; }
    public ShowText(string text) { _text = text ?? string.Empty; }
    public ShowText(string text, Aspose.Pdf.Text.Font font) { _text = text ?? string.Empty; _font = font; }
    public ShowText(int index, string text) { _text = text ?? string.Empty; _ = index; }

    /// <summary>Optional font hint kept for back-compat — does not surface in
    /// the Aspose.PDF for .NET reflection surface.</summary>
    internal ShowText(string text, FontInfo? font) { _text = text ?? string.Empty; _font = font; }

    public override string ToPdf()
    {
        var escaped = _text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        return $"({escaped}) Tj";
    }
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

// =====================================================================
// Abstract bases — for tests pattern-matching on operator categories.
// =====================================================================

/// <summary>Common base for all text-related operators (BT/ET, Tx state, text-show, text-place).</summary>
public abstract class TextOperator : Operator
{
    public TextOperator() { }
    public TextOperator(Aspose.Pdf.Facades.TextProperties textProperties) { TextProperties = textProperties; }

    /// <summary>Optional appearance metadata.</summary>
    public Aspose.Pdf.Facades.TextProperties? TextProperties { get; }

    /// <summary>Default IOperatorSelector dispatch — concrete subclasses generally
    /// shadow this with a typed Visit call.</summary>
    public override void Accept(IOperatorSelector visitor) { _ = visitor; }
}

/// <summary>Common base for text-state operators (Tc, Tw, Tz, TL, Tf, Tr, Ts).</summary>
public abstract class TextStateOperator : TextOperator
{
    public TextStateOperator() { }
    public TextStateOperator(Aspose.Pdf.Facades.TextProperties textProperties) : base(textProperties) { }
}

/// <summary>Common base for text-positioning operators (Td, TD, T*, Tm).</summary>
public abstract class TextPlaceOperator : TextOperator
{
    public TextPlaceOperator() { }
    public TextPlaceOperator(Aspose.Pdf.Facades.TextProperties textProperties) : base(textProperties) { }
}

/// <summary>Common base for the BT / ET delimiters.</summary>
public abstract class BlockTextOperator : TextOperator
{
    public BlockTextOperator() { }
    public BlockTextOperator(Aspose.Pdf.Facades.TextProperties textProperties) : base(textProperties) { }
}

/// <summary>Common base for the SC/SCN/sc/scn family.</summary>
public abstract class SetColorOperator : Operator
{
    /// <summary>Return the operator's colour as a System.Drawing.Color.
    /// Override on concrete subclasses; the base returns black.</summary>
    public virtual System.Drawing.Color getColor() => System.Drawing.Color.Black;

    /// <summary>Map a PDF color array (1=gray, 3=RGB, 4=CMYK) to a System.Drawing.Color.</summary>
    internal static System.Drawing.Color ToSystemColor(double[] color)
    {
        if (color is null || color.Length == 0) return System.Drawing.Color.Black;
        if (color.Length == 1)
        {
            var g = Clamp255(color[0]);
            return System.Drawing.Color.FromArgb(g, g, g);
        }
        if (color.Length == 3)
            return System.Drawing.Color.FromArgb(Clamp255(color[0]), Clamp255(color[1]), Clamp255(color[2]));
        if (color.Length >= 4)
        {
            // Use the SWOP-style CMYK→sRGB profile LUT (the same transform the renderer
            // uses) rather than a naive cutoff — a CMYK black (K=1) maps to a rich black,
            // not pure (0,0,0), matching the colour-managed result.
            var (r, g, b) = Aspose.Pdf.Devices.CmykToRgbLut.Convert(color[0], color[1], color[2], color[3]);
            return System.Drawing.Color.FromArgb(r, g, b);
        }
        return System.Drawing.Color.Black;
    }

    private static int Clamp255(double v) => v <= 0 ? 0 : v >= 1 ? 255 : (int)System.Math.Round(v * 255);
}

/// <summary>Common base for the rg/RG/g/G/k/K family. Exposes get-only
/// projections over the color components; concrete subclasses (SetGray,
/// SetRGBColor, SetCMYKColor, …) shadow with their own typed get/set
/// storage via the `new` keyword.</summary>
public abstract class BasicSetColorOperator : SetColorOperator
{
    public virtual double[] Color => Array.Empty<double>();
    public virtual double Gray => 0;
    public virtual double R => 0;
    public virtual double G => 0;
    public virtual double B => 0;
    public virtual double C => 0;
    public virtual double M => 0;
    public virtual double Y => 0;
    public virtual double K => 0;
}

/// <summary>Common base for SCN/scn (basic color or pattern).</summary>
public abstract class BasicSetColorAndPatternOperator : SetColorOperator
{
    /// <summary>Pattern resource name used by this operator, or empty when this is a plain colour.</summary>
    public virtual string PatternName => string.Empty;
}

// =====================================================================
// Enums used by graphics-state operators.
// =====================================================================

/// <summary>Line cap style. PDF 32000-1 §8.4.3.3.</summary>
public enum LineCap { ButtCap = 0, RoundCap = 1, ProjectingSquareCap = 2, SquareCap = ProjectingSquareCap }

/// <summary>Line join style. PDF 32000-1 §8.4.3.4.</summary>
public enum LineJoin { MiterJoin = 0, RoundJoin = 1, BevelJoin = 2 }

// =====================================================================
// Path construction operators (PDF 32000-1 §8.5.2).
// =====================================================================

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

/// <summary>S — Stroke path.</summary>
public sealed class Stroke : Operator
{
    public override string ToPdf() => "S";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>s — Close and stroke path.</summary>
public sealed class ClosePathStroke : Operator
{
    public override string ToPdf() => "s";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>f — Fill path (nonzero winding rule).</summary>
public sealed class Fill : Operator
{
    public override string ToPdf() => "f";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>F — Fill path (deprecated; equivalent to f).</summary>
public sealed class ObsoleteFill : Operator
{
    public override string ToPdf() => "F";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>f* — Fill path (even-odd rule).</summary>
public sealed class EOFill : Operator
{
    public override string ToPdf() => "f*";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>B — Fill and stroke path (nonzero winding rule).</summary>
public sealed class FillStroke : Operator
{
    public override string ToPdf() => "B";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>B* — Fill and stroke path (even-odd rule).</summary>
public sealed class EOFillStroke : Operator
{
    public override string ToPdf() => "B*";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>b — Close, fill, and stroke path (nonzero winding rule).</summary>
public sealed class ClosePathFillStroke : Operator
{
    public override string ToPdf() => "b";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>b* — Close, fill, and stroke path (even-odd rule).</summary>
public sealed class ClosePathEOFillStroke : Operator
{
    public override string ToPdf() => "b*";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>n — End path without filling or stroking.</summary>
public sealed class EndPath : Operator
{
    public override string ToPdf() => "n";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

// =====================================================================
// Graphics-state operators (PDF 32000-1 §8.4.4).
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
    /// <summary>Aspose.PDF for .NET-shape alias for <see cref="LineWidth"/>.</summary>
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
    /// <summary>Aspose.PDF for .NET-shape alias for <see cref="DashArray"/>.</summary>
    public int[] Pattern { get => DashArray; set => DashArray = value; }
    /// <summary>Aspose.PDF for .NET-shape alias for <see cref="DashPhase"/>.</summary>
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

/// <summary>g — Set gray fill color.</summary>
public sealed class SetGray : BasicSetColorOperator
{
    public new double Gray { get; set; }
    public SetGray(double gray) { Gray = gray; }
    public override string ToPdf() => $"{FmtColor(Gray)} g";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
    public override System.Drawing.Color getColor() => SetColorOperator.ToSystemColor(new[] { Gray });
}

/// <summary>G — Set gray stroke color.</summary>
public sealed class SetGrayStroke : BasicSetColorOperator
{
    public new double Gray { get; set; }
    public SetGrayStroke(double gray) { Gray = gray; }
    public override string ToPdf() => $"{FmtColor(Gray)} G";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
    public override System.Drawing.Color getColor() => SetColorOperator.ToSystemColor(new[] { Gray });
}

/// <summary>RG — Set RGB stroke color.</summary>
public sealed class SetRGBColorStroke : BasicSetColorOperator
{
    public new double R { get; set; }
    public new double G { get; set; }
    public new double B { get; set; }
    public SetRGBColorStroke(double r, double g, double b) { R = r; G = g; B = b; }
    public SetRGBColorStroke(System.Drawing.Color color)
        : this(color.R / 255.0, color.G / 255.0, color.B / 255.0) { }
    public override string ToPdf() => $"{FmtColor(R)} {FmtColor(G)} {FmtColor(B)} RG";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
    public override System.Drawing.Color getColor() => SetColorOperator.ToSystemColor(new[] { R, G, B });
}

/// <summary>k — Set CMYK fill color.</summary>
public sealed class SetCMYKColor : BasicSetColorOperator
{
    public new double C { get; set; }
    public new double M { get; set; }
    public new double Y { get; set; }
    public new double K { get; set; }
    public SetCMYKColor(double c, double m, double y, double k) { C = c; M = m; Y = y; K = k; }
    public override string ToPdf() => $"{FmtColor(C)} {FmtColor(M)} {FmtColor(Y)} {FmtColor(K)} k";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
    public override System.Drawing.Color getColor() => SetColorOperator.ToSystemColor(new[] { C, M, Y, K });
}

/// <summary>K — Set CMYK stroke color.</summary>
public sealed class SetCMYKColorStroke : BasicSetColorOperator
{
    public new double C { get; set; }
    public new double M { get; set; }
    public new double Y { get; set; }
    public new double K { get; set; }
    public SetCMYKColorStroke(double c, double m, double y, double k) { C = c; M = m; Y = y; K = k; }
    public override string ToPdf() => $"{FmtColor(C)} {FmtColor(M)} {FmtColor(Y)} {FmtColor(K)} K";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
    public override System.Drawing.Color getColor() => SetColorOperator.ToSystemColor(new[] { C, M, Y, K });
}

/// <summary>cs — Set color space for non-stroking operations.</summary>
public sealed class SetColorSpace : Operator
{
    public string Name { get; set; }
    public SetColorSpace(string name) { Name = name; }
    public override string ToPdf() => $"/{Name} cs";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>CS — Set color space for stroking operations.</summary>
public sealed class SetColorSpaceStroke : Operator
{
    public string Name { get; set; }
    public SetColorSpaceStroke(string name) { Name = name; }
    public override string ToPdf() => $"/{Name} CS";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>sc — Set color in current color space (non-stroking).</summary>
public sealed class SetColor : SetColorOperator
{
    public double[] Color { get; private set; }
    public SetColor() { Color = Array.Empty<double>(); }
    public SetColor(double g) { Color = new[] { g }; }
    public SetColor(double r, double g, double b) { Color = new[] { r, g, b }; }
    public SetColor(double c, double m, double y, double k) { Color = new[] { c, m, y, k }; }
    public SetColor(double[] color) { Color = color ?? Array.Empty<double>(); }
    public override string ToPdf()
    {
        var sb = new StringBuilder();
        foreach (var c in Color) sb.Append(Fmt(c)).Append(' ');
        sb.Append("sc");
        return sb.ToString();
    }
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);

    public double R { get => Color.Length >= 3 ? Color[0] : 0; set => SetSlotRgb(0, value); }
    public double G { get => Color.Length == 1 ? Color[0] : (Color.Length >= 3 ? Color[1] : 0); set => SetSlotG(value); }
    public double B { get => Color.Length >= 3 ? Color[2] : 0; set => SetSlotRgb(2, value); }
    public double C { get => Color.Length == 4 ? Color[0] : 0; set => SetSlotCmyk(0, value); }
    public double M { get => Color.Length == 4 ? Color[1] : 0; set => SetSlotCmyk(1, value); }
    public double Y { get => Color.Length == 4 ? Color[2] : 0; set => SetSlotCmyk(2, value); }
    public double K { get => Color.Length == 4 ? Color[3] : 0; set => SetSlotCmyk(3, value); }

    public override System.Drawing.Color getColor() => SetColorOperator.ToSystemColor(Color);

    private void SetSlotRgb(int idx, double v) { if (Color.Length < 3) Color = new double[3]; Color[idx] = v; }
    private void SetSlotCmyk(int idx, double v) { if (Color.Length < 4) Color = new double[4]; Color[idx] = v; }
    private void SetSlotG(double v)
    {
        if (Color.Length == 0) Color = new double[1];
        if (Color.Length == 1) Color[0] = v;
        else if (Color.Length >= 3) Color[1] = v;
    }
}

/// <summary>SC — Set color in current color space (stroking).</summary>
public sealed class SetColorStroke : SetColorOperator
{
    public double[] Color { get; private set; }
    public SetColorStroke() { Color = Array.Empty<double>(); }
    public SetColorStroke(double g) { Color = new[] { g }; }
    public SetColorStroke(double r, double g, double b) { Color = new[] { r, g, b }; }
    public SetColorStroke(double c, double m, double y, double k) { Color = new[] { c, m, y, k }; }
    public SetColorStroke(double[] color) { Color = color ?? Array.Empty<double>(); }
    public override string ToPdf()
    {
        var sb = new StringBuilder();
        foreach (var c in Color) sb.Append(Fmt(c)).Append(' ');
        sb.Append("SC");
        return sb.ToString();
    }
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);

    public double R { get => Color.Length >= 3 ? Color[0] : 0; set => SetSlotRgb(0, value); }
    public double G { get => Color.Length == 1 ? Color[0] : (Color.Length >= 3 ? Color[1] : 0); set => SetSlotG(value); }
    public double B { get => Color.Length >= 3 ? Color[2] : 0; set => SetSlotRgb(2, value); }
    public double C { get => Color.Length == 4 ? Color[0] : 0; set => SetSlotCmyk(0, value); }
    public double M { get => Color.Length == 4 ? Color[1] : 0; set => SetSlotCmyk(1, value); }
    public double Y { get => Color.Length == 4 ? Color[2] : 0; set => SetSlotCmyk(2, value); }
    public double K { get => Color.Length == 4 ? Color[3] : 0; set => SetSlotCmyk(3, value); }

    public override System.Drawing.Color getColor() => SetColorOperator.ToSystemColor(Color);

    private void SetSlotRgb(int idx, double v) { if (Color.Length < 3) Color = new double[3]; Color[idx] = v; }
    private void SetSlotCmyk(int idx, double v) { if (Color.Length < 4) Color = new double[4]; Color[idx] = v; }
    private void SetSlotG(double v)
    {
        if (Color.Length == 0) Color = new double[1];
        if (Color.Length == 1) Color[0] = v;
        else if (Color.Length >= 3) Color[1] = v;
    }
}

/// <summary>scn — Set color (with optional pattern name) in current color space (non-stroking).</summary>
public sealed class SetAdvancedColor : BasicSetColorAndPatternOperator
{
    public double[] Color { get; }
    public new string? PatternName { get; }
    public SetAdvancedColor() { Color = Array.Empty<double>(); }
    public SetAdvancedColor(double g) { Color = new[] { g }; }
    public SetAdvancedColor(string patternName) { Color = Array.Empty<double>(); PatternName = patternName; }
    public SetAdvancedColor(double g, string patternName)
    { Color = new[] { g }; PatternName = patternName; }
    public SetAdvancedColor(double[] colors, string patternName)
    { Color = colors ?? Array.Empty<double>(); PatternName = patternName; }
    public SetAdvancedColor(double r, double g, double b, string patternName)
    { Color = new[] { r, g, b }; PatternName = patternName; }
    public SetAdvancedColor(double c, double m, double y, double k, string patternName)
    { Color = new[] { c, m, y, k }; PatternName = patternName; }
    public override string ToPdf()
    {
        var sb = new StringBuilder();
        foreach (var c in Color) sb.Append(Fmt(c)).Append(' ');
        if (PatternName is not null) sb.Append('/').Append(PatternName).Append(' ');
        sb.Append("scn");
        return sb.ToString();
    }
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
    public override System.Drawing.Color getColor() => SetColorOperator.ToSystemColor(Color);
}

/// <summary>SCN — Set color (with optional pattern name) in current color space (stroking).</summary>
public sealed class SetAdvancedColorStroke : BasicSetColorAndPatternOperator
{
    public double[] Color { get; }
    public new string? PatternName { get; }
    public SetAdvancedColorStroke() { Color = Array.Empty<double>(); }
    public SetAdvancedColorStroke(double g) { Color = new[] { g }; }
    public SetAdvancedColorStroke(string patternName) { Color = Array.Empty<double>(); PatternName = patternName; }
    public SetAdvancedColorStroke(double g, string patternName)
    { Color = new[] { g }; PatternName = patternName; }
    public SetAdvancedColorStroke(double[] colors, string patternName)
    { Color = colors ?? Array.Empty<double>(); PatternName = patternName; }
    public SetAdvancedColorStroke(double r, double g, double b, string patternName)
    { Color = new[] { r, g, b }; PatternName = patternName; }
    public SetAdvancedColorStroke(double c, double m, double y, double k, string patternName)
    { Color = new[] { c, m, y, k }; PatternName = patternName; }
    public override string ToPdf()
    {
        var sb = new StringBuilder();
        foreach (var c in Color) sb.Append(Fmt(c)).Append(' ');
        if (PatternName is not null) sb.Append('/').Append(PatternName).Append(' ');
        sb.Append("SCN");
        return sb.ToString();
    }
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
    public override System.Drawing.Color getColor() => SetColorOperator.ToSystemColor(Color);
}

/// <summary>ri — Set color rendering intent.</summary>
public sealed class SetColorRenderingIntent : Operator
{
    public string RenderingIntent { get; set; }
    /// <summary>Aspose.PDF for .NET-shape alias for <see cref="RenderingIntent"/>.</summary>
    public string IntentName { get => RenderingIntent; set => RenderingIntent = value; }
    public SetColorRenderingIntent() { RenderingIntent = "RelativeColorimetric"; }
    public SetColorRenderingIntent(string intentName) { RenderingIntent = intentName; }
    public override string ToPdf() => $"/{RenderingIntent} ri";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

// =====================================================================
// Text-state operators (PDF 32000-1 §9.3).
// =====================================================================

/// <summary>Tc — Set character spacing.</summary>
public sealed class SetCharacterSpacing : TextStateOperator
{
    public double CharSpace { get; set; }
    /// <summary>Aspose.PDF for .NET-shape alias for <see cref="CharSpace"/>.</summary>
    public double CharSpacing { get => CharSpace; set => CharSpace = value; }
    public SetCharacterSpacing(double charSpacing) { CharSpace = charSpacing; }
    public override string ToPdf() => $"{Fmt(CharSpace)} Tc";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>Tw — Set word spacing.</summary>
public sealed class SetWordSpacing : TextStateOperator
{
    public double WordSpace { get; set; }
    /// <summary>Aspose.PDF for .NET-shape alias for <see cref="WordSpace"/>.</summary>
    public double WordSpacing { get => WordSpace; set => WordSpace = value; }
    public SetWordSpacing(double wordSpacing) { WordSpace = wordSpacing; }
    public override string ToPdf() => $"{Fmt(WordSpace)} Tw";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>Tz — Set horizontal text scaling.</summary>
public sealed class SetHorizontalTextScaling : TextStateOperator
{
    public double Scale { get; set; }
    /// <summary>Aspose.PDF for .NET-shape alias for <see cref="Scale"/>.</summary>
    public double HorizontalScaling { get => Scale; set => Scale = value; }
    public SetHorizontalTextScaling(double horizintalScaling) { Scale = horizintalScaling; }
    public override string ToPdf() => $"{Fmt(Scale)} Tz";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>TL — Set text leading.</summary>
public sealed class SetTextLeading : TextStateOperator
{
    public double Leading { get; set; }
    public SetTextLeading(double leading) { Leading = leading; }
    public override string ToPdf() => $"{Fmt(Leading)} TL";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>Tr — Set text rendering mode.</summary>
public sealed class SetTextRenderingMode : TextStateOperator
{
    public int RenderingMode { get; }
    public SetTextRenderingMode() { RenderingMode = 0; }
    public SetTextRenderingMode(int renderingMode) { RenderingMode = renderingMode; }
    public override string ToPdf() => $"{RenderingMode.ToString(CultureInfo.InvariantCulture)} Tr";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>Ts — Set text rise.</summary>
public sealed class SetTextRise : TextStateOperator
{
    public double Rise { get; set; }
    /// <summary>Aspose.PDF for .NET-shape alias for <see cref="Rise"/>.</summary>
    public double TextRise { get => Rise; set => Rise = value; }
    public SetTextRise(double textRise) { Rise = textRise; }
    public override string ToPdf() => $"{Fmt(Rise)} Ts";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

// =====================================================================
// Type 3 font operators (PDF 32000-1 §9.6.5).
// =====================================================================

/// <summary>d0 — Set glyph width in a Type 3 font.</summary>
public sealed class SetCharWidth : Operator
{
    public double Wx { get; }
    public double Wy { get; }
    public SetCharWidth(double wx, double wy) { Wx = wx; Wy = wy; }
    public override string ToPdf() => $"{Fmt(Wx)} {Fmt(Wy)} d0";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>d1 — Set glyph width and bounding box in a Type 3 font.</summary>
public sealed class SetCharWidthBoundingBox : Operator
{
    public double Wx { get; }
    public double Wy { get; }
    public double LLx { get; }
    public double LLy { get; }
    public double URx { get; }
    public double URy { get; }
    /// <summary>Aspose.PDF for .NET-shape camel-cased alias for <see cref="LLx"/>.</summary>
    public double Llx => LLx;
    /// <summary>Aspose.PDF for .NET-shape camel-cased alias for <see cref="LLy"/>.</summary>
    public double Lly => LLy;
    /// <summary>Aspose.PDF for .NET-shape camel-cased alias for <see cref="URx"/>.</summary>
    public double Urx => URx;
    /// <summary>Aspose.PDF for .NET-shape camel-cased alias for <see cref="URy"/>.</summary>
    public double Ury => URy;
    public SetCharWidthBoundingBox(double wx, double wy, double llx, double lly, double urx, double ury)
    { Wx = wx; Wy = wy; LLx = llx; LLy = lly; URx = urx; URy = ury; }
    public override string ToPdf() =>
        $"{Fmt(Wx)} {Fmt(Wy)} {Fmt(LLx)} {Fmt(LLy)} {Fmt(URx)} {Fmt(URy)} d1";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

// =====================================================================
// BT / ET (text-block delimiters; reparented for the abstract hierarchy).
// =====================================================================
// NOTE: BT and ET are already defined above (sealed : Operator). C# cannot
// re-derive a sealed class so we leave them as-is. The BlockTextOperator
// abstract base exists for surface parity but lib's BT / ET do not derive
// from it. Tests that pattern-match on BlockTextOperator will see false
// for BT/ET — flagged as a known surface gap.

// =====================================================================
// Marked-content operators (PDF 32000-1 §14.6).
// =====================================================================

/// <summary>BMC — Begin marked-content sequence (no properties).</summary>
public sealed class BMC : Operator
{
    public string Tag { get; set; }
    public BMC(string tag) { Tag = tag; }
    public override string ToPdf() => $"/{Tag} BMC";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>MP — Designate marked-content point (no properties).</summary>
public sealed class MP : Operator
{
    public string Tag { get; set; }
    public MP(string tag) { Tag = tag; }
    public override string ToPdf() => $"/{Tag} MP";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>DP — Designate marked-content point with property list.</summary>
public sealed class DP : Operator
{
    public string Tag { get; set; }
    public Aspose.Pdf.Facades.BDCProperties? Properties { get; }

    /// <summary>The marked-content property list as a name-keyed dictionary
    /// (the modelled /MCID, /Lang and /E entries), mirroring the Aspose.PDF for .NET
    /// DP.PropertiesDictionary accessor. Empty when no /Properties are present.</summary>
    public System.Collections.Generic.Dictionary<string, object> PropertiesDictionary
    {
        get
        {
            var d = new System.Collections.Generic.Dictionary<string, object>();
            if (Properties is { } p)
            {
                if (p.MCID.HasValue) d["MCID"] = p.MCID.Value;
                if (p.Lang is not null) d["Lang"] = p.Lang;
                if (p.E is not null) d["E"] = p.E;
            }
            return d;
        }
    }

    public DP(string tag) { Tag = tag; }
    public DP(string tag, Aspose.Pdf.Facades.BDCProperties properties) { Tag = tag; Properties = properties; }
    public override string ToPdf() =>
        Properties is null ? $"/{Tag} DP" : $"/{Tag} {Properties.ToPdf()} DP";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

// =====================================================================
// Compatibility operators (PDF 32000-1 §14.10).
// =====================================================================

/// <summary>BX — Begin compatibility section.</summary>
public sealed class BX : Operator
{
    public override string ToPdf() => "BX";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>EX — End compatibility section.</summary>
public sealed class EX : Operator
{
    public override string ToPdf() => "EX";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

// =====================================================================
// Inline-image operators (PDF 32000-1 §8.9.7).
// =====================================================================

/// <summary>BI — Begin inline-image object.</summary>
public sealed class BI : Operator
{
    public override string ToPdf() => "BI";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>ID — Begin image data (after the inline-image dictionary).</summary>
public sealed class ID : Operator
{
    public override string ToPdf() => "ID";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>EI — End inline-image object.</summary>
public sealed class EI : Operator
{
    public override string ToPdf() => "EI";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

// =====================================================================
// Shading operator (PDF 32000-1 §8.7.4).
// =====================================================================

/// <summary>sh — Paint shading specified by named resource.</summary>
public sealed class ShFill : Operator
{
    public string Name { get; set; }
    public ShFill(string shadingName) { Name = shadingName; }
    public override string ToPdf() => $"/{Name} sh";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

// =====================================================================
// GlyphPosition — helper for TJ array entries.
// =====================================================================

/// <summary>One element of a TJ-style glyph-position array: a string with
/// an optional preceding/following position adjustment (in 1/1000 text units).</summary>
public sealed class GlyphPosition
{
    public string Text { get; }
    public double Position { get; }
    public bool HasPosition { get; }
    public GlyphPosition(string text) { Text = text; HasPosition = false; }
    public GlyphPosition(string text, double position)
    { Text = text; Position = position; HasPosition = true; }
}

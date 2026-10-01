// PDF content stream operators — PDF32000_2008 §8–9
using System.Globalization;
using System.Text;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Operators;

/// <summary>W — Set clipping path (nonzero winding rule).</summary>
public sealed class Clip : Operator
{
    /// <summary>Creates a <c>W</c> operator, which intersects the clipping path with the current path using the nonzero winding rule.</summary>
    public Clip() { }
    public override string ToPdf() => "W";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>W* — Set clipping path (even-odd rule).</summary>
public sealed class EOClip : Operator
{
    /// <summary>Creates a <c>W*</c> operator, which intersects the clipping path with the current path using the even-odd rule.</summary>
    public EOClip() { }
    public override string ToPdf() => "W*";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>S — Stroke path.</summary>
public sealed class Stroke : Operator
{
    /// <summary>Creates an <c>S</c> operator, which strokes the current path.</summary>
    public Stroke() { }
    public override string ToPdf() => "S";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>s — Close and stroke path.</summary>
public sealed class ClosePathStroke : Operator
{
    /// <summary>Creates an <c>s</c> operator, which closes the current subpath and strokes the path.</summary>
    public ClosePathStroke() { }
    public override string ToPdf() => "s";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>f — Fill path (nonzero winding rule).</summary>
public sealed class Fill : Operator
{
    /// <summary>Creates an <c>f</c> operator, which fills the current path using the nonzero winding rule.</summary>
    public Fill() { }
    public override string ToPdf() => "f";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>F — Fill path (deprecated; equivalent to f).</summary>
public sealed class ObsoleteFill : Operator
{
    /// <summary>Creates an <c>F</c> operator, the obsolete equivalent of <c>f</c>, which fills the current path using the nonzero winding rule.</summary>
    public ObsoleteFill() { }
    public override string ToPdf() => "F";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>f* — Fill path (even-odd rule).</summary>
public sealed class EOFill : Operator
{
    /// <summary>Creates an <c>f*</c> operator, which fills the current path using the even-odd rule.</summary>
    public EOFill() { }
    public override string ToPdf() => "f*";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>B — Fill and stroke path (nonzero winding rule).</summary>
public sealed class FillStroke : Operator
{
    /// <summary>Creates a <c>B</c> operator, which fills the current path using the nonzero winding rule and then strokes it.</summary>
    public FillStroke() { }
    public override string ToPdf() => "B";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>B* — Fill and stroke path (even-odd rule).</summary>
public sealed class EOFillStroke : Operator
{
    /// <summary>Creates a <c>B*</c> operator, which fills the current path using the even-odd rule and then strokes it.</summary>
    public EOFillStroke() { }
    public override string ToPdf() => "B*";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>b — Close, fill, and stroke path (nonzero winding rule).</summary>
public sealed class ClosePathFillStroke : Operator
{
    /// <summary>Creates a <c>b</c> operator, which closes the current subpath, fills the path using the nonzero winding rule and then strokes it.</summary>
    public ClosePathFillStroke() { }
    public override string ToPdf() => "b";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>b* — Close, fill, and stroke path (even-odd rule).</summary>
public sealed class ClosePathEOFillStroke : Operator
{
    /// <summary>Creates a <c>b*</c> operator, which closes the current subpath, fills the path using the even-odd rule and then strokes it.</summary>
    public ClosePathEOFillStroke() { }
    public override string ToPdf() => "b*";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>n — End path without filling or stroking.</summary>
public sealed class EndPath : Operator
{
    /// <summary>Creates an <c>n</c> operator, which ends the current path without filling or stroking it; it usually follows a clipping operator.</summary>
    public EndPath() { }
    public override string ToPdf() => "n";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

// =====================================================================
// Graphics-state operators (PDF 32000-1 §8.4.4).
// =====================================================================

/// <summary>sh — Paint shading specified by named resource.</summary>
public sealed class ShFill : Operator
{
    /// <summary>Gets or sets the name of the shading resource, without the leading slash.</summary>
    public string Name { get; set; }
    /// <summary>Creates an <c>sh</c> operator that paints the named shading resource (given without the leading slash) over the current clipping area.</summary>
    public ShFill(string shadingName) { Name = shadingName; }
    public override string ToPdf() => $"/{Name} sh";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

// =====================================================================
// GlyphPosition — helper for TJ array entries.
// =====================================================================

// PDF content stream operators — PDF32000_2008 §8–9
using System.Globalization;
using System.Text;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Operators;

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

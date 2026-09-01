// PDF content stream operators — PDF32000_2008 §8–9
using System.Globalization;
using System.Text;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Operators;

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

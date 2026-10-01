// PDF content stream operators — PDF32000_2008 §8–9
using System.Globalization;
using System.Text;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Operators;

/// <summary>Do — Invoke named XObject.</summary>
public sealed class Do : Operator
{
    /// <summary>Gets or sets the name of the XObject resource, without the leading slash.</summary>
    public string Name { get; set; }

    /// <summary>Creates a <c>Do</c> operator with an empty XObject name.</summary>
    public Do() { Name = string.Empty; }
    /// <summary>Creates a <c>Do</c> operator that paints the named XObject resource, such as an image or a form, given without the leading slash.</summary>
    public Do(string name) { Name = name; }

    public override string ToPdf() => $"/{Name} Do";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>BI — Begin inline-image object.</summary>
public sealed class BI : Operator
{
    /// <summary>Creates a <c>BI</c> operator, which begins an inline image object.</summary>
    public BI() { }
    public override string ToPdf() => "BI";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>ID — Begin image data (after the inline-image dictionary).</summary>
public sealed class ID : Operator
{
    /// <summary>Creates an <c>ID</c> operator, which ends the inline image dictionary and begins the image data.</summary>
    public ID() { }
    public override string ToPdf() => "ID";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>EI — End inline-image object.</summary>
public sealed class EI : Operator
{
    /// <summary>Creates an <c>EI</c> operator, which ends an inline image object.</summary>
    public EI() { }
    public override string ToPdf() => "EI";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

// =====================================================================
// Shading operator (PDF 32000-1 §8.7.4).
// =====================================================================

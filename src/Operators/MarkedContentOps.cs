// PDF content stream operators — PDF32000_2008 §8–9
using System.Globalization;
using System.Text;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Operators;

/// <summary>EMC — End marked content.</summary>
public sealed class EMC : Operator
{
    /// <summary>Creates an <c>EMC</c> operator, which ends the innermost marked-content sequence opened by <c>BMC</c> or <c>BDC</c>.</summary>
    public EMC() { }
    public override string ToPdf() => "EMC";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>BDC — Begin marked content with properties.</summary>
public sealed class BDC : Operator
{
    /// <summary>Gets or sets the marked-content tag, such as <c>Span</c> or <c>P</c>, without the leading slash.</summary>
    public string Tag { get; set; }
    /// <summary>Gets the property list attached to the sequence, or null when there is none.</summary>
    public Aspose.Pdf.Facades.BDCProperties? Properties { get; private set; }

    /// <summary>Gives the sequence the marked-content id <paramref name="mcid"/>, keeping whatever
    /// else its property list said: what tagging a sequence into a structure element needs.</summary>
    internal void Retag(int mcid)
    {
        var was = Properties;
        Properties = new Aspose.Pdf.Facades.BDCProperties(mcid, was?.Lang, was?.E) { ActualText = was?.ActualText };
    }

    /// <summary>Creates a <c>BDC</c> operator that begins a marked-content sequence with the given tag (without the leading slash) and no property list.</summary>
    public BDC(string tag) { Tag = tag; }
    /// <summary>Creates a <c>BDC</c> operator that begins a marked-content sequence with the given tag (without the leading slash) and property list, such as an MCID or language.</summary>
    public BDC(string tag, Aspose.Pdf.Facades.BDCProperties properties) { Tag = tag; Properties = properties; }

    public override string ToPdf() =>
        Properties is null ? $"/{Tag} BDC" : $"/{Tag} {Properties.ToPdf()} BDC";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>BMC — Begin marked-content sequence (no properties).</summary>
public sealed class BMC : Operator
{
    /// <summary>Gets or sets the marked-content tag, without the leading slash.</summary>
    public string Tag { get; set; }
    /// <summary>Creates a <c>BMC</c> operator that begins a marked-content sequence with the given tag (without the leading slash) and no property list.</summary>
    public BMC(string tag) { Tag = tag; }
    public override string ToPdf() => $"/{Tag} BMC";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>MP — Designate marked-content point (no properties).</summary>
public sealed class MP : Operator
{
    /// <summary>Gets or sets the marked-content tag, without the leading slash.</summary>
    public string Tag { get; set; }
    /// <summary>Creates an <c>MP</c> operator that marks a single point in the content stream with the given tag (without the leading slash).</summary>
    public MP(string tag) { Tag = tag; }
    public override string ToPdf() => $"/{Tag} MP";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>DP — Designate marked-content point with property list.</summary>
public sealed class DP : Operator
{
    /// <summary>Gets or sets the marked-content tag, without the leading slash.</summary>
    public string Tag { get; set; }
    /// <summary>Gets the property list attached to the point, or null when there is none.</summary>
    public Aspose.Pdf.Facades.BDCProperties? Properties { get; }

    /// <summary>The marked-content property list as a name-keyed dictionary
    /// (the modelled /MCID, /Lang and /E entries).
    /// Empty when no /Properties are present.</summary>
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

    /// <summary>Creates a <c>DP</c> operator that marks a single point with the given tag (without the leading slash) and no property list.</summary>
    public DP(string tag) { Tag = tag; }
    /// <summary>Creates a <c>DP</c> operator that marks a single point with the given tag (without the leading slash) and property list, such as an MCID or language.</summary>
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
    /// <summary>Creates a <c>BX</c> operator, which begins a compatibility section in which a reader ignores operators it does not recognise.</summary>
    public BX() { }
    public override string ToPdf() => "BX";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>EX — End compatibility section.</summary>
public sealed class EX : Operator
{
    /// <summary>Creates an <c>EX</c> operator, which ends a compatibility section begun by <c>BX</c>.</summary>
    public EX() { }
    public override string ToPdf() => "EX";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

// =====================================================================
// Inline-image operators (PDF 32000-1 §8.9.7).
// =====================================================================

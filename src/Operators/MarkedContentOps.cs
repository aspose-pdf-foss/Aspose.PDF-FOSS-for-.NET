// PDF content stream operators — PDF32000_2008 §8–9
using System.Globalization;
using System.Text;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Operators;

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

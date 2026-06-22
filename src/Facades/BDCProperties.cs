using System.Text;

namespace Aspose.Pdf.Facades;

/// <summary>Properties for a BDC / DP marked-content operator (/MCID and /Lang).</summary>
public sealed class BDCProperties
{
    public int? MCID { get; }

    /// <summary>Language tag (/Lang entry).</summary>
    public string? Lang { get; set; }

    /// <summary>Expansion text (/E entry).</summary>
    public string? E { get; set; }

    public BDCProperties(string lang) { Lang = lang; }
    public BDCProperties(int mcid, string lang) { MCID = mcid; Lang = lang; }

    /// <summary>Construct with language + expansion text but no /MCID.</summary>
    public BDCProperties(string lang, string expansionText)
    {
        Lang = lang;
        E = expansionText;
    }

    /// <summary>Full ctor with optional /MCID + language + expansion text.</summary>
    public BDCProperties(int? mcid, string lang, string expansionText)
    {
        MCID = mcid;
        Lang = lang;
        E = expansionText;
    }

    internal string ToPdf()
    {
        var sb = new StringBuilder("<<");
        if (MCID.HasValue) sb.Append($" /MCID {MCID.Value}");
        if (Lang is not null) sb.Append($" /Lang ({Lang})");
        if (E is not null) sb.Append($" /E ({E})");
        sb.Append(" >>");
        return sb.ToString();
    }
}

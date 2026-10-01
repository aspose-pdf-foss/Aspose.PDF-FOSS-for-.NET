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

    /// <summary>
    /// The text this marked-content sequence really says (/ActualText).
    ///
    /// It is what a reader takes INSTEAD of the glyphs inside: the usual case is a
    /// ligature, where one glyph stands for several characters and the sequence is
    /// marked "(fi)" so the page can be read back as it was written. The parser here
    /// has always understood /ActualText on the way in; this is what writes one.
    /// </summary>
    public string? ActualText { get; set; }

    /// <summary>Creates marked-content properties with a language tag (/Lang) and no /MCID.</summary>
    public BDCProperties(string lang) { Lang = lang; }

    /// <summary>Creates marked-content properties with a marked-content identifier (/MCID) and a language tag (/Lang).</summary>
    public BDCProperties(int mcid, string lang) { MCID = mcid; Lang = lang; }

    /// <summary>Construct with language + expansion text but no /MCID.</summary>
    public BDCProperties(string lang, string expansionText)
    {
        Lang = lang;
        E = expansionText;
    }

    /// <summary>Full ctor with optional /MCID + language + expansion text.</summary>
    public BDCProperties(int? mcid, string? lang, string? expansionText)
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
        if (ActualText is not null) sb.Append($" /ActualText ({Escape(ActualText)})");
        sb.Append(" >>");
        return sb.ToString();
    }

    /// <summary>A literal string's own delimiters have to be escaped, or a bracket in
    /// the text ends the string early and the dictionary after it is unreadable.</summary>
    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
}

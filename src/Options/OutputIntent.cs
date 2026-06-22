using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

/// <summary>
/// Represents a PDF OutputIntent entry (PDF32000 §14.11.5).
/// </summary>
public sealed class OutputIntent
{
    /// <summary>The output intent subtype (e.g., "GTS_PDFA1", "GTS_PDFX").</summary>
    public string Subtype { get; }

    /// <summary>Human-readable description of the output condition, or null.</summary>
    public string? OutputCondition { get; set; }

    /// <summary>Machine-readable identifier for the output condition (required).</summary>
    public string OutputConditionIdentifier { get; set; }

    /// <summary>URI of the registry where the output condition is registered, or null.</summary>
    public string? RegistryName { get; set; }

    /// <summary>Additional human-readable information, or null.</summary>
    public string? Info { get; set; }

    /// <summary>Raw bytes of the embedded ICC color profile stream, or null.</summary>
    public byte[]? DestOutputProfile { get; }

    /// <summary>True if this is a PDF/A output intent.</summary>
    public bool IsPdfA => Subtype.StartsWith("GTS_PDFA", StringComparison.Ordinal);

    /// <summary>True if this is a PDF/X output intent.</summary>
    public bool IsPdfX => Subtype.StartsWith("GTS_PDFX", StringComparison.Ordinal);

    /// <summary>True if this is a PDF/E output intent.</summary>
    public bool IsPdfE => Subtype.StartsWith("GTS_PDFE", StringComparison.Ordinal);

    /// <summary>True if an ICC color profile is embedded.</summary>
    public bool HasIccProfile => DestOutputProfile is not null;

    /// <summary>
    /// Create an OutputIntent with the specified output condition identifier.
    /// Defaults to GTS_PDFX subtype.
    /// </summary>
    public OutputIntent(string outputConditionIdentifier)
    {
        Subtype = "GTS_PDFX";
        OutputConditionIdentifier = outputConditionIdentifier;
    }

    /// <summary>
    /// Create an OutputIntent with full details.
    /// </summary>
    public OutputIntent(string subtype, string outputConditionIdentifier,
        string? outputCondition = null, string? registryName = null,
        string? info = null, byte[]? destOutputProfile = null)
    {
        Subtype = subtype;
        OutputConditionIdentifier = outputConditionIdentifier;
        OutputCondition = outputCondition;
        RegistryName = registryName;
        Info = info;
        DestOutputProfile = destOutputProfile;
    }

    internal static OutputIntent[] ParseFromCatalog(PdfDictionary catalog, PdfReader reader)
    {
        var intentsObj = reader.Resolve(catalog.Get("OutputIntents"));
        if (intentsObj is not PdfArray arr || arr.Count == 0)
            return [];

        var result = new List<OutputIntent>();
        foreach (var item in arr)
        {
            var dict = reader.ResolveDict(item);
            if (dict is null) continue;

            var subtype = dict.GetName("S") ?? "";
            var oci = GetString(dict, "OutputConditionIdentifier", reader) ?? "";
            var oc = GetString(dict, "OutputCondition", reader);
            var reg = GetString(dict, "RegistryName", reader);
            var inf = GetString(dict, "Info", reader);

            byte[]? iccProfile = null;
            var profileObj = reader.Resolve(dict.Get("DestOutputProfile"));
            if (profileObj is PdfStream stream)
            {
                iccProfile = stream.RawData;
            }

            result.Add(new OutputIntent(subtype, oci, oc, reg, inf, iccProfile));
        }

        return result.ToArray();
    }

    private static string? GetString(PdfDictionary dict, string key, PdfReader reader)
    {
        var obj = reader.Resolve(dict.Get(key));
        return obj switch
        {
            PdfString s => s.ToText(),
            PdfName n => n.Value,
            _ => null,
        };
    }
}

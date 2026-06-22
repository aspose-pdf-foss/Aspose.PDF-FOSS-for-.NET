using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Content;

/// <summary>
/// Represents an Extended Graphics State dictionary (PDF32000 §8.4.5).
/// Parsed from page resources /ExtGState entries.
/// </summary>
public sealed class ExtGState
{
    /// <summary>Fill opacity (ca). Range [0,1]. Default: 1.0 (opaque).</summary>
    public double FillAlpha { get; init; } = 1.0;

    /// <summary>Stroke opacity (CA). Range [0,1]. Default: 1.0 (opaque).</summary>
    public double StrokeAlpha { get; init; } = 1.0;

    /// <summary>Blend mode (BM). Default: "Normal".</summary>
    public string BlendMode { get; init; } = "Normal";

    /// <summary>Overprint for stroking (OP). Default: false.</summary>
    public bool OverprintStroke { get; init; }

    /// <summary>Overprint for non-stroking (op). Default: false.</summary>
    public bool OverprintFill { get; init; }

    /// <summary>Line width override (LW), or null if not set.</summary>
    public double? LineWidth { get; init; }

    /// <summary>Line cap override (LC), or null if not set.</summary>
    public int? LineCap { get; init; }

    /// <summary>Line join override (LJ), or null if not set.</summary>
    public int? LineJoin { get; init; }

    /// <summary>
    /// Parse an ExtGState from a PDF dictionary.
    /// </summary>
    internal static ExtGState FromDict(PdfDictionary dict)
    {
        return new ExtGState
        {
            FillAlpha = GetDouble(dict, "ca", 1.0),
            StrokeAlpha = GetDouble(dict, "CA", 1.0),
            BlendMode = dict.GetName("BM") ?? "Normal",
            OverprintStroke = GetBool(dict, "OP"),
            OverprintFill = GetBool(dict, "op"),
            LineWidth = GetNullableDouble(dict, "LW"),
            LineCap = GetNullableInt(dict, "LC"),
            LineJoin = GetNullableInt(dict, "LJ"),
        };
    }

    /// <summary>
    /// Resolve all ExtGState dictionaries from a page's resources.
    /// </summary>
    public static IReadOnlyDictionary<string, ExtGState> FromPage(Page page)
    {
        var result = new Dictionary<string, ExtGState>();
        var resources = page.Reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null) return result;

        var gsDict = page.Reader.ResolveDict(resources.Get("ExtGState"));
        if (gsDict is null) return result;

        foreach (var key in gsDict.Keys)
        {
            var entryDict = page.Reader.ResolveDict(gsDict.Get(key));
            if (entryDict is not null)
                result[key] = FromDict(entryDict);
        }
        return result;
    }

    /// <summary>
    /// Resolve raw ExtGState PdfDictionaries from page resources (for ContentStreamParser).
    /// </summary>
    internal static Dictionary<string, PdfDictionary> ResolveRawFromPage(PdfDictionary pageDict, PdfReader reader)
    {
        var result = new Dictionary<string, PdfDictionary>(StringComparer.Ordinal);
        var resources = reader.ResolveDict(pageDict.Get("Resources"));
        if (resources is null) return result;

        var gsDict = reader.ResolveDict(resources.Get("ExtGState"));
        if (gsDict is null) return result;

        foreach (var key in gsDict.Keys)
        {
            var entryDict = reader.ResolveDict(gsDict.Get(key));
            if (entryDict is not null)
                result[key] = entryDict;
        }
        return result;
    }

    private static double GetDouble(PdfDictionary dict, string key, double defaultValue)
    {
        var obj = dict.Get(key);
        return obj switch
        {
            PdfReal r => r.Value,
            PdfInteger i => i.Value,
            _ => defaultValue,
        };
    }

    private static double? GetNullableDouble(PdfDictionary dict, string key)
    {
        var obj = dict.Get(key);
        return obj switch
        {
            PdfReal r => r.Value,
            PdfInteger i => i.Value,
            _ => null,
        };
    }

    private static int? GetNullableInt(PdfDictionary dict, string key)
    {
        var obj = dict.Get(key);
        return obj switch
        {
            PdfInteger i => (int)i.Value,
            _ => null,
        };
    }

    private static bool GetBool(PdfDictionary dict, string key)
    {
        var obj = dict.Get(key);
        return obj is PdfBoolean b && b.Value;
    }

    /// <summary>
    /// Convert this ExtGState to a PDF dictionary for writing.
    /// Only includes entries that differ from defaults.
    /// </summary>
    internal PdfDictionary ToPdfDictionary()
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("ExtGState"));

        if (FillAlpha < 1.0)
            dict.Set("ca", new PdfReal(FillAlpha));
        if (StrokeAlpha < 1.0)
            dict.Set("CA", new PdfReal(StrokeAlpha));
        if (BlendMode != "Normal")
            dict.Set("BM", new PdfName(BlendMode));
        if (OverprintStroke)
            dict.Set("OP", PdfBoolean.True);
        if (OverprintFill)
            dict.Set("op", PdfBoolean.True);
        if (LineWidth.HasValue)
            dict.Set("LW", new PdfReal(LineWidth.Value));
        if (LineCap.HasValue)
            dict.Set("LC", new PdfInteger(LineCap.Value));
        if (LineJoin.HasValue)
            dict.Set("LJ", new PdfInteger(LineJoin.Value));

        return dict;
    }
}

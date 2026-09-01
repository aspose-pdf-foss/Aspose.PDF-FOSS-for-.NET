using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

/// <summary>
/// Represents a page transition effect (PDF32000 §12.4.4).
/// </summary>
public sealed class PageTransition
{
    /// <summary>Transition style (e.g., "Split", "Blinds", "Box", "Wipe", "Dissolve", "Glitter", "R", "Fly", "Push", "Cover", "Uncover", "Fade").</summary>
    public string Style { get; }

    /// <summary>Duration of the transition in seconds (default 1.0).</summary>
    public double Duration { get; }

    /// <summary>Duration of page display before next transition, in seconds, or null if not set.</summary>
    public double? DisplayDuration { get; }

    /// <summary>Direction of motion in degrees, or null if not applicable.</summary>
    public int? Angle { get; }

    /// <summary>Dimension for Split/Blinds transitions: "H" (horizontal) or "V" (vertical), or null.</summary>
    public string? Dimension { get; }

    /// <summary>Motion direction for Box/Split: "I" (inward) or "O" (outward), or null.</summary>
    public string? Motion { get; }

    /// <summary>For Fly transition, whether rectangular (default false).</summary>
    public bool IsRectangular { get; }

    /// <summary>For Fly transition, the starting/ending scale factor, or null.</summary>
    public double? Scale { get; }

    internal PageTransition(string style, double duration, double? displayDuration,
        int? angle, string? dimension, string? motion, bool isRectangular, double? scale)
    {
        Style = style;
        Duration = duration;
        DisplayDuration = displayDuration;
        Angle = angle;
        Dimension = dimension;
        Motion = motion;
        IsRectangular = isRectangular;
        Scale = scale;
    }

    internal static PageTransition? FromPageDict(PdfDictionary pageDict, PdfReader reader)
    {
        var transObj = reader.Resolve(pageDict.Get("Trans"));
        if (transObj is not PdfDictionary transDict)
            return null;

        var style = transDict.GetName("S") ?? "R";
        var duration = GetDouble(transDict, "D") ?? 1.0;
        var angle = GetInt(transDict, "Di");
        var dimension = transDict.GetName("Dm");
        var motion = transDict.GetName("M");
        var isRectangular = transDict.Get("B") is PdfBoolean b && b.Value;
        var scale = GetDouble(transDict, "SS");

        // Display duration comes from the page dict, not the transition dict
        var displayDuration = GetDouble(pageDict, "Dur");

        return new PageTransition(style, duration, displayDuration,
            angle, dimension, motion, isRectangular, scale);
    }

    private static double? GetDouble(PdfDictionary dict, string key)
    {
        var obj = dict.Get(key);
        return obj switch
        {
            PdfReal r => r.Value,
            PdfInteger i => i.Value,
            _ => null,
        };
    }

    private static int? GetInt(PdfDictionary dict, string key)
    {
        var obj = dict.Get(key);
        return obj switch
        {
            PdfInteger i => (int)i.Value,
            PdfReal r => (int)r.Value,
            _ => null,
        };
    }
}

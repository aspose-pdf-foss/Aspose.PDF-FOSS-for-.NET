// PDF pattern dictionaries — PDF32000_2008 §8.7.3
//
// Patterns are templates for filling/stroking shapes. Two types:
//   PatternType 1 — Tiling pattern  (§8.7.3.1): repeating tile with content stream
//   PatternType 2 — Shading pattern (§8.7.3.2): smooth gradient via Shading dict

using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Shading;

/// <summary>Abstract base for all PDF pattern types.</summary>
public abstract class Pattern
{
    /// <summary>Pattern type (1 = tiling, 2 = shading).</summary>
    public abstract int PatternType { get; }

    /// <summary>Transformation matrix [a b c d e f], or null if absent (identity).</summary>
    public double[]? Matrix { get; }

    protected Pattern(double[]? matrix)
    {
        Matrix = matrix;
    }

    /// <summary>Parse a pattern dictionary or stream reference.</summary>
    internal static Pattern? Parse(PdfObject? obj, PdfReader reader)
    {
        if (obj is null) return null;
        try
        {
            PdfDictionary dict;
            var resolved = reader.Resolve(obj);
            if (resolved is PdfStream stream)
                dict = stream.Dict;
            else if (resolved is PdfDictionary d)
                dict = d;
            else
                return null;

            var matrix = ParseMatrix(dict.Get("Matrix") as PdfArray);
            var pt = (int)dict.GetInt("PatternType");

            if (pt == 1)
                return new TilingPattern(dict, matrix);
            if (pt == 2)
            {
                var shading = ShadingBase.Parse(dict.Get("Shading"), reader);
                return new ShadingPattern(shading, matrix);
            }
            return null;
        }
        catch { return null; }
    }

    /// <summary>Resolve the /Pattern resource dictionary from a page.</summary>
    internal static Dictionary<string, Pattern> ResolvePatterns(PdfDictionary pageDict, PdfReader reader)
    {
        var result = new Dictionary<string, Pattern>();
        var resources = ResolveInheritedResources(pageDict, reader);
        if (resources is null) return result;

        var patternDict = reader.ResolveDict(resources.Get("Pattern"));
        if (patternDict is null) return result;

        foreach (var key in patternDict.Keys)
        {
            try
            {
                var p = Parse(patternDict.Get(key), reader);
                if (p is not null) result[key] = p;
            }
            catch { /* skip malformed entry */ }
        }
        return result;
    }

    private static double[]? ParseMatrix(PdfArray? arr)
    {
        if (arr is null || arr.Count < 6) return null;
        return PdfArrayHelper.ToDoubleArray(arr);
    }

    private static PdfDictionary? ResolveInheritedResources(PdfDictionary pageDict, PdfReader reader)
    {
        if (pageDict.ContainsKey("Resources"))
        {
            try { return reader.ResolveDict(pageDict.Get("Resources")); } catch { /* fall through */ }
        }
        var parentRef = pageDict.Get("Parent");
        while (parentRef is not null)
        {
            try
            {
                var parent = reader.ResolveDict(parentRef);
                if (parent is null) break;
                if (parent.ContainsKey("Resources"))
                    return reader.ResolveDict(parent.Get("Resources"));
                parentRef = parent.Get("Parent");
            }
            catch { break; }
        }
        return null;
    }
}

/// <summary>Tiling pattern (PatternType 1, §8.7.3.1): repeating tile.</summary>
public sealed class TilingPattern : Pattern
{
    public override int PatternType => 1;

    /// <summary>Paint type: 1 = coloured, 2 = uncoloured.</summary>
    public int PaintType { get; }
    /// <summary>Tiling type: 1 = ConstantSpacing, 2 = NoDistortion, 3 = ConstantSpacingAndFaster.</summary>
    public int TilingType { get; }
    /// <summary>Bounding box [xmin, ymin, xmax, ymax].</summary>
    public double[] BBox { get; }
    /// <summary>Horizontal spacing between pattern cells.</summary>
    public double XStep { get; }
    /// <summary>Vertical spacing between pattern cells.</summary>
    public double YStep { get; }

    internal TilingPattern(PdfDictionary dict, double[]? matrix) : base(matrix)
    {
        var pt = (int)dict.GetInt("PaintType");
        PaintType = pt is 1 or 2 ? pt : 1;
        var tt = (int)dict.GetInt("TilingType");
        TilingType = tt is 1 or 2 or 3 ? tt : 1;
        var bboxArr = dict.Get("BBox") as PdfArray;
        BBox = bboxArr is not null && bboxArr.Count >= 4
            ? PdfArrayHelper.ToDoubleArray(bboxArr)
            : [0, 0, 0, 0];
        XStep = PdfArrayHelper.GetDoubleFromDict(dict, "XStep", 0);
        YStep = PdfArrayHelper.GetDoubleFromDict(dict, "YStep", 0);
    }
}

/// <summary>Shading pattern (PatternType 2, §8.7.3.2): smooth gradient.</summary>
public sealed class ShadingPattern : Pattern
{
    public override int PatternType => 2;

    /// <summary>The shading that defines the gradient, or null if absent.</summary>
    public ShadingBase? Shading { get; }

    internal ShadingPattern(ShadingBase? shading, double[]? matrix) : base(matrix)
    {
        Shading = shading;
    }
}

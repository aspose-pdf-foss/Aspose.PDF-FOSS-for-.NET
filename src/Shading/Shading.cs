// PDF shading dictionaries — PDF32000_2008 §8.7
//
// Shadings define smooth colour gradients. They appear as the /Shading entry
// in a Type 2 Pattern dictionary or are painted directly via the `sh` operator.

using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Shading;

/// <summary>Shading type — /ShadingType entry.</summary>
public enum ShadingType
{
    FunctionBased = 1,
    Axial = 2,
    Radial = 3,
    FreeFormGouraud = 4,
    LatticeFormGouraud = 5,
    CoonsPatch = 6,
    TensorProductPatch = 7,
}

/// <summary>Abstract base for all PDF shading types (§8.7.4.1).</summary>
public abstract class ShadingBase
{
    /// <summary>The shading type.</summary>
    public abstract ShadingType ShadingType { get; }

    /// <summary>Colour space name for the shading's output.</summary>
    public string ColorSpaceName { get; }

    /// <summary>
    /// Tint-transform function for a /Separation or /DeviceN output colour space,
    /// or null for a device colour space. The shading function's components are the
    /// tint values that this function maps into <see cref="AltSpaceName"/>.
    /// </summary>
    public PdfFunction? TintTransform { get; }

    /// <summary>Alternate colour-space family ("DeviceRGB"/"DeviceCMYK"/"DeviceGray"/"Lab")
    /// that <see cref="TintTransform"/> produces, or null.</summary>
    public string? AltSpaceName { get; }

    /// <summary>Background colour components, or null if absent.</summary>
    public double[]? Background { get; }

    /// <summary>Bounding box [xmin, ymin, xmax, ymax], or null if absent.</summary>
    public double[]? BBox { get; }

    /// <summary>Whether anti-aliasing is requested (advisory).</summary>
    public bool AntiAlias { get; }

    internal ShadingBase(PdfDictionary dict, PdfReader reader)
    {
        var csObj = reader.Resolve(dict.Get("ColorSpace"));
        ColorSpaceName = csObj switch
        {
            PdfName n => n.Value,
            PdfArray arr when arr.Count > 0 && arr[0] is PdfName n2 => n2.Value,
            _ => "DeviceGray",
        };
        // A /Separation or /DeviceN output space carries a tint transform that maps the
        // shading function's components into an alternate device space (§8.6.6). Capture
        // it so the gradient is painted in its true colour rather than as raw tints.
        if (csObj is PdfArray csArr && csArr.Count >= 4 && csArr[0] is PdfName csFam
            && (csFam.Value == "Separation" || csFam.Value == "DeviceN"))
        {
            TintTransform = PdfFunction.Parse(csArr[3], reader);
            AltSpaceName = ResolveAltSpaceFamily(reader.Resolve(csArr[2]), reader);
        }
        Background = dict.Get("Background") is PdfArray bg ? PdfArrayHelper.ToDoubleArray(bg) : null;
        BBox = dict.Get("BBox") is PdfArray bbox && bbox.Count >= 4 ? PdfArrayHelper.ToDoubleArray(bbox) : null;
        AntiAlias = dict.Get("AntiAlias") is PdfBoolean ab && ab.Value;
    }

    /// <summary>
    /// Resolve the alternate-space family of a /Separation or /DeviceN tint output to
    /// DeviceGray/DeviceRGB/DeviceCMYK/Lab (ICCBased maps by component count). Returns
    /// null when unrecognised.
    /// </summary>
    private static string? ResolveAltSpaceFamily(PdfObject? obj, PdfReader reader)
    {
        switch (obj)
        {
            case PdfName n:
                return n.Value switch
                {
                    "DeviceCMYK" or "DeviceRGB" or "DeviceGray" => n.Value,
                    "CalGray" => "DeviceGray",
                    "CalRGB" => "DeviceRGB",
                    _ => null,
                };
            case PdfArray a when a.Count > 0 && a[0] is PdfName fam:
                if (fam.Value == "ICCBased" && a.Count > 1 && reader.ResolveStream(a[1]) is { } icc)
                    return (int)icc.Dict.GetInt("N") switch { 1 => "DeviceGray", 3 => "DeviceRGB", 4 => "DeviceCMYK", _ => null };
                return fam.Value switch { "CalGray" => "DeviceGray", "CalRGB" => "DeviceRGB", "Lab" => "Lab", _ => null };
            default:
                return null;
        }
    }

    /// <summary>Parse a shading dictionary or stream reference.</summary>
    internal static ShadingBase? Parse(PdfObject? obj, PdfReader reader)
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

            var st = (int)dict.GetInt("ShadingType");
            // Types 4-7 are stream-only (the mesh data is the stream body).
            return st switch
            {
                1 => new FunctionBasedShading(dict, reader),
                2 => new AxialShading(dict, reader),
                3 => new RadialShading(dict, reader),
                4 when resolved is PdfStream s4 => new FreeFormGouraudShading(s4, reader),
                5 when resolved is PdfStream s5 => new LatticeFormGouraudShading(s5, reader),
                6 when resolved is PdfStream s6 => new CoonsPatchShading(s6, reader),
                7 when resolved is PdfStream s7 => new TensorPatchShading(s7, reader),
                _ => null,
            };
        }
        catch { return null; }
    }
}

/// <summary>Function-based shading (Type 1, §8.7.4.2).</summary>
public sealed class FunctionBasedShading : ShadingBase
{
    public override ShadingType ShadingType => ShadingType.FunctionBased;
    public double[] Domain { get; }
    public PdfFunction? Function { get; }

    internal FunctionBasedShading(PdfDictionary dict, PdfReader reader) : base(dict, reader)
    {
        var domainArr = dict.Get("Domain") as PdfArray;
        Domain = domainArr is not null && domainArr.Count >= 4
            ? PdfArrayHelper.ToDoubleArray(domainArr)
            : [0, 1, 0, 1];
        Function = PdfFunction.Parse(dict.Get("Function"), reader);
    }
}

/// <summary>Axial (linear) shading (Type 2, §8.7.4.3).</summary>
public sealed class AxialShading : ShadingBase
{
    public override ShadingType ShadingType => ShadingType.Axial;

    /// <summary>Start point X.</summary>
    public double X0 { get; }
    /// <summary>Start point Y.</summary>
    public double Y0 { get; }
    /// <summary>End point X.</summary>
    public double X1 { get; }
    /// <summary>End point Y.</summary>
    public double Y1 { get; }
    /// <summary>Parameter range [t0, t1]. Default [0, 1].</summary>
    public double[] Domain { get; }
    /// <summary>Function mapping t → colour components.</summary>
    public PdfFunction? Function { get; }
    /// <summary>[extendBefore, extendAfter]. Default [false, false].</summary>
    public bool[] Extend { get; }

    internal AxialShading(PdfDictionary dict, PdfReader reader) : base(dict, reader)
    {
        var coordsArr = dict.Get("Coords") as PdfArray;
        var coords = coordsArr is not null ? PdfArrayHelper.ToDoubleArray(coordsArr) : [0, 0, 1, 0];
        X0 = coords.Length > 0 ? coords[0] : 0;
        Y0 = coords.Length > 1 ? coords[1] : 0;
        X1 = coords.Length > 2 ? coords[2] : 1;
        Y1 = coords.Length > 3 ? coords[3] : 0;

        var domainArr = dict.Get("Domain") as PdfArray;
        Domain = domainArr is not null && domainArr.Count >= 2
            ? PdfArrayHelper.ToDoubleArray(domainArr) : [0, 1];

        Function = PdfFunction.Parse(dict.Get("Function"), reader);

        var extArr = dict.Get("Extend") as PdfArray;
        Extend = extArr is not null && extArr.Count >= 2
            ? [extArr[0] is PdfBoolean b1 && b1.Value, extArr[1] is PdfBoolean b2 && b2.Value]
            : [false, false];
    }

    /// <summary>Sample the shading at parameter t ∈ [domain[0], domain[1]].</summary>
    public double[]? SampleAt(double t)
    {
        return Function?.Evaluate([t]);
    }
}

/// <summary>Radial (circular) shading (Type 3, §8.7.4.4).</summary>
public sealed class RadialShading : ShadingBase
{
    public override ShadingType ShadingType => ShadingType.Radial;

    public double X0 { get; }
    public double Y0 { get; }
    public double R0 { get; }
    public double X1 { get; }
    public double Y1 { get; }
    public double R1 { get; }
    public double[] Domain { get; }
    public PdfFunction? Function { get; }
    public bool[] Extend { get; }

    internal RadialShading(PdfDictionary dict, PdfReader reader) : base(dict, reader)
    {
        var coordsArr = dict.Get("Coords") as PdfArray;
        var coords = coordsArr is not null ? PdfArrayHelper.ToDoubleArray(coordsArr) : [0, 0, 0, 1, 1, 0];
        X0 = coords.Length > 0 ? coords[0] : 0;
        Y0 = coords.Length > 1 ? coords[1] : 0;
        R0 = coords.Length > 2 ? coords[2] : 0;
        X1 = coords.Length > 3 ? coords[3] : 1;
        Y1 = coords.Length > 4 ? coords[4] : 1;
        R1 = coords.Length > 5 ? coords[5] : 0;

        var domainArr = dict.Get("Domain") as PdfArray;
        Domain = domainArr is not null && domainArr.Count >= 2
            ? PdfArrayHelper.ToDoubleArray(domainArr) : [0, 1];

        Function = PdfFunction.Parse(dict.Get("Function"), reader);

        var extArr = dict.Get("Extend") as PdfArray;
        Extend = extArr is not null && extArr.Count >= 2
            ? [extArr[0] is PdfBoolean b1 && b1.Value, extArr[1] is PdfBoolean b2 && b2.Value]
            : [false, false];
    }

    /// <summary>Sample the shading at parameter t.</summary>
    public double[]? SampleAt(double t)
    {
        return Function?.Evaluate([t]);
    }
}

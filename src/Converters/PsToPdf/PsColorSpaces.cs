using System;

namespace Aspose.Pdf.Converters.PsToPdf;

/// <summary>
/// Maps PostScript colour-space objects onto the four the converter tracks. The
/// reference converter writes every colour as DeviceRGB, so a space matters only for
/// how many components <c>setcolor</c> reads and what <c>currentcolor</c> reports.
/// </summary>
internal static class PsColorSpaces
{
    /// <summary>Which of the tracked spaces a colour-space object names. A space
    /// built on a base — Indexed, Separation, DeviceN, the CIE family — is classified
    /// by what its components ultimately mean once converted.</summary>
    public static PsColorSpace Classify(PsValue space)
    {
        var name = NameOf(space);
        return name switch
        {
            "DeviceGray" or "CalGray" or "G" => PsColorSpace.Gray,
            "DeviceCMYK" or "CMYK" => PsColorSpace.Cmyk,
            "Pattern" => PsColorSpace.Pattern,
            "Separation" or "DeviceN" => AlternateOf(space),
            _ => PsColorSpace.Rgb,
        };
    }

    /// <summary>What a tint space's colours ultimately mean: the space underneath it.</summary>
    private static PsColorSpace AlternateOf(PsValue space)
    {
        var array = space.AsArray;
        return array != null && array.Length > 2 ? Classify(array[2]) : PsColorSpace.Gray;
    }

    /// <summary>The family name of a colour-space object, whether written as a bare
    /// name or as an array whose first element is the family.</summary>
    private static string NameOf(PsValue space)
    {
        if (space.Type == PsType.Name) return space.Text ?? string.Empty;
        var array = space.AsArray;
        if (array is null || array.Length == 0) return string.Empty;
        return array[0].Type == PsType.Name ? array[0].Text ?? string.Empty : string.Empty;
    }

    /// <summary>A space whose colour is a tint that a procedure turns into a colour in
    /// the space underneath it: one tint for a separation, one per colourant for a
    /// DeviceN.</summary>
    public sealed class PsTintSpace
    {
        public PsTintSpace(int components, PsArray transform, PsColorSpace alternate)
        {
            Components = components;
            Transform = transform;
            Alternate = alternate;
        }

        /// <summary>How many tints the space's colour is written with.</summary>
        public int Components { get; }

        /// <summary>The procedure that turns the tints into the space underneath.</summary>
        public PsArray Transform { get; }

        /// <summary>What that space is.</summary>
        public PsColorSpace Alternate { get; }
    }

    /// <summary>The tint space an object describes, or null when it is not one. The
    /// shape is <c>[/Separation name alternate proc]</c>, or <c>[/DeviceN names
    /// alternate proc]</c> with one tint per name.</summary>
    public static PsTintSpace? AsTintSpace(PsValue space)
    {
        var name = NameOf(space);
        var array = space.AsArray;
        if (array is null || array.Length < TintSpaceLength) return null;
        var transform = array[TintSpaceLength - 1].AsArray;
        if (transform is null) return null;
        if (name == "Separation") return new PsTintSpace(1, transform, Classify(array[2]));
        if (name != "DeviceN") return null;
        var names = array[1].AsArray;
        var count = names is null ? 1 : Math.Max(1, names.Length);
        return new PsTintSpace(count, transform, Classify(array[2]));
    }

    /// <summary>How many elements describe a tint space: family, colourants, the space
    /// underneath, and the procedure between them.</summary>
    private const int TintSpaceLength = 4;

    /// <summary>How many components a space's colour is written with. A pattern space
    /// reports what its underlying space wants, since that is what an uncoloured
    /// pattern is given; a bare <c>/Pattern</c> underlies nothing and wants none.</summary>
    public static int ComponentCount(PsValue space)
    {
        var name = NameOf(space);
        if (name == "Pattern")
        {
            var array = space.AsArray;
            return array != null && array.Length > 1 ? ComponentCount(array[1]) : 0;
        }

        if (AsTintSpace(space) is { } tint) return tint.Components;
        return Classify(space) switch
        {
            PsColorSpace.Gray => GrayComponents,
            PsColorSpace.Cmyk => CmykComponents,
            _ => RgbComponents,
        };
    }

    /// <summary>A grey level is one number.</summary>
    private const int GrayComponents = 1;

    /// <summary>An additive colour is three numbers.</summary>
    private const int RgbComponents = 3;

    /// <summary>A process colour is four numbers.</summary>
    private const int CmykComponents = 4;

    /// <summary>A one-element array naming a space, the form
    /// <c>currentcolorspace</c> reports.</summary>
    public static PsArray Describe(PsColorSpace space)
    {
        var array = new PsArray(1);
        array[0] = PsValue.LiteralName(space switch
        {
            PsColorSpace.Gray => "DeviceGray",
            PsColorSpace.Cmyk => "DeviceCMYK",
            PsColorSpace.Pattern => "Pattern",
            _ => "DeviceRGB",
        });
        return array;
    }
}

using System;
using System.Collections.Generic;

namespace Aspose.Pdf.Converters.PsToPdf;

/// <summary>How a colour was set, which decides what the current colour reads back as.</summary>
internal enum PsColorSpace
{
    /// <summary>A grey level.</summary>
    Gray,

    /// <summary>Red, green and blue.</summary>
    Rgb,

    /// <summary>Cyan, magenta, yellow and black.</summary>
    Cmyk,

    /// <summary>A tiling or shading pattern.</summary>
    Pattern,
}

/// <summary>
/// One PostScript graphics state. <c>gsave</c> copies it and <c>grestore</c> puts the
/// copy back, so every field is a value or is copied on write.
/// </summary>
internal sealed class PsGraphicsState
{
    /// <summary>The colour a fresh state paints with: opaque black.</summary>
    public static readonly PsColor Black = PsColor.FromGray(0);

    /// <summary>PostScript's initial line width.</summary>
    public const double DefaultLineWidth = 1.0;

    /// <summary>PostScript's initial miter limit.</summary>
    public const double DefaultMiterLimit = 10.0;

    /// <summary>PostScript's initial flatness.</summary>
    public const double DefaultFlatness = 1.0;

    /// <summary>The current transformation matrix.</summary>
    public PsMatrix Ctm { get; set; } = PsMatrix.Identity;

    /// <summary>The current colour.</summary>
    public PsColor Color { get; set; } = Black;

    /// <summary>Which space the current colour was set in.</summary>
    public PsColorSpace Space { get; set; } = PsColorSpace.Gray;

    /// <summary>The pattern the current colour names, when the space is Pattern.</summary>
    public PsDictionary? Pattern { get; set; }

    /// <summary>The page-resource name that pattern is written under, or null when
    /// the current colour is a plain one.</summary>
    public string? PatternName { get; set; }

    /// <summary>The components an uncoloured pattern paints with. A coloured one
    /// carries its own colours and takes none.</summary>
    public double[]? PatternComponents { get; set; }

    /// <summary>How many components the space underlying a pattern space carries, and
    /// so how many an uncoloured pattern is given when it is set.</summary>
    public int PatternBaseComponents { get; set; } = 1;

    /// <summary>The tint space in force, when the current space is one whose colour
    /// is a tint rather than a colour of its own.</summary>
    public PsColorSpaces.PsTintSpace? Tint { get; set; }

    /// <summary>Stroke width in user space.</summary>
    public double LineWidth { get; set; } = DefaultLineWidth;

    /// <summary>Stroke cap style, 0 butt, 1 round, 2 square.</summary>
    public int LineCap { get; set; }

    /// <summary>Stroke join style, 0 miter, 1 round, 2 bevel.</summary>
    public int LineJoin { get; set; }

    /// <summary>Miter limit.</summary>
    public double MiterLimit { get; set; } = DefaultMiterLimit;

    /// <summary>Curve flatness tolerance.</summary>
    public double Flatness { get; set; } = DefaultFlatness;

    /// <summary>Whether stroke adjustment is on. Recorded, never emitted.</summary>
    public bool StrokeAdjust { get; set; }

    /// <summary>Whether overprint is on. Recorded, never emitted.</summary>
    public bool Overprint { get; set; }

    /// <summary>Dash pattern lengths in user space, empty for a solid line.</summary>
    public double[] DashArray { get; set; } = new double[0];

    /// <summary>Dash phase.</summary>
    public double DashPhase { get; set; }

    /// <summary>The current font, or null when none has been selected.</summary>
    public PsFontInstance? Font { get; set; }

    /// <summary>The current path, in device space. The path is part of the graphics
    /// state, so <c>gsave</c> snapshots it and <c>grestore</c> puts it back — the
    /// idiom that measures a glyph inside a save and expects its own path afterwards
    /// depends on that.</summary>
    public PsPath Path { get; set; } = new();

    /// <summary>Every clip in force, in the order they were set, in device space.
    /// Clipping is cumulative: a second <c>clip</c> narrows the first rather than
    /// replacing it, and PDF intersects the paths of successive clips itself, so the
    /// list is written out whole instead of being intersected here.</summary>
    public List<PsClip> Clips { get; set; } = new();

    /// <summary>Whether marks are discarded. The null device the measuring idiom
    /// installs takes everything a program paints and keeps none of it; the device is
    /// part of the state, so a restore brings the real one back.</summary>
    public bool Discarding { get; set; }

    /// <summary>The clip most recently set, which is what <c>clippath</c> reports.</summary>
    public PsPath? Clip => Clips.Count == 0 ? null : Clips[Clips.Count - 1].Path;

    /// <summary>A copy that shares nothing mutable with this state.</summary>
    public PsGraphicsState Clone() => new()
    {
        Ctm = Ctm,
        Color = Color,
        Space = Space,
        Pattern = Pattern,
        PatternName = PatternName,
        PatternComponents = PatternComponents,
        PatternBaseComponents = PatternBaseComponents,
        Tint = Tint,
        LineWidth = LineWidth,
        LineCap = LineCap,
        LineJoin = LineJoin,
        MiterLimit = MiterLimit,
        Flatness = Flatness,
        StrokeAdjust = StrokeAdjust,
        Overprint = Overprint,
        DashArray = (double[])DashArray.Clone(),
        DashPhase = DashPhase,
        Font = Font,
        Path = Path.Clone(),
        Clips = new List<PsClip>(Clips),
        Discarding = Discarding,
    };
}

/// <summary>One clip path and the rule that fills it.</summary>
internal readonly struct PsClip
{
    /// <summary>Create a clip.</summary>
    public PsClip(PsPath path, bool evenOdd)
    {
        Path = path;
        EvenOdd = evenOdd;
    }

    /// <summary>The path, in device space.</summary>
    public PsPath Path { get; }

    /// <summary>Whether it is filled by the even-odd rule.</summary>
    public bool EvenOdd { get; }
}

/// <summary>
/// A colour, held as the components it was set with plus the RGB the writer emits.
/// The reference converter writes every colour as DeviceRGB, so the conversion
/// happens here rather than in the page writer.
/// </summary>
internal readonly struct PsColor
{
    private PsColor(double red, double green, double blue, double[] components)
    {
        Red = red;
        Green = green;
        Blue = blue;
        Components = components;
    }

    /// <summary>Red, 0 to 1.</summary>
    public double Red { get; }

    /// <summary>Green, 0 to 1.</summary>
    public double Green { get; }

    /// <summary>Blue, 0 to 1.</summary>
    public double Blue { get; }

    /// <summary>The components the colour was set with, for <c>currentcolor</c>.</summary>
    public double[] Components { get; }

    /// <summary>A grey level.</summary>
    public static PsColor FromGray(double gray)
    {
        var g = Clamp(gray);
        return new PsColor(g, g, g, new[] { g });
    }

    /// <summary>An RGB colour.</summary>
    public static PsColor FromRgb(double red, double green, double blue)
    {
        double r = Clamp(red), g = Clamp(green), b = Clamp(blue);
        return new PsColor(r, g, b, new[] { r, g, b });
    }

    /// <summary>A CMYK colour, converted to RGB the way the reference converter does:
    /// each additive component is <c>(1 − ink)(1 − black)</c>.</summary>
    public static PsColor FromCmyk(double cyan, double magenta, double yellow, double black)
    {
        double c = Clamp(cyan), m = Clamp(magenta), y = Clamp(yellow), k = Clamp(black);
        return new PsColor((1 - c) * (1 - k), (1 - m) * (1 - k), (1 - y) * (1 - k),
            new[] { c, m, y, k });
    }

    /// <summary>An HSB colour, converted through the standard sextant rule.</summary>
    public static PsColor FromHsb(double hue, double saturation, double brightness)
    {
        double h = Clamp(hue), s = Clamp(saturation), v = Clamp(brightness);
        if (s <= 0) return FromRgb(v, v, v);
        var sector = (h - Math.Floor(h)) * Sextants;
        var index = (int)Math.Floor(sector);
        var frac = sector - index;
        var p = v * (1 - s);
        var q = v * (1 - s * frac);
        var t = v * (1 - s * (1 - frac));
        return index switch
        {
            0 => FromRgb(v, t, p),
            1 => FromRgb(q, v, p),
            2 => FromRgb(p, v, t),
            3 => FromRgb(p, q, v),
            4 => FromRgb(t, p, v),
            _ => FromRgb(v, p, q),
        };
    }

    /// <summary>The hue circle divides into six linear sectors.</summary>
    private const double Sextants = 6.0;

    /// <summary>A component as the reference conversion keeps it: an eight-bit level,
    /// TRUNCATED rather than rounded, and a level that runs past white wraps round to
    /// black instead of stopping there. A program that adds an eighth to its grey until
    /// the grey reaches white therefore keeps going round, and the etalon holds every
    /// turn it makes.</summary>
    private static double Clamp(double value)
    {
        var level = (int)(value * ByteLevels) % ByteStates;
        if (level < 0) level += ByteStates;
        return level / (double)ByteLevels;
    }

    /// <summary>The level that stands for white.</summary>
    private const int ByteLevels = 255;

    /// <summary>How many levels there are before one wraps round.</summary>
    private const int ByteStates = 256;

    /// <summary>Whether two colours would emit the same operands.</summary>
    public bool SameAs(PsColor other) =>
        Red == other.Red && Green == other.Green && Blue == other.Blue;
}

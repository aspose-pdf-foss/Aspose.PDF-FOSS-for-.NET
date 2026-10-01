using System;

namespace Aspose.Pdf.Vector;

/// <summary>What <see cref="SvgExtractor"/> takes from a page and how it groups it.</summary>
public class SvgExtractionOptions
{
    /// <summary>The group strength lies in 0..1: 0 keeps every element apart, 1 joins elements whose
    /// boxes lie within a whole element's size of each other.</summary>
    private const double MaxGroupStrength = 1.0;

    private const double DefaultGroupStrength = 0.5;

    private double _groupStrength = DefaultGroupStrength;

    /// <summary>Decides, per placed form XObject, whether its elements are extracted one by one
    /// (true) or the placement is kept as one element (false). When set, it decides in place of
    /// <see cref="UnpackPageContentXForm"/>.</summary>
    public Predicate<XFormPlacement>? UnpackXFormPredicate { get; set; }

    /// <summary>Whether every placed form XObject is unpacked into its elements before extraction
    /// (unless <see cref="UnpackXFormPredicate"/> decides form by form).</summary>
    public bool UnpackPageContentXForm { get; set; }

    /// <summary>Whether every element of the page - each painted sub-path, each form placement as a whole -
    /// becomes an SVG of its own, instead of the elements that touch or overlap sharing one.</summary>
    public bool ExtractEverySubPathToSvg { get; set; }

    /// <summary>When set, only elements inside this page-space rectangle are extracted.</summary>
    public Rectangle? ExtractionAreaBound { get; set; }

    /// <summary>With <see cref="ExtractionAreaBound"/>: true takes only elements wholly inside it,
    /// false also takes the ones that touch it.</summary>
    public bool StrictExtractionAreaBoundCheck { get; set; }

    /// <summary>How eagerly <see cref="AutoGrouping"/> joins neighbouring elements into one SVG, 0..1:
    /// two elements are grouped when the gap between their boxes is at most this fraction of
    /// the smaller box's size.</summary>
    public double GroupStrength
    {
        get => _groupStrength;
        set
        {
            if (value < 0 || value > MaxGroupStrength)
                throw new ArgumentOutOfRangeException(nameof(value), $"GroupStrength must lie in 0..{MaxGroupStrength}");
            _groupStrength = value;
        }
    }

    /// <summary>Whether elements near each other, not only the ones that touch or overlap, are written
    /// into one SVG (see <see cref="GroupStrength"/>).</summary>
    public bool AutoGrouping { get; set; }

    /// <summary>Stroked sub-paths thinner than this (points) are left out; 0 keeps every one.</summary>
    public double MinStrokeWidth { get; set; }

    /// <summary>Options that extract each cluster of touching or overlapping elements of a page as one SVG.</summary>
    public SvgExtractionOptions()
    {
    }
}

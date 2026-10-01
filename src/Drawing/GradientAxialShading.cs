using Aspose.Pdf.Content;

namespace Aspose.Pdf.Drawing;

/// <summary>
/// Defines a linear (axial) gradient between two colors for use as a fill pattern.
/// </summary>
public sealed class GradientAxialShading : PatternColorSpace
{
    /// <summary>Start color of the gradient.</summary>
    public Aspose.Pdf.Color? StartColor { get; set; }

    /// <summary>End color of the gradient.</summary>
    public Aspose.Pdf.Color? EndColor { get; set; }

    /// <summary>Start point of the gradient axis.</summary>
    public Aspose.Pdf.Point Start { get; set; } = new Aspose.Pdf.Point(0, 0);

    /// <summary>End point of the gradient axis.</summary>
    public Aspose.Pdf.Point End { get; set; } = new Aspose.Pdf.Point(1, 0);

    /// <summary>
    /// Colours the ramp passes through, evenly spaced along the axis. A gradient
    /// of more than two colours cannot be said with <see cref="StartColor"/> and
    /// <see cref="EndColor"/> alone; when this holds two or more entries it is
    /// what the ramp is built from, and the two colour properties are ignored.
    /// </summary>
    public System.Collections.Generic.IList<Aspose.Pdf.Color> Stops { get; } =
        new System.Collections.Generic.List<Aspose.Pdf.Color>();

    /// <summary>Construct an empty gradient. Colours and endpoints can be set via properties.</summary>
    public GradientAxialShading() { }

    /// <summary>Construct with start/end colours; endpoints default to (0,0)→(1,0).</summary>
    public GradientAxialShading(Aspose.Pdf.Color startColor, Aspose.Pdf.Color endColor)
    {
        StartColor = startColor;
        EndColor = endColor;
    }
}

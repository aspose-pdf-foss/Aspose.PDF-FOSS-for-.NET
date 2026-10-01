namespace Aspose.Pdf.Text;

/// <summary>The geometry of an underline or strike-through rule the flow draws
/// under a run: a filled rectangle as wide as the text, <c>Thickness + ThicknessEm
/// × size</c> tall, centred <c>Offset + OffsetEm × size</c> from the baseline
/// (negative = below), in <see cref="Color"/> or, when that is null, the text's
/// own fill colour. A caller matching another typesetter's rule hands its
/// numbers over here; without a style the flow draws its own default rule.</summary>
public sealed class TextDecorationStyle
{
    public double Thickness { get; set; }
    public double ThicknessEm { get; set; }
    public double Offset { get; set; }
    public double OffsetEm { get; set; }
    public Color? Color { get; set; }

    /// <summary>The line cap (<c>J</c>: 0 butt, 1 round, 2 square) set before the
    /// rule is painted; a filled rectangle does not show it, but a caller
    /// mirroring another writer's stream may want the operator.</summary>
    public int LineCap { get; set; }

    /// <summary>The rule's fill opacity (<c>ca</c>) as a real in 0..1; null (the
    /// default) paints it opaque.</summary>
    public double? Opacity { get; set; }

    /// <summary>The rule's height and the height of its centre above the
    /// baseline at <paramref name="fontSize"/>.</summary>
    public (double Thickness, double Centre) At(double fontSize) =>
        (Thickness + ThicknessEm * fontSize, Offset + OffsetEm * fontSize);
}

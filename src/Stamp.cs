namespace Aspose.Pdf;

/// <summary>Top-level Aspose.Pdf-shape abstract base for stamps applied
/// to a <see cref="Page"/> via <see cref="Page.AddStamp(Stamp)"/>.
/// Coexists with <see cref="Aspose.Pdf.Stamps.Stamp"/> (the FOSS
/// positioning-oriented base used by the existing TextStamp / ImageStamp
/// / PdfPageStamp / PageNumberStamp pipeline) — they're parallel hierarchies.
/// Concrete subclasses must override <see cref="Put(Page)"/>.</summary>
public abstract class Stamp
{
    private int _stampId;

    public bool Background { get; set; }
    public double BottomMargin { get; set; }
    public double Height { get; set; }
    public HorizontalAlignment HorizontalAlignment { get; set; }
    public double LeftMargin { get; set; }
    public double Opacity { get; set; } = 1.0;
    public double OutlineOpacity { get; set; } = 1.0;
    public double OutlineWidth { get; set; }
    public double RightMargin { get; set; }
    public Rotation Rotate { get; set; }
    public double RotateAngle { get; set; }
    public double TopMargin { get; set; }
    public VerticalAlignment VerticalAlignment { get; set; }
    public double Width { get; set; }
    public double XIndent { get; set; }
    public double YIndent { get; set; }
    public double Zoom { get; set; } = 1.0;
    public double ZoomX { get; set; } = 1.0;
    public double ZoomY { get; set; } = 1.0;

    /// <summary>Apply this stamp to <paramref name="page"/>. Required override.</summary>
    public abstract void Put(Page page);

    public int getStampId() => _stampId;

    public void setStampId(int value) => _stampId = value;
}

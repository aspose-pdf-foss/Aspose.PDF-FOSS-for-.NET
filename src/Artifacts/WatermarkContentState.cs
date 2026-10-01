using System.IO;
using Aspose.Pdf.Content;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class WatermarkArtifact
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class WatermarkContentState
{
    // Apply page-number substitution: replace the configured token with the
    // 1-based page number. A null/empty token disables substitution.
    public string renderText = null!;
    public double pageWidth;
    public double pageHeight;
    public float fontSize;
    // Estimate text dimensions
    public double charWidth;
    public double textWidth;
    public float textHeight;
    // Calculate position based on alignment / explicit Position / margins
    public double x;
    public double y;
    // Compute bounding box for /BBox in the BDC properties dict.
    public Rectangle bbox = null!;
    public Content.ContentStreamBuilder builder = null!;
    // Begin marked content for artifact — use BDC with properties so the
    // /Type, /Subtype, and /BBox round-trip through ArtifactCollection.
    public System.Globalization.CultureInfo ci = null!;
    public string bboxStr = null!;
    public string dict = null!;
    public Page page = default!;
    public string fontResourceName = default!;
}
}

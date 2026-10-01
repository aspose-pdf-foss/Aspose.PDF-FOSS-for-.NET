using System.Text;

namespace Aspose.Pdf.Text;

internal sealed partial class GlyphOutlineParser
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class SimpleGlyphState
{
    // Read endPtsOfContours
    public int[] endPts = null!;
    public int numPoints;
    public int instrLen;
    // Read flags
    public byte[] flags = null!;
    // Read X coordinates (deltas)
    public double[] xCoords = null!;
    public double x;
    // Read Y coordinates (deltas)
    public double[] yCoords = null!;
    public double y;
    // Build contours. numPoints comes from the LAST endPt alone, so a font whose
    // endPtsOfContours is not non-descending - or which names a point past the end -
    // yields a count that walks the flag and coordinate arrays off their ends. The
    // outline is unusable at that point, so report it missing like every other
    // malformed case here rather than drawing part of it.
    public Aspose.Pdf.Text.ContourPoint[][] contours = null!;
    public int ptIdx;
    public int offset = 0;
    public int numContours = 0;
    public double xMin = 0;
    public double yMin = 0;
    public double xMax = 0;
    public double yMax = 0;
}
}

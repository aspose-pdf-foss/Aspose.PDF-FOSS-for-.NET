using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

public partial class TextMarkupAnnotation
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class MarkedTextState
{
    public Aspose.Pdf.Text.TextFragmentCollection result = null!;
    public List<Aspose.Pdf.Text.TextFragment> fragments = null!;
    // Quad boxes.
    public List<(double minX, double minY, double maxX, double maxY)> boxes = null!;
    // Assign each marked character to a SINGLE best quad (largest X-overlap within
    // the quad's Y band). Adjacent quads can overlap by a fraction of a point, so
    // collecting per-quad independently would double-count boundary glyphs. Group the
    // chars by (best quad, source text run) — one output fragment per group, yielding
    // a fragment per marked run, not per quad.
    public double grazeTolerance;
    public Dictionary<(int q, int fi), List<(char ch, double cx)>> groups = null!;
    public List<(int q, int fi)> order = null!;
    public int fi;
    // One piece per (quad, run) group, tagged with the quad's position for re-ordering.
    public List<(double midY, double minX, double maxX, string text)> pieces = null!;
    public double rightMargin;
    public List<List<(double midY, double minX, double maxX, string text)>> lines = null!;
    public double lineTol;
}
}

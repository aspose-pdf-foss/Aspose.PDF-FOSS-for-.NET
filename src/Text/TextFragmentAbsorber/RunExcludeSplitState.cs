using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class RunExcludeSplitState
{
    public Aspose.Pdf.Text.TextFragmentAbsorber.RawTextRun run;
    public int n;
    // Vertical glyph band in page space (descent..ascent along the Tm up-axis).
    public double fs;
    public double descentOffset;
    public double ascentHeight;
    public double bandLly;
    public double bandUry;
    public double bandH;
    public double tol;
    public bool dbg;
    public List<Aspose.Pdf.Rectangle> active = null!;
    // Per-character page-space X positions (cumulative advances include Tc/Tw).
    public double[]? cum;
    public double[]? ends;
    public double[] charX = null!;
    public bool haveCum;
    public bool[] excluded = null!;
    public bool any;
    public List<Aspose.Pdf.Text.TextFragmentAbsorber.RawTextRun> pieces = null!;
    public int start;
    public List<RawTextRun> runs = default!;
    public Rectangle[] excludeRects = default!;
}
}

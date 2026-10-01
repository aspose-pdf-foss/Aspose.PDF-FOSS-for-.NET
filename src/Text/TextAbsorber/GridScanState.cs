using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class GridScanState
{
    public IO.PdfLexer lexer = null!;
    public List<Aspose.Pdf.Core.PdfObject> operands = null!;
    public FontMetrics? metrics;
    public double tlmX;
    public double cmTx;
    // Baseline Y (device, approximate) and leading — only consulted for
    // the bounds check, mirroring the X tracking's level of fidelity.
    public double tlmY;
    public double preTL;
    // Rotated mirror of the extraction loop: for sideways text (rotation in
    // the Tm or in the CTM) the reading-axis X is the composed origin
    // projected on (a,b), Td advances scale by |(a,b)|, and the dominant
    // font size counts in DEVICE units — otherwise the pre-scan minX/cell
    // disagree with the runtime grid coordinates.
    public double tmScaleX;
    public double fsScale;
    // Horizontal scaling (Tz, percent/100): condensed text draws — and
    // measures — narrower than the font's nominal advances.
    public double preHorizScale;
    // Character/word spacing (Tc/Tw, text-space units): the
    // segment measure includes them — a negative Tc condenses every
    // advance the mean-advance cell averages.
    public double preTc;
    public double preTw;
    public bool preRot;
    public double cmA;
    public double cmB;
    public double cmC;
    public double cmD;
    public double cmE;
    public double cmF;
    public Stack<(double a, double b, double c, double d, double e, double f)> cmFullStack = null!;
    public Stack<double> cmStack = null!;
    public double fontSize = 12;
    public PdfReader reader = null!;
    public double[]? bounds = null;
    public Dictionary<string, PdfDictionary> fonts = null!;
    public PdfDictionary resDict = null!;
    public int rdepth = 0;
    public bool recurse = false;
}
}

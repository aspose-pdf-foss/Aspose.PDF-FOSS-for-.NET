using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class PageGridState
{
    public double sumW;
    public int rotChars;
    public int uprightChars;
    public Dictionary<double, double> rawBySize = null!;
    public Dictionary<double, double> widthPerSize = null!;
    // Pure glyph advances (kern adjustments excluded, synthesized spaces
    // not counted): diagnostic population, kept separate from the
    // kern-inclusive sums the gap heuristics were calibrated on.
    public Dictionary<double, double> pureWidthPerSize = null!;
    public Dictionary<double, int> pureCharsPerSize = null!;
    // The mean-advance population the cell rule averages:
    // kern-inclusive run widths WITHOUT the drawn space glyphs (their
    // advances and counts both come out), synthesized adjustment spaces
    // counted, per-run 0.6 em cap. Calibrated on three-way evidence: a
    // kern-gap French daily needs the kerns counted, a resume with drawn
    // spaces needs them excluded, a rotated CID report needs the formula
    // term to win the min().
    public Dictionary<double, double> avgWidthPerSize = null!;
    public Dictionary<double, int> avgCharsPerSize = null!;
    public double minX;
    public double minXAny;
    public Dictionary<double, int> charsPerSize = null!;
    public Dictionary<string, Aspose.Pdf.Core.PdfDictionary> pageFonts = null!;
    public bool rotDom;
    public double gridMinX;
    // Dominant font size: most characters; tie → smallest size.
    public double domSize;
    public int cnt;
}
}

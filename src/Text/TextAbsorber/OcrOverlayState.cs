using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class OcrOverlayState
{
    // Group runs into visual lines. Runs are taken top-to-bottom; one joins the
    // current line when its baseline is within a font-relative tolerance of the
    // line's top run — so a giant glyph (a "/" many times the text size) still
    // joins its line, while the next text line, a full leading below, does not.
    public List<(string text, double x, double y, double fs, double width)> ordered = null!;
    public List<List<(string text, double x, double fs, double width, double y)>> lines = null!;
    // Per line: baseline = median glyph baseline; bottom = deepest glyph (lowest y).
    public double[] baseline = null!;
    public double[] bottom = null!;
    // Grid geometry: cell from the dominant-by-char font size; origin at leftmost run.
    public double minX;
    public Dictionary<int, int> charByFs = null!;
    public int fdom;
    public int bestChars;
    public double cell;
    public System.Text.StringBuilder sb = null!;
    public int textStart = 0;
}
}

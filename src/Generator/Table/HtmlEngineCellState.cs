using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class Table
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class HtmlEngineCellState
{
    public double smallSize;
    public (double Box, double Drop) rootLine;
    public double rootBox;
    public double baseDrop;
    public List<Aspose.Pdf.Table.CellLine> lines = null!;
    public List<Aspose.Pdf.Table.HtmlRun> curRuns = null!;
    public double curX;
    public int boldDepth;
    public int smallDepth;
    public bool anyText;
    public Stack<string> anchors = null!;
    // Open <span style=...> elements, innermost last: colour, an own font size
    // (unitless/pt = points, px = CSS px), and text-decoration: underline.
    public List<(Aspose.Pdf.Color? Color, double Size, bool Underline)> spans = null!;
    // Open <ul>/<ol> elements, innermost last: each carries its kind and, for an
    // ordered list, the ordinal its next item takes.
    public List<(bool Ordered, int Counter)> lists = null!;
    // The x every line of the item in progress starts at (its list's indent), and
    // the marker its FIRST line still owes.
    public double lineIndent;
    public string? pendingMarker;
    /// <summary>Inside an open &lt;li&gt; (with or without an enclosing list).</summary>
    public bool inListItem;
    public int pos;
    // The cell's content box ends at the LAST baseline + the last line's win
    // descent — not at the full line-box bottom (no bottom leading).
    public Table.CellLine lastLine = null!;
    // Open elements that set a face, size, weight, slant or block margin, innermost last.
    public List<EngineFaceStyle> faceStack = new();
    public Dictionary<string, UaFace?> faceCache = new();
    public int faceIndex;
    // The UA margin the block in progress still owes its first line.
    public double pendingBlockMargin;
    public int blockDepth;
    public string? html = null;
    public double availWidth = 0;
    public double baseSize = 0;
    public bool breakWords = false;
    public bool plainText = false;
    /// <summary>Float columns laid out so far (merged by their line tops) and the x the next one opens at.</summary>
    public List<Aspose.Pdf.Table.CellLine>? floatLines;
    public double floatPen;
    /// <summary>The indent and width a nested table's cell inset replaced, restored at its close.</summary>
    public Stack<(double Indent, double Avail)> tableStack = new();
}
}

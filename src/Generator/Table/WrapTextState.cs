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
private sealed class WrapTextState
{
    public List<string> lines = null!;
    public string[] words = null!;
    public double spaceW;
    public string currentLine = null!;
    public double currentWidth;
    // "Has this line been opened yet" is NOT "is it still empty": a line opened by
    // the empty token that leads a run of spaces has length zero and still owns
    // every space after it. Testing emptiness dropped a paragraph's leading
    // indent, and with it the line the indent occupies when the word behind it is
    // too long to follow (cell paragraphs can be indented by the verbatim
    // string that wrote them, and the shipped template gives each indent its own
    // line).
    public bool lineStarted;
    public string text = default!;
    public double fontSize = 0;
    public double availWidth = 0;
    public Func<string, double>? measure = null;
    public bool overflowLongWords = false;
    // A line broken AT A SPACE keeps that space, which then hangs past the
    // measure (the caller's own typographic policy -- see
    // TextFormattingOptions.HangingBreakSpace, which asks the page flow's wrap
    // for the same thing).
    public bool hangingBreakSpace = false;
}
}

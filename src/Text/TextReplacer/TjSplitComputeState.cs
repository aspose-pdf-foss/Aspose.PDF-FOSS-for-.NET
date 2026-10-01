using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class TjSplitComputeState
{
    public FontMetrics? metrics;
    public bool isCid;
    // Per-element char-start in the concatenated text (mirroring ConcatenateTJText's
    // synthetic-space rule) and the pen advance before each element (kern-aware).
    public int[] charStart = null!;
    public double[] localXBefore = null!;
    public string?[] decoded = null!;
    public System.Text.StringBuilder sb = null!;
    public Aspose.Pdf.Text.TextReplacer.TjBreakRule tjRule;
    public double localX;
    public string concat = null!;
    public int matchStart;
    public int matchEnd;
    // Locate the elements containing the match start/end. A boundary that
    // falls exactly between elements belongs to the LATER element for the
    // start (offset 0) and the EARLIER one for the end (offset = length),
    // so partial slices stay minimal.
    public int startEl;
    public int endEl;
    public int startOff;
    public int endOff;
    public int startByte;
    public int endByte;
    public Aspose.Pdf.Text.TextReplacer.TjSplitPlan plan = null!;
    // Suffix: the post-match slice plus the whole elements after it.
    public byte[] endBytes = null!;
    public PdfArray arr = default!;
    public string search = default!;
    public Dictionary<int, string>? toUnicode = null;
    public PdfDictionary? fontDict = null;
    public double fontSize = 0;
    public double tc = 0;
    public double tw = 0;
    public PdfReader reader = default!;
}
}

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class DtpRichTextState
{
    public List<Aspose.Pdf.Converters.HtmlToPdfConverter.DtpLogicalLine> lines = null!;
    public DtpLogicalLine? cur;
    public int bold;
    public int ital;
    public int under;
    public int link;
    public Stack<string> alignStack = null!;
    // span style overrides nest; (size, face, color) frames.
    public Stack<(double? Size, string? Face, (double, double, double)? Color)> spanStack = null!;
    public double msoFirstIndent;
    public double msoHangIndent;
    public bool inMso;
    public int idx;
    public System.Text.RegularExpressions.Regex tokRx = null!;
    public string inner = default!;
    public DtpClassRule baseClass = default!;
    public Dictionary<string, DtpClassRule> classRules = default!;
    public string defaultAlign = default!;
    public System.Text.RegularExpressions.Match tm = null!;
    public int textEnd;
    public bool close;
    public string name = null!;
    public string attrs = null!;
}
}

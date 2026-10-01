using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class DnnWalkState
{
    public bool lastRowPad;
    public int pos;
    public DnnFieldRow? row;
    public double runX;
    public bool brRun;
    public string inner = default!;
    public double indentPx = 0;
    public List<DnnBlock> blocks = default!;
    public bool inRowPad = false;
    public System.Text.RegularExpressions.Match m = null!;
    public int openIdx;
    public string open = null!;
    public string tag = null!;
    public string cls = null!;
    public string st = null!;
}
}

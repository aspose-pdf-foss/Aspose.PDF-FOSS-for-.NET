using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class BarSvgState
{
    public System.Text.RegularExpressions.Match open = null!;
    public double vw;
    public double vh;
    // tokenizer over g/line/rect/text/path with a translate stack
    public Stack<(double tx, double ty, string fill)> stack = null!;
    public StringBuilder sb = default!;
    public string svg = default!;
    public double xPt = 0;
    public double yTopTd = 0;
    public double pageHeight = 0;
    public Color chartGray = default!;
    public System.Globalization.CultureInfo invc = default!;
    public string tag = null!;
    public string attrs = null!;
    public (double tx, double ty, string fill) top;
}
}

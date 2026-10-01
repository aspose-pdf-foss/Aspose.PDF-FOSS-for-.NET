using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.PdfToMarkdown;

internal static partial class MarkdownRenderer
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class PaintedPathsState
{
    public List<(Aspose.Pdf.Rectangle box, string elem)> paints = null!;
    public double[] ctm = null!;
    public Stack<double[]> stack = null!;
    public List<(double x, double y)> pts = null!;
    public System.Text.StringBuilder d = null!;
    public string fill = null!;
    public Page page = default!;
}
}

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class SplitPanelFlowState
{
    public List<Aspose.Pdf.Converters.HtmlToPdfConverter.SpBlock> blocks = null!;
    public Converters.HtmlToPdfConverter.SpBlock cur = null!;
    public List<(string Face, double Fs, Aspose.Pdf.Color? Col)> faceStack = null!;
    public int boldDepth;
    public double indent;
    public System.Text.StringBuilder text = null!;
    public bool pendingPMargin;
    public string inner = default!;
}
}

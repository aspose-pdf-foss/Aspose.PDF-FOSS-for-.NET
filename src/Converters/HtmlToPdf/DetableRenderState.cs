using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class DetableRenderState
{
    public double contentX;
    public double contentTop;
    public double tableX;
    // columns from the th width attributes
    public List<double> colW = null!;
    public List<string> thTexts = null!;
    // data rows: per cell, the widget element flag + the label text
    public List<List<(bool el, string lbl)>> rows = null!;
    // whole-table avoidance: a table that cannot fit the remainder opens on
    // a fresh page, its top border 11.8 under the content top
    public double tableH;
    public double top;
    public double gridTop;
    public double y;
    public StepRowsState sw = default!;
    public string tableHtml = default!;
    public double yTd = 0;
    public double limit = 0;
}
}

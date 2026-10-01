using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class EmberChartRenderState
{
    public System.Globalization.CultureInfo invc = null!;
    public double originX;
    public double originY;
    // ── the cards ──
    public List<(double x, double y, double w, double h, List<string> fields)> cards = null!;
    public double maxRight;
    public Document doc = null!;
    public double sheetW;
    public Page page = null!;
    public System.Text.StringBuilder sb = null!;
    public double drop;
    public string html = default!;
    public double pageWidth = 0;
    public double pageHeight = 0;
}
}

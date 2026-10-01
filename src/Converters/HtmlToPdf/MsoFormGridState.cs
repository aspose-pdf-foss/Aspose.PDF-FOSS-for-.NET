using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class MsoFormGridState
{
    public System.Globalization.CultureInfo invc = null!;
    // ── column solve ─────────────────────────────────────────────────────
    public int nCols;
    public double[] colW = null!;
    public double tableW;
    public double pageW;
    public Page page = null!;
    public double x0;
    public double yTop;
    public double y;
    public System.Text.StringBuilder sb = null!;
    public System.Text.StringBuilder tsb = null!;
    // roster group state: the nested checkbox table draws inside the host
    // cell's border box, with the empty side cell braced to its right
    public bool groupOpen;
    public double groupTop;
    public Document doc = default!;
    public List<MsoRow> rows = default!;
    // row height: teal/band rows take the style height; input rows grow
    // label line + box + the measured bottom band; text rows by lines.
    public double rowH;
    // an inkless teal band row draws the measured .1in band
    public bool allTeal;
    public double cx;
    public int ci;
    public bool hostRow;
}
}

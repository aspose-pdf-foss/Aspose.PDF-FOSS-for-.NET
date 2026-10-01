using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class BarChartRenderState
{
    // This export is laid out on the UA sheet: 90 pt side margins
    // + the 6 pt body margin, 72 pt top + the same body margin (measured:
    // the stray lead glyph at (96, 78)).
    public double marginLeft;
    public double marginTop;
    public System.Globalization.CultureInfo invc = null!;
    public Document doc = null!;
    public Page page = null!;
    public double contentL;
    public double contentR;
    public double contentW;
    public Color chartGray = null!;
    public System.Text.StringBuilder sb = null!;
    // ── the document flow above the chart ──
    // Any bare text before the chart div sets one UA text line at the origin.
    public double yTd;
    public System.Text.RegularExpressions.Match chartOpen = null!;
    public string lead = null!;
    // ── the chart row: three svgs in a flex row inside the centred 75% box ──
    public System.Text.RegularExpressions.Match container = null!;
    public double contPct;
    public double contW;
    public double contX;
    public double rowTopTd;
    public System.Text.RegularExpressions.Match rowM = null!;
    public double rowHPt;
    // walk the row's immediate div children: each advances the pen by ITS
    // declared width; an svg inside draws clipped to the SVG box at the div's x
    public double penX;
    public int rowEnd;
    // the axis caption: its 100px box centred in the container, text left in it
    public System.Text.RegularExpressions.Match capM = null!;
    public string html = default!;
    public double pageWidth = 0;
    public double pageHeight = 0;
}
}

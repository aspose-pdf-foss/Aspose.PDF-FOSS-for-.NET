using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class PrintPageState
{
    public double pageWidth;
    public double contentL;
    public double contentR;
    public double canvasBot;
    public double hdrBorder;
    public double ftrBorder;
    public Color? canvasBg;
    public string headerText = null!;
    public string footerText = null!;
    public bool headerCentered;
    // Leaf `.page` divs (the outer wrapper contains everything, including the
    // bands — only the leaves are content pages).
    public List<(string text, double lineBox)> pages = null!;
    // ── layout ──
    public Document doc = null!;
    public System.Globalization.CultureInfo invc = null!;
    public double contentTop;
    public double ftrBandTop;
    public double drop12;
    public Page page = null!;
    public double y;
    public double cellL;
    public double cellR;
    public string html = default!;
    public IReadOnlyDictionary<string, Dictionary<string, string>> css = default!;
    public double pageHeight = 0;
    // the @media body width, the band heights and the three declared backgrounds
    public double bodyW;
    public double hdrH;
    public double ftrH;
    public Color? hdrBg;
    public Color? ftrBg;
    // the Times New Roman win metrics every baseline drop measures against
    public (double asc, double sum) fm;
}
}

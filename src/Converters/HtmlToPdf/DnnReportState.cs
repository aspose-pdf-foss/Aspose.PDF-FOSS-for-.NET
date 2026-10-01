using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class DnnReportState
{
    public double pageH;
    public double marginL;
    public double marginR;
    public double marginT;
    public double marginB;
    public double pageW;
    public double bandBottom;
    public double contentL;
    public double contentR;
    public double moduleL;
    public double moduleR;
    public double bodyL;
    // ── harvest the header pieces ──────────────────────────────────────
    public string? logoUrl;
    public double logoHpx;
    public System.Text.RegularExpressions.Match logoM = null!;
    public System.Text.RegularExpressions.Match dateM = null!;
    public string dateText = null!;
    // ── module content: every ModuleContainer in document order (the page
    // stacks several modules; their boxes render flush as one) ───────────
    public List<Aspose.Pdf.Converters.HtmlToPdfConverter.DnnBlock> blocks = null!;
    // ── layout + draw ──────────────────────────────────────────────────
    public Document doc = null!;
    public Aspose.Pdf.Core.PdfDictionary fontDict = null!;
    public System.Globalization.CultureInfo inv = null!;
    public List<List<string>> pageOps = null!;
    public List<List<(byte[] Bytes, double X, double YTop, double W, double H)>> pageStamps = null!;
    public List<double> pageBottom = null!;
    public List<double> moduleTopPerPage = null!;
    public List<double> moduleBotPerPage = null!;
    public (double, double, double) teal;
    public (double, double, double) subBg;
    public (double, double, double) gridHdrBg;
    public (double, double, double) listBg;
    public (double, double, double) black;
    public (double, double, double) white;
    public double asc8;
    // module flow
    public int page;
    public double y;
    // ── assemble pages: frame first, then content ──────────────────────
    public (double, double, double) borderBlue;
    public (double, double, double) nearWhite;
    public double logoWpx;
}
}

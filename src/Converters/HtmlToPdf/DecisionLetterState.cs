using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class DecisionLetterState
{
    public string c = null!;
    public System.Globalization.CultureInfo inv = null!;
    public System.Text.RegularExpressions.Match bodyM = null!;
    public System.Text.RegularExpressions.Match faceM = null!;
    public string face = null!;
    public double bodyFs;
    public System.Text.RegularExpressions.Match basisM = null!;
    public double basisW;
    public System.Text.RegularExpressions.Match padM = null!;
    public double sidePad;
    public double pageWidth;
    public double cx;
    public double cw;
    public double lineH;
    public Color ink = null!;
    public Color bandBlue = null!;
    public Color borderLite = null!;
    public Color frameGray = null!;
    public Document doc = null!;
    public Page page = null!;
    public Aspose.Pdf.Core.PdfDictionary docFontDict = null!;
    public System.Text.StringBuilder sb = null!;
    public List<(Aspose.Pdf.Page pg, System.Text.StringBuilder ops)> pageStreams = null!;
    // ── header: broken logo + floated title column + broken QR ──
    public Aspose.Pdf.Core.PdfIndirectRef? iconRef;
    public System.Text.RegularExpressions.Match qrPadM = null!;
    public double qrPad;
    public double hdrImgW;
    public System.Text.RegularExpressions.Match hdrImgM = null!;
    public double titlePadLeft;
    public double titleX;
    public System.Text.RegularExpressions.Match h3M = null!;
    public System.Text.RegularExpressions.Match h1M = null!;
    public double y;
    // ── the info tables: cells = label-span lines ──
    public List<List<List<(string label, string val)>>> infoTables = null!;
    // r1: the banded 3-column grid — equal declared columns, band fill,
    // #d3e0ec horizontals and #f3f3f3 verticals; 12px/15px cell padding
    public double infoTop;
    public List<List<(string label, string val)>> r1 = null!;
    public double r1RowH;
    public int nCols1;
    public double colW1;
    // r2: the auto-layout contact strip — columns at max-content + 15 px
    // side pads, surplus ∝ content (the engine's auto solve)
    public List<List<(string label, string val)>> r2 = null!;
    public double r2Top;
    public double r2RowH;
    public double infoBottom;
    // ── Collateral: th band + value row, equal declared columns ──
    public double sectionTop0;
    public System.Text.RegularExpressions.Match colM = null!;
    public double colBottom;
    // ── the 48.5 % float pair: left Dealer Structure, right the
    //    Stipulations + Comments stack ──
    public double midTop;
    public System.Text.RegularExpressions.Match halfM = null!;
    public double halfPct;
    public double halfW;
    public double rightX;
    // left: bold-label / value rows at the UA 13 px line in 2px-padded rows
    public double leftBottom;
    public System.Text.RegularExpressions.Match dealerM = null!;
    // right: Stipulations p-rows, then the Comments paragraph box
    public double rightBottom;
    // ── Notification Disclosure (full width, after the clear) ──
    public double discTop;
    public double pageHeight;
    public (double asc, double sum) wm;
}
}

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class AuditLetterState
{
    public System.Globalization.CultureInfo inv = null!;
    public string c = null!;
    public double marginTop;
    public double marginBottom;
    // the editor column's wrap edge (measured: the widest paragraph line
    // runs to 567.7 — the card's inner box less the content-area pads)
    // …plus ~4 pt headroom over our Arial advances (the expected
    // lines are kerned slightly tighter than our advance sums)
    // measured: the widest expected line runs to 577.7
    public double contentRight;
    public Color inkBody = null!;
    public Color inkEditor = null!;
    public Color inkAbbr = null!;
    public Color inkCode = null!;
    public Color railGray = null!;
    public Document doc = null!;
    public Page page = null!;
    public Aspose.Pdf.Core.PdfDictionary docFontDict = null!;
    public System.Text.StringBuilder sb = null!;
    public List<(Aspose.Pdf.Page pg, System.Text.StringBuilder ops)> streams = null!;
    public double y;
    public double railFrom;
    // ── the heading group ──
    public System.Text.RegularExpressions.Match h1M = null!;
    // ── the label rows ──
    // editor paragraph model: runs of (text, italic, color)
    public System.Text.RegularExpressions.Regex pRunRx = null!;
    public string contentArea = null!;
    public (double asc, double sum) wm;
    public double pageHeight;
    public double pageWidth;
}
}

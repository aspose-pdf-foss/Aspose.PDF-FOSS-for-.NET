using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ResumeDocState
{
    public string face = null!;
    public System.Globalization.CultureInfo inv = null!;
    public string c = null!;
    public double marginTop;
    public double marginBottom;
    public Document doc = null!;
    public Aspose.Pdf.Core.PdfDictionary docFontDict = null!;
    public Page page = null!;
    // fragments buffer: titles flush FIRST on each page
    public List<(int grp, double x, double y, double fs, bool bold, Aspose.Pdf.Color col, string t)> frags = null!;
    public Color black = null!;
    public double y;
    // rich text: runs of (text, bold, color) — each SPAN piece keeps its
    // own fragment, split further at line breaks
    public System.Text.RegularExpressions.Regex runRx = null!;
    // ── the walk ──
    public int seq;
    public bool firstSection;
    public double pageWidth;
    public double pageHeight;
    public (double asc, double sum) wm;
    public string kind = null!;
    public string inner = null!;
    public System.Text.RegularExpressions.Match titleM = null!;
    public int paraN;
}
}

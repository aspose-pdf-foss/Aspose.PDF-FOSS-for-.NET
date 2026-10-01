using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ContractInvoiceState
{
    public System.Globalization.CultureInfo inv = null!;
    // the linked stylesheet is inlined by the preprocessor when reachable;
    // fetch it directly when the raw link is still in place
    public string cssText = null!;
    public Dictionary<(int weight, bool italic), string> faces = null!;
    public Color ink = null!;
    public Color blue = null!;
    public Color orange = null!;
    public Color white = null!;
    public Document doc = null!;
    public Aspose.Pdf.Core.PdfDictionary docFontDict = null!;
    public Page page = null!;
    public System.Text.StringBuilder sb = null!;
    public List<(Aspose.Pdf.Page pg, System.Text.StringBuilder ops)> streams = null!;
    // ── the header: blue info panel + fetched logo ──
    public double yTop;
    public double y;
    // ── the contract blocks ──
    public double[] colShare = null!;
    public double tX;
    public double tW;
    public double[] colX = null!;
    public bool first;
    public string html = null!;
    public double pageHeight = 0;
    public string lightFace = null!;
    public (double asc, double sum) wm;
    public string regFace = null!;
}
}

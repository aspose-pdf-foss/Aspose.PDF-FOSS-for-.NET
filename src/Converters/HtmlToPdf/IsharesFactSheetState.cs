using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class IsharesFactSheetState
{
    public List<Aspose.Pdf.Converters.HtmlToPdfConverter.IfsColumn> cols = null!;
    public Document doc = null!;
    public Aspose.Pdf.Core.PdfDictionary docFontDict = null!;
    public Page page = null!;
    public System.Globalization.CultureInfo inv = null!;
    public System.Text.StringBuilder sb = null!;
    public string html = default!;
    public Converters.HtmlToPdfConverter.IfsColumn col = null!;
    public bool isAllo2;
    public double x0;
    public double tableW;
    public double x1;
    // The hang cell fits the widest suffix run; a percent span outside
    // the first row is hidden but keeps its width less the -7 pt margin.
    public double hangW;
    // The value cell fits the widest head line (sup included).
    public double valueW;
    public double labelW;
    public double labelRight;
    public double valueRight;
    // Column heads: value-head lines right-aligned at the value edge
    // (sup included), the label head on the LAST line's natural baseline;
    // a line holding <ins> content seats half a point lower.
    public double headBase;
    public double headPitch;
    public double lastHeadBase;
    public double ruleTd;
    // Rows.
    public double dotW;
    public double yRow;
    public double closeTd;
}
}

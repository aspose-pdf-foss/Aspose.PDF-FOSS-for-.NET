using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ResultCardState
{
    // Gate: a px-width body style, a nowrap label class with paddings, and a
    // px-width padded background box div — the results-card shape.
    public System.Text.RegularExpressions.Match bodyM = null!;
    public double bodyPx;
    public Dictionary<string, string>? labelCls;
    public string? labelClsName;
    public System.Text.RegularExpressions.Match boxM = null!;
    public double pageWidth;
    public Document doc = null!;
    public Aspose.Pdf.Core.PdfDictionary docFontDict = null!;
    public Page page = null!;
    public double contentX;
    public double contentW;
    public (double asc, double sum) arial;
    public (double asc, double sum) serif;
    public System.Text.StringBuilder sb = null!;
    public System.Globalization.CultureInfo inv = null!;
    public double y;
    // Walk the body content in source order: imgs (alt text), spacer divs,
    // styled text divs, and the padded box (its inner label table).
    public string body = null!;
    public int boxStart;
    // 3. the padded background box with its label table.
    public double boxPx;
    public Color boxCol = null!;
    public double boxPad;
    public double padTop;
    public double padBottom;
    public double padRight;
    public double labelFs;
    // rows: label td (class) + value td, from the box's inner table
    public string boxHtml = null!;
    public List<(string label, string value)> rows = null!;
    public double rowLine;
    public double rowPitch;
    public double boxH;
    public double tableX;
    public double labelW;
    // measured: the label column box = widest label + its padding-right +
    // one default-font em of slack (130.13 on the card's reference)
    public double labelBoxW;
    public double valueX;
    public double rowTop;
    public double innerW;
    public System.Text.RegularExpressions.Match footerAlt = null!;
    public string html = default!;
    public IReadOnlyDictionary<string, Dictionary<string, string>> css = default!;
    public double pageHeight = 0;
}
}

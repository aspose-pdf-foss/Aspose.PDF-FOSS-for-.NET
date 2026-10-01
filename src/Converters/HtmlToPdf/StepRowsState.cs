using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class StepRowsState
{
    public System.Globalization.CultureInfo invc = null!;
    public double contentTop;
    public double bulletX;
    public double contentX;
    public double limit;
    public double pageWidth;
    public Document doc = null!;
    public Page page = null!;
    public System.Text.StringBuilder sb = null!;
    public double yTd;
    public string html = default!;
    public IReadOnlyDictionary<string, Dictionary<string, string>> css = default!;
    public double pageHeight = 0;
    public int srEnd;
    public string srBody = null!;
    public System.Text.RegularExpressions.Match bulletM = null!;
    public string bullet = null!;
    public System.Text.RegularExpressions.Match scM = null!;
    public int scEnd;
    public string content = null!;
    public double rowTop;
    public System.Text.RegularExpressions.Match detM = null!;
    // heading (steps 2+): a centred hN whose weight follows <strong>
    public System.Text.RegularExpressions.Match headM = null!;
    public bool consumedHead;
    // the attribute table (steps 2-4): 2 percent columns of flattened
    // widget text on the 18px line
    public System.Text.RegularExpressions.Match atM = null!;
}
}

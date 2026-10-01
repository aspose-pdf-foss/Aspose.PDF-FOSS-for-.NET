using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class InvoiceCardState
{
    public string inner = null!;
    // measure the block: h2 (2 lines when the description floats) +
    // thead + body rows (the total row runs 2 lines in its column)
    public List<List<string>> rows = null!;
    public List<string> ths = null!;
    public int nPlain;
    public double lastRowH;
    public double blockH;
    public double h2Base;
    // h2: 'Contract #: ' label + value, description right-aligned on
    // its own line
    public System.Text.RegularExpressions.MatchCollection spans = null!;
    public string lbl1 = null!;
    public string val1 = null!;
    public string lbl2 = null!;
    public string val2 = null!;
    // a SHORT description floats beside the contract line; a long one
    // wraps under it, and the table then clears the taller float
    public double line1W;
    public double descW;
    public bool descSameLine;
    // thead band + centred 15 pt headers (the first wraps)
    // a same-line float leaves the h2 margin gap; a wrapped float
    // hugs the table (both measured)
    public double thTop;
    // body rows: bordered #f69a1b cells, centred values; the LAST row
    // is borderless — its middle cell right-aligns and wraps
    public double rTop;
}
}

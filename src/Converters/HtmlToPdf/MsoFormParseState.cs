using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class MsoFormParseState
{
    public List<Aspose.Pdf.Converters.HtmlToPdfConverter.MsoRow> rows = null!;
    public MsoRow? row;
    public MsoCell? cell;
    // the open inline style (span/b/i nesting collapses onto one state)
    public double fs;
    public bool white;
    public bool pendingLine;
    public bool pendingBr;
    public int nestedDepth;
    public System.Text.StringBuilder text = null!;
    public bool inSelect;
    public bool inSelectedOption;
    public string tableHtml = default!;
    public IReadOnlyDictionary<string, double> idW = default!;
    public IReadOnlyDictionary<string, double> idH = default!;
    public string face = null!;
    public bool bold;
    public bool ital;
    public bool teal;
    public bool center;
}
}

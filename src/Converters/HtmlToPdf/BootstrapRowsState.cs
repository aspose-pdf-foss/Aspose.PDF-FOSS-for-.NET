using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class BootstrapRowsState
{
    public string face = null!;
    public Document doc = null!;
    public Page page = null!;
    public System.Globalization.CultureInfo invc = null!;
    public double contentL;
    public double contentR;
    public double limit;
    // baseline seat inside the 20px line box (half-leading + the face ascent)
    public double drop;
    public double dropH3;
    // ── walk the body's top-level constructs in document order ──
    public System.Text.RegularExpressions.Match bodyM = null!;
    public int pos;
    public double yTd;
    public double pendingMb;
    public System.Text.RegularExpressions.Regex construct = null!;
    public string html = default!;
    public IReadOnlyDictionary<string, Dictionary<string, string>> css = default!;
    public double pageWidth = 0;
    public double pageHeight = 0;
}
}

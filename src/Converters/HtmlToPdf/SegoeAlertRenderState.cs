using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class SegoeAlertRenderState
{
    public byte[]? segoe;
    public byte[]? segoeBold;
    public System.Globalization.CultureInfo invc = null!;
    public Color grey = null!;
    public Color red = null!;
    public Color white = null!;
    public Color black = null!;
    public Document doc = null!;
    public Page page = null!;
    // ── the banner and its hr ──
    public System.Text.RegularExpressions.Match bodyM = null!;
    // ── the sensor grid ──
    public System.Text.RegularExpressions.Match gm = null!;
    public string html = default!;
    public IReadOnlyDictionary<string, Dictionary<string, string>> css = default!;
    public double pageWidth = 0;
    public double pageHeight = 0;
}
}

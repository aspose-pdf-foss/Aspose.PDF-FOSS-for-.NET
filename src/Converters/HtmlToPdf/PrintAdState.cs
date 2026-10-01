using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class PrintAdState
{
    public string? contCls;
    public Dictionary<string, string>? contRule;
    public System.Text.RegularExpressions.Match bodyM = null!;
    public string? face;
    public double baseFs;
    public double lineFactor;
    public System.Text.RegularExpressions.Match contM = null!;
    public string? inner;
    public double pageWidth;
    public double xText;
    public double contentW;
    // The `li:before` marker (content + margin-right), read off the raw css.
    public System.Text.RegularExpressions.Match beforeM = null!;
    public string liMarker = null!;
    public double liMarkerGap;
    public double h6Fs;
    public double h1Fs;
    public double h1SpanFs;
    public double descFs;
    public double pMb;
    public double h1Mb;
    public Dictionary<string, string>? h6Rule;
    public double h6Mb;
    public double blockMb;
    public Document doc = null!;
    public Aspose.Pdf.Core.PdfDictionary docFontDict = null!;
    public Page page = null!;
    public System.Text.StringBuilder sb = null!;
    public System.Globalization.CultureInfo inv = null!;
    public double y;
    public double pendingGap;
    // ── the walk: h1 / h6 / p / ul / bare text / img-alt, hidden classes skipped
    public System.Text.RegularExpressions.Regex tagRx = null!;
    public string html = null!;
    public IReadOnlyDictionary<string, Dictionary<string, string>> css = null!;
    public double pageHeight = 0;
    public HtmlLoadOptions? options = null;
    public (double asc, double sum) wm;
    public double h6PadBottom;
    public Color? h6Border;
}
}

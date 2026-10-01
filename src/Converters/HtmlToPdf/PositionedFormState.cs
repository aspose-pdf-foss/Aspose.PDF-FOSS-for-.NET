using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class PositionedFormState
{
    public System.Text.RegularExpressions.Match wrapM = null!;
    public double wrapPx;
    public double pageWidth;
    public Document doc = null!;
    public Aspose.Pdf.Core.PdfDictionary docFontDict = null!;
    public Page page = null!;
    public double contentBottom;
    public double wrapX;
    public double wrapW;
    public (double asc, double sum) arial;
    public double fs;
    public double lineBox;
    public double drop;
    public double spaceAdv;
    public System.Globalization.CultureInfo inv = null!;
    public System.Text.StringBuilder sb1 = null!;
    public System.Text.StringBuilder sb2 = null!;
    // h1 header
    public double contentX;
    public System.Text.RegularExpressions.Match h1M = null!;
    // the sections, in order
    public double secY;
    public double secX;
    public double lastBottom;
    // footer h4 on the continuation page
    public System.Text.RegularExpressions.Match h4M = null!;
    // the white wrapper band under everything, both pages
    public string p1Band = null!;
    public string html = default!;
    public HtmlLoadOptions? options = null;
    public double pageHeight = 0;
}
}

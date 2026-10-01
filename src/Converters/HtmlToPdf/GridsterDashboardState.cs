using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class GridsterDashboardState
{
    public List<Aspose.Pdf.Converters.HtmlToPdfConverter.GridsterItem> items = null!;
    public List<Aspose.Pdf.Converters.HtmlToPdfConverter.GridsterItem> widgets = null!;
    public System.Globalization.CultureInfo inv = null!;
    // Page: the widest widget's right edge plus the container padding, between
    // the document's own margins (the HTML defaults this dialect keeps).
    public double marginLeft;
    public double marginRight;
    public double originX;
    public double widestPx;
    public double pageWidth;
    public Document doc = null!;
    public Page page = null!;
    public Dictionary<string, string> resByFace = null!;
    public System.Text.StringBuilder sb = null!;
    // The gridster's own top: the page margin plus the container's padding and
    // the .pdf-gridster-container padding-top (10 px).
    public double originY;
    public string html = default!;
    public double pageHeight = 0;
}
}

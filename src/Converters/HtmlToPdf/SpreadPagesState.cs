using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class SpreadPagesState
{
    public System.Text.RegularExpressions.MatchCollection spreads = null!;
    public double bodyEmPx;
    public double contentEmPx;
    public double bodyFs;
    public double h1Fs;
    public double natFs;
    public double capFs;
    public double bodyPitch;
    public double capPitch;
    public double headPitch;
    // .page3 box: padding around the content column, the noind paragraphs'
    // own left margin, the figure/caption colour.
    public double padL;
    public double padR;
    public double padT;
    public double noindML;
    // Metrics: ascent-only baseline seat for headings/floats (line-height
    // normal), the half-leading drop for the 1.1em body lines.
    public double h1Asc;
    public double h1MarginY;
    public double bodyDrop;
    public double bodyDesc;
    public double natAsc;
    public double capDrop;
    public double pageWidth;
    public double pageHeight;
    public double contentL;
    public double contentR;
    public double contentW;
    public Document doc = null!;
    public System.Globalization.CultureInfo invc = null!;
    public string html = default!;
    public IReadOnlyDictionary<string, Dictionary<string, string>> css = default!;
    public HtmlLoadOptions? options = null;
    public double defaultPageHeight = 0;
    public Color capColor = null!;
    public (double asc, double sum) arialM;
}
}

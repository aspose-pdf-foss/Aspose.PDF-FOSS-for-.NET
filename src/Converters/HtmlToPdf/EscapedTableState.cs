using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class EscapedTableState
{
    public List<List<(bool Header, List<Aspose.Pdf.Converters.HtmlToPdfConverter.Block> Items)>> trRows = null!;
    public int nCols;
    public double[] colFull = null!;
    public double[] colMin = null!;
    public double[] colW = null!;
    public double gridChrome;
    public List<(List<(List<(Aspose.Pdf.Converters.HtmlToPdfConverter.Block? Ctl, string? Txt, double XOff, double FontPt, string Res)> Items, double H)> Lines, double ContentH)[]> planRows = null!;
    public List<double> rowHs = null!;
    public double tableW;
    public double tableH;
    // The grid's top edge sits one text ascent above the cursor (the flow
    // runs in baseline space); a grid that no longer fits moves whole.
    public double gridTop;
    public Color? gridDark;
    public double gx;
    public double rowTop;
}
}

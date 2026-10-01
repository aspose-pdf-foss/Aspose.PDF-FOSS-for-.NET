using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class GanttChartState
{
    public System.Globalization.CultureInfo inv = null!;
    public Document doc = null!;
    public Page page = null!;
    public Dictionary<string, string> resByFace = null!;
    public System.Text.StringBuilder sb = null!;
    // (size, x, y, text, face, ink) — flushed after the fills so bars sit UNDER
    // their labels.
    public List<(double Size, double X, double Y, string Text, string Face, (double R, double G, double B) Ink)> texts = null!;
    // The page title rides the ordinary UA flow above the widget.
    public System.Text.RegularExpressions.Match title = null!;
    public double gridX;
    public double timelineX;
    public double dataTop;
    // ── the left grid ──
    public System.Text.RegularExpressions.Match gridScale = null!;
    public List<(double WidthPx, string Align, string Text, double IndentPx)> headCells = null!;
    public List<double> colX = null!;
    public List<double> colW = null!;
    public List<List<(double WidthPx, string Align, string Text, double IndentPx)>> rows = null!;
    // The widget chrome (all measured):
    //  - the outer frame and the grid/timeline separator in the darker rule gray;
    //  - an hour line at every scale-cell boundary and a row line under every row
    //    band, in the light #ebebeb — the timeline lines run the full declared
    //    1190 px area, the row lines both panes.
    public double areaBottom;
    public double frameRight;
    public double frameBottom;
    // ── the timeline scale ──
    public System.Text.RegularExpressions.Match taskScale = null!;
    // ── the connector lines (each segment is its own declared box) ──
    public System.Text.RegularExpressions.Match linksArea = null!;
    // ── the task bars ──
    // The timeline viewport clips at the .gantt_task box (probed: the last
    // bar's label shows as "Lau").
    public double viewportRight;
    public System.Text.RegularExpressions.Match barsArea = null!;
    public string barsHtml = null!;
    public string html = default!;
    public HtmlLoadOptions? options = null;
    public double pageWidth = 0;
    public double pageHeight = 0;
    public double marginLeft = 0;
    public double marginTop = 0;
}
}

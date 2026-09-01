using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Stamps;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class HeaderFooter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ReportRegionState
{
    // The dialect's rhythm: `div { font-size: small }` = 13 css px = 9.75 pt on the
    // face's normal 17 px line = 12.75 pt. Every distance below is an empirical
    // constant of the dialect, holding on both fieldsets of the region:
    //   FsLegendDrop  — frame top → legend baseline: the browser opens the frame at
    //                   the legend's mid-cap, so the border crosses its letters;
    //   FsLegendToRow — legend baseline → first row baseline (the frame's padding
    //                   plus one row seat);
    //   FsPadBottom   — last row baseline → frame bottom (padding + descent);
    //   FsGap         — frame bottom → the next frame's top (8 css px);
    //   CheckRowPitch — a row holding an <input> takes the input's taller line box.
    public double fs;
    public double pitch;
    public bool draw;
    public System.Text.RegularExpressions.Regex rx = null!;
    public double yBase;
    public int pos;
    public List<(string inner, double frac)> pendingCols = null!;
    // The region inputs, captured from the method parameters.
    public Page? page;
    public ContentStreamBuilder? b;
    public string html = null!;
    public double x;
    public double w;
    public double yTopBase;
    public bool inFieldset;
    public string? boldRes;
    public string? plainRes;
}
}

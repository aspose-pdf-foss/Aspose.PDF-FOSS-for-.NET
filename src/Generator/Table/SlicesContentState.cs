using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class Table
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class SlicesContentState
{
    public Content.ContentStreamBuilder builder = null!;
    public List<(Aspose.Pdf.Rectangle rect, Aspose.Pdf.Hyperlink link)>? links;
    public List<(Aspose.Pdf.Forms.RadioButtonOptionField opt, Aspose.Pdf.Rectangle rect)>? optionSink;
    // Checkbox placements are recorded for every real page: the first page
    // binds its widgets now, a spill page's are handed to the flow dispatcher
    // (LastCheckboxDraws) for the page that materialises later.
    public List<(Aspose.Pdf.Forms.CheckboxField cbf, Aspose.Pdf.Rectangle rect)>? checkboxSink;
    public List<(byte[] data, Aspose.Pdf.Rectangle rect)> pageImages = null!;
    public List<(ReservedBlock block, ReservedPart part, Aspose.Pdf.Rectangle rect)> pageBlocks = null!;
    public List<byte[]> pageGraphs = null!;
    public List<(Aspose.Pdf.Note note, double x, double baseline, double size)>? pageFootnotes;
    public List<(Aspose.Pdf.Table.CellLine Line, string Original)>? macroSwaps;
    // Blocks whose content is written when the slice for their middle row has been
    // laid down (see EmitSpanBlockContent).
    public List<(Aspose.Pdf.Table.SpanBlock block, double x, double w, double h, double top, double bottom, int contentRow)> spanContent = null!;
    public Cell cell = null!;
    public Row row = null!;
    public MarginInfo? padding;
    public double dp;
    public double padLeft;
    public double padRight;
    public double padTop;
    public double gapsTotal;
    public double blockH;
    // A spanning cell's content block: an unset alignment centres it
    // vertically in the portion visible on the page; an EXPLICIT Top
    // pins it to the span top (stacking down at line pitch), and
    // Bottom seats it on the span bottom.
    public VerticalAlignment effVA;
    public double offset;
    public double gapAccum;
    public Table.CellLine line = null!;
    public double tw;
    public double lineX;
    public double lineBase;
}
}

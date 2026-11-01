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
private sealed class TableDrawState
{
    public string fontName = null!;
    public double[] colWidths = null!;
    public Content.ContentStreamBuilder builder = null!;
    // The layout frame's height (see Page.LayoutFrameHeight): a /Rotate page's
    // table lays out in the page as shown.
    public double pageHeight;
    // Table origin in PDF coordinates (bottom-left origin)
    public double marginLeft;
    public double marginTop;
    public double tableX;
    public double tableTopY;
    // Render rows
    public double currentY;
    public Page page = default!;
    public Row row = null!;
    public double rowHeight;
    public double cellX;
    public Cell cell = null!;
    public double cellWidth;
    public double cellTopY;
    // Effective padding
    public MarginInfo? padding;
    public double padLeft;
    public double padRight;
    public double padTop;
    // Draw cell background
    public Color? bgColor;
    // Draw cell text content
    public Aspose.Pdf.Text.TextState? textState;
    public double fontSize;
    public double textX;
}
}

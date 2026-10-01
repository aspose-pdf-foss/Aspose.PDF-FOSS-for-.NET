using System.Globalization;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class FloatingBox
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class FloatingBoxBuildState
{
    public double pageHeight;
    public Content.ContentStreamBuilder builder = null!;
    // Calculate the box position in PDF coordinates (origin at bottom-left).
    // Left/Top are in page coordinates where Top is measured from the top edge.
    public double marginLeft;
    public double marginTop;
    public double marginRight;
    public double marginBottom;
    // Box origin (bottom-left corner in PDF coordinates)
    public double boxX;
    public double boxY;
    public double padLeft;
    public double padTop;
    public double padRight;
    public double padBottom;
    // Render paragraph content (text fragments)
    public double contentX;
    public double contentY;
    // Lines a fixed-height box could not show; they open a continuation page below.
    public List<(string Text, double FontSize, string FontRes, Aspose.Pdf.Color? Fill)> overflowLines = null!;
    // The box's own alignment places its CONTENT inside the declared box — the box
    // itself never moves (probed 2026-08-26: a bottom/right aligned 100x100 box
    // still seats at the flow cursor and only its text moves).
    //
    // ⚠ The vertical alignments do NOT lay the children out as one stack:
    //   • Bottom pins EVERY text paragraph's line box bottom to the box's bottom
    //     edge — three paragraphs of 10/14/10 pt all bottom on the same y and
    //     overprint each other.
    //   • Center puts the FIRST paragraph's box centre on the box's centre and
    //     then walks: each following paragraph's centre sits on the previous
    //     paragraph's box BOTTOM (10/14/10 pt centre on 720/715/708 in a
    //     670..770 box).
    //   • Padding takes no part in either axis (a 15 pt padding moved nothing).
    // Top keeps the ordinary stacking cursor.
    public double boxCentreCursor;
    public Page page = default!;
}
}

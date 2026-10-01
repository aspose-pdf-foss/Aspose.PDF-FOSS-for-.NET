using System.Globalization;
using System.Text;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Devices;

public sealed partial class SvgDevice
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class SvgRenderState
{
    public Dictionary<string, Aspose.Pdf.Core.PdfDictionary> fonts = null!;
    public Dictionary<string, Aspose.Pdf.Core.PdfDictionary> extGStates = null!;
    public IO.PdfLexer lexer = null!;
    public List<Aspose.Pdf.Core.PdfObject> operands = null!;
    // tm is the text matrix, tlm the text line matrix (PDF row-vector convention).
    public double[] tm = null!;
    public double[] tlm = null!;
    public Stack<Aspose.Pdf.Devices.SvgDevice.GState> gsStack = null!;
    // The live graphics state. `Q` REPLACES it from the stack, which is why it travelled
    // as a ref parameter before it joined the stack it is pushed onto.
    public Aspose.Pdf.Devices.SvgDevice.GState gs = null!;
    public System.Text.StringBuilder pathData = null!;
    // Path current point in USER coordinates (needed by the v operator).
    public double curX;
    public double curY;
}
}

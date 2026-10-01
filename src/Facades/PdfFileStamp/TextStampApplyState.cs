using System.Text;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfFileStamp
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class TextStampApplyState
{
    public Facades.FormattedText text = null!;
    public Aspose.Pdf.Text.TextState? ts;
    public string fontName = null!;
    // Stamp bounds in page space. The logo box is TextWidth wide and ~1.1·FontSize tall
    // (a single line at default leading); a 90°/270° rotation swaps those dimensions. The
    // box is anchored at the stamp origin and grows +x/+y, per the GetStamps contract.
    public double fontSize;
    public double boxW;
    public double boxH;
    public double rot;
    public bool quarterTurn;
    public double rectW;
    public double rectH;
    public double ox;
    public double oy;
    // Every line of the (possibly multi-line) FormattedText; .Text is only the first.
    public List<string> lines = null!;
    public System.Text.StringBuilder sb = null!;
    public Page page = default!;
    public Stamp stamp = default!;
}
}

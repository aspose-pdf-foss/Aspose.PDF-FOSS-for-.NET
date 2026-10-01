
namespace Aspose.Pdf.Text;

public partial class TextFragment
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class FormFillWrapState
{
    public Aspose.Pdf.Text.Font? font;
    public double fs;
    public double width;
    public Aspose.Pdf.Text.Font writeFont = null!;
    public List<string> wrapped = null!;
    public System.Text.StringBuilder cur = null!;
    // Delete the source operator(s) by REGION — every text op starting inside the
    // fragment's own rect span at its baseline. (Trailing padding spaces in the
    // same operator go with it; nothing observable asserts them.)
    public Aspose.Pdf.Text.TextReplacer del = null!;
    public List<double> targetYs = null!;
    public double baseY;
    // Emit the wrapped lines.
    public Aspose.Pdf.Text.TextBuilder tb = null!;
    public double x0;
    public double y0;
    public double step;
    public List<(string text, double by, double w)> laidOut = null!;
    public double maxLineW;
    public Page page = default!;
    public string oldText = default!;
    public string newText = default!;
}
}

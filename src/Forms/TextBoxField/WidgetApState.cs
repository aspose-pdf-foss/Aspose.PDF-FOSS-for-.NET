using System.Collections;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

public partial class TextBoxField
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class WidgetApState
{
    public string text = null!;
    // /MK /R rotated widget: lay out in the rotated box, rotate via the form /Matrix.
    public int apRotation;
    public Aspose.Pdf.Core.PdfString? daSrc;
    public string da = null!;
    public string fontName = null!;
    public double fontSize;
    public string[] daParts = null!;
    // Values beyond WinAnsi (Hebrew, CJK …) get the same embedded-Type0
    // treatment as RegenerateAppearance: private face, CID hex, visual order.
    public bool widgetNeedsUni;
    public byte[]? wuTtf;
    public string wuFam = null!;
    public PdfDictionary? wuFonts;
    public string wuRes = null!;
    public string content = null!;
    public Aspose.Pdf.Core.PdfStream apStream = null!;
    public Aspose.Pdf.Core.PdfArray bbox = null!;
    public Aspose.Pdf.Core.PdfDictionary apDict = null!;
    public double w = 0;
    public double h = 0;
    public PdfDictionary? widgetDict = null;
}
}

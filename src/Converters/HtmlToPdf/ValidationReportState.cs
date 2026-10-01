using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ValidationReportState
{
    public byte[] faceReg = null!;
    public byte[] faceIt = null!;
    public byte[] faceSemi = null!;
    public double contentH;
    public System.Text.RegularExpressions.Match bodyM = null!;
    public string src = null!;
    public List<Aspose.Pdf.Converters.HtmlToPdfConverter.VrGroup> groups = null!;
    public Document doc = null!;
    public List<Aspose.Pdf.Page> pages = null!;
    public System.Globalization.CultureInfo invc = null!;
    // The report is laid out in document order but PAINTED in stacking
    // order: a rule frame's own background is measured only once its
    // contents have been placed, so every operator is banked against the
    // layer it belongs to and the sheets are written out at the end.
    public List<(int Sheet, int Layer, int Seq, string Text)> ops = null!;
    public int seq;
    public double boxL;
    public double boxR;
    public double y;
    // == the brand banner ================================================
    public double bannerTop;
    public List<string> items = null!;
    // == the four-column results panel ===================================
    public double panelL;
    public double panelR;
    public double panelContentW;
    public double infoL;
    public double infoW;
    public double infoTop;
    public double colW;
    public double colTop;
    public int colLines;
    public List<(string Label, string Value)> cols = null!;
    public double infoBottom;
    public double genTop;
    public List<(string Label, string Value)> gen = null!;
    public double genContentTop;
    public double genLabelX;
    public double genValueX;
    public double genRowH;
    public double genBottom;
    public double envL;
    public double envW;
    public double envTop;
    public List<(string Label, string Value)> envRows = null!;
    public double envSplit;
    public double envBottom;
    // == the File / Path table ============================================
    public double fileTop;
    public double fileHeadTop;
    public double fileX;
    public double fileSplit;
    public double fy;
    public double listTop;
    public double outerR;
    public double outerContentL;
    public double outerContentR;
    public double listBottom;
    public double pageWidth;
    public double pageHeight;
    public double marginLeft;
    public double marginRight;
    public double marginTop;
    public double marginBottom;
}
}

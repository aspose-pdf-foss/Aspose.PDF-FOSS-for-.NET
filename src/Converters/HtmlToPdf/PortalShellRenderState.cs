using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class PortalShellRenderState
{
    public System.Globalization.CultureInfo inv = null!;
    // The wrapper's declared px width is the content box; without it the
    // page cannot grow and this is a different document.
    public System.Text.RegularExpressions.Match wrapM = null!;
    public double wrapPt;
    public System.Text.RegularExpressions.Match hdrM = null!;
    public double headerPt;
    // The print sheet's body border-top (colour fixed black in this class).
    public System.Text.RegularExpressions.Match barM = null!;
    public double barPt;
    public (double R, double G, double B) canvas;
    public (double R, double G, double B) wrapBg;
    // The .85em body on the 16 px base: 13.6 px = 10.2 pt.
    public double fs;
    public System.Text.RegularExpressions.Match bodyFsM = null!;
    public double pageW;
    public double pageH;
    public double left;
    public double right;
    public double top;
    public double bottom;
    public Document doc = null!;
    public Page page = null!;
    public Dictionary<string, string> resByFace = null!;
    public System.Text.StringBuilder sb = null!;
    public double headerTop;
    public double bannerTop;
    // The header search input (float-cleared) and its empty submit bevel.
    public double inpX;
    public double inpTop;
    public double subX;
    public double subTop;
    // The .welcome list: bullets + items in the body face and ink.
    public List<string> items = null!;
    public System.Text.RegularExpressions.Match welcomeM = null!;
    public double textX;
    public double goX;
    public System.Text.RegularExpressions.Match goValM = null!;
    public string goLabel = null!;
    public double goLabelW;
    public string html = default!;
}
}

using System.Globalization;
using System.Text;
using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

internal static partial class XfaRenderer
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class XfaRenderState
{
    public System.Xml.XmlElement? root;
    // Data-driven occurrence expansion: clone the template and duplicate repeatable
    // subforms once per bound data group, so layout and value binding see the real
    // instance list (e.g. an order subform with occur max=3 and three data rows).
    public System.Xml.XmlElement? dataRoot;
    public List<System.Xml.XmlElement> groups = null!;
    public System.Xml.XmlElement? formRoot;
    public System.Xml.XmlElement origRoot = null!;
    public List<System.Xml.XmlElement> pageAreas = null!;
    // Ordered pageSet progression: the pageAreas are consumed front to
    // back, one area per page up to its occur max (default 1, -1 unbounded), and
    // the LAST area repeats for every remaining page - a first-page master with a
    // big header hands over to the taller continuation master from page 2 on.
    // An explicit breakBefore target overrides the cursor (handled at the break).
    public int masterIdx;
    public int pagesOnMaster;
    public Dictionary<string, byte[]> xfaImages = null!;
    // id="…" elements (floatingFields etc.) referenced by rich-text <span xfa:embed="#id"/>.
    public Dictionary<string, System.Xml.XmlElement> idElements = null!;
    public List<(double w, double h, List<Aspose.Pdf.Forms.Xfa.XfaRenderer.Item> items)> newPages = null!;
    public Ctx ctx = null!;
    public double pw;
    public double ph;
    public List<(double x, double y, double w, double h, string name)> areas = null!;
    public int ai;
    public bool pageFresh;
    public string rootPath = null!;
    // The total page count is only known now: substitute it into any
    // xfa.layout.pageCount() placeholders emitted while painting masters.
    public string total = null!;
    public Document doc = null!;
    public XmlElement template = null!;
    public Func<string, string?>? rawValue = null;
    public double used;
    public XmlElement master = null!;
}
}

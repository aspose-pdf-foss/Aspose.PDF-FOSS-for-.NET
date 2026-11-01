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
    // The page area the container being flowed sends its overflow to (its <overflow
    // target>), null when it names none: the next page then follows the progression.
    public System.Xml.XmlElement? overflowMaster;
    // The subform that container repeats at the top of every area its content overflows
    // into, where it is placed (left indent, SOM path of its parent), and how much of
    // the current area such a leader took: the flow is at the area's top up to there.
    public System.Xml.XmlElement? overflowLeader;
    public double leaderIndent;
    public string leaderPath = string.Empty;
    public double areaStart;
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
    // Set when some field's layout-ready script asks for page numbers: those are resolved per page.
    public PageScripts? pageScripts;
    // The field placements of each page, by the page's item list, with the path the fields of its master go under.
    public Dictionary<List<Aspose.Pdf.Forms.Xfa.XfaRenderer.Item>, (List<XfaFieldPlacement> fields, string master)> placements = new();
    // What of each page's master is painted after the body (the boxes declared after the content area)
    // and after everything (the values of the master's fields), by the page's item list.
    public Dictionary<List<Aspose.Pdf.Forms.Xfa.XfaRenderer.Item>, (List<Aspose.Pdf.Forms.Xfa.XfaRenderer.Item> afterBody, List<Aspose.Pdf.Forms.Xfa.XfaRenderer.Item> values)> pageTails = new();
    // False when no widgets are wanted: the render then notes no placement at all.
    public bool placeFields;
    // The master page of each page and the context its body was painted in, by the page's item list: the masters
    // are painted once the pages are counted, so what a master shows on a page may depend on the page's number.
    public Dictionary<List<Aspose.Pdf.Forms.Xfa.XfaRenderer.Item>, (XmlElement master, Aspose.Pdf.Forms.Xfa.XfaRenderer.Ctx ctx)> pageMasters = new();
}
}

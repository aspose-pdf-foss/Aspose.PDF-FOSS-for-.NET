using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Converters;

internal static partial class SvgToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class SvgConvertState
{
    public System.Xml.XmlElement? svgRoot;
    // Page size rule: each dimension independently comes from
    // the root width/height attribute converted to points — unitless and px scale
    // by 0.75 (CSS 96dpi), pt×1, in×72, pc×12, cm×28.346, mm×2.8346, em/ex×1.
    // A missing, percentage, zero, or invalid attribute defaults that dimension
    // to 500pt. The viewBox NEVER influences the page size; it only defines the
    // user-space window that is scaled onto the page.
    public double width;
    public double height;
    public string viewBox = null!;
    public double vbMinX;
    public double vbMinY;
    public double vbW;
    public double vbH;
    public bool hasViewBox;
    // Page geometry from load-option PageInfo (all values CSS px ×0.75): an
    // explicit Width/Height replaces the content-derived dimension;
    // otherwise margins grow the page around the artwork. The artwork itself is
    // never scaled — it anchors to the left/top margin when one is set, else to
    // the right/bottom margin (which can push it off-page), else to the origin.
    public double pageW;
    public double pageH;
    public double offX;
    public double offY;
    public PageInfo? pi;
    // Create PDF document with one page matching SVG dimensions
    public Document doc = null!;
    public Page page = null!;
    public Ctx ctx = null!;
    public System.Text.StringBuilder sb = null!;
    public double[] ctm = null!;
    public Dictionary<string, string> rootStyle = null!;
    public XmlDocument xml = default!;
    public SvgLoadOptions? options = null;
    public string? baseDir = null;
    public bool imageMode = false;
}
}

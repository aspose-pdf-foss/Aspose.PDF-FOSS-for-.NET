using System.Globalization;
using System.Xml;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

internal static partial class XmlBinding
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class PageFragmentBuildState
{
    public string? id;
    public Aspose.Pdf.Text.TextFragment tf = null!;
    // Fragment-level styling: the <TextState> child of the fragment (also the
    // shape wrapping the segments — a template may nest <TextSegment> INSIDE the
    // state element), FontSize/HorizontalAlignment attributes on the
    // fragment itself, and the document DefaultTextState as the fallback.
    // A fragment-level <TextState> element REPLACES the document defaults
    // wholesale — unspecified properties fall back to the schema defaults
    // (10 pt, Helvetica, black, no leading), NOT to the DefaultTextState
    // (under a 9 pt / LineSpacing 4 document default,
    // colour-only fragment states render 10 pt bodies on a bare 10 pt pitch,
    // and the 20 pt title's leading blank line is 10 pt tall). A fragment
    // with NO TextState of its own takes the document defaults (e.g.
    // 12 pt + 4 leading bodies).
    public bool hasFragState;
    public XmlBinding.XmlTextStyle fragState = null!;
    public TabStops? tabStops;
    public MarginInfo? margin;
    public bool any;
    public int authoredSegments;
    public Document document = default!;
    public XmlNode fragNode = default!;
    public XmlDefaults defaults = default!;
    public string textPrefix = default!;
    public bool includeEmpty = false;
}
}

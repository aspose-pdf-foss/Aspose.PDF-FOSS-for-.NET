using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class MarginFragmentState
{
    // A fragment whose load options declare page margins lays
    // out in its own box INSIDE the page's content box: the
    // declared margins add to the page's. The first page takes
    // PageInfo.Margin, every generated page after it takes
    // AnyMargin. Vertical rhythm is the CSS half-leading model
    // on an integer-pixel "normal" line height, and a line's
    // ascent/descent is the max over the fragment's own font
    // (the block strut) and every run on the line.
    public double mfSize;
    public MarginInfo mfFirst = null!;
    public MarginInfo mfRest = null!;
    public double mfLeft;
    public double mfRight;
    public double mfWidth;
    public double mfBottom;
    public double mfTopFirst;
    public double mfTopRest;
    // The strut: the fragment's own face, mapped onto the
    // Standard-14 metric twin the renderer can draw with.
    public string mfStrut = null!;
    // one laid-out piece of a line
    public List<(List<(string Text, Aspose.Pdf.Converters.HtmlToPdfConverter.FlowRun Run, string Face, double X, double W)> Pieces, double Above, double Below)> mfLines = null!;
    // place: greedy fill while the line box stays inside the
    // bottom limit; a block moves whole unless two of its
    // lines still fit on the page
    public double mfY;
    public double mfPageTop;
    public Content.ContentStreamBuilder mfBuilder = null!;
    public bool mfDrew;
    public double mfPrevBelow;
    public int mfOnPage;
    public double mfEnd;
}
}

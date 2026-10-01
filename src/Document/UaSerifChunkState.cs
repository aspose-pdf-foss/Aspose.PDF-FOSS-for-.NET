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
private sealed class UaSerifChunkState
{
    public string uaBody = null!;
    public Content.ContentStreamBuilder uaB = null!;
    public string uaTimes = null!;
    public string uaTimesB = null!;
    public bool uaAfterHead;
    // at a line-box edge (chunk start, or after a blank <br> line)
    // the next baseline seats at the ascent drop, not a full pitch
    public bool uaAtBoxEdge;
    public string chunk = default!;
    public double uaWrapPt = 0;
    public HtmlFragment html = default!;
    public FlowLayout flow = default!;
    public double marginLeft = 0;
    public bool isHead;
    public string inner = null!;
    // inline runs: span colours and strong/b bold, inherited
    public List<(string T, Aspose.Pdf.Color? C, bool Bold, bool Styled, bool Lead, bool Trail)> uaRuns = null!;
    public Stack<(Aspose.Pdf.Color?, bool)> uaStack = null!;
    public Color? uaC;
    public bool uaStyled;
    public int uaBold;
    public int rp;
    // edge whitespace decides word seams between adjacent runs;
    // StripHtmlTags trims it, so read it off the raw slice
    public bool uaForceLead;
    public double uaFs;
    // wrap the run stream at the UA text width
    public List<List<(double X, string T, Aspose.Pdf.Color? C, bool Bold)>> uaLines = null!;
    public List<(double, string, Aspose.Pdf.Color?, bool)> uaCur = null!;
    public bool uaLineStyled;
    public double uaX;
    public bool uaPrevOpen;
    public bool firstOfElement;
}
}

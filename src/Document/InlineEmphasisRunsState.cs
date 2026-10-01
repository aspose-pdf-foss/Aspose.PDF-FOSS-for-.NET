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
private sealed class InlineEmphasisRunsState
{
    public byte[]? regTtf;
    public byte[] boldTtf = null!;
    public Aspose.Pdf.Text.FontData regData = null!;
    public Aspose.Pdf.Text.FontData boldData = null!;
    public System.Func<string, double> mReg = null!;
    public System.Func<string, double> mBold = null!;
    // Line-box metrics in px (pt = px * 0.75), em = 2048.
    public double sPx;
    public double em;
    public double faceAscent;
    public double faceDescent;
    public double faceLineGap;
    public double ascPx;
    public double descPx;
    public double lPx;
    public double halfLead;
    public double strutTop;
    public double strutBottom;
    public double above;
    public double below;
    public double firstBaselinePt;
    public double linePitchPt;
    // Tokenise the styled runs into word/space atoms for a greedy
    // wrap; a line break drops the space it breaks on.
    public List<(string text, bool bold, bool underline, bool space)> atoms = null!;
    public double contentW;
    public List<List<(string text, bool bold, bool underline, double x, double w)>> lines2 = null!;
    public List<(string, bool, bool, double, double)> cur = null!;
    public double curW;
    public double frameTop;
    public Aspose.Pdf.Core.PdfDictionary fontDict2 = null!;
    public Content.ContentStreamBuilder b2 = null!;
    // The block consumes its content extent without the outer
    // half-leadings: ascent + (n-1) pitches + descent.
    public double blockH;
    public string iface = default!;
    public double ipt = 0;
    public List<(string text, bool bold, bool underline, bool italic)> iruns = default!;
    public FlowLayout flow = default!;
    public Page page = default!;
    public double marginLeft = 0;
    public double marginRight = 0;
}
}

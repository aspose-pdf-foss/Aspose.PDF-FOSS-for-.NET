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
private sealed class UaSerifTableState
{
    public System.Text.RegularExpressions.Match twm = null!;
    public double tableW;
    public bool zeroPad;
    public double pad;
    // columns: declared pt widths from the colgroup, else
    // percentage widths off the first row's cells
    public List<double> colWs = null!;
    // faces: css-named family with real metrics, the
    // fallback face for glyphs the primary lacks, and the
    // UA serif default drawn as the base-14 Times
    public Dictionary<string, (byte[] Ttf, string Name, Aspose.Pdf.Text.GlyphOutlineParser Gp, Aspose.Pdf.Text.TrueTypeParser Tp)> faces = null!;
    public Aspose.Pdf.Core.PdfDictionary fontDict = null!;
    public string uaTimes2 = null!;
    public Content.ContentStreamBuilder tb = null!;
    public double topD;
    public double totalH;
    public string chunk = null!;
    public double uaBoxPt = 0;
    public FlowLayout flow = null!;
    public Page page = null!;
    public double marginLeft = 0;
}
}

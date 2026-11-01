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
private sealed class AlphaZeroRewriteState
{
    public Aspose.Pdf.Core.PdfDictionary? extGStates;
    public string text = null!;
    // The paint operators to replace, as ranges of the text with what goes in their place.
    public List<(int start, int end, string replacement)> edits = null!;
    public Stack<(bool fill0, bool stroke0)> stack = null!;
    public bool fill0;
    public bool stroke0;
    public string? lastName;
    public int pos;
    // an inline image ends the rewrite with the original bytes kept
    public bool bailOut;
}
}

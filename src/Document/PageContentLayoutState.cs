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
private sealed class PageContentLayoutState
{
    public Document.PageLayoutState pl = null!;
    public Page page = default!;
    public PageContentState pc = default!;
    // The walk over pl.paraList: an inline chain consumes several paragraphs at
    // once, so a stage can move the cursor past the members it laid out.
    public int paraIdx;
}
}

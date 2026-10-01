using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfContentEditor
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class StampBlocksState
{
    public List<Aspose.Pdf.Facades.PdfContentEditor.QBlock> blocks = null!;
    public List<Aspose.Pdf.Facades.PdfContentEditor.StampBlock> result = null!;
    public Aspose.Pdf.Core.PdfDictionary? resources;
    public Aspose.Pdf.Core.PdfDictionary? xobjects;
    public string content = default!;
    public Page page = default!;
    public Document doc = default!;
}
}

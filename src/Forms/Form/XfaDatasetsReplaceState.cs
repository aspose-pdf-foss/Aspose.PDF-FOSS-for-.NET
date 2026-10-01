using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

public sealed partial class Form
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class XfaDatasetsReplaceState
{
    // No "datasets" part in the XFA array
    public IO.PdfReader? rdr;
    public Aspose.Pdf.Core.PdfDictionary? acroForm;
    public Aspose.Pdf.Core.PdfObject? xfaObj;
    public XmlDocument importedXml = default!;
}
}

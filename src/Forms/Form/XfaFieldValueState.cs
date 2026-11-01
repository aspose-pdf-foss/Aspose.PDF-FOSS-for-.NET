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
private sealed class XfaFieldValueState
{
    // Fallback: single-stream XFA
    public IO.PdfReader? reader;
    public string? result;
    public string path = default!;
    public bool strict = false;
    public XmlDocument? datasets;   // the datasets, parsed at the first lookup of this read
    public bool leafFallback;       // the last lookup matched the leaf name anywhere, not the path
}
}

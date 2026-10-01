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
private sealed class XfaNodeFindState
{
    public string[] parts = null!;
    public bool abandoned;   // a strict walk met a part with no match: the lookup yields nothing
    public XmlNode? current;
    // Try strict path walk first
    public System.Xml.XmlNode? root;
    public XmlDocument doc = default!;
    public string path = default!;
    public bool strict = false;
}
}

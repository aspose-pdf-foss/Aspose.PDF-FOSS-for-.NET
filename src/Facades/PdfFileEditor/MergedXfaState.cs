using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfFileEditor
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class MergedXfaState
{
    public List<(System.Xml.XmlDocument? tpl, System.Xml.XmlDocument? ds)> parts = null!;
    public int withTemplate;
    // ── Merged template ──
    public XmlDocument? mergedTpl;
    public XmlElement? tplRootSub;
    public Dictionary<int, Dictionary<string, string>> renameByInput = null!;
    public Dictionary<string, string> firstXmlByName = null!;
    public Dictionary<string, int> dupCount = null!;
    public string mergedTemplateXml = null!;
    // ── Merged datasets ──
    public XmlDocument? mergedDs;
    public XmlElement? dsRootEl;
    public XmlElement? dataEl;
    public string? mergedDatasetsXml;
    // ── Emit /XFA array ──
    public Aspose.Pdf.Core.PdfArray arr = null!;
    public List<PdfReader> readers = default!;
}
}

using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

public sealed partial class XmpMetadata
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class XmpBytesState
{
    public System.Text.StringBuilder sb = null!;
    // Collect used namespace prefixes — from flat properties and from every
    // (recursively nested) key inside the structured properties, so that the
    // structured RDF emitted below has every prefix declared (an undeclared
    // prefix makes the reload-time XDocument.Parse throw and silently drop the
    // whole structured block).
    public HashSet<string> usedPrefixes = null!;
}
}

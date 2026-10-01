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
private sealed class XmpParseState
{
    // Simple regex-based XMP parser for common properties
    // Handles <ns:Property>value</ns:Property> patterns (including empty values)
    public System.Text.RegularExpressions.MatchCollection matches = null!;
    // Also handle <rdf:li> lists inside properties (e.g., dc:creator)
    public System.Text.RegularExpressions.MatchCollection listMatches = null!;
    // Structured (named-value) properties: a property element wrapping a
    // nested <rdf:Description> of child fields, e.g.
    //   <custprops:Property1><rdf:Description>
    //     <custprops:Name>TestProperty</custprops:Name>
    //     <custprops:Value>TestValue</custprops:Value>
    //   </rdf:Description></custprops:Property1>
    // Parsed into an XmpValue holding the ordered (childKey -> value) pairs.
    public System.Text.RegularExpressions.Regex structPattern = null!;
    public System.Text.RegularExpressions.Regex fieldPattern = null!;
    // PDF/A extension-schema descriptions: each <rdf:li> inside
    // <pdfaExtension:schemas> carries pdfaSchema:prefix / namespaceURI / schema
    // (the description). Recover them so ExtensionFields survives a reload.
    public System.Text.RegularExpressions.Regex liPattern = null!;
    public string xml = default!;
}
}

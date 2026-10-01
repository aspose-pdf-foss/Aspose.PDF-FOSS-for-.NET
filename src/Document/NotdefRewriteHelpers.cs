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
// The notdef-rewrite helpers: a font's encoding names (cached), and the operand-start and operator-end bookkeeping of the content scan.
    private string?[]? EncodingFor(NotdefRewriteState nd, string fontName)
    {
        if (nd.encodings.TryGetValue(fontName, out var cached)) return cached;
        string?[]? names = null;
        if (_reader.ResolveDict(nd.fonts.Get(fontName)) is { } fontDict
            && fontDict.GetName("Subtype") is "Type1" or "MMType1")
            names = Devices.SoftwarePageRenderer.ResolveEncoding(fontDict, _reader);
        nd.encodings[fontName] = names;
        return names;
    }

    private static void BeginOperand(NotdefRewriteState nd, int at) { if (nd.operandStart < 0) nd.operandStart = at; }

    private static void EndOperator(NotdefRewriteState nd) { nd.operandStart = -1; nd.strings.Clear(); }
}

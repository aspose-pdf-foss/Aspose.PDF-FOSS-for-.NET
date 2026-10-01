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
private sealed class FontPruneState
{
    // Collect the fonts a `Tf` selects that actually SHOW text, and the form
    // XObjects a `Do` invokes. A font selected only by an empty run (`/F Tf`
    // followed by `[] TJ` with no glyphs, then another `Tf`) is not really used —
    // counting it would keep an orphan font after a full RemoveUnusedFonts replace.
    public HashSet<string> usedFonts = null!;
    public List<string> formNames = null!;
    public IO.PdfLexer lexer = null!;
    public string? lastName;
    public string? currentFont;
    public bool sawGlyphs;
    public byte[]? rewritten;
    public Aspose.Pdf.Core.PdfDictionary? fontDict;
    public Aspose.Pdf.Core.PdfDictionary? xobjects;
    public byte[]? content = null;
    public PdfDictionary? resources = null;
    public HashSet<PdfDictionary> visitedForms = default!;
}
}

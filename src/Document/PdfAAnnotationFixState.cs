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
private sealed class PdfAAnnotationFixState
{
    public Aspose.Pdf.Core.PdfObject? annotsObj;
    // PDF/A-1 prohibits the PDF 1.5+ annotation subtypes that later parts allow.
    public bool isPdfA1;
    public List<int> indicesToRemove = null!;
    public Page page = default!;
    public PdfFormatConversionOptions options = default!;
    public bool fix = false;
    public bool strip = false;
    public Aspose.Pdf.Core.PdfDictionary? annotDict;
    public string? subtype;
    // PDF/A-4f (ISO 19005-4) permits embedded files, so FileAttachment
    // annotations stay as authored — no violation, no stripping.
    public bool allowedByPart;
    // Check/remove prohibited actions on annotations. PDF/X (ISO 15930)
    // prohibits interactive behaviour outright — EVERY annotation /A is a
    // violation there, whatever its type; PDF/A prohibits only the
    // executable/media set.
    public bool isPdfXTarget;
    public Aspose.Pdf.Core.PdfDictionary? actionObj;
    // Check/remove AA on annotations
    public Aspose.Pdf.Core.PdfDictionary? annotAa;
}
}

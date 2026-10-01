using System.Collections;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

public sealed partial class PageCollection
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class FormFieldImportState
{
    public Aspose.Pdf.Core.PdfObject? annotsObj;
    public Aspose.Pdf.Core.PdfArray annots = null!;
    // Also resolve source page's annotations for name lookup
    public Aspose.Pdf.Core.PdfObject? srcAnnotsObj;
    public Aspose.Pdf.Core.PdfArray? srcAnnots;
    // Ensure AcroForm exists in the catalog
    public Aspose.Pdf.Core.PdfDictionary catalog = null!;
    public Aspose.Pdf.Core.PdfDictionary acroForm = null!;
    public Aspose.Pdf.Core.PdfArray fieldsArr = null!;
    // Collect existing field names for deduplication
    public HashSet<string> existingNames = null!;
    public Dictionary<(Aspose.Pdf.IO.PdfReader reader, int parentObjNum), (Aspose.Pdf.Core.PdfIndirectRef fieldRef, Aspose.Pdf.Core.PdfDictionary fieldDict, int fieldsIndex, bool promoted)> owners = null!;
    // A distinct /Annots array for the inserted page, materialised lazily when a
    // same-document widget slot must be re-pointed at a synthesized kid (so the
    // source page's shared /Annots array is left untouched).
    public PdfArray? distinctAnnots;
    public PdfDictionary clonedPageDict = default!;
    public PdfDictionary sourcePageDict = default!;
    public PdfReader sourceReader = default!;
    public Aspose.Pdf.Core.PdfObject annotObj = null!;
    public Aspose.Pdf.Core.PdfDictionary annotDict = null!;
    // Check if this is a Widget annotation
    public string? subtype;
    // Get the field name from the cloned dict
    public string partialName = null!;
    // A later widget of a FOREIGN field this import already materialised (the
    // same source /Parent - a multi-widget field repeated on one page or spread
    // over several imported pages) joins that field as a kid: no /T of its
    // own, /Parent to the imported field, linked into its /Kids. Renaming it
    // into a field of its own would grow Form.Count by one per widget.
    // Only a /T-less source widget is a widget OF its parent; a kid carrying its
    // own /T is a named child field and keeps its own identity.
    public int? srcParentNum;
}
}

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
private sealed class FieldAddState
{
    public IO.PdfReader? reader;
    // Copy the source field dict (except /Kids, rebuilt below) and rename it.
    public Aspose.Pdf.Core.PdfDictionary newDict = null!;
    // Rebuild /Kids as independent copies of each source widget so that
    // editing a copied widget's /Rect does not mutate the original.
    public Aspose.Pdf.Core.PdfArray? srcKids;
    public Aspose.Pdf.Core.PdfArray newKids = null!;
    public List<Aspose.Pdf.Core.PdfDictionary> copiedWidgets = null!;
    public Aspose.Pdf.Forms.Field newField = null!;
    // Register in the AcroForm /Fields array.
    public Aspose.Pdf.Core.PdfDictionary catalog = null!;
    public Aspose.Pdf.Core.PdfDictionary? acroForm;
    public Aspose.Pdf.Core.PdfArray? fieldsArray;
    // Bind the widget(s) to the target page's /Annots.
    public PageCollection pages = null!;
    public PdfDictionary? pageDict;
    // Mark dirty so incremental save persists the new field/annots.
    public Document? doc;
    public Field field = default!;
    public string partialName = default!;
    public int pageNumber = 0;
}
}

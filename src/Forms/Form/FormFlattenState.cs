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
private sealed class FormFlattenState
{
    // 1. Force each field's appearance to reflect the current value.
    //    The per-type GenerateAppearance short-circuits when /AP is present,
    //    so for text/choice/check/button/radio fields we delete the existing
    //    /AP and re-emit — that's the only way to capture an updated value.
    public bool refresh;
    // 2. Hoist /AcroForm/DR fonts into each page's /Resources so the
    //    appearance Form-XObject's content stream (which references fonts by
    //    AcroForm-DR alias like /Helv) still resolves after step 4 strips
    //    the /AcroForm dict. /AP streams without their own /Resources
    //    fall back to the page's resources per PDF 32000-2 § 8.10.
    public Aspose.Pdf.Core.PdfDictionary? acroForm;
    public Aspose.Pdf.Core.PdfDictionary? drFonts;
    public bool hideButtons;
    // The PDF/A flatten stamps only widgets REACHABLE from the AcroForm /Fields
    // tree (field dicts + their /Kids). An orphan widget annotation (left behind
    // by a merge that deduplicated its field entry) gets no page-content fragment
    // — PDF/A output does not stamp orphan widgets. The same set also
    // dedupes a widget dict shared by several pages' /Annots.
    public System.Collections.Generic.HashSet<PdfDictionary>? fieldWidgets;
    public Document document = default!;
    public FlattenSettings? settings = null;
    public int frmStartIndex = 0;
    public bool flattenNonWidgets = false;
    public bool skipInvisible = false;
    public bool keepAcroFormDict = false;
}
}

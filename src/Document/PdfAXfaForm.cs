using Aspose.Pdf.Core;
using Aspose.Pdf.Optimization;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>ISO 19005-2 clause 6.4.2: the interactive form has no XFA entry and the catalog
    /// no NeedsRendering. A conversion that deletes what is prohibited turns the form into a
    /// standard one first - a dynamic form is laid out onto pages of its own, because the pages
    /// it came with only hold the notice a viewer shows until it has rendered the form - and
    /// then the two entries are gone. The pages are replaced before anything else is converted,
    /// so every later step works on the content the output keeps.</summary>
    private void ReplaceXfaFormForPdfA(PdfAConvertState pa)
    {
        if (_reader.ResolveDict(_reader.Catalog.Get("AcroForm"))?.Get("XFA") is null) return;
        pa.options.ConversionLog.Add(new PdfAViolation
        {
            Rule = "XfaForm",
            Clause = pa.part == "1" ? null : "6.4.2",
            Description = "The interactive form dictionary contains the XFA key",
        });
        if (pa.strip) Form.ConvertXfaToStandardWithoutWidgets();
    }
}

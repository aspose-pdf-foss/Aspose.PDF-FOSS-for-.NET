using System.Collections;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

public sealed partial class PageCollection
{
    /// <summary>
    /// After importing a page, scan its /Annots for Widget annotations and register
    /// them in the target document's AcroForm /Fields array. Handles duplicate field
    /// names by appending numeric suffixes.
    /// </summary>
    private void ImportFormFieldsFromPage(PdfDictionary clonedPageDict,
        PdfDictionary sourcePageDict, PdfReader sourceReader)
    {
        var im = new FormFieldImportState();
        im.clonedPageDict = clonedPageDict;
        im.sourcePageDict = sourcePageDict;
        im.sourceReader = sourceReader;
        if (OwnerDocument is null) return;

        im.annotsObj = im.clonedPageDict.Get("Annots");
        var annots = im.annotsObj as PdfArray ?? ResolveImportedObject(im.annotsObj) as PdfArray;
        if (annots is null) return;
        im.annots = annots;

        im.srcAnnotsObj = im.sourceReader.Resolve(im.sourcePageDict.Get("Annots"));
        im.srcAnnots = im.srcAnnotsObj as PdfArray;

        im.catalog = _reader.Catalog;
        var acroForm = _reader.ResolveDict(im.catalog.Get("AcroForm"));
        if (acroForm is null)
        {
            acroForm = new PdfDictionary();
            im.catalog.Set("AcroForm", acroForm);
        }
        im.acroForm = acroForm;

        var fieldsArr = _reader.Resolve(im.acroForm.Get("Fields")) as PdfArray;
        if (fieldsArr is null)
        {
            fieldsArr = new PdfArray();
            im.acroForm.Set("Fields", fieldsArr);
        }
        im.fieldsArr = fieldsArr;

        // Carry the source AcroForm's default resources across the merge: imported
        // widgets' /DA strings name fonts by /DR alias (e.g. /HeBo), so the
        // destination AcroForm needs those /DR entries (and a default /DA) or the
        // aliases dangle — appearance regeneration and DefaultResources readers
        // would come up empty after the merge.
        ImportCrossReaderFieldTree(im);

        im.existingNames = new HashSet<string>();
        CollectFieldNames(im.fieldsArr, im.existingNames);

        im.owners = _importedFieldOwners
            ?? new Dictionary<(PdfReader, int), (PdfIndirectRef, PdfDictionary, int, bool)>();

        im.distinctAnnots = null;

        for (int i = 0; i < im.annots.Count; i++)
        {
            if (!ImportFormFieldAnnotation(im, i)) break;
        }

        // Register imported objects with the reader so they can be resolved in-memory
        foreach (var (objNum, obj) in _importedObjects)
            _reader.RegisterOverlayObject(objNum, obj);

        // Invalidate the cached Form object so it re-reads from the updated AcroForm
        OwnerDocument.InvalidateForm();
    }
}

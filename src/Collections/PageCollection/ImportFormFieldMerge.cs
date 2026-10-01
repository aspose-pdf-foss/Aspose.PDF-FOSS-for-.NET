using System.Collections;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

public sealed partial class PageCollection
{
    /// <summary>Page form-field import: a widget whose field name already exists joins that field as a kid.</summary>
    private bool MergeWidgetIntoExistingField(FormFieldImportState im, int i)
    {
        if (im.existingNames.Contains(im.partialName))
        {
            // Same-document page copy/insert: the inserted page's /Annots still
            // points at the SOURCE field's own widget dict (the shallow page clone
            // shares it). Instead of renaming it into a separate field, give the
            // existing field a fresh DISTINCT kid widget for the new page and link
            // it into /Kids — so Field.Count grows by one and the original field
            // keeps its name (FindByName still resolves it).
            if (im.sourceReader == _reader
                && OwnerDocument!.FindObjectNumber(im.annotDict) is int fieldObjNum && fieldObjNum >= 0)
            {
                var kid = new PdfDictionary();
                kid.Set("Type", new PdfName("Annot"));
                kid.Set("Subtype", new PdfName("Widget"));
                foreach (var vk in new[] { "Rect", "AP", "MK", "DA", "BS", "Border", "F", "Q", "H", "AS", "DV" })
                    if (im.annotDict.Get(vk) is { } vv) kid.Set(vk, vv);
                kid.Set("Parent", new PdfIndirectRef(fieldObjNum, 0));

                var kidObjNum = ImportObjNumBase();
                _importedObjects.Add((kidObjNum, kid));

                // Promote the merged leaf to "merged-self + one kid": the field keeps
                // its own /Rect+/AP on the source page and gains the new page's kid.
                var kids = _reader.Resolve(im.annotDict.Get("Kids")) as PdfArray;
                if (kids is null) { kids = new PdfArray(); im.annotDict.Set("Kids", kids); }
                kids.Add(new PdfIndirectRef(kidObjNum, 0));

                // Re-point the inserted page's /Annots slot at the fresh kid, on a
                // distinct array so the source page's /Annots is not mutated.
                im.distinctAnnots ??= CloneAnnotArray(im.annots, im.clonedPageDict);
                im.distinctAnnots.ReplaceAt(i, new PdfIndirectRef(kidObjNum, 0));
                return true;
            }

            // Check if this is a same-source duplicate (same reader = same document)
            // vs a different-source field needing rename. Only a SAME-READER
            // source can claim the multi-widget exemption — a widget imported
            // from another document that happens to carry a /Parent is a
            // FOREIGN field colliding by name, and keeping it unrenamed leaves
            // two fields with one /T in the merged form.
            bool isSameSourceDuplicate = false;
            if (im.sourceReader == _reader && im.srcAnnots is not null && i < im.srcAnnots.Count)
            {
                var srcAnnotDict = im.sourceReader.ResolveDict(im.srcAnnots[i]);
                if (srcAnnotDict?.Get("Parent") is PdfIndirectRef parentRef)
                {
                    // This Widget shares a parent field — it's a multi-widget field
                    // within the same document. Skip the duplicate.
                    isSameSourceDuplicate = true;
                }
            }

            if (isSameSourceDuplicate)
                return true;

            // Different source — rename the field
            var newName = DeduplicateFieldName(im.partialName, im.existingNames);
            im.annotDict.Set("T", new PdfString(Compat.Latin1.GetBytes(newName)));
            im.partialName = newName;
        }
        return false;
    }
}

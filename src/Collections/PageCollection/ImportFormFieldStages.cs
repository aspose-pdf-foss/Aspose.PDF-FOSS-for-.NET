using System.Collections;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

public sealed partial class PageCollection
{
    /// <summary>Page form-field import: one widget annotation of the source page imported into the acroform.</summary>
    private bool ImportFormFieldAnnotation(FormFieldImportState im, int i)
    {
        im.annotObj = im.annots[i];
        var annotDict = ResolveForImport(im.annotObj);
        if (annotDict is null) return true;
        im.annotDict = annotDict;

        im.subtype = im.annotDict.GetName("Subtype");
        if (im.subtype != "Widget") return true;

        var partialName = GetFieldPartialName(im.annotDict);

        // If no /T in clone (Parent was stripped), look up from source
        if (string.IsNullOrEmpty(partialName) && im.srcAnnots is not null && i < im.srcAnnots.Count)
        {
            var srcAnnotDict = im.sourceReader.ResolveDict(im.srcAnnots[i]);
            if (srcAnnotDict is not null)
                partialName = GetFullFieldName(srcAnnotDict, im.sourceReader);
        }

        if (string.IsNullOrEmpty(partialName)) return true;
        im.partialName = partialName;

        im.srcParentNum = null;
        if (im.sourceReader != _reader && im.srcAnnots is not null && i < im.srcAnnots.Count
            && im.sourceReader.ResolveDict(im.srcAnnots[i]) is { } srcWidget
            && srcWidget.Get("T") is null
            && srcWidget.Get("Parent") is PdfIndirectRef srcParentRef)
            im.srcParentNum = srcParentRef.ObjectNumber;
        if (im.srcParentNum is int parentNum
            && im.owners.TryGetValue((im.sourceReader, parentNum), out var owner)
            && im.annotObj is PdfIndirectRef kidRef)
        {
            // The field imported first was a merged field-widget; the moment a
            // second widget arrives it becomes a PURE field whose kids are ALL its
            // widgets (the first one included) - a radio group's Value setter walks
            // /Kids for the option state, so the first widget must be a kid too.
            if (!owner.promoted)
            {
                owner = PromoteImportedWidgetToField(owner, im.fieldsArr);
                im.owners[(im.sourceReader, parentNum)] = owner;
            }
            im.annotDict.Remove("T");
            im.annotDict.Set("Parent", owner.fieldRef);
            ((PdfArray)owner.fieldDict.Get("Kids")!).Add(kidRef);
            return true;
        }

        // Set the name on the cloned Widget so it becomes a standalone field
        im.annotDict.Set("T", new PdfString(Compat.Latin1.GetBytes(im.partialName)));

        // For same-source multi-widget fields: skip if same name already exists
        // (same field appearing on multiple pages within one document)
        if (MergeWidgetIntoExistingField(im, i)) return true;
        im.existingNames.Add(im.partialName);

        // Copy field properties from parent chain if missing on the Widget
        if (im.srcAnnots is not null && i < im.srcAnnots.Count)
        {
            var srcAnnotDict = im.sourceReader.ResolveDict(im.srcAnnots[i]);
            if (srcAnnotDict is not null)
                CopyFieldPropertiesFromParent(srcAnnotDict, im.sourceReader, im.annotDict);
        }

        // Add to AcroForm /Fields
        im.fieldsArr.Add(im.annotObj!);
        if (im.srcParentNum is int ownedParentNum && im.annotObj is PdfIndirectRef fieldRef)
            im.owners[(im.sourceReader, ownedParentNum)] = (fieldRef, im.annotDict, im.fieldsArr.Count - 1, false);
        return true;
    }

    /// <summary>Page form-field import: a source field tree from another reader cloned into this document.</summary>
    private void ImportCrossReaderFieldTree(FormFieldImportState im)
    {
        if (im.sourceReader != _reader
            && im.sourceReader.ResolveDict(im.sourceReader.Catalog.Get("AcroForm")) is { } srcAcro)
        {
            if (im.acroForm.Get("DA") is null && srcAcro.Get("DA") is PdfString srcDa)
                im.acroForm.Set("DA", srcDa);
            if (im.sourceReader.ResolveDict(srcAcro.Get("DR")) is { } srcDr)
            {
                var remapDr = GetOrCreateCloneCache(im.sourceReader);
                var destDr = _reader.ResolveDict(im.acroForm.Get("DR"));
                if (destDr is null)
                {
                    im.acroForm.Set("DR", RemapObject(srcDr, im.sourceReader, remapDr));
                }
                else if (im.sourceReader.ResolveDict(srcDr.Get("Font")) is { } srcDrFonts)
                {
                    // Merge font aliases that don't collide with existing ones.
                    var destFonts = _reader.ResolveDict(destDr.Get("Font"));
                    if (destFonts is null)
                    {
                        destFonts = new PdfDictionary();
                        destDr.Set("Font", destFonts);
                    }
                    foreach (var alias in srcDrFonts.Keys)
                    {
                        if (destFonts.ContainsKey(alias)) continue;
                        var entry = srcDrFonts.Get(alias);
                        if (entry is not null)
                            destFonts.Set(alias, RemapObject(entry, im.sourceReader, remapDr));
                    }
                }
            }
        }
    }
}

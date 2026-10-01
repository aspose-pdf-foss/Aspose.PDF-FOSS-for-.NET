using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

public sealed partial class Form : ICollection<Aspose.Pdf.Annotations.WidgetAnnotation>
{
    /// <summary>The stages of the field add: the source kids copy, the page placement and the document registration.</summary>
    private void RegisterAddedField(FieldAddState fa)
    {
        var acroFormObjNum = fa.doc!.FindObjectNumber(fa.acroForm!);
        if (acroFormObjNum > 0)
            fa.doc.MarkDirty(acroFormObjNum, fa.acroForm!);
        if (fa.pageDict is not null)
        {
            var pageObjNum = fa.doc.FindObjectNumber(fa.pageDict);
            if (pageObjNum > 0)
                fa.doc.MarkDirty(pageObjNum, fa.pageDict);
        }
    }

    /// <summary>The stages of the field add: the source kids copy, the page placement and the document registration.</summary>
    private void PlaceAddedFieldOnPage(FieldAddState fa)
    {
        fa.pageDict = fa.pages[fa.pageNumber].Dict;
        var annots = fa.pageDict.Get("Annots") as PdfArray;
        if (annots is null)
        {
            annots = new PdfArray();
            fa.pageDict.Set("Annots", annots);
        }

        if (fa.copiedWidgets.Count > 0)
        {
            foreach (var widget in fa.copiedWidgets)
            {
                widget.Set("P", fa.pageDict);
                widget.Set("Parent", fa.newDict);
                annots.Add(widget);
            }
        }
        else
        {
            // Single-widget field merged into the field dict.
            fa.newDict.Set("P", fa.pageDict);
            annots.Add(fa.newDict);
        }
    }

    /// <summary>The stages of the field add: the source kids copy, the page placement and the document registration.</summary>
    private void CopyAddedFieldKids(FieldAddState fa)
    {
        foreach (var k in fa.srcKids!)
        {
            if (fa.reader!.Resolve(k) is not PdfDictionary srcKid) continue;
            var newKid = new PdfDictionary();
            foreach (var kk in srcKid.Keys)
            {
                var kv = srcKid.Get(kk);
                if (kv is not null) newKid.Set(kk, kv);
            }
            fa.newKids.Add(newKid);
            fa.copiedWidgets.Add(newKid);
        }
    }
}

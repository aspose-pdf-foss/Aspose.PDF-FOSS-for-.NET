using Aspose.Pdf.Core;

namespace Aspose.Pdf.Optimization;

internal static partial class PdfAValidator
{
    /// <summary>
    /// PDF/UA-1 §7.18.5: a link annotation must carry an alternate description in its
    /// /Contents entry. The visible link text says where the link goes to a reader who
    /// can see the page; assistive technology reads /Contents instead, and a link
    /// without one announces nothing.
    /// </summary>
    private static void CheckUaLinkAnnotations(Document document, Page page, UaReport report)
    {
        var reader = document.Reader;
        if (reader.Resolve(page.Dict.Get("Annots")) is not PdfArray annots) return;

        foreach (var entry in annots)
        {
            var annot = reader.ResolveDict(entry);
            if (annot is null || annot.GetName("Subtype") != "Link") continue;
            var contents = reader.Resolve(annot.Get("Contents"));
            if (contents is PdfString { } s && s.ToText().Length > 0) continue;
            var objectNumber = (entry as PdfIndirectRef)?.ObjectNumber ?? 0;
            report.Add(UaProblems.LinkAnnotationMissingContents, "UaLinkAnnotation",
                "'Link' annotation is missing an alternate description under a 'Content' key",
                page.Number, objectNumber == 0 ? null : objectNumber.ToString());
        }
    }
}

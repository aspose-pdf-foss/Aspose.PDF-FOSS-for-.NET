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
    /// <summary>The stages of the form flatten: the appearance refresh, the invisible-widget skip, one page's annotations, and the AcroForm prune.</summary>
    private void PruneFlattenedAcroFormFields(FormFlattenState fl)
    {
        foreach (var af in new[] { fl.acroForm, _acroForm })
        {
            if (af is null || !af.ContainsKey("Fields")) continue;
            var keptFields = new PdfArray();
            if (fl.keepAcroFormDict && fl.document.Reader.Resolve(af.Get("Fields")) is PdfArray oldFields)
            {
                foreach (var fRef in oldFields)
                {
                    var fd = fl.document.Reader.ResolveDict(fRef);
                    if (fd?.GetName("FT") == "Sig" && HasVisibleWidget(fd, fl.document.Reader))
                        keptFields.Add(fRef);
                }
            }
            af.Set("Fields", keptFields);
        }
    }

    /// <summary></summary>
    private bool FlattenPageAnnotations(FormFlattenState fl, Page page)
    {
        // The facade flatten (flattenNonWidgets) consumes the page's WHOLE
        // /Annots — probed: a sticky note and a highlight leave with the
        // fields — so annotations carrying no /AP first get the appearance a
        // viewer would synthesise (the same materialisation the save pass
        // runs), and whatever still has none is dropped rather than kept.
        if (fl.flattenNonWidgets)
            foreach (var ann in page.Annotations)
            {
                if (fl.document.Reader.ResolveDict(ann.Dict.Get("AP")) is not null) continue;
                var st = ann.Dict.GetName("Subtype");
                if (st is "Widget" or "Popup" or "Link" or null) continue;
                // A hidden annotation (/F bit 2) leaves without ink.
                if (((int)ann.Dict.GetInt("F") & 2) != 0) continue;
                try
                {
                    // The synthesised set, op-measured: FreeText
                    // writes its /DA text; a highlight fills its quad boxes under
                    // its /CA; a strikeout is one rect-mid line; notes, shapes,
                    // ink and stamps draw themselves. An UNDERLINE or SQUIGGLY
                    // with no /AP draws NOTHING (both are dropped, with
                    // or without quads), and so do carets and file attachments.
                    if (ann is Aspose.Pdf.Annotations.FreeTextAnnotation freeText)
                        freeText.GenerateAppearance();
                    else if (ann is Aspose.Pdf.Annotations.LineAnnotation
                                 or Aspose.Pdf.Annotations.PolygonAnnotation
                                 or Aspose.Pdf.Annotations.PolylineAnnotation
                                 or Aspose.Pdf.Annotations.SquareAnnotation
                                 or Aspose.Pdf.Annotations.CircleAnnotation
                                 or Aspose.Pdf.Annotations.TextAnnotation
                                 or Aspose.Pdf.Annotations.InkAnnotation
                                 or Aspose.Pdf.Annotations.HighlightAnnotation
                                 or Aspose.Pdf.Annotations.StrikeOutAnnotation
                                 or Aspose.Pdf.Annotations.StampAnnotation)
                        ann.UpdateAppearances();
                }
                catch { /* an unsynthesisable appearance leaves the annotation to drop below */ }
            }
        FlattenFieldsOnPage(page, fl.hideButtons, fl.frmStartIndex, fl.flattenNonWidgets, fl.skipInvisible,
            fl.fieldWidgets, dropUnstamped: fl.flattenNonWidgets);
        return true;
    }
}

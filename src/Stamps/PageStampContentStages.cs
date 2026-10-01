using System.Collections.Generic;
using System.Globalization;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

public sealed partial class PdfPageStamp : Stamp
{
    /// <summary>Places the form: the horizontal and vertical alignment resolve the origin, the zoomed scale and the target page's rotation give the cm matrix.</summary>
    private void ComputeStampPlacement(PageStampContentState pg)
    {
        pg.x = HorizontalAlignment switch
        {
            HorizontalAlignment.Center => (pg.targetPage.Width - Width * ZoomX) / 2 + LeftMargin - RightMargin,
            HorizontalAlignment.Right => pg.targetPage.Width - Width * ZoomX - RightMargin - XIndent,
            HorizontalAlignment.Left => XIndent != 0 ? XIndent : LeftMargin,
            _ => XIndent,
        };
        pg.y = VerticalAlignment switch
        {
            VerticalAlignment.Top => pg.targetPage.Height - Height * ZoomY - TopMargin - YIndent,
            VerticalAlignment.Center => (pg.targetPage.Height - Height * ZoomY) / 2 + BottomMargin - TopMargin,
            VerticalAlignment.Bottom => YIndent != 0 ? YIndent : BottomMargin,
            _ => YIndent,
        };


        pg.rot = ((pg.targetPage.RotateDegrees % 360) + 360) % 360;
        if (pg.rot == 90)
        {
            var tmb = pg.targetPage.MediaBox;
            pg.matrix = $"0 {Fmt(pg.sx)} {Fmt(-pg.sy)} 0 {Fmt(tmb.Width - pg.y)} {Fmt(pg.x)}";
        }
        else
        {
            // Indents are measured from the TARGET page's box origin (a page whose
            // MediaBox lower-left is not (0,0) still stamps at its visible corner),
            // and the SOURCE page's own box origin maps to the placement point.
            var tmb = pg.targetPage.MediaBox;
            pg.matrix = $"{Fmt(pg.sx)} 0 0 {Fmt(pg.sy)} {Fmt(tmb.LLX + pg.x - pg.mb.LLX * pg.sx)} {Fmt(tmb.LLY + pg.y - pg.mb.LLY * pg.sy)}";
        }
    }

    /// <summary>Under font promotion, copies the form's fonts into the target page's resources under their names.</summary>
    private void PromoteFormFonts(PageStampContentState pg)
    {
        if (PromoteFontsToPage && _promotedFonts is { Count: > 0 })
        {
            var pageFonts = pg.targetReader.ResolveDict(pg.targetResources.Get("Font"))
                ?? pg.targetResources.Get("Font") as PdfDictionary;
            if (pageFonts is null)
            {
                pageFonts = new PdfDictionary();
                pg.targetResources.Set("Font", pageFonts);
            }
            foreach (var (name, font) in _promotedFonts)
                if (!pageFonts.ContainsKey(name))
                    pageFonts.Set(name, font);
        }
    }

    /// <summary>Builds the source page as a form XObject: its BBox, its resources imported into the target document (fonts pulled out under font promotion), the content stream, registered once per target document.</summary>
    private void ImportSourceForm(PageStampContentState pg)
    {
        var formDict = new PdfDictionary();
        formDict.Set("Type", new PdfName("XObject"));
        formDict.Set("Subtype", new PdfName("Form"));

        var bbox = new PdfArray();
        bbox.Add(new PdfReal(pg.mb.LLX - BBoxOutsetPt));
        bbox.Add(new PdfReal(pg.mb.LLY - BBoxOutsetPt));
        bbox.Add(new PdfReal(pg.mb.URX + BBoxOutsetPt));
        bbox.Add(new PdfReal(pg.mb.URY + BBoxOutsetPt));
        formDict.Set("BBox", bbox);

        // Import the source page's resources into the TARGET document. The source
        // /Resources dictionary holds indirect references into the source document's
        // object table (fonts, ICC colour spaces, images, ExtGStates); copying them
        // verbatim would leave dangling references in the target. ImportDict resolves
        // the whole object graph against the source reader and re-registers it with
        // fresh object numbers in the target so the form is self-contained.
        var srcResources = ResolveEffectiveResources(pg.sourcePage.Dict, pg.sourceReader);
        if (srcResources is not null && pg.targetDoc is not null)
            formDict.Set("Resources", pg.targetDoc.ImportDict(srcResources, pg.sourceReader,
                pg.targetDoc.GetSharedImportCloneMap(pg.sourceReader)));
        else if (srcResources is not null)
            formDict.Set("Resources", srcResources);

        // Hoist the imported form's fonts for page-level promotion (facade path):
        // capture the /Font entries and strip the key from the form's resources so
        // the form inherits them from the page (the expected layout).
        if (PromoteFontsToPage)
        {
            var formRes = pg.targetReader.ResolveDict(formDict.Get("Resources"))
                ?? formDict.Get("Resources") as PdfDictionary;
            var formFonts = formRes is null ? null
                : pg.targetReader.ResolveDict(formRes.Get("Font")) ?? formRes.Get("Font") as PdfDictionary;
            if (formRes is not null && formFonts is not null)
            {
                _promotedFonts = new List<(string, PdfObject)>();
                foreach (var key in formFonts.Keys.ToList())
                {
                    var v = formFonts.Get(key);
                    if (v is not null) _promotedFonts.Add((key, v));
                }
                formRes.Remove("Font");
            }
        }

        var formStream = new PdfStream(formDict, pg.sourceContent);

        if (pg.targetDoc is not null)
        {
            // Register the form as a single indirect object so repeated applications
            // (and the writer) reference one shared copy.
            var objNum = pg.targetDoc.AllocateObjectNumber();
            pg.targetDoc.AddNewObject(objNum, formStream, registerOverlay: true);
            var formRef = new PdfIndirectRef(objNum, 0);
            _importedForm[pg.targetDoc] = formRef;
            pg.formObject = formRef;
        }
        else
        {
            pg.formObject = formStream;
        }
    }
}

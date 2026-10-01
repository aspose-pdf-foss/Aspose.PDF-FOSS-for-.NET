using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document : IDisposable
{
    /// <summary></summary>
    private void PlacePageAnnotations(Page page)
    {
        foreach (var annot in page.Annotations)
        {
            if (annot is Annotations.FreeTextAnnotation freeText)
                freeText.GenerateAppearance();
            else if (annot is Annotations.LineAnnotation or Annotations.PolygonAnnotation
                            or Annotations.PolylineAnnotation or Annotations.SquareAnnotation
                            or Annotations.CircleAnnotation or Annotations.TextAnnotation
                     && annot.NormalAppearance is null)
                annot.UpdateAppearances();
        }
    }

    /// <summary></summary>
    private void ApplyExplicitPageBackground(Page page)
    {
        if (page.ExplicitBackground is { } pageBg && !page.BackgroundApplied)
        {
            page.BackgroundApplied = true;
            page.RemoveTaggedBackground();
            var isWhite = pageBg.R == 255 && pageBg.G == 255 && pageBg.B == 255;
            if (!isWhite)
            {
                var box = page.MediaBox;
                var bgBuilder = new Content.ContentStreamBuilder();
                bgBuilder.BeginMarkedContent(Page.BackgroundMarkerTag);
                bgBuilder.SaveState();
                bgBuilder.SetFillColor(pageBg.R / 255.0, pageBg.G / 255.0, pageBg.B / 255.0);
                bgBuilder.Rectangle(box.LLX, box.LLY, box.Width, box.Height);
                bgBuilder.Fill();
                bgBuilder.RestoreState();
                bgBuilder.EndMarkedContent();
                page.PrependContentStream(bgBuilder.Build());
            }
        }
    }

    /// <summary></summary>
    private void ApplyPageHeaderFooterBands(Page page)
    {
        page.HeaderFooterApplied = true;
        // Header paragraphs — text, HTML and Table alike — render on every
        // page that references them, whether laid out by the generator or
        // imported with its own content (their cells stay text-extractable
        // and any widgets/links bind to the page they land on).
        //
        // A FOOTER table is the one case that is gated: one HeaderFooter
        // instance shared across the already-inked static pages of an
        // imported document draws no footer table (a footer table stamped
        // onto every page of a loaded document is dropped). It still draws
        // on a generator-laid-out page, and a footer owned by a single page
        // draws normally; text/HTML footer fragments always render.
        bool FooterDrawsTables()
        {
            if (page.Paragraphs.Count > 0 || page.TocInfo is not null
                || (page.GetContentStreamBytes()?.Length ?? 0) == 0)
                return true;
            var footer = page.Footer;
            var refs = 0;
            for (var pi2 = 1; pi2 <= Pages.Count; pi2++)
            {
                if (ReferenceEquals(Pages[pi2].Footer, footer)) refs++;
                if (refs > 1) return false;
            }
            return true;
        }
        // A band that prints its own page number waits until every page exists: laying out
        // this page may still insert one before it, and the number stamped now would be the
        // one it is about to stop having. The footer-table decision belongs to the layout
        // pass, so it is carried across rather than recomputed later.
        if (BandNamesItsPageNumber(page.Header) || BandNamesItsPageNumber(page.Footer))
        {
            page.HeaderFooterApplied = false;
            _deferredNumberBands[page] = FooterDrawsTables();
            return;
        }
        page.Header?.RenderToPage(page, isHeader: true, page.Number, this);
        page.Footer?.RenderToPage(page, isHeader: false, page.Number, this,
            FooterDrawsTables());
    }
}

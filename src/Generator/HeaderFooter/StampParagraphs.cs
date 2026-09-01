using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Stamps;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class HeaderFooter
{
    private void StampParagraphs(Page page, bool isHeader,
        Document? document = null, int pageNumber = 0, bool tableContent = true)
    {
        var hf = new StampParagraphsState();
        hf.page = page;
        hf.isHeader = isHeader;
        hf.document = document;
        hf.pageNumber = pageNumber;
        hf.tableContent = tableContent;
        hf.pageHeight = hf.page.Height;
        hf.fontSize = TextState.FontSize > 0 ? TextState.FontSize : 10;
        hf.mTop = Margin.TopTouched ? Margin.Top : 0;
        hf.mBottom = Margin.BottomTouched ? Margin.Bottom : 20;
        hf.mLeft = Margin.LeftTouched ? Margin.Left
            : hf.page.PageInfo?.Margin is { LeftTouched: true } pm ? pm.Left
            : hf.document?.PageInfo?.Margin is { LeftTouched: true } dm ? dm.Left
            : 90;
        hf.probedFooterBand = !hf.isHeader && Margin.TopTouched
            && (!Margin.BottomTouched || Margin.Bottom == 0);
        if (hf.probedFooterBand)
            foreach (var member in Paragraphs)
                if (member is not (Image or TextFragment)
                    || (member is TextFragment mtf
                        && (mtf.IsInLineParagraph || FragmentLeading(mtf) > 0)))
                { hf.probedFooterBand = false; break; }
        hf.bodyBottom = hf.page.PageInfo?.Margin is { BottomTouched: true } bpm ? bpm.Bottom
            : hf.document?.PageInfo?.Margin is { BottomTouched: true } bdm ? bdm.Bottom
            : 72;
        hf.y = hf.isHeader
            ? hf.pageHeight - hf.mTop - hf.fontSize
            : hf.probedFooterBand ? hf.bodyBottom - hf.mTop
            : hf.mBottom + hf.fontSize;
        hf.x = hf.mLeft;
        hf.firstParagraph = true;
        hf.footerLineTop = double.NaN;
        hf.lastTextY = double.NaN;
        hf.lastTextEndX = double.NaN;

        // Surface every LocalHyperlink / WebHyperlink nested in this header
        // or footer's Paragraphs tree as a LinkAnnotation on the page. The
        // header's text rendering itself is handled per-paragraph-type below,
        // but the annotations need to be emitted regardless of which paragraph
        // types are renderable -- a TextFragment buried inside a Table cell
        // would otherwise drop its hyperlink on the floor.
        EmitNestedHyperlinks(hf.page, Paragraphs, hf.x, hf.y, hf.fontSize);

        foreach (var para in Paragraphs)
        {
            // ── Image ──────────────────────────────────────────────────
            // Images in HeaderFooter.Paragraphs (e.g. logos in a page header
            // or footer) need to drop an Image XObject into the page resources
            // and emit a Do reference. The same code path Document.cs uses for
            // page.Paragraphs Image entries, scoped to a HeaderFooter slot.
            // Headers grow downward from y; footers grow upward from the page
            // bottom so the image stays in the visible footer band even with
            // the default 20-pt margins.
            if (para is Image img) { StampImage(hf, img); continue; }

            // ── FloatingBox ────────────────────────────────────────────
            // A header/footer box honours its Top/Left (positioned at page
            // coordinates) and renders its background plus nested paragraphs
            // (e.g. a Table). Its Left is relative to the page's left content
            // margin, so offset by that
            // margin (the 90 pt Generator default when untouched) while
            // rendering, then restore the caller's values.
            if (para is FloatingBox fb) { StampFloatingBox(hf, fb); continue; }

            // ── Table ──────────────────────────────────────────────────
            // A bare table in the header/footer renders at the running y.
            if (para is Table tbl) { StampTable(hf, tbl); continue; }

            StampText(hf, para);
        }
    }
}

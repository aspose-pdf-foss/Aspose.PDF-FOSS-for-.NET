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
    /// <summary>Point size of a table-of-contents entry whose heading carries no size of its own.</summary>
    private const double DefaultTocEntrySize = 10.0;

    /// <summary>Lays out a page's table of contents and paragraphs: margins and cursor, the TOC entries, then the paragraph flow.</summary>
    private void LayoutPageContent(Page page, PageContentState pc)
    {
        var lc = new PageContentLayoutState();
        lc.page = page;
        lc.pc = pc;
        lc.pl = new PageLayoutState();
        lc.pl.page = lc.page;
        lc.pl.fontName = null;
        lc.pl.pageMargin = lc.page.PageInfo?.Margin;
        lc.pl.docMargin = PageInfo?.Margin;
        lc.pl.marginTop = lc.pl.pageMargin?.TopTouched    == true ? lc.pl.pageMargin!.Top    : lc.pl.docMargin?.TopTouched    == true ? lc.pl.docMargin!.Top    : 72;
        lc.pl.marginBottom = lc.pl.pageMargin?.BottomTouched == true ? lc.pl.pageMargin!.Bottom : lc.pl.docMargin?.BottomTouched == true ? lc.pl.docMargin!.Bottom : 72;
        lc.pl.marginLeft = lc.pl.pageMargin?.LeftTouched   == true ? lc.pl.pageMargin!.Left   : lc.pl.docMargin?.LeftTouched   == true ? lc.pl.docMargin!.Left   : 90;
        lc.pl.marginRight = lc.pl.pageMargin?.RightTouched  == true ? lc.pl.pageMargin!.Right  : lc.pl.docMargin?.RightTouched  == true ? lc.pl.docMargin!.Right  : 90;
        lc.pl.layoutTopY = lc.page.LayoutFrameHeight;
        PrepareLayout(lc);
        lc.pl.tocTopY = null;
        lc.pl.tocCounters = new int[12];
        lc.pl.tocCjkTtf = null;
        lc.pl.tocColCount = 1;
        lc.pl.tocColLefts = System.Array.Empty<double>();
        lc.pl.tocColWidths = System.Array.Empty<double>();
        LayoutTocHeader(lc.page, lc.pc, lc.pl);

        lc.pl.flow = new FlowLayout(lc.page, lc.pc.overflowPages, lc.pl.marginLeft, lc.pl.marginRight, lc.pl.marginTop, lc.pl.marginBottom, lc.pl.curY, EnableNotificationLogging);
        lc.pl.flowSlotStart = lc.pc.overflowPages.Count;
        if (lc.page.ColumnInfo is { ColumnCount: > 1 } columns)
        {
            var (lefts, widths) = BuildColumnGeometry(columns, lc.pl.marginLeft,
                lc.page.Width - lc.pl.marginLeft - lc.pl.marginRight);
            lc.pl.flow.BeginColumns(lefts, widths);
        }
        // A page with an OnBeforePageGenerate handler: every page the flow
        // generates from it gets the handler BEFORE its content is laid
        // (a provisional page carrying the source page's PageInfo and the
        // expected number), so a handler that re-margins or re-heads page 2
        // shapes page 2's layout. The materialised page adopts that state.
        if (lc.page.HasBeforePageGenerate)
        {
            var sourcePage = lc.page;
            lc.pl.flow.OnPageBreak = slot =>
            {
                var prepared = sourcePage.CreateDetachedSibling();
                prepared.SetIndex(sourcePage.Index + (slot - lc.pl.flowSlotStart) + 1);
                prepared.InheritPageInfoFrom(sourcePage);
                prepared.Header = sourcePage.Header;
                prepared.Footer = sourcePage.Footer;
                prepared.CopyBeforePageGenerateFrom(sourcePage);
                prepared.RaiseBeforePageGenerate();
                _preparedOverflowPages[slot] = prepared;
                var pm = prepared.PageInfo.Margin;
                return (pm.TopTouched ? pm.Top : lc.pl.flow.ContentTopMargin,
                        pm.BottomTouched ? pm.Bottom : lc.pl.flow.BottomMargin);
            };
        }
        lc.pl.tb = new Text.TextBuilder(lc.page);
        lc.pl.pendingInlineLineHeight = 0;
        lc.pl.renderedTables = new HashSet<Table>(ReferenceEqualityComparer.Instance);
        lc.pl.paraList = lc.page.Paragraphs.ToList();
        for (lc.paraIdx = 0; lc.paraIdx < lc.pl.paraList.Count; lc.paraIdx++)
        {
            if (!LayoutParagraphAt(lc)) break;
        }
        if (lc.pl.pendingInlineLineHeight > 0)
        {
            lc.pl.flow.AdvanceY(lc.pl.pendingInlineLineHeight);
            lc.pl.pendingInlineLineHeight = 0;
        }
        // TOC entries whose headings live on OTHER pages (they are not
        // paragraphs of this page, so the flow above never reached them):
        // append them after the page's own content, chaining the cursor.
        PlaceTocEntries(lc);
        lc.pl.flow.Commit();
        // FinaliseFootnotes runs before slotEnd capture so its spillover
        // pages (added via _overflowPages) extend this flow's slot range.
        lc.pl.flow.FinaliseNoteBands();
        lc.pc.pendingFlows.Add((lc.pl.flow, lc.pl.flowSlotStart, lc.pc.overflowPages.Count));
        // Remember where this pass left the cursor: on the original page when
        // the flow never spilled, otherwise on the flow's final overflow slot
        // (persisted onto the Page object when the slot materialises below).
        if (lc.pl.flow.CurrentSlot < 0)
            lc.page.LayoutCursorY = lc.pl.flow.CurrentY;
        lc.page.Paragraphs.Clear();
    }
}

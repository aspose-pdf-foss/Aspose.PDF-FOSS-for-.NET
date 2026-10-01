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
    /// <summary>Lays out the head of a table of contents: the column geometry, then the title band.</summary>
    private void LayoutTocHeader(Page page, PageContentState pc, PageLayoutState pl)
    {
        if (pl.tocEntries.Count > 0)
        {
            // Column geometry: honour ColumnInfo.ColumnCount/widths/spacing
            // for a multi-column TOC; otherwise a single column. The single-
            // column geometry is kept identical to the legacy layout (right
            // edge clamped to a 36 pt inset, 18 pt per indent level) so simple
            // one-column TOCs are unaffected.
            var ci = page.TocInfo!.ColumnInfo;
            pl.tocColCount = ci is { ColumnCount: > 1 } ? ci.ColumnCount : 1;
            if (pl.tocColCount > 1)
                (pl.tocColLefts, pl.tocColWidths) = BuildColumnGeometry(
                    ci!, pl.marginLeft, page.Width - pl.marginLeft - pl.marginRight);
            else
            {
                pl.tocColLefts = new[] { pl.marginLeft };
                // The entry band mirrors the page margins: the page number's
                // right edge sits at Width − marginRight.
                pl.tocColWidths = new[] { page.Width - pl.marginRight - pl.marginLeft };
            }
        }

        // Render TOC title if present. In a MULTI-COLUMN TOC the title
        // belongs to the FIRST column's flow: it centres within that
        // column's width and consumes an entry slot there, while the
        // other columns start at the pre-title top (the
        // second column's first entry aligns with the title row).
        if (page.TocInfo?.Title is { } tocTitle)
        {
            if (!LayoutTocTitle(page, pl, tocTitle)) return;
        }
    }
}

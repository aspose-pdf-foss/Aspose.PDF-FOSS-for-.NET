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
    /// <summary>The TOC title block of the page's TOC header.</summary>
    private bool LayoutTocTitle(Page page, PageLayoutState pl, Aspose.Pdf.Text.TextFragment tocTitle)
    {
        pl.fontName ??= Table.RegisterFont(page);
        var titleSize = tocTitle.TextState.FontSize > 0 ? tocTitle.TextState.FontSize : 16;
        // A bold title is emitted with the Helvetica-Bold base font; its
        // glyphs are ~6% wider, which the centring estimate accounts for.
        var titleBold = tocTitle.TextState.IsBold;
        var titleFont = titleBold ? Table.RegisterFont(page, "Helvetica-Bold") : pl.fontName;
        // Measure with the REAL rendered widths (per-glyph Standard-14
        // advances) of the matching AFM table: a length×avg-width guess
        // over/under-shoots and shifts the title off centre.
        var titleMetricFont = titleBold ? "Helvetica-Bold" : "Helvetica";
        double TitleMeasure(string s)
        {
            double w = 0;
            foreach (var tc in s)
            {
                var tcw = Text.Standard14Fonts.GetWidth(titleMetricFont, tc < 256 ? tc : '?');
                if (tcw < 0) tcw = 500;
                w += tcw * titleSize / 1000.0;
            }
            return w;
        }
        // The title band: the first column of a multi-column TOC,
        // else the page's content width.
        var titleBandLeft = pl.tocColCount > 1 && pl.tocColLefts.Length > 0 ? pl.tocColLefts[0] : pl.marginLeft;
        var titleBandWidth = pl.tocColCount > 1 && pl.tocColWidths.Length > 0
            ? pl.tocColWidths[0]
            : page.Width - pl.marginLeft - pl.marginRight;
        // Wrap the title to the band: word-wrap first, and when a
        // single word alone exceeds the band (e.g. a 120 pt
        // "TableOfContents") fill CHARACTER-wise — such a title
        // breaks mid-word across several lines rather
        // than overflowing the page.
        var titleLines = new List<string>();
        foreach (var logical in tocTitle.Text.Replace("\r\n", "\n").Split('\n'))
        {
            var cur = new System.Text.StringBuilder();
            foreach (var word in logical.Split(' '))
            {
                var trial = cur.Length == 0 ? word : cur + " " + word;
                if (TitleMeasure(trial) <= titleBandWidth || cur.Length == 0)
                {
                    if (cur.Length > 0) cur.Append(' ');
                    cur.Append(word);
                }
                else
                {
                    titleLines.Add(cur.ToString());
                    cur.Clear();
                    cur.Append(word);
                }
                // Character-fill an over-wide run (single word or the
                // current accumulation) into full lines.
                while (TitleMeasure(cur.ToString()) > titleBandWidth && cur.Length > 1)
                {
                    var fit = cur.Length - 1;
                    while (fit > 1 && TitleMeasure(cur.ToString(0, fit)) > titleBandWidth) fit--;
                    titleLines.Add(cur.ToString(0, fit));
                    cur.Remove(0, fit);
                }
            }
            if (cur.Length > 0) titleLines.Add(cur.ToString());
        }
        if (titleLines.Count == 0) titleLines.Add(string.Empty);

        // Columns other than the first anchor at the PRE-title top.
        pl.tocTopY = pl.curY;
        // Each title line occupies a 1-em line box below the cursor:
        // baseline at (1 - |descent|) of the em, cursor advancing by
        // the full em (a 12 pt title at cursor
        // 770 draws its baseline at 760.48 and the next box starts
        // at 758).
        var titleAscFrac = (1000.0 + Text.Standard14Fonts.GetDescent("Helvetica")) / 1000.0;
        var builder = new Content.ContentStreamBuilder();
        foreach (var tl in titleLines)
        {
            var titleWidth = TitleMeasure(tl);
            // Single-column: centre on the PAGE (the title
            // x is independent of asymmetric margins). Multi-column:
            // centre within the first column.
            var titleX = pl.tocColCount > 1
                ? System.Math.Max(titleBandLeft, titleBandLeft + (titleBandWidth - titleWidth) / 2)
                : System.Math.Max(pl.marginLeft, (page.Width - titleWidth) / 2);
            builder.BeginText()
                .SetFont(titleFont, titleSize)
                .SetFillColor(0, 0, 0)
                .MoveTextPosition(titleX, pl.curY - titleSize * titleAscFrac)
                .ShowText(tl)
                .EndText();
            pl.curY -= titleSize;
        }
        // The title of an authored tagged document is the marked content of the header bound to it.
        if (page.TocInfo?.TitleHeader is { } titleHeader)
        {
            var mcid = NextTocMcid(page);
            page.AddContentStream(Encoding.ASCII.GetBytes($"/H1 <</MCID {mcid}>> BDC\n"));
            page.AddContentStream(builder.Build());
            page.AddContentStream(Encoding.ASCII.GetBytes("EMC\n"));
            PendingTocTags.Add(new Tagged.TocTag(titleHeader, null, page, mcid, null, tocTitle.Text));
            return true;
        }
        page.AddContentStream(builder.Build());
        return true;
    }
}

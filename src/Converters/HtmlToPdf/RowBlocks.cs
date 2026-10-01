using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>
    /// Extract markup regions that render as STYLED INLINE ROWS — layouts the block
    /// flow cannot express:
    ///  • a site nav bar: a fixed-height container with an absolutely-positioned
    ///    full-width background strip and inline-block tab lists (left + right groups);
    ///  • a centered line of inline links (text-align:center / &lt;center&gt; with only
    ///    inline children), where inter-link spacing comes from CSS margins and the link
    ///    color from the stylesheet.
    /// Each extracted region is replaced by a &lt;rowmark i="N"&gt;&lt;/rowmark&gt;
    /// placeholder; ParseBlocks emits the prebuilt row block at that position.
    /// </summary>
    private static (string result, List<Block> rowBlocks) ExtractRowBlocks(string html, IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        List<Block>? rowBlocks = default;
        var rb = new RowBlocksState();
        rb.html = html;
        rb.css = css;
        rowBlocks = new List<Block>();
        // (an inline-styled document's rows declare themselves in their style attributes)
        var inlineRows = rb.html.IndexOf("inline-block", StringComparison.OrdinalIgnoreCase) >= 0;
        if ((rb.css is null && !inlineRows) || (rb.css is null || rb.css.Count == 0) && !inlineRows
            && rb.html.IndexOf("text-align", StringComparison.OrdinalIgnoreCase) < 0
            && rb.html.IndexOf("<pre", StringComparison.OrdinalIgnoreCase) < 0)
            return (rb.html, rowBlocks);
        // Comments blank out too (space-padded so SrcIndex offsets survive) —
        // ParseDom has no comment handling and their text would leak into the
        // tree as literal content.
        try { rb.dom = ParseDom(Regex.Replace(Regex.Replace(rb.html,
                 @"<!--[\s\S]*?-->", m => new string(' ', m.Length)),
                 @"<(script|style|head)[^>]*>[\s\S]*?</\1>",
                 m => new string(' ', m.Length), RegexOptions.IgnoreCase)); }
        catch { return (rb.html, rowBlocks); }

        rb.extracts = new List<(int start, int end, Block block)>();
        // ── preformatted blocks (verbatim lines on their own faces) ──
        ScanPreBlocks(rb);

        // ── nav bars ──
        ScanNavRows(rb);

        // ── positioned media cards (relative media box + absolute caption bars +
        //    float prose/info columns) ──
        ScanPositionedCards(rb);

        // ── flex-row waybill grids (a full-width bordered container whose rows
        //    are display:flex divs of percent-width bordered columns) ──
        ScanFlexGrids(rb);

        // ── single-line flex rows (a display:flex div of percent-width and
        //    flex-factor items holding inline text) ──
        ScanFlexRows(rb);

        // ── inline-block rows (a div of percent-width inline-block items) ──
        ScanInlineRows(rb);

        // ── positioned slides (a relative min/max-width canvas whose direct
        //    children are absolutely positioned text and background-image boxes —
        //    a slide editor's saved markup) ──
        ScanPositionedSlides(rb);

        // ── centered search forms ──
        ScanSearchForms(rb);

        // ── RTL fixed-width diagram tables (figure + labels + svg legend row) ──
        ScanRtlTables(rb);

        // ── centered inline-link rows ──
        ScanCenteredLinkRows(rb);

        if (rb.extracts.Count == 0) return (rb.html, rowBlocks);
        // Assign indices in document order; substitute back-to-front so earlier
        // regions' source offsets stay valid.
        rb.extracts.Sort((a, b) => a.start.CompareTo(b.start));
        rb.sb = new StringBuilder(rb.html);
        for (var i = rb.extracts.Count - 1; i >= 0; i--)
        {
            var (start, end, _) = rb.extracts[i];
            rb.sb.Remove(start, end - start);
            rb.sb.Insert(start, $"<rowmark i=\"{i}\"></rowmark>");
        }
        foreach (var (_, _, b) in rb.extracts) rowBlocks.Add(b);
        return (rb.sb.ToString(), rowBlocks);
    }
}

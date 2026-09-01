using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Stamps;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

/// <summary>
/// Represents a header or footer that can be applied to PDF pages.
/// Supports page number substitution: use '#' in the text to insert the 1-based page number.
/// </summary>
public sealed partial class HeaderFooter
{
    /// <summary>The text content. Use '#' as a placeholder for the page number.</summary>
    public string Text { get; set; } = "";

    /// <summary>Text formatting state (font, size, color).</summary>
    public TextState TextState { get; set; } = new()
    {
        FontName = "Helvetica",
        FontSize = 10,
        ForegroundColor = Color.Black,
    };

    /// <summary>Margins controlling the position of the header/footer text.
    /// Untouched sides resolve to path-specific defaults at stamp time: the
    /// plain-text stamp keeps the legacy 20 pt band, while the Paragraphs
    /// render uses left = the page's content left margin and header top = 0
    /// (a header paragraph starts at the physical page top).</summary>
    public MarginInfo Margin { get; set; } = new();

    /// <summary>Horizontal alignment of the text.</summary>
    public HorizontalAlignment HorizontalAlignment { get; set; } = HorizontalAlignment.Center;

    /// <summary>
    /// Collection of paragraph objects (TextFragment, HtmlFragment, etc.) to render in the header/footer.
    /// When populated, these are used instead of <see cref="Text"/>.
    /// </summary>
    public Paragraphs Paragraphs { get; set; } = new();

    /// <summary>Whether the band prints the document's page count (<c>$P</c>),
    /// which on a page that still has paragraphs to lay out is only known once
    /// the flow's overflow pages exist.</summary>
    internal bool UsesPageCount
    {
        get
        {
            if (Text.Contains("$P")) return true;
            foreach (var p in Paragraphs)
                if (p is TextFragment tf && tf.Text.Contains("$P")) return true;
            return false;
        }
    }

    /// <summary>Whether content overflowing the header/footer area is clipped. Stored only.</summary>
    public bool IsClipExtraContent { get; set; }

    /// <summary>
    /// Create a HeaderFooter from a text string.
    /// </summary>
    /// <param name="text">Text content. Use '#' for page number substitution.</param>
    public static HeaderFooter FromText(string text)
    {
        return new HeaderFooter { Text = text };
    }

    /// <summary>
    /// Shallow clone — copies <see cref="Text"/>/<see cref="TextState"/>/
    /// <see cref="Margin"/>/<see cref="HorizontalAlignment"/> plus shallow-copied
    /// references to every entry in <see cref="Paragraphs"/>. Same-content-on-every-page
    /// usage requires this; otherwise cloned headers/footers would render blank.
    /// Returns <see cref="object"/> to keep the published reflection shape.
    /// </summary>
    public object Clone()
    {
        var copy = new HeaderFooter
        {
            Text = Text,
            TextState = TextState,
            Margin = Margin,
            HorizontalAlignment = HorizontalAlignment,
            IsClipExtraContent = IsClipExtraContent,
        };
        foreach (var p in Paragraphs)
            copy.Paragraphs.Add(p);
        return copy;
    }

    /// <summary>
    /// Apply this header/footer as a header (positioned at the top) to the given pages.
    /// </summary>
    /// <param name="pages">Pages to stamp.</param>
    /// <param name="substitutePageNumbers">
    /// When true, '#' in the text is replaced with the 1-based page number.
    /// </param>
    public void ApplyAsHeader(Page[] pages, bool substitutePageNumbers = true)
    {
        for (int i = 0; i < pages.Length; i++)
        {
            var page = pages[i];
            var text = substitutePageNumbers ? Text.Replace("#", (i + 1).ToString()) : Text;
            StampText(page, text, isHeader: true);
        }
    }

    /// <summary>
    /// Apply this header/footer as a footer (positioned at the bottom) to the given pages.
    /// </summary>
    /// <param name="pages">Pages to stamp.</param>
    /// <param name="substitutePageNumbers">
    /// When true, '#' in the text is replaced with the 1-based page number.
    /// </param>
    public void ApplyAsFooter(Page[] pages, bool substitutePageNumbers = true)
    {
        for (int i = 0; i < pages.Length; i++)
        {
            var page = pages[i];
            var text = substitutePageNumbers ? Text.Replace("#", (i + 1).ToString()) : Text;
            StampText(page, text, isHeader: false);
        }
    }

    /// <summary>Render this header/footer onto a page that referenced it through
    /// <see cref="Page.Header"/> / <see cref="Page.Footer"/>. Substitutes '#' in
    /// <see cref="Text"/> with the 1-based page number, then emits the text or
    /// paragraph content at the top (header) or bottom (footer) margin.
    /// <paramref name="paragraphContent"/> gates TABLE paragraphs only:
    /// a header/footer table belongs to the page generator and draws only on
    /// generator-laid-out pages — a footer table assigned to the static pages
    /// of an imported document stays undrawn, while text/HTML fragments (and
    /// the plain <see cref="Text"/> stamp) render everywhere.</summary>
    internal void RenderToPage(Page page, bool isHeader, int pageNumber, Document? document = null,
        bool paragraphContent = true)
    {
        // An imported page can leave a persistent CTM active at the end of its
        // content (e.g. a top-level y-flip); bracket it so the header/footer
        // draws in default page space.
        page.IsolateExistingContent();
        var text = Text.Replace("#", pageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
        text = ApplyLabelMacros(text, document, pageNumber);
        StampText(page, text, isHeader, document, pageNumber, paragraphContent);
    }

    /// <summary>Resolve the page-label macros <c>$p</c> (this page's label) and
    /// <c>$P</c> (the last-page label of this page's label range — the section
    /// total) against the document's /PageLabels. No-op when no document is
    /// supplied. On a document with no labels these degrade to the page number
    /// and the total page count respectively.</summary>
    /// <summary>The band's macro resolution, shared with the page tables: a table cell
    /// outside a band resolves <c>$p</c>/<c>$P</c> the same way and per page.</summary>
    internal static string ApplyPageLabelMacros(string text, Document? document, int pageNumber)
        => ApplyLabelMacros(text, document, pageNumber);

    private static string ApplyLabelMacros(string text, Document? document, int pageNumber)
    {
        if (document is null || string.IsNullOrEmpty(text)) return text;
        if (!text.Contains("$p") && !text.Contains("$P")) return text;
        var idx0 = pageNumber - 1;
        var labels = document.PageLabels;
        var pageCount = document.Pages.Count;
        return text
            .Replace("$P", labels.GetRangeLastLabel(idx0, pageCount))
            .Replace("$p", labels.FormatLabel(idx0));
    }

    /// <summary>The Generator's default page content margin, which an untouched
    /// header/footer band inherits on the left and right.</summary>
    private const double DefaultBandMargin = 90;

    /// <summary>Substitute the <c>$p</c>/<c>$P</c> page-label macros in every cell
    /// text segment of a band table (nested tables included), returning the
    /// originals for <see cref="RestoreCellMacros"/> — the same table object is
    /// stamped on every page, so the substitution must not stick.</summary>
    private static List<(Text.TextSegment seg, string original)>? SubstituteCellMacros(
        Table table, Document? document, int pageNumber)
    {
        if (document is null) return null;
        List<(Text.TextSegment, string)>? swaps = null;
        void Walk(Table t)
        {
            foreach (var row in t.Rows)
                foreach (var cell in row.Cells)
                    foreach (var p in cell.Paragraphs)
                        switch (p)
                        {
                            case Text.TextFragment ctf:
                                foreach (var seg in ctf.Segments)
                                {
                                    var txt = seg.Text;
                                    if (string.IsNullOrEmpty(txt)
                                        || (!txt.Contains("$p") && !txt.Contains("$P"))) continue;
                                    (swaps ??= new()).Add((seg, txt));
                                    seg.Text = ApplyLabelMacros(txt, document, pageNumber);
                                }
                                break;
                            case Table nested:
                                Walk(nested);
                                break;
                        }
        }
        Walk(table);
        return swaps;
    }

    private static void RestoreCellMacros(List<(Text.TextSegment seg, string original)>? swaps)
    {
        if (swaps is null) return;
        foreach (var (seg, original) in swaps)
            seg.Text = original;
    }

    /// <summary>The size a fragment draws at and the face it carries: the
    /// fragment's own when the caller touched it, else its first sized segment's.</summary>
    private static (double fs, Aspose.Pdf.Text.Font? face) FragmentSizeAndFace(TextFragment tf)
    {
        double fs = tf.TextState.FontSizeTouched ? tf.TextState.FontSize : 0;
        var face = tf.TextState.Font?.SourceFontData is not null ? tf.TextState.Font : null;
        foreach (var seg in tf.Segments)
        {
            if (fs <= 0 && seg.TextState.FontSizeTouched) fs = seg.TextState.FontSize;
            if (face is null && seg.TextState.Font?.SourceFontData is not null) face = seg.TextState.Font;
        }
        return (fs, face);
    }

                                                      // sits at this plus the sheet's @page margin
                                                      // (constant across page/body widths)

    // The REAL Segoe UI faces, read once from the system font files: the report
    // dialect draws with the real faces' glyph shapes wherever they are
    // available (FindFont system faces carry no width tables, so the
    // files are read directly). Null on a machine without them — the dialect then
    // falls back to metric-anchored Standard-14 glyphs.
    private static byte[]? _segoeTtf, _segoeBoldTtf;
    private static bool _segoeProbed;


    private static Dictionary<int, double>? _segoeKern, _segoeBoldKern;

}

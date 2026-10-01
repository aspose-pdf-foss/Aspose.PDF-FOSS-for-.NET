using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class HeaderFooter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class StampParagraphsState
{
    public double pageHeight;
    public float fontSize;
    // Untouched margins resolve to the header-band geometry:
    // left = the page's content left margin (page PageInfo → document
    // PageInfo → the 90 pt Generator default), header top = 0 — a header
    // paragraph's first baseline hangs just below the physical page top.
    public double mTop;
    /// <summary>The header's text members stacked so far: each one's box (size + leading), the next seats under them.</summary>
    public double headerStackH;
    public double mBottom;
    public double mLeft;
    // Footer band law (isolated on the plain Image/TextFragment
    // stack): the band is a TOP-DOWN stack whose top hangs Margin.Top below
    // the BODY's bottom content margin (top 10 → band top 50 on a 60 pt
    // bottom margin; top 30 → 30). Scoped to the probed shape — a footer
    // whose Margin.Top was touched, whose Margin.Bottom is zero, and whose
    // members are only plain images and text fragments (no leading, no
    // inline members); every other band keeps the legacy bottom-up stack.
    // ⚠ The MODERN writer anchors the leaded/inline member seat at the
    // same bodyBottom − Margin.Top band top (probed: baselines
    // 44.899/44.07 = 62 − (fs+leading) + descent·fs), but the corpus
    // templates for that family bake the OLD era's placement — widening
    // this band to them fails their templates, so they stay legacy.
    public bool probedFooterBand;
    public double bodyBottom;
    public double y;
    public double x;
    // Nothing has been placed in the band yet: the first paragraph seats against
    // the band's own top edge.
    public bool firstParagraph;
    // Top of a footer's first text line, once placed (its inline members hang
    // from it).
    public double footerLineTop;
    // Baseline and end-X of the last rendered text paragraph, so a following
    // TextFragment with IsInLineParagraph continues on the SAME line directly
    // after it (such fragments render inline, with no gap).
    public double lastTextY;
    public double lastTextEndX;
    // The seat of a stamped table: its top edge, and the bottom bound the
    // multi-page build must respect (a plain footer table grows downward into
    // the margin, so its bound reaches below the page).
    public double tableTop;
    public double footerBottomBound;
    // Per-page working clones of interactive fields in a footer table:
    // the SAME footer renders on every page, and each page must carry
    // its own field + widget (one AcroForm field per page, all at the
    // same footer rectangle).
    public List<(Cell cell, int idx, Aspose.Pdf.Forms.CheckboxField proto, Aspose.Pdf.Forms.CheckboxField clone)>? fieldSwaps;
    // The stamping inputs, captured from the method parameters.
    public Page page = null!;
    public bool isHeader;
    public Document? document;
    public int pageNumber;
    public bool tableContent;
}
}

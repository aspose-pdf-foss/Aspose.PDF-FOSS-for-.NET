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

public sealed partial class Document
{
    /// <summary>A list box's margin above and below, in em (probed: 13.4 pt at 12 pt).</summary>
    private const double ListMarginEm = 1.12;

    /// <summary>The gap between a list marker's right edge and its item's text (probed:
    /// the bullet ends at 115.5 before text at 120).</summary>
    private const double ListMarkerGapPt = 4.5;

    /// <summary>Whether the block after <paramref name="b"/> is the next line of the same
    /// paragraph: a text line a <c>&lt;br&gt;</c> split off, with no margin, list state or
    /// indent of its own.</summary>
    private static bool ContinuesParagraph(HtmlBlockRenderState hb, Aspose.Pdf.Converters.HtmlToPdfConverter.Block b)
    {
        if (hb.blockIndex + 1 >= hb.blocks.Count || b.IsListItem || string.IsNullOrWhiteSpace(b.Text)) return false;
        var next = hb.blocks[hb.blockIndex + 1];
        return !next.IsHardBreak && !next.IsListItem && !next.IsImage && !string.IsNullOrWhiteSpace(next.Text)
            && next.MarginTop <= 0 && b.MarginBottom <= 0 && next.LeftIndent == b.LeftIndent;
    }

    /// <summary>HTML blocks: one parsed block rendered into the flow.</summary>
    private bool RenderHtmlBlock(HtmlBlockRenderState hb, Aspose.Pdf.Converters.HtmlToPdfConverter.Block b)
    {
        var rb = new HtmlBlockState();
        rb.styledFace = null;
        if (!hb.legacyDialect && b.EmBold && b.EmItalic && b.FontFamily is { Length: > 0 } sf)
        {
            var stl = Text.FontStyles.Bold | Text.FontStyles.Italic;
            var cand = SafeFindFontStyled(sf, stl);
            if (cand?.SourceFontData?.TtfData is { Length: > 0 }) rb.styledFace = cand;
        }
        rb.fontSize = hb.legacyDialect && b.LegacyFontPt > 0 ? b.LegacyFontPt
            : rb.styledFace is not null ? (b.FontSize > 11.0 ? b.FontSize : 12.0)
            : b.FontSize > 0 ? b.FontSize : 11.0;
        rb.legacyLead = hb.legacyDialect ? rb.fontSize * 0.25 : 0.0;
        // Entering or leaving a list spends the list box's margin (collapsed with the
        // neighbouring block's own); the items inside sit at the plain line pitch.
        var listMargin = b.IsListItem != hb.prevWasListItem ? rb.fontSize * ListMarginEm : 0.0;
        rb.topMargin = Math.Max(b.MarginTop, listMargin);
        if (hb.paragraphMargins)
        {
            rb.topMargin = Math.Max(rb.topMargin, hb.pendingMarginBottom);
            hb.pendingMarginBottom = b.MarginBottom;
        }
        hb.prevWasListItem = b.IsListItem;
        if (rb.topMargin > 0) hb.flow.AdvanceY(rb.topMargin);

        if (TryRenderSpecialBlock(hb, rb, b)) return true;
        rb.bf = new Text.TextFragment(b.Text);
        rb.bf.TextState.FontSize = (float)rb.fontSize;
        rb.htmlCallerLs = (double)(hb.html.TextState?.LineSpacing ?? 0f);
        PrepareBlockText(hb, rb, b);
        // The block pitch above is layout-synthesised, not a caller
        // request — keep the legacy first-line drop. A pitch the CALLER
        // declared is a CSS line box instead, and seats its first baseline
        // on the box's own half-leading plus ascent.
        rb.bf.TextState.LineSpacingSynthetic = true;
        rb.bf.TextState.LineBoxSeat = rb.htmlCallerLs > 0;
        rb.bf.TextState.CssLineBoxSeat = rb.cssLineBox && rb.htmlCallerLs <= 0;
        rb.bandColor = b.BackgroundColor;
        rb.bandStartSlot = 0;
        rb.bandTop = 0.0;
        // Lines a <br> split off one paragraph stay together across the page edge: a
        // line whose paragraph continues below it needs room for two lines, as the
        // flow's own widow rule gives a fragment of two (measured on the 100px
        // line-height body: the three-line closing paragraph opens the next page).
        if (ContinuesParagraph(hb, b))
            hb.flow.EnsureRoomFor(2 * (rb.fontSize + rb.bf.TextState.LineSpacing));
        if (b.Marker is { Length: > 0 } marker && !b.MarkerAfter)
            hb.flow.WriteListMarker(marker, rb.bf.TextState, rb.fontSize,
                hb.marginLeft + b.LeftIndent - ListMarkerGapPt);
        EmitBlockText(hb, rb, b);
        if (b.MarginBottom > 0 && !hb.paragraphMargins) hb.flow.AdvanceY(b.MarginBottom);
        return true;
    }
}

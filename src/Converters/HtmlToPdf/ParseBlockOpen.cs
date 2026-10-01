using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One arm of ParseBlocks' token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleBlockOpen(ParseBlocksState pb, Token tok, string tag)
    {
        // Browser-UA flow: a self-closed <p/> is an EMPTY paragraph —
        // its UA margin max-collapses onto the next block; nothing is
        // pushed. A real <p> open is counted so a matching </p> is told
        // apart from the stray-close quirk above.
        if (!OpensAsSpecialBlock(pb, tok, tag)) return;
        // Start a new block: flush any pending inline text at the
        // outer style, then push the new style.
        BeginBlockStyle(pb, tag);
        // Redline: a paragraph's 1-3 value margin shorthand opens a top
        // margin (`margin: 8pt 0pt 0pt`), and a positive text-indent
        // shifts its (single) line right.
        ApplySpanPtTypography(pb, tok);
        ApplyWordMailIndent(pb, tok);
        // A sheet rule addressed at this block — `p.MsoNormal { margin:
        // 0cm; margin-bottom: .0001pt }` (the Word-filtered idiom) or a
        // bare element rule — replaces the UA paragraph margins: the
        // sheet authors its own rhythm.
        ApplyBrowserUaCssAndClassStack(pb, tok, tag);
        // Container box chrome from CLASS rules (containerBoxIndents mode):
        // padding+border-left indent the content; the vertical chrome stacks
        // onto the next block's top margin; a class-rule HEIGHT (the widget
        // header band) floors the next block's height. A width:100%
        // container's horizontal chrome overflows its parent (CSS content-box:
        // its content box equals the parent's, the chrome paints outside), so
        // it indents but must NOT bill the page-widen; width:auto chrome does.
        ApplyContainerBoxIndents(pb);
        // Band annotation injected by the print-grid pre-pass (the ancestry was
        // resolved before segmentation split the host div away).
        ApplyUaBandAndStyleAttributes(pb, tok, tag);
        // The element's OWN padding-top LONGHAND (any flow, longhand only —
        // the `padding:` shorthand stays with each dialect's own rules): fuel
        // for the childless-empty close spacer alone. Probed (bench d1): an
        // empty <div style="padding-top:70px"></div> between two paragraphs
        // is real box space; a padded element WITH content
        // keeps its dialect's existing behaviour untouched.
        ApplyCssAndLedgerRules(pb, tok, tag);
        // Styled-article panel (a div class declaring border + background,
        // the `.td-toc` box): its vertical box is real space — a pad above
        // its header now, the bottom pad + panel margin when it closes.
        ApplyArticleRhythmAndBands(pb, tok, tag);
        // A border-TOP-only element is a DIVIDER: one rule above its
        // content, emitted as its own marker block so the border never
        // rides the element's text blocks.
        ApplyBorderBoxes(pb, tok);
        // List context: an <ol>/<ul> style carries a counter its <li> children
        // draw from; an <li> takes the next marker from its enclosing list. A list
        // whose CSS supplies its own `li:nth-child(..)::before { content }` markers uses
        // those (indexed by child position) instead of the numeric/bullet default.
        if (tag is "ol" or "ul")
        {
            OpenListBlock(pb, tok, tag);
        }
        else if (tag == "li" && pb.parent.ListKind != 0)
        {
            OpenListItemBlock(pb);
        }
        // A container that paints a background opens a BOX: remember where its first child
        // block will land. The fill is emitted per LINE, so a container whose content turns out
        // to be block-level children flushes no line of its own and would paint nothing at all;
        // the close decides whether that happened and, if so, brackets the children with markers.
        // Never the BODY: its background is the page canvas, painted once behind the whole
        // content box rather than as a box around the blocks it happens to contain.
        if (pb.style.BackgroundColor is { } bgSpanFill
            && tag is not ("body" or "html"))
            pb.bgSpans.Push((pb.blocks.Count, bgSpanFill, pb.style.DeclaredWidthPt,
                pb.style.DeclaredWidthFrac, pb.style.BgSpanPadTopPt, pb.style.BgSpanPadBottomPt,
                pb.style.BgSpanPadLeftPt, pb.styleStack.Count + 1));
        // A declared height/min-height opens a FLOOR: remember the flow
        // cursor here, and at the element's close drop it to (start - H)
        // if the content has not already reached that far.
        if (pb.style.HeightFloorPt > 0)
        {
            pb.style.HeightFloorDeferred = true;
            pb.heightFloors.Push((pb.blocks.Count, pb.style.HeightFloorPt, pb.styleStack.Count + 1));
            pb.blocks.Add(new Block
            {
                Text = "",
                HeightFloorStart = true,
                FontSize = pb.style.FontSize,
                FontRes = pb.style.FontRes,
                LeftIndent = pb.style.LeftIndent,
            });
        }
        pb.styleStack.Push(pb.style);
    }
}

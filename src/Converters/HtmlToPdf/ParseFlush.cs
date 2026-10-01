using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The block parser's Flush, lifted out of ParseBlocks: it closes the block
// under construction from the parse state and the rhythm flags. Body is verbatim.
    private static void Flush(ParseBlocksState pb, bool controlBoxes, bool uaBlockRhythm, bool articleRhythm, bool spanPtTypography, bool _unused, BlockStyle styleUsed)
    {
        // An <a> still open at the flush boundary covers text up to here in THIS block.
        CloseInlineRuns(pb);
        var raw = pb.currentText.ToString();
        // Collapse runs of *ASCII* whitespace only — U+00A0 (from
        // &nbsp;) is intentional visual content and must survive
        // collapse+Trim so an &nbsp;-only <p> still emits a line.
        // CollapseWhitespaceWithMap reproduces that collapse+Trim while tracking,
        // for each output char, the raw index it came from — so inline anchor
        // spans can be re-expressed in the collapsed Text's coordinates.
        var (collapsed, rawOf) = CollapseWhitespaceWithMap(raw);
        // The spaces inside an isolated span's run are non-breaking: the run wraps as one unit.
        collapsed = ApplyIsolatesAndRunEdges(pb, collapsed, rawOf, raw, controlBoxes);
        // font-size:0 spacer (the float-terminator "clear:both;height:0;
        // font-size:0" idiom): its &nbsp; occupies a zero-height line box —
        // emit nothing rather than a default-size blank line.
        if (styleUsed.ZeroFontSize && collapsed.Trim(' ', ' ').Length == 0)
        {
            pb.currentText.Clear();
            pb.rawAnchors.Clear();
            pb.rawBolds.Clear();
            pb.rawUnders.Clear(); pb.rawStrikes.Clear();
            pb.rawItalics.Clear();
            pb.rawColorRuns.Clear();
            pb.rawSizeRuns.Clear();
            pb.rawFamilyRuns.Clear();
            pb.rawDecorRuns.Clear();
            return;
        }
        if (collapsed.Length > 0)
        {
            // A block whose tag rule underlines it: the run covers its whole text. Only a
            // flush that EMITS consumes the mark - the whitespace-only flushes between a
            // heading's open tag and its text must not.
            if (pb.blockUnderStart >= 0)
            {
                if (pb.currentText.Length > pb.blockUnderStart) pb.rawUnders.Add((pb.blockUnderStart, pb.currentText.Length));
                pb.blockUnderStart = -1;
            }
            EmitFlushedBlock(pb, controlBoxes, uaBlockRhythm, articleRhythm, spanPtTypography, styleUsed, collapsed, rawOf);
        }
        // The promoted block is out: the enclosing style takes its face back.
        if (pb.emphasisRestorePending && collapsed.Length > 0 && pb.emphasisSave is { } saved)
        {
            styleUsed.FontRes = saved.FontRes;
            styleUsed.EmBold = saved.EmBold;
            styleUsed.EmItalic = saved.EmItalic;
            pb.emphasisSave = null;
            pb.emphasisRestorePending = false;
        }
        // A closed sized span's line is out: the lines after it take the size back -
        // unless another sized span is still open (a span broken by <br>s keeps its
        // size for every line until its own close).
        if (pb.uaSpanSizeRestorePt > 0 && collapsed.Length > 0 && pb.uaSpanSizeSaves.Count == 0)
        {
            styleUsed.FontSize = pb.uaSpanSizeRestorePt;
            pb.uaSpanSizeRestorePt = 0;
        }
        else if (styleUsed.ExplicitHeight > 0 && !styleUsed.HeightFloorDeferred)
        {
            EmitHeightFloorBlock(pb, styleUsed);
        }
        // Empty block close-tags without explicit height do not emit a
        // spacer — nested empty containers (e.g. <div><div></div></div>)
        // would otherwise inflate page count well beyond the text
        // volume. Explicit vertical spacing comes from <br>, <hr>,
        // block margins, and any CSS height/min-height override.
        pb.currentText.Clear();
        pb.ptyLeadFs = 0;
        pb.rawDecorRuns.Clear();
        pb.rawAnchors.Clear();
        pb.rawBolds.Clear();
        pb.rawUnders.Clear(); pb.rawStrikes.Clear();
        pb.rawItalics.Clear();
        pb.rawColorRuns.Clear();
            pb.rawSizeRuns.Clear();
            pb.rawFamilyRuns.Clear();
        // An <a> left open across a block/line boundary continues in the next
        // block; record what it covered here and re-anchor it at offset 0.
        if (pb.openAnchors.Count > 0)
        {
            var carried = pb.openAnchors.ToArray();
            pb.openAnchors.Clear();
            for (int oi = carried.Length - 1; oi >= 0; oi--)
                pb.openAnchors.Push((0, carried[oi].url));
        }
    }
}

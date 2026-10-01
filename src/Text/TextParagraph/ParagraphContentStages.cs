using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using System.Globalization;

namespace Aspose.Pdf.Text;

public sealed partial class TextParagraph
{
    /// <summary>The stages of the paragraph content build: trimming unwrapped lines to the default box, seating the block in its rectangle, and seating an unrotated block above the page origin.</summary>
    private void SeatUnrotatedBlock(ParagraphContentState pc)
    {
        if (!pc.hasRotation && Rectangle is null)
        {
            var lastLine = pc.visualLines[pc.visualLines.Count - 1];
            var lastTs = lastLine.Count > 0 ? lastLine[0].ts : _lines[_lines.Count - 1].TextState;
            var lastFd = lastTs.FontData ?? lastTs.Font?.SourceFontData;
            bool lifted = pc.ensureCidFont is not null && lastFd is { TtfData: not null };
            pc.startY += pc.blockHeight + (lifted ? 0 : GetDescentCompensation(lastTs, LineFontSize(lastLine)));
        }
    }

    /// <summary></summary>
    private void SeatBlockInRectangle(ParagraphContentState pc)
    {
        // The block is seated in the rect per VerticalAlignment (default
        // Bottom = its bottom ON LLY + Margin.Bottom); startY is its top.
        double blockBottom = BlockBottom(pc.blockHeight);
        pc.startY = blockBottom + pc.blockHeight;

        // Paragraph background: one box under the whole block, as wide as the
        // widest line plus the pad — or exactly the content width when a line
        // overflows it. Written in a local frame (cm + re) like the fragment
        // highlight, before the clip so it is the page's first rectangle.
        if (BackgroundColor is { } pbg && !pc.hasRotation)
        {
            double maxLineWidth = 0;
            foreach (var ln in pc.visualLines)
            {
                double w = 0;
                foreach (var (text, ts) in ln) w += MeasureLineWidth(text, ts);
                if (w > maxLineWidth) maxLineWidth = w;
            }
            double bgWidth = maxLineWidth <= pc.clipWidth!.Value ? maxLineWidth + BackgroundPad : pc.clipWidth.Value;
            pc.builder.SaveState();
            pc.builder.SetFillColor(pbg.R / 255.0, pbg.G / 255.0, pbg.B / 255.0);
            pc.builder.SetMatrix(1, 0, 0, 1, pc.startX, blockBottom);
            pc.builder.Rectangle(0, 0, bgWidth, pc.blockHeight);
            pc.builder.FillEvenOdd();
            pc.builder.RestoreState();
        }

        // The clip spans the content width and rises ClipLineBoxFactor × the
        // block height from the block bottom — past the rect top when the block
        // is taller than the rect — and never reaches below the rect bottom.
        double clipBottom = Math.Max(blockBottom, Rectangle!.LLY + Margin.Bottom);
        double clipTop = blockBottom + pc.blockHeight * ClipLineBoxFactor;
        if (clipTop > clipBottom)
        {
            pc.builder.Rectangle(pc.startX, clipBottom, pc.clipWidth!.Value, clipTop - clipBottom);
            pc.builder.Clip();
        }
    }

    /// <summary></summary>
    private void TrimUnwrappedLinesToBox(ParagraphContentState pc)
    {
        for (int li = 0; li < pc.visualLines.Count; li++)
        {
            var line = pc.visualLines[li];
            double run = 0; bool cut = false;
            var kept = new List<(string text, TextState ts)>();
            foreach (var (text, ts) in line)
            {
                if (cut) break;
                int keep = text.Length;
                for (int ci = 0; ci < text.Length; ci++)
                {
                    var w = CharWidth(text[ci], ts);
                    if (run + w > 500.0001) { keep = ci; cut = true; break; }
                    run += w;
                }
                if (keep > 0) kept.Add((text.Substring(0, keep), ts));
            }
            if (cut) pc.visualLines[li] = kept;
        }
    }
}

using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The paragraph and heading tags that close a metric cell's segment, and the white span that closes with them.</summary>
    private static void CloseMetricParagraphAndSpanTag(MetricTableState mt, string tag)
    {
        if ((tag is "p" || (mt.mps.uaBlockCells && tag is "h1" or "h2" or "h3" or "h4" or "h5" or "h6"))
            && mt.wrapperStacks && !mt.mps.collapsedGrid && mt.mps.cell is not null)
        {
            // The UA serif grid seats a cell's paragraph inside its block margins - except the
            // quirks-mode paragraph that is the cell's DIRECT child, whose margins the browser
            // drops (measured: the surgery-lights' span-wrapped paragraphs keep 13.44 above and
            // below; the claim forms' bare td>p rows band on their lines alone).
            if (mt.stdSerif && !mt.reportCells && !mt.mps.pInlineCells && (!_quirksRowStrut || mt.mps.inlineWrapDepth > 0))
                mt.mps.cell.ParaBlocks++;
            // Report cells: the closing paragraph SEGMENT snapshots the
            // typography its spans left active, and carries the UA
            // 1.12 em block margins (collapsed between neighbours).
            if (mt.reportCells && mt.mps.curSeg is not null)
            {
                // (a UA block keeps the size its own style states)
                mt.mps.curSeg.FontSize ??= mt.mps.segInkSeen ? mt.mps.segFs : mt.mps.cell.FontSize;
                mt.mps.curSeg.Face = mt.mps.segInkSeen ? mt.mps.segFace : mt.mps.cell.Face;
                // bold by MAJORITY of the paragraph's ink (its strong
                // runs against its plain runs); style bold always wins
                if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_MROW") == "1")
                    Console.WriteLine("[pclose] tag=" + tag + " ua=" + mt.mps.uaBlockCells + " segFs=" + mt.mps.curSeg.FontSize + " lines=" + mt.mps.curSeg.LineTypo.Count + " bold=" + mt.mps.segBoldChars + "/" + mt.mps.segPlainChars + " text='" + mt.mps.divText.ToString().Substring(0, Math.Min(20, mt.mps.divText.Length)) + "'");
                if (mt.mps.uaBlockCells && mt.mps.curSeg.LineTypo.Count > 0) CloseUaBlockLine(mt.mps);
                mt.mps.curSeg.Bold |= mt.mps.cell.Bold || mt.mps.segBoldChars > mt.mps.segPlainChars;
                mt.mps.curSeg.Fore = mt.mps.segInkSeen ? mt.mps.segFore : mt.mps.cell.Fore;
                // a UA block's margins are em of ITS OWN size: the paragraph's 1.12 em, a
                // heading's UA margin (measured: 9 pt paragraphs stand 10.08 apart, the 12 pt
                // h2 above them 9 = 0.75 em)
                mt.mps.curSeg.IsParagraph = tag == "p";
                mt.mps.curSeg.IsHeading = tag != "p";
                mt.mps.curSeg.DirectChild = mt.mps.inlineWrapDepth == 0;
                var pFs = mt.mps.uaBlockCells ? mt.mps.curSeg.FontSize ?? mt.mps.cell.FontSize ?? mt.mps.fontSize
                    : mt.mps.cell.FontSize ?? mt.mps.fontSize;
                var blockMarginEm = mt.mps.uaBlockCells && tag != "p" ? UaHeadingEm(tag).marginEm : UaBlockMarginEm;
                if (!mt.mps.curSeg.MarginsExplicit)
                {
                    mt.mps.curSeg.MarginTopPt = mt.mps.curSeg.MarginTopStatedPt ?? blockMarginEm * pFs;
                    mt.mps.curSeg.MarginBottomPt = mt.mps.curSeg.MarginBottomStatedPt ?? blockMarginEm * pFs;
                }
                var pMarkers = string.Concat(
                    from Match pm in Regex.Matches(mt.mps.divText.ToString(), "\u0002\\d+\u0003")
                    select pm.Value);
                if (pMarkers.Length > 0)
                {
                    var cleaned = Regex.Replace(mt.mps.divText.ToString(), "\u0002\\d+\u0003", " ");
                    mt.mps.divText.Clear();
                    mt.mps.divText.Append(cleaned);
                    mt.text.Append(pMarkers);
                }
                CloseSeg(mt.mps, mt.text, mt.reportCells, mt.stdSerif);
            }
            // other wrapper flows keep the calibrated blank-line gap
            // (…a UA paragraph declaring `margin: 0` closes on the line break alone - probed on the
            // Words letter: two nbsp paragraphs stand two lines, not three)
            else if (mt.mps.curSeg is null && mt.text.Length > 0 && !EmptyUaParagraph(mt))
            {
                mt.text.Append('\u0001');
                if (!(mt.stdSerif && mt.mps.paraZeroMargin)) mt.text.Append('\u0001');
            }
        }
        else if (tag is "span" && mt.whiteSpans.Count > 0)
        {
            if (mt.whiteSpans.Pop()) mt.mps.whiteDepth = Math.Max(0, mt.mps.whiteDepth - 1);
            if (mt.floatSpans.Count > 0 && mt.floatSpans.Pop()) mt.floatDepth = Math.Max(0, mt.floatDepth - 1);
            // report cells: the span's typography ends here - and a UA cell's span styles what it
            // CONTAINS (probed on the cheque: an 8 px span's neighbour draws at the cell's 14 px)
            if ((mt.reportCells || mt.stdSerif) && mt.spanSaves.Count > 0 && mt.mps.cell is not null)
                (mt.mps.cell.FontSize, mt.mps.cell.Face, mt.mps.cell.Bold, mt.mps.cell.Fore) = mt.spanSaves.Pop();

        }
    }
}

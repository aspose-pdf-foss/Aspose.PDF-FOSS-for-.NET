using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A report cell's paragraph opens its own segment, carrying the sheet's and the tag's own typography onto it.</summary>
    private static void OpenReportCellParagraph(MetricParseState mps, Token tok, StringBuilder text, IReadOnlyDictionary<string, Dictionary<string, string>> css, bool stdSerif, bool reportCells)
    {
        if (reportCells && !mps.collapsedGrid && mps.cell is not null)
        {
            if (mps.curSeg is null && text.Length > 0)
            {
                var leadStr = text.ToString();
                var leadMarkers = string.Concat(
                    from Match lm in Regex.Matches(leadStr, "\u0002\\d+\u0003")
                    select lm.Value);
                leadStr = Regex.Replace(leadStr, "\u0002\\d+\u0003", " ");
                text.Clear();
                text.Append(leadMarkers);
                if (CollapseWs(leadStr).Trim(' ').Length > 0)
                {
                    // the lead draws with the typography its first
                    // ink SAW — its spans have closed and restored
                    // the cell state by now
                    mps.curSeg = new MetricDivSeg
                    {
                        FontSize = mps.leadSeen ? mps.leadFs : mps.cell.FontSize,
                        Face = mps.leadSeen ? mps.leadFace : mps.cell.Face,
                        Bold = mps.leadSeen ? mps.leadBold : mps.cell.Bold,
                        Fore = mps.leadSeen ? mps.leadFore : mps.cell.Fore,
                    };
                    mps.divText.Append(leadStr);
                    CloseSeg(mps, text, reportCells, stdSerif);
                }
                mps.leadSeen = false;
            }
            CloseSeg(mps, text, reportCells, stdSerif);
            FlushUaBlockGridMarkers(mps, text);
            mps.curSeg = new MetricDivSeg();
            if (mps.uaBlockCells) ReadUaBlockStyle(mps.curSeg, tok, mps.cell.FontSize ?? mps.fontSize);
            // (the paragraph's own align attribute aligns its lines, whatever the cell's: the safety
            // data sheet's `<p align="center">` inside a `<td align="left">` centres its title)
            if (stdSerif && tok.Attributes is { } paA && paA.TryGetValue("align", out var paAlign) && paAlign is not null)
            {
                var pal = paAlign.Trim().ToLowerInvariant();
                mps.curSeg.AlignCenter = pal == "center";
                mps.curSeg.AlignRight = pal == "right";
            }
            // the paragraph's class authors its own margins
            // (`margin: 0pt …`) — they replace the UA block margins
            if (tok.Attributes is { } pMa
                && pMa.TryGetValue("class", out var pMCls) && pMCls is not null)
                foreach (var pmc in pMCls.Split(' ',
                    StringSplitOptions.RemoveEmptyEntries))
                    if (css.TryGetValue("." + pmc, out var pmr)
                        && pmr.TryGetValue("margin", out var pmv))
                    {
                        var pmParts = pmv.Trim().Split(' ',
                            StringSplitOptions.RemoveEmptyEntries);
                        if (pmParts.Length > 0)
                        {
                            mps.curSeg.MarginsExplicit = true;
                            mps.curSeg.MarginTopPt = TryParseLength(
                                pmParts[0]) is { } pmT ? pmT : 0;
                            var pmBi = pmParts.Length >= 3 ? 2 : 0;
                            mps.curSeg.MarginBottomPt = TryParseLength(
                                pmParts[pmBi]) is { } pmB ? pmB : 0;
                        }
                    }
        }
    }
}

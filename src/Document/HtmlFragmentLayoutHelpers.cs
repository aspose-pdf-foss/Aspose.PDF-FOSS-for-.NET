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
// The framed-block helpers of the HTML fragment layout: opening and closing frame spans.
    // Open every frame that starts inside [from, to) and close every one
    // that ends there. A frame is honoured only when nothing renderable
    // precedes its open tag in its own chunk — the box top is the flow
    // cursor at the chunk boundary, so text before it would sit inside a
    // box that has not begun.
    private static void HtmlFramesOpening(HtmlFragmentLayoutState hl, int from, int to, string chunkText)
    {
        for (var fi = 0; fi < hl.htmlFrames.Count; fi++)
        {
            var f = hl.htmlFrames[fi];
            if (f.Start < from || f.Start >= to) continue;
            var before = hl.htmlContent.Substring(from, f.Start - from);
            if (HtmlFragment.StripHtmlTags(before).Trim().Length > 0) continue;
            _ = chunkText;
            hl.htmlOpenFrames.Add((fi, hl.flow.CurrentSlot, hl.flow.CurrentY));
            // The content starts below the top border and its padding,
            // and clear of the left border.
            hl.flow.AdvanceY(f.BorderWidthPt + f.PadTopPt);
            hl.htmlFrameIndent += f.BorderWidthPt;
        }
    }

    private static void HtmlFramesClosing(HtmlFragmentLayoutState hl, int from, int to)
    {
        for (var oi = hl.htmlOpenFrames.Count - 1; oi >= 0; oi--)
        {
            var open = hl.htmlOpenFrames[oi];
            var f = hl.htmlFrames[open.Index];
            if (f.End <= from || f.End > to) continue;
            hl.flow.DrawFrameBox(open.Slot, open.Top, hl.flow.CurrentY,
                f.BorderWidthPt, f.BorderColor);
            hl.htmlFrameIndent -= f.BorderWidthPt;
            hl.htmlOpenFrames.RemoveAt(oi);
        }
    }
}

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
    /// <summary>Render a recognised step-list (a single <c>ul</c> whose items nest heading /
    /// paragraph blocks) with browser-style HTML layout: embedded serif faces,
    /// pixel-quantized CSS line boxes, UA block margins, pair-kerned runs split at inline-bold
    /// edges, and a real bullet marker. All metrics exact to 4 decimals.
    /// Returns false without emitting anything when the serif faces are unavailable, the flow
    /// has already page-broken, or the list does not fit the remaining page space — the caller
    /// then falls back to the legacy flat flow.</summary>
    private static bool RenderHtmlStepList(List<Converters.HtmlToPdfConverter.StepListItem> items,
        FlowLayout flow, double marginLeft, double marginRight, Color? htmlColor)
    {
        var hs = new HtmlStepListState();
        hs.items = items;
        hs.flow = flow;
        hs.marginLeft = marginLeft;
        hs.marginRight = marginRight;
        hs.htmlColor = htmlColor;
        if (hs.flow.HasOverflowed) return false;
        if (!LoadStepListFaces(hs)) return false;

        hs.pageW = hs.flow.CurrentPage.Width;
        hs.ulMargin = 1.12 * hs.em;                 // ul top/bottom margin
        hs.liLeft = hs.marginLeft + 30.0;           // ul default padding-left: 40px per level

        hs.runsOut = new List<(double yDown, double x, string text, bool bold, double size)>();
        hs.bulletsOut = new List<(double yDown, double x)>();
        hs.yDown = 0.0;
        hs.pendingMargin = hs.ulMargin;             // collapses (max) with the next block's own

        foreach (var item in hs.items)
        {
            LayoutStepListItem(hs, item);
        }
        hs.totalH = hs.yDown + hs.ulMargin;
        if (hs.runsOut.Count == 0 || hs.flow.CurrentY - hs.totalH < hs.flow.BottomMargin) return false;

        hs.csb = new Content.ContentStreamBuilder();
        if (hs.htmlColor is not null) hs.csb.SetFillColor(hs.htmlColor);
        hs.fontDict = Table.ResolvePageFontDict(hs.flow.CurrentPage);

        foreach (var (by, bx) in hs.bulletsOut)
            EmitRun(hs, "•", false, hs.em, bx, hs.flow.CurrentY - by);
        foreach (var (ry, rx, text, bold, size) in hs.runsOut)
            EmitRun(hs, text, bold, size, rx, hs.flow.CurrentY - ry);

        hs.flow.InjectContentAtCursor(hs.csb.Build());
        hs.flow.AdvanceY(hs.totalH);
        return true;
    }

    /// <summary>Load the UA serif faces and their line metrics into <paramref name="hs"/>;
    /// false when a face is unavailable or unparsable.</summary>
    private static bool LoadStepListFaces(HtmlStepListState hs)
    {
        byte[]? regTtf;
        byte[]? boldTtf;
        try
        {
            regTtf = Text.FontRepository.GetTtfData("Times New Roman");
            boldTtf = Text.FontRepository.GetTtfData("Times New Roman Bold");
        }
        catch { return false; }
        if (regTtf is null || boldTtf is null) return false;
        hs.regTtf = regTtf;
        hs.boldTtf = boldTtf;

        try
        {
            hs.tp = new Text.TrueTypeParser(hs.regTtf);
            hs.tp.Parse();
            hs.gpReg = new Text.GlyphOutlineParser(hs.regTtf);
            hs.gpBold = new Text.GlyphOutlineParser(hs.boldTtf);
        }
        catch { return false; }
        if (hs.tp.UnitsPerEm <= 0 || hs.tp.UsWinAscent <= 0) return false;

        hs.upm = hs.tp.UnitsPerEm;
        hs.winAsc = hs.tp.UsWinAscent;
        hs.winDesc = hs.tp.UsWinDescent;
        hs.hheaSum = hs.tp.Ascent + System.Math.Abs(hs.tp.Descent) + hs.tp.LineGap;
        hs.em = 12.0;                    // HTML default body size (16px)

        return true;
    }
}

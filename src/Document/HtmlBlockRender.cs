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
    private void RenderHtmlBlocks(string chunk, HtmlFragment html, FlowLayout flow, Page page,
        Text.TextBuilder tb, Color? htmlColor, List<byte[]> inlineSvgs,
        HtmlFragmentLayoutState hl, double htmlFrameIndent,
        double marginLeft, double marginRight, double marginTop)
    {
        var hb = new HtmlBlockRenderState();
        // The flag belongs to the fragment above; the chunk state carries it while blocks render.
        hb.htmlFragmentLinkEmitted = hl.htmlFragmentLinkEmitted;
        hb.chunk = chunk;
        hb.html = html;
        hb.flow = flow;
        hb.page = page;
        hb.tb = tb;
        hb.htmlColor = htmlColor;
        hb.inlineSvgs = inlineSvgs;
        hb.htmlFrameIndent = htmlFrameIndent;
        hb.marginLeft = marginLeft;
        hb.marginRight = marginRight;
        hb.marginTop = marginTop;
        hb.bodyFs = hb.html.TextState is { FontSizeTouched: true } bts && bts.FontSize > 0
            ? (double)bts.FontSize : 0;
        hb.bodyCss = Converters.HtmlToPdfConverter.BodyCssFont(hb.chunk);
        if (hb.bodyFs <= 0 && hb.bodyCss.SizePt > 0) hb.bodyFs = hb.bodyCss.SizePt;
        hb.bodyCssFace = hb.html.TextState?.Font is null
            && string.IsNullOrEmpty(hb.html.TextState?.FontName)
            && hb.bodyCss.Face is { Length: > 0 } bcf
            ? SafeFindFont(bcf) : null;
        if (hb.bodyCssFace?.SourceFontData?.TtfData is not { Length: > 0 }) hb.bodyCssFace = null;
        hb.uaDefaultFace = UaDefaultFaceFor(hb.html, page, hb.bodyFs, hb.bodyCssFace);
        if (hb.uaDefaultFace is not null) hb.bodyFs = UaSerifPt;
        hb.blocks = Converters.HtmlToPdfConverter.ParseHtmlBlocks(
            hb.chunk, hb.bodyFs, inlineEmphasisRuns: true, paragraphMargins: hb.html.IsParagraphHasMargin);
        hb.paragraphMargins = hb.html.IsParagraphHasMargin;
        hb.pendingMarginBottom = 0;
        hb.bodyBgStartSlot = hb.flow.CurrentSlot;
        hb.savedMinLines = hb.flow.MinLinesPerPage;
        hb.flow.MinLinesPerPage = 2;
        hb.legacyFace = null;
        hb.legacyDialect = false;
        foreach (var b in hb.blocks)
            if (b.LegacyFontSized && b.FontFamily is { Length: > 0 } fam0)
            {
                var f0 = SafeFindFont(fam0);
                if (f0?.SourceFontData?.TtfData is { Length: > 0 })
                { hb.legacyFace = f0; hb.legacyDialect = true; break; }
            }
        for (hb.blockIndex = 0; hb.blockIndex < hb.blocks.Count; hb.blockIndex++)
        {
            if (!RenderHtmlBlock(hb, hb.blocks[hb.blockIndex])) break;
        }
        hb.flow.MinLinesPerPage = hb.savedMinLines;
        if (hb.bodyCss.BgColor is { } bodyBg) hb.flow.QueueBodyBackground(hb.bodyBgStartSlot, bodyBg);
        hb.flow.FlushBackgroundFills();
        // Draw this chunk's <img> elements in-flow (per segment), so a
        // logo lands at its position rather than after all content.
        RenderHtmlImages(hb.chunk, hb.flow, hb.marginLeft, hb.marginRight, hb.inlineSvgs);
        hl.htmlFragmentLinkEmitted = hb.htmlFragmentLinkEmitted;
    }

    /// <summary>The UA serif a full-document fragment (rooted at <c>&lt;html&gt;</c> or
    /// <c>&lt;body&gt;</c>) draws in when it names no face and no size — on itself, on its
    /// markup and on the page's default text state — as the converter's plain UA page does
    /// (measured: Times New Roman 12 pt on the body-rooted line-height fragment and on the
    /// html-wrapped free-text one). Null for a bare fragment, which keeps the Standard-14
    /// default its expected renders carry, and for one that names anything.</summary>
    private Text.Font? UaDefaultFaceFor(HtmlFragment html, Page page, double bodyFs, Text.Font? bodyCssFace)
    {
        if (html.TextState?.Font is not null || !string.IsNullOrEmpty(html.TextState?.FontName)
            || bodyCssFace is not null || bodyFs > 0 || DefaultTextStateFace(page) is not null)
            return null;
        if (!System.Text.RegularExpressions.Regex.IsMatch(html.HtmlContent ?? "",
                @"\A\s*(?:<!--.*?-->\s*)*<(?:html|body)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline))
            return null;
        var face = SafeFindFont("Times New Roman");
        return face?.SourceFontData?.TtfData is { Length: > 0 } ? face : null;
    }
}

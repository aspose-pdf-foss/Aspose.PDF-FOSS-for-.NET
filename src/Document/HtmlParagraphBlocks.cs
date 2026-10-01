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
    // Render a single-font inline-emphasis fragment (one <font face size>
    // wrapper holding only b/u/i runs) as embedded styled runs with
    // stroked underlines, on the half-leading line model: for a run of
    // s px, box top/bottom = hhea ascent/descent plus half of
    // (round(lineHeight px) - ascent - descent), maxed against the
    // 16px serif strut; the first baseline sits `above` under the
    // cursor and lines advance by (above + below).
    private bool RenderInlineEmphasisRuns(string iface, double ipt, List<(string text, bool bold, bool underline, bool italic)> iruns, FlowLayout flow, Page page, double marginLeft, double marginRight)
    {
        var ie = new InlineEmphasisRunsState();
        ie.iface = iface;
        ie.ipt = ipt;
        ie.iruns = iruns;
        ie.flow = flow;
        ie.page = page;
        ie.marginLeft = marginLeft;
        ie.marginRight = marginRight;
        ie.regTtf = Text.FontRepository.GetTtfData(ie.iface);
        if (ie.regTtf is null) return false;
        ie.boldTtf = Text.FontRepository.GetTtfData(ie.iface + " Bold") ?? ie.regTtf;
        ie.regData = new Text.FontData(ie.iface, Text.FontType.TrueType);
        ie.regData.SetTtfData(ie.regTtf);
        ie.boldData = new Text.FontData(ie.iface + " Bold", Text.FontType.TrueType);
        ie.boldData.SetTtfData(ie.boldTtf);
        ie.mReg = Text.TextPaginator.CreateMeasurer(ie.iface, ie.ipt, ie.regData);
        ie.mBold = Text.TextPaginator.CreateMeasurer(ie.iface, ie.ipt, ie.boldData);

        ie.sPx = ie.ipt / 0.75;
        ie.em = 2048.0;
        ie.faceAscent = 1854;
        ie.faceDescent = 434;
        ie.faceLineGap = 67;
        ie.ascPx = ie.faceAscent * ie.sPx / ie.em;
        ie.descPx = ie.faceDescent * ie.sPx / ie.em;
        ie.lPx = Math.Round(ie.sPx * (ie.faceAscent + ie.faceDescent + ie.faceLineGap) / ie.em,
            MidpointRounding.AwayFromZero);
        ie.halfLead = (ie.lPx - ie.ascPx - ie.descPx) / 2;
        ie.strutTop = 14.3984375;
        ie.strutBottom = 3.6015625;
        ie.above = Math.Max(ie.ascPx + ie.halfLead, ie.strutTop);
        ie.below = Math.Max(ie.descPx + ie.halfLead, ie.strutBottom);
        ie.firstBaselinePt = ie.above * 0.75;
        ie.linePitchPt = (ie.above + ie.below) * 0.75;

        ie.atoms = new List<(string text, bool bold, bool underline, bool space)>();
        TokeniseInlineEmphasisRuns(ie);

        ie.contentW = ie.page.Width - ie.marginLeft - ie.marginRight;
        ie.lines2 = new List<List<(string text, bool bold, bool underline, double x, double w)>>();
        ie.cur = new List<(string, bool, bool, double, double)>();
        ie.curW = 0;
        WrapInlineEmphasisAtoms(ie);
        if (ie.cur.Count > 0) ie.lines2.Add(ie.cur);
        if (ie.lines2.Count == 0) return false;

        ie.frameTop = ie.flow.CurrentY;
        ie.fontDict2 = Table.ResolvePageFontDict(ie.flow.CurrentPage);
        ie.b2 = new Content.ContentStreamBuilder();
        for (var li = 0; li < ie.lines2.Count; li++)
        {
            if (!EmitInlineEmphasisLine(ie, li)) break;
        }
        ie.flow.InjectContentAtCursor(ie.b2.Build());
        ie.blockH = (ie.ascPx + (ie.lines2.Count - 1) * (ie.above + ie.below) + ie.descPx) * 0.75;
        ie.flow.AdvanceY(ie.blockH);
        return true;
    }

    private void RenderUaSerifChunk(string chunk, double uaWrapPt, HtmlFragment html,
        FlowLayout flow, double marginLeft)
    {
        var uc = new UaSerifChunkState();
        uc.chunk = chunk;
        uc.uaWrapPt = uaWrapPt;
        uc.html = html;
        uc.flow = flow;
        uc.marginLeft = marginLeft;
        uc.uaBody = System.Text.RegularExpressions.Regex.Replace(uc.chunk,
            @"(?s)<head\b.*?</head>|<!--.*?-->", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        // only p/h elements and inline span/emphasis tags carry text;
        // structural tags (html, body, div, ...) leave the scan so their
        // names never surface as content
        uc.uaBody = System.Text.RegularExpressions.Regex.Replace(uc.uaBody,
            @"</(?!p\b|h[1-6]\b|span\b|strong\b|b\b|em\b|i\b|br\b)[^>]*>" +
            @"|<(?!/|p\b|h[1-6]\b|span\b|strong\b|b\b|em\b|i\b|br\b)[^>]*>", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        uc.uaB = new Content.ContentStreamBuilder();
        uc.uaB.SaveState();
        uc.uaTimes = Table.RegisterFont(uc.flow.CurrentPage, "Times-Roman");
        uc.uaTimesB = Table.RegisterFont(uc.flow.CurrentPage, "Times-Bold");
        uc.uaAfterHead = false;
        uc.uaAtBoxEdge = true;
        foreach (System.Text.RegularExpressions.Match em in
            System.Text.RegularExpressions.Regex.Matches(uc.uaBody,
                @"(?s)<(?<tag>p|h[1-6])\b[^>]*>(?<in>.*?)</\k<tag>>|<br\b[^>]*>|(?<bare>[^<]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            if (!RenderUaSerifElement(uc, em)) break;
        }
        uc.uaB.RestoreState();
        uc.flow.InjectContentAtCursor(uc.uaB.Build());
        // close the last line box so following content (a table)
        // starts at the box bottom, not the baseline
        if (!uc.uaAtBoxEdge) uc.flow.AdvanceY(UaSerifPitchPt - UaSerifSeatPt);
    }

}

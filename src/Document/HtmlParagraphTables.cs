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
    private double RenderLayoutTable(Table lt, double originX, double boxW, double startY,
        FlowLayout flow, double marginLeft, HashSet<Table> renderedTables, bool measureOnly = false)
    {
        var rl = new LayoutTableRenderState();
        rl.lt = lt;
        rl.originX = originX;
        rl.boxW = boxW;
        rl.startY = startY;
        rl.flow = flow;
        rl.marginLeft = marginLeft;
        rl.renderedTables = renderedTables;
        rl.measureOnly = measureOnly;
        rl.originLeft = rl.originX >= 0 ? rl.originX : rl.marginLeft;
        rl.lpadTop = rl.lt.DefaultCellPadding?.Top ?? 0;
        rl.lpadBottom = rl.lt.DefaultCellPadding?.Bottom ?? 0;
        rl.y = rl.startY;
        foreach (var lrow in rl.lt.Rows)
        {
            if (!RenderLayoutTableRow(rl, lrow)) break;
        }
        return rl.startY - rl.y;
    }

    // Render a real HTML <table> as a generator Table at the flow cursor,
    // paginating like a page-level Table paragraph (same logic as the
    // `para is Table` branch below).
    private void RenderHtmlTable(Table t, FlowLayout flow, Page page, double marginLeft, double marginTop,
        List<(byte[] content, double width, double height)> overflowPages,
        Dictionary<int, List<(byte[] data, Rectangle rect)>> overflowImages)
    {
        var tablePage = flow.CurrentPage;
        t.FlowLeftOffset = marginLeft;
        var spillTopMargin = PageInfo?.Margin is { TopTouched: true } dm ? dm.Top : marginTop;

        // Page-break-before: if the whole table doesn't fit in the space left
        // on the current page but would fit on a fresh one, move it to the next
        // page (keeps a table together — the common HTML expectation). Measure
        // its single-page height from the content top first.
        t.BuildMultiPage(tablePage, flow.ContentTop, flow.BottomMargin, measureOnly: true);
        var tableH = t.LastRenderedHeight;
        var avail = flow.CurrentY - flow.BottomMargin;
        var pageBudget = flow.ContentTop - flow.BottomMargin;
        // …but the form-grid dialect SPLITS a section table
        // instead (the band row stays on the page foot,
        // the header/data rows continue overleaf).
        if (tableH > avail + 0.5 && tableH <= pageBudget + 0.5
            && flow.CurrentY < flow.ContentTop - 0.5
            && !t.HonorCellTtfFaces)
            flow.ForceNewPage();

        var pageContents = t.BuildMultiPage(tablePage, flow.CurrentY, flow.BottomMargin, spillTopMargin,
            contentFlow: true);
        var tableImages = t.LastImageDraws;
        var tableGraphs = t.LastGraphDraws;
        // Inject the first slice at the flow's CURRENT page position (the start
        // page, or the current overflow buffer once the flow has page-broken) —
        // NOT directly on the start page, which is where the cursor no longer is.
        flow.InjectContentAtCursor(pageContents[0]);
        if (tableGraphs.Count > 0)
            foreach (var gc in tableGraphs[0])
                flow.InjectContentAtCursor(gc);
        // Cell images: drawn on the live start page (only correct before the flow
        // overflows — overflowed cell images are rare and out of scope here).
        if (!flow.HasOverflowed && tableImages.Count > 0)
            foreach (var (data, rect) in tableImages[0])
                tablePage.AddImage(data, rect);
        if (pageContents.Count == 1)
        {
            flow.AdvanceY(t.LastRenderedHeight);
        }
        else
        {
            for (var pi = 1; pi < pageContents.Count - 1; pi++)
            {
                if (pi < tableImages.Count && tableImages[pi].Count > 0)
                    overflowImages[overflowPages.Count] = tableImages[pi];
                overflowPages.Add((pageContents[pi], tablePage.Width, tablePage.Height));
            }
            var lastIdx = pageContents.Count - 1;
            var lastSlot = flow.ContinueOnPrebuiltSpill(pageContents[lastIdx], t.LastPageEndY);
            if (lastIdx < tableImages.Count && tableImages[lastIdx].Count > 0)
                overflowImages[lastSlot] = tableImages[lastIdx];
        }
    }

    private const System.Text.RegularExpressions.RegexOptions UaRx =
        System.Text.RegularExpressions.RegexOptions.IgnoreCase
        | System.Text.RegularExpressions.RegexOptions.Singleline;
    private const char UaFallbackChar = '�';
    private const string UaFallbackFamily = "Microsoft Sans Serif";

    private void RenderUaSerifTable(string chunk, double uaBoxPt, FlowLayout flow, Page page,
        double marginLeft)
    {
        var ua = new UaSerifTableState();
        ua.chunk = chunk;
        ua.uaBoxPt = uaBoxPt;
        ua.flow = flow;
        ua.page = page;
        ua.marginLeft = marginLeft;
        ua.twm = System.Text.RegularExpressions.Regex.Match(ua.chunk,
            @"<table\b[^>]*\bwidth\s*=\s*[""']?(\d+)(?![\d%])", UaRx);
        ua.tableW = ua.twm.Success
            ? double.Parse(ua.twm.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture) * 0.75
            : ua.uaBoxPt;
        ua.zeroPad = System.Text.RegularExpressions.Regex.IsMatch(ua.chunk,
            @"<table\b[^>]*\bcellpadding\s*=\s*[""']?0[""']?", UaRx);
        ua.pad = ua.zeroPad ? 0.0 : UaTdPadPt;

        ua.colWs = new List<double>();
        foreach (System.Text.RegularExpressions.Match cm in
            System.Text.RegularExpressions.Regex.Matches(ua.chunk,
                @"<col\b[^>]*width\s*:\s*([\d.]+)pt", UaRx))
            ua.colWs.Add(double.Parse(cm.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture));
        if (ua.colWs.Count == 0)
            foreach (System.Text.RegularExpressions.Match cm in
                System.Text.RegularExpressions.Regex.Matches(ua.chunk,
                    @"<td\b[^>]*width\s*:\s*([\d.]+)%", UaRx))
                ua.colWs.Add(ua.tableW * double.Parse(cm.Groups[1].Value,
                    System.Globalization.CultureInfo.InvariantCulture) / 100.0);
        if (ua.colWs.Count == 0) return;

        ua.faces = new Dictionary<string,
            (byte[] Ttf, string Name, Text.GlyphOutlineParser Gp, Text.TrueTypeParser Tp)>();
        // the paste's U+FFFD stays as-is; it draws
        // through the system fallback face, which carries the
        // replacement-character glyph

        ua.fontDict = Table.ResolvePageFontDict(ua.flow.CurrentPage);
        ua.uaTimes2 = Table.RegisterFont(ua.flow.CurrentPage, "Times-Roman");
        ua.tb = new Content.ContentStreamBuilder();
        ua.tb.SaveState();
        ua.topD = ua.page.Height - ua.flow.CurrentY;
        ua.totalH = 0.0;

        foreach (System.Text.RegularExpressions.Match rm in
            System.Text.RegularExpressions.Regex.Matches(ua.chunk,
                @"<tr(?<a>[^>]*)>(?<in>.*?)</tr>", UaRx))
        {
            if (!RenderUaRow(ua, rm)) break;
        }
        ua.tb.RestoreState();
        ua.flow.InjectContentAtCursor(ua.tb.Build());
        ua.flow.AdvanceY(ua.totalH);
    }
}

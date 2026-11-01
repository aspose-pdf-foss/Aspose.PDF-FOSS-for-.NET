using System.Globalization;
using System.Text;
using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

internal static partial class XfaRenderer
{
    /// <summary>XFA render bodies: one body box placed on the flowing pages.</summary>
    private static bool RenderBody(XfaRenderState xr, XmlElement body)
    {
        double h = Height(xr.ctx, body, xr.areas[xr.ai].w);
        // An empty trailing body (Designer end-of-form marker) renders nothing and
        // must not force a page even when it carries a breakBefore.
        if (h <= 0.5) return true;
        // A y-positioned decorative draw overlays the current position without
        // consuming flow height (same rule as inside container flows).
        if (IsFloating(body))
        {
            Place(xr.ctx, body, xr.areas[xr.ai].x + Len(body.GetAttribute("x"), 0),
                  xr.areas[xr.ai].y + xr.used + Len(body.GetAttribute("y"), 0), xr.rootPath,
                  xr.areas[xr.ai].w - Len(body.GetAttribute("x"), 0));
            return true;
        }
        // A breakBefore acts only when it names a pageArea or requests a new page;
        // Designer also emits EMPTY <breakBefore/> placeholders that are no-ops.
        var brk = body.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == "breakBefore");
        ApplyBreakBefore(xr, body, brk!);
        xr.overflowMaster = OverflowTarget(body, xr.pageAreas);
        (xr.overflowLeader, xr.leaderIndent, xr.leaderPath) = (OverflowLeaderOf(xr, body), 0, xr.rootPath);
        // A flow body splits between its rows mid-page (keep=none fills the page
        // bottom) in two cases: it is substantially taller than a WHOLE content
        // area (it could never fit any single area), or it fits a whole area but
        // not the remaining space AND its first row does — then the rows that fit
        // stay at the page bottom and the rest continues on the next page. A body
        // in the small-overflow band just above a whole area (≤20pt over) advances
        // instead — a viewer places it whole, tolerating the overflow, rather than
        // breaking it up.
        double maxAreaH = xr.areas.Max(a => a.h);
        bool fillsBottom = h > xr.areas[xr.ai].h - xr.used + 0.1 && h <= maxAreaH + 0.1
            && Boxes(body).FirstOrDefault(c => !IsFloating(c)) is { } firstRow
            && Height(xr.ctx, firstRow, xr.areas[xr.ai].w) <= xr.areas[xr.ai].h - xr.used + 0.1;
        // A body that wraps its boxes into rows fills the page bottom the same way; one
        // taller than a whole area is flowed from the top of a fresh area, further down.
        var bodyLayout = body.GetAttribute("layout");
        if ((h > maxAreaH + 20 || fillsBottom) && xr.used > 0 && Splittable(body)
            && (bodyLayout is "tb" or "table" || (fillsBottom && bodyLayout == "lr-tb")))
        {
            if (System.Environment.GetEnvironmentVariable("XFA_PAGES") is not null)
                System.Console.Error.WriteLine($"BODYSPLIT\t{body.GetAttribute("name")}\th={h:F1}\tpage={xr.newPages.Count}\tused={xr.used:F1}");
            FlowRows(xr, body, 0, 0, xr.rootPath);
            xr.pageFresh = false;
            return true;
        }
        while (h > xr.areas[xr.ai].h - xr.used + 0.1 && !(xr.used == 0 && h > xr.areas[xr.ai].h))
            NextArea(xr, overflow: false);
        if (System.Environment.GetEnvironmentVariable("XFA_PAGES") is not null)
            System.Console.Error.WriteLine($"BODY\t{body.GetAttribute("name")}\th={h:F1}\tpage={xr.newPages.Count}\tarea={xr.ai}\tused={xr.used:F1}\tareaH={xr.areas[xr.ai].h:F1}");
        if (h > xr.areas[xr.ai].h + 20 && xr.used == 0 && body.GetAttribute("layout") == "lr-tb"
            && FlowContainer(body) && Boxes(body).Any())
        {
            // An over-tall lr-tb body flows its wrapped rows across content
            // areas / continuation pages, same as a tb/table body below —
            // placing it whole would clip everything past the first area.
            FlowRows(xr, body, 0, 0, xr.rootPath);
            xr.pageFresh = false;
            return true;
        }
        PlaceOverflowBody(xr, body, h);
        xr.pageFresh = false;
        return true;
    }

    /// <summary></summary>
    private static void PlaceOverflowBody(XfaRenderState xr, XmlElement body, double h)
    {
        if (h > xr.areas[xr.ai].h + 20 && xr.used == 0 && body.GetAttribute("layout") is "tb" or "table")
        {
            // A flow body substantially taller than a whole content area SPLITS: its
            // top-level children flow across content areas / continuation pages
            // (over-tall subforms paginate inside rather than clipping).
            // A small overshoot (≤20pt) stays on one page — coarse Height() rounding
            // must not force a page for content that belongs together.
            var (mtB, _, mlB, mrB) = Margins(body);
            xr.used += mtB;
            // The children are placed one by one, so the body's own segment goes into their paths here.
            var bodyName = body.GetAttribute("name");
            var bodyPath = bodyName.Length > 0 ? $"{xr.rootPath}.{bodyName}[{SiblingIndex(body)}]" : xr.rootPath;
            foreach (var c in Boxes(body).ToList())
            {
                if (IsFloating(c))
                {
                    Place(xr.ctx, c, xr.areas[xr.ai].x + mlB + Len(c.GetAttribute("x"), 0),
                          xr.areas[xr.ai].y + xr.used + Len(c.GetAttribute("y"), 0), bodyPath,
                          xr.areas[xr.ai].w - mlB - Len(c.GetAttribute("x"), 0));
                    continue;
                }
                double ch = Height(xr.ctx, c, xr.areas[xr.ai].w - mlB - mrB);
                if (ch > MinFlowHeight) ApplyNestedBreakBefore(xr, c);
                if (System.Environment.GetEnvironmentVariable("XFA_PAGES") is not null)
                    System.Console.Error.WriteLine($"CHILD\t{c.GetAttribute("name")}\tch={ch:F1}\tpage={xr.newPages.Count}\tused={xr.used:F1}");
                if (OverTallSplittable(xr, c, ch)
                    || (ch > xr.areas[xr.ai].h - xr.used + 0.1 && !(AtAreaTop(xr) && ch > xr.areas[xr.ai].h - xr.used) && Splittable(c)))
                {
                    FlowRows(xr, c, mlB, mrB, bodyPath);
                    continue;
                }
                bool sliced = OverTallLeaf(xr, c, ch);
                while (ch > xr.areas[xr.ai].h - xr.used + 0.1 && !(AtAreaTop(xr) && ch > xr.areas[xr.ai].h - xr.used)
                       && !(sliced && RoomToStartSlicing(xr, c)))
                    NextArea(xr);
                if (sliced)
                {
                    PlaceSliced(xr, c, xr.areas[xr.ai].x + mlB, bodyPath, xr.areas[xr.ai].w - mlB - mrB, ch);
                    continue;
                }
                Place(xr.ctx, c, xr.areas[xr.ai].x + mlB, xr.areas[xr.ai].y + xr.used, bodyPath, xr.areas[xr.ai].w - mlB - mrB);
                xr.used += ch;
            }
        }
        else
        {
            Place(xr.ctx, body, xr.areas[xr.ai].x, xr.areas[xr.ai].y + xr.used, xr.rootPath, xr.areas[xr.ai].w);
            xr.used += h;
        }
    }

    /// <summary></summary>
    private static void ApplyBreakBefore(XfaRenderState xr, XmlElement body, XmlElement brk)
    {
        if (brk is not null)
        {
            var target = BreakTarget(body, xr.pageAreas);
            var startNew = brk.GetAttribute("startNew") == "1";
            if (target is not null || startNew)
            {
                var switched = target is not null && !ReferenceEquals(target, xr.master);
                if (target is not null)
                {
                    xr.master = target;
                    xr.masterIdx = Math.Max(0, xr.pageAreas.IndexOf(target));
                    // A break to the master already in effect (Designer stamps one
                    // on the entry body) must not restart its occur count - that
                    // would pin the ordered progression to it forever.
                    if (switched) xr.pagesOnMaster = 0;
                }
                // A CONTENT-AREA-targeted break on the SAME master moves the flow
                // into that area - on the CURRENT page while the area is unused
                // there (a multi-area page's bodies lay into their
                // own areas of ONE page, startNew notwithstanding); a fresh page
                // only when the area is already consumed.
                var caName = brk is null ? null : BreakContentAreaName(brk);
                var caIdx = caName is null ? -1 : xr.areas.FindIndex(a => a.name == caName);
                if (!switched && caIdx >= 0)
                {
                    var consumed = caIdx < xr.ai || (caIdx == xr.ai && xr.used > 0);
                    if (consumed && !xr.pageFresh) NewPage(xr);
                    var landIdx = xr.areas.FindIndex(a => a.name == caName);
                    if (landIdx >= 0) { xr.ai = landIdx; xr.used = 0; }
                }
                else
                {
                    if (!xr.pageFresh) NewPage(xr);
                    else if (switched) { xr.newPages.RemoveAt(xr.newPages.Count - 1); NewPage(xr); }
                    if (caName is not null)
                    {
                        var landIdx = xr.areas.FindIndex(a => a.name == caName);
                        if (landIdx >= 0) { xr.ai = landIdx; xr.used = 0; }
                    }
                }
            }
        }
    }
}

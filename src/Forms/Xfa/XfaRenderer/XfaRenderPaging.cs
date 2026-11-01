using System.Globalization;
using System.Text;
using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

internal static partial class XfaRenderer
{
// XFA render paging helpers: occurrence caps, master advance, new page, split rules and row flow.
    private static int OccurMax(XmlElement pa)
    {
        var oc = pa.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == "occur");
        // A page area that declares no occurrence limit serves as many pages as flow onto
        // it: the flow leaves it by an overflow target or a break, not by a count.
        if (oc is null) return int.MaxValue;
        var maxS = oc.GetAttribute("max");
        if (string.IsNullOrEmpty(maxS)) return 1;
        if (maxS == "-1") return int.MaxValue;
        return int.TryParse(maxS, NumberStyles.Integer, CultureInfo.InvariantCulture, out var m) && m > 0 ? m : 1;
    }

    /// <summary>The page area a container sends its overflow to (&lt;overflow target&gt;, or
    /// the older &lt;break overflowTarget&gt;, as "PageArea.ContentArea", a bare area name or
    /// an #id), or null.</summary>
    private static XmlElement? OverflowTarget(XmlElement c, List<XmlElement> pageAreas)
    {
        var name = FirstChild(c, "overflow")?.GetAttribute("target").TrimStart('#') ?? string.Empty;
        if (name.Length == 0) name = FirstChild(c, "break")?.GetAttribute("overflowTarget").TrimStart('#') ?? string.Empty;
        if (name.Length == 0) return null;
        var dot = name.IndexOf('.');
        var areaName = dot > 0 ? name.Substring(0, dot) : name;
        var contentName = dot > 0 ? name.Substring(dot + 1) : name;
        return pageAreas.FirstOrDefault(p => p.GetAttribute("name") == areaName || p.GetAttribute("id") == areaName)
            ?? pageAreas.FirstOrDefault(p => p.ChildNodes.OfType<XmlElement>().Any(k => k.LocalName == "contentArea"
                && (k.GetAttribute("name") == contentName || k.GetAttribute("id") == contentName)));
    }

    private static void AdvanceMaster(XfaRenderState xr)
    {
        // The container being flowed names where its overflow goes.
        if (xr.overflowMaster is not null)
        {
            if (!ReferenceEquals(xr.overflowMaster, xr.master))
            {
                xr.master = xr.overflowMaster;
                xr.masterIdx = Math.Max(0, xr.pageAreas.IndexOf(xr.master));
                xr.pagesOnMaster = 0;
            }
            return;
        }
        if (xr.pagesOnMaster >= OccurMax(xr.master) && xr.masterIdx + 1 < xr.pageAreas.Count)
        {
            xr.masterIdx++;
            xr.master = xr.pageAreas[xr.masterIdx];
            xr.pagesOnMaster = 0;
        }
    }

    private static void NewPage(XfaRenderState xr)
    {
        (xr.pw, xr.ph) = MasterMedium(xr.master);
        xr.areas = MasterContentAreas(xr.master, xr.pw, xr.ph);
        var items = new List<Item>();
        xr.ctx = new Ctx
        {
            PageH = xr.ph, RawValue = xr.rawValue, Items = items, Groups = xr.groups, Images = xr.xfaImages,
            IdElements = xr.idElements, DataRoot = xr.dataRoot, PageNum = xr.newPages.Count + 1,
            StrictBinding = xr.formRoot is null, PageScripts = xr.pageScripts,
        };
        if (xr.placeFields)
        {
            var (masterPrefix, masterName) = MasterBasePath(xr.master, xr.root!.GetAttribute("name") + "[0]");
            (xr.ctx.PageW, xr.ctx.Placements) = (xr.pw, new List<XfaFieldPlacement>());
            xr.placements[items] = (xr.ctx.Placements, masterName.Length == 0 ? string.Empty : masterPrefix + "." + masterName);
        }
        // The master is painted once the pages are counted: what it shows may depend on the page's number.
        xr.pageMasters[items] = (xr.master, xr.ctx);
        xr.newPages.Add((xr.pw, xr.ph, items));
        if (System.Environment.GetEnvironmentVariable("XFA_PAGES") is not null)
            System.Console.Error.WriteLine($"NEWPAGE\tpage={xr.newPages.Count}\tmaster={xr.master.GetAttribute("name")}\tidx={xr.masterIdx}\tonMaster={xr.pagesOnMaster}");
        xr.pagesOnMaster++;
        xr.ai = 0; xr.used = 0;
        xr.areaStart = 0;
        xr.pageFresh = true;
    }

    /// <summary>Paints every page's master once the pages are counted: a master box whose presence a script decides
    /// from the page's number (a block shown on the last page only) is resolved for each page before it is painted,
    /// and the master's fields are named under the page area and the count of pages it served before this one.</summary>
    private static void PaintMasters(XfaRenderState xr)
    {
        var rootPath = xr.root!.GetAttribute("name") + "[0]";
        for (int i = 0; i < xr.newPages.Count; i++)
        {
            var (pw, _, items) = xr.newPages[i];
            if (!xr.pageMasters.TryGetValue(items, out var page)) continue;
            xr.pageScripts?.ResolveMasterOn(i + 1, xr.newPages.Count);
            var ctx = page.ctx;
            if (xr.placeFields) (ctx.MasterPrefix, ctx.MasterName) = MasterBasePath(page.master, rootPath);
            var under = new List<Item>();
            PaintMaster(xr, page.master, ctx, pw, items, under);
            ctx.MasterPrefix = null;
            items.InsertRange(0, under);
        }
    }

    /// <summary>Paints the master's boxes in their document order with the body at the content area's
    /// place: what the master declares before its (first) content area goes under the body, what it
    /// declares after follows the body (a banner declared last covers what it overlaps, a caption
    /// declared before it lies under it), and the values of the master's fields come last of all - a
    /// viewer shows them as widgets over the page. The two later parts wait in the page's tail lists.</summary>
    private static void PaintMaster(XfaRenderState xr, XmlElement master, Ctx ctx, double pw, List<Item> items, List<Item> under)
    {
        var afterBody = new List<Item>();
        var values = new List<Item>();
        xr.pageTails[items] = (afterBody, values);
        ctx.ValueItems = values;
        bool pastContentArea = false;
        foreach (var c in master.ChildNodes.OfType<XmlElement>())
        {
            if (c.LocalName == "contentArea") { pastContentArea = true; continue; }
            if (!BoxTags.Contains(c.LocalName) || Hidden(c)) continue;
            ctx.Items = pastContentArea ? afterBody : under;
            Place(ctx, c, Len(c.GetAttribute("x"), 0), Len(c.GetAttribute("y"), 0), "", pw - Len(c.GetAttribute("x"), 0));
        }
        ctx.Items = items;
        ctx.ValueItems = null;
    }

    // A flow-container that cannot fit the remaining space SPLITS between its own
    // rows rather than forcing a fresh page (XFA's default keep=none): a viewer
    // fills the page bottom with the rows that fit and continues the rest on the
    // next page. Containers carrying <keep intact> stay whole.
    private static bool KeepIntact(XmlElement c) => c.ChildNodes.OfType<XmlElement>()
        .Any(k => k.LocalName == "keep" && k.GetAttribute("intact") is "contentArea" or "pageArea");

    // How far a box may overrun a whole content area and still be placed in one piece:
    // coarse height rounding must not break up content that belongs together.
    private const double OverTallSlack = 20;

    /// <summary>True for a flow container no content area of the page can hold: wherever
    /// it starts it runs off the area, so it is laid out row by row across areas and
    /// pages, however deep in the flow it sits. One box inside is enough here: it is the
    /// box that is too tall, and the row flow continues it. A request to keep the
    /// container whole cannot be met where no area holds it, and does not stop this.</summary>
    private static bool OverTallSplittable(XfaRenderState xr, XmlElement c, double h) =>
        h > xr.areas.Max(a => a.h) + OverTallSlack && FlowsItsBoxes(c) && Boxes(c).Any() && !WrapsFixedTable(c);

    /// <summary>True for a container whose single box is a table of a fixed set of rows:
    /// none of its rows repeats and it names nothing for an overflow. The designer drew
    /// such a table for the page it stands on, and it is placed in one piece; a table that
    /// grows with its data, or says how it continues, is flowed row by row.</summary>
    private static bool WrapsFixedTable(XmlElement c)
    {
        var only = Boxes(c).FirstOrDefault();
        if (only is null || Boxes(c).Skip(1).Any() || only.GetAttribute("layout") != "table") return false;
        if (FirstChild(only, "overflow") is not null || FirstChild(only, "break") is not null) return false;
        return !Boxes(only).Any(row => FirstChild(row, "occur") is not null);
    }

    private static bool FlowsItsBoxes(XmlElement c) =>
        c.LocalName == "subform" && c.GetAttribute("layout") is "tb" or "table" or "lr-tb";

    private static bool FlowContainer(XmlElement c) => FlowsItsBoxes(c) && !KeepIntact(c);

    /// <summary>True when a flow container that does not fit the room left in the current
    /// area can leave its first row there: it holds several boxes and the first fits, or
    /// it holds one box - a wrapper - and that box can do the same.</summary>
    private static bool SplitsInRoomLeft(XfaRenderState xr, XmlElement c, double w)
    {
        if (!FlowContainer(c)) return false;
        var first = Boxes(c).FirstOrDefault(k => !IsFloating(k));
        if (first is null) return false;
        if (Boxes(c).Skip(1).Any()) return Height(xr.ctx, first, w) <= xr.areas[xr.ai].h - xr.used + 0.1;
        return SplitsInRoomLeft(xr, first, w);
    }

    private static bool Splittable(XmlElement c) => FlowContainer(c) && Boxes(c).Count() > 1;

    // A box lower than this takes no room in the flow and starts no page.
    private const double MinFlowHeight = 0.5;

    /// <summary>A box nested in a flowing body that asks to start on a new page (breakBefore
    /// startNew), or on a page area other than the current one, gets one, on the page area it names if it names one, as a body of the
    /// form does. Nothing has flowed on a page whose first area is still unused: the box
    /// then starts there, and only a change of page area replaces that untouched page.</summary>
    private static void ApplyNestedBreakBefore(XfaRenderState xr, XmlElement c)
    {
        var brk = FirstChild(c, "breakBefore");
        if (brk is null) return;
        var target = BreakTarget(c, xr.pageAreas);
        bool switched = target is not null && !ReferenceEquals(target, xr.master);
        // Without startNew the box needs a new page only to reach a page area other than the current one.
        if (brk.GetAttribute("startNew") != "1" && !switched) return;
        bool flowed = !AtAreaTop(xr) || xr.ai > 0;
        if (!flowed && !switched) return;
        if (target is null) AdvanceMaster(xr);
        else
        {
            xr.master = target;
            xr.masterIdx = Math.Max(0, xr.pageAreas.IndexOf(target));
            if (switched) xr.pagesOnMaster = 0;
        }
        if (!flowed) xr.newPages.RemoveAt(xr.newPages.Count - 1);
        NewPage(xr);
    }

    /// <summary>Flows a container's rows; <paramref name="parentPath"/> is the SOM path of its parent, so a
    /// field's path keeps every named ancestor whichever container split the flow.</summary>
    private static void FlowRows(XfaRenderState xr, XmlElement c, double indentL, double indentR, string parentPath)
    {
        var outerOverflow = xr.overflowMaster;
        xr.overflowMaster = OverflowTarget(c, xr.pageAreas) ?? outerOverflow;
        var outerLeader = (xr.overflowLeader, xr.leaderIndent, xr.leaderPath);
        FlowContainerRows(xr, c, indentL, indentR, parentPath);
        xr.overflowMaster = outerOverflow;
        (xr.overflowLeader, xr.leaderIndent, xr.leaderPath) = outerLeader;
    }

    private static void FlowContainerRows(XfaRenderState xr, XmlElement c, double indentL, double indentR, string parentPath)
    {
        var (mt2, _, ml2, _) = Margins(c);
        var cname = c.GetAttribute("name");
        var p2 = cname.Length > 0 ? $"{parentPath}.{cname}[{SiblingIndex(c)}]" : parentPath;
        double cw = BoxW(c);
        if (cw <= 0) cw = xr.areas[xr.ai].w - indentL - indentR;
        if (OverflowLeaderOf(xr, c) is { } leader)
            (xr.overflowLeader, xr.leaderIndent, xr.leaderPath) = (leader, indentL + ml2, p2);
        xr.used += mt2;
        // Group children into visual rows: tb/table advance per child; lr-tb
        // accumulates children left-to-right until the width wraps.
        var rows = new List<List<XmlElement>>();
        if (c.GetAttribute("layout") == "lr-tb")
        {
            double xx = 0; List<XmlElement> row = new();
            foreach (var k in Boxes(c))
            {
                double kw = FlowWidth(xr.ctx, k);
                if (row.Count > 0 && xx + kw > cw + 0.5) { rows.Add(row); row = new(); xx = 0; }
                row.Add(k); xx += kw;
            }
            if (row.Count > 0) rows.Add(row);
        }
        else
            rows.AddRange(Boxes(c).Select(k => new List<XmlElement> { k }));
        foreach (var row in rows)
        {
            double rh = row.Max(k => Height(xr.ctx, k, cw));
            if (rh > MinFlowHeight) ApplyNestedBreakBefore(xr, row[0]);
            if (System.Environment.GetEnvironmentVariable("XFA_PAGES") is not null)
                System.Console.Error.WriteLine($"ROW\t{string.Join("+", row.Select(k => k.GetAttribute("name")))}\trh={rh:F1}\tpage={xr.newPages.Count}\tused={xr.used:F1}\tareaH={xr.areas[xr.ai].h:F1}");
            // A lone splittable subform row that overflows the remaining space
            // while its own first row still fits splits at the page bottom
            // (same fills-bottom rule as the top-level flow) instead of moving
            // whole to the next area.
            if (row.Count == 1 && (OverTallSplittable(xr, row[0], rh)
                || (rh > xr.areas[xr.ai].h - xr.used + 0.1 && SplitsInRoomLeft(xr, row[0], cw))))
            {
                FlowRows(xr, row[0], indentL + ml2, indentR, p2);
                continue;
            }
            bool sliced = row.Count == 1 && OverTallLeaf(xr, row[0], rh);
            while (rh > xr.areas[xr.ai].h - xr.used + 0.1 && !(AtAreaTop(xr) && rh > xr.areas[xr.ai].h - xr.used)
                   && !(sliced && RoomToStartSlicing(xr, row[0])))
                NextArea(xr);
            double xx2 = xr.areas[xr.ai].x + indentL + ml2;
            if (sliced)
            {
                double leafW = FlowWidth(xr.ctx, row[0]);
                PlaceSliced(xr, row[0], xx2, p2, leafW > 0 ? leafW : cw, rh);
                continue;
            }
            foreach (var rc in row)
            {
                double rcw = FlowWidth(xr.ctx, rc);
                Place(xr.ctx, rc, xx2, xr.areas[xr.ai].y + xr.used, p2, rcw > 0 ? Math.Min(rcw, cw) : cw);
                xx2 += rcw;
            }
            xr.used += rh;
        }
    }
}

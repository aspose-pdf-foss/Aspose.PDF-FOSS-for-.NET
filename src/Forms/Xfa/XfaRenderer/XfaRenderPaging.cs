using System.Globalization;
using System.Text;
using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

internal static partial class XfaRenderer
{
// XFA render paging helpers: occurrence caps, master advance, new page, split rules and row flow.
    private static int OccurMax(XfaRenderState xr, XmlElement pa)
    {
        var oc = pa.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == "occur");
        var maxS = oc?.GetAttribute("max");
        if (string.IsNullOrEmpty(maxS)) return 1;
        if (maxS == "-1") return int.MaxValue;
        return int.TryParse(maxS, out var m) && m > 0 ? m : 1;
    }

    private static void AdvanceMaster(XfaRenderState xr)
    {
        if (xr.pagesOnMaster >= OccurMax(xr, xr.master!) && xr.masterIdx + 1 < xr.pageAreas.Count)
        {
            xr.masterIdx++;
            xr.master = xr.pageAreas[xr.masterIdx];
            xr.pagesOnMaster = 0;
        }
    }

    private static void NewPage(XfaRenderState xr)
    {
        (xr.pw, xr.ph) = MasterMedium(xr.master!);
        xr.areas = MasterContentAreas(xr.master!, xr.pw, xr.ph);
        var items = new List<Item>();
        xr.ctx = new Ctx
        {
            PageH = xr.ph, RawValue = xr.rawValue, Items = items, Groups = xr.groups, Images = xr.xfaImages,
            IdElements = xr.idElements, DataRoot = xr.dataRoot, PageNum = xr.newPages.Count + 1,
            StrictBinding = xr.formRoot is null,
        };
        // Master content (cover art, headers, footers) is positioned in page coordinates.
        foreach (var c in Boxes(xr.master!))
            Place(xr.ctx, c, Len(c.GetAttribute("x"), 0), Len(c.GetAttribute("y"), 0), "", xr.pw - Len(c.GetAttribute("x"), 0));
        xr.newPages.Add((xr.pw, xr.ph, items));
        if (System.Environment.GetEnvironmentVariable("XFA_PAGES") is not null)
            System.Console.Error.WriteLine($"NEWPAGE	page={xr.newPages.Count}	master={xr.master!.GetAttribute("name")}	idx={xr.masterIdx}	onMaster={xr.pagesOnMaster}");
        xr.pagesOnMaster++;
        xr.ai = 0; xr.used = 0;
        xr.pageFresh = true;
    }

    // A flow-container that cannot fit the remaining space SPLITS between its own
    // rows rather than forcing a fresh page (XFA's default keep=none): a viewer
    // fills the page bottom with the rows that fit and continues the rest on the
    // next page. Containers carrying <keep intact> stay whole.
    private static bool KeepIntact(XfaRenderState xr, XmlElement c) => c.ChildNodes.OfType<XmlElement>()
        .Any(k => k.LocalName == "keep" && k.GetAttribute("intact") is "contentArea" or "pageArea");

    private static bool Splittable(XfaRenderState xr, XmlElement c) =>
        c.LocalName == "subform"
        && c.GetAttribute("layout") is "tb" or "table" or "lr-tb"
        && !KeepIntact(xr, c)
        && Boxes(c).Count() > 1;

    private static void FlowRows(XfaRenderState xr, XmlElement c, double indentL, double indentR)
    {
        var (mt2, _, ml2, _) = Margins(c);
        var cname = c.GetAttribute("name");
        var p2 = cname.Length > 0 ? $"{xr.rootPath}.{cname}[{SiblingIndex(c)}]" : xr.rootPath;
        double cw = BoxW(c);
        if (cw <= 0) cw = xr.areas[xr.ai].w - indentL - indentR;
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
            if (System.Environment.GetEnvironmentVariable("XFA_PAGES") is not null)
                System.Console.Error.WriteLine($"ROW\t{string.Join("+", row.Select(k => k.GetAttribute("name")))}\trh={rh:F1}\tpage={xr.newPages.Count}\tused={xr.used:F1}\tareaH={xr.areas[xr.ai].h:F1}");
            // A lone splittable subform row that overflows the remaining space
            // while its own first row still fits splits at the page bottom
            // (same fills-bottom rule as the top-level flow) instead of moving
            // whole to the next area.
            if (row.Count == 1 && rh > xr.areas[xr.ai].h - xr.used + 0.1 && Splittable(xr, row[0])
                && Boxes(row[0]).FirstOrDefault(k => !IsFloating(k)) is { } firstNested
                && Height(xr.ctx, firstNested, cw) <= xr.areas[xr.ai].h - xr.used + 0.1)
            {
                FlowRows(xr, row[0], indentL + ml2, indentR);
                continue;
            }
            while (rh > xr.areas[xr.ai].h - xr.used + 0.1 && !(xr.used == 0 && rh > xr.areas[xr.ai].h))
            {
                xr.ai++;
                if (xr.ai >= xr.areas.Count) { AdvanceMaster(xr); NewPage(xr); }
                else xr.used = 0;
            }
            double xx2 = xr.areas[xr.ai].x + indentL + ml2;
            foreach (var rc in row)
            {
                double rcw = FlowWidth(xr.ctx, rc);
                Place(xr.ctx, rc, xx2, xr.areas[xr.ai].y + xr.used, p2, rcw > 0 ? rcw : cw);
                xx2 += rcw;
            }
            xr.used += rh;
        }
    }
}

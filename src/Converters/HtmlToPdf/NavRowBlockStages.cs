using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The stages of the nav-row block: the hidden test, the cluster collection and one cluster item.
    private static bool NavHiddenWithin(NavRowBlockState nv, HtmlNode n, HtmlNode stopAt)
    {
        for (HtmlNode? p = n; p is not null && p != stopAt.Parent; p = p.Parent)
            if (p.Tag.Length > 0 && IsHiddenElement(p.Tag, p.Attrs, nv.css)) return true;
        return false;
    }

    private static void CollectNavCluster(NavRowBlockState nv, HtmlNode cluster, bool rightGroup)
    {
        foreach (var li in cluster.Descendants())
        {
            if (!CollectNavClusterItem(nv, cluster, rightGroup, li)) break;
        }
    }

    /// <summary></summary>
    private static bool CollectNavClusterItem(NavRowBlockState nv, HtmlNode cluster, bool rightGroup, HtmlNode li)
    {
        if (li.Tag != "li" || NavHiddenWithin(nv, li, cluster)) return true;
        // An <li> nested inside another collected <li> (dropdown menus) is
        // not a tab of this row.
        var nested = false;
        for (var p = li.Parent; p is not null && p != cluster; p = p.Parent)
            if (p.Tag == "li") { nested = true; break; }
        if (nested) return true;
        var text = DomText(li, nv.css);
        if (text.Length == 0) return true;
        // style anchor: deepest element directly holding a text node
        HtmlNode styleEl = li;
        HtmlNode? Find(HtmlNode n)
        {
            foreach (var c in n.Children)
            {
                if (c.Tag.Length > 0 && !IsHiddenElement(c.Tag, c.Attrs, nv.css))
                {
                    var inner = Find(c);
                    if (inner is not null) return inner;
                }
                else if (c.Tag.Length == 0 && c.Text.Trim().Length > 0)
                    return n;
            }
            return null;
        }
        styleEl = Find(li) ?? li;
        // horizontal padding/borders accumulated from the li down to the anchor
        double padL = 0, padR = 0;
        for (var n = styleEl; n is not null && n != li.Parent; n = n.Parent)
        {
            var (l, r) = DomBoxLR(n, "padding", nv.css);
            padL += l; padR += r;
            if (!string.IsNullOrEmpty(DomDecl(n, "border-left", nv.css))) padL += 1;
            if (!string.IsNullOrEmpty(DomDecl(n, "border-right", nv.css))) padR += 1;
        }
        // active-tab strip: a descendant border-top with a real color
        Color? strip = null;
        double stripH = 2;
        foreach (var d in li.Descendants())
        {
            if (d.Tag.Length == 0 || NavHiddenWithin(nv, d, li)) continue;
            // Zero-height elements are CSS-triangle tricks (dropdown arrows),
            // not tab strips.
            var hDecl = DomDecl(d, "height", nv.css);
            if (hDecl is not null && ParsePxValue(hDecl) <= 0) continue;
            var stc = DomDecl(d, "border-top-color", nv.css);
            var c2 = stc is not null ? ParseCssColor(stc) : null;
            if (c2 is null)
            {
                var bt = DomDecl(d, "border-top", nv.css);
                if (bt is not null && !bt.Contains("transparent", StringComparison.OrdinalIgnoreCase))
                    c2 = ParseCssColor(bt);
            }
            if (c2 is not null)
            {
                strip = c2;
                var btw = DomDecl(d, "border-top", nv.css);
                var wpx = btw is not null ? ParsePxValue(btw) : 0;
                if (wpx > 0) stripH = wpx;
                break;
            }
        }
        string? url = null;
        for (HtmlNode? n = styleEl; n is not null && n != li.Parent; n = n.Parent)
            if (n.Tag == "a" && n.Attrs is not null && n.Attrs.TryGetValue("href", out var h)) { url = h; break; }

        nv.runs.Add(new RowRun
        {
            Text = text,
            FontPx = DomFontPx(styleEl, nv.fontPx, nv.css),
            Bold = DomBold(styleEl, nv.css),
            Color = DomColor(styleEl, nv.css) ?? Color.FromArgb(204, 204, 204),
            PadLeftPx = padL,
            PadRightPx = padR,
            TopStripColor = strip,
            TopStripHeightPx = stripH,
            RightGroup = rightGroup,
            Url = url,
        });
        return true;
    }
}

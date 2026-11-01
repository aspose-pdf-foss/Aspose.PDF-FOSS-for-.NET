using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

internal static partial class XfaRenderer
{
    // A leaf is cut between text lines only when it holds several: a one-line box taller
    // than the page (a spacer, a picture) has nothing to continue.
    private const int MinLinesToSlice = 2;

    /// <summary>True for a text field or text draw no content area of the page can hold:
    /// its lines are continued in the following areas instead of running off the page.</summary>
    private static bool OverTallLeaf(XfaRenderState xr, XmlElement c, double h) =>
        c.LocalName is "field" or "draw" && h > xr.areas.Max(a => a.h) + OverTallSlack;

    // A leaf that will be cut anyway starts in what is left of the current area when that
    // holds this many of its lines; with less room it starts at the top of the next one.
    private const int MinLinesToStartSlicing = 3;

    /// <summary>True when the rest of the current content area is room enough to start a
    /// leaf that is continued in the following areas: it then fills the foot of the area
    /// instead of leaving it empty, and what follows it finds the room it left.</summary>
    private static bool RoomToStartSlicing(XfaRenderState xr, XmlElement leaf) =>
        xr.areas[xr.ai].h - xr.used >= FontSize(leaf) * MinLinesToStartSlicing;

    /// <summary>Places a leaf at the flow position and, while what it painted runs below
    /// the content area, moves the lines and the part of its box under the area's bottom
    /// to the top of the next area. The cut falls between text lines; the flow ends below
    /// the last piece.</summary>
    private static void PlaceSliced(XfaRenderState xr, XmlElement leaf, double x, string path, double w, double h)
    {
        int first = xr.ctx.Items.Count, firstPlaced = xr.ctx.Placements?.Count ?? -1;
        double top = xr.areas[xr.ai].y + xr.used;
        Place(xr.ctx, leaf, x, top, path, w);
        double remaining = h;
        bool sliceable = xr.ctx.Items.Skip(first).Count(i => i.Kind == "text") >= MinLinesToSlice;
        while (sliceable && remaining > xr.areas[xr.ai].y + xr.areas[xr.ai].h - top + 0.1)
        {
            var area = xr.areas[xr.ai];
            double pageH = xr.ctx.PageH;
            var carried = CutBelow(xr.ctx.Items, first, pageH - (area.y + area.h), out var cut);
            double consumed = pageH - cut - top;
            if (carried.Count == 0 || consumed <= 0) break;
            // The field's widget stays on the page it starts on, over the part left there.
            if (firstPlaced >= 0) CutPlacementsBelow(xr.ctx, firstPlaced, cut);
            firstPlaced = -1;
            NextArea(xr);
            var next = xr.areas[xr.ai];
            // What lay just under the cut now lies just under the top of the next area,
            // below the leader if the container repeats one there.
            double dy = xr.ctx.PageH - (next.y + xr.used) - cut, dx = next.x - area.x;
            foreach (var item in carried) { item.Y += dy; item.X += dx; }
            first = xr.ctx.Items.Count;
            xr.ctx.Items.AddRange(carried);
            remaining -= consumed;
            top = next.y + xr.used;
        }
        xr.used = top - xr.areas[xr.ai].y + remaining;
    }

    /// <summary>Takes out of <paramref name="items"/>, from index <paramref name="first"/> on,
    /// what lies below the height <paramref name="bottom"/> (page coordinates, y up) and
    /// returns it: text lines and rules wholly, the lower part of a fill or box that
    /// crosses it. <paramref name="cut"/> is where the cut fell: the bottom itself, or the
    /// top of a text line that crossed it, so no line is left half on each side.</summary>
    private static List<Item> CutBelow(List<Item> items, int first, double bottom, out double cut)
    {
        cut = bottom;
        for (int i = first; i < items.Count; i++)
        {
            var t = items[i];
            if (t.Kind == "text" && t.Y < bottom && t.Y + t.FontSize > cut) cut = t.Y + t.FontSize;
        }
        var carried = new List<Item>();
        for (int i = first; i < items.Count; i++)
        {
            var item = items[i];
            bool rect = item.Kind is "fill" or "box";
            if (rect && item.Y < cut && item.Y + item.H > cut)
            {
                // The box crosses the cut: its lower part goes on, its upper part stays.
                var lower = new Item { Kind = item.Kind, X = item.X, Y = item.Y, W = item.W, H = cut - item.Y, Color = item.Color, Radius = item.Radius };
                item.H -= lower.H;
                item.Y = cut;
                carried.Add(lower);
                continue;
            }
            bool below = item.Kind == "text" ? item.Y < bottom
                : rect ? item.Y + item.H <= cut
                : item.Kind == "line" && item.Y < cut;
            if (!below) continue;
            carried.Add(item);
            items.RemoveAt(i--);
        }
        return carried;
    }
}

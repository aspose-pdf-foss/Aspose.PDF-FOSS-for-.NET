using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

internal static partial class XfaRenderer
{
    // A leader may take this share of a content area at most: one that leaves no room
    // under it for the rows it leads is not placed.
    private const double MaxLeaderShare = 0.5;

    /// <summary>The subform a container repeats at the top of every area its content
    /// overflows into (&lt;overflow leader&gt;, or the older &lt;break overflowLeader&gt;): a
    /// child of the container by name - a table's header row - or any subform of the
    /// template by #id. Null when the container names none.</summary>
    private static XmlElement? OverflowLeaderOf(XfaRenderState xr, XmlElement c)
    {
        var reference = FirstChild(c, "overflow")?.GetAttribute("leader") ?? string.Empty;
        if (reference.Length == 0) reference = FirstChild(c, "break")?.GetAttribute("overflowLeader") ?? string.Empty;
        if (reference.Length == 0) return null;
        if (reference[0] == '#')
            return xr.idElements.TryGetValue(reference.Substring(1), out var byId) && byId.LocalName == "subform" ? byId : null;
        var bracket = reference.IndexOf('[');
        var name = bracket < 0 ? reference : reference.Substring(0, bracket);
        return c.ChildNodes.OfType<XmlElement>().FirstOrDefault(k => k.LocalName == "subform" && k.GetAttribute("name") == name)
            ?? (xr.idElements.TryGetValue(name, out var named) && named.LocalName == "subform" ? named : null);
    }

    /// <summary>True while the current content area holds nothing of the flow but the
    /// leader placed at its top: a box that does not fit here fits no better in the next.</summary>
    private static bool AtAreaTop(XfaRenderState xr) => xr.used <= xr.areaStart;

    /// <summary>Moves the flow to the next content area of the page, or to a new page past
    /// the last one. An overflow (<paramref name="overflow"/>) of a container that names a
    /// leader places that leader at the top of the area, and the flow goes on under it.</summary>
    private static void NextArea(XfaRenderState xr, bool overflow = true)
    {
        xr.ai++;
        if (xr.ai >= xr.areas.Count) { AdvanceMaster(xr); NewPage(xr); }
        else xr.used = 0;
        xr.areaStart = 0;
        if (!overflow || xr.overflowLeader is not { } leader) return;
        var area = xr.areas[xr.ai];
        double w = area.w - xr.leaderIndent;
        double h = Height(xr.ctx, leader, w);
        if (h <= MinFlowHeight || h > area.h * MaxLeaderShare) return;
        Place(xr.ctx, leader, area.x + xr.leaderIndent, area.y, xr.leaderPath, w);
        xr.used = h;
        xr.areaStart = h;
    }
}

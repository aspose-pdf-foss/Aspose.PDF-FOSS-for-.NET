using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

/// <summary>
/// A coarse XFA box model — just enough to decide how many physical pages the flowing body content
/// occupies (which drives how many times the master page is emitted, and hence Form.Count). It is
/// NOT a full layout engine: it measures block heights from the template's explicit <c>h</c>/
/// <c>minH</c> (growing containers to their content, clipping fixed-height ones; positioned
/// containers take their children's bottom extent) and divides the total flowed height by the
/// content-area height. Page COUNT is robust even when the exact break position is not, because the
/// total body height sits comfortably within a page-count band. Heights are measured over the raw
/// template (so <c>draw</c> elements — captions, legal text — count) using the static
/// <c>presence</c>; which top-level body blocks flow at all is decided by the post-script model.
/// </summary>
internal static class XfaLayout
{
    private const double DefaultLeafMm = 6.0;
    private static readonly string[] Boxes = { "subform", "subformSet", "area", "field", "draw", "exclGroup" };

    /// <summary>Number of physical pages the given (post-script visible) body blocks flow into (min 1).</summary>
    internal static int PageCount(IEnumerable<XfaNode> bodyBlocks, double contentAreaMm)
    {
        if (contentAreaMm <= 0) return 1;
        double total = 0;
        foreach (var b in bodyBlocks)
            if (!b.EffectiveHidden) total += Height(b.Template);
        if (total <= 0) return 1;
        return Math.Max(1, (int)Math.Ceiling(total / contentAreaMm - 1e-6));
    }

    /// <summary>Number of physical pages when page capacities follow the ordered
    /// pageSet sequence: capacity i applies to page i, and the LAST capacity repeats
    /// for every further page (the continuation master). Mirrors the renderer's
    /// ordered-pageArea progression.</summary>
    internal static int PageCount(IEnumerable<XfaNode> bodyBlocks, IReadOnlyList<double> pageCapacitiesMm)
    {
        if (pageCapacitiesMm.Count == 0) return 1;
        double total = 0;
        foreach (var b in bodyBlocks)
            if (!b.EffectiveHidden) total += Height(b.Template);
        if (total <= 0) return 1;
        var pages = 0;
        var remaining = total;
        // A zero capacity (an area-less master) would never drain the flow; the
        // page cap is a runaway guard far above any real form.
        const int MaxPages = 4096;
        while (remaining > 1e-6 && pages < MaxPages)
        {
            var cap = pageCapacitiesMm[Math.Min(pages, pageCapacitiesMm.Count - 1)];
            if (cap <= 0) break;
            remaining -= cap;
            pages++;
        }
        return Math.Max(1, pages);
    }

    /// <summary>Content-area height (mm) of a master pageArea, or 0 if none declared.</summary>
    internal static double ContentAreaHeightMm(XfaNode pageArea)
    {
        foreach (XmlNode c in pageArea.Template.ChildNodes)
            if (c.NodeType == XmlNodeType.Element && c.LocalName == "contentArea")
                return Mm(c.Attributes?["h"]?.Value) ?? 0;
        return 0;
    }

    /// <summary>Estimated laid-out height (mm) of a template box element's subtree.</summary>
    internal static double Height(XmlNode el)
    {
        if (Hidden(el)) return 0;
        var local = el.LocalName;
        double? h = Mm(el.Attributes?["h"]?.Value);
        double? minH = Mm(el.Attributes?["minH"]?.Value);

        if (local is "field" or "draw" or "exclGroup")
            return h ?? minH ?? DefaultLeafMm;

        // container: a fixed h clips; otherwise it grows to max(minH, content height)
        if (h is not null) return h.Value;
        return Math.Max(minH ?? 0, ContentHeight(el));
    }

    private static double ContentHeight(XmlNode el)
    {
        var layout = el.Attributes?["layout"]?.Value ?? "position";
        bool flowed = layout is "tb" or "lr-tb" or "rl-tb";
        double sum = 0, max = 0;
        foreach (XmlNode c in el.ChildNodes)
        {
            if (c.NodeType != XmlNodeType.Element || Array.IndexOf(Boxes, c.LocalName) < 0 || Hidden(c)) continue;
            double ch = Height(c);
            if (flowed) sum += ch;
            else max = Math.Max(max, (Mm(c.Attributes?["y"]?.Value) ?? 0) + ch);
        }
        return flowed ? sum : max;
    }

    private static bool Hidden(XmlNode el)
    {
        var p = el.Attributes?["presence"]?.Value;
        return p is "hidden" or "invisible" or "inactive";
    }

    /// <summary>Parse an XFA measurement (e.g. "156.9mm", "6.85in", "12pt") to millimetres.</summary>
    private static double? Mm(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        v = v.Trim();
        (string u, double f)[] units = { ("mm", 1), ("in", 25.4), ("cm", 10), ("pt", 25.4 / 72.0) };
        foreach (var (u, f) in units)
            if (v.EndsWith(u, StringComparison.Ordinal)
                && double.TryParse(v[..^u.Length], NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                return d * f;
        return double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var raw) ? raw : null;
    }
}

using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The stages of the agenda outline render: the text, indent, marker-box, size and presenter helpers.
    private static double AgIndentOf(int lvl)
    {
        var x = 0.0;
        for (var i = 0; i < lvl; i++) x += i == 0 ? 45.75 : 30.0;
        return x;
    }

    private static double AgMarkerBoxOf(int lvl)
    => lvl switch { 0 => 37.5, 1 or 2 => 22.5, _ => 0.0 };

    private static double AgSizeOf(int lvl)
    => lvl switch { 0 => 12.0, 1 => 11.0, _ => 10.0 };

    private static double AgPresenterOf(int lvl)
    => lvl switch { 0 => 42.75, 3 => 11.25, _ => 30.0 };

    /// <summary></summary>
    private static bool RenderAgendaItem(AgendaOutlineRenderState ao, Match li)
    {
        var lvl = li.Groups[1].Value[0] - '0';
        var inner = li.Groups[2].Value;
        var size = AgSizeOf(lvl);
        var indent = ao.marginLeft + AgIndentOf(lvl);

        // one item padding between siblings, and one more for every level
        // the outline closes on the way back out
        if (ao.prevLvl >= 0) ao.y += AgItemPadPt * (1 + Math.Max(0, ao.prevLvl - lvl));
        else ao.y += AgItemPadPt;

        var mk = Regex.Match(inner,
            @"<span\b[^>]*class\s*=\s*[""'][^""']*agenda-outline-level[^""']*[""'][^>]*>([\s\S]*?)</span\s*>",
            RegexOptions.IgnoreCase);
        var nm = Regex.Match(inner,
            @"<span\b[^>]*class\s*=\s*[""'][^""']*agenda-item-name[^""']*[""'][^>]*>([\s\S]*?)</span\s*>",
            RegexOptions.IgnoreCase);
        var pr = Regex.Match(inner,
            @"<span\b[^>]*class\s*=\s*[""'][^""']*agenda-presenter[^""']*[""'][^>]*>([\s\S]*?)</span\s*>",
            RegexOptions.IgnoreCase);

        var boxW = AgMarkerBoxOf(lvl);
        var markerRight = indent + boxW;
        if (mk.Success)
        {
            var t = AgTxt(mk.Groups[1].Value);
            if (t.Length > 0)
            {
                var w = MeasureFaceText(ao.face, t, size);
                // right-aligned in its box; a level with no declared box
                // shrinks to the numbering itself
                var mx = boxW > 0 ? markerRight - w : indent;
                if (boxW <= 0) markerRight = indent + w;
                EmitPositionedRun(ao.page, ao.res, size, mx,
                    ao.pageH - (ao.y + size * AgDropFactor), t);
            }
        }
        var textX = markerRight + AgMarkerGapPt;
        if (nm.Success)
        {
            var t = AgTxt(nm.Groups[1].Value);
            foreach (var ln in MeasuredWordWrap(t, ao.marginLeft + ao.contentW - textX, ao.face, size))
            {
                EmitPositionedRun(ao.page, ao.res, size, textX,
                    ao.pageH - (ao.y + size * AgDropFactor), ln);
                ao.y += size * AgLineFactor;
            }
            if (!nm.Success) ao.y += size * AgLineFactor;
        }
        if (pr.Success)
        {
            var t = AgTxt(pr.Groups[1].Value);
            if (t.Length > 0)
            {
                var ps = size * 0.9;
                EmitPositionedRun(ao.page, ao.resI, ps, indent + AgPresenterOf(lvl),
                    ao.pageH - (ao.y + ps * AgDropFactor), t);
                ao.y += ps * AgLineFactor;
            }
        }
        ao.prevLvl = lvl;
        return true;
    }
    private static string AgTxt(string markup) =>
        CollapseWs(DecodeEntities(Regex.Replace(markup, @"<[^>]+>", " "))).Trim();
}

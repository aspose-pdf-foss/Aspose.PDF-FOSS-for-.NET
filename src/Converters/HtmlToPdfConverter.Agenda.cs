using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The meeting-agenda fragment: a centred header block over an `agenda-outline`
// list whose items nest four levels deep. Each level indents by its own list
// margin, seats its numbering RIGHT-ALIGNED in a fixed box, and hangs an
// italic presenter line under the item at that level's own offset. Added to a
// page as an HtmlFragment rather than converted, so it draws straight onto the
// page the caller built (see LayoutHtmlFragmentParagraph).
internal static partial class HtmlToPdfConverter
{
    private const double AgLineFactor = 1.15;     // Arial's line box
    private const double AgDropFactor = 0.926;    // its baseline inside that box
    private const double AgMarkerGapPt = 3.75;    // the numbering's 5px margin-right
    private const double AgItemPadPt = 3.0;       // the items' 4px vertical padding
    private const double AgHeaderGapPt = 22.5;    // the header block's 30px margin-bottom

    /// <summary>Draw the agenda fragment onto <paramref name="page"/> at the
    /// caller's margins, or false when the fragment is not one.</summary>
    internal static bool TryRenderAgendaOutline(string html, Page page,
        double marginLeft, double marginRight, double marginTop)
    {
        if (!Regex.IsMatch(html, @"class\s*=\s*[""']agenda-outline", RegexOptions.IgnoreCase))
            return false;
        const string face = "Arial";
        if (WinMetricsFor(face) is null) return false;
        var pageH = page.GetPageRect(true).Height;
        var pageW = page.GetPageRect(true).Width;
        var contentW = pageW - marginLeft - marginRight;
        EnsureFonts(page);
        const string res = "F8", resI = "F9";
        EnsureFont(page, face, res);
        EnsureFont(page, face + "-Italic", resI);

        static string Txt(string markup) =>
            CollapseWs(DecodeEntities(Regex.Replace(markup, @"<[^>]+>", " "))).Trim();

        var y = marginTop;

        // ── the header: its details column is a percent-wide float, and its
        // lines centre on that column's own middle ──────────────────────────
        var hdrM = Regex.Match(html,
            @"<div\b[^>]*id\s*=\s*[""']agendaMeetingDetails[""'][^>]*>([\s\S]*?)</div\s*>\s*</div\s*>",
            RegexOptions.IgnoreCase);
        if (hdrM.Success)
        {
            // the logo column and its gutter push the details column right
            var logoFrac = PercentOf(html, "agendaCompanyLogo", "width", 0.20)
                         + PercentOf(html, "agendaCompanyLogo", "margin-right", 0.03);
            var detFrac = PercentOf(html, "agendaMeetingDetails", "width", 0.50);
            var centre = marginLeft + contentW * (logoFrac + detFrac / 2.0);
            var size = 11.0;
            foreach (Match dm in Regex.Matches(hdrM.Groups[1].Value,
                         @"<div\b[^>]*>([\s\S]*?)</div\s*>", RegexOptions.IgnoreCase))
            {
                var t = Txt(dm.Groups[1].Value);
                if (t.Length == 0) continue;
                var w = MeasureFaceText(face, t, size);
                EmitPositionedRun(page, res, size, centre - w / 2,
                    pageH - (y + size * AgDropFactor), t);
                y += size * AgLineFactor;
            }
            y += AgHeaderGapPt;
        }

        // ── the outline ───────────────────────────────────────────────────
        // level 0 opens at the content edge; each nesting adds its list's own
        // margin, and levels 0-2 seat their numbering in a fixed box.
        static double IndentOf(int lvl)
        {
            var x = 0.0;
            for (var i = 0; i < lvl; i++) x += i == 0 ? 45.75 : 30.0;
            return x;
        }
        static double MarkerBoxOf(int lvl) => lvl switch { 0 => 37.5, 1 or 2 => 22.5, _ => 0.0 };
        static double SizeOf(int lvl) => lvl switch { 0 => 12.0, 1 => 11.0, _ => 10.0 };
        // the presenter's own indent inside its item
        static double PresenterOf(int lvl) => lvl switch { 0 => 42.75, 3 => 11.25, _ => 30.0 };

        var prevLvl = -1;
        foreach (Match li in Regex.Matches(html,
                     @"<li\b[^>]*class\s*=\s*[""'][^""']*\blevel-(\d)\b[^""']*[""'][^>]*>([\s\S]*?)(?=<li\b|</ol)",
                     RegexOptions.IgnoreCase))
        {
            var lvl = li.Groups[1].Value[0] - '0';
            var inner = li.Groups[2].Value;
            var size = SizeOf(lvl);
            var indent = marginLeft + IndentOf(lvl);

            // one item padding between siblings, and one more for every level
            // the outline closes on the way back out
            if (prevLvl >= 0) y += AgItemPadPt * (1 + Math.Max(0, prevLvl - lvl));
            else y += AgItemPadPt;

            var mk = Regex.Match(inner,
                @"<span\b[^>]*class\s*=\s*[""'][^""']*agenda-outline-level[^""']*[""'][^>]*>([\s\S]*?)</span\s*>",
                RegexOptions.IgnoreCase);
            var nm = Regex.Match(inner,
                @"<span\b[^>]*class\s*=\s*[""'][^""']*agenda-item-name[^""']*[""'][^>]*>([\s\S]*?)</span\s*>",
                RegexOptions.IgnoreCase);
            var pr = Regex.Match(inner,
                @"<span\b[^>]*class\s*=\s*[""'][^""']*agenda-presenter[^""']*[""'][^>]*>([\s\S]*?)</span\s*>",
                RegexOptions.IgnoreCase);

            var boxW = MarkerBoxOf(lvl);
            var markerRight = indent + boxW;
            if (mk.Success)
            {
                var t = Txt(mk.Groups[1].Value);
                if (t.Length > 0)
                {
                    var w = MeasureFaceText(face, t, size);
                    // right-aligned in its box; a level with no declared box
                    // shrinks to the numbering itself
                    var mx = boxW > 0 ? markerRight - w : indent;
                    if (boxW <= 0) markerRight = indent + w;
                    EmitPositionedRun(page, res, size, mx,
                        pageH - (y + size * AgDropFactor), t);
                }
            }
            var textX = markerRight + AgMarkerGapPt;
            if (nm.Success)
            {
                var t = Txt(nm.Groups[1].Value);
                foreach (var ln in MeasuredWordWrap(t, marginLeft + contentW - textX, face, size))
                {
                    EmitPositionedRun(page, res, size, textX,
                        pageH - (y + size * AgDropFactor), ln);
                    y += size * AgLineFactor;
                }
                if (!nm.Success) y += size * AgLineFactor;
            }
            if (pr.Success)
            {
                var t = Txt(pr.Groups[1].Value);
                if (t.Length > 0)
                {
                    var ps = size * 0.9;
                    EmitPositionedRun(page, resI, ps, indent + PresenterOf(lvl),
                        pageH - (y + ps * AgDropFactor), t);
                    y += ps * AgLineFactor;
                }
            }
            prevLvl = lvl;
        }
        return true;
    }

    /// <summary>A percent declared on an id's inline rule, as a fraction.</summary>
    private static double PercentOf(string html, string id, string prop, double fallback)
    {
        var m = Regex.Match(html, @"#" + Regex.Escape(id) + @"\s*\{[^}]*?" + prop
            + @"\s*:\s*([\d.]+)%", RegexOptions.IgnoreCase);
        return m.Success && double.TryParse(m.Groups[1].Value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v / 100.0 : fallback;
    }
}

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
        var ao = new AgendaOutlineRenderState();
        ao.html = html;
        ao.page = page;
        ao.marginLeft = marginLeft;
        ao.marginRight = marginRight;
        ao.marginTop = marginTop;
        if (!Regex.IsMatch(ao.html, @"class\s*=\s*[""']agenda-outline", RegexOptions.IgnoreCase))
            return false;
        ao.face = "Arial";
        if (WinMetricsFor(ao.face) is null) return false;
        ao.pageH = ao.page.GetPageRect(true).Height;
        ao.pageW = ao.page.GetPageRect(true).Width;
        ao.contentW = ao.pageW - ao.marginLeft - ao.marginRight;
        EnsureFonts(ao.page);
        ao.res = "F8";
        ao.resI = "F9";
        EnsureFont(ao.page, ao.face, ao.res);
        EnsureFont(ao.page, ao.face + "-Italic", ao.resI);

        ao.y = ao.marginTop;

        ao.hdrM = Regex.Match(ao.html,
            @"<div\b[^>]*id\s*=\s*[""']agendaMeetingDetails[""'][^>]*>([\s\S]*?)</div\s*>\s*</div\s*>",
            RegexOptions.IgnoreCase);
        if (ao.hdrM.Success)
        {
            // the logo column and its gutter push the details column right
            var logoFrac = PercentOf(ao.html, "agendaCompanyLogo", "width", 0.20)
                         + PercentOf(ao.html, "agendaCompanyLogo", "margin-right", 0.03);
            var detFrac = PercentOf(ao.html, "agendaMeetingDetails", "width", 0.50);
            var centre = ao.marginLeft + ao.contentW * (logoFrac + detFrac / 2.0);
            var size = 11.0;
            foreach (Match dm in Regex.Matches(ao.hdrM.Groups[1].Value,
                         @"<div\b[^>]*>([\s\S]*?)</div\s*>", RegexOptions.IgnoreCase))
            {
                var t = AgTxt(dm.Groups[1].Value);
                if (t.Length == 0) continue;
                var w = MeasureFaceText(ao.face, t, size);
                EmitPositionedRun(ao.page, ao.res, size, centre - w / 2,
                    ao.pageH - (ao.y + size * AgDropFactor), t);
                ao.y += size * AgLineFactor;
            }
            ao.y += AgHeaderGapPt;
        }

        // ── the outline ───────────────────────────────────────────────────
        // level 0 opens at the content edge; each nesting adds its list's own
        // margin, and levels 0-2 seat their numbering in a fixed box.
        // the presenter's own indent inside its item
        ao.prevLvl = -1;
        foreach (Match li in Regex.Matches(ao.html,
                     @"<li\b[^>]*class\s*=\s*[""'][^""']*\blevel-(\d)\b[^""']*[""'][^>]*>([\s\S]*?)(?=<li\b|</ol)",
                     RegexOptions.IgnoreCase))
        {
            if (!RenderAgendaItem(ao, li)) break;
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

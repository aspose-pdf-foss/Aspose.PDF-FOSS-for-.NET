using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    private static Document? TryRenderCjkOrderReport(string html,
        IReadOnlyDictionary<string, Dictionary<string, string>> css,
        double pageHeight)
    {
        var co = new CjkOrderReportState();
        co.html = html;
        co.css = css;
        co.pageHeight = pageHeight;
        if (!co.css.TryGetValue("*", out var starRule)
            || !starRule.ContainsKey("font-family")
            || !co.css.TryGetValue("thead", out var theadR)
            || !theadR.TryGetValue("display", out var thDisp)
            || !thDisp.Contains("table-header-group", StringComparison.OrdinalIgnoreCase)
            || !co.css.ContainsKey(".text-xs")
            || Regex.Matches(co.html, @"<table\b", RegexOptions.IgnoreCase).Count < 5)
            return null;
        co.simsun = Text.SystemFontResolver.Resolve("SimSun");
        co.arial = Text.SystemFontResolver.Resolve("Arial");
        co.arialBold = Text.SystemFontResolver.Resolve("Arial-Bold");
        if (co.simsun is null || co.arial is null || co.arialBold is null) return null;
        co.invc = System.Globalization.CultureInfo.InvariantCulture;

        co.doc = new Document();
        co.page = co.doc.Pages.Add(CjkPageW, co.pageHeight);
        EnsureFonts(co.page);

        // balanced table list (the report nests wrapper tables several deep)
        ScanTables(co);
        RenderPageOneInfo(co);

        RenderActivityTables(co);

        RenderInfraTable(co);
        // ── the remaining sections: plain text on the following sheets ──
        RenderTailPages(co);
        return co.doc;
    }
}

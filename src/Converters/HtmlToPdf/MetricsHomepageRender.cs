using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    private static Document? TryRenderMetricsHomepage(
        string html, HtmlLoadOptions? options)
    {
        var mh = new MetricsHomepageState();
        mh.html = html;
        mh.options = options;
        if (!mh.html.Contains("metrics.aspose.com", StringComparison.Ordinal)
            || !mh.html.Contains("class=\"flex-cell\"", StringComparison.Ordinal)
            || !mh.html.Contains("headergraphics.svg", StringComparison.Ordinal)
            || !mh.html.Contains("class=\"table-container\"", StringComparison.Ordinal))
            return null;

        var segoe = Text.SystemFontResolver.Resolve("Segoe UI");
        var segoeSemi = Text.SystemFontResolver.Resolve("Segoe UI Semibold")
            ?? Text.SystemFontResolver.Resolve("SegoeUI-Semibold")
            ?? Text.SystemFontResolver.Resolve("SegoeUI-Bold");
        var arialBold = Text.SystemFontResolver.Resolve("Arial-Bold")
            ?? Text.SystemFontResolver.Resolve("Arial Bold")
            ?? Text.SystemFontResolver.Resolve("SegoeUI-Bold");
        if (segoe is null || segoeSemi is null || arialBold is null) return null;
        mh.segoe = segoe;
        mh.segoeSemi = segoeSemi;
        mh.arialBold = arialBold;


        ParseMetricsTables(mh);
        if (mh.tables.Count == 0) return null;

        mh.ctaM = Regex.Match(mh.html, @"class=""[^""]*herobtn[^""]*""[^>]*>([\s\S]*?)</a>",
            RegexOptions.IgnoreCase);
        mh.ctaText = mh.ctaM.Success ? MhFlat(mh.ctaM.Groups[1].Value) : "Try Our SDKs for Free";

        mh.inv = System.Globalization.CultureInfo.InvariantCulture;
        mh.measureDict = new Core.PdfDictionary();
        mh.doc = Document.Create();
        mh.page = mh.doc.Pages.Add(MhPageW, MhPageH);
        mh.shapes = new StringBuilder();
        mh.runs = new List<(byte[] Ttf, string Face, double Fs, double X, double BaseTd, string Text, string Col)>();
        DrawMetricsHero(mh);

        DrawMetricsCta(mh);

        // ── the metric grids ─────────────────────────────────────────────────
        for (var ti = 0; ti < mh.tables.Count && ti < 2; ti++)
        {
            if (!DrawMetricsGrid(mh, ti)) break;
        }
        FlushPage(mh);
        return mh.doc;
    }
}

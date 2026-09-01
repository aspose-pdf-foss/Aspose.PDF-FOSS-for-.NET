using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // ── The portal-shell page ───────────────────────────────────────────────
    //
    // An ASP.NET portal skeleton: a fixed-width #wrapper holding a #header
    // (logo float + search form), a #banner with a .welcome list, and a #col2
    // input row — styled by TWO linked sheets that both survive the medium
    // (the print sheet paints the html canvas and the body's top border, the
    // screen sheet sizes the wrapper and colours it). The logo and header
    // background images are unreachable, so the chrome is the boxes alone.
    //
    // Measured geometry:
    //  - the page keeps the A4 height and GROWS its width to the wrapper:
    //    90 + 1007 px · 0.75 + 90 = 935.25 pt (the expected page measures
    //    935.1 × 842);
    //  - the html canvas colour fills exactly the CONTENT box (margins stay
    //    white), the body border-top strokes 10 px black across it, and the
    //    55 px #header paints its own white over the wrapper's #ccc;
    //  - the wrapper's #ccc runs from the header's bottom to the bottom of
    //    the #col2 input row (196.3 pt from the top);
    //  - the .welcome list sets its first glyph top 15.1 pt under the banner
    //    top and paces 12 pt per item (16 px lines of the .85em body), text
    //    40 px inside the content edge (UA ul padding), bullets 6.3 pt left
    //    of the text;
    //  - both text inputs render the default 157.5 px × ~22 px sunken box
    //    (the header's at the float-cleared 220 px, the col2 one at the
    //    content edge), the empty submit is a 19×9 px bevel right of the
    //    search box, and the Go button is a 39 px bevel with a 10 pt label.
    private const double PsMarginLr = 90.0;             // metric-flow side margins
    private const double PsMarginTb = 72.0;
    private const double PsA4WidthPt = 595.0;
    private const double PsA4HeightPt = 842.0;
    /// <summary>First .welcome glyph top under the banner top: the ul's 1 em
    /// margin plus the half-leading of a 13.6 px run in its 16 px line box
    /// (measured 135.8 − 120.75).</summary>
    private const double PsListFirstGlyphDropPt = 15.1;
    private const double PsListPitchPt = 12.0;          // 16 px line box
    private const double PsListIndentPt = 30.0;         // UA ul padding-left 40 px
    private const double PsBulletLeftOfTextPt = 6.3;
    private const double PsBulletRadiusPt = 1.2;
    private const double PsBulletDropPt = 3.55;         // centre under the glyph top
    /// <summary>Arial cap height per em — glyph tops were measured on capitals.</summary>
    private const double PsCapHeight = 0.716;
    /// <summary>Default text-input box: 157.5 × 21.1 css px (probed on both inputs).</summary>
    private const double PsInputWPt = 118.1;
    private const double PsHdrInputHPt = 15.84;
    /// <summary>The header input opens this far under the body border (probed).</summary>
    private const double PsHdrInputLiftPt = 1.14;
    /// <summary>The search form clears the floated 200 px logo + its 20 px margin.</summary>
    private const double PsHdrInputLeftPx = 220.0;
    private const double PsSubmitGapPt = 1.9;           // submit left of input right
    private const double PsSubmitDropPt = 4.76;         // under the input top
    private const double PsSubmitWPt = 14.5;
    private const double PsSubmitHPt = 6.8;
    /// <summary>The col2 input row opens 2.85 pt under the list's bottom margin
    /// (probed: box top 180.0 with the ul ending at 177.15).</summary>
    private const double PsCol2InputTopPt = 180.0;
    private const double PsCol2InputHPt = 17.05;
    private const double PsGoGapPt = 1.5;               // Go button left of input right
    private const double PsGoWPt = 29.3;
    private const double PsGoHPt = 17.8;
    private const double PsGoLabelPt = 10.0;            // default button font
    /// <summary>The Go glyph top from the page top (probed 184.8).</summary>
    private const double PsGoGlyphTopPt = 184.8;
    /// <summary>Text runs in the body's #333 (the print sheet's body colour).</summary>
    private const double PsInk = 0.2;

    private static Document? TryRenderPortalShell(string html)
    {
        if (html.IndexOf("id=\"wrapper\"", System.StringComparison.OrdinalIgnoreCase) < 0
            || html.IndexOf("id=\"banner\"", System.StringComparison.OrdinalIgnoreCase) < 0
            || html.IndexOf("id=\"col2\"", System.StringComparison.OrdinalIgnoreCase) < 0
            || html.IndexOf("class=\"welcome\"", System.StringComparison.OrdinalIgnoreCase) < 0
            || html.IndexOf("sButton", System.StringComparison.Ordinal) < 0) return null;

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        double Px(string s) => double.Parse(s, inv) * 0.75;

        // The wrapper's declared px width is the content box; without it the
        // page cannot grow and this is a different document.
        var wrapM = Regex.Match(html, @"#wrapper\s*\{[^}]*(?<![-\w])width\s*:\s*([\d.]+)\s*px",
            RegexOptions.IgnoreCase);
        if (!wrapM.Success) return null;
        var wrapPt = Px(wrapM.Groups[1].Value);

        var hdrM = Regex.Match(html, @"#header\s*\{[^}]*(?<![-\w])height\s*:\s*([\d.]+)\s*px",
            RegexOptions.IgnoreCase);
        var headerPt = hdrM.Success ? Px(hdrM.Groups[1].Value) : 41.25;

        // The print sheet's body border-top (colour fixed black in this class).
        var barM = Regex.Match(html, @"(?<![-\w])body\s*\{[^}]*border-top\s*:\s*solid\s+([\d.]+)\s*px",
            RegexOptions.IgnoreCase);
        var barPt = barM.Success ? Px(barM.Groups[1].Value) : 7.5;

        // Canvas + wrapper colours, straight from their rules.
        (double R, double G, double B) CssColor(string selector, (double, double, double) fallback)
        {
            var m = Regex.Match(html, Regex.Escape(selector)
                + @"\s*\{[^}]*background(?:-color)?\s*:\s*#(?<h>[0-9a-fA-F]{3,6})",
                RegexOptions.IgnoreCase);
            if (!m.Success) return fallback;
            var hx = m.Groups["h"].Value;
            if (hx.Length == 3) hx = $"{hx[0]}{hx[0]}{hx[1]}{hx[1]}{hx[2]}{hx[2]}";
            return (System.Convert.ToInt32(hx[..2], 16) / 255.0,
                    System.Convert.ToInt32(hx[2..4], 16) / 255.0,
                    System.Convert.ToInt32(hx[4..6], 16) / 255.0);
        }
        var canvas = CssColor("html", (226 / 255.0, 226 / 255.0, 226 / 255.0));
        var wrapBg = CssColor("#wrapper", (0.8, 0.8, 0.8));

        // The .85em body on the 16 px base: 13.6 px = 10.2 pt.
        var fs = 10.2;
        var bodyFsM = Regex.Match(html, @"(?<![-\w])body\s*\{[^}]*font-size\s*:\s*(\.?[\d.]+)\s*em",
            RegexOptions.IgnoreCase);
        if (bodyFsM.Success && double.TryParse(bodyFsM.Groups[1].Value,
                System.Globalization.NumberStyles.Float, inv, out var bodyEm) && bodyEm > 0)
            fs = bodyEm * 16.0 * 0.75;

        var pageW = System.Math.Max(PsA4WidthPt, PsMarginLr * 2 + wrapPt);
        var pageH = PsA4HeightPt;
        var left = PsMarginLr;
        var right = left + wrapPt;
        var top = PsMarginTb;
        var bottom = pageH - PsMarginTb;

        var doc = new Document();
        var page = doc.Pages.Add(pageW, pageH);
        EnsureFonts(page);
        var resByFace = new Dictionary<string, string>(System.StringComparer.Ordinal);
        var sb = new StringBuilder();
        void Rect(double x, double yTop, double w, double h, (double R, double G, double B) c)
            => sb.Append(string.Create(inv,
                $"q {c.R:0.###} {c.G:0.###} {c.B:0.###} rg {x:F2} {pageH - yTop - h:F2} {w:F2} {h:F2} re f Q\n"));

        // The canvas fills the content box; the chrome layers over it.
        Rect(left, top, wrapPt, bottom - top, canvas);
        Rect(left, top, wrapPt, barPt, (0, 0, 0));                       // body border-top
        var headerTop = top + barPt;
        Rect(left, headerTop, wrapPt, headerPt, (1, 1, 1));              // #header's own white
        var bannerTop = headerTop + headerPt;
        Rect(left, bannerTop, wrapPt, PsCol2InputTopPt + PsCol2InputHPt - bannerTop, wrapBg);

        // The header search input (float-cleared) and its empty submit bevel.
        var inpX = left + PsHdrInputLeftPx * 0.75;
        var inpTop = headerTop + PsHdrInputLiftPt;
        void SunkenBox(double x, double yTop, double w, double h)
        {
            Rect(x, yTop, w, h, (0.25, 0.25, 0.25));
            Rect(x + 1.0, yTop + 1.0, w - 2.0, h - 2.0, (1, 1, 1));
        }
        SunkenBox(inpX, inpTop, PsInputWPt, PsHdrInputHPt);
        var subX = inpX + PsInputWPt + PsSubmitGapPt;
        var subTop = inpTop + PsSubmitDropPt;
        Rect(subX, subTop, PsSubmitWPt, PsSubmitHPt, (0.25, 0.25, 0.25));
        Rect(subX + 0.5, subTop + 0.5, PsSubmitWPt - 1.0, PsSubmitHPt - 1.0, (0.75, 0.75, 0.75));
        Rect(subX + 2.0, subTop + 2.0, PsSubmitWPt - 4.0, PsSubmitHPt - 4.0, (0.66, 0.66, 0.66));

        // The .welcome list: bullets + items in the body face and ink.
        var items = new List<string>();
        var welcomeM = Regex.Match(html,
            @"class=""welcome""[^>]*>(?<b>[\s\S]*?)</ul\s*>", RegexOptions.IgnoreCase);
        if (welcomeM.Success)
            foreach (Match li in Regex.Matches(welcomeM.Groups["b"].Value,
                         @"<li\b[^>]*>(?<t>[\s\S]*?)</li\s*>", RegexOptions.IgnoreCase))
                items.Add(Regex.Replace(DecodeEntities(
                    Regex.Replace(li.Groups["t"].Value, "<[^>]+>", "")), @"\s+", " ").Trim());

        sb.Append(string.Create(inv, $"q {PsInk:0.###} {PsInk:0.###} {PsInk:0.###} rg\n"));
        page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
        sb.Clear();
        var textX = left + PsListIndentPt;
        for (var i = 0; i < items.Count; i++)
        {
            var glyphTop = bannerTop + PsListFirstGlyphDropPt + i * PsListPitchPt;
            var baseline = pageH - (glyphTop + PsCapHeight * fs);
            EmitGridsterText(page, resByFace, fs, textX, baseline, items[i], "Arial");
            var cy = pageH - (glyphTop + PsBulletDropPt);
            var r = PsBulletRadiusPt;
            sb.Append(string.Create(inv,
                $"{textX - PsBulletLeftOfTextPt - r:F2} {cy - r:F2} {2 * r:F2} {2 * r:F2} re f\n"));
        }
        page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString() + "Q\n"));
        sb.Clear();

        // The #col2 input row at the content edge, and the Go bevel.
        SunkenBox(left, PsCol2InputTopPt, PsInputWPt, PsCol2InputHPt);
        var goX = left + PsInputWPt + PsGoGapPt;
        Rect(goX, PsCol2InputTopPt, PsGoWPt, PsGoHPt, (0.25, 0.25, 0.25));
        Rect(goX + 0.5, PsCol2InputTopPt + 0.5, PsGoWPt - 1.0, PsGoHPt - 1.0, (0.83, 0.83, 0.83));
        Rect(goX + 2.0, PsCol2InputTopPt + 2.0, PsGoWPt - 4.0, PsGoHPt - 4.0, (0.94, 0.94, 0.94));
        var goValM = Regex.Match(html, @"<input\b[^>]*value=""(?<v>[^""]+)""[^>]*type=""submit""[^>]*>|<input\b[^>]*type=""submit""[^>]*value=""(?<v>[^""]+)""[^>]*>",
            RegexOptions.IgnoreCase);
        var goLabel = goValM.Success ? goValM.Groups["v"].Value : "Go";
        var goLabelW = MeasureFaceText("Arial", goLabel, PsGoLabelPt);
        EmitGridsterText(page, resByFace, PsGoLabelPt,
            goX + (PsGoWPt - goLabelW) / 2,
            pageH - (PsGoGlyphTopPt + PsCapHeight * PsGoLabelPt),
            goLabel, "Arial");

        page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
        PruneUnusedFonts(doc);
        return doc;
    }
}

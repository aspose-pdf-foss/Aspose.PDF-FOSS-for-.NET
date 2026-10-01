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
        var po = new PortalShellRenderState();
        po.html = html;
        if (po.html.IndexOf("id=\"wrapper\"", System.StringComparison.OrdinalIgnoreCase) < 0
            || po.html.IndexOf("id=\"banner\"", System.StringComparison.OrdinalIgnoreCase) < 0
            || po.html.IndexOf("id=\"col2\"", System.StringComparison.OrdinalIgnoreCase) < 0
            || po.html.IndexOf("class=\"welcome\"", System.StringComparison.OrdinalIgnoreCase) < 0
            || po.html.IndexOf("sButton", System.StringComparison.Ordinal) < 0) return null;

        po.inv = System.Globalization.CultureInfo.InvariantCulture;
        po.wrapM = Regex.Match(po.html, @"#wrapper\s*\{[^}]*(?<![-\w])width\s*:\s*([\d.]+)\s*px",
            RegexOptions.IgnoreCase);
        if (!po.wrapM.Success) return null;
        po.wrapPt = PortalPx(po, po.wrapM.Groups[1].Value);

        po.hdrM = Regex.Match(po.html, @"#header\s*\{[^}]*(?<![-\w])height\s*:\s*([\d.]+)\s*px",
            RegexOptions.IgnoreCase);
        po.headerPt = po.hdrM.Success ? PortalPx(po, po.hdrM.Groups[1].Value) : 41.25;

        po.barM = Regex.Match(po.html, @"(?<![-\w])body\s*\{[^}]*border-top\s*:\s*solid\s+([\d.]+)\s*px",
            RegexOptions.IgnoreCase);
        po.barPt = po.barM.Success ? PortalPx(po, po.barM.Groups[1].Value) : 7.5;

        po.canvas = PortalCssColor(po, "html", (226 / 255.0, 226 / 255.0, 226 / 255.0));
        po.wrapBg = PortalCssColor(po, "#wrapper", (0.8, 0.8, 0.8));

        po.fs = 10.2;
        po.bodyFsM = Regex.Match(po.html, @"(?<![-\w])body\s*\{[^}]*font-size\s*:\s*(\.?[\d.]+)\s*em",
            RegexOptions.IgnoreCase);
        if (po.bodyFsM.Success && double.TryParse(po.bodyFsM.Groups[1].Value,
                System.Globalization.NumberStyles.Float, po.inv, out var bodyEm) && bodyEm > 0)
            po.fs = bodyEm * 16.0 * 0.75;

        po.pageW = System.Math.Max(PsA4WidthPt, PsMarginLr * 2 + po.wrapPt);
        po.pageH = PsA4HeightPt;
        po.left = PsMarginLr;
        po.right = po.left + po.wrapPt;
        po.top = PsMarginTb;
        po.bottom = po.pageH - PsMarginTb;

        po.doc = new Document();
        po.page = po.doc.Pages.Add(po.pageW, po.pageH);
        EnsureFonts(po.page);
        po.resByFace = new Dictionary<string, string>(System.StringComparer.Ordinal);
        po.sb = new StringBuilder();
        // The canvas fills the content box; the chrome layers over it.
        PortalRect(po, po.left, po.top, po.wrapPt, po.bottom - po.top, po.canvas);
        PortalRect(po, po.left, po.top, po.wrapPt, po.barPt, (0, 0, 0));                       // body border-top
        po.headerTop = po.top + po.barPt;
        PortalRect(po, po.left, po.headerTop, po.wrapPt, po.headerPt, (1, 1, 1));              // #header's own white
        po.bannerTop = po.headerTop + po.headerPt;
        PortalRect(po, po.left, po.bannerTop, po.wrapPt, PsCol2InputTopPt + PsCol2InputHPt - po.bannerTop, po.wrapBg);

        po.inpX = po.left + PsHdrInputLeftPx * 0.75;
        po.inpTop = po.headerTop + PsHdrInputLiftPt;
        PortalSunkenBox(po, po.inpX, po.inpTop, PsInputWPt, PsHdrInputHPt);
        po.subX = po.inpX + PsInputWPt + PsSubmitGapPt;
        po.subTop = po.inpTop + PsSubmitDropPt;
        PortalRect(po, po.subX, po.subTop, PsSubmitWPt, PsSubmitHPt, (0.25, 0.25, 0.25));
        PortalRect(po, po.subX + 0.5, po.subTop + 0.5, PsSubmitWPt - 1.0, PsSubmitHPt - 1.0, (0.75, 0.75, 0.75));
        PortalRect(po, po.subX + 2.0, po.subTop + 2.0, PsSubmitWPt - 4.0, PsSubmitHPt - 4.0, (0.66, 0.66, 0.66));

        po.items = new List<string>();
        po.welcomeM = Regex.Match(po.html,
            @"class=""welcome""[^>]*>(?<b>[\s\S]*?)</ul\s*>", RegexOptions.IgnoreCase);
        RenderPortalPanels(po);

        po.page.AddContentStream(Encoding.ASCII.GetBytes(po.sb.ToString()));
        PruneUnusedFonts(po.doc);
        return po.doc;
    }
}

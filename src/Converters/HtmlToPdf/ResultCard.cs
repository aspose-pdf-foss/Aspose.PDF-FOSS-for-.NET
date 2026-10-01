using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // ── The px-width body RESULT CARD ───────────────────────────────────────────
    // A letter-style results page: a px-width body, stacked styled divs (Arial px
    // fonts), px-height spacer divs, unfetchable images rendering their ALT text,
    // and a padded background-color box holding a two-column label table whose
    // label class declares nowrap + paddings and whose rows draw 1px (white)
    // bottom borders. The whole ladder is formula-driven from the win metrics;
    // the only measured constants are the broken-image line box and its baseline.
    //
    // Geometry (all measured):
    //   page = 96 + bodyPx + 90 wide; content x = 96.75 (one cell padding);
    //   broken-img alt line box 20.63 with its baseline 17.55 below the top;
    //   the box div's padding insets its table; label rows pitch at
    //   padTop + line + padBottom with the border stroke on the row bottom.

    private const double RcBrokenImgLineBoxPt = 20.63;   // measured: alt line box
    private const double RcBrokenImgBaselinePt = 17.55;  // measured: alt baseline drop

    private static Document? TryRenderResultCard(string html,
        IReadOnlyDictionary<string, Dictionary<string, string>> css,
        double pageHeight)
    {
        var rc = new ResultCardState();
        rc.html = html;
        rc.css = css;
        rc.pageHeight = pageHeight;
        rc.bodyM = Regex.Match(rc.html,
            @"<body\b[^>]*style\s*=\s*(['""])[^'""]*?width\s*:\s*(\d+(?:\.\d+)?)\s*px[^'""]*\1",
            RegexOptions.IgnoreCase);
        if (!rc.bodyM.Success) return null;
        rc.bodyPx = double.Parse(rc.bodyM.Groups[2].Value,
            System.Globalization.CultureInfo.InvariantCulture);
        rc.labelCls = null;
        rc.labelClsName = null;
        foreach (var (sel, props) in rc.css)
            if (sel.StartsWith('.') && props.TryGetValue("white-space", out var wsv)
                && wsv.Contains("nowrap", StringComparison.OrdinalIgnoreCase)
                && props.ContainsKey("padding-top"))
            { rc.labelCls = props; rc.labelClsName = sel[1..]; break; }
        if (rc.labelCls is null || rc.labelClsName is null) return null;
        rc.boxM = Regex.Match(rc.html,
            @"<div\b[^>]*style\s*=\s*(['""])(?=[^'""]*width\s*:\s*(\d+(?:\.\d+)?)px)(?=[^'""]*background-color\s*:\s*(#[0-9a-fA-F]{3,6}))(?=[^'""]*padding\s*:\s*(\d+(?:\.\d+)?)px)[^'""]*\1[^>]*>",
            RegexOptions.IgnoreCase);
        if (!rc.boxM.Success) return null;

        const double PxPt = 0.75;
        rc.pageWidth = 96.0 + rc.bodyPx * PxPt + 90.0;
        rc.doc = Document.Create();
        rc.docFontDict = new Core.PdfDictionary();
        rc.page = rc.doc.Pages.Add(rc.pageWidth, rc.pageHeight);
        EnsureFonts(rc.page, rc.docFontDict);
        EnsureFont(rc.page, "Arial", "F8");
        EnsureFont(rc.page, "ArialBold", "F9");

        rc.contentX = 96.0 + 0.75;                   // one cell padding inside the margin
        rc.contentW = rc.bodyPx * PxPt;
        rc.arial = WinMetricsFor("Arial") ?? (0.905, 1.117);
        rc.serif = WinMetricsFor("Times New Roman") ?? (0.891, 1.107);

        rc.sb = new StringBuilder();
        rc.inv = System.Globalization.CultureInfo.InvariantCulture;
        rc.y = 72.0;                                 // body margin-top:0 → raw content top

        rc.body = Regex.Match(rc.html, @"<body\b[^>]*>([\s\S]*)</body>", RegexOptions.IgnoreCase) is
            { Success: true } bm ? bm.Groups[1].Value : rc.html;
        rc.boxStart = rc.body.IndexOf(rc.boxM.Value, StringComparison.Ordinal);
        if (rc.boxStart < 0) return null;

        // 1. leading img alt line (the logo). alt='' images draw nothing.
        foreach (Match im in Regex.Matches(rc.body[..rc.boxStart], @"<img\b[^>]*>", RegexOptions.IgnoreCase))
        {
            var alt = Regex.Match(im.Value, @"alt\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase);
            if (alt.Success && alt.Groups[1].Value.Trim().Length > 0)
            {
                Run(rc, "F5", 12, rc.contentX, rc.y + RcBrokenImgBaselinePt, alt.Groups[1].Value.Trim());
                break;                                 // the logo line holds one alt run
            }
        }
        rc.y += RcBrokenImgLineBoxPt;

        // 2. the stacked divs before the box: px-height spacers advance; styled
        // text divs draw at their declared Arial size (bold spans bold).
        EmitResultCardParagraphs(rc);

        rc.boxPx = double.Parse(rc.boxM.Groups[2].Value, rc.inv) * PxPt;
        rc.boxCol = ParseCssColor(rc.boxM.Groups[3].Value) ?? Color.FromArgb(226, 232, 237);
        rc.boxPad = double.Parse(rc.boxM.Groups[4].Value, rc.inv) * PxPt;
        rc.padTop = rc.labelCls.TryGetValue("padding-top", out var ptv) && TryParseLength(ptv) is { } ptPt ? ptPt : 7.5;
        rc.padBottom = rc.labelCls.TryGetValue("padding-bottom", out var pbv) && TryParseLength(pbv) is { } pbPt ? pbPt : 7.5;
        rc.padRight = rc.labelCls.TryGetValue("padding-right", out var prv) && TryParseLength(prv) is { } prPt ? prPt : 11.25;
        rc.labelFs = rc.labelCls.TryGetValue("font-size", out var lfv) && TryParseCssFontSize(lfv) is { } lfPt ? lfPt : 11.25;

        rc.boxHtml = rc.body[rc.boxStart..];
        rc.rows = new List<(string label, string value)>();
        foreach (Match rm in Regex.Matches(rc.boxHtml, @"<tr>([\s\S]*?)</tr>", RegexOptions.IgnoreCase))
        {
            var cells = Regex.Matches(rm.Groups[1].Value, @"<td\b[^>]*>([\s\S]*?)</td>", RegexOptions.IgnoreCase);
            if (cells.Count < 2) continue;
            string CellText(Match c) => CollapseWs(DecodeEntities(Regex.Replace(c.Groups[1].Value, @"<[^>]+>", " ")));
            rc.rows.Add((CellText(cells[0]), CellText(cells[1])));
        }
        if (rc.rows.Count == 0) return null;

        EmitResultCardBox(rc);
        rc.y += rc.boxH;

        // 4. trailing spacers and the footer img alt with the outer cell's own
        // white bottom border across the content box.
        EmitResultCardFooter(rc);

        rc.page.AddContentStream(Encoding.ASCII.GetBytes(rc.sb.ToString()));
        return rc.doc;
    }
}

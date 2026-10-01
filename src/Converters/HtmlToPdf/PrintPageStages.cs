using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The text of a markup fragment: tags dropped, entities decoded, whitespace collapsed.</summary>
    private static string FlatPrintText(string frag)
        => Regex.Replace(DecodeEntities(Regex.Replace(frag, @"<[^>]+>", " ")),
            @"\s+", " ").Trim();

    /// <summary>The flattened text of the band div carrying the class, empty when absent.</summary>
    private static string PrintBandText(PrintPageState pp, string cls)
    {
        var m = Regex.Match(pp.html,
            @"<div\b[^>]*class\s*=\s*['""]" + cls + @"['""][^>]*>(?<body>[\s\S]*?)</div>",
            RegexOptions.IgnoreCase);
        if (!m.Success) return "";
        // display:none controls (the demo's PRINT ME button) leave no text
        return FlatPrintText(Regex.Replace(m.Groups["body"].Value,
            @"<button\b[\s\S]*?</button>", " ", RegexOptions.IgnoreCase));
    }

    /// <summary>Open a new sheet: the canvas tint, the page box, the two bands with their
    /// demo borders, and the header / footer text.</summary>
    private static Page NewPrintSheet(PrintPageState pp)
    {
        var pg = pp.doc.Pages.Add(pp.pageWidth, pp.pageHeight);
        EnsureFonts(pg);
        var sb = new StringBuilder();
        void Fill(Color? c, double x0, double yTop, double x1, double yBot)
        {
            if (c is not { } cc) return;
            sb.Append(Compat.Format(pp.invc,
                $"q {cc.R / 255.0:0.###} {cc.G / 255.0:0.###} {cc.B / 255.0:0.###} rg " +
                $"{x0:0.##} {pp.pageHeight - yBot:0.##} {x1 - x0:0.##} {yBot - yTop:0.##} re f Q\n"));
        }
        // canvas tint, the page's own white box, and the two bands
        Fill(pp.canvasBg, pp.contentL, PpMarginTopPt, pp.contentR, pp.canvasBot);
        Fill(Color.FromArgb(255, 255, 255), pp.contentL, PpMarginTopPt, pp.contentR, pp.canvasBot);
        Fill(pp.hdrBg, pp.contentL, PpMarginTopPt, pp.contentR, PpMarginTopPt + pp.hdrH + pp.hdrBorder);
        Fill(pp.ftrBg, pp.contentL, pp.ftrBandTop, pp.contentR, pp.canvasBot);
        // the demo borders draw as their own hairlines
        if (pp.hdrBorder > 0)
            sb.Append(Compat.Format(pp.invc,
                $"q 0 0 0 RG {pp.hdrBorder:0.##} w {pp.contentL:0.##} {pp.pageHeight - PpMarginTopPt - pp.hdrH - pp.hdrBorder / 2:0.##} m {pp.contentR:0.##} {pp.pageHeight - PpMarginTopPt - pp.hdrH - pp.hdrBorder / 2:0.##} l S Q\n"));
        if (pp.ftrBorder > 0)
            sb.Append(Compat.Format(pp.invc,
                $"q 0 0 0 RG {pp.ftrBorder:0.##} w {pp.contentL:0.##} {pp.pageHeight - pp.ftrBandTop - pp.ftrBorder / 2:0.##} m {pp.contentR:0.##} {pp.pageHeight - pp.ftrBandTop - pp.ftrBorder / 2:0.##} l S Q\n"));
        pg.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
        if (pp.headerText.Length > 0)
        {
            var hw = MeasureFaceText("Times New Roman", pp.headerText, PpFontPt);
            var hx = pp.headerCentered ? pp.contentL + (pp.bodyW - hw) / 2 : pp.contentL;
            EmitPositionedRun(pg, "F5", PpFontPt, hx,
                pp.pageHeight - PpMarginTopPt - pp.drop12, pp.headerText);
        }
        if (pp.footerText.Length > 0)
            EmitPositionedRun(pg, "F5", PpFontPt, pp.contentL,
                pp.pageHeight - pp.ftrBandTop - pp.ftrBorder - pp.drop12, pp.footerText);
        return pg;
    }

    /// <summary>Lay the content pages out: each .page div's wrapped rows on the current sheet, spilling onto fresh sheets above the footer band.</summary>
    private static void LayoutPrintPages(PrintPageState pp)
    {
        for (var pi = 0; pi < pp.pages.Count; pi++)
        {
            var (text, lineBox) = pp.pages[pi];
            var lines = MeasuredWordWrap(text, pp.cellR - pp.cellL, "Times New Roman", PpFontPt);
            var drop = MetricBaselineDrop(PpFontPt, lineBox, pp.fm);
            var i = 0;
            while (i < lines.Length)
            {
                // rows that fit above the footer band on this sheet
                var fit = Math.Max(1, (int)Math.Floor((pp.ftrBandTop - pp.y) / lineBox));
                var n = Math.Min(fit, lines.Length - i);
                // the .page div paints its own white box behind its rows
                pp.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(pp.invc,
                    $"q 1 1 1 rg {pp.cellL:0.##} {pp.pageHeight - pp.y - n * lineBox:0.##} {pp.cellR - pp.cellL:0.##} {n * lineBox:0.##} re f Q\n")));
                for (var k = 0; k < n; k++, i++)
                    EmitPositionedRun(pp.page, "F5", PpFontPt, pp.cellL,
                        pp.pageHeight - (pp.y + k * lineBox) - drop, lines[i]);
                pp.y += n * lineBox;
                if (i < lines.Length) { pp.page = NewPrintSheet(pp); pp.y = pp.contentTop; }
            }
            // page-break-after:always — the next .page div opens a fresh sheet
            if (pi < pp.pages.Count - 1)
            {
                pp.page = NewPrintSheet(pp);
                pp.y = pp.contentTop;
            }
        }
    }

    /// <summary>Collect the leaf .page divs' text and line-box height.</summary>
    private static void ReadPrintPageDivs(PrintPageState pp)
    {
        foreach (Match pm in Regex.Matches(pp.html,
            @"<div\b[^>]*class\s*=\s*['""]page['""](?<attrs>[^>]*)>", RegexOptions.IgnoreCase))
        {
            // find the matching close by depth
            var depth = 1;
            var end = pp.html.Length;
            foreach (Match t in Regex.Matches(pp.html[(pm.Index + pm.Length)..],
                @"<div\b|</div\s*>", RegexOptions.IgnoreCase))
            {
                depth += t.Value.StartsWith("</") ? -1 : 1;
                if (depth == 0) { end = pm.Index + pm.Length + t.Index; break; }
            }
            var body = pp.html[(pm.Index + pm.Length)..end];
            if (Regex.IsMatch(body, @"class\s*=\s*['""]page['""]", RegexOptions.IgnoreCase))
                continue;                           // the outer wrapper
            var lb = PpLineBoxPt;
            var lhM = Regex.Match(pm.Groups["attrs"].Value,
                @"line-height\s*:\s*([\d.]+)\s*[;'""]", RegexOptions.IgnoreCase);
            if (lhM.Success && double.TryParse(lhM.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var lhF)
                && lhF > 0)
                lb = lhF * PpFontPt;
            var txt = FlatPrintText(body);
            if (txt.Length > 0) pp.pages.Add((txt, lb));
        }
    }
}

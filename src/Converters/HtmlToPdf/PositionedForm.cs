using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // ── The centred-wrapper POSITIONED FORM ─────────────────────────────────────
    // A form-builder export: an auto-centred px-width white wrapper, a padded
    // relative canvas, and absolutely-positioned "field" boxes (style border,
    // fill, 5px padding) holding an inline-block question div beside a response
    // (underline divs, radio/checkbox tables, textarea rules). The engine sizes
    // the page 96 + wrapper + 90 wide, keeps the caller's page height, IGNORES
    // the sheet's page-break <br>s (measured: section 2 flows on and its box
    // splits at the content bottom), and draws Arial throughout.
    //
    // All geometry measured: content x = 96 + padding; the first section
    // canvas opens 19.05 below the h1 baseline; question text seats
    // halfLeading + ascent below its content top (13px Arial in a 16px line);
    // an inline-block neighbour follows one space advance after the question.

    private const double PfSecAfterH1Pt = 19.05;      // h1 baseline -> section canvas top
    private const double PfSecGapPt = 12.8;           // section border bottom -> next border top
    private const double PfFieldPadPt = 3.75;         // the 5px field content padding
    private const double PfH1BaselinePt = 124.8;      // measured: content top 78 + pad + h1 seat

    private static Document? TryRenderPositionedForm(string html,
        HtmlLoadOptions? options, double pageHeight)
    {
        var wrapM = Regex.Match(html,
            @"<div\b[^>]*style\s*=\s*(['""])[^'""]*margin:\s*0\s+auto;[^'""]*width:\s*(\d+(?:\.\d+)?)px[^'""]*\1",
            RegexOptions.IgnoreCase);
        if (!wrapM.Success) return null;
        if (Regex.Matches(html, @"position\s*:\s*absolute\s*;\s*left", RegexOptions.IgnoreCase).Count < 3)
            return null;
        if (!html.Contains("-bounding", StringComparison.OrdinalIgnoreCase)) return null;

        const double PxPt = 0.75;
        var wrapPx = double.Parse(wrapM.Groups[2].Value,
            System.Globalization.CultureInfo.InvariantCulture);
        var pageWidth = 96.0 + wrapPx * PxPt + 90.0;
        var doc = Document.Create();
        var docFontDict = new Core.PdfDictionary();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        EnsureFonts(page, docFontDict);
        EnsureFont(page, "Arial", "F8");
        EnsureFont(page, "ArialBold", "F9");

        var contentBottom = pageHeight - 72.0;
        var wrapX = 96.0;
        var wrapW = wrapPx * PxPt;
        var arial = WinMetricsFor("Arial") ?? (0.905, 1.117);
        var fs = 9.75;                                  // the sheet's 13px body font
        var lineBox = 12.0;                             // its 16px line box
        var drop = (lineBox - fs * arial.sum) / 2 + fs * arial.asc;
        var spaceAdv = MeasureFaceText("Arial", " ", fs);

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var sb1 = new StringBuilder();                  // page 1 ops
        var sb2 = new StringBuilder();                  // page 2 ops
        StringBuilder SbFor(double yTd) => yTd <= contentBottom ? sb1 : sb2;
        double MapY(double yTd) => yTd <= contentBottom ? yTd : yTd - contentBottom + 72.0;

        void Text(double x, double yTd, string t, bool bold = false, double size = 0)
        {
            var sz = size > 0 ? size : fs;
            SbFor(yTd).AppendLine(string.Create(inv,
                $"BT /{(bold ? "F9" : "F8")} {sz:0.##} Tf 1 0 0 1 {x:F2} {pageHeight - MapY(yTd):F2} Tm ({EscapePdfString(t)}) Tj ET"));
        }
        void Fill(double x, double yTd, double w, double h, Color c)
        {
            // split across the page boundary
            var y0 = yTd; var y1 = yTd + h;
            foreach (var (a, b) in new[] { (Math.Min(y0, contentBottom), Math.Min(y1, contentBottom)),
                                           (Math.Max(y0, contentBottom), Math.Max(y1, contentBottom)) })
            {
                if (b - a < 0.01) continue;
                var sb = a >= contentBottom ? sb2 : sb1;
                var ay = a >= contentBottom ? a - contentBottom + 72.0 : a;
                var by = b >= contentBottom ? b - contentBottom + 72.0 : b;
                sb.AppendLine(string.Create(inv,
                    $"q {c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} rg {x:F2} {pageHeight - by:F2} {w:F2} {by - ay:F2} re f Q"));
            }
        }
        void HStroke(double x0, double x1, double yTd, Color c, double w)
        {
            SbFor(yTd).AppendLine(string.Create(inv,
                $"q {c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} RG {w:0.##} w {x0:F2} {pageHeight - MapY(yTd):F2} m {x1:F2} {pageHeight - MapY(yTd):F2} l S Q"));
        }
        void VStroke(double x, double y0Td, double y1Td, Color c, double w)
        {
            foreach (var (a, b) in new[] { (Math.Min(y0Td, contentBottom), Math.Min(y1Td, contentBottom)),
                                           (Math.Max(y0Td, contentBottom), Math.Max(y1Td, contentBottom)) })
            {
                if (b - a < 0.01) continue;
                var sb = a >= contentBottom ? sb2 : sb1;
                var ay = a >= contentBottom ? a - contentBottom + 72.0 : a;
                var by = b >= contentBottom ? b - contentBottom + 72.0 : b;
                sb.AppendLine(string.Create(inv,
                    $"q {c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} RG {w:0.##} w {x:F2} {pageHeight - ay:F2} m {x:F2} {pageHeight - by:F2} l S Q"));
            }
        }
        void Box(double x, double yTd, double w, double h, Color c, double bw)
        {
            HStroke(x, x + w, yTd + bw / 2, c, bw);
            HStroke(x, x + w, yTd + h - bw / 2, c, bw);
            VStroke(x + bw / 2, yTd, yTd + h, c, bw);
            VStroke(x + w - bw / 2, yTd, yTd + h, c, bw);
        }
        void Circle(double cx, double yTd, double r)
        {
            const double k = 0.5523;
            var cy = pageHeight - MapY(yTd);
            SbFor(yTd).AppendLine(string.Create(inv,
                $"q 0 0 0 RG 0.75 w {cx + r:F2} {cy:F2} m " +
                $"{cx + r:F2} {cy + k * r:F2} {cx + k * r:F2} {cy + r:F2} {cx:F2} {cy + r:F2} c " +
                $"{cx - k * r:F2} {cy + r:F2} {cx - r:F2} {cy + k * r:F2} {cx - r:F2} {cy:F2} c " +
                $"{cx - r:F2} {cy - k * r:F2} {cx - k * r:F2} {cy - r:F2} {cx:F2} {cy - r:F2} c " +
                $"{cx + k * r:F2} {cy - r:F2} {cx + r:F2} {cy - k * r:F2} {cx + r:F2} {cy:F2} c S Q"));
        }

        // h1 header
        var contentX = wrapX + 15.0;                    // the 20px canvas padding
        var h1M = Regex.Match(html, @"<h1\b[^>]*>([\s\S]*?)</h1>", RegexOptions.IgnoreCase);
        if (h1M.Success)
            Text(contentX, PfH1BaselinePt,
                CollapseWs(DecodeEntities(Regex.Replace(h1M.Groups[1].Value, "<[^>]+>", " "))),
                bold: true, size: 19.5);

        // the sections, in order
        var secY = PfH1BaselinePt + PfSecAfterH1Pt;
        var secX = wrapX + 15.0 + 0.75;                 // 1px white border div inset
        var lastBottom = secY;
        foreach (Match secM in Regex.Matches(html,
            @"<div\b[^>]*style\s*=\s*(['""])[^'""]*position:\s*relative;[^'""]*height:\s*(\d+(?:\.\d+)?)px[^'""]*\1\s*>",
            RegexOptions.IgnoreCase))
        {
            var secH = double.Parse(secM.Groups[2].Value, inv) * PxPt;
            // section body = to the matching close of this div
            var body = DivBodyAt(html, secM.Index + secM.Length);

            foreach (Match fb in Regex.Matches(body,
                @"<div\b[^>]*style\s*=\s*(['""])position:\s*absolute;left:\s*(-?\d+(?:\.\d+)?)px;top:\s*(-?\d+(?:\.\d+)?)px;?(?:width:\s*(\d+(?:\.\d+)?)px;?)?(?:height:\s*(\d+(?:\.\d+)?)px;?)?\s*\1[^>]*>",
                RegexOptions.IgnoreCase))
            {
                var fx = secX + double.Parse(fb.Groups[2].Value, inv) * PxPt;
                var fy = secY + double.Parse(fb.Groups[3].Value, inv) * PxPt;
                var fw = fb.Groups[4].Success ? double.Parse(fb.Groups[4].Value, inv) * PxPt : 0;
                var fh = fb.Groups[5].Success ? double.Parse(fb.Groups[5].Value, inv) * PxPt : 0;
                var fBody = DivBodyAt(body, fb.Index + fb.Length);

                // the content div: border colour/width, optional background
                var cdM = Regex.Match(fBody, @"<div\b[^>]*style\s*=\s*(['""])([^'""]*border-color[^'""]*)\1[^>]*>",
                    RegexOptions.IgnoreCase);
                var st = cdM.Success ? cdM.Groups[2].Value : "";
                var bCol = Regex.Match(st, @"border-color:\s*(#[0-9a-fA-F]{3,6})") is { Success: true } bcm
                    ? ParseCssColor(bcm.Groups[1].Value) ?? Color.FromArgb(0, 0, 0) : Color.FromArgb(0, 0, 0);
                var bw = Regex.Match(st, @"border-width:\s*(\d+(?:\.\d+)?)px") is { Success: true } bwm
                    ? double.Parse(bwm.Groups[1].Value, inv) * PxPt : 0.75;
                var bgCol = Regex.Match(st, @"background-color:\s*(#[0-9a-fA-F]{3,6})") is { Success: true } bgm
                    ? ParseCssColor(bgm.Groups[1].Value) : null;

                // question text + width
                var qM = Regex.Match(fBody,
                    @"class=""pan-question-content[^""]*""\s*style=""width:\s*(\d+(?:\.\d+)?)px[^""]*""[^>]*>\s*([\s\S]*?)</div>",
                    RegexOptions.IgnoreCase);
                var plainM = Regex.Match(fBody,
                    @"class=""pan-field-content""[^>]*>\s*([\s\S]*?)</div>", RegexOptions.IgnoreCase);
                var qW = qM.Success ? double.Parse(qM.Groups[1].Value, inv) * PxPt : 0;
                var qText = CollapseWs(DecodeEntities(Regex.Replace(
                    qM.Success ? qM.Groups[2].Value : plainM.Success ? plainM.Groups[1].Value : "",
                    "<[^>]+>", " ")));

                var contentX0 = fx + bw + PfFieldPadPt;
                var contentY0 = fy + bw + PfFieldPadPt;

                // auto width: fit the single question/content line
                if (fw <= 0)
                {
                    var tw = MeasureFaceText("Arial", qText, fs);
                    fw = tw + 2 * PfFieldPadPt + 2 * bw + 1.5;
                }
                if (fh <= 0)
                {
                    // auto height: the content's own lines + padding
                    var qLines = qW > 0 ? MeasuredWordWrap(qText, qW, "Arial", fs).Length : 1;
                    fh = qLines * lineBox + 2 * PfFieldPadPt + 2 * bw + 1.5;
                }

                if (bgCol is { } bg) Fill(fx + bw, fy + bw, fw - 2 * bw, fh - 2 * bw, bg);
                Box(fx, fy, fw, fh, bCol, bw);

                var qRight = contentX0;
                if (qText.Length > 0)
                {
                    var lines = qW > 0 ? MeasuredWordWrap(qText, qW, "Arial", fs) : new[] { qText };
                    var ly = contentY0;
                    foreach (var ln in lines)
                    {
                        Text(contentX0, ly + drop, ln);
                        ly += lineBox;
                    }
                    qRight = contentX0 + (qW > 0 ? qW : 0) + spaceAdv;
                }

                // responses: underline divs (border-bottom w×h), radio/checkbox
                // tables, in source order after the question
                var respM = Regex.Match(fBody,
                    @"class=""pan-question-response[^""]*""[^>]*>([\s\S]*)$", RegexOptions.IgnoreCase);
                if (!respM.Success) continue;
                var resp = respM.Groups[1].Value;
                // question wider than the row pushes the response BELOW it
                var respBelow = qW >= 220;              // 300px+ questions stack (measured)
                var rx = respBelow ? contentX0 : qRight;
                var ry = respBelow ? contentY0 + lineBox : contentY0;

                var uy = ry;
                foreach (Match um in Regex.Matches(resp,
                    @"<div\b[^>]*style\s*=\s*(['""])[^'""]*border-bottom:[^'""]*width:\s*(\d+(?:\.\d+)?)px;height:\s*(\d+(?:\.\d+)?)px[^'""]*\1|<div\b[^>]*style\s*=\s*(['""])[^'""]*width:\s*(\d+(?:\.\d+)?)px[^'""]*border-bottom:[^'""]*height:\s*(\d+(?:\.\d+)?)px[^'""]*\4",
                    RegexOptions.IgnoreCase))
                {
                    var uw = (um.Groups[2].Success ? double.Parse(um.Groups[2].Value, inv)
                        : double.Parse(um.Groups[5].Value, inv)) * PxPt;
                    var uh = (um.Groups[3].Success ? double.Parse(um.Groups[3].Value, inv)
                        : double.Parse(um.Groups[6].Value, inv)) * PxPt;
                    HStroke(rx, rx + uw, uy + uh + 0.38, Color.FromArgb(0, 0, 0), 0.75);
                    uy += uh + 0.7;
                }

                // radio / checkbox option tables: one row per <tr>, control glyph
                // + label cell at 0.5em paddings
                var pad = 0.5 * fs;
                var rowY = ry;
                foreach (Match trM in Regex.Matches(resp, @"<tr>\s*([\s\S]*?)</tr>", RegexOptions.IgnoreCase))
                {
                    var cells = Regex.Matches(trM.Groups[1].Value, @"<td\b[^>]*>([\s\S]*?)</td>", RegexOptions.IgnoreCase);
                    if (cells.Count == 0) continue;
                    var cx = rx;
                    for (var ci = 0; ci < cells.Count; ci++)
                    {
                        var cellHtml = cells[ci].Groups[1].Value;
                        var isRadio = Regex.IsMatch(cellHtml, @"type=""radio""", RegexOptions.IgnoreCase);
                        var isCheck = Regex.IsMatch(cellHtml, @"type=""checkbox""", RegexOptions.IgnoreCase);
                        var label = CollapseWs(DecodeEntities(Regex.Replace(cellHtml, "<[^>]+>", " ")));
                        if (isRadio)
                        {
                            Circle(cx + pad + 4.1, rowY + pad + 4.5, 4.1);
                            cx += pad + 9.75 + pad;
                        }
                        else if (isCheck)
                        {
                            Box(cx + pad, rowY + pad, 9.0, 9.0, Color.FromArgb(0, 0, 0), 0.75);
                            cx += pad + 9.75 + pad;
                        }
                        else if (label.Length > 0)
                        {
                            Text(cx + pad, rowY + pad + drop, label);
                            cx += pad + MeasureFaceText("Arial", label, fs) + pad + 100.0;
                        }
                    }
                    rowY += 2 * pad + lineBox + 2.6;
                }
            }

            lastBottom = secY + secH + 0.75;            // the white border div bottom
            // the ignored page-break <br> = one 16px line between sections
            secY = lastBottom + PfSecGapPt + 0.75;
        }

        // footer h4 on the continuation page
        var h4M = Regex.Match(html, @"<h4\b[^>]*>([\s\S]*?)</h4>", RegexOptions.IgnoreCase);
        if (h4M.Success)
            Text(contentX, contentBottom + (251.2 - 72.0),
                CollapseWs(DecodeEntities(Regex.Replace(h4M.Groups[1].Value, "<[^>]+>", " "))),
                bold: true);

        // the white wrapper band under everything, both pages
        var p1Band = string.Create(inv,
            $"q 1 1 1 rg {wrapX:F2} {pageHeight - contentBottom:F2} {wrapW:F2} {contentBottom - 78.0:F2} re f Q\n");
        page.AddContentStream(Encoding.ASCII.GetBytes(p1Band + sb1));
        if (sb2.Length > 0)
        {
            var page2 = doc.Pages.Add(pageWidth, pageHeight);
            EnsureFonts(page2, docFontDict);
            EnsureFont(page2, "Arial", "F8");
            EnsureFont(page2, "ArialBold", "F9");
            var p2Band = string.Create(inv,
                $"q 1 1 1 rg {wrapX:F2} {pageHeight - 280.0:F2} {wrapW:F2} {280.0 - 72.0:F2} re f Q\n");
            page2.AddContentStream(Encoding.ASCII.GetBytes(p2Band + sb2));
        }
        return doc;
    }

    /// <summary>The inner HTML of the div OPENING at <paramref name="afterOpen"/>
    /// (the index just past its open tag) — up to its matching close.</summary>
    private static string DivBodyAt(string html, int afterOpen)
    {
        var depth = 1;
        foreach (Match t in Regex.Matches(html[afterOpen..], @"<(/?)div\b[^>]*>", RegexOptions.IgnoreCase))
        {
            if (t.Groups[1].Value.Length == 0) depth++;
            else if (--depth == 0) return html[afterOpen..(afterOpen + t.Index)];
        }
        return html[afterOpen..];
    }
}

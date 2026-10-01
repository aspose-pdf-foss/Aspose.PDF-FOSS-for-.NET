using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Positioned form field: the radio/checkbox option rows drawn.</summary>
    private static void RenderFieldOptionRows(FormFieldState ff)
    {
        ff.pad = 0.5 * ff.pf.fs;
        ff.rowY = ff.ry;
        foreach (Match trM in Regex.Matches(ff.resp, @"<tr>\s*([\s\S]*?)</tr>", RegexOptions.IgnoreCase))
        {
            var cells = Regex.Matches(trM.Groups[1].Value, @"<td\b[^>]*>([\s\S]*?)</td>", RegexOptions.IgnoreCase);
            if (cells.Count == 0) continue;
            var cx = ff.rx;
            for (var ci = 0; ci < cells.Count; ci++)
            {
                var cellHtml = cells[ci].Groups[1].Value;
                var isRadio = Regex.IsMatch(cellHtml, @"type=""radio""", RegexOptions.IgnoreCase);
                var isCheck = Regex.IsMatch(cellHtml, @"type=""checkbox""", RegexOptions.IgnoreCase);
                var label = CollapseWs(DecodeEntities(Regex.Replace(cellHtml, "<[^>]+>", " ")));
                if (isRadio)
                {
                    Circle(ff.pf, cx + ff.pad + 4.1, ff.rowY + ff.pad + 4.5, 4.1);
                    cx += ff.pad + 9.75 + ff.pad;
                }
                else if (isCheck)
                {
                    Box(ff.pf, cx + ff.pad, ff.rowY + ff.pad, 9.0, 9.0, Color.FromArgb(0, 0, 0), 0.75);
                    cx += ff.pad + 9.75 + ff.pad;
                }
                else if (label.Length > 0)
                {
                    PfText(ff.pf, cx + ff.pad, ff.rowY + ff.pad + ff.pf.drop, label);
                    cx += ff.pad + MeasureFaceText("Arial", label, ff.pf.fs) + ff.pad + 100.0;
                }
            }
            ff.rowY += 2 * ff.pad + ff.pf.lineBox + 2.6;
        }
    }

    /// <summary>Positioned form field: the response underline rules drawn.</summary>
    private static void RenderFieldUnderlines(FormFieldState ff)
    {
        ff.uy = ff.ry;
        foreach (Match um in Regex.Matches(ff.resp,
            @"<div\b[^>]*style\s*=\s*(['""])[^'""]*border-bottom:[^'""]*width:\s*(\d+(?:\.\d+)?)px;height:\s*(\d+(?:\.\d+)?)px[^'""]*\1|<div\b[^>]*style\s*=\s*(['""])[^'""]*width:\s*(\d+(?:\.\d+)?)px[^'""]*border-bottom:[^'""]*height:\s*(\d+(?:\.\d+)?)px[^'""]*\4",
            RegexOptions.IgnoreCase))
        {
            var uw = (um.Groups[2].Success ? double.Parse(um.Groups[2].Value, ff.pf.inv)
                : double.Parse(um.Groups[5].Value, ff.pf.inv)) * PxPt;
            var uh = (um.Groups[3].Success ? double.Parse(um.Groups[3].Value, ff.pf.inv)
                : double.Parse(um.Groups[6].Value, ff.pf.inv)) * PxPt;
            HStroke(ff.pf, ff.rx, ff.rx + uw, ff.uy + uh + 0.38, Color.FromArgb(0, 0, 0), 0.75);
            ff.uy += uh + 0.7;
        }
    }

    /// <summary>Positioned form field: the question text lines drawn.</summary>
    private static void RenderFieldQuestion(FormFieldState ff)
    {
        ff.qRight = ff.contentX0;
        if (ff.qText.Length > 0)
        {
            var lines = ff.qW > 0 ? MeasuredWordWrap(ff.qText, ff.qW, "Arial", ff.pf.fs) : new[] { ff.qText };
            var ly = ff.contentY0;
            foreach (var ln in lines)
            {
                PfText(ff.pf, ff.contentX0, ly + ff.pf.drop, ln);
                ly += ff.pf.lineBox;
            }
            ff.qRight = ff.contentX0 + (ff.qW > 0 ? ff.qW : 0) + ff.pf.spaceAdv;
        }
    }

    /// <summary>Positioned form field: the auto width/height settled and the box drawn.</summary>
    private static void SizeAndBoxField(FormFieldState ff)
    {
        // auto width: fit the single question/content line
        if (ff.fw <= 0)
        {
            var tw = MeasureFaceText("Arial", ff.qText, ff.pf.fs);
            ff.fw = tw + 2 * PfFieldPadPt + 2 * ff.bw + 1.5;
        }
        if (ff.fh <= 0)
        {
            // auto height: the content's own lines + padding
            var qLines = ff.qW > 0 ? MeasuredWordWrap(ff.qText, ff.qW, "Arial", ff.pf.fs).Length : 1;
            ff.fh = qLines * ff.pf.lineBox + 2 * PfFieldPadPt + 2 * ff.bw + 1.5;
        }

        if (ff.bgCol is { } bg) Fill(ff.pf, ff.fx + ff.bw, ff.fy + ff.bw, ff.fw - 2 * ff.bw, ff.fh - 2 * ff.bw, bg);
        Box(ff.pf, ff.fx, ff.fy, ff.fw, ff.fh, ff.bCol, ff.bw);
    }

    /// <summary>Positioned form field: the box geometry, border style and question read off the html.</summary>
    private static void ParseFieldBox(FormFieldState ff)
    {
        ff.fx = ff.pf.secX + double.Parse(ff.fb.Groups[2].Value, ff.pf.inv) * PxPt;
        ff.fy = ff.pf.secY + double.Parse(ff.fb.Groups[3].Value, ff.pf.inv) * PxPt;
        ff.fw = ff.fb.Groups[4].Success ? double.Parse(ff.fb.Groups[4].Value, ff.pf.inv) * PxPt : 0;
        ff.fh = ff.fb.Groups[5].Success ? double.Parse(ff.fb.Groups[5].Value, ff.pf.inv) * PxPt : 0;
        ff.fBody = DivBodyAt(ff.sn.body, ff.fb.Index + ff.fb.Length);

        ff.cdM = Regex.Match(ff.fBody, @"<div\b[^>]*style\s*=\s*(['""])([^'""]*border-color[^'""]*)\1[^>]*>",
            RegexOptions.IgnoreCase);
        ff.st = ff.cdM.Success ? ff.cdM.Groups[2].Value : "";
        ff.bCol = Regex.Match(ff.st, @"border-color:\s*(#[0-9a-fA-F]{3,6})") is { Success: true } bcm
            ? ParseCssColor(bcm.Groups[1].Value) ?? Color.FromArgb(0, 0, 0) : Color.FromArgb(0, 0, 0);
        ff.bw = Regex.Match(ff.st, @"border-width:\s*(\d+(?:\.\d+)?)px") is { Success: true } bwm
            ? double.Parse(bwm.Groups[1].Value, ff.pf.inv) * PxPt : 0.75;
        ff.bgCol = Regex.Match(ff.st, @"background-color:\s*(#[0-9a-fA-F]{3,6})") is { Success: true } bgm
            ? ParseCssColor(bgm.Groups[1].Value) : null;

        ff.qM = Regex.Match(ff.fBody,
            @"class=""pan-question-content[^""]*""\s*style=""width:\s*(\d+(?:\.\d+)?)px[^""]*""[^>]*>\s*([\s\S]*?)</div>",
            RegexOptions.IgnoreCase);
        ff.plainM = Regex.Match(ff.fBody,
            @"class=""pan-field-content""[^>]*>\s*([\s\S]*?)</div>", RegexOptions.IgnoreCase);
        ff.qW = ff.qM.Success ? double.Parse(ff.qM.Groups[1].Value, ff.pf.inv) * PxPt : 0;
        ff.qText = CollapseWs(DecodeEntities(Regex.Replace(
            ff.qM.Success ? ff.qM.Groups[2].Value : ff.plainM.Success ? ff.plainM.Groups[1].Value : "",
            "<[^>]+>", " ")));

        ff.contentX0 = ff.fx + ff.bw + PfFieldPadPt;
        ff.contentY0 = ff.fy + ff.bw + PfFieldPadPt;
    }
}

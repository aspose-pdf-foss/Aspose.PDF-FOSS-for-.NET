using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The step-rows render helpers: the ops flush, the page break, the run emitter, horizontal and vertical rules, flattened text and the div-close scan.
    private static void FlushOps(StepRowsState sw)
    {
        if (sw.sb.Length == 0) return;
        sw.page.AddContentStream(Encoding.ASCII.GetBytes(sw.sb.ToString()));
        sw.sb.Clear();
    }

    private static void NewPage(StepRowsState sw)
    {
        FlushOps(sw);
        sw.page = sw.doc.Pages.Add(sw.pageWidth, sw.pageHeight);
        EnsureFonts(sw.page);
        RegisterProcedureFonts(sw.page);
    }

    private static void Run(StepRowsState sw, string res, double fs, double x, double glyphTopTd, string text)
    {
        // glyph-top anchored: baseline = top + the face ascent
        var baseTd = glyphTopTd + fs * ArialAscentEm;
        sw.sb.Append(Compat.Format(sw.invc,
            $"BT /{res} {fs:0.##} Tf 1 0 0 1 {x:0.##} {sw.pageHeight - baseTd:0.##} Tm ({EscapePdfString(text)}) Tj ET\n"));
    }

    private static void HLine(StepRowsState sw, double x0, double x1, double yTd2, double w = 0.75)
        => sw.sb.Append(Compat.Format(sw.invc,
            $"q 0 0 0 RG {w:0.##} w {x0:0.##} {sw.pageHeight - yTd2:0.##} m {x1:0.##} {sw.pageHeight - yTd2:0.##} l S Q\n"));

    private static void VLine(StepRowsState sw, double x, double y0Td, double y1Td)
        => sw.sb.Append(Compat.Format(sw.invc,
            $"q 0 0 0 RG 0.75 w {x:0.##} {sw.pageHeight - y0Td:0.##} m {x:0.##} {sw.pageHeight - y1Td:0.##} l S Q\n"));

    private static string Flat(StepRowsState sw, string frag)
        => Regex.Replace(DecodeEntities(Regex.Replace(frag, @"<[^>]+>", " ")), @"\s+", " ").Trim();

    // balanced-div close scan (local: the shared helper lives in another arm)
    private static int DivClose(string s, int afterOpen)
    {
        var depth = 1;
        foreach (Match t in Regex.Matches(s[afterOpen..], @"<div\b|</div\s*>",
            RegexOptions.IgnoreCase))
        {
            depth += t.Value.StartsWith("</") ? -1 : 1;
            if (depth == 0) return afterOpen + t.Index;
        }
        return s.Length;
    }
}

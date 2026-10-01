using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// Ishares fact sheet: 
    private static string IfsFlat(string s) => DecodeEntities(
        Regex.Replace(s, @"<[^>]+>", "")).Replace(' ', ' ').Trim();

    // the 0.001 pt "@[" "]@" wrappers are invisible markers
    private static string StripMarkers(IsharesFactSheetState ix, string s) => Regex.Replace(s,
        @"<span style=""font-size: 0\.001pt;"">[^<]*</span>", "");

    private static double MW(IsharesFactSheetState ix, string s, double fs, string face = "Arial") => MeasureFaceText(face, s, fs);

    private static void Run(IsharesFactSheetState ix, string res, double fs, double x, double yTd, string text)
        => ix.sb.AppendLine(Compat.Format(ix.inv,
            $"BT /{res} {fs:0.##} Tf 1 0 0 1 {x:F2} {IfsPageH - yTd:F2} Tm ({EscapePdfString(text)}) Tj ET"));

    private static void HRule(IsharesFactSheetState ix, double x0, double x1, double yTd, double w)
        => ix.sb.AppendLine(Compat.Format(ix.inv,
            $"q 0 0 0 RG {w:0.##} w {x0:F2} {IfsPageH - yTd:F2} m {x1:F2} {IfsPageH - yTd:F2} l S Q"));

    // the DeltaSymbol triangle box: solid, centred on the ins segment's start,
    // hanging from the baseline (css `top: 100%`, drawn as fat border strokes
    // that merge into a solid block)
    private static void Delta(IsharesFactSheetState ix, double cx, double yTd)
        => ix.sb.AppendLine(Compat.Format(ix.inv,
            $"q 0 0 0 rg {cx - IfsDeltaW / 2:F2} {IfsPageH - yTd - IfsDeltaH:F2} {IfsDeltaW:0.##} {IfsDeltaH:0.##} re f Q"));
}

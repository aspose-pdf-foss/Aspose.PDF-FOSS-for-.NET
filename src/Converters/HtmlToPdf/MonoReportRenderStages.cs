using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The stages of the mono report render: the head table and one row.</summary>
    private static bool RenderMonoReportRow(MonoReportRenderState mr, int ri)
    {
        var rowH = ri == 0 || ri == mr.lastRi ? MonoFirstRowPt : MonoRowPt;
        if (ri == mr.lastRi && mr.lastRi > 0) mr.rowTop += MonoRulePt;
        var boxLeft = mr.contentLeft;
        for (var c = 0; c < mr.rows[ri].Count && c < mr.nCols; c++)
        {
            var boxW = mr.colW[c] + MonoCellBoxPadPt;
            if (mr.rows[ri][c].Bg is { } cbg)
                mr.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(mr.invc,
                    $"q {cbg.R / 255.0:0.###} {cbg.G / 255.0:0.###} {cbg.B / 255.0:0.###} rg " +
                    $"{boxLeft + MonoCellInsetPt:F2} {mr.pageHeight - mr.rowTop - rowH:F2} {boxW:F2} {rowH:F2} re f Q\n")));
            var txt = mr.rows[ri][c].Text;
            if (txt.Trim().Length > 0)
                EmitPositionedRun(mr.page, mr.res, MonoFontPt, boxLeft + MonoCellInsetPt,
                    mr.pageHeight - (mr.rowTop + MonoCellDropPt), txt);
            boxLeft += boxW;
        }
        mr.rowTop += rowH;
        return true;
    }

    /// <summary></summary>
    private static bool ReadMonoReportHeadRow(MonoReportRenderState mr, Match rm)
    {
        var cells = new List<(string Text, Color? Bg)>();
        foreach (Match cm in Regex.Matches(rm.Groups[1].Value,
                     @"<t[dh]([^>]*)>([\s\S]*?)</t[dh]\s*>", RegexOptions.IgnoreCase))
        {
            // the cell's own background, however its unquoted style
            // attribute happens to be spelled
            var bgM = Regex.Match(cm.Groups[1].Value,
                @"back\s*ground\s*:\s*(#[0-9a-fA-F]{3,6})", RegexOptions.IgnoreCase);
            cells.Add((MonoNobrText(cm.Groups[2].Value),
                bgM.Success ? ParseCssColor(bgM.Groups[1].Value) : null));
        }
        if (cells.Count > 0) mr.rows.Add(cells);
        return true;
    }
    private static string MonoNobrText(string markup) =>
        DecodeEntities(Regex.Replace(markup, @"<[^>]+>", "")).Trim('\r', '\n', '\t');
}

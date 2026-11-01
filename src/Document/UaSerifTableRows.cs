using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>UA serif table rows: one table row parsed, measured and drawn.</summary>
    private bool RenderUaRow(UaSerifTableState ua, System.Text.RegularExpressions.Match rm)
    {
        var ur = new UaRowState();
        ur.declH = 0.0;
        ur.dhm = SafeRegex.Match(
            rm.Groups["a"].Value, @"height\s*:\s*([\d.]+)pt", UaRx);
        if (ur.dhm.Success)
            ur.declH = double.Parse(ur.dhm.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture);

        ur.cells = new List<(string Text, double Fs, string? Family,
            Color? Bg, Color? EdgeColor, bool[] Solid,
            List<string> Lines, List<double> Boxes)>();
        foreach (System.Text.RegularExpressions.Match dm in
            SafeRegex.Matches(rm.Groups["in"].Value,
                @"<td(?<a>[^>]*)>(?<in>.*?)</td>", UaRx))
        {
            ParseUaCell(ur, dm);
        }
        if (ur.cells.Count == 0) return true;

        ur.rowContentH = 0.0;
        for (var ci = 0; ci < ur.cells.Count; ci++)
        {
            MeasureUaCell(ua, ur, ci);
        }

        ur.edged = false;
        foreach (var solid in ur.cells.Select(c => c.Solid)) if (solid[0] || solid[2]) ur.edged = true;
        var edges = ur.edged ? 2 * UaTableEdgePt : 0;
        ur.rowH = Math.Max(
            ur.declH > 0 ? ur.declH + edges : 0,
            ur.rowContentH + 2 * ua.pad);

        ur.cellL = ua.marginLeft;
        for (var ci = 0; ci < ur.cells.Count; ci++)
        {
            DrawUaCell(ua, ur, ci);
        }
        ua.topD += ur.rowH;
        ua.totalH += ur.rowH;
        return true;
    }
}

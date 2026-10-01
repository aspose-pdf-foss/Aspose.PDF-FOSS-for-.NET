using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal partial class HtmlToPdfConverter
{
    /// <summary>Mso form grid: one row of the grid drawn.</summary>
    private static bool RenderMsoFormRow(MsoFormGridState mg, MsoRow r)
    {
        if (DrawMsoRosterRow(mg, r)) return true;
        if (CloseMsoGroupBeforeRow(mg, r)) return true;
        MeasureMsoRowHeight(mg, r);
        if (mg.allTeal) mg.rowH = Math.Max(mg.rowH, 12.2);

        mg.cx = mg.x0;
        mg.ci = 0;
        DrawMsoRowCells(mg, r);
        mg.hostRow = false;
        foreach (var mc in r.Cells) if (mc.NestedHost) mg.hostRow = true;
        if (mg.hostRow) { mg.groupOpen = true; mg.groupTop = mg.y; }
        mg.y -= mg.rowH;
        return true;
    }
}

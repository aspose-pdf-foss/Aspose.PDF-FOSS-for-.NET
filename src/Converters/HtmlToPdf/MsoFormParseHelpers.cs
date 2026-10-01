using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal partial class HtmlToPdfConverter
{
// Run flush, cell close and row close behind the MSO form table parser.
    private static void FlushText(MsoFormParseState mp)
    {
        var t = CollapseWs(mp.text.ToString());
        mp.text.Clear();
        if (mp.cell is null || t.Trim(' ', '\u00A0').Length == 0) { return; }
        mp.cell.Runs.Add(new MsoRun
        {
            Text = t.Trim(), Fs = mp.fs, Face = mp.face, Bold = mp.bold, Italic = mp.ital,
            White = mp.white, Teal = mp.teal, Center = mp.center, NewLine = mp.pendingLine,
        });
        mp.pendingLine = false;
    }

    private static void CloseCell(MsoFormParseState mp) { FlushText(mp); if (mp.cell is not null) mp.row!.Cells.Add(mp.cell); mp.cell = null; }

    private static void CloseRow(MsoFormParseState mp)
    {
        CloseCell(mp);
        if (mp.row is { Cells.Count: > 0 }) { mp.row.Nested = mp.nestedDepth > 0; mp.rows.Add(mp.row); }
        mp.row = null;
    }
}

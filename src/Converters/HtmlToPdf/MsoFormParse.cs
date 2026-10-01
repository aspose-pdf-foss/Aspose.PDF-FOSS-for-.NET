using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal partial class HtmlToPdfConverter
{
    private static List<MsoRow> ParseMsoFormTable(string tableHtml,
        IReadOnlyDictionary<string, double> idW, IReadOnlyDictionary<string, double> idH)
    {
        var mp = new MsoFormParseState();
        mp.tableHtml = tableHtml;
        mp.idW = idW;
        mp.idH = idH;
        mp.rows = new List<MsoRow>();
        mp.row = null;
        mp.cell = null;
        mp.fs = 12;
        mp.face = "Times New Roman";
        mp.bold = false;
        mp.ital = false;
        mp.white = false;
        mp.teal = false;
        mp.center = false;
        mp.pendingLine = false;
        mp.pendingBr = false;
        mp.nestedDepth = 0;
        mp.text = new StringBuilder();

        mp.inSelect = false;
        mp.inSelectedOption = false;
        foreach (var tok in Tokenize(StripNonContent(mp.tableHtml)))
        {
            if (!ParseMsoFormToken(mp, tok)) break;
        }
        CloseRow(mp);
        return mp.rows;
    }
}

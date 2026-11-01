using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal partial class HtmlToPdfConverter
{
    private static void RenderMsoFormGrid(Document doc, List<MsoRow> rows)
    {
        var mg = new MsoFormGridState();
        mg.doc = doc;
        mg.rows = rows;
        mg.invc = System.Globalization.CultureInfo.InvariantCulture;

        SolveMsoColumns(mg);

        OpenMsoFormPage(mg);

        mg.groupOpen = false;
        mg.groupTop = 0;
#pragma warning disable S3267 // a step loop: each call does the work and returns false to stop - as a LINQ predicate the side effects would hide in a query
        foreach (var r in mg.rows)
#pragma warning restore S3267
        {
            if (!RenderMsoFormRow(mg, r)) break;
        }
        CloseGroup(mg, mg.y);

        mg.page.AddContentStream(Encoding.ASCII.GetBytes(mg.sb.ToString()));
        mg.page.AddContentStream(Encoding.ASCII.GetBytes(mg.tsb.ToString()));
    }
}

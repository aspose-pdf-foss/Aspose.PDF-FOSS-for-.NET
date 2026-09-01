using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class Table
{
// The anchor-run helper of the row-plan paragraph pass, lifted out of BuildRowPlanParagraph.
    private static List<(double XOff, double W, Hyperlink Link)>? TakeAnchorRuns(RowPlanParagraphState pp, 
        string lineText, Func<string, double> measure)
    {
        if (pp.pendingAnchors is not { Count: > 0 }) return null;
        List<(double XOff, double W, Hyperlink Link)>? runs = null;
        for (var ai = 0; ai < pp.pendingAnchors.Count; ai++)
        {
            var (atext, url) = pp.pendingAnchors[ai];
            var idx = atext.Length > 0 ? lineText.IndexOf(atext, StringComparison.Ordinal) : -1;
            if (idx < 0) continue;
            (runs ??= new()).Add((idx > 0 ? measure(lineText[..idx]) : 0,
                measure(atext), new WebHyperlink(url)));
            pp.pendingAnchors.RemoveAt(ai);
            ai--;
        }
        return runs;
    }
}

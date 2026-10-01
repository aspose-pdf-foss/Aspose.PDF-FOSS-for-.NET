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
// One table-of-contents entry: its title wrapping and its render on the TOC page.
    // Entries are set at the level format's size when the caller set
    // one, else the first explicitly-sized segment, else the 10 pt
    // LevelFormat default. The heading's OWN TextState is NOT in the
    // chain: a 24 pt heading's TOC line draws at the
    // plain 10 pt entry size (only the heading's in-content render
    // uses its TextState).
    private static List<string> WrapEntry(string s, double maxW, double fs, string face = "Helvetica")
    {
        var lines = new List<string>();
        var cur = new System.Text.StringBuilder();
        foreach (var word in s.Split(' '))
        {
            var trial = cur.Length == 0 ? word : cur + " " + word;
            if (MeasureEntry(trial, fs, face) <= maxW || cur.Length == 0)
            {
                if (cur.Length > 0) cur.Append(' ');
                cur.Append(word);
            }
            else
            {
                lines.Add(cur.ToString());
                cur.Clear();
                cur.Append(word);
            }
        }
        if (cur.Length > 0) lines.Add(cur.ToString());
        if (lines.Count == 0) lines.Add(string.Empty);
        return lines;
    }

    // Render ONE TOC entry with its line box starting at startY; returns
    // the Y the next entry (or following paragraph) continues from.
    private static double RenderTocEntry(PageLayoutState pl, Heading h, int destIdx, double startY)
    {
        pl.fontName ??= Table.RegisterFont(pl.page);
        pl.tocRendered.Add(h);
        // Top of the entry area — column jumps restart from here.
        pl.tocTopY ??= startY;
        var te = new TocEntryState();
        ResolveTocEntryFormat(te, pl, h, startY);
        ResolveTocEntryText(te, pl, h, destIdx);
        BreakTocEntryColumn(te, pl, h, startY);
        EmitTocEntry(te, pl, h, destIdx);
        return te.lastY - te.descFrac * te.entrySize - te.entryMarginBottom;
    }
}

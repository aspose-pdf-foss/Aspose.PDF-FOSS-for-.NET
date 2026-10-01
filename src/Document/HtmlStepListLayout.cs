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
    /// <summary>Lays one step-list item out: each block's runs wrapped into lines, the bullet seat and every kerned text run collected top-down.</summary>
    private static void LayoutStepListItem(HtmlStepListState hs, Converters.HtmlToPdfConverter.StepListItem item)
    {
        var textLeft = hs.liLeft + item.PadLeftPt;
        var maxW = hs.pageW - hs.marginRight - textLeft;
        var liFirstLine = true;
        foreach (var block in item.Blocks)
        {
            var (size, margin, boldTag) = TagStyle(hs, block.Tag);
            var stream = new List<(char c, bool bold)>();
            foreach (var r in block.Runs)
                foreach (var ch in r.Text)
                    stream.Add((ch, boldTag || r.Bold));
            if (stream.Count == 0) continue;

            var lines = Wrap(hs, stream, size, maxW);
            var pitch = Pitch(hs, size);
            hs.yDown += System.Math.Max(hs.pendingMargin, margin);
            var baseline = hs.yDown + Asc(hs, size);
            foreach (var ln in lines)
            {
                if (liFirstLine)
                {
                    var bAdv = GlyphW(hs, Gid(hs, '•', false), false, hs.em);
                    hs.bulletsOut.Add((baseline, hs.liLeft - 0.375 * hs.em - bAdv));
                    liFirstLine = false;
                }
                var x = textLeft;
                var gi = 0;
                while (gi < ln.Count)
                {
                    var runBold = ln[gi].bold;
                    var sb = new System.Text.StringBuilder();
                    var runW = 0.0;
                    var pg = -1;
                    while (gi < ln.Count && ln[gi].bold == runBold)
                    {
                        var gid = Gid(hs, ln[gi].c, runBold);
                        if (pg >= 0) runW += KernW(hs, pg, gid, runBold, size);
                        runW += GlyphW(hs, gid, runBold, size);
                        sb.Append(ln[gi].c);
                        pg = gid;
                        gi++;
                    }
                    hs.runsOut.Add((baseline, x, sb.ToString(), runBold, size));
                    x += runW;
                }
                baseline += pitch;
            }
            hs.yDown += lines.Count * pitch;
            hs.pendingMargin = margin;
        }
    }
}

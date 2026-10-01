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
    private sealed partial class FlowLayout
    {
        /// <summary>Each laid-out line of a note group becomes a band line, seated at the group's pitch and aligned the way its head asks.</summary>
        private void EmitNoteGroupLines(List<(double left, List<(double x, string text, StyledRun run)> cells)> laid, List<BandLine> lines, NoteEntry e, int gi, double groupLs, double groupFs, double mTop, double mLeft, double mBottom, HorizontalAlignment align, bool fullWidth, bool paintFromRule)
        {
            for (var li = 0; li < laid.Count; li++)
            {
                var (left, cells) = laid[li];
                double maxBase = 0, markSize = 0, width = 0, maxImage = 0;
                foreach (var (x, text, r) in cells)
                {
                    var sz = StyledRunSize(r);
                    if (r.ImageData is not null)
                    {
                        maxImage = Math.Max(maxImage, r.ImageH);
                        width = Math.Max(width, x + r.ImageW);
                        // (a picture cell has no text to measure; the line's other cells still count)
                        continue;
                    }
                    if (r.NoteMark) markSize = Math.Max(markSize, sz);
                    else if (!r.Sup) maxBase = Math.Max(maxBase, r.Size);
                    if (text.Length > 0) width = Math.Max(width, x + MeasureStyled(text, r, sz));
                }
                var hasText = maxBase > 0;
                var h = hasText ? maxBase
                    : maxImage > 0 ? maxImage
                    : li == 0 && groupFs > 0 ? groupFs : markSize > 0 ? markSize : 10;
                var bl = new BandLine
                {
                    Pitch = hasText ? h + groupLs : h, TextHeight = hasText ? h : 0, HasText = hasText,
                    NaturalWidth = width, Note = e.Note,
                    NoteFirst = gi == 0 && li == 0, ParaFirst = li == 0, LastOfParagraph = li == laid.Count - 1,
                    Align = align, Left = mLeft + left, FullWidthBox = fullWidth,
                    MarginTop = li == 0 ? mTop : 0, MarginBottom = li == laid.Count - 1 ? mBottom : 0,
                    ParaMarginTop = mTop, PaintFromRule = paintFromRule,
                };
                foreach (var (x, text, r) in cells) bl.Cells.Add((x, text, r, StyledRunSize(r)));
                lines.Add(bl);
            }
        }
    }
}

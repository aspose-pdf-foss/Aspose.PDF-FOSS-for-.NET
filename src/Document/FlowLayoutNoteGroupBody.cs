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
        /// <summary>One note group: its marker, the wrapped lines of its body and the rule that separates it from the group above.</summary>
        private void LayoutOneNoteGroup(List<List<BaseParagraph>> groups, int gi, List<BandLine> lines, NoteEntry e, BandGeometry g, BaseParagraph? noteHead)
        {
            var group = groups[gi];
            var head = group.Count > 0 ? group[0] : null;
            var runs = new List<StyledRun>();
            if (gi == 0 && e.Marker.Length > 0)
                runs.Add(new StyledRun
                {
                    Text = e.Marker, Size = e.ParentSize, NoteMark = true, Sup = true, Note = e.Note,
                    State = new Text.TextState { ForegroundColor = e.Note.TextState?.ForegroundColor },
                });
            double groupLs = 0, groupFs = 0;
            // A table paragraph is the whole line: it renders as a grid from the
            // band cursor (indented past the mark when it opens the note) and the
            // line is as tall as the grid.
            if (group.Count == 1 && group[0] is Table bandTable)
            {
                var markW = 0.0;
                if (runs.Count > 0 && runs[0].NoteMark)
                    markW = MeasureStyled(runs[0].Text, runs[0], StyledRunSize(runs[0]));
                var tblLeft = g.Left + markW;
                bandTable.FlowLeftOffset = tblLeft;
                bandTable.BuildMultiPage(_startPage, _startPageHeight - _marginTop,
                    0, 0, measureOnly: true);
                var tblH = bandTable.LastRenderedHeight;
                var tblLine = new BandLine
                {
                    Pitch = tblH, TextHeight = 0, HasText = false,
                    NaturalWidth = markW, Note = e.Note,
                    NoteFirst = gi == 0, ParaFirst = true, LastOfParagraph = true,
                    Align = HorizontalAlignment.Left, Left = 0,
                    TableBlock = bandTable, TableLeft = tblLeft,
                };
                foreach (var r in runs) tblLine.Cells.Add((0, r.Text, r, StyledRunSize(r)));
                lines.Add(tblLine);
                return;
            }
            foreach (var member in group)
            {
                if (member is Image gImg)
                {
                    // A picture in a note band: laid at the band cursor after
                    // whatever precedes it on the line, its own size (the flow
                    // image rule), and giving the line no height of its own
                    // while the line carries text.
                    if (LoadFlowImage(gImg, g.Width, 0) is (var giData, var giW, var giH))
                        runs.Add(new StyledRun { ImageData = giData, ImageW = giW, ImageH = giH });
                    // (the picture is one member among the group's; the members after it still lay out)
                    continue;
                }
                if (member is not Text.TextFragment tf) continue;
                var parent = tf.TextState;
                var pfs = parent.FontSizeTouched ? (double)parent.FontSize : 0;
                if (parent.LineSpacing > groupLs) groupLs = parent.LineSpacing;
                foreach (var seg in tf.Segments)
                {
                    var st = seg.TextState;
                    if (st.LineSpacing > groupLs) groupLs = st.LineSpacing;
                    if (string.IsNullOrEmpty(seg.Text)) continue;
                    var size = st.FontSizeTouched ? (double)st.FontSize : pfs > 0 ? pfs : 10;
                    if (size > groupFs) groupFs = size;
                    var merged = new Text.TextState
                    {
                        ForegroundColor = st.ForegroundColor ?? parent.ForegroundColor,
                        Underline = st.Underline || parent.Underline,
                        IsBold = st.IsBold || parent.IsBold,
                        IsItalic = st.IsItalic || parent.IsItalic,
                    };
                    var font = st.Font?.SourceFontData is not null ? st.Font
                        : parent.Font?.SourceFontData is not null ? parent.Font : null;
                    if (font is not null) merged.Font = font;
                    if ((st.FontData ?? parent.FontData) is { } fd) merged.FontData = fd;
                    var name = st.FontName ?? parent.FontName;
                    if (!string.IsNullOrEmpty(name) && font is null) merged.FontName = name;
                    runs.Add(new StyledRun
                    {
                        Text = seg.Text, Size = size, State = merged, Sup = st.Superscript,
                        Link = seg.Hyperlink ?? tf.HyperlinkValue,
                    });
                }
            }
            var headTf = head as Text.TextFragment;
            var align = headTf is null ? HorizontalAlignment.Left
                : headTf.HorizontalAlignment != HorizontalAlignment.Left ? headTf.HorizontalAlignment
                : headTf.TextState.HorizontalAlignment;
            // ⚠ TextFragment SHADOWS BaseParagraph.Margin with `new`, so reading it
            // through the base-typed head returns the fragment's UNSET base object
            // and a margined note paragraph loses its box.
            var hm = headTf is not null ? headTf.Margin : head?.Margin;
            double mTop = hm?.Top ?? 0, mBottom = hm?.Bottom ?? 0, mLeft = hm?.Left ?? 0, mRight = hm?.Right ?? 0;
            var paintFromRule = mTop != 0 || mBottom != 0 || mLeft != 0 || mRight != 0;
            var fullWidth = noteHead is Text.TextFragment { AutoNoteText: false };
            var laid = LayoutStyledLines(runs, Math.Max(1, g.Width - mLeft - mRight));
            EmitNoteGroupLines(laid, lines, e, gi, groupLs, groupFs, mTop, mLeft, mBottom, align, fullWidth, paintFromRule);
        }
    }
}

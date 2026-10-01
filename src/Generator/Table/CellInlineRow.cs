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
    /// <summary>Row column chrome: one inline row of a cell drawn - its graphs, rules and text runs.</summary>
    private void EmitCellInlineRow(RowColumnState rc, double cellX, List<List<InlineItem>> inlineRows, int ri, double inlineLift)
    {
        // Generator cells honour the cell's vertical alignment for
        // inline rows too (a two-line cell centres in its 102 pt row).
        var lineTop = rc.slice.TopY - rc.padTop - (rc.generatorCell ? rc.borderInsetTop + rc.vaOffset : 0)
            - (rc.generatorCell ? rc.inlineStack : ri * rc.slice.Plan.LineHeight);
        if (rc.generatorCell) rc.inlineStack += InlineRowHeight(inlineRows[ri], rc.slice.Plan.LineHeight);
        foreach (var item in inlineRows[ri])
        {
            var ix = cellX + rc.padLeft + item.X;
            if (item.ImageData is { } inlineImgData)
                rc.imageSink?.Add((inlineImgData,
                    new Rectangle(ix, lineTop - item.Height, ix + item.Width, lineTop)));
            else if (item.Graph is { } g)
                // The graph's local origin is its box's BOTTOM-left corner,
                // which sits its own declared height below the line top —
                // NOT one text line below it.
                rc.graphSink?.Add(g.Build(null, ix, lineTop - item.Height));
            else if (item.Empty)
            {
                // Emit an explicit empty run so the generator's leading empty
                // segment round-trips as an (empty) text fragment.
                rc.builder.BeginText();
                rc.builder.SetFont(rc.fontName, item.FontSize);
                rc.builder.MoveTextPosition(ix, lineTop - item.FontSize);
                rc.builder.ShowText("");
                rc.builder.EndText();
            }
            else if (item.Text is { Length: > 0 } t)
            {
                // Seat the run on the line baseline (the pre-shrink size), then apply any
                // sub/superscript shift — so a reduced-size super/subscript glyph still
                // hangs off the common baseline rather than its own smaller box.
                var baseSize = item.BaseFontSize > 0 ? item.BaseFontSize : item.FontSize;
                // Generator dialect: the line's baseline is the fragment size's
                // face-descent seat, and a smaller segment bottom-aligns on its
                // own descent (probed: a 13 pt run beside 15 pt Calibri seats
                // 0.5 pt lower); sub/superscripts shift off the segment's seat.
                var lineFs = rc.generatorCell && item.LineFontSize > 0 ? item.LineFontSize : baseSize;
                var seatY = lineTop - lineFs * (1 - inlineLift) - (lineFs - baseSize) * inlineLift
                    + item.BaselineShift;
                // Per-run embedded font (e.g. NotoSans / NotoSansArabic): embed it as a
                // Type0/CID font and emit the run as hex glyph IDs.
                if (item.Ttf is not null && rc.page is not null)
                {
                    var fontDict = ResolvePageFontDict(rc.page);
                    var (resName, hex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                        fontDict, item.Ttf, item.FontName ?? "Font", t, stripSpacesInBaseFont: true);
                    rc.builder.BeginText();
                    rc.builder.SetFont(resName, item.FontSize);
                    ApplyColor(rc.builder, item.Color);
                    rc.builder.MoveTextPosition(ix, seatY);
                    rc.builder.ShowTextHex(hex);
                    rc.builder.EndText();
                    continue;
                }
                // The run draws in ITS OWN face: a segment that asked for
                // bold/italic gets the matching Standard-14 face, not the
                // table's default one.
                var itemFont = rc.fontName;
                if ((item.Bold || item.Italic) && rc.page is not null)
                    itemFont = RegisterFont(rc.page, item.Bold && item.Italic
                        ? "Helvetica-BoldOblique"
                        : item.Bold ? "Helvetica-Bold" : "Helvetica-Oblique");
                rc.builder.BeginText();
                rc.builder.SetFont(itemFont, item.FontSize);
                ApplyColor(rc.builder, item.Color);
                rc.builder.MoveTextPosition(ix, seatY);
                rc.builder.ShowText(t);
                rc.builder.EndText();
                // …and its own underline, one rule under the run at the
                // link-underline seat.
                if (item.Underline && item.Width > 0)
                {
                    rc.builder.SaveState();
                    if (item.Color is { } uc)
                        rc.builder.SetStrokeColor(uc.R / 255.0, uc.G / 255.0, uc.B / 255.0);
                    rc.builder.SetLineWidth(LinkUnderlineWPt * item.FontSize / LinkProbeBasePt);
                    var uy = seatY - LinkUnderlineDropPt * item.FontSize / LinkProbeBasePt;
                    rc.builder.MoveTo(ix, uy).LineTo(ix + item.Width, uy).Stroke();
                    rc.builder.RestoreState();
                }
            }
        }
    }
}

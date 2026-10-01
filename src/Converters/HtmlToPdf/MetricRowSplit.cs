namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // A text row that does not fit the page is split by LINES (probed on the loose-doctype
    // reference form: the three-line closing note draws two lines at the foot of page 2 and
    // its last line at the raw top of page 3). Only a paragraph row splits: one wrapped text
    // cell, no grids, images, backgrounds or declared heights - anything else keeps the
    // whole-row move.
    //
    // A LABELLED paragraph splits the same way. The reference's complaint report carries its
    // narrative in a two-cell row - "Modified Narrative:" beside the paragraph - and cuts it at
    // the page foot exactly as it cuts a bare paragraph, drawing the label beside the part that
    // stays. The label is not one line: a 5em column wraps it to two, so it is the label's
    // FIT ABOVE THE CUT that admits the split, not its line count. Demanding a lone text cell
    // held that row together and pushed it whole to the next page, losing the seven lines that
    // belonged at the foot of the first.

    /// <summary>When the measured row crosses the bottom margin and is a paragraph row - one
    /// wrapped text cell, optionally beside shorter label cells that fit above the cut - cut
    /// the paragraph after the lines that fit and queue the rest as the next row; true when
    /// the row was cut (re-measure it).</summary>
    private static bool TrySplitMetricRowByLines(MetricRowsState mr, int ri)
    {
        if (mr.cursor.y - mr.s - mr.rowBoxH >= mr.marginBottom) return false;
        if (mr.cursor.y >= mr.pageHeight - mr.marginTop - 1e-3) return false;
        if (ri < mr.mps.rowHeights.Count && mr.mps.rowHeights[ri] > 0) return false;
        foreach (var extra in mr.rowSpanExtra) if (extra > 0) return false;

        // The paragraph is the tallest text cell in the row; every other text cell is a label
        // that must ride with it. All of them must be as plain as a paragraph - anything
        // carrying its own grid, image, fill, height or span keeps the whole-row move.
        MetricCell? textCell = null;
        var labels = new List<MetricCell>();
        foreach (var mc in mr.r)
        {
            if (mc.Phantom || mc.Text.Trim().Length == 0)
            {
                if (mc.SubTables is { Count: > 0 } || mc.ImgHPt > 0 || mc.HrRule || mc.Bg is not null) return false;
                continue;
            }
            if (mc.SubTables is { Count: > 0 } || mc.DivSegs is { Count: > 0 } || mc.Flow is { Count: > 0 }
                || mc.ImgHPt > 0 || mc.Bg is not null || mc.HeightPt > 0 || mc.RowSpan > 1
                || mc.BorderBottomW > 0 || mc.BorderTopW > 0)
                return false;
            if (textCell is null || mc.Lines.Length > textCell.Lines.Length)
            {
                if (textCell is not null) labels.Add(textCell);
                textCell = mc;
            }
            else labels.Add(mc);
        }
        if (textCell is null || textCell.Lines.Length < 2) return false;

        var lineH = CellLineOf(mr.mps, mr.stdSerif, mr.wrapperStacks, mr.hheaSum, mr.face, mr.fm, textCell,
            textCell.FontSize ?? mr.mps.fontSize);
        if (lineH <= 0 || Math.Abs(textCell.ContentH - textCell.Lines.Length * lineH) > 1e-6) return false;
        var room = mr.cursor.y - mr.s - 2 * mr.p - mr.marginBottom;
        var keep = (int)Math.Floor(room / lineH + 1e-9);
        if (keep < 1 || keep >= textCell.Lines.Length) return false;
        // A label taller than the part that stays would be cut itself, and a cut label is not
        // something the reference ever draws: keep such a row whole.
        foreach (var mc in labels) if (mc.Lines.Length > keep) return false;

        var rest = textCell.CloneShallow();
        rest.Lines = textCell.Lines[keep..];
        rest.Text = string.Join(" ", rest.Lines);
        rest.ContentH = rest.Lines.Length * lineH;
        textCell.Lines = textCell.Lines[..keep];
        textCell.ContentH = keep * lineH;
        var continuation = new List<MetricCell>(mr.r.Count);
        foreach (var mc in mr.r)
        {
            if (ReferenceEquals(mc, textCell)) { continuation.Add(rest); continue; }
            // A label belongs to the part that stayed behind: carrying it into the continuation
            // would draw it twice, once beside each half of its own paragraph.
            continuation.Add(labels.Contains(mc) ? BlankMetricCell(mc) : mc);
        }
        mr.rows.Insert(ri + 1, continuation);
        mr.rowSpanExtra = new double[mr.rows.Count];
        return true;
    }

    /// <summary>An empty stand-in for a cell whose text stayed on the page above: the
    /// continuation row keeps the column, not the words.</summary>
    private static MetricCell BlankMetricCell(MetricCell src)
    {
        var blank = src.CloneShallow();
        blank.Lines = [];
        blank.Text = "";
        blank.ContentH = 0;
        return blank;
    }
}

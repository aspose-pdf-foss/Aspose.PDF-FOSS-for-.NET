namespace Aspose.Pdf;

public partial class Table
{
    /// <summary>The top a reserved block is laid out at while a cell measures it: a plain
    /// page height, well inside the room its caller is offered below it. The block is told
    /// where the cell put it once the page is drawn.</summary>
    private const double ReservedBlockMeasureTop = 1000;

    /// <summary>The room offered under that top: taller than any page, so the block lays out whole.</summary>
    private const double ReservedBlockMeasureRoom = 1e6;

    /// <summary>Plans a <see cref="ReservedBlock"/> among a cell's paragraphs: its caller lays it
    /// out at the cell's content width, and the cell keeps a box of the height it answers with,
    /// as it keeps a picture's - exactly that box in an exact-stack cell, else whole lines of the
    /// row's pitch. The box is reported to the block when its page is drawn.</summary>
    private void PlanReservedBlockParagraph(ReservedBlock block, RowPlanColumnState pc, RowPlanState rp, int col)
    {
        if (block.LayOut is null) return;
        var width = pc.availWidth;
        var room = new ReservedRoom(0, 0, width, ReservedBlockMeasureTop, ReservedBlockMeasureTop - ReservedBlockMeasureRoom, true);
        if (block.LayOut(room) is not { } part) return;
        // The cell's text walk marks the block's place in the content with this comment.
        part.Marker = "reserved part " + Guid.NewGuid().ToString("N");
        AddCellImage(rp.plan, col, new CellImage
        {
            Block = block, Part = part, Width = width, Height = part.Height, Align = HorizontalAlignment.Left,
            LineOffset = pc.lines.Count,
            OwnSeat = CellPictureBoxesAreExact,
        });
        var pitch = pc.defaultFontSize * 1.2;
        if (CellPictureBoxesAreExact)
        {
            Consider(rp, pitch, pitch);
            pc.lines.Add(new CellLine { Text = "", FontSize = pc.defaultFontSize, ImgReserve = true, OwnPitch = part.Height });
            return;
        }
        var lines = Math.Max(1, (int)Math.Ceiling(part.Height / pitch));
        Consider(rp, pitch, pitch);
        for (var k = 0; k < lines; k++)
            pc.lines.Add(new CellLine { Text = "", FontSize = pc.defaultFontSize, ImgReserve = true });
    }
}

using Aspose.Pdf.Content;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class Table
{
    /// <summary>A text a cell holds turned by its <see cref="TextState.Rotation"/>: its lines
    /// in the face, size and colour they draw in, and the turn. The cell keeps a box of the
    /// turned block's extent for it, as it keeps a picture's.</summary>
    private sealed class RotatedCellText
    {
        public string[] Lines = System.Array.Empty<string>();
        public string Face = "Helvetica";
        public double FontSize;
        public Color? Color;
        public double Rotation;
        public double Pitch;
        /// <summary>Where the turned block's bounding box starts, from the first baseline's
        /// origin, in the block's own unturned frame turned onto the page.</summary>
        public double OriginX;
        public double OriginY;
    }

    /// <summary>The turn a cell text is drawn at: its own state's, else the default state of
    /// its cell, row or table - zero when none turns it.</summary>
    private double EffectiveCellRotation(TextFragment tf, Cell cell, Row row)
    {
        var turn = Turn(tf.TextState);
        if (turn == 0) turn = Turn(cell.DefaultCellTextState);
        if (turn == 0) turn = Turn(row.DefaultCellTextState);
        return turn == 0 ? Turn(DefaultCellTextState) : turn;
    }

    private static double Turn(TextState? state) =>
        state is null ? 0 : ((state.Rotation % FullTurn) + FullTurn) % FullTurn;

    private const double FullTurn = 360;

    /// <summary>A turned text in a cell: laid out unturned on its hard breaks (a turned line
    /// does not wrap at the column), turned, and kept as a box of the turned block's extent,
    /// so the row grows to hold it and the cell's alignment seats it. It used to be drawn
    /// upright and wrapped at the column, its rotation dropped. Plain single-face fragments
    /// only: segment runs, HTML and form text keep their own paths.</summary>
    private bool PlanRotatedTextParagraph(TextFragment tf, RowPlanColumnState pc, RowPlanState rp, int col, Row row)
    {
        if (tf.Segments.Count > 1 || string.IsNullOrEmpty(tf.Text)) return false;
        var rotation = EffectiveCellRotation(tf, pc.cell, row);
        if (rotation == 0) return false;
        var fontSize = ResolveCellParagraphFontSize(tf, pc.defaultFontSize, pc.cell, row);
        var font = ResolveGeneratorCellFont(tf, pc.cell, row);
        var face = TextBuilder.MapToStandard14Public(new TextState
        {
            FontName = font is null || ReferenceEquals(font, FontInfo.DefaultHelvetica) ? tf.TextState.FontName : font.FontName,
            IsBold = tf.TextState.IsBold || DeclaredCellBold(pc.cell, row),
            IsItalic = tf.TextState.IsItalic || DeclaredCellItalic(pc.cell, row),
        });
        var lines = tf.Text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var measure = TextPaginator.CreateMeasurer(face, fontSize, null);
        double longest = 0;
        foreach (var line in lines) longest = Math.Max(longest, measure(line));
        var ascent = Standard14Fonts.GetAscent(face) / EmUnits * fontSize;
        var descent = Math.Abs(Standard14Fonts.GetDescent(face)) / EmUnits * fontSize;
        var pitch = fontSize + CallerLineSpacing(tf, pc.cell, row);
        var (originX, originY, width, height) = TurnedBlockExtent(rotation, longest, ascent, descent, lines.Length, pitch);
        var text = new RotatedCellText
        {
            Lines = lines, Face = face, FontSize = fontSize, Rotation = rotation, Pitch = pitch,
            Color = tf.TextState.ForegroundColor ?? FirstSegmentForegroundColor(tf) ?? pc.textState?.ForegroundColor,
            OriginX = originX, OriginY = originY,
        };
        AddCellImage(rp.plan, col, new CellImage
        {
            Rotated = text, Width = width, Height = height, Align = pc.cellAlign,
            LineOffset = pc.lines.Count, OwnSeat = CellPictureBoxesAreExact,
        });
        // The box books lines as a picture's does: exactly its height in an exact-stack
        // cell, else whole lines of the row's pitch.
        Consider(rp, fontSize, fontSize);
        if (CellPictureBoxesAreExact)
        {
            pc.lines.Add(new CellLine { Text = "", FontSize = pc.defaultFontSize, ImgReserve = true, OwnPitch = height });
            return true;
        }
        var reserve = Math.Max(1, (int)Math.Ceiling(height / fontSize));
        for (var k = 0; k < reserve; k++)
            pc.lines.Add(new CellLine { Text = "", FontSize = fontSize, ImgReserve = true });
        return true;
    }

    /// <summary>The box a block of text takes turned by <paramref name="rotation"/> degrees
    /// about its first baseline's origin - the block unturned runs from 0 to its longest line
    /// and from the first line's ascent down to the last line's descent - with where that
    /// origin stands from the box's lower-left corner.</summary>
    internal static (double OriginX, double OriginY, double Width, double Height) TurnedBlockExtent(
        double rotation, double longest, double ascent, double descent, int lineCount, double pitch)
    {
        var rad = rotation * Math.PI / HalfTurn;
        var (cos, sin) = (Math.Round(Math.Cos(rad), TurnDecimals), Math.Round(Math.Sin(rad), TurnDecimals));
        var bottom = -descent - (lineCount - 1) * pitch;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var (x, y) in new[] { (0.0, ascent), (longest, ascent), (0.0, bottom), (longest, bottom) })
        {
            var px = cos * x - sin * y;
            var py = sin * x + cos * y;
            minX = Math.Min(minX, px);
            maxX = Math.Max(maxX, px);
            minY = Math.Min(minY, py);
            maxY = Math.Max(maxY, py);
        }
        return (-minX, -minY, maxX - minX, maxY - minY);
    }

    /// <summary>The text matrix that turns a run by <paramref name="rotation"/> degrees about (x, y).</summary>
    internal static void SetTurnedTextMatrix(ContentStreamBuilder builder, double rotation, double x, double y)
    {
        var rad = rotation * Math.PI / HalfTurn;
        var (cos, sin) = (Math.Round(Math.Cos(rad), TurnDecimals), Math.Round(Math.Sin(rad), TurnDecimals));
        builder.SetTextMatrix(cos, sin, -sin, cos, x, y);
    }

    internal const double EmUnits = 1000;
    private const double HalfTurn = 180;
    private const int TurnDecimals = 10;

    /// <summary>Draws a turned cell text into the box the cell seated it in: one text object
    /// whose matrix turns it about its first baseline, the next lines a pitch below in the
    /// turned frame.</summary>
    private static void DrawRotatedCellText(RowColumnState rc, RotatedCellText text, Rectangle box)
    {
        if (rc.page is null) return;
        rc.builder.SaveState();
        rc.builder.BeginText();
        rc.builder.SetFont(RegisterFont(rc.page, text.Face), text.FontSize);
        ApplyColor(rc.builder, text.Color);
        rc.builder.SetLeading(text.Pitch);
        SetTurnedTextMatrix(rc.builder, text.Rotation, box.LLX + text.OriginX, box.LLY + text.OriginY);
        for (var i = 0; i < text.Lines.Length; i++)
        {
            if (i > 0) rc.builder.NextLine();
            rc.builder.ShowText(text.Lines[i]);
        }
        rc.builder.EndText();
        rc.builder.RestoreState();
    }
}


namespace Aspose.Pdf.Text;

public partial class TextFragment
{
    /// <summary>The stages of the block translate: one content operator at a time.</summary>
    private void TranslateBlockOperator(BlockTranslateState bt, Operator op)
    {
        switch (op)
        {
            case Aspose.Pdf.Operators.BT:
                bt.afterBt = true;
                break;
            case Aspose.Pdf.Operators.SetTextMatrix tm
                when Math.Abs(tm.A - 1) < 1e-6 && Math.Abs(tm.B) < 1e-6
                    && Math.Abs(tm.C) < 1e-6 && Math.Abs(tm.D - 1) < 1e-6
                    && InRegion(bt, tm.E, tm.F):
                bt.toReplace.Add(tm);
                bt.afterBt = false;
                break;
            case Aspose.Pdf.Operators.MoveTextPosition td
                when bt.afterBt && InRegion(bt, td.X, td.Y):
                // Td straight after BT is absolute (line matrix = identity).
                td.X += bt.dx;
                td.Y += bt.dy;
                bt.shifted++;
                bt.afterBt = false;
                break;
            case Aspose.Pdf.Operators.TextPlaceOperator:
                bt.afterBt = false;
                break;
        }
    }

    private static bool InRegion(BlockTranslateState bt, double x, double y) =>
        x >= bt.obLLX - bt.padX && x <= bt.obURX + bt.padX
        && y >= bt.obLLY - bt.padY && y <= bt.obURY + bt.padY;
}

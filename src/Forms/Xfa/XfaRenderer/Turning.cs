using System.Globalization;
using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

internal static partial class XfaRenderer
{
    private const int QuarterTurn = 90, HalfTurn = 180, ThreeQuarterTurn = 270, FullTurn = 360;

    /// <summary>The turn a field or draw declares (rotate), in degrees counter-clockwise: 0, 90, 180 or 270.
    /// Any other angle is not a turn the layout makes and reads as none.</summary>
    private static int TurnOf(XmlElement e)
    {
        if (e.LocalName is not ("field" or "draw")) return 0;
        var declared = e.GetAttribute("rotate");
        if (declared.Length == 0
            || !double.TryParse(declared, NumberStyles.Float, CultureInfo.InvariantCulture, out var angle)) return 0;
        var turn = (((int)Math.Round(angle) % FullTurn) + FullTurn) % FullTurn;
        return turn is QuarterTurn or HalfTurn or ThreeQuarterTurn ? turn : 0;
    }

    /// <summary>True for a box turned a quarter either way whose width and height are both declared: in
    /// the flow it takes its height across and its width down.</summary>
    private static bool TurnsItsBox(XmlElement e) =>
        TurnOf(e) is QuarterTurn or ThreeQuarterTurn
        && LenN(e.GetAttribute("w")) is not null && LenN(e.GetAttribute("h")) is not null;

    /// <summary>Places a turned field or draw. It is painted upright in its own box and everything it
    /// painted is turned about the pivot. In a positioned container the pivot is the box's own (x, y),
    /// the point its anchorType names on the box - its top-left corner unless it says otherwise; in a
    /// flow, where (x, y) is the top-left of the room the turned box takes (<paramref name="w"/> by
    /// <paramref name="h"/>), it is the corner of that room the box's top-left lands on.</summary>
    private static void PlaceTurned(Ctx ctx, XmlElement e, int turn, bool parentFlows, double x, double y, double w, double h, string path)
    {
        // The upright size: the room's sides swap back for a quarter turn that swapped them.
        bool swapped = TurnsItsBox(e);
        double ownW = swapped ? h : w, ownH = swapped ? w : h;
        double px = x, py = y;
        if (parentFlows)
        {
            double roomW = turn == HalfTurn ? ownW : ownH, roomH = turn == HalfTurn ? ownH : ownW;
            if (turn != QuarterTurn) px = x + roomW;
            if (turn != ThreeQuarterTurn) py = y + roomH;
        }
        var (ax, ay) = parentFlows ? (0, 0) : AnchorOffset(e, ownW, ownH);
        int first = ctx.Items.Count, firstPlaced = ctx.Placements?.Count ?? 0;
        ctx.Turning = true;
        if (e.LocalName == "field") PaintField(ctx, e, px - ax, py - ay, ownW, ownH, path);
        else PaintDraw(ctx, e, px - ax, py - ay, ownW, ownH);
        ctx.Turning = false;
        var matrix = TurnMatrix(turn, px, ctx.PageH - py);
        for (int i = first; i < ctx.Items.Count; i++) ctx.Items[i].Matrix = matrix;
        TurnPlacements(ctx, firstPlaced, turn, matrix);
    }

    /// <summary>The matrix that turns the page counter-clockwise by a quarter-turn multiple about (cx, cy).</summary>
    private static double[] TurnMatrix(int turn, double cx, double cy)
    {
        var (cos, sin) = turn switch { QuarterTurn => (0, 1), HalfTurn => (-1, 0), _ => (0, -1) };
        return new[] { cos, sin, -sin, cos, cx - cx * cos + cy * sin, cy - cx * sin - cy * cos };
    }
}

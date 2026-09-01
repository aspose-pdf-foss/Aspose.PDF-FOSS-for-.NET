using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Content;

/// <summary>A single path construction command with coordinates.</summary>
public readonly struct PathCommand
{
    public PathOp Op { get; }
    public double X1 { get; }
    public double Y1 { get; }
    public double X2 { get; }
    public double Y2 { get; }
    public double X3 { get; }
    public double Y3 { get; }

    public PathCommand(PathOp op, double x1 = 0, double y1 = 0,
        double x2 = 0, double y2 = 0, double x3 = 0, double y3 = 0)
    {
        Op = op; X1 = x1; Y1 = y1; X2 = x2; Y2 = y2; X3 = x3; Y3 = y3;
    }
}

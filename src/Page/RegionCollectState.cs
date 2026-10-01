using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

public static partial class PageExtensions
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class RegionCollectState
{
    public IO.PdfLexer lexer = null!;
    public List<Aspose.Pdf.Core.PdfObject> operands = null!;
    public PageExtensions.Mat ctm;
    public Stack<Aspose.Pdf.PageExtensions.Mat> ctmStack = null!;
    public PageExtensions.GState gs = null!;
    public Stack<Aspose.Pdf.PageExtensions.GState> gsStack = null!;
    public System.Text.StringBuilder path = null!;
    public bool started;
    public double minX;
    public double minY;
    public double maxX;
    public double maxY;
    public double curX;
    public double curY;
    public bool inText;
    public byte[] streamBytes = default!;
    public Rectangle region = default!;
    public double deltaX = 0;
    public double deltaY = 0;
    public StringBuilder dup = default!;
}
}

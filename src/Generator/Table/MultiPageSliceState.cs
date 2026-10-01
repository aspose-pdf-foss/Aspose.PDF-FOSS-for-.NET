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
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class MultiPageSliceState
{
    public string fontName = null!;
    public double[] colWidths = null!;
    // Column pagination across PAGES: a table wider than the page's content
    // band with RepeatingColumnsCount > 0 and/or TableBroken.Vertical splits
    // into column slices, one run of pages per slice. Slicing is GRID-column
    // based (a ColSpan cell crossing a slice boundary contributes an EMPTY
    // continuation cell that keeps its background/border; text renders once,
    // in the slice where the cell starts). Each slice re-renders every row;
    // the first `repeat` grid columns are prepended to every slice. The
    // packing budget is the page content band (page width minus BOTH page
    // margins) minus the repeating block — measured on the
    // 12/6/8-page layouts of the repeating-column shape.
    public int repeat;
    public int[] identity = null!;
    public List<byte[]> built = null!;
    public Page page = default!;
    public double startY = 0;
    public double bottomMargin = 0;
    public double topMargin = 0;
    public bool measureOnly = false;
    public bool contentFlow = false;
}
}

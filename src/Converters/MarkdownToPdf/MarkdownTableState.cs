using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Converters;

internal static partial class MarkdownToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class MarkdownTableState
{
    public double top;
    public double size;
    public List<List<List<Aspose.Pdf.Converters.MarkdownToPdfConverter.Run>>> rows = null!;
    public int cols;
    // Header runs render bold.
    public List<List<List<Aspose.Pdf.Converters.MarkdownToPdfConverter.Run>>> styled = null!;
    // Natural (unwrapped) column widths; columns whose natural width no longer
    // fits share the remaining span equally and wrap their cells.
    public double[] natural = null!;
    public double avail;
    public double[] widths = null!;
    public double[] colX = null!;
    public double xCursor;
    // Track the row's FIRST BASELINE directly: the probed uniform advance is
    // firstBase(r+1) − firstBase(r) = maxLines(r)·13.5 + 3 (the header seats its
    // baseline 2.21 + ascent under the table top).
    public double firstBase;
    public int lastMax;
    public Flow flow = default!;
    public TableBlk table = default!;
}
}

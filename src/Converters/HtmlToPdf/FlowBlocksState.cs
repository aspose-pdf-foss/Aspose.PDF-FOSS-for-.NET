using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class FlowBlocksState
{
    public List<Aspose.Pdf.Converters.HtmlToPdfConverter.Block> list = null!;
    public ConvertState cv = default!;
    public bool absSpanLedger = false;
    public List<BeforeMarker> beforeMarkers = default!;
    public string? elementGridFace = null;
    public bool htmlHasFormInput = false;
    public bool inlineBlockColRules = false;
    public bool perTableFormGate = false;
    public List<Block> rowBlocks = default!;
    public bool sheetChromesCells = false;
    public string frag = default!;
    /// <summary>The container elements open above the segment being built — the ancestor
    /// chain a table segment carries into its own grid build (see Block.CssAncestors).</summary>
    public List<CssElem>? openChain;
    /// <summary>The containers open above the table segment, whatever the sheet's dialect: the host
    /// whose rule types or sizes the grid (see Block.HostFace / HostWidthPt).</summary>
    public List<CssElem>? hostChain;
    // Where the last unwrapped wrapper table's blocks ended: a wrapper table that opens
    // right there stacks on it and stands the closing chrome of both.
    public int lastUnwrapEnd = -1;
}
}

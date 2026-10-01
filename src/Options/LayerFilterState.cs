using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

internal static partial class LayerContentFilter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class LayerFilterState
{
    public List<string> lines = null!;
    public List<(string Operands, string Op)> ops = null!;
    // First pass (Save only): index of the LAST op contributed via a target BDC block.
    public int lastTarget;
    public bool hasDoContribution;
    public List<bool?> ocStack = null!;
    // A Do-style layer keeps its full trailing context (no tail cut);
    // Flatten/Delete never cut the tail.
    public int tailStart;
    public List<(string Operands, string Op)> output = null!;
    public List<(string Operands, string Op)> pathBuffer = null!;
    public int tailDepth;
    public System.Text.StringBuilder sb = null!;
    public byte[] content = default!;
    public string layerId = default!;
    public IReadOnlyCollection<string> layerXObjects = default!;
    public LayerFilterMode mode = default!;
}
}

using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using System.Globalization;

namespace Aspose.Pdf.Text;

public sealed partial class TextParagraph
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class LocalRenderState
{
    public int lineCount;
    // In local coords, lines are placed bottom-up: the last line's baseline at
    // Y=0, each earlier line raised by the advances of the lines below it.
    public double[] localBaseY = null!;
    public double acc;
    public ContentStreamBuilder builder = default!;
    public List<List<(string text, TextState ts)>> visualLines = default!;
    public Func<string, string> ensureFont = default!;
    public Func<FontData, string, (string fontResName, byte[] hexGlyphIds)>? ensureCidFont = null;
    public Page page = default!;
}
}

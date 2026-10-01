using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class BootstrapBlocksState
{
    public List<Aspose.Pdf.Converters.HtmlToPdfConverter.BsBlock> blocks = null!;
    public BsParagraph? p;
    public BsTable? table;
    public List<(string Text, bool Th)>? row;
    public System.Text.StringBuilder text = null!;
    public string? textTarget;
    public bool cellTh;
    public BsButton? btn;
    public int linkDepth;
    public string bodyHtml = default!;
    public BootstrapScreenState bs = default!;
    public IReadOnlyDictionary<string, Dictionary<string, string>> css = default!;
}
}

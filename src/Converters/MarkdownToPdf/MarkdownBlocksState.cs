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
private sealed class MarkdownBlocksState
{
    public List<Aspose.Pdf.Converters.MarkdownToPdfConverter.Blk> blocks = null!;
    public int i;
    // An `<p align="center">` opener and its content can sit in separate HTML
    // chunks (a blank line between them); the alignment carries until its `</p>`.
    public bool pendingCenter;
    public string[] lines = default!;
    public string line = null!;
    public string trimmed = null!;
    // ATX heading.
    public System.Text.RegularExpressions.Match headingMatch = null!;
    // List (unordered or ordered): consecutive item lines form one block.
    public System.Text.RegularExpressions.Match ulMatch = null!;
    public System.Text.RegularExpressions.Match olMatch = null!;
    // Paragraph: consecutive plain lines; a line ending in 2+ spaces keeps a hard break.
    public List<List<Aspose.Pdf.Converters.MarkdownToPdfConverter.Run>> hardLines = null!;
    public System.Text.StringBuilder buf = null!;
}
}

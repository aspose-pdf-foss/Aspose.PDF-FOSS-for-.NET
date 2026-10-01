using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Converters;

internal static partial class SvgToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class SvgTextRenderState
{
    public System.Text.StringBuilder sb = null!;
    public string transform = null!;
    public double[]? tmMatrix;
    // A pure matrix() transform is applied through the text matrix (Tm) in
    // EmitRun — emitting it as a cm too would double the translation.
    public double[] newCtm = null!;
    // Walk the text content: direct text nodes and tspan children, tracking the
    // current text position.
    public double curX;
    public double curY;
    public XmlElement elem = default!;
    public Ctx ctx = default!;
    public Dictionary<string, string> style = default!;
    public double[] ctm = default!;
    public double fontSize;
    // Non-WinAnsi text (Arabic, Hebrew, Cyrillic, CJK, …) cannot be written with a
    // Standard-14 face — it would flatten to '?'. Route it through an embedded Type0
    // face (RTL runs shaped to visual order first). uniTtf == null => the run keeps
    // the Standard-14 path below.
    // A family the DOCUMENT itself ships (@font-face, inline or from a linked
    // stylesheet) is embedded and used as-is — that is the whole point of
    // shipping it, and a Standard-14 substitute would draw the wrong typeface.
    public byte[]? uniTtf;
    public string? declaredFaceName = null!;
    public string display = null!;
    public string baseFont = null!;
    public Aspose.Pdf.Core.PdfDictionary fontDict = null!;
    public string fontRes = null!;
    public string embedName = null!;
    public double width;
    public string anchor = null!;
    public double x;
    public bool visible;
}
}

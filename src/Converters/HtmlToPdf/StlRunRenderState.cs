using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class StlRunRenderState
{
    public int k;
    public Page pg = null!;
    public double x;
    public double y;
    // Face resolution: the document's own @font-face program first, then
    // an installed face by that family name, then the serif fallback.
    public byte[]? faceTtf;
    public Text.GlyphOutlineParser? faceParser;
    public double faceUpm;
    public string faceName = null!;
    public List<(StlFontFace face, string text)>? runUnion;
    public Aspose.Pdf.Core.PdfDictionary? res;
    public Aspose.Pdf.Core.PdfDictionary fontDict = null!;
    public System.Text.StringBuilder sb = null!;
    public double cr;
    public double cg;
    public double cb;
    // Word-spacing applies per space glyph — and, in the IE model the
    // em-compensation dialect follows, between two adjacent full-em
    // CJK characters. PDF Tw does not act on the Type0 2-byte
    // encoding, so segments between the boundaries are placed at
    // their computed x offsets instead.
    public List<(string Text, char Sep)> segments = null!;
    public double segX;
    public StlPositionedState sp = default!;
    public int p = 0;
    public StlRun r = default!;
}
}

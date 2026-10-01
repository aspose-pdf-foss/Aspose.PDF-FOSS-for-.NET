using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

internal sealed partial class TextStateModifier
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class TfNameRangeState
{
    public Dictionary<string, Aspose.Pdf.Core.PdfDictionary> fonts = null!;
    public IO.PdfLexer lexer = null!;
    public List<(Aspose.Pdf.IO.TokenKind kind, Aspose.Pdf.Core.PdfObject obj, int startPos, int endPos)> operands = null!;
    public int lastTfNameStart;
    public int lastTfNameEnd;
    public double lastTfSize;
    public Dictionary<int, string>? currentToUnicode;
    // A simple (single-byte) font is swapped for our simple WinAnsi embedded font by
    // repointing Tf alone: the shown bytes are reinterpreted under the new font's
    // encoding. A Type0/CID font shows 2-byte codes that a simple font cannot
    // represent, so its show operand has to be re-encoded as well — the caller does
    // that when the site reports Composite. A font the resource dict doesn't resolve
    // is left alone entirely: its codes decode to nothing reliable.
    public bool currentFontIsSimple;
    public bool currentFontResolved;
    public string? currentFontRes;
    // A run whose text IS the fragment's is the fragment's own run; one that merely
    // contains it may belong to a different fragment. Preferring the exact run keeps
    // a short fragment ("c" out of a split word) from claiming a long run and leaving
    // its own showing the original font.
    public FontSwapSite? found;   // the exact-match site that ends the scan
    public FontSwapSite? containing;
    public byte[] streamBytes = default!;
    public string text = default!;
    public PdfDictionary pageDict = default!;
    public PdfReader reader = default!;
    public string? alreadyReplacedRes = null;
    public bool exactOnly = false;
}
}

using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

internal sealed partial class TextStateModifier
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class FontSizeStreamState
{
    public FontSizeStreamState(byte[] streamBytes, string text, double oldSize, double newSize,
        PdfDictionary pageDict, PdfReader reader, bool allowCollateral,
        LineReseat? reseat, bool splitSubRun)
    {
        this.streamBytes = streamBytes;
        this.text = text;
        this.oldSize = oldSize;
        this.newSize = newSize;
        this.pageDict = pageDict;
        this.reader = reader;
        this.allowCollateral = allowCollateral;
        this.reseat = reseat;
        this.splitSubRun = splitSubRun;
        fonts = TextAbsorber.ResolveFonts(pageDict, reader);
        lexer = new PdfLexer(streamBytes);
    }

    public Dictionary<string, Aspose.Pdf.Core.PdfDictionary> fonts;
    public IO.PdfLexer lexer;
    public List<(Aspose.Pdf.IO.TokenKind kind, Aspose.Pdf.Core.PdfObject obj, int startPos, int endPos)> operands = new();
    // Track the position of the most recent Tf operator
    public int lastTfSizeStart = -1;
    public int lastTfSizeEnd = -1;
    public double lastTfSize;
    // text matrix vertical scale factor
    public double tmScaleY = 1;
    // The CTM scale in force, tracked through q/Q/cm. A producer that lays a page out in
    // its own space — `0.8625 0 0 -0.8625 50 700 cm`, then `16 Tf` — draws text at an
    // EFFECTIVE 13.8 pt, which is the size the absorber reports and therefore the size a
    // caller passes as oldSize. Reading the raw 16 off the Tf made every such run fail the
    // oldSize test, so the resize silently did nothing. The raw value written back is
    // recovered through the same combined scale below, so it stays in the page's space.
    public double ctmScale = 1;
    public Stack<double> ctmStack = new();
    public string? currentFontName;
    public Dictionary<int, string>? currentToUnicode;
    // Every text show with the Tf that governs it. A fragment's phrase is
    // often split over several consecutive shows, each re-issuing its own
    // Tf (accented glyphs, kerned words), so the match must run over the
    // concatenated show text and then patch EVERY Tf covering the match.
    public List<(string decoded, int tfStart, int tfEnd, double effSize, int showStart, int showEnd, string? fontRes)> shows = new();
    public System.Text.StringBuilder concat = null!;
    public (int start, int end)[] spans = null!;
    // Walk occurrences until one is drawn at the expected old size — the
    // same text can appear elsewhere at other sizes (the caller resizes a
    // specific absorbed fragment, identified by its size).
    public string concatStr = null!;
    public SortedDictionary<int, (int end, double newTf)> patches = null!;
    // The last TJ array's decoded text, kept as a string: the operand's byte form is a
    // cp1252 round-trip that turns every non-Latin character into '?'.
    public string? arrayDecoded;
    // The caller's view of the resized fragment (page units) and its measure, for the
    // line re-seat after a whole-show resize; null = patch the Tf only.
    public LineReseat? reseat;
    // The show a single whole-show patch resized, -1 when the patch covers several shows
    // or only part of one.
    public int wholeShow = -1;
    // Whether a match that is part of one show may split it (see ModifyFontSize).
    public bool splitSubRun = true;
    public byte[] result = null!;
    public byte[] streamBytes;
    public string text;
    public double oldSize;
    public double newSize;
    public PdfDictionary pageDict;
    public PdfReader reader;
    public bool allowCollateral;
}
}

using System.Text;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Text;

public sealed partial class FontEmbedder
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class FontDictEmbedState
{
    public Aspose.Pdf.Text.TrueTypeParser parser = null!;
    public double scale;
    // Embedding the whole system TTF (often ~1 MB) for every referenced font bloats the
    // output enormously — a PDF/A conversion of a small file can balloon to tens of MB.
    // Reduce the font program to just the glyphs reachable through this dictionary's
    // WinAnsi 32..255 range, which is all a simple TrueType font can address. When the
    // subset is a proper reduction it is presented under a 6-letter subset tag per
    // PDF 32000-1 §9.6.4. Falls back to the full program if subsetting can't apply
    // (e.g. CFF-based or loca-less fonts).
    public byte[] fontProgram = null!;
    public string embedName = null!;
    // The same system face is typically referenced by many font dictionaries
    // (e.g. dozens of "Arial"/"ArialMT" entries across a converted document). Embedding
    // an identical program once and sharing the FontFile2 object keeps the output small.
    public int fontFileObjNum;
    public string? cacheKey;
    public Aspose.Pdf.Core.PdfDictionary descriptor = null!;
    public int[] bbox = null!;
    public Aspose.Pdf.Core.PdfArray bboxArray = null!;
    public Document document = default!;
    public byte[] ttfData = default!;
    public PdfDictionary fontDict = default!;
    public string baseFontName = default!;
    public Dictionary<string, (int objNum, string embedName)>? fontFileCache = null;
    public bool subset = false;
}
}

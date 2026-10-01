using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class NotdefRewriteState
{
    public Aspose.Pdf.Core.PdfDictionary fonts = null!;
    // code→glyph-name table per font resource; null = skip the font. Only Type1
    // faces qualify: their glyph lookup is NAME-keyed through the encoding, so a
    // control code with no name is a certain .notdef reference. A TrueType font
    // (esp. a subset with no /Encoding) addresses glyphs through its internal
    // cmap where low codes can be REAL glyphs, and composite (Type0) fonts use
    // multi-byte codes — no verdict is possible from the font dict alone.
    public Dictionary<string, string?[]?> encodings = null!;
    public string text = null!;
    public List<(int start, int end)> deletions = null!;
    public bool inlineImage;   // an inline image makes the stream unsafe to rewrite: keep the original bytes
    public string? lastName;
    public string? currentFont;
    public int operandStart;
    public List<byte[]> strings = null!;
    public int pos;
    public List<byte> output = null!;
    public int copyFrom;
    public byte[] contentBytes = default!;
    public PdfDictionary resources = default!;
    public PdfFormatConversionOptions options = default!;
    public int pageNumber = 0;
    public bool strip = false;
}
}

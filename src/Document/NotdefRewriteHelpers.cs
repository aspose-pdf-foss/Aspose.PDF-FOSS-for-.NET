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
// The notdef-rewrite helpers: a font's encoding names (cached), and the operand-start and operator-end bookkeeping of the content scan.
    // A font's encoding resolved once for the conversion's notdef pass: the same font is named on
    // every page, and the pass clears the table when it is done.
    private Dictionary<PdfDictionary, string?[]?>? _notdefEncodings;

    private string?[]? EncodingFor(NotdefRewriteState nd, string fontName)
    {
        if (nd.encodings.TryGetValue(fontName, out var cached)) return cached;
        string?[]? names = null;
        if (_reader.ResolveDict(nd.fonts.Get(fontName)) is { } fontDict
            && fontDict.GetName("Subtype") is "Type1" or "MMType1")
        {
            _notdefEncodings ??= new Dictionary<PdfDictionary, string?[]?>();
            if (!_notdefEncodings.TryGetValue(fontDict, out names))
                _notdefEncodings[fontDict] = names = Devices.SoftwarePageRenderer.ResolveEncoding(fontDict, _reader);
        }
        nd.encodings[fontName] = names;
        return names;
    }

    // The Identity-encoded composite fonts the conversion found showing CID 0, their .notdef
    // glyph; null when it found none, which is the usual case and costs the scan nothing.
    private HashSet<PdfDictionary>? _notdefCidFonts;

    // The width of a CID that neither /W nor /DW states (PDF 32000-1, 9.7.4.3).
    private const double DefaultCidWidth = 1000;

    /// <summary>For a show under one of those fonts whose every code is CID 0: the operator
    /// that moves the text on by the advance of the glyphs it showed and draws nothing, so
    /// what follows on the line stays where it was. Null for any other show. Character
    /// spacing, which a show adds per glyph, is not carried over.</summary>
    private string? NotdefCidAdvance(NotdefRewriteState nd, string fontName)
    {
        if (nd.strings.Count == 0 || _reader.ResolveDict(nd.fonts.Get(fontName)) is not { } font
            || !_notdefCidFonts!.Contains(font) || font.GetName("Encoding") != "Identity-H") return null;
        var codes = 0;
        foreach (var shown in nd.strings)
        {
            if (shown.Length % 2 != 0) return null;
            foreach (var b in shown)
                if (b != 0) return null;
            codes += shown.Length / 2;
        }
        if (codes == 0) return null;

        var cidFont = _reader.Resolve(font.Get("DescendantFonts")) is PdfArray { Count: > 0 } descendants
            ? _reader.ResolveDict(descendants[0]) : null;
        var width = Text.TrueTypeAdvances.CidWidths(_reader.Resolve(cidFont?.Get("W")) as PdfArray, _reader)
            .TryGetValue(0, out var stated) ? stated : StatedWidth(cidFont?.Get("DW")) ?? DefaultCidWidth;
        // A number in a TJ array moves the text back by that many thousandths of the font size.
        return "[" + (-codes * width).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "] TJ";
    }

    private static void BeginOperand(NotdefRewriteState nd, int at) { if (nd.operandStart < 0) nd.operandStart = at; }

    private static void EndOperator(NotdefRewriteState nd) { nd.operandStart = -1; nd.strings.Clear(); }
}

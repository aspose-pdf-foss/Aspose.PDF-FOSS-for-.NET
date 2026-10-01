using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
// Tj replace: 
    // Flat-string fallback (used when match position is unavailable or when
    // match covers the whole TJ — splitting adds no value). The TJ caller
    // owns the _replacementCount increment, so this path must NOT call
    // ApplyReplace (which would double-count).
    private PdfArray FlatReplace(TjReplaceState tj)
    {
        var replacedText = _isRegex && _regexPattern is not null
            ? _regexPattern.Replace(tj.normalizedCombined, tj.replacement)
            : tj.normalizedCombined.Replace(tj.normalizedSearch, tj.replacement, StringComparison.Ordinal);
        var replacedBytes = EncodeString(replacedText, tj.toUnicode, tj.fontDict);
        var useHex = tj.parts.Count > 0 && tj.parts[0].isHex;
        var flat = new PdfArray();
        flat.Add(new PdfString(replacedBytes, useHex));
        return flat;
    }

    // Offset-inside-string = count of prior chars mapped to the same arrIdx
    // before the match boundary.
    private static int CountCharsUpTo(TjReplaceState tj, int stop, int arrIdx)
    {
        var c = 0;
        for (var k = 0; k < stop; k++)
            if (tj.charMap[k] == arrIdx) c++;
        return c;
    }
}

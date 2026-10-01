using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

internal sealed partial class TextStateModifier
{
    /// <summary>Font size stream: one content operator applied to the scan state.</summary>
    private void ScanFontSizeOperator(FontSizeStreamState fz, Token token, int startPos, int endPos)
    {
        var op = token.StringValue!;
        switch (op)
        {
            case "Tf":
                if (fz.operands.Count >= 2)
                {
                    if (fz.operands[0].obj is PdfName fn)
                    {
                        fz.currentFontName = fn.Value;
                        if (fz.fonts.TryGetValue(fz.currentFontName, out var fontDict))
                            fz.currentToUnicode = TextAbsorber.ParseToUnicodeFromDict(fontDict, fz.reader);
                        else
                            fz.currentToUnicode = null;
                    }
                    // Record position of the size operand
                    fz.lastTfSizeStart = fz.operands[1].startPos;
                    fz.lastTfSizeEnd = fz.operands[1].endPos;
                    if (fz.operands[1].obj is PdfInteger pi)
                        fz.lastTfSize = pi.Value;
                    else if (fz.operands[1].obj is PdfReal pr)
                        fz.lastTfSize = pr.Value;
                }
                break;

            case "Tm":
                // Tm: a b c d e f — text matrix; effective font size = Tf_size * sqrt(c² + d²)
                if (fz.operands.Count >= 6)
                {
                    double c = 0, d = 0;
                    if (fz.operands[2].obj is PdfReal cr2) c = cr2.Value;
                    else if (fz.operands[2].obj is PdfInteger ci2) c = ci2.Value;
                    if (fz.operands[3].obj is PdfReal dr2) d = dr2.Value;
                    else if (fz.operands[3].obj is PdfInteger di2) d = di2.Value;
                    fz.tmScaleY = Math.Sqrt(c * c + d * d);
                    if (fz.tmScaleY < 0.001) fz.tmScaleY = 1;
                }
                break;

            case "q":
                fz.ctmStack.Push(fz.ctmScale);
                break;

            case "Q":
                if (fz.ctmStack.Count > 0) fz.ctmScale = fz.ctmStack.Pop();
                break;

            case "cm":
                if (fz.operands.Count >= 6)
                {
                    static double Num((TokenKind kind, PdfObject obj, int startPos, int endPos) o)
                        => o.obj is PdfReal r ? r.Value : o.obj is PdfInteger i ? i.Value : 0;
                    var det = Math.Abs(Num(fz.operands[0]) * Num(fz.operands[3])
                        - Num(fz.operands[1]) * Num(fz.operands[2]));
                    if (det > 1e-9) fz.ctmScale *= Math.Sqrt(det);
                }
                break;

            case "Tj":
            case "'":
            case "\"":
                if (fz.operands.Count >= 1 && fz.operands[^1].obj is PdfString textStr)
                {
                    var decoded = DecodeTextString(textStr.Value, fz.currentToUnicode);
                    if (decoded.Length > 0 && fz.lastTfSizeStart >= 0)
                        fz.shows.Add((decoded, fz.lastTfSizeStart, fz.lastTfSizeEnd,
                            fz.lastTfSize * fz.tmScaleY * fz.ctmScale,
                            fz.operands[^1].startPos, endPos, fz.currentFontName));
                }
                break;

            case "TJ":
                // TJ array: text was decoded during array parsing
                if (fz.operands.Count >= 1 && fz.operands[^1].obj is PdfString tjText)
                {
                    var decoded = fz.operands[^1].kind == TokenKind.ArrayStart && fz.arrayDecoded is not null
                        ? fz.arrayDecoded
                        : DecodeTextString(tjText.Value, fz.currentToUnicode);
                    if (decoded.Length > 0 && fz.lastTfSizeStart >= 0)
                        fz.shows.Add((decoded, fz.lastTfSizeStart, fz.lastTfSizeEnd,
                            fz.lastTfSize * fz.tmScaleY * fz.ctmScale,
                            fz.operands[^1].startPos, endPos, fz.currentFontName));
                }
                break;
        }
    }
}

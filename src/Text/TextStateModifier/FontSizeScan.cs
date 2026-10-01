using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

internal sealed partial class TextStateModifier
{
    /// <summary>Font size stream: one token of the content stream scanned into the show list.</summary>
    private bool ScanFontSizeToken(FontSizeStreamState fz)
    {
        var startPos = (int)fz.lexer.Position;
        var token = fz.lexer.NextToken();
        if (token.Kind == TokenKind.Eof) return false;
        var endPos = (int)fz.lexer.Position;

        switch (token.Kind)
        {
            case TokenKind.Integer:
                fz.operands.Add((token.Kind, new PdfInteger(token.IntValue), startPos, endPos));
                break;
            case TokenKind.Real:
                fz.operands.Add((token.Kind, new PdfReal(token.RealValue), startPos, endPos));
                break;
            case TokenKind.LiteralString:
                fz.operands.Add((token.Kind, new PdfString(token.BytesValue!), startPos, endPos));
                break;
            case TokenKind.HexString:
                fz.operands.Add((token.Kind, new PdfString(token.BytesValue!, isHex: true), startPos, endPos));
                break;
            case TokenKind.Name:
                fz.operands.Add((token.Kind, new PdfName(token.StringValue!), startPos, endPos));
                break;
            case TokenKind.ArrayStart:
            {
                // Collect array elements (for TJ operator)
                var arrTexts = new StringBuilder();
                int arrStringCount = 0;
                while (true)
                {
                    var t = fz.lexer.NextToken();
                    if (t.Kind == TokenKind.Eof) return false;
                    if (t.Kind == TokenKind.ArrayEnd) break;
                    if (t.Kind == TokenKind.LiteralString || t.Kind == TokenKind.HexString)
                    {
                        var strBytes = t.BytesValue;
                        if (strBytes is not null)
                        {
                            arrTexts.Append(DecodeTextString(strBytes, fz.currentToUnicode));
                            arrStringCount++;
                        }
                    }
                }
                // Store the concatenated text from the array as an operand
                fz.arrayDecoded = arrTexts.ToString();
                fz.operands.Add((TokenKind.ArrayStart, new PdfString(
                    Cp1252.GetBytes(arrTexts.ToString())), startPos, (int)fz.lexer.Position));
                break;
            }
            case TokenKind.DictStart:
            {
                int depth = 1;
                while (depth > 0)
                {
                    var t = fz.lexer.NextToken();
                    if (t.Kind == TokenKind.Eof) return false;
                    if (t.Kind == TokenKind.DictStart) depth++;
                    if (t.Kind == TokenKind.DictEnd) depth--;
                }
                fz.operands.Clear();
                break;
            }
            case TokenKind.Keyword:
            {
                ScanFontSizeOperator(fz, token, startPos, endPos);
                fz.operands.Clear();
                break;
            }
            default:
                fz.operands.Clear();
                break;
        }
        return true;
    }
}

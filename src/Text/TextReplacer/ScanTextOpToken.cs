using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
    /// <summary>Collect text ops: one token of the content stream scanned into the op list.</summary>
    private bool ScanTextOpToken(CollectTextOpsState ct)
    {
        var sp = (int)ct.lexer2.Position;
        var tok = ct.lexer2.NextToken();
        if (tok.Kind == TokenKind.Eof) return false;
        var ep = (int)ct.lexer2.Position;

        switch (tok.Kind)
        {
            case TokenKind.Integer:
            case TokenKind.Real:
            case TokenKind.Name:
                ct.ops2.Add((tok.Kind, tok.Kind == TokenKind.Name ? new PdfName(tok.StringValue!) :
                    tok.Kind == TokenKind.Integer ? new PdfInteger(tok.IntValue) :
                    (PdfObject)new PdfReal(tok.RealValue), sp, ep));
                break;
            case TokenKind.LiteralString:
                ct.ops2.Add((tok.Kind, new PdfString(tok.BytesValue!), sp, ep));
                break;
            case TokenKind.HexString:
                ct.ops2.Add((tok.Kind, new PdfString(tok.BytesValue!, isHex: true), sp, ep));
                break;
            case TokenKind.ArrayStart:
                var (arr, aep) = ParseContentArrayWithPositions(ct.lexer2);
                ct.ops2.Add((TokenKind.ArrayStart, arr, sp, aep));
                break;
            case TokenKind.Keyword:
                ApplyTextOpKeyword(ct, tok, sp, ep);
                break;
            default:
                ct.ops2.Clear();
                break;
        }
        return true;
    }
}

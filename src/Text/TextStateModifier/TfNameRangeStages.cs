using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

internal sealed partial class TextStateModifier
{
    /// <summary>Consumes one content token: operands accumulate (arrays and dictionaries skipped whole), a Tf remembers the font name span and its encoding, a show operator whose decoded text holds the search text yields the swap site - exactly when the show is the text alone, else as the containing candidate.</summary>
    private bool ScanTfNameToken(TfNameRangeState tn)
    {
        var startPos = (int)tn.lexer.Position;
        var token = tn.lexer.NextToken();
        if (token.Kind == TokenKind.Eof) return false;
        var endPos = (int)tn.lexer.Position;

        switch (token.Kind)
        {
            case TokenKind.Integer:
                tn.operands.Add((token.Kind, new PdfInteger(token.IntValue), startPos, endPos));
                break;
            case TokenKind.Real:
                tn.operands.Add((token.Kind, new PdfReal(token.RealValue), startPos, endPos));
                break;
            case TokenKind.LiteralString:
                tn.operands.Add((token.Kind, new PdfString(token.BytesValue!), startPos, endPos));
                break;
            case TokenKind.HexString:
                tn.operands.Add((token.Kind, new PdfString(token.BytesValue!, isHex: true), startPos, endPos));
                break;
            case TokenKind.Name:
                tn.operands.Add((token.Kind, new PdfName(token.StringValue!), startPos, endPos));
                break;
            case TokenKind.ArrayStart:
            {
                var arrTexts = new StringBuilder();
                while (true)
                {
                    var t = tn.lexer.NextToken();
                    if (t.Kind == TokenKind.Eof) return false;
                    if (t.Kind == TokenKind.ArrayEnd) break;
                    if (t.Kind == TokenKind.LiteralString || t.Kind == TokenKind.HexString)
                    {
                        var strBytes = t.BytesValue;
                        if (strBytes is not null)
                            arrTexts.Append(DecodeTextString(strBytes, tn.currentToUnicode));
                    }
                }
                tn.operands.Add((TokenKind.ArrayStart, new PdfString(
                    Cp1252.GetBytes(arrTexts.ToString())), startPos, (int)tn.lexer.Position));
                break;
            }
            case TokenKind.DictStart:
            {
                int depth = 1;
                while (depth > 0)
                {
                    var t = tn.lexer.NextToken();
                    if (t.Kind == TokenKind.Eof) return false;
                    if (t.Kind == TokenKind.DictStart) depth++;
                    if (t.Kind == TokenKind.DictEnd) depth--;
                }
                tn.operands.Clear();
                break;
            }
            case TokenKind.Keyword:
            {
                var op = token.StringValue!;
                switch (op)
                {
                    case "Tf":
                        if (!NoteTfOperator(tn)) return false;
                        break;
                    case "Tj":
                    case "'":
                    case "\"":
                    case "TJ":
                        if (!MatchTfShowOperator(tn, op)) return false;
                        break;
                }
                tn.operands.Clear();
                break;
            }
            default:
                tn.operands.Clear();
                break;
        }
        return true;
    }

    /// <summary>A show operator: decodes its string with the current font's ToUnicode; when the text is inside and a Tf precedes it, records the swap site - the exact match ends the scan, a containing show is kept as the fallback. False to stop the scan.</summary>
    private bool MatchTfShowOperator(TfNameRangeState tn, string op)
    {
        if (tn.operands.Count >= 1 && tn.operands[^1].obj is PdfString showStr)
        {
            var decoded = DecodeTextString(showStr.Value, tn.currentToUnicode);
            var alreadyDone = tn.alreadyReplacedRes is not null
                && string.Equals(tn.currentFontRes, tn.alreadyReplacedRes, StringComparison.Ordinal);
            if (decoded.Contains(tn.text) && tn.lastTfNameStart >= 0 && tn.currentFontResolved
                && !alreadyDone)
            {
                var litOk = op == "Tj" && tn.operands[^1].kind == TokenKind.LiteralString;
                var site = new FontSwapSite(tn.lastTfNameStart, tn.lastTfNameEnd,
                    litOk ? tn.operands[^1].startPos : -1,
                    litOk ? tn.operands[^1].endPos : -1,
                    tn.lastTfSize, decoded,
                    tn.operands[^1].startPos, tn.operands[^1].endPos,
                    Composite: !tn.currentFontIsSimple);
                if (decoded.Length == tn.text.Length) { tn.found = site; return false; }
                tn.containing ??= site;
            }
        }
        return true;
    }

    /// <summary>A Tf operator: remembers the font name's byte span and resolves its encoding for the shows that follow.</summary>
    private bool NoteTfOperator(TfNameRangeState tn)
    {
        if (tn.operands.Count >= 2 && tn.operands[0].obj is PdfName fn)
        {
            tn.currentFontRes = fn.Value;
            if (tn.fonts.TryGetValue(fn.Value, out var fontDict))
            {
                tn.currentToUnicode = TextAbsorber.ParseToUnicodeFromDict(fontDict, tn.reader);
                tn.currentFontIsSimple = fontDict.GetName("Subtype") != "Type0";
                tn.currentFontResolved = true;
            }
            else
            {
                tn.currentToUnicode = null;
                tn.currentFontIsSimple = false;
                tn.currentFontResolved = false;
            }
            tn.lastTfNameStart = tn.operands[0].startPos;
            tn.lastTfNameEnd = tn.operands[0].endPos;
            tn.lastTfSize = tn.operands[1].obj is PdfInteger ti ? ti.Value
                : tn.operands[1].obj is PdfReal tr ? tr.Value : 0;
        }
        return true;
    }
}

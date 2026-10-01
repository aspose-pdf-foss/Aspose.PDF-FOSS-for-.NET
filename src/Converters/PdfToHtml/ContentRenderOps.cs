using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
    /// <summary>One content-stream token of the render, verbatim: the body of RenderContentToHtml's
    /// operator loop. Returns false where the loop ended; a continue became return true.</summary>
    private static bool RenderContentOp(ContentRenderState ct)
    {
        var token = ct.lexer.NextToken();
        if (token.Kind == TokenKind.Eof) return false;

        switch (token.Kind)
        {
            case TokenKind.Integer: ct.operands.Add(new PdfInteger(token.IntValue)); break;
            case TokenKind.Real: ct.operands.Add(new PdfReal(token.RealValue)); break;
            case TokenKind.LiteralString: ct.operands.Add(new PdfString(token.BytesValue!)); break;
            case TokenKind.HexString: ct.operands.Add(new PdfString(token.BytesValue!, isHex: true)); break;
            case TokenKind.Name: ct.operands.Add(new PdfName(token.StringValue!)); break;
            case TokenKind.ArrayStart:
                ct.operands.Add(ParseArray(ct.lexer));
                break;
            case TokenKind.Keyword:
            {
                var op = token.StringValue!;
                // Operator ordinal within this content stream. A path element's
                // id is the 0-based index of the operator that opened its
                // construction, so the emitted SVG identifies each path by
                // where it is authored rather than by a dense running count.
                var opIndex0 = ct.opCounter++;
                if (ct.pathState.Data.Length == 0 && ct.pathOpenIndex < 0
                    && op is "m" or "re" or "l" or "c" or "v" or "y")
                    ct.pathOpenIndex = opIndex0;
                // UseZOrder paint counter: each path paint op and image Do is
                // one atomic object (whatever the subpath count or clip
                // outcome); an ExtGState carrying a soft mask adds the mask
                // form's own object count at EVERY gs that loads it. Glyphs
                // count in ShowRun; forms count through their contents.
                if (ct.zCounter is not null)
                {
                    switch (op)
                    {
                        case "S" or "s" or "f" or "F" or "f*" or "B" or "B*" or "b" or "b*":
                            ct.zCounter.V++;
                            break;
                        case "BI":
                            ct.zCounter.V++;
                            break;
                        case "Do":
                            if (ct.operands.Count >= 1 && ct.operands[0] is PdfName zxn
                                && IsImageXObject(zxn.Value, ct.imageXObjects, ct.resources, ct.reader))
                                ct.zCounter.V++;
                            break;
                        case "gs":
                            if (ct.resources is not null && ct.operands.Count >= 1 && ct.operands[0] is PdfName zgs)
                            {
                                var egs = ct.reader.ResolveDict(
                                    ct.reader.ResolveDict(ct.resources.Get("ExtGState"))?.Get(zgs.Value));
                                var smDict = egs is not null ? ct.reader.ResolveDict(egs.Get("SMask")) : null;
                                var maskForm = smDict is not null ? ct.reader.ResolveStream(smDict.Get("G")) : null;
                                if (maskForm is not null)
                                    ct.zCounter.V += CountMaskPaintOps(maskForm, ct.reader, ct.zCounter.MaskMemo, depth: 0);
                            }
                            break;
                    }
                }
                if (RenderOperator(ct, op)) return true;
                ct.operands.Clear();
                break;
            }
            default:
                ct.operands.Clear();
                break;
        }
        return true;
    }

    /// <summary>Dispatches one content operator to its arm; true when the arm finished the token itself and the caller must return at once.</summary>
    private static bool RenderOperator(ContentRenderState ct, string op)
    {
        switch (op)
        {
            case "q": case "Q": case "gs": case "cm": case "BDC": case "BMC": case "EMC": case "w":
                if (RenderStateOperator(ct, op) is { } renderStateOperatorResult) return renderStateOperatorResult;
                break;
            case "BT": case "ET": case "Tf": case "Tr": case "TL": case "Td": case "TD": case "Tm": case "T*": case "Ts": case "Tc": case "Tw": case "Tj": case "TJ": case "'":
                if (RenderTextOperator(ct, op) is { } renderTextOperatorResult) return renderTextOperatorResult;
                break;
            case "rg": case "RG": case "g": case "G": case "k": case "K": case "cs": case "CS": case "sc": case "scn": case "SC": case "SCN":
                if (RenderColorOperator(ct, op) is { } renderColorOperatorResult) return renderColorOperatorResult;
                break;
            case "Do": case "m": case "l": case "c": case "v": case "y": case "h": case "re": case "S": case "s": case "f": case "F": case "f*": case "B": case "B*": case "b": case "b*": case "W": case "W*": case "n": case "sh": case "BI":
                if (RenderPathOperator(ct, op) is { } renderPathOperatorResult) return renderPathOperatorResult;
                break;
        }
        return false;
    }
}

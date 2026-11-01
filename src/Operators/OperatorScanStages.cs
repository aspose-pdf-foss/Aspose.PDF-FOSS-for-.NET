using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;
using System.Text;

namespace Aspose.Pdf;

internal static partial class ContentStreamOperatorParser
{
    /// <summary>The stages of the operator scan: one token at a time.</summary>
    private static bool ScanOperator(OperatorScanState po)
    {
        SkipWhitespaceAndComments(po);
        if (po.pos >= po.len) return false;

        char c = po.text[po.pos];

        if (c == '(')
        {
            po.operands.Add(ReadParenString(po));
        }
        else if (c == '<' && po.pos + 1 < po.len && po.text[po.pos + 1] == '<')
        {
            po.operands.Add(ReadDictionary(po));
        }
        else if (c == '<')
        {
            po.operands.Add(ReadHexString(po));
        }
        else if (c == '[')
        {
            po.operands.Add(ReadArray(po));
        }
        else if (c == '/')
        {
            po.operands.Add(ReadName(po));
        }
        else if (c == '-' || c == '+' || c == '.' || (c >= '0' && c <= '9'))
        {
            po.operands.Add(ReadNumber(po));
        }
        else if (c == 'B' && po.pos + 1 < po.len && po.text[po.pos + 1] == 'I' &&
                 (po.pos + 2 >= po.len || IsDelimiter(po.text[po.pos + 2])))
        {
            // BI . ID . EI — inline image: count as 3 operators (BI, ID, EI)
            // per the OperatorCollection contract
            var start = po.pos;
            var eiPos = FindInlineImageEnd(po);
            po.operands.Clear();
            if (po.sink is { } sink)
            {
                // The image's parameters are not operands; its three operators go by name.
                sink(new ScannedOperator(po, "BI"));
                sink(new ScannedOperator(po, "ID"));
                sink(new ScannedOperator(po, "EI"));
                return true;
            }
            var fullText = po.text[start..eiPos].TrimEnd();
            // BI with its key-value pairs
            var idIdx = fullText.IndexOf("\nID", StringComparison.Ordinal);
            if (idIdx < 0) idIdx = fullText.IndexOf(" ID", StringComparison.Ordinal);
            if (idIdx >= 0)
            {
                po.result!.Add(fullText[..idIdx].TrimEnd()); // BI + parameters
                // The image data rides on the ID operator (the bytes between ID and EI,
                // their delimiting whitespace included), so a write-back of the list
                // keeps the image instead of an empty ID EI pair.
                var dataStart = idIdx + 1 + ID.Keyword.Length;
                po.result.Add(ID.Keyword + fullText[dataStart..^EI.Keyword.Length]);
                po.result.Add(EI.Keyword); // EI operator
            }
            else
            {
                po.result!.Add(fullText);
            }
        }
        else if (IsOperatorChar(c))
        {
            EmitOperator(po);
        }
        else
        {
            po.pos++; // skip unexpected chars
        }
        return true;
    }

    /// <summary>The stages of the operator scan: one token at a time.</summary>
    private static void EmitOperator(OperatorScanState po)
    {
        var start = po.pos;
        var opName = ReadOperatorName(po);
        // Check for true/false/null which are operands
        if (opName == "true" || opName == "false" || opName == "null")
        {
            po.operands.Add((start, po.pos));
        }
        else
        {
            // Handle concatenated single-letter operators like "QQQQQ" (5× Q)
            // or "nq" / "QQ" (2-char glues occur too, e.g. "nq0.0 … re").
            // Some corrupt PDFs omit whitespace between operators.
            bool isConcatenated = opName.Length >= 2 && !IsKnownOperator(opName)
                && opName.All(ch => IsKnownSingleCharOp(ch));
            if (isConcatenated)
            {
                // First operator gets any pending operands; the remaining characters are individual operators
                for (int ci = 0; ci < opName.Length; ci++)
                    Emit(po, opName[ci].ToString());
            }
            else
            {
                Emit(po, opName);
            }
        }
    }
}

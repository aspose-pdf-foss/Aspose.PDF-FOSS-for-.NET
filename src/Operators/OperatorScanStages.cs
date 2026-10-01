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
            var fullText = po.text[start..eiPos].TrimEnd();
            // BI with its key-value pairs
            var idIdx = fullText.IndexOf("\nID", StringComparison.Ordinal);
            if (idIdx < 0) idIdx = fullText.IndexOf(" ID", StringComparison.Ordinal);
            if (idIdx >= 0)
            {
                po.result.Add(fullText[..idIdx].TrimEnd()); // BI + parameters
                po.result.Add("ID"); // ID operator
                po.result.Add("EI"); // EI operator
            }
            else
            {
                po.result.Add(fullText);
            }
            po.operands.Clear();
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
        var opName = ReadOperatorName(po);
        // Check for true/false/null which are operands
        if (opName == "true" || opName == "false" || opName == "null")
        {
            po.operands.Add(opName);
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
                // First operator gets any pending operands
                if (po.operands.Count > 0)
                {
                    po.result.Add(string.Join(" ", po.operands) + " " + opName[0]);
                    po.operands.Clear();
                }
                else
                {
                    po.result.Add(opName[0].ToString());
                }
                // Remaining characters are individual operators
                for (int ci = 1; ci < opName.Length; ci++)
                    po.result.Add(opName[ci].ToString());
            }
            else
            {
                // This is an operator — emit with operands
                if (po.operands.Count > 0)
                {
                    po.result.Add(string.Join(" ", po.operands) + " " + opName);
                    po.operands.Clear();
                }
                else
                {
                    po.result.Add(opName);
                }
            }
        }
    }
}

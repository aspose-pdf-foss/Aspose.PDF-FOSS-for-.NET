using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;
using System.Text;

namespace Aspose.Pdf;

/// <summary>Parses PDF content stream bytes into individual operator strings.</summary>
internal static partial class ContentStreamOperatorParser
{
    internal static List<string> ParseOperators(byte[] data)
    {
        var po = new OperatorScanState();
        po.data = data;
        po.result = new List<string>();
        po.text = Compat.Latin1.GetString(po.data);
        po.pos = 0;
        po.len = po.text.Length;
        po.operands = new List<string>();

        while (po.pos < po.len)
        {
            if (!ScanOperator(po)) break;
        }

        return po.result;
    }

    private static bool IsOperatorChar(char c) =>
        (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '\'' || c == '"' || c == '*';

    private static bool IsKnownSingleCharOp(char c) =>
        c == 'q' || c == 'Q' || c == 'n' || c == 'f' || c == 'h' || c == 'W' || c == 'S' ||
        c == 'B' || c == 'b' || c == 's' || c == 'F';

    private static readonly HashSet<string> _knownOps = new(StringComparer.Ordinal)
    {
        "q","Q","cm","m","l","c","v","y","h","re","S","s","f","F","B","b","n","W",
        "BT","ET","Tf","Td","TD","Tm","TJ","Tj","TL","Tc","Tw","Tz","Tr","Ts",
        "d0","d1","CS","cs","SC","SCN","sc","scn","G","g","RG","rg","K","k",
        "gs","ri","i","Do","BI","ID","EI","sh","BX","EX","MP","DP","BMC","BDC","EMC",
        "w","J","j","M","d","T*","'","\"","W*","f*","b*","B*",
    };
    private static bool IsKnownOperator(string op) => _knownOps.Contains(op);

    private static bool IsDelimiter(char c) =>
        c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '(' || c == ')' ||
        c == '<' || c == '>' || c == '[' || c == ']' || c == '/' || c == '%';

    private static void SkipWhitespaceAndComments(OperatorScanState po)
    {
        while (po.pos < po.len)
        {
            char c = po.text[po.pos];
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\0')
            {
                po.pos++;
            }
            else if (c == '%')
            {
                while (po.pos < po.len && po.text[po.pos] != '\n' && po.text[po.pos] != '\r') po.pos++;
            }
            else break;
        }
    }

    private static string ReadParenString(OperatorScanState po)
    {
        int start = po.pos;
        po.pos++; // skip (
        int depth = 1;
        while (po.pos < po.len && depth > 0)
        {
            if (po.text[po.pos] == '\\') { po.pos += 2; continue; }
            if (po.text[po.pos] == '(') depth++;
            else if (po.text[po.pos] == ')') depth--;
            po.pos++;
        }
        return po.text[start..po.pos];
    }

    private static string ReadHexString(OperatorScanState po)
    {
        int start = po.pos;
        po.pos++; // skip <
        while (po.pos < po.len && po.text[po.pos] != '>') po.pos++;
        if (po.pos < po.len) po.pos++; // skip >
        return po.text[start..po.pos];
    }

    private static string ReadDictionary(OperatorScanState po)
    {
        int start = po.pos;
        po.pos += 2; // skip <<
        int depth = 1;
        while (po.pos < po.len && depth > 0)
        {
            if (po.pos + 1 < po.len && po.text[po.pos] == '<' && po.text[po.pos + 1] == '<') { depth++; po.pos += 2; }
            else if (po.pos + 1 < po.len && po.text[po.pos] == '>' && po.text[po.pos + 1] == '>') { depth--; po.pos += 2; }
            else po.pos++;
        }
        return po.text[start..po.pos];
    }

    private static string ReadArray(OperatorScanState po)
    {
        int start = po.pos;
        po.pos++; // skip [
        int depth = 1;
        while (po.pos < po.len && depth > 0)
        {
            if (po.text[po.pos] == '[') depth++;
            else if (po.text[po.pos] == ']') depth--;
            po.pos++;
        }
        return po.text[start..po.pos];
    }

    private static string ReadName(OperatorScanState po)
    {
        int start = po.pos;
        po.pos++; // skip /
        while (po.pos < po.len && !IsDelimiter(po.text[po.pos]) && po.text[po.pos] != '/' &&
               po.text[po.pos] != '(' && po.text[po.pos] != '<' && po.text[po.pos] != '[') po.pos++;
        return po.text[start..po.pos];
    }

    private static string ReadNumber(OperatorScanState po)
    {
        int start = po.pos;
        if (po.text[po.pos] == '+' || po.text[po.pos] == '-') po.pos++;
        while (po.pos < po.len && ((po.text[po.pos] >= '0' && po.text[po.pos] <= '9') || po.text[po.pos] == '.')) po.pos++;
        return po.text[start..po.pos];
    }

    private static string ReadOperatorName(OperatorScanState po)
    {
        int start = po.pos;
        while (po.pos < po.len && IsOperatorChar(po.text[po.pos])) po.pos++;
        return po.text[start..po.pos];
    }

    private static int FindInlineImageEnd(OperatorScanState po)
    {
        // Skip past BI, then find ID, then find EI
        po.pos += 2; // skip BI
        // Find ID
        while (po.pos + 1 < po.len)
        {
            if (po.text[po.pos] == 'I' && po.text[po.pos + 1] == 'D' &&
                (po.pos == 0 || po.text[po.pos - 1] == ' ' || po.text[po.pos - 1] == '\n' || po.text[po.pos - 1] == '\r'))
            {
                po.pos += 2;
                if (po.pos < po.len && po.text[po.pos] == ' ') po.pos++; // skip single space after ID
                break;
            }
            po.pos++;
        }
        // Find EI — must be preceded by whitespace
        while (po.pos + 2 < po.len)
        {
            if ((po.text[po.pos] == '\n' || po.text[po.pos] == '\r' || po.text[po.pos] == ' ') &&
                po.text[po.pos + 1] == 'E' && po.text[po.pos + 2] == 'I' &&
                (po.pos + 3 >= po.len || IsDelimiter(po.text[po.pos + 3])))
            {
                po.pos += 3;
                return po.pos;
            }
            po.pos++;
        }
        po.pos = po.len;
        return po.pos;
    }
}

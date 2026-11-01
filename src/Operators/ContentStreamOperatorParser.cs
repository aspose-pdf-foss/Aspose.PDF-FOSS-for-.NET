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
        var po = NewScan(data);
        po.result = new List<string>();
        while (po.pos < po.len)
        {
            if (!ScanOperator(po)) break;
        }
        return po.result;
    }

    /// <summary>Hands each operator to <paramref name="sink"/> as the content is scanned, the same
    /// tokens <see cref="ParseOperators"/> lists but without keeping a line per operator: a pass
    /// that only counts operators by name over a large stream reads it this way.</summary>
    internal static void ScanOperators(byte[] data, Action<ScannedOperator> sink)
    {
        var po = NewScan(data);
        po.sink = sink;
        while (po.pos < po.len)
        {
            if (!ScanOperator(po)) break;
        }
    }

    private static OperatorScanState NewScan(byte[] data)
    {
        var po = new OperatorScanState();
        po.data = data;
        po.text = Compat.Latin1.GetString(po.data);
        po.pos = 0;
        po.len = po.text.Length;
        po.operands = new List<(int start, int end)>();
        po.line = new StringBuilder();
        return po;
    }

    /// <summary>One operator as the scan meets it, or one line of an earlier parse: its name, the
    /// place its name follows in its line, and the line itself on demand.</summary>
    internal readonly struct ScannedOperator
    {
        private readonly OperatorScanState? _po;
        private readonly string? _line;

        internal ScannedOperator(OperatorScanState po, string name) { _po = po; Name = name; }

        /// <summary>A line as <see cref="ParseOperators"/> lists it: the name is its last token.</summary>
        internal ScannedOperator(string line)
        {
            _line = line;
            var space = line.LastIndexOf(' ');
            // The ID line carries the image data after its keyword, not before it.
            Name = line.StartsWith(ID.Keyword, StringComparison.Ordinal)
                && (line.Length == ID.Keyword.Length || char.IsWhiteSpace(line[ID.Keyword.Length]))
                ? ID.Keyword
                : space < 0 ? line : line.Substring(space + 1);
        }

        public string Name { get; }

        /// <summary>Where the name starts in the line less one - the operands' length with their
        /// separating spaces - or -1 for an operator without operands.</summary>
        public int NameAt
        {
            get
            {
                if (_line is not null) return _line.LastIndexOf(' ');
                if (_po!.operands.Count == 0) return -1;
                var length = _po.operands.Count - 1;
                foreach (var (start, end) in _po.operands) length += end - start;
                return length;
            }
        }

        /// <summary>The operator's line, operands first and the name last.</summary>
        public string Line => _line ?? (_po!.operands.Count > 0 ? OperatorLine(_po, Name) : Name);
    }

    /// <summary>One operator out, with the operands gathered since the last: to the sink, or as a line.</summary>
    private static void Emit(OperatorScanState po, string name)
    {
        if (po.sink is { } sink) sink(new ScannedOperator(po, name));
        else po.result!.Add(po.operands.Count > 0 ? OperatorLine(po, name) : name);
        po.operands.Clear();
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

    private static (int start, int end) ReadParenString(OperatorScanState po)
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
        // an escape character that ends the data steps one past it
        if (po.pos > po.len) po.pos = po.len;
        return (start, po.pos);
    }

    private static (int start, int end) ReadHexString(OperatorScanState po)
    {
        int start = po.pos;
        po.pos++; // skip <
        while (po.pos < po.len && po.text[po.pos] != '>') po.pos++;
        if (po.pos < po.len) po.pos++; // skip >
        return (start, po.pos);
    }

    private static (int start, int end) ReadDictionary(OperatorScanState po)
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
        return (start, po.pos);
    }

    private static (int start, int end) ReadArray(OperatorScanState po)
    {
        int start = po.pos;
        po.pos++; // skip [
        int depth = 1;
        while (po.pos < po.len && depth > 0)
        {
            var c = po.text[po.pos];
            // A string element holds any byte, a bracket included (a two-byte glyph code
            // such as 0x015D is "\x01]"): step over the string whole, or the array ends
            // inside it and every operator after it is read from the wrong place.
            if (c == '(') { ReadParenString(po); continue; }
            if (c == '<' && !(po.pos + 1 < po.len && po.text[po.pos + 1] == '<')) { ReadHexString(po); continue; }
            if (c == '[') depth++;
            else if (c == ']') depth--;
            po.pos++;
        }
        return (start, po.pos);
    }

    private static (int start, int end) ReadName(OperatorScanState po)
    {
        int start = po.pos;
        po.pos++; // skip /
        while (po.pos < po.len && !IsDelimiter(po.text[po.pos]) && po.text[po.pos] != '/' &&
               po.text[po.pos] != '(' && po.text[po.pos] != '<' && po.text[po.pos] != '[') po.pos++;
        return (start, po.pos);
    }

    private static (int start, int end) ReadNumber(OperatorScanState po)
    {
        int start = po.pos;
        if (po.text[po.pos] == '+' || po.text[po.pos] == '-') po.pos++;
        while (po.pos < po.len && ((po.text[po.pos] >= '0' && po.text[po.pos] <= '9') || po.text[po.pos] == '.')) po.pos++;
        return (start, po.pos);
    }

    private static string ReadOperatorName(OperatorScanState po)
    {
        int start = po.pos;
        while (po.pos < po.len && IsOperatorChar(po.text[po.pos])) po.pos++;
        return po.text[start..po.pos];
    }

    /// <summary>The operands joined by single spaces, then the operator name. The operand text is
    /// sliced here, once per operator, instead of once per operand and again for the join.</summary>
    private static string OperatorLine(OperatorScanState po, string opName)
    {
        var line = po.line.Clear();
        foreach (var (start, end) in po.operands) line.Append(po.text, start, end - start).Append(' ');
        return line.Append(opName).ToString();
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

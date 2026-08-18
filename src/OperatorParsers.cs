using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Stamps;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

/// <summary>
/// Dispatches a parsed operator token (e.g. <c>"BT"</c>, <c>"0.5 0.3 0.1 rg"</c>,
/// <c>"/F1 12 Tf"</c>) to the matching typed <see cref="Operator"/> subclass,
/// falling back to <see cref="RawOperator"/> for commands we don't yet model
/// or whose operands fail to parse cleanly.
/// </summary>
internal static class TypedOperatorParser
{
    internal static Operator Parse(string text)
    {
        var trimmed = text.TrimEnd();
        var lastSpace = trimmed.LastIndexOf(' ');
        var op = lastSpace >= 0 ? trimmed[(lastSpace + 1)..] : trimmed;
        var operandText = lastSpace >= 0 ? trimmed[..lastSpace] : "";

        try
        {
            switch (op)
            {
                case "q":  return new Aspose.Pdf.Operators.GSave();
                case "Q":  return new Aspose.Pdf.Operators.GRestore();
                case "BT": return new Aspose.Pdf.Operators.BT();
                case "ET": return new Aspose.Pdf.Operators.ET();
                case "W":  return new Aspose.Pdf.Operators.Clip();
                case "W*": return new Aspose.Pdf.Operators.EOClip();
                case "EMC": return new Aspose.Pdf.Operators.EMC();
                case "BMC": // /Tag BMC — begin marked content
                    return new Aspose.Pdf.Operators.BMC(operandText.Trim().TrimStart('/'));
                case "BDC": // /Tag <</Props…>> BDC (or /Tag /Name BDC) — begin marked content with properties
                {
                    var t = operandText.TrimStart();
                    var tag = t.StartsWith('/') ? t[1..] : t;
                    var cut = tag.IndexOfAny([' ', '<', '[']);
                    return new Aspose.Pdf.Operators.BDC(cut > 0 ? tag[..cut].Trim() : tag.Trim());
                }
                case "MP":  // /Tag MP — marked-content point
                    return new Aspose.Pdf.Operators.MP(operandText.Trim().TrimStart('/'));
                case "T*": return new Aspose.Pdf.Operators.MoveToNextLine();
                case "Tf":
                {
                    // /Name size Tf
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 2 && ops[0].StartsWith('/')
                        && double.TryParse(ops[1], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var size))
                        return new Aspose.Pdf.Operators.SelectFont(ops[0][1..], size);
                    break;
                }
                case "rg":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 3
                        && TryD(ops[0], out var r) && TryD(ops[1], out var g) && TryD(ops[2], out var b))
                        return new Aspose.Pdf.Operators.SetRGBColor(r, g, b);
                    break;
                }
                case "RG":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 3
                        && TryD(ops[0], out var r) && TryD(ops[1], out var g) && TryD(ops[2], out var b))
                        return new Aspose.Pdf.Operators.SetRGBColorStroke(r, g, b);
                    break;
                }
                case "g":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 1 && TryD(ops[0], out var gray))
                        return new Aspose.Pdf.Operators.SetGray(gray);
                    break;
                }
                case "G":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 1 && TryD(ops[0], out var gray))
                        return new Aspose.Pdf.Operators.SetGrayStroke(gray);
                    break;
                }
                case "k":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 4 && TryD(ops[0], out var c) && TryD(ops[1], out var m)
                        && TryD(ops[2], out var y) && TryD(ops[3], out var kk))
                        return new Aspose.Pdf.Operators.SetCMYKColor(c, m, y, kk);
                    break;
                }
                case "K":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 4 && TryD(ops[0], out var c) && TryD(ops[1], out var m)
                        && TryD(ops[2], out var y) && TryD(ops[3], out var kk))
                        return new Aspose.Pdf.Operators.SetCMYKColorStroke(c, m, y, kk);
                    break;
                }
                case "Td":
                case "TD":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 2 && TryD(ops[0], out var x) && TryD(ops[1], out var y))
                        return op == "Td"
                            ? new Aspose.Pdf.Operators.MoveTextPosition(x, y)
                            : new Aspose.Pdf.Operators.MoveTextPositionSetLeading(x, y);
                    break;
                }
                case "Tm":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 6
                        && TryD(ops[0], out var a) && TryD(ops[1], out var b)
                        && TryD(ops[2], out var c) && TryD(ops[3], out var d)
                        && TryD(ops[4], out var e) && TryD(ops[5], out var f))
                        return new Aspose.Pdf.Operators.SetTextMatrix(a, b, c, d, e, f);
                    break;
                }
                case "Tr":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 1
                        && int.TryParse(ops[0], System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out var mode))
                        return new Aspose.Pdf.Operators.SetTextRenderingMode(mode);
                    break;
                }
                case "Tc":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 1 && TryD(ops[0], out var charSpace))
                        return new Aspose.Pdf.Operators.SetCharacterSpacing(charSpace);
                    break;
                }
                case "Tw":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 1 && TryD(ops[0], out var wordSpace))
                        return new Aspose.Pdf.Operators.SetWordSpacing(wordSpace);
                    break;
                }
                case "Tz":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 1 && TryD(ops[0], out var scale))
                        return new Aspose.Pdf.Operators.SetHorizontalTextScaling(scale);
                    break;
                }
                case "TL":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 1 && TryD(ops[0], out var leading))
                        return new Aspose.Pdf.Operators.SetTextLeading(leading);
                    break;
                }
                case "Ts":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 1 && TryD(ops[0], out var rise))
                        return new Aspose.Pdf.Operators.SetTextRise(rise);
                    break;
                }
                case "cm":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 6
                        && TryD(ops[0], out var a) && TryD(ops[1], out var b)
                        && TryD(ops[2], out var c) && TryD(ops[3], out var d)
                        && TryD(ops[4], out var e) && TryD(ops[5], out var f))
                        return new Aspose.Pdf.Operators.ConcatenateMatrix(a, b, c, d, e, f);
                    break;
                }
                case "Do":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 1 && ops[0].StartsWith('/'))
                        return new Aspose.Pdf.Operators.Do(ops[0][1..]);
                    break;
                }
                case "gs":
                {
                    // /Name gs — apply parameters from a named ExtGState resource.
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 1 && ops[0].StartsWith('/'))
                        return new Aspose.Pdf.Operators.GS(ops[0][1..]);
                    break;
                }
                case "Tj":
                {
                    // (text) Tj — operand is a single literal/hex string
                    var s = ParseSingleStringOperand(operandText);
                    if (s is not null) return new Aspose.Pdf.Operators.ShowText(s);
                    break;
                }
                case "'":
                {
                    // (text) '
                    var s = ParseSingleStringOperand(operandText);
                    if (s is not null) return new Aspose.Pdf.Operators.MoveToNextLineShowText(s);
                    break;
                }
                case "\"":
                {
                    // wordSpace charSpace (text) "
                    var lastParenStart = operandText.LastIndexOf('(');
                    var lastParenEnd = operandText.LastIndexOf(')');
                    if (lastParenStart >= 0 && lastParenEnd > lastParenStart)
                    {
                        var nums = SplitOperands(operandText[..lastParenStart]);
                        var s = ParseSingleStringOperand(operandText[lastParenStart..(lastParenEnd + 1)]);
                        if (s is not null && nums.Length == 2
                            && TryD(nums[0], out var ws) && TryD(nums[1], out var cs))
                            return new Aspose.Pdf.Operators.SetSpacingMoveToNextLineShowText(ws, cs, s);
                    }
                    break;
                }
                case "TJ":
                {
                    // [ (str) num (str) num ... ] TJ — array of strings + numeric kerning
                    var items = ParseTJArrayOperands(operandText);
                    if (items is not null) return new Aspose.Pdf.Operators.SetGlyphsPositionShowText(items);
                    break;
                }
                case "i":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 1 && TryD(ops[0], out var flat))
                        return new Aspose.Pdf.Operators.SetFlat(flat);
                    break;
                }
                case "m":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 2 && TryD(ops[0], out var x) && TryD(ops[1], out var y))
                        return new Aspose.Pdf.Operators.MoveTo(x, y);
                    break;
                }
                case "l":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 2 && TryD(ops[0], out var x) && TryD(ops[1], out var y))
                        return new Aspose.Pdf.Operators.LineTo(x, y);
                    break;
                }
                case "c":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 6 && TryD(ops[0], out var x1) && TryD(ops[1], out var y1)
                        && TryD(ops[2], out var x2) && TryD(ops[3], out var y2)
                        && TryD(ops[4], out var x3) && TryD(ops[5], out var y3))
                        return new Aspose.Pdf.Operators.CurveTo(x1, y1, x2, y2, x3, y3);
                    break;
                }
                case "v":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 4 && TryD(ops[0], out var x2) && TryD(ops[1], out var y2)
                        && TryD(ops[2], out var x3) && TryD(ops[3], out var y3))
                        return new Aspose.Pdf.Operators.CurveTo1(x2, y2, x3, y3);
                    break;
                }
                case "y":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 4 && TryD(ops[0], out var x1) && TryD(ops[1], out var y1)
                        && TryD(ops[2], out var x3) && TryD(ops[3], out var y3))
                        return new Aspose.Pdf.Operators.CurveTo2(x1, y1, x3, y3);
                    break;
                }
                case "re":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 4 && TryD(ops[0], out var x) && TryD(ops[1], out var y)
                        && TryD(ops[2], out var rw) && TryD(ops[3], out var rh))
                        return new Aspose.Pdf.Operators.Re(x, y, rw, rh);
                    break;
                }
                case "w":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 1 && TryD(ops[0], out var lw))
                        return new Aspose.Pdf.Operators.SetLineWidth(lw);
                    break;
                }
                case "J":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 1 && TryD(ops[0], out var cap))
                        return new Aspose.Pdf.Operators.SetLineCap((int)cap);
                    break;
                }
                case "j":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 1 && TryD(ops[0], out var join))
                        return new Aspose.Pdf.Operators.SetLineJoin((int)join);
                    break;
                }
                case "M":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 1 && TryD(ops[0], out var miter))
                        return new Aspose.Pdf.Operators.SetMiterLimit(miter);
                    break;
                }
                // Path-painting and path-close operators (no operands).
                case "h":  return new Aspose.Pdf.Operators.ClosePath();
                case "S":  return new Aspose.Pdf.Operators.Stroke();
                case "s":  return new Aspose.Pdf.Operators.ClosePathStroke();
                case "f":
                case "F":  return new Aspose.Pdf.Operators.Fill();
                case "f*": return new Aspose.Pdf.Operators.EOFill();
                case "B":  return new Aspose.Pdf.Operators.FillStroke();
                case "B*": return new Aspose.Pdf.Operators.EOFillStroke();
                case "b":  return new Aspose.Pdf.Operators.ClosePathFillStroke();
                case "b*": return new Aspose.Pdf.Operators.ClosePathEOFillStroke();
                case "n":  return new Aspose.Pdf.Operators.EndPath();
            }
        }
        catch
        {
            // Operand parse failed — fall through to RawOperator.
        }

        return new RawOperator(text);
    }

    private static bool TryD(string s, out double v) =>
        double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out v);

    /// <summary>Parse a single literal `(text)` or hex `&lt;...&gt;` operand.
    /// Returns null on parse failure; PDF escape sequences inside literal
    /// strings (\\, \(, \)) are unescaped.</summary>
    private static string? ParseSingleStringOperand(string s)
    {
        s = s.Trim();
        if (s.StartsWith('(') && s.EndsWith(')'))
        {
            var body = s.Substring(1, s.Length - 2);
            var sb = new System.Text.StringBuilder(body.Length);
            for (int i = 0; i < body.Length; i++)
            {
                if (body[i] == '\\' && i + 1 < body.Length)
                {
                    sb.Append(body[++i]);
                }
                else sb.Append(body[i]);
            }
            return sb.ToString();
        }
        if (s.StartsWith('<') && s.EndsWith('>'))
        {
            var hex = s.Substring(1, s.Length - 2).Replace(" ", "");
            if (hex.Length % 2 != 0) hex += "0";
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return System.Text.Encoding.Latin1.GetString(bytes);
        }
        return null;
    }

    /// <summary>Parse a `[ (str) num (str) num ... ]` TJ-array operand into
    /// the mixed object[] expected by SetGlyphsPositionShowText.Items.</summary>
    private static object[]? ParseTJArrayOperands(string s)
    {
        s = s.Trim();
        if (!s.StartsWith('[') || !s.EndsWith(']')) return null;
        var inner = s.Substring(1, s.Length - 2);
        var items = new List<object>();
        int i = 0;
        while (i < inner.Length)
        {
            while (i < inner.Length && char.IsWhiteSpace(inner[i])) i++;
            if (i >= inner.Length) break;
            if (inner[i] == '(')
            {
                int start = i;
                i++;
                while (i < inner.Length && inner[i] != ')')
                {
                    if (inner[i] == '\\' && i + 1 < inner.Length) i += 2;
                    else i++;
                }
                if (i >= inner.Length) return null;
                i++; // past ')'
                var parsed = ParseSingleStringOperand(inner[start..i]);
                if (parsed is null) return null;
                items.Add(parsed);
            }
            else if (inner[i] == '<')
            {
                int start = i;
                while (i < inner.Length && inner[i] != '>') i++;
                if (i >= inner.Length) return null;
                i++;
                var parsed = ParseSingleStringOperand(inner[start..i]);
                if (parsed is null) return null;
                items.Add(parsed);
            }
            else
            {
                int start = i;
                while (i < inner.Length && !char.IsWhiteSpace(inner[i]) && inner[i] != '(' && inner[i] != '<') i++;
                var token = inner[start..i];
                if (TryD(token, out var d)) items.Add(d);
                else return null;
            }
        }
        return items.ToArray();
    }

    private static string[] SplitOperands(string s)
    {
        var trimmed = s.Trim();
        return trimmed.Length == 0
            ? Array.Empty<string>()
            : trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}

/// <summary>Parses PDF content stream bytes into individual operator strings.</summary>
internal static class ContentStreamOperatorParser
{
    internal static List<string> ParseOperators(byte[] data)
    {
        var result = new List<string>();
        var text = Encoding.Latin1.GetString(data);
        int pos = 0;
        int len = text.Length;
        var operands = new List<string>();

        while (pos < len)
        {
            SkipWhitespaceAndComments(text, ref pos, len);
            if (pos >= len) break;

            char c = text[pos];

            if (c == '(')
            {
                operands.Add(ReadParenString(text, ref pos, len));
            }
            else if (c == '<' && pos + 1 < len && text[pos + 1] == '<')
            {
                operands.Add(ReadDictionary(text, ref pos, len));
            }
            else if (c == '<')
            {
                operands.Add(ReadHexString(text, ref pos, len));
            }
            else if (c == '[')
            {
                operands.Add(ReadArray(text, ref pos, len));
            }
            else if (c == '/')
            {
                operands.Add(ReadName(text, ref pos, len));
            }
            else if (c == '-' || c == '+' || c == '.' || (c >= '0' && c <= '9'))
            {
                operands.Add(ReadNumber(text, ref pos, len));
            }
            else if (c == 'B' && pos + 1 < len && text[pos + 1] == 'I' &&
                     (pos + 2 >= len || IsDelimiter(text[pos + 2])))
            {
                // BI . ID . EI — inline image: count as 3 operators (BI, ID, EI)
                // per the OperatorCollection contract
                var start = pos;
                var eiPos = FindInlineImageEnd(text, ref pos, len);
                var fullText = text[start..eiPos].TrimEnd();
                // BI with its key-value pairs
                var idIdx = fullText.IndexOf("\nID", StringComparison.Ordinal);
                if (idIdx < 0) idIdx = fullText.IndexOf(" ID", StringComparison.Ordinal);
                if (idIdx >= 0)
                {
                    result.Add(fullText[..idIdx].TrimEnd()); // BI + parameters
                    result.Add("ID"); // ID operator
                    result.Add("EI"); // EI operator
                }
                else
                {
                    result.Add(fullText);
                }
                operands.Clear();
            }
            else if (IsOperatorChar(c))
            {
                var opName = ReadOperatorName(text, ref pos, len);
                // Check for true/false/null which are operands
                if (opName == "true" || opName == "false" || opName == "null")
                {
                    operands.Add(opName);
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
                        if (operands.Count > 0)
                        {
                            result.Add(string.Join(" ", operands) + " " + opName[0]);
                            operands.Clear();
                        }
                        else
                        {
                            result.Add(opName[0].ToString());
                        }
                        // Remaining characters are individual operators
                        for (int ci = 1; ci < opName.Length; ci++)
                            result.Add(opName[ci].ToString());
                    }
                    else
                    {
                        // This is an operator — emit with operands
                        if (operands.Count > 0)
                        {
                            result.Add(string.Join(" ", operands) + " " + opName);
                            operands.Clear();
                        }
                        else
                        {
                            result.Add(opName);
                        }
                    }
                }
            }
            else
            {
                pos++; // skip unexpected chars
            }
        }

        return result;
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

    private static void SkipWhitespaceAndComments(string text, ref int pos, int len)
    {
        while (pos < len)
        {
            char c = text[pos];
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\0')
            {
                pos++;
            }
            else if (c == '%')
            {
                while (pos < len && text[pos] != '\n' && text[pos] != '\r') pos++;
            }
            else break;
        }
    }

    private static string ReadParenString(string text, ref int pos, int len)
    {
        int start = pos;
        pos++; // skip (
        int depth = 1;
        while (pos < len && depth > 0)
        {
            if (text[pos] == '\\') { pos += 2; continue; }
            if (text[pos] == '(') depth++;
            else if (text[pos] == ')') depth--;
            pos++;
        }
        return text[start..pos];
    }

    private static string ReadHexString(string text, ref int pos, int len)
    {
        int start = pos;
        pos++; // skip <
        while (pos < len && text[pos] != '>') pos++;
        if (pos < len) pos++; // skip >
        return text[start..pos];
    }

    private static string ReadDictionary(string text, ref int pos, int len)
    {
        int start = pos;
        pos += 2; // skip <<
        int depth = 1;
        while (pos < len && depth > 0)
        {
            if (pos + 1 < len && text[pos] == '<' && text[pos + 1] == '<') { depth++; pos += 2; }
            else if (pos + 1 < len && text[pos] == '>' && text[pos + 1] == '>') { depth--; pos += 2; }
            else pos++;
        }
        return text[start..pos];
    }

    private static string ReadArray(string text, ref int pos, int len)
    {
        int start = pos;
        pos++; // skip [
        int depth = 1;
        while (pos < len && depth > 0)
        {
            if (text[pos] == '[') depth++;
            else if (text[pos] == ']') depth--;
            pos++;
        }
        return text[start..pos];
    }

    private static string ReadName(string text, ref int pos, int len)
    {
        int start = pos;
        pos++; // skip /
        while (pos < len && !IsDelimiter(text[pos]) && text[pos] != '/' &&
               text[pos] != '(' && text[pos] != '<' && text[pos] != '[') pos++;
        return text[start..pos];
    }

    private static string ReadNumber(string text, ref int pos, int len)
    {
        int start = pos;
        if (text[pos] == '+' || text[pos] == '-') pos++;
        while (pos < len && ((text[pos] >= '0' && text[pos] <= '9') || text[pos] == '.')) pos++;
        return text[start..pos];
    }

    private static string ReadOperatorName(string text, ref int pos, int len)
    {
        int start = pos;
        while (pos < len && IsOperatorChar(text[pos])) pos++;
        return text[start..pos];
    }

    private static int FindInlineImageEnd(string text, ref int pos, int len)
    {
        // Skip past BI, then find ID, then find EI
        pos += 2; // skip BI
        // Find ID
        while (pos + 1 < len)
        {
            if (text[pos] == 'I' && text[pos + 1] == 'D' &&
                (pos == 0 || text[pos - 1] == ' ' || text[pos - 1] == '\n' || text[pos - 1] == '\r'))
            {
                pos += 2;
                if (pos < len && text[pos] == ' ') pos++; // skip single space after ID
                break;
            }
            pos++;
        }
        // Find EI — must be preceded by whitespace
        while (pos + 2 < len)
        {
            if ((text[pos] == '\n' || text[pos] == '\r' || text[pos] == ' ') &&
                text[pos + 1] == 'E' && text[pos + 2] == 'I' &&
                (pos + 3 >= len || IsDelimiter(text[pos + 3])))
            {
                pos += 3;
                return pos;
            }
            pos++;
        }
        pos = len;
        return pos;
    }
}

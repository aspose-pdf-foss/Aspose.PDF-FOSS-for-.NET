using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Shading;
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
                case "q": case "Q": case "W": case "W*": case "EMC": case "BMC": case "BDC": case "MP": case "cm": case "Do": case "gs": case "i": case "w": case "J": case "j": case "M":
                    if (ParseStateOperator(operandText, op) is { } parseStateOperatorResult) return parseStateOperatorResult;
                    break;
                case "BT": case "ET": case "T*": case "Tf": case "Td": case "TD": case "Tm": case "Tr": case "Tc": case "Tw": case "Tz": case "TL": case "Ts": case "Tj": case "'": case "TJ":
                    if (ParseTextOperator(operandText, op) is { } parseTextOperatorResult) return parseTextOperatorResult;
                    break;
                case "rg": case "RG": case "g": case "G": case "k": case "K":
                    if (ParseColorOperator(operandText, op) is { } parseColorOperatorResult) return parseColorOperatorResult;
                    break;
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
                            && TryD(nums[0]) is { } ws && TryD(nums[1]) is { } cs)
                            return new Aspose.Pdf.Operators.SetSpacingMoveToNextLineShowText(ws, cs, s);
                    }
                    break;
                }
                case "m": case "l": case "c": case "v": case "y": case "re": case "h": case "S": case "s": case "f": case "F": case "f*": case "B": case "B*": case "b": case "b*": case "n":
                    if (ParsePathOperator(operandText, op) is { } parsePathOperatorResult) return parsePathOperatorResult;
                    break;
            }
        }
        catch
        {
            // Operand parse failed — fall through to RawOperator.
        }

        return new RawOperator(text);
    }

    private static double? TryD(string s) =>
        double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;

    /// <summary>Parse a single literal `(text)` or hex `&lt;...&gt;` operand.
    /// Returns null on parse failure; PDF escape sequences inside literal
    /// strings (\\, \(, \)) are unescaped.</summary>
    private static string? ParseSingleStringOperand(string s)
    {
        s = s.Trim();
        if (s.StartsWith('(') && s.EndsWith(')'))
        {
            return UnescapeLiteral(s.Substring(1, s.Length - 2));
        }
        if (s.StartsWith('<') && s.EndsWith('>'))
        {
            var hex = s.Substring(1, s.Length - 2).Replace(" ", "");
            if (hex.Length % 2 != 0) hex += "0";
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return Compat.Latin1.GetString(bytes);
        }
        return null;
    }

    /// <summary>The bytes a literal string stands for: the named escapes (\n \r \t \b \f),
    /// an escaped delimiter or backslash, an octal code of up to three digits, and a
    /// backslash before a line end that joins the lines; a bare line end reads as a
    /// newline whatever its form. A backslash before any other character drops.</summary>
    private static string UnescapeLiteral(string body)
    {
        var sb = new System.Text.StringBuilder(body.Length);
        for (int i = 0; i < body.Length; i++)
        {
            var c = body[i];
            if (c == '\r')
            {
                if (i + 1 < body.Length && body[i + 1] == '\n') i++;
                sb.Append('\n');
                continue;
            }
            if (c != '\\' || i + 1 >= body.Length) { sb.Append(c); continue; }
            var e = body[++i];
            switch (e)
            {
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case '\r':
                    if (i + 1 < body.Length && body[i + 1] == '\n') i++;
                    break;
                case '\n': break;
                default:
                    if (e >= '0' && e <= '7')
                    {
                        int code = e - '0';
                        for (int k = 0; k < 2 && i + 1 < body.Length && body[i + 1] >= '0' && body[i + 1] <= '7'; k++)
                            code = code * 8 + (body[++i] - '0');
                        sb.Append((char)(code & 0xFF));
                    }
                    else sb.Append(e);
                    break;
            }
        }
        return sb.ToString();
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
                if (TryD(token) is { } d) items.Add(d);
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

    /// <summary>The graphics-state, marked-content, XObject and stroke-parameter operators.</summary>
    private static Operator? ParseStateOperator(string operandText, string op)
    {
        switch (op)
        {
            case "q":  return new Aspose.Pdf.Operators.GSave();
            case "Q":  return new Aspose.Pdf.Operators.GRestore();
            case "W":  return new Aspose.Pdf.Operators.Clip();
            case "W*": return new Aspose.Pdf.Operators.EOClip();
            case "EMC": case "BMC": case "BDC": case "MP":
                if (ParseMarkedContentOperator(operandText, op) is { } parseMarkedContentOperatorResult) return parseMarkedContentOperatorResult;
                break;
            case "cm":
            {
                var ops = SplitOperands(operandText);
                if (ops.Length == 6
                    && TryD(ops[0]) is { } a && TryD(ops[1]) is { } b
                    && TryD(ops[2]) is { } c && TryD(ops[3]) is { } d
                    && TryD(ops[4]) is { } e && TryD(ops[5]) is { } f)
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
            case "i":
            {
                var ops = SplitOperands(operandText);
                if (ops.Length == 1 && TryD(ops[0]) is { } flat)
                    return new Aspose.Pdf.Operators.SetFlat(flat);
                break;
            }
            case "w":
            {
                var ops = SplitOperands(operandText);
                if (ops.Length == 1 && TryD(ops[0]) is { } lw)
                    return new Aspose.Pdf.Operators.SetLineWidth(lw);
                break;
            }
            case "J":
            {
                var ops = SplitOperands(operandText);
                if (ops.Length == 1 && TryD(ops[0]) is { } cap)
                    return new Aspose.Pdf.Operators.SetLineCap((int)cap);
                break;
            }
            case "j":
            {
                var ops = SplitOperands(operandText);
                if (ops.Length == 1 && TryD(ops[0]) is { } join)
                    return new Aspose.Pdf.Operators.SetLineJoin((int)join);
                break;
            }
            case "M":
            {
                var ops = SplitOperands(operandText);
                if (ops.Length == 1 && TryD(ops[0]) is { } miter)
                    return new Aspose.Pdf.Operators.SetMiterLimit(miter);
                break;
            }
        }
        return null;
    }

    /// <summary>The text object, its state, its positioning and the strings it shows.</summary>
    private static Operator? ParseTextOperator(string operandText, string op)
    {
        switch (op)
        {
            case "BT": return new Aspose.Pdf.Operators.BT();
            case "ET": return new Aspose.Pdf.Operators.ET();
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
            case "Td":
            case "TD":
            {
                var ops = SplitOperands(operandText);
                if (ops.Length == 2 && TryD(ops[0]) is { } x && TryD(ops[1]) is { } y)
                    return op == "Td"
                        ? new Aspose.Pdf.Operators.MoveTextPosition(x, y)
                        : new Aspose.Pdf.Operators.MoveTextPositionSetLeading(x, y);
                break;
            }
            case "Tm":
            {
                var ops = SplitOperands(operandText);
                if (ops.Length == 6
                    && TryD(ops[0]) is { } a && TryD(ops[1]) is { } b
                    && TryD(ops[2]) is { } c && TryD(ops[3]) is { } d
                    && TryD(ops[4]) is { } e && TryD(ops[5]) is { } f)
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
                if (ops.Length == 1 && TryD(ops[0]) is { } charSpace)
                    return new Aspose.Pdf.Operators.SetCharacterSpacing(charSpace);
                break;
            }
            case "Tw":
            {
                var ops = SplitOperands(operandText);
                if (ops.Length == 1 && TryD(ops[0]) is { } wordSpace)
                    return new Aspose.Pdf.Operators.SetWordSpacing(wordSpace);
                break;
            }
            case "Tz":
            {
                var ops = SplitOperands(operandText);
                if (ops.Length == 1 && TryD(ops[0]) is { } scale)
                    return new Aspose.Pdf.Operators.SetHorizontalTextScaling(scale);
                break;
            }
            case "TL":
            {
                var ops = SplitOperands(operandText);
                if (ops.Length == 1 && TryD(ops[0]) is { } leading)
                    return new Aspose.Pdf.Operators.SetTextLeading(leading);
                break;
            }
            case "Ts":
            {
                var ops = SplitOperands(operandText);
                if (ops.Length == 1 && TryD(ops[0]) is { } rise)
                    return new Aspose.Pdf.Operators.SetTextRise(rise);
                break;
            }
            case "Tj": case "'": case "TJ":
                if (ParseTextShowOperator(operandText, op) is { } parseTextShowOperatorResult) return parseTextShowOperatorResult;
                break;
        }
        return null;
    }

    /// <summary>The device colour operators, fill and stroke.</summary>
    private static Operator? ParseColorOperator(string operandText, string op)
    {
        switch (op)
        {
            case "rg":
            {
                var ops = SplitOperands(operandText);
                if (ops.Length == 3
                    && TryD(ops[0]) is { } r && TryD(ops[1]) is { } g && TryD(ops[2]) is { } b)
                    return new Aspose.Pdf.Operators.SetRGBColor(r, g, b);
                break;
            }
            case "RG":
            {
                var ops = SplitOperands(operandText);
                if (ops.Length == 3
                    && TryD(ops[0]) is { } r && TryD(ops[1]) is { } g && TryD(ops[2]) is { } b)
                    return new Aspose.Pdf.Operators.SetRGBColorStroke(r, g, b);
                break;
            }
            case "g":
            {
                var ops = SplitOperands(operandText);
                if (ops.Length == 1 && TryD(ops[0]) is { } gray)
                    return new Aspose.Pdf.Operators.SetGray(gray);
                break;
            }
            case "G":
            {
                var ops = SplitOperands(operandText);
                if (ops.Length == 1 && TryD(ops[0]) is { } gray)
                    return new Aspose.Pdf.Operators.SetGrayStroke(gray);
                break;
            }
            case "k":
            {
                var ops = SplitOperands(operandText);
                if (ops.Length == 4 && TryD(ops[0]) is { } c && TryD(ops[1]) is { } m
                    && TryD(ops[2]) is { } y && TryD(ops[3]) is { } kk)
                    return new Aspose.Pdf.Operators.SetCMYKColor(c, m, y, kk);
                break;
            }
            case "K":
            {
                var ops = SplitOperands(operandText);
                if (ops.Length == 4 && TryD(ops[0]) is { } c && TryD(ops[1]) is { } m
                    && TryD(ops[2]) is { } y && TryD(ops[3]) is { } kk)
                    return new Aspose.Pdf.Operators.SetCMYKColorStroke(c, m, y, kk);
                break;
            }
        }
        return null;
    }

    /// <summary>The path construction and painting operators.</summary>
    private static Operator? ParsePathOperator(string operandText, string op)
    {
        switch (op)
        {
            case "m":
            {
                var ops = SplitOperands(operandText);
                if (ops.Length == 2 && TryD(ops[0]) is { } x && TryD(ops[1]) is { } y)
                    return new Aspose.Pdf.Operators.MoveTo(x, y);
                break;
            }
            case "l":
            {
                var ops = SplitOperands(operandText);
                if (ops.Length == 2 && TryD(ops[0]) is { } x && TryD(ops[1]) is { } y)
                    return new Aspose.Pdf.Operators.LineTo(x, y);
                break;
            }
            case "c":
            {
                var ops = SplitOperands(operandText);
                if (ops.Length == 6 && TryD(ops[0]) is { } x1 && TryD(ops[1]) is { } y1
                    && TryD(ops[2]) is { } x2 && TryD(ops[3]) is { } y2
                    && TryD(ops[4]) is { } x3 && TryD(ops[5]) is { } y3)
                    return new Aspose.Pdf.Operators.CurveTo(x1, y1, x2, y2, x3, y3);
                break;
            }
            case "v":
            {
                var ops = SplitOperands(operandText);
                if (ops.Length == 4 && TryD(ops[0]) is { } x2 && TryD(ops[1]) is { } y2
                    && TryD(ops[2]) is { } x3 && TryD(ops[3]) is { } y3)
                    return new Aspose.Pdf.Operators.CurveTo1(x2, y2, x3, y3);
                break;
            }
            case "y":
            {
                var ops = SplitOperands(operandText);
                if (ops.Length == 4 && TryD(ops[0]) is { } x1 && TryD(ops[1]) is { } y1
                    && TryD(ops[2]) is { } x3 && TryD(ops[3]) is { } y3)
                    return new Aspose.Pdf.Operators.CurveTo2(x1, y1, x3, y3);
                break;
            }
            case "re":
            {
                var ops = SplitOperands(operandText);
                if (ops.Length == 4 && TryD(ops[0]) is { } x && TryD(ops[1]) is { } y
                    && TryD(ops[2]) is { } rw && TryD(ops[3]) is { } rh)
                    return new Aspose.Pdf.Operators.Re(x, y, rw, rh);
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
        return null;
    }

    /// <summary>The marked-content operators: begin with a tag and its properties, a point, and the end.</summary>
    private static Operator? ParseMarkedContentOperator(string operandText, string op)
    {
        switch (op)
        {
            case "EMC": return new Aspose.Pdf.Operators.EMC();
            case "BMC": // /Tag BMC — begin marked content
                return new Aspose.Pdf.Operators.BMC(operandText.Trim().TrimStart('/'));
            case "BDC": // /Tag <</Props…>> BDC (or /Tag /Name BDC) — begin marked content with properties
            {
                var t = operandText.TrimStart();
                var tag = t.StartsWith('/') ? t[1..] : t;
                var cut = tag.IndexOfAny([' ', '<', '[']);
                var tagName = cut > 0 ? tag[..cut].Trim() : tag.Trim();
                // An inline property dict carries the marked-content id — surface
                // /MCID (+ /Lang, /E) through BDCProperties so consumers reading
                // page.Contents see the ids a tagged page's BDCs carry.
                var dictIdx = operandText.IndexOf("<<", System.StringComparison.Ordinal);
                if (dictIdx >= 0)
                {
                    var dictText = operandText[dictIdx..];
                    var mMcid = System.Text.RegularExpressions.Regex.Match(dictText, "/MCID\\s+(\\d+)");
                    var mLang = System.Text.RegularExpressions.Regex.Match(dictText, "/Lang\\s*\\(([^)]*)\\)");
                    var mExp = System.Text.RegularExpressions.Regex.Match(dictText, "/E\\s*\\(([^)]*)\\)");
                    // ANY inline dict yields a properties object - an artifact BDC
                    // (<</BBox .../Type /Pagination>>) has no MCID, but consumers
                    // walking page.Contents read bdc.Properties on every BDC and a
                    // null there is a corpus-visible break, not a shorthand.
                    return new Aspose.Pdf.Operators.BDC(tagName, new Aspose.Pdf.Facades.BDCProperties(
                        mMcid.Success
                            ? int.Parse(mMcid.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
                            : (int?)null,
                        mLang.Success ? mLang.Groups[1].Value : null,
                        mExp.Success ? mExp.Groups[1].Value : null));
                }
                return new Aspose.Pdf.Operators.BDC(tagName);
            }
            case "MP":  // /Tag MP — marked-content point
                return new Aspose.Pdf.Operators.MP(operandText.Trim().TrimStart('/'));
        }
        return null;
    }

    /// <summary>The text-showing operators: a string, a string on the next line, an array of strings and kerns.</summary>
    private static Operator? ParseTextShowOperator(string operandText, string op)
    {
        switch (op)
        {
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
            case "TJ":
            {
                // [ (str) num (str) num ... ] TJ — array of strings + numeric kerning
                var items = ParseTJArrayOperands(operandText);
                if (items is not null) return new Aspose.Pdf.Operators.SetGlyphsPositionShowText(items);
                break;
            }
        }
        return null;
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Aspose.Pdf.Converters.PsToPdf;

/// <summary>
/// Turns PostScript source bytes into objects, one token at a time. The scanner is
/// the only place that knows the language's lexical rules; everything above it sees
/// objects. It reads from a byte buffer so that <c>currentfile</c> can hand the
/// interpreter's own remaining input to a program that wants to read data inline.
/// </summary>
internal sealed class PsScanner
{
    private const int RadixMin = 2;
    private const int RadixMax = 36;

    private readonly byte[] _data;

    /// <summary>Scan the given source.</summary>
    public PsScanner(byte[] data, int position = 0)
    {
        _data = data ?? new byte[0];
        Position = position;
    }

    /// <summary>Where the next token starts.</summary>
    public int Position { get; set; }

    /// <summary>The source being scanned.</summary>
    public byte[] Data => _data;

    /// <summary>Whether a byte ends a token without being part of it.</summary>
    public static bool IsWhite(int c) =>
        c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\f' || c == 0;

    /// <summary>Whether a byte both ends a token and starts one of its own.</summary>
    public static bool IsDelimiter(int c) =>
        c == '(' || c == ')' || c == '<' || c == '>' || c == '[' || c == ']' ||
        c == '{' || c == '}' || c == '/' || c == '%';

    /// <summary>Read the next object, or false at end of source. A procedure body is
    /// returned whole, so the caller never sees a brace.</summary>
    public bool TryRead(out PsValue value)
    {
        value = PsValue.Null;
        if (!SkipToToken()) return false;
        var c = _data[Position];
        switch (c)
        {
            case (byte)'{':
                Position++;
                value = PsValue.Proc(ReadProcedureBody());
                return true;
            case (byte)'}':
            case (byte)']':
                Position++;
                value = PsValue.ExecName(((char)c).ToString());
                return true;
            case (byte)'[':
                Position++;
                value = PsValue.ExecName("[");
                return true;
            case (byte)'/':
                return TryReadName(out value);
            case (byte)'(':
                Position++;
                value = PsValue.Str(ReadLiteralString());
                return true;
            case (byte)'<':
                return TryReadAngle(out value);
            case (byte)'>':
                Position += _data.Length - Position >= 2 && _data[Position + 1] == '>' ? 2 : 1;
                value = PsValue.ExecName(">>");
                return true;
            case (byte)')':
                Position++;
                return TryRead(out value);
            default:
                value = ClassifyWord(ReadWord());
                return true;
        }
    }

    /// <summary>Advance to the first byte of the next token, skipping whitespace and
    /// comments. Returns false when the source is exhausted.</summary>
    public bool SkipToToken()
    {
        while (Position < _data.Length)
        {
            var c = _data[Position];
            if (IsWhite(c))
            {
                Position++;
            }
            else if (c == '%')
            {
                while (Position < _data.Length && _data[Position] != '\n' && _data[Position] != '\r')
                    Position++;
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Read a procedure body up to its closing brace, nesting included.</summary>
    private PsArray ReadProcedureBody()
    {
        var items = new List<PsValue>();
        while (SkipToToken())
        {
            if (_data[Position] == '}')
            {
                Position++;
                break;
            }

            if (!TryRead(out var item)) break;
            items.Add(item);
        }

        return new PsArray(items);
    }

    /// <summary>Read a literal name, or the immediately-evaluated <c>//name</c> form.
    /// The immediate form is substituted HERE, while scanning, so that it works inside
    /// a procedure body too — a procedure that reads a value at definition time and
    /// then never sees the name again is exactly what the form is for.</summary>
    private bool TryReadName(out PsValue value)
    {
        Position++;
        var immediate = Position < _data.Length && _data[Position] == '/';
        if (immediate) Position++;
        var text = ReadWord();
        if (immediate && ImmediateResolver != null)
        {
            var resolved = ImmediateResolver(text);
            if (resolved.HasValue)
            {
                value = resolved.Value;
                return true;
            }
        }

        value = PsValue.LiteralName(text);
        return true;
    }

    /// <summary>Looks a name up for the <c>//name</c> form, or returns null when it is
    /// undefined and the literal name must stand.</summary>
    public Func<string, PsValue?>? ImmediateResolver { get; set; }

    /// <summary>Read a hex string, or report the dictionary-opening token.</summary>
    private bool TryReadAngle(out PsValue value)
    {
        if (_data.Length - Position >= 2 && _data[Position + 1] == '<')
        {
            Position += 2;
            value = PsValue.ExecName("<<");
            return true;
        }

        if (_data.Length - Position >= 2 && _data[Position + 1] == '~')
        {
            Position += 2;
            value = PsValue.Str(ReadAscii85String());
            return true;
        }

        Position++;
        value = PsValue.Str(ReadHexString());
        return true;
    }

    /// <summary>Read a parenthesised string, honouring nesting and the escape set.</summary>
    private PsString ReadLiteralString()
    {
        var bytes = new List<byte>();
        var depth = 1;
        while (Position < _data.Length)
        {
            var c = _data[Position++];
            if (c == '\\')
            {
                ReadEscape(bytes);
                continue;
            }

            if (c == '(') depth++;
            else if (c == ')' && --depth == 0) break;
            bytes.Add(c);
        }

        return new PsString(bytes.ToArray());
    }

    /// <summary>Consume one backslash escape, appending what it stands for.</summary>
    private void ReadEscape(List<byte> bytes)
    {
        if (Position >= _data.Length) return;
        var e = _data[Position++];
        switch (e)
        {
            case (byte)'n': bytes.Add((byte)'\n'); return;
            case (byte)'r': bytes.Add((byte)'\r'); return;
            case (byte)'t': bytes.Add((byte)'\t'); return;
            case (byte)'b': bytes.Add(8); return;
            case (byte)'f': bytes.Add(12); return;
            case (byte)'\r':
                if (Position < _data.Length && _data[Position] == '\n') Position++;
                return;
            case (byte)'\n':
                return;
            default:
                if (e >= '0' && e <= '7') bytes.Add(ReadOctal(e));
                else bytes.Add(e);
                return;
        }
    }

    /// <summary>Read the remaining digits of a one-to-three digit octal escape.</summary>
    private byte ReadOctal(byte first)
    {
        var value = first - '0';
        for (var i = 0; i < 2 && Position < _data.Length; i++)
        {
            var d = _data[Position];
            if (d < '0' || d > '7') break;
            value = value * 8 + (d - '0');
            Position++;
        }

        return (byte)value;
    }

    /// <summary>Read a hex string up to its closing angle bracket.</summary>
    private PsString ReadHexString()
    {
        var bytes = new List<byte>();
        var high = -1;
        while (Position < _data.Length)
        {
            var c = _data[Position++];
            if (c == '>') break;
            var digit = HexDigit(c);
            if (digit < 0) continue;
            if (high < 0) high = digit;
            else
            {
                bytes.Add((byte)((high << 4) | digit));
                high = -1;
            }
        }

        if (high >= 0) bytes.Add((byte)(high << 4));
        return new PsString(bytes.ToArray());
    }

    /// <summary>Read an ASCII85 string up to its <c>~&gt;</c> terminator.</summary>
    private PsString ReadAscii85String()
    {
        var start = Position;
        while (Position < _data.Length &&
               !(_data[Position] == '~' && Position + 1 < _data.Length && _data[Position + 1] == '>'))
            Position++;
        var body = new byte[Position - start];
        Array.Copy(_data, start, body, 0, body.Length);
        Position = Math.Min(_data.Length, Position + 2);
        return new PsString(IO.Filters.Ascii85DecodeFilter.Decode(body));
    }

    private static int HexDigit(int c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;
        return -1;
    }

    /// <summary>Read a run of regular characters.</summary>
    private string ReadWord()
    {
        var sb = new StringBuilder();
        while (Position < _data.Length)
        {
            var c = _data[Position];
            if (IsWhite(c) || IsDelimiter(c)) break;
            sb.Append((char)c);
            Position++;
        }

        return sb.ToString();
    }

    /// <summary>Decide whether a bare word is a number or an executable name.</summary>
    public static PsValue ClassifyWord(string word)
    {
        if (word.Length == 0) return PsValue.ExecName(string.Empty);
        if (TryParseNumber(word, out var number)) return number;
        return PsValue.ExecName(word);
    }

    /// <summary>Parse the three numeric forms: integer, real and radix.</summary>
    public static bool TryParseNumber(string word, out PsValue value)
    {
        value = PsValue.Null;
        var hash = word.IndexOf('#');
        if (hash > 0) return TryParseRadix(word, hash, out value);
        if (!LooksNumeric(word)) return false;
        if (word.IndexOf('.') < 0 && word.IndexOf('e') < 0 && word.IndexOf('E') < 0 &&
            long.TryParse(word, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var i))
        {
            value = PsValue.Int(i);
            return true;
        }

        if (double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            value = PsValue.Real(d);
            return true;
        }

        return false;
    }

    /// <summary>A word can only be a number when it starts like one, which keeps
    /// names such as <c>e</c> or <c>-</c> from reaching the number parsers.</summary>
    private static bool LooksNumeric(string word)
    {
        var i = word[0] == '+' || word[0] == '-' ? 1 : 0;
        if (i >= word.Length) return false;
        return char.IsDigit(word[i]) || (word[i] == '.' && i + 1 < word.Length && char.IsDigit(word[i + 1]));
    }

    /// <summary>Parse <c>radix#digits</c>, the form <c>16#FF</c> uses.</summary>
    private static bool TryParseRadix(string word, int hash, out PsValue value)
    {
        value = PsValue.Null;
        if (!int.TryParse(word.Substring(0, hash), NumberStyles.None, CultureInfo.InvariantCulture,
                out var radix) || radix < RadixMin || radix > RadixMax)
            return false;
        long result = 0;
        for (var i = hash + 1; i < word.Length; i++)
        {
            var digit = DigitValue(word[i]);
            if (digit < 0 || digit >= radix) return false;
            result = result * radix + digit;
        }

        if (word.Length == hash + 1) return false;
        value = PsValue.Int(result);
        return true;
    }

    private static int DigitValue(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'z') return c - 'a' + 10;
        if (c >= 'A' && c <= 'Z') return c - 'A' + 10;
        return -1;
    }
}

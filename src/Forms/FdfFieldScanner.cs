using System.Text;

namespace Aspose.Pdf.Forms;

/// <summary>Reads the field tree of an FDF file straight from its text.
/// <para>FDF /Fields is a tree: each entry has an optional /T (partial name), an optional /V
/// (value), and an optional /Kids (child entries). The full field name of a leaf is the
/// dotted join of /T values from the root. Acrobat exports hierarchical fields this way -
/// e.g. <c>&lt;&lt;/Kids[&lt;&lt;/T(0)/V(01-09-2010)&gt;&gt; ...]/T(SA Datum Sollicitatie)&gt;&gt;</c>
/// resolves to "SA Datum Sollicitatie.0". A flat <c>/T(.)/V(.)</c> scan would mistake the
/// kids' partial names ("0".."6") for full names, so this is a recursive-descent walk that
/// handles nested <c>&lt;&lt;...&gt;&gt;</c> dicts and <c>[...]</c> arrays.</para>
/// <para>The text is the file's bytes Latin1-decoded, so string literals arrive as raw
/// bytes: octal escapes are folded, and a UTF-16BE BOM marks a Unicode text string whose
/// byte pairs fold back into characters (PDF 32000 §7.9.2.2) - Hebrew/CJK values would
/// otherwise import as mojibake.</para>
/// <para>One instance is one walk: the text and the cursor into it.</para></summary>
internal sealed class FdfFieldScanner
{
    private readonly string _t;
    private int _pos;
    private readonly List<(string name, string value)> _fields = new();

    private FdfFieldScanner(string fdf)
    {
        _t = fdf;
    }

    /// <summary>Every leaf field of the /Fields tree with its full dotted name and its /V
    /// text - a string literal decoded, a name object without its slash, or empty when the
    /// entry carries no readable value.</summary>
    internal static List<(string name, string value)> ReadFields(string fdf)
    {
        var scanner = new FdfFieldScanner(fdf);
        var fieldsIdx = fdf.IndexOf("/Fields", StringComparison.Ordinal);
        if (fieldsIdx < 0) return scanner._fields;
        var pos = fdf.IndexOf('[', fieldsIdx);
        if (pos < 0) return scanner._fields;
        scanner._pos = pos + 1; // step past '['
        scanner.ReadFieldsArray(parentPath: null);
        return scanner._fields;
    }

    private void ReadFieldsArray(string? parentPath)
    {
        while (_pos < _t.Length)
        {
            SkipWS();
            if (_pos >= _t.Length) return;
            if (_t[_pos] == ']') { _pos++; return; }
            if (_pos + 1 < _t.Length && _t[_pos] == '<' && _t[_pos + 1] == '<')
            {
                _pos += 2;
                ReadFieldDict(parentPath);
            }
            else
            {
                _pos++; // tolerate stray bytes
            }
        }
    }

    private void ReadFieldDict(string? parentPath)
    {
        string? partialName = null;
        string? value = null;
        int kidsStart = -1;

        while (_pos < _t.Length)
        {
            SkipWS();
            if (_pos >= _t.Length) return;
            if (_pos + 1 < _t.Length && _t[_pos] == '>' && _t[_pos + 1] == '>') { _pos += 2; break; }
            if (_t[_pos] != '/') { _pos++; continue; }
            _pos++; // step past '/'
            int kStart = _pos;
            while (_pos < _t.Length && !IsDelimOrWS(_t[_pos])) _pos++;
            var key = _t.Substring(kStart, _pos - kStart);
            SkipWS();
            if (key == "T" && _pos < _t.Length && _t[_pos] == '(')
                partialName = ReadStringLiteral();
            else if (key == "V")
                value = ReadValue();
            else if (key == "Kids" && _pos < _t.Length && _t[_pos] == '[')
            {
                kidsStart = _pos + 1; // remember; consume below
                SkipArray();
            }
            else
                SkipValue();
        }

        var fullPath = (parentPath, partialName) switch
        {
            (null, null) => null,
            (null, _) => partialName,
            (_, null) => parentPath,
            _ => $"{parentPath}.{partialName}",
        };

        if (kidsStart >= 0)
        {
            // The kids were skipped as a value while the dict was read; walk them now
            // from where they start, then resume after the dict.
            var resume = _pos;
            _pos = kidsStart;
            ReadFieldsArray(fullPath);
            _pos = resume;
        }
        else if (fullPath is not null)
        {
            _fields.Add((fullPath, value ?? ""));
        }
    }

    /// <summary>Read a /V value: either a string literal <c>(...)</c> or a name
    /// object <c>/Off</c> (checkbox states). Returns the decoded text; the name
    /// object's value is returned without the leading slash.</summary>
    private string? ReadValue()
    {
        SkipWS();
        if (_pos >= _t.Length) return null;
        if (_t[_pos] == '(') return ReadStringLiteral();
        if (_t[_pos] == '/')
        {
            _pos++; // step past '/'
            int s = _pos;
            while (_pos < _t.Length && !IsDelimOrWS(_t[_pos])) _pos++;
            return _t.Substring(s, _pos - s);
        }
        SkipValue();
        return null;
    }

    private void SkipWS()
    {
        while (_pos < _t.Length)
        {
            char c = _t[_pos];
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\f' || c == '\0') _pos++;
            else if (c == '%') { while (_pos < _t.Length && _t[_pos] != '\n') _pos++; }
            else break;
        }
    }

    private static bool IsDelimOrWS(char c) =>
        c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\f' || c == '\0'
        || c == '(' || c == ')' || c == '<' || c == '>' || c == '[' || c == ']'
        || c == '/' || c == '%';

    private string ReadStringLiteral()
    {
        _pos++; // step past '('
        var sb = new StringBuilder();
        int depth = 1;
        while (_pos < _t.Length && depth > 0)
        {
            char c = _t[_pos++];
            if (c == '\\')
            {
                if (_pos >= _t.Length) break;
                char esc = _t[_pos++];
                switch (esc)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case '(': sb.Append('('); break;
                    case ')': sb.Append(')'); break;
                    case '\\': sb.Append('\\'); break;
                    case '\n': break;
                    case '\r': if (_pos < _t.Length && _t[_pos] == '\n') _pos++; break;
                    case >= '0' and <= '7':
                    {
                        // Octal byte escape \d, \dd or \ddd.
                        int v = esc - '0';
                        for (var k = 0; k < 2 && _pos < _t.Length && _t[_pos] is >= '0' and <= '7'; k++)
                            v = v * 8 + (_t[_pos++] - '0');
                        sb.Append((char)(v & 0xFF));
                        break;
                    }
                    default: sb.Append(esc); break;
                }
            }
            else if (c == '(') { depth++; sb.Append(c); }
            else if (c == ')') { depth--; if (depth > 0) sb.Append(c); }
            else sb.Append(c);
        }
        // A UTF-16BE BOM marks a Unicode text string: fold the byte pairs back
        // into characters (see the class summary).
        if (sb.Length >= 2 && sb[0] == 'þ' && sb[1] == 'ÿ')
        {
            var chars = new StringBuilder((sb.Length - 2) / 2);
            for (var i = 2; i + 1 < sb.Length; i += 2)
                chars.Append((char)((sb[i] << 8) | sb[i + 1]));
            return chars.ToString();
        }
        return sb.ToString();
    }

    private void SkipValue()
    {
        SkipWS();
        if (_pos >= _t.Length) return;
        char c = _t[_pos];
        if (c == '(') { ReadStringLiteral(); }
        else if (c == '[') { SkipArray(); }
        else if (c == '<' && _pos + 1 < _t.Length && _t[_pos + 1] == '<') { SkipDict(); }
        else if (c == '<') { _pos++; while (_pos < _t.Length && _t[_pos] != '>') _pos++; if (_pos < _t.Length) _pos++; }
        else if (c == '/') { _pos++; while (_pos < _t.Length && !IsDelimOrWS(_t[_pos])) _pos++; }
        else { while (_pos < _t.Length && !IsDelimOrWS(_t[_pos])) _pos++; }
    }

    private void SkipArray()
    {
        _pos++; // step past '['
        int depth = 1;
        while (_pos < _t.Length && depth > 0)
        {
            SkipWS();
            if (_pos >= _t.Length) break;
            char c = _t[_pos];
            if (c == '[') { _pos++; depth++; }
            else if (c == ']') { _pos++; depth--; }
            else if (c == '<' && _pos + 1 < _t.Length && _t[_pos + 1] == '<') SkipDict();
            else if (c == '(') ReadStringLiteral();
            else SkipValue();
        }
    }

    private void SkipDict()
    {
        _pos += 2; // step past '<<'
        int depth = 1;
        while (_pos < _t.Length && depth > 0)
        {
            SkipWS();
            if (_pos >= _t.Length) break;
            char c = _t[_pos];
            if (_pos + 1 < _t.Length && c == '<' && _t[_pos + 1] == '<') { _pos += 2; depth++; }
            else if (_pos + 1 < _t.Length && c == '>' && _t[_pos + 1] == '>') { _pos += 2; depth--; }
            else if (c == '(') ReadStringLiteral();
            else if (c == '[') SkipArray();
            else SkipValue();
        }
    }
}

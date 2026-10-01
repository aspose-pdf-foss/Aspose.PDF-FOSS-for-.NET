using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

/// <summary>
/// Replays a page content stream the way the Layer operations
/// (Save / Flatten / Delete) rewrite it — calibrated exactly against the expected
/// content templates of the layered-form family:
/// <list type="bullet">
/// <item>The layer's own content — any op inside a <c>/OC</c> BDC block of the
/// target OCG (nested marked content included), and a <c>Do</c> of a form
/// XObject whose <c>/OC</c> is the target — is kept verbatim (Save), kept with
/// its markers dropped (Flatten), or reduced/dropped (Delete).</item>
/// <item>Skeleton reduction keeps only structure and state: q/Q/cm, colour,
/// line, text-state and text-positioning operators. Clipping paths keep their
/// construction and clip op (painting replaced by <c>n</c>); painted paths,
/// text showing, BT/ET and other layers' XObject draws drop. A bare no-op
/// <c>n</c> without construction survives.</item>
/// <item>In Save mode, after the target's LAST marked-content contribution only
/// the <c>Q</c>s that close still-open groups are emitted (no tail cut for
/// Do-style layers).</item>
/// <item>Runs of the SAME state operator that become consecutive through the
/// drops collapse to the last occurrence.</item>
/// <item>Serialization: a leading newline, one op per line, no trailing newline;
/// numbers re-format as doubles with at most 17 decimal places; paren strings
/// re-escape bytes outside 32..127 as 3-digit octal; inline dictionaries take
/// minimal canonical spacing.</item>
/// </list>
/// </summary>
internal static partial class LayerContentFilter
{
    private static readonly HashSet<string> StateOps = new(StringComparer.Ordinal)
    {
        "q", "Q", "cm", "gs", "w", "J", "j", "M", "d", "ri", "i",
        "rg", "RG", "g", "G", "k", "K", "cs", "CS", "sc", "scn", "SC", "SCN",
        "Tc", "Tw", "Tz", "TL", "Tf", "Tr", "Ts", "Tm", "Td", "TD", "T*",
    };

    private static readonly HashSet<string> PathConstructionOps = new(StringComparer.Ordinal)
        { "m", "l", "c", "v", "y", "re", "h" };

    private static readonly HashSet<string> ClipOps = new(StringComparer.Ordinal) { "W", "W*" };

    private static readonly HashSet<string> PaintOps = new(StringComparer.Ordinal)
        { "S", "s", "f", "F", "f*", "B", "B*", "b", "b*", "n" };

    /// <summary>State ops whose consecutive runs collapse to the last occurrence.</summary>
    private static readonly HashSet<string> CollapseOps = new(StringComparer.Ordinal)
    {
        "w", "J", "j", "M", "d", "ri", "i", "gs",
        "Tc", "Tw", "Tz", "TL", "Tf", "Tr", "Ts", "Td", "TD", "Tm",
        "rg", "RG", "g", "G", "k", "K", "cs", "CS", "sc", "scn", "SC", "SCN",
    };

    /// <summary>Filter <paramref name="content"/> for the layer named
    /// <paramref name="layerId"/>. <paramref name="layerXObjects"/> lists the
    /// page XObject resource names whose stream carries the layer's /OC.
    /// In Save mode returns null when the page carries no contribution at all.</summary>
    public static byte[]? Filter(byte[] content, string layerId, IReadOnlyCollection<string> layerXObjects, LayerFilterMode mode = LayerFilterMode.Save)
    {
        var lf = new LayerFilterState();
        lf.content = content;
        lf.layerId = layerId;
        lf.layerXObjects = layerXObjects;
        lf.mode = mode;
        lf.lines = ContentStreamOperatorParser.ParseOperators(lf.content);
        lf.ops = new List<(string Operands, string Op)>(lf.lines.Count);
        foreach (var line in lf.lines)
        {
            var idx = line.LastIndexOf(' ');
            lf.ops.Add(idx < 0 ? (string.Empty, line) : (line[..idx], line[(idx + 1)..]));
        }

        lf.lastTarget = -1;
        lf.hasDoContribution = false;
        lf.ocStack = new List<bool?>();
        ScanLayerTargets(lf);

        if (lf.mode == LayerFilterMode.Save && lf.lastTarget < 0 && !lf.hasDoContribution)
            return null; // the page content carries nothing of this layer

        lf.tailStart = lf.mode != LayerFilterMode.Save || lf.lastTarget < 0 || lf.hasDoContribution
            ? lf.ops.Count
            : lf.lastTarget + 1;

        lf.output = new List<(string Operands, string Op)>();
        lf.ocStack.Clear();
        lf.pathBuffer = new List<(string Operands, string Op)>();
        lf.tailDepth = 0;
        for (var i = 0; i < lf.ops.Count; i++)
        {
            if (!FilterLayerOperator(lf, i)) break;
        }

        lf.sb = new StringBuilder();
        foreach (var (operands, op) in lf.output)
        {
            lf.sb.Append('\n');
            if (operands.Length > 0)
            {
                lf.sb.Append(SerializeOperands(operands));
                lf.sb.Append(' ');
            }
            lf.sb.Append(op);
        }
        return Compat.Latin1.GetBytes(lf.sb.ToString());
    }

    private static bool InTarget(List<bool?> ocStack)
    {
        foreach (var e in ocStack)
            if (e == true) return true;
        return false;
    }

    private static bool IsTargetOcOperand(string operands, string layerId)
    {
        // "/OC /oc1" — direct named form. Anything else (property dicts) is
        // treated as a foreign block.
        var parts = operands.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && parts[0] == "/OC" && parts[1] == "/" + layerId;
    }

    private static bool IsTargetDo(string operands, IReadOnlyCollection<string> layerXObjects)
    {
        if (layerXObjects.Count == 0) return false;
        var name = operands.Trim();
        if (!name.StartsWith('/')) return false;
        return layerXObjects.Contains(name[1..]);
    }

    /// <summary>Re-serialize an operand run: numbers re-format as doubles with at
    /// most 17 decimal places, paren strings re-escape non-ASCII bytes as octal,
    /// inline dictionaries take canonical minimal spacing; names / arrays / hex
    /// strings pass through verbatim.</summary>
    private static string SerializeOperands(string operands)
    {
        var sb = new StringBuilder(operands.Length);
        var i = 0;
        var first = true;
        while (i < operands.Length)
        {
            var c = operands[i];
            if (c == ' ') { i++; continue; }
            if (!first) sb.Append(' ');
            first = false;
            if (c == '(')
            {
                (var str, i) = ScanParenString(operands, i);
                sb.Append(ReescapeString(str));
            }
            else if (c == '<' && i + 1 < operands.Length && operands[i + 1] == '<')
            {
                (var dict, i) = ScanBalanced(operands, i, "<<", ">>");
                sb.Append(CanonicalizeDict(dict));
            }
            else if (c == '<')
            {
                var end = operands.IndexOf('>', i);
                if (end < 0) end = operands.Length - 1;
                sb.Append(operands[i..(end + 1)]);
                i = end + 1;
            }
            else if (c == '[')
            {
                (var arr, i) = ScanArray(operands, i);
                sb.Append(arr);
            }
            else if (c is '-' or '+' or '.' || Compat.IsAsciiDigit(c))
            {
                var start = i;
                i++;
                while (i < operands.Length && (Compat.IsAsciiDigit(operands[i]) || operands[i] == '.')) i++;
                sb.Append(FormatNumber(operands[start..i]));
            }
            else
            {
                // name or keyword: read to next whitespace/delimiter
                var start = i;
                i++;
                while (i < operands.Length && operands[i] is not (' ' or '(' or '<' or '['))
                    i++;
                sb.Append(operands[start..i]);
            }
        }
        return sb.ToString();
    }

    /// <summary>Numbers print in the expected writer shape: parsed to double and
    /// formatted with up to 17 decimal places (invariant culture).</summary>
    private static string FormatNumber(string token)
    {
        if (double.TryParse(token, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
            return v.ToString("0.#################", System.Globalization.CultureInfo.InvariantCulture);
        return token;
    }

    /// <summary>Rewrite an inline dictionary with minimal spacing — a space only
    /// between two tokens neither of which is self-delimiting
    /// (<c>&lt;&lt;/Lang(en-US)/MCID 0&gt;&gt;</c>).</summary>
    private static string CanonicalizeDict(string dict)
    {
        if (dict.Length < 4) return dict;
        var body = dict[2..^2];
        var sb = new StringBuilder(body.Length + 4);
        sb.Append("<<");
        var i = 0;
        var prevSelfDelimited = true;
        while (i < body.Length)
        {
            var c = body[i];
            if (c is ' ' or '\t' or '\r' or '\n') { i++; continue; }
            string tok;
            if (c == '(') (tok, i) = ScanParenString(body, i);
            else if (c == '<' && i + 1 < body.Length && body[i + 1] == '<')
            {
                (tok, i) = ScanBalanced(body, i, "<<", ">>");
                tok = CanonicalizeDict(tok);
            }
            else if (c == '<')
            {
                var end = body.IndexOf('>', i);
                if (end < 0) end = body.Length - 1;
                tok = body[i..(end + 1)];
                i = end + 1;
            }
            else if (c == '[') (tok, i) = ScanArray(body, i);
            else if (c == '/')
            {
                var start = i;
                i++;
                while (i < body.Length && body[i] is not (' ' or '\t' or '\r' or '\n'
                       or '(' or '<' or '[' or '/' or '>' or ')' or ']'))
                    i++;
                tok = body[start..i];
            }
            else
            {
                var start = i;
                i++;
                while (i < body.Length && body[i] is not (' ' or '\t' or '\r' or '\n'
                       or '(' or '<' or '[' or '/' or '>' or ')' or ']'))
                    i++;
                tok = body[start..i];
            }
            var selfStarting = tok.Length > 0 && tok[0] is '/' or '(' or '<' or '[';
            if (!selfStarting && !prevSelfDelimited)
                sb.Append(' ');
            else if (!selfStarting && sb.Length > 2 && sb[^1] is not ('>' or ')' or ']'))
                sb.Append(' '); // a number after a bare name ("/MCID 0")
            sb.Append(tok);
            prevSelfDelimited = tok.Length > 0 && tok[^1] is ')' or '>' or ']';
        }
        sb.Append(">>");
        return sb.ToString();
    }

    /// <returns>The literal starting at <paramref name="i"/> and the index just past it.</returns>
    private static (string token, int end) ScanParenString(string s, int i)
    {
        var start = i;
        var depth = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '\\') { i += 2; continue; }
            if (c == '(') depth++;
            else if (c == ')')
            {
                depth--;
                if (depth == 0) { i++; break; }
            }
            i++;
        }
        return (s[start..Math.Min(i, s.Length)], i);
    }

    /// <returns>The balanced span starting at <paramref name="i"/> and the index just past it.</returns>
    private static (string token, int end) ScanBalanced(string s, int i, string open, string close)
    {
        var start = i;
        var depth = 0;
        while (i < s.Length)
        {
            if (i + open.Length <= s.Length && s.AsSpan(i, open.Length).SequenceEqual(open))
            { depth++; i += open.Length; continue; }
            if (i + close.Length <= s.Length && s.AsSpan(i, close.Length).SequenceEqual(close))
            {
                depth--; i += close.Length;
                if (depth == 0) break;
                continue;
            }
            i++;
        }
        return (s[start..Math.Min(i, s.Length)], i);
    }

    /// <returns>The array starting at <paramref name="i"/> and the index just past it.</returns>
    private static (string token, int end) ScanArray(string s, int i)
    {
        var start = i;
        var depth = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '\\') { i += 2; continue; }
            if (c == '(')
            {
                (_, i) = ScanParenString(s, i);
                continue;
            }
            if (c == '[') depth++;
            else if (c == ']')
            {
                depth--;
                if (depth == 0) { i++; break; }
            }
            i++;
        }
        return (s[start..Math.Min(i, s.Length)], i);
    }

    /// <summary>Decode a raw paren-string literal and re-escape it the way the
    /// reference writer does: bytes 32..127 literal (with \\, \(, \) escaped),
    /// everything else as a 3-digit octal escape.</summary>
    private static string ReescapeString(string literal)
    {
        if (literal.Length < 2) return literal;
        var body = literal[1..^1];
        var sb = new StringBuilder(body.Length + 8);
        sb.Append('(');
        var i = 0;
        while (i < body.Length)
        {
            var c = body[i];
            if (c == '\\' && i + 1 < body.Length)
            {
                var n = body[i + 1];
                if (n is >= '0' and <= '7')
                {
                    var j = i + 1;
                    var code = 0;
                    while (j < body.Length && j < i + 4 && body[j] is >= '0' and <= '7')
                    {
                        code = code * 8 + (body[j] - '0');
                        j++;
                    }
                    AppendEscaped(sb, (char)(code & 0xFF));
                    i = j;
                    continue;
                }
                var mapped = n switch
                {
                    'n' => '\n', 'r' => '\r', 't' => '\t', 'b' => '\b', 'f' => '\f',
                    _ => n,
                };
                AppendEscaped(sb, mapped);
                i += 2;
                continue;
            }
            AppendEscaped(sb, c);
            i++;
        }
        sb.Append(')');
        return sb.ToString();
    }

    private static void AppendEscaped(StringBuilder sb, char c)
    {
        var o = (int)c & 0xFF;
        if (o is >= 32 and <= 127)
        {
            if (c is '\\' or '(' or ')') sb.Append('\\');
            sb.Append((char)o);
            return;
        }
        sb.Append('\\');
        sb.Append((char)('0' + ((o >> 6) & 7)));
        sb.Append((char)('0' + ((o >> 3) & 7)));
        sb.Append((char)('0' + (o & 7)));
    }
}

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
internal static class LayerContentFilter
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
    public static byte[]? Filter(
        byte[] content, string layerId, IReadOnlyCollection<string> layerXObjects,
        LayerFilterMode mode = LayerFilterMode.Save)
    {
        var lines = ContentStreamOperatorParser.ParseOperators(content);
        var ops = new List<(string Operands, string Op)>(lines.Count);
        foreach (var line in lines)
        {
            var idx = line.LastIndexOf(' ');
            ops.Add(idx < 0 ? (string.Empty, line) : (line[..idx], line[(idx + 1)..]));
        }

        // First pass (Save only): index of the LAST op contributed via a target BDC block.
        var lastTarget = -1;
        var hasDoContribution = false;
        var ocStack = new List<bool?>();
        for (var i = 0; i < ops.Count; i++)
        {
            var (operands, op) = ops[i];
            if (op is "BDC" or "BMC")
            {
                ocStack.Add(op == "BDC" ? IsTargetOcOperand(operands, layerId) : null);
                continue;
            }
            if (op == "EMC")
            {
                if (InTarget(ocStack)) lastTarget = i;
                if (ocStack.Count > 0) ocStack.RemoveAt(ocStack.Count - 1);
                continue;
            }
            if (InTarget(ocStack)) lastTarget = i;
            if (op == "Do" && IsTargetDo(operands, layerXObjects)) hasDoContribution = true;
        }

        if (mode == LayerFilterMode.Save && lastTarget < 0 && !hasDoContribution)
            return null; // the page content carries nothing of this layer

        // A Do-style layer keeps its full trailing context (no tail cut);
        // Flatten/Delete never cut the tail.
        var tailStart = mode != LayerFilterMode.Save || lastTarget < 0 || hasDoContribution
            ? ops.Count
            : lastTarget + 1;

        var output = new List<(string Operands, string Op)>();
        void Emit(string operands, string op)
        {
            if (CollapseOps.Contains(op) && output.Count > 0 && output[^1].Op == op)
            {
                output[^1] = (operands, op);
                return;
            }
            output.Add((operands, op));
        }

        ocStack.Clear();
        var pathBuffer = new List<(string Operands, string Op)>();
        var tailDepth = 0;
        for (var i = 0; i < ops.Count; i++)
        {
            var (operands, op) = ops[i];
            if (op is "BDC" or "BMC")
            {
                var isTarget = op == "BDC" && IsTargetOcOperand(operands, layerId);
                var insideTarget = InTarget(ocStack);
                ocStack.Add(op == "BDC" ? isTarget : null);
                // Flatten/Delete keep other layers' markers when they sit OUTSIDE
                // the target's blocks; Save drops every marker.
                if (mode != LayerFilterMode.Save && !isTarget && !insideTarget && i < tailStart)
                    Emit(operands, op);
                continue;
            }
            if (op == "EMC")
            {
                bool? wasTarget = null;
                if (ocStack.Count > 0)
                {
                    wasTarget = ocStack[^1];
                    ocStack.RemoveAt(ocStack.Count - 1);
                }
                if (mode != LayerFilterMode.Save && wasTarget != true && !InTarget(ocStack) && i < tailStart)
                    Emit(string.Empty, "EMC");
                continue;
            }
            if (i >= tailStart)
            {
                // Save mode, after the target's last block: only the Qs that
                // close groups opened before the tail survive.
                if (op == "q") tailDepth++;
                else if (op == "Q")
                {
                    if (tailDepth > 0) tailDepth--;
                    else Emit(string.Empty, "Q");
                }
                continue;
            }
            var inTargetBlock = InTarget(ocStack);
            var reduceToSkeleton = mode switch
            {
                LayerFilterMode.Save => !inTargetBlock,
                LayerFilterMode.Delete => inTargetBlock,
                _ => false, // Flatten keeps everything verbatim
            };
            if (!reduceToSkeleton)
            {
                if (mode == LayerFilterMode.Delete && op == "Do" && IsTargetDo(operands, layerXObjects))
                    continue; // Delete drops the layer's own XObject draw
                Emit(operands, op);
                continue;
            }
            // Skeleton region.
            if (PathConstructionOps.Contains(op) || ClipOps.Contains(op))
            {
                pathBuffer.Add((operands, op));
                continue;
            }
            if (PaintOps.Contains(op))
            {
                var hadClip = false;
                foreach (var b in pathBuffer)
                    if (ClipOps.Contains(b.Op)) { hadClip = true; break; }
                if (hadClip)
                {
                    foreach (var b in pathBuffer) Emit(b.Operands, b.Op);
                    Emit(string.Empty, "n");
                }
                else if (op == "n" && pathBuffer.Count == 0)
                {
                    Emit(string.Empty, "n"); // a bare no-op n survives
                }
                pathBuffer.Clear();
                continue;
            }
            if (op == "Do" && mode == LayerFilterMode.Save && IsTargetDo(operands, layerXObjects))
            {
                Emit(operands, op);
                continue;
            }
            if (StateOps.Contains(op))
                Emit(operands, op);
            // everything else (text showing, BT/ET, foreign Do, sh, inline images) drops
        }

        var sb = new StringBuilder();
        foreach (var (operands, op) in output)
        {
            sb.Append('\n');
            if (operands.Length > 0)
            {
                sb.Append(SerializeOperands(operands));
                sb.Append(' ');
            }
            sb.Append(op);
        }
        return Encoding.Latin1.GetBytes(sb.ToString());
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
                var str = ScanParenString(operands, ref i);
                sb.Append(ReescapeString(str));
            }
            else if (c == '<' && i + 1 < operands.Length && operands[i + 1] == '<')
            {
                sb.Append(CanonicalizeDict(ScanBalanced(operands, ref i, "<<", ">>")));
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
                sb.Append(ScanArray(operands, ref i));
            }
            else if (c is '-' or '+' or '.' || char.IsAsciiDigit(c))
            {
                var start = i;
                i++;
                while (i < operands.Length && (char.IsAsciiDigit(operands[i]) || operands[i] == '.')) i++;
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
            if (c == '(') tok = ScanParenString(body, ref i);
            else if (c == '<' && i + 1 < body.Length && body[i + 1] == '<')
                tok = CanonicalizeDict(ScanBalanced(body, ref i, "<<", ">>"));
            else if (c == '<')
            {
                var end = body.IndexOf('>', i);
                if (end < 0) end = body.Length - 1;
                tok = body[i..(end + 1)];
                i = end + 1;
            }
            else if (c == '[') tok = ScanArray(body, ref i);
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

    private static string ScanParenString(string s, ref int i)
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
        return s[start..Math.Min(i, s.Length)];
    }

    private static string ScanBalanced(string s, ref int i, string open, string close)
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
        return s[start..Math.Min(i, s.Length)];
    }

    private static string ScanArray(string s, ref int i)
    {
        var start = i;
        var depth = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '\\') { i += 2; continue; }
            if (c == '(')
            {
                ScanParenString(s, ref i);
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
        return s[start..Math.Min(i, s.Length)];
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

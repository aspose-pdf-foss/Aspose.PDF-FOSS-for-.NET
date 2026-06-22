using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Functions;

/// <summary>
/// PostScript calculator evaluator for Type 4 PDF functions (PDF32000 §7.10.5).
/// Supports the subset of PostScript Level 2 operators used in PDF functions.
/// </summary>
public static class PostScriptEvaluator
{
    /// <summary>
    /// Evaluate a PostScript program string with the given input values on the stack.
    /// Returns the resulting stack values.
    /// </summary>
    public static double[] Evaluate(string program, double[] inputs)
    {
        var tokens = Tokenize(program);
        var proc = BuildProc(tokens);
        var stack = new List<object>();
        foreach (var v in inputs) stack.Add(v);
        Eval(proc, stack);
        return stack.Select(v => v is double d ? d : v is bool b ? (b ? 1.0 : 0.0) : Convert.ToDouble(v)).ToArray();
    }

    /// <summary>
    /// Evaluate a PostScript program string with no inputs.
    /// </summary>
    public static double[] Evaluate(string program) => Evaluate(program, Array.Empty<double>());

    private static List<object> Tokenize(string src)
    {
        var tokens = new List<object>();
        var i = 0;
        while (i < src.Length)
        {
            while (i < src.Length && char.IsWhiteSpace(src[i])) i++;
            if (i >= src.Length) break;
            var ch = src[i];
            if (ch == '{' || ch == '}') { tokens.Add(ch.ToString()); i++; continue; }
            if (ch == '%') { while (i < src.Length && src[i] != '\n') i++; continue; }
            var start = i;
            while (i < src.Length && !char.IsWhiteSpace(src[i]) && src[i] != '{' && src[i] != '}' && src[i] != '%') i++;
            var tok = src[start..i];
            if (tok.Length == 0) continue;
            if (tok == "true") { tokens.Add(true); continue; }
            if (tok == "false") { tokens.Add(false); continue; }
            if (double.TryParse(tok, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var num))
            { tokens.Add(num); continue; }
            tokens.Add(tok);
        }
        return tokens;
    }

    private static List<object> BuildProc(List<object> tokens)
    {
        var i = 0;

        List<object> ParseProc()
        {
            var proc = new List<object>();
            while (i < tokens.Count)
            {
                var tok = tokens[i++];
                if (tok is string s && s == "{") { proc.Add(ParseProc()); }
                else if (tok is string s2 && s2 == "}") { return proc; }
                else { proc.Add(tok); }
            }
            return proc;
        }

        // Skip to the first '{'
        while (i < tokens.Count && !(tokens[i] is string s && s == "{")) i++;
        if (i < tokens.Count) { i++; return ParseProc(); }
        return new List<object>();
    }

    private static void Eval(List<object> proc, List<object> stack)
    {
        object Pop()
        {
            if (stack.Count == 0) throw new InvalidOperationException("PS stack underflow");
            var v = stack[^1];
            stack.RemoveAt(stack.Count - 1);
            return v;
        }

        double PopN()
        {
            var v = Pop();
            if (v is double d) return d;
            if (v is int n) return n;
            if (v is bool b) return b ? 1.0 : 0.0;
            return Convert.ToDouble(v);
        }

        foreach (var instr in proc)
        {
            if (instr is double || instr is bool || instr is List<object>)
            {
                stack.Add(instr);
                continue;
            }

            if (instr is not string op) continue;

            switch (op)
            {
                // Arithmetic
                case "add": { var b = PopN(); var a = PopN(); stack.Add(a + b); break; }
                case "sub": { var b = PopN(); var a = PopN(); stack.Add(a - b); break; }
                case "mul": { var b = PopN(); var a = PopN(); stack.Add(a * b); break; }
                case "div": { var b = PopN(); var a = PopN(); stack.Add(a / b); break; }
                case "idiv": { var b = PopN(); var a = PopN(); stack.Add((double)Math.Truncate(a / b)); break; }
                case "mod": { var b = PopN(); var a = PopN(); stack.Add(a - Math.Truncate(a / b) * b); break; }
                case "abs": stack.Add(Math.Abs(PopN())); break;
                case "neg": stack.Add(-PopN()); break;
                case "ceiling": stack.Add(Math.Ceiling(PopN())); break;
                case "floor": stack.Add(Math.Floor(PopN())); break;
                case "round": { var v = PopN(); stack.Add(Math.Floor(v + 0.5)); break; }
                case "truncate": stack.Add((double)Math.Truncate(PopN())); break;
                case "sqrt": stack.Add(Math.Sqrt(PopN())); break;
                case "exp": { var e = PopN(); var b2 = PopN(); stack.Add(Math.Pow(b2, e)); break; }
                case "ln": stack.Add(Math.Log(PopN())); break;
                case "log": stack.Add(Math.Log10(PopN())); break;
                case "sin": stack.Add(Math.Sin(PopN() * Math.PI / 180)); break;
                case "cos": stack.Add(Math.Cos(PopN() * Math.PI / 180)); break;
                case "atan": { var den = PopN(); var num = PopN(); stack.Add(Math.Atan2(num, den) * 180 / Math.PI); break; }

                // Relational
                case "eq": { var b = Pop(); var a = Pop(); stack.Add(Equals(a, b)); break; }
                case "ne": { var b = Pop(); var a = Pop(); stack.Add(!Equals(a, b)); break; }
                case "gt": { var b = PopN(); var a = PopN(); stack.Add(a > b); break; }
                case "ge": { var b = PopN(); var a = PopN(); stack.Add(a >= b); break; }
                case "lt": { var b = PopN(); var a = PopN(); stack.Add(a < b); break; }
                case "le": { var b = PopN(); var a = PopN(); stack.Add(a <= b); break; }

                // Boolean / bitwise
                case "and":
                {
                    var b = Pop(); var a = Pop();
                    if (a is bool ab && b is bool bb) stack.Add(ab && bb);
                    else stack.Add((double)((int)Convert.ToDouble(a) & (int)Convert.ToDouble(b)));
                    break;
                }
                case "or":
                {
                    var b = Pop(); var a = Pop();
                    if (a is bool ab && b is bool bb) stack.Add(ab || bb);
                    else stack.Add((double)((int)Convert.ToDouble(a) | (int)Convert.ToDouble(b)));
                    break;
                }
                case "not":
                {
                    var v = Pop();
                    if (v is bool bv) stack.Add(!bv);
                    else stack.Add((double)(~(int)Convert.ToDouble(v)));
                    break;
                }
                case "xor":
                {
                    var b = Pop(); var a = Pop();
                    if (a is bool ab && b is bool bb) stack.Add(ab != bb);
                    else stack.Add((double)((int)Convert.ToDouble(a) ^ (int)Convert.ToDouble(b)));
                    break;
                }
                case "bitshift":
                {
                    var n = (int)PopN(); var v = (int)PopN();
                    stack.Add((double)(n >= 0 ? v << n : v >> -n));
                    break;
                }

                // Stack
                case "pop": Pop(); break;
                case "exch": { var b = Pop(); var a = Pop(); stack.Add(b); stack.Add(a); break; }
                case "dup": { var v = Pop(); stack.Add(v); stack.Add(v); break; }
                case "copy":
                {
                    var n = (int)PopN();
                    var items = stack.Skip(stack.Count - n).ToList();
                    stack.AddRange(items);
                    break;
                }
                case "index":
                {
                    var n = (int)PopN();
                    var v = stack[stack.Count - 1 - n];
                    stack.Add(v);
                    break;
                }
                case "roll":
                {
                    var j = (int)PopN();
                    var n = (int)PopN();
                    if (n > 0)
                    {
                        var slice = stack.GetRange(stack.Count - n, n);
                        stack.RemoveRange(stack.Count - n, n);
                        var r = ((j % n) + n) % n;
                        var rotated = slice.Skip(n - r).Concat(slice.Take(n - r)).ToList();
                        stack.AddRange(rotated);
                    }
                    break;
                }

                // Type conversion
                case "cvi": stack.Add((double)Math.Truncate(PopN())); break;
                case "cvr": stack.Add(PopN()); break;

                // Literals
                case "true": stack.Add(true); break;
                case "false": stack.Add(false); break;

                // Control flow
                case "if":
                {
                    var thenProc = Pop() as List<object>;
                    var cond = Pop();
                    var isTruthy = cond is bool cb ? cb : Convert.ToDouble(cond) != 0;
                    if (isTruthy && thenProc != null) Eval(thenProc, stack);
                    break;
                }
                case "ifelse":
                {
                    var elseProc = Pop() as List<object>;
                    var thenProc = Pop() as List<object>;
                    var cond = Pop();
                    var isTruthy = cond is bool cb ? cb : Convert.ToDouble(cond) != 0;
                    if (isTruthy && thenProc != null) Eval(thenProc, stack);
                    else if (!isTruthy && elseProc != null) Eval(elseProc, stack);
                    break;
                }
            }
        }
    }

    private static new bool Equals(object? a, object? b)
    {
        if (a is double da && b is double db) return da == db;
        if (a is bool ba && b is bool bb) return ba == bb;
        return object.Equals(a, b);
    }
}

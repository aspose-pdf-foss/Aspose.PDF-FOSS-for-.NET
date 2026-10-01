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

    private static object Pop(List<object> stack)
    {
        if (stack.Count == 0) throw new InvalidOperationException("PS stack underflow");
        var v = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        return v;
    }

    private static double PopN(List<object> stack)
    {
        var v = Pop(stack);
        if (v is double d) return d;
        if (v is int n) return n;
        if (v is bool b) return b ? 1.0 : 0.0;
        return Convert.ToDouble(v);
    }

    private static void Eval(List<object> proc, List<object> stack)
    {
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
                case "add": case "sub": case "mul": case "div": case "idiv": case "mod": case "abs": case "neg": case "ceiling": case "floor": case "round": case "truncate": case "sqrt": case "exp": case "ln": case "log": case "sin": case "cos": case "atan":
                    EvalArithmetic(stack, op);
                    break;
                case "eq": case "ne": case "gt": case "ge": case "lt": case "le": case "and": case "or": case "not": case "xor": case "bitshift": case "true": case "false":
                    EvalLogic(stack, op);
                    break;
                case "pop": case "exch": case "dup": case "copy": case "index": case "roll": case "cvi": case "cvr":
                    EvalStack(stack, op);
                    break;
                // Control flow
                case "if":
                {
                    var thenProc = Pop(stack) as List<object>;
                    var cond = Pop(stack);
                    var isTruthy = cond is bool cb ? cb : Convert.ToDouble(cond) != 0;
                    if (isTruthy && thenProc != null) Eval(thenProc, stack);
                    break;
                }
                case "ifelse":
                {
                    var elseProc = Pop(stack) as List<object>;
                    var thenProc = Pop(stack) as List<object>;
                    var cond = Pop(stack);
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

    /// <summary>The arithmetic operators of a PostScript calculator function.</summary>
    private static void EvalArithmetic(List<object> stack, string op)
    {
        switch (op)
        {
            // Arithmetic
            case "add": { var b = PopN(stack); var a = PopN(stack); stack.Add(a + b); break; }
            case "sub": { var b = PopN(stack); var a = PopN(stack); stack.Add(a - b); break; }
            case "mul": { var b = PopN(stack); var a = PopN(stack); stack.Add(a * b); break; }
            case "div": { var b = PopN(stack); var a = PopN(stack); stack.Add(a / b); break; }
            case "idiv": { var b = PopN(stack); var a = PopN(stack); stack.Add((double)Math.Truncate(a / b)); break; }
            case "mod": { var b = PopN(stack); var a = PopN(stack); stack.Add(a - Math.Truncate(a / b) * b); break; }
            case "abs": stack.Add(Math.Abs(PopN(stack))); break;
            case "neg": stack.Add(-PopN(stack)); break;
            case "ceiling": stack.Add(Math.Ceiling(PopN(stack))); break;
            case "floor": stack.Add(Math.Floor(PopN(stack))); break;
            case "round": { var v = PopN(stack); stack.Add(Math.Floor(v + 0.5)); break; }
            case "truncate": stack.Add((double)Math.Truncate(PopN(stack))); break;
            case "sqrt": stack.Add(Math.Sqrt(PopN(stack))); break;
            case "exp": { var e = PopN(stack); var b2 = PopN(stack); stack.Add(Math.Pow(b2, e)); break; }
            case "ln": stack.Add(Math.Log(PopN(stack))); break;
            case "log": stack.Add(Math.Log10(PopN(stack))); break;
            case "sin": stack.Add(Math.Sin(PopN(stack) * Math.PI / 180)); break;
            case "cos": stack.Add(Math.Cos(PopN(stack) * Math.PI / 180)); break;
            case "atan": { var den = PopN(stack); var num = PopN(stack); stack.Add(Math.Atan2(num, den) * 180 / Math.PI); break; }

        }
    }

    /// <summary>The comparison, boolean and bitwise operators.</summary>
    private static void EvalLogic(List<object> stack, string op)
    {
        switch (op)
        {
            // Relational
            case "eq": { var b = Pop(stack); var a = Pop(stack); stack.Add(Equals(a, b)); break; }
            case "ne": { var b = Pop(stack); var a = Pop(stack); stack.Add(!Equals(a, b)); break; }
            case "gt": { var b = PopN(stack); var a = PopN(stack); stack.Add(a > b); break; }
            case "ge": { var b = PopN(stack); var a = PopN(stack); stack.Add(a >= b); break; }
            case "lt": { var b = PopN(stack); var a = PopN(stack); stack.Add(a < b); break; }
            case "le": { var b = PopN(stack); var a = PopN(stack); stack.Add(a <= b); break; }

            // Boolean / bitwise
            case "and":
            {
                var b = Pop(stack); var a = Pop(stack);
                if (a is bool ab && b is bool bb) stack.Add(ab && bb);
                else stack.Add((double)((int)Convert.ToDouble(a) & (int)Convert.ToDouble(b)));
                break;
            }
            case "or":
            {
                var b = Pop(stack); var a = Pop(stack);
                if (a is bool ab && b is bool bb) stack.Add(ab || bb);
                else stack.Add((double)((int)Convert.ToDouble(a) | (int)Convert.ToDouble(b)));
                break;
            }
            case "not":
            {
                var v = Pop(stack);
                if (v is bool bv) stack.Add(!bv);
                else stack.Add((double)(~(int)Convert.ToDouble(v)));
                break;
            }
            case "xor":
            {
                var b = Pop(stack); var a = Pop(stack);
                if (a is bool ab && b is bool bb) stack.Add(ab != bb);
                else stack.Add((double)((int)Convert.ToDouble(a) ^ (int)Convert.ToDouble(b)));
                break;
            }
            case "bitshift":
            {
                var n = (int)PopN(stack); var v = (int)PopN(stack);
                stack.Add((double)(n >= 0 ? v << n : v >> -n));
                break;
            }

            // Literals
            case "true": stack.Add(true); break;
            case "false": stack.Add(false); break;

        }
    }

    /// <summary>The stack and conversion operators.</summary>
    private static void EvalStack(List<object> stack, string op)
    {
        switch (op)
        {
            // Stack
            case "pop": Pop(stack); break;
            case "exch": { var b = Pop(stack); var a = Pop(stack); stack.Add(b); stack.Add(a); break; }
            case "dup": { var v = Pop(stack); stack.Add(v); stack.Add(v); break; }
            case "copy":
            {
                var n = (int)PopN(stack);
                var items = stack.Skip(stack.Count - n).ToList();
                stack.AddRange(items);
                break;
            }
            case "index":
            {
                var n = (int)PopN(stack);
                var v = stack[stack.Count - 1 - n];
                stack.Add(v);
                break;
            }
            case "roll":
            {
                var j = (int)PopN(stack);
                var n = (int)PopN(stack);
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
            case "cvi": stack.Add((double)Math.Truncate(PopN(stack))); break;
            case "cvr": stack.Add(PopN(stack)); break;

        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

/// <summary>
/// Minimal Acrobat-JS calculation engine for form-field <c>/AA/C</c> Calculate
/// actions. Like <see cref="FieldFormatScript"/> it does not run a full JS engine;
/// it pattern-matches the two shapes Acrobat emits from the field-properties UI:
///   • <c>AFSimple_Calculate("SUM"|"AVG"|"PRD"|"MIN"|"MAX", new Array("F1","F2",…))</c>
///   • <c>event.value = &lt;arithmetic over AFMakeNumber(getField("X").value) and numbers&gt;</c>
/// and evaluates them from the current values of the referenced fields. This lets a
/// calculated field report its recomputed value on read (Acrobat's auto-calculate).
/// </summary>
internal static class FieldCalculateScript
{
    /// <summary>Return the recomputed value string for a field carrying a recognised
    /// /AA/C calculate action, or null when the field has no calculate action or the
    /// script isn't a supported built-in.</summary>
    internal static string? ComputeValue(PdfDictionary fieldDict, PdfReader reader)
    {
        var script = ExtractCalculateScript(fieldDict, reader);
        if (script is null) return null;
        if (TryEvaluate(script, name => ResolveNumber(name, reader, new HashSet<string>())) is not { } result)
            return null;
        return FormatNumber(result);
    }

    private static string? ExtractCalculateScript(PdfDictionary fieldDict, PdfReader reader)
    {
        var aa = reader.ResolveDict(fieldDict.Get("AA"));
        if (aa is null) return null;
        var c = reader.ResolveDict(aa.Get("C"));
        if (c is null) return null;
        var js = reader.Resolve(c.Get("JS"));
        return js switch
        {
            PdfString s => s.ToText(),
            PdfStream stream => Compat.Latin1.GetString(reader.DecodeStream(stream)),
            _ => null,
        };
    }

    /// <summary>Evaluate a calculate script given a numeric field-value resolver.</summary>
    internal static double? TryEvaluate(string script, Func<string, double> getNum)
    {
        double result = 0;
        if (string.IsNullOrEmpty(script)) return null;

        // AFSimple_Calculate("OP", new Array("a","b",…))  or  ("OP", ["a","b"])
        var simple = Regex.Match(script,
            @"AFSimple_Calculate\s*\(\s*""(\w+)""\s*,\s*(?:new\s+Array\s*)?[\(\[]([^)\]]*)[)\]]",
            RegexOptions.IgnoreCase);
        if (simple.Success)
        {
            var op = simple.Groups[1].Value.ToUpperInvariant();
            var names = Regex.Matches(simple.Groups[2].Value, @"""([^""]*)""")
                             .Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            var vals = names.Select(getNum).ToList();
            result = Aggregate(op, vals);
            return result;
        }

        // Variable declarations: var X = AFMakeNumber(this.getField("F").value)
        // (or the bare getField(...).value form). Build a name→value map.
        var vars = new Dictionary<string, double>();
        foreach (Match vm in Regex.Matches(script,
            @"var\s+(\w+)\s*=\s*(?:AFMakeNumber\s*\(\s*)?(?:this\.)?getField\s*\(\s*""([^""]*)""\s*\)\s*\.\s*value"))
            vars[vm.Groups[1].Value] = getNum(vm.Groups[2].Value);

        // Field-OBJECT declarations: var X = this.getField("F");  (no .value) — the
        // expression then reads X.value. Bind X.value up front so the substitution
        // pass below sees a plain variable.
        foreach (Match vm in Regex.Matches(script,
            @"var\s+(\w+)\s*=\s*(?:this\.)?getField\s*\(\s*""([^""]*)""\s*\)\s*;"))
            if (!vars.ContainsKey(vm.Groups[1].Value + ".value"))
                vars[vm.Groups[1].Value + ".value"] = getNum(vm.Groups[2].Value);

        // event.value = <expression>. There may be several (e.g. an if/else that
        // clears the field); take the first non-empty-string assignment that
        // evaluates to a number.
        foreach (Match ev in Regex.Matches(script, @"event\.value\s*=\s*([^;\r\n}]+)"))
        {
            var expr = ev.Groups[1].Value.Trim();
            if (expr.Length == 0 || expr == "\"\"" || expr == "''") continue;
            if (TryEvalExpression(expr, getNum, vars) is { } value) return value;
        }

        return null;
    }

    private static double Aggregate(string op, List<double> vals)
    {
        if (vals.Count == 0) return 0;
        return op switch
        {
            "SUM" => vals.Sum(),
            "AVG" => vals.Average(),
            "PRD" => vals.Aggregate(1.0, (a, b) => a * b),
            "MIN" => vals.Min(),
            "MAX" => vals.Max(),
            _ => 0,
        };
    }

    /// <summary>Substitute <c>AFMakeNumber(this.getField("X").value)</c> /
    /// <c>getField("X").value</c> occurrences and any declared variables with their
    /// numeric values, then evaluate the arithmetic.</summary>
    private static double? TryEvalExpression(string expr, Func<string, double> getNum,
        Dictionary<string, double> vars)
    {
        // AFMakeNumber(this.getField("X").value)  and  this.getField("X").value
        expr = Regex.Replace(expr, @"AFMakeNumber\s*\(\s*(?:this\.)?getField\s*\(\s*""([^""]*)""\s*\)\s*\.\s*value\s*\)",
            m => Num(getNum(m.Groups[1].Value)));
        expr = Regex.Replace(expr, @"(?:this\.)?getField\s*\(\s*""([^""]*)""\s*\)\s*\.\s*value",
            m => Num(getNum(m.Groups[1].Value)));
        // Substitute declared variables (whole-word), longest names first so a
        // variable name that is a prefix of another doesn't partially match.
        foreach (var kv in vars.OrderByDescending(k => k.Key.Length))
            expr = Regex.Replace(expr, @"\b" + Regex.Escape(kv.Key) + @"\b", Num(kv.Value));
        // Bail if any unresolved identifiers remain (unsupported script shape).
        if (Regex.IsMatch(expr, @"[A-Za-z_]")) return null;
        return ArithmeticEvaluator.TryEval(expr);
    }

    private static string Num(double d) => d.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>Format a computed number the way Acrobat reports a calculated
    /// field value: an integral result carries no decimals ("21", "2000"); otherwise
    /// trailing zeros are trimmed.</summary>
    internal static string FormatNumber(double result)
    {
        if (Math.Abs(result - Math.Round(result)) < 1e-9)
            return ((long)Math.Round(result)).ToString(CultureInfo.InvariantCulture);
        return result.ToString("0.######", CultureInfo.InvariantCulture);
    }

    // ── field-value resolution ──────────────────────────────────────────

    private static double ResolveNumber(string name, PdfReader reader, HashSet<string> visiting)
    {
        if (!visiting.Add(name)) return 0; // cycle guard
        try
        {
            var dict = FindFieldByName(reader, name);
            if (dict is null) return 0;
            var calc = ExtractCalculateScript(dict, reader);
            if (calc is not null &&
                TryEvaluate(calc, n => ResolveNumber(n, reader, visiting)) is { } r)
                return r;
            return ParseNumber(ReadRawValue(dict, reader));
        }
        finally { visiting.Remove(name); }
    }

    private static string? ReadRawValue(PdfDictionary dict, PdfReader reader)
        => reader.Resolve(dict.Get("V")) switch
        {
            PdfString s => s.ToText(),
            PdfName n => n.Value,
            _ => null,
        };

    private static double ParseNumber(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        // Tolerate thousands separators / stray currency; keep digits, sign, dot.
        var cleaned = Regex.Replace(s, @"[^\d.\-]", "");
        return double.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    /// <summary>Locate a field dictionary in the AcroForm field tree by its fully
    /// qualified name (parent.child…) or, failing that, its leaf /T.</summary>
    private static PdfDictionary? FindFieldByName(PdfReader reader, string name)
    {
        PdfDictionary? acroForm;
        try { acroForm = reader.ResolveDict(reader.Catalog?.Get("AcroForm")); }
        catch (InvalidOperationException) { return null; }
        if (acroForm is null) return null;
        if (reader.Resolve(acroForm.Get("Fields")) is not PdfArray fields) return null;

        PdfDictionary? leafMatch = null;
        PdfDictionary? Walk(PdfArray arr, string prefix)
        {
            foreach (var item in arr)
            {
                if (reader.Resolve(item) is not PdfDictionary d) continue;
                var t = (reader.Resolve(d.Get("T")) as PdfString)?.ToText();
                var full = t is null ? prefix : (prefix.Length == 0 ? t : prefix + "." + t);
                if (full == name) return d;
                if (t == name) leafMatch ??= d;
                if (reader.Resolve(d.Get("Kids")) is PdfArray kids)
                {
                    var found = Walk(kids, full);
                    if (found is not null) return found;
                }
            }
            return null;
        }
        return Walk(fields, string.Empty) ?? leafMatch;
    }
}

/// <summary>Tiny arithmetic evaluator for the +, -, *, / and parenthesised
/// expressions produced after field-reference substitution in a calculate script.</summary>
internal static class ArithmeticEvaluator
{
    internal static double? TryEval(string expr)
    {
        double result = 0;
        try
        {
            var parser = new Parser(expr);
            var v = parser.ParseExpr();
            parser.SkipWs();
            if (!parser.AtEnd) return null;
            result = v;
            return (!double.IsNaN(v) && !double.IsInfinity(v)) ? result : null;
        }
        catch { return null; }
    }

    /// <summary>One recursive-descent pass over an expression: the text and the cursor into it.</summary>
    private sealed class Parser
    {
        private readonly string _s;
        private int _p;

        public Parser(string s)
        {
            _s = s;
        }

        public bool AtEnd => _p == _s.Length;

        public double ParseExpr() // + and -
        {
            var v = ParseTerm();
            while (true)
            {
                SkipWs();
                if (_p < _s.Length && (_s[_p] == '+' || _s[_p] == '-'))
                {
                    var op = _s[_p++];
                    var rhs = ParseTerm();
                    v = op == '+' ? v + rhs : v - rhs;
                }
                else break;
            }
            return v;
        }

        private double ParseTerm() // * and /
        {
            var v = ParseFactor();
            while (true)
            {
                SkipWs();
                if (_p < _s.Length && (_s[_p] == '*' || _s[_p] == '/'))
                {
                    var op = _s[_p++];
                    var rhs = ParseFactor();
                    v = op == '*' ? v * rhs : v / rhs;
                }
                else break;
            }
            return v;
        }

        private double ParseFactor()
        {
            SkipWs();
            if (_p < _s.Length && _s[_p] == '(')
            {
                _p++;
                var v = ParseExpr();
                SkipWs();
                if (_p < _s.Length && _s[_p] == ')') _p++;
                return v;
            }
            if (_p < _s.Length && (_s[_p] == '+' || _s[_p] == '-'))
            {
                var op = _s[_p++];
                var v = ParseFactor();
                return op == '-' ? -v : v;
            }
            int start = _p;
            while (_p < _s.Length && (char.IsDigit(_s[_p]) || _s[_p] == '.')) _p++;
            return double.Parse(_s.Substring(start, _p - start), CultureInfo.InvariantCulture);
        }

        public void SkipWs() { while (_p < _s.Length && char.IsWhiteSpace(_s[_p])) _p++; }
    }
}

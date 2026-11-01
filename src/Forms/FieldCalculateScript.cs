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
        // A script that reads its sources as text joins them; as numbers an address came out
        // as nothing and a postcode would lose its leading zero.
        if (script.Contains(TextProperty, StringComparison.Ordinal)
            && TryJoinText(script, name => SourceText(name, reader)) is { } joined)
            return joined;
        // A source the form no longer holds (flattened, removed) cannot be read: the script fails in
        // a viewer and the stored value stands, where counting the source as zero recomputed a
        // total from nothing.
        var sourceMissing = false;
        double Source(string name)
        {
            var value = ResolveNumber(name, reader, new HashSet<string>());
            if (!double.IsNaN(value)) return value;
            sourceMissing = true;
            return 0;
        }
        if (TryEvaluate(script, Source) is not { } result || sourceMissing)
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

    // ── text joins ──────────────────────────────────────────────────────

    /// <summary>The field property a script reads a source's text through.</summary>
    private const string TextProperty = "valueAsString";

    private const string TextOfField = @"(?:this\.)?getField\s*\(\s*""([^""]*)""\s*\)\s*\.\s*" + TextProperty;

    /// <summary>The text a named field holds, empty when it has no value; null when the form has no such field.</summary>
    private static string? SourceText(string name, PdfReader reader)
        => FindFieldByName(reader, name) is { } source ? ReadRawValue(source, reader) ?? string.Empty : null;

    /// <summary>Evaluate a script that joins text: variables holding a field's text or a string
    /// literal, and ONE <c>event.value</c> assignment of such terms joined with +. Null for any
    /// other shape (branches, loops, calls) and when a source field is missing.</summary>
    internal static string? TryJoinText(string script, Func<string, string?> getText)
    {
        if (SafeRegex.IsMatch(script, @"\b(?:if|else|for|while|switch|function)\b")) return null;
        var assignments = SafeRegex.Matches(script, @"event\.value\s*=(?!=)");
        if (assignments.Count != 1) return null;

        var vars = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var groups in SafeRegex.Matches(script,
            @"var\s+(\w+)\s*=\s*(?:" + TextOfField + @"|""((?:[^""\\]|\\.)*)"")\s*;").Select(m => m.Groups))
        {
            var text = groups[2].Success ? getText(groups[2].Value) : Unescape(groups[3].Value);
            if (text is null) return null;
            vars[groups[1].Value] = text;
        }

        var terms = SplitJoinTerms(script, assignments[0].Index + assignments[0].Length);
        if (terms is null) return null;
        var joined = new System.Text.StringBuilder();
        foreach (var term in terms)
        {
            if (JoinTermText(term, vars, getText) is not { } text) return null;
            joined.Append(text);
        }
        return joined.ToString();
    }

    /// <summary>The terms of the + chain that starts at <paramref name="at"/> and ends with its
    /// statement; null when a string literal is left open.</summary>
    private static List<string>? SplitJoinTerms(string script, int at)
    {
        var terms = new List<string>();
        var term = new System.Text.StringBuilder();
        var quote = '\0';
        for (; at < script.Length; at++)
        {
            var c = script[at];
            if (quote != '\0')
            {
                term.Append(c);
                if (c == '\\' && at + 1 < script.Length) term.Append(script[++at]);
                else if (c == quote) quote = '\0';
                continue;
            }
            if (c is ';' or '\r' or '\n') break;
            if (c is '"' or '\'') quote = c;
            if (c == '+')
            {
                terms.Add(term.ToString().Trim());
                term.Clear();
            }
            else
            {
                term.Append(c);
            }
        }
        if (quote != '\0') return null;
        terms.Add(term.ToString().Trim());
        return terms;
    }

    /// <summary>The text of one term: a string literal, a declared variable, or a field's text.</summary>
    private static string? JoinTermText(string term, Dictionary<string, string> vars, Func<string, string?> getText)
    {
        if (term.Length >= 2 && term[0] is '"' or '\'' && term[^1] == term[0])
            return Unescape(term.Substring(1, term.Length - 2));
        if (vars.TryGetValue(term, out var text)) return text;
        var field = SafeRegex.Match(term, "^" + TextOfField + "$");
        return field.Success ? getText(field.Groups[1].Value) : null;
    }

    /// <summary>A string literal's text with its backslash escapes resolved.</summary>
    private static string Unescape(string literal)
    {
        if (literal.IndexOf('\\') < 0) return literal;
        var text = new System.Text.StringBuilder(literal.Length);
        for (var i = 0; i < literal.Length; i++)
        {
            var c = literal[i];
            if (c != '\\' || i + 1 == literal.Length)
            {
                text.Append(c);
                continue;
            }
            c = literal[++i];
            text.Append(c switch { 'r' => '\r', 'n' => '\n', 't' => '\t', _ => c });
        }
        return text.ToString();
    }

    /// <summary>Evaluate a calculate script given a numeric field-value resolver.</summary>
    internal static double? TryEvaluate(string script, Func<string, double> getNum)
    {
        double result = 0;
        if (string.IsNullOrEmpty(script)) return null;

        // AFSimple_Calculate("OP", new Array("a","b",…))  or  ("OP", ["a","b"])
        var simple = SafeRegex.Match(script,
            @"AFSimple_Calculate\s*\(\s*""(\w+)""\s*,\s*(?:new\s+Array\s*)?[\(\[]([^)\]]*)[)\]]",
            RegexOptions.IgnoreCase);
        if (simple.Success)
        {
            var op = simple.Groups[1].Value.ToUpperInvariant();
            var names = SafeRegex.Matches(simple.Groups[2].Value, @"""([^""]*)""")
                             .Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            var vals = names.Select(getNum).ToList();
            result = Aggregate(op, vals);
            return result;
        }

        // Variable declarations: var X = AFMakeNumber(this.getField("F").value)
        // (or the bare getField(...).value form). Build a name→value map.
        var vars = new Dictionary<string, double>();
        foreach (var groups in SafeRegex.Matches(script,
            @"var\s+(\w+)\s*=\s*(?:AFMakeNumber\s*\(\s*)?(?:this\.)?getField\s*\(\s*""([^""]*)""\s*\)\s*\.\s*value").Select(vm => vm.Groups))
            vars[groups[1].Value] = getNum(groups[2].Value);
        // var X = Number(this.getField("F").valueAsString.replace("$", "")): the field's text made a number, its
        // currency and grouping marks stripped - what the number a field holds is read as anyway.
        foreach (var groups in SafeRegex.Matches(script,
            @"var\s+(\w+)\s*=\s*(?:Number|AFMakeNumber)\s*\(\s*(?:this\.)?getField\s*\(\s*""([^""]*)""\s*\)\s*\.\s*value(?:AsString)?"
            + @"(?:\s*\.\s*replace\s*\(\s*""[^""]*""\s*,\s*""[^""]*""\s*\))*\s*\)\s*;").Select(vm => vm.Groups))
            vars[groups[1].Value] = getNum(groups[2].Value);

        // Field-OBJECT declarations: var X = this.getField("F");  (no .value) — the
        // expression then reads X.value. Bind X.value up front so the substitution
        // pass below sees a plain variable.
        foreach (var vmGroups in SafeRegex.Matches(script,
            @"var\s+(\w+)\s*=\s*(?:this\.)?getField\s*\(\s*""([^""]*)""\s*\)\s*;").Where(vm => !vars.ContainsKey(vm.Groups[1].Value + ".value")).Select(vm => vm.Groups))
            vars[vmGroups[1].Value + ".value"] = getNum(vmGroups[2].Value);

        // event.value = <expression>. There may be several (e.g. an if/else that
        // clears the field); take the first non-empty-string assignment that
        // evaluates to a number.
        foreach (Match ev in SafeRegex.Matches(script, @"event\.value\s*=\s*([^;\r\n}]+)"))
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
        // AFMakeNumber(this.getField("X").value), Number(...) and  this.getField("X").value
        expr = SafeRegex.Replace(expr, @"(?:AFMakeNumber|Number)\s*\(\s*(?:this\.)?getField\s*\(\s*""([^""]*)""\s*\)\s*\.\s*value\s*\)",
            m => Num(getNum(m.Groups[1].Value)));
        expr = SafeRegex.Replace(expr, @"(?:this\.)?getField\s*\(\s*""([^""]*)""\s*\)\s*\.\s*value",
            m => Num(getNum(m.Groups[1].Value)));
        // Substitute declared variables (whole-word), longest names first so a
        // variable name that is a prefix of another doesn't partially match.
        foreach (var kv in vars.OrderByDescending(k => k.Key.Length))
            expr = SafeRegex.Replace(expr, @"\b" + Regex.Escape(kv.Key) + @"\b", Num(kv.Value));
        // Bail if any unresolved identifiers remain (unsupported script shape).
        if (SafeRegex.IsMatch(expr, @"[A-Za-z_]")) return null;
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

    /// <summary>The number a named field holds or calculates; NaN when the form has no such field.</summary>
    private static double ResolveNumber(string name, PdfReader reader, HashSet<string> visiting)
    {
        if (!visiting.Add(name)) return 0; // cycle guard
        try
        {
            var dict = FindFieldByName(reader, name);
            if (dict is null) return double.NaN;
            var calc = ExtractCalculateScript(dict, reader);
            if (calc is not null &&
                TryEvaluate(calc, n => OrZero(ResolveNumber(n, reader, visiting))) is { } r)
                return r;
            return ParseNumber(ReadRawValue(dict, reader));
        }
        finally { visiting.Remove(name); }
    }

    private static double OrZero(double value) => double.IsNaN(value) ? 0 : value;

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
        var cleaned = SafeRegex.Replace(s, @"[^\d.\-]", "");
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
                string full;
                if (t is null) full = prefix;
                else full = prefix.Length == 0 ? t : prefix + "." + t;
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

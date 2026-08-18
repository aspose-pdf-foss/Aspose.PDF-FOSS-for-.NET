using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // ── Trivial inline DOM scripts ──────────────────────────────────────────────
    // The source renderer executes page scripts before layout, so a document whose
    // only content is script-built text still renders that text. A full script
    // engine is out of scope; instead a micro-interpreter covers the STRAIGHT-LINE
    // subset such pages actually use:
    //
    //   assignments        x = expr;  var x = expr;  x += expr;
    //   expressions        'str' / "str" literals, numbers, [ 'a', 'b' ] arrays,
    //                      identifiers, arr[i] indexing, arr.length, + concatenation
    //   counted loop       for (var i = 0; i < arr.length; i++) { ...assignments... }
    //   DOM tail           document.getElementsByTagName('body')[0]
    //                      document.createTextNode(x) · body.appendChild(node)
    //
    // Each <script> body either parses WHOLE into this grammar (its appendChild
    // text is then injected where the script tag stood, exactly where the DOM got
    // it) or is left for the existing strip — a partial parse never emits anything.

    /// <summary>Replace each evaluable inline script with the text its appendChild
    /// calls added to the document; leave every other script untouched.</summary>
    private static string ApplyTrivialDomScripts(string html)
    {
        return Regex.Replace(html, @"<script[^>]*>([\s\S]*?)</script>", m =>
        {
            var body = m.Groups[1].Value;
            return TryEvalTrivialDomScript(body, out var text) ? text : m.Value;
        }, RegexOptions.IgnoreCase);
    }

    private sealed class JsVal
    {
        public string? Str;
        public double Num;
        public bool IsNum;
        public List<string>? Arr;
        public bool IsBody;          // the document.body element
        public string? TextNode;     // a created text node's contents
    }

    private static bool TryEvalTrivialDomScript(string js, out string appended)
    {
        appended = "";
        if (string.IsNullOrWhiteSpace(js)) return false;
        var vars = new Dictionary<string, JsVal>(StringComparer.Ordinal);
        var output = new StringBuilder();
        if (!EvalScriptBlock(js, vars, output, depth: 0)) return false;
        appended = output.ToString();
        return appended.Length > 0;
    }

    /// <summary>Evaluate a sequence of statements; false = the block (or any part
    /// of it) falls outside the subset, and the caller must not use any output.</summary>
    private static bool EvalScriptBlock(string block, Dictionary<string, JsVal> vars,
        StringBuilder output, int depth)
    {
        if (depth > 2) return false;
        var pos = 0;
        while (true)
        {
            while (pos < block.Length && (char.IsWhiteSpace(block[pos]) || block[pos] == ';')) pos++;
            if (pos >= block.Length) return true;

            // for (var i = 0; i < arr.length; i++) { body }
            var forM = Regex.Match(block[pos..],
                @"\Afor\s*\(\s*(?:var\s+)?(\w+)\s*=\s*(\d+)\s*;\s*\1\s*<\s*(\w+)\.length\s*;\s*\1\+\+\s*\)\s*\{",
                RegexOptions.Singleline);
            if (forM.Success)
            {
                var open = pos + forM.Length - 1;
                var close = FindMatchingBrace(block, open);
                if (close < 0) return false;
                var idxVar = forM.Groups[1].Value;
                if (!int.TryParse(forM.Groups[2].Value, out var start)) return false;
                if (!vars.TryGetValue(forM.Groups[3].Value, out var arrV) || arrV.Arr is null)
                    return false;
                var bodyStmt = block[(open + 1)..close];
                for (var i = start; i < arrV.Arr.Count; i++)
                {
                    vars[idxVar] = new JsVal { IsNum = true, Num = i };
                    if (!EvalScriptBlock(bodyStmt, vars, output, depth + 1)) return false;
                }
                vars.Remove(idxVar);
                pos = close + 1;
                continue;
            }

            // one plain statement up to the next ';' OUTSIDE quotes/brackets
            // (a "; " string literal must not split its own statement)
            var end = FindStatementEnd(block, pos);
            var stmt = block[pos..end].Trim();
            pos = end + 1;
            if (stmt.Length == 0) continue;
            if (!EvalStatement(stmt, vars, output)) return false;
        }
    }

    /// <summary>Index of the ';' ending the statement starting at <paramref name="pos"/>,
    /// skipping quoted strings and bracketed groups; the block end when none.</summary>
    private static int FindStatementEnd(string s, int pos)
    {
        var depth = 0; var quote = '\0';
        for (var i = pos; i < s.Length; i++)
        {
            var c = s[i];
            if (quote != '\0') { if (c == quote && s[i - 1] != '\\') quote = '\0'; continue; }
            if (c is '\'' or '"') quote = c;
            else if (c is '[' or '(' or '{') depth++;
            else if (c is ']' or ')' or '}') depth--;
            else if (c == ';' && depth == 0) return i;
        }
        return s.Length;
    }

    private static int FindMatchingBrace(string s, int open)
    {
        var d = 0;
        for (var i = open; i < s.Length; i++)
        {
            if (s[i] == '{') d++;
            else if (s[i] == '}' && --d == 0) return i;
        }
        return -1;
    }

    private static bool EvalStatement(string stmt, Dictionary<string, JsVal> vars, StringBuilder output)
    {
        // x.appendChild(y)
        var app = Regex.Match(stmt, @"^(\w+)\.appendChild\s*\(\s*(\w+)\s*\)$");
        if (app.Success)
        {
            if (!vars.TryGetValue(app.Groups[1].Value, out var target) || !target.IsBody) return false;
            if (!vars.TryGetValue(app.Groups[2].Value, out var node) || node.TextNode is null) return false;
            output.Append(node.TextNode);
            return true;
        }

        // [var] name = expr   ·   name += expr
        var asg = Regex.Match(stmt, @"^(?:var\s+)?(\w+)\s*(\+?=)\s*(.+)$", RegexOptions.Singleline);
        if (!asg.Success) return false;
        var name = asg.Groups[1].Value;
        var rhs = asg.Groups[3].Value.Trim();

        JsVal? val;
        // document.getElementsByTagName('body')[0]
        if (Regex.IsMatch(rhs, @"^document\.getElementsByTagName\s*\(\s*['""]body['""]\s*\)\s*\[\s*0\s*\]$"))
            val = new JsVal { IsBody = true };
        // document.createTextNode(expr)
        else if (Regex.Match(rhs, @"^document\.createTextNode\s*\(\s*(.+?)\s*\)$") is { Success: true } ctn)
        {
            if (EvalExpr(ctn.Groups[1].Value, vars) is not { Str: not null } tv) return false;
            val = new JsVal { TextNode = tv.Str };
        }
        else
            val = EvalExpr(rhs, vars);
        if (val is null) return false;

        if (asg.Groups[2].Value == "+=")
        {
            if (!vars.TryGetValue(name, out var cur) || cur.Str is null || val.Str is null) return false;
            vars[name] = new JsVal { Str = cur.Str + val.Str };
        }
        else
            vars[name] = val;
        return true;
    }

    /// <summary>Evaluate a +-concatenation of string/number literals, identifiers,
    /// arr[idx] and arr.length terms; null = outside the subset.</summary>
    private static JsVal? EvalExpr(string expr, Dictionary<string, JsVal> vars)
    {
        expr = expr.Trim();
        // array literal of string literals
        if (expr.StartsWith('[') && expr.EndsWith(']'))
        {
            var items = new List<string>();
            var inner = expr[1..^1].Trim();
            if (inner.Length > 0)
                foreach (var raw in SplitTopLevel(inner, ','))
                {
                    var it = raw.Trim();
                    if (it.Length < 2 || (it[0] != '\'' && it[0] != '"') || it[^1] != it[0]) return null;
                    items.Add(it[1..^1]);
                }
            return new JsVal { Arr = items };
        }

        string? acc = null;
        double numAcc = 0;
        var numeric = true;
        var first = true;
        foreach (var raw in SplitTopLevel(expr, '+'))
        {
            var term = raw.Trim();
            if (term.Length == 0) return null;
            JsVal? tv;
            if ((term[0] == '\'' || term[0] == '"') && term.Length >= 2 && term[^1] == term[0])
                tv = new JsVal { Str = term[1..^1] };
            else if (double.TryParse(term, System.Globalization.NumberStyles.Float,
                         System.Globalization.CultureInfo.InvariantCulture, out var n))
                tv = new JsVal { IsNum = true, Num = n };
            else if (Regex.Match(term, @"^(\w+)\.length$") is { Success: true } lm)
            {
                if (!vars.TryGetValue(lm.Groups[1].Value, out var av) || av.Arr is null) return null;
                tv = new JsVal { IsNum = true, Num = av.Arr.Count };
            }
            else if (Regex.Match(term, @"^(\w+)\s*\[\s*(\w+)\s*\]$") is { Success: true } ix)
            {
                if (!vars.TryGetValue(ix.Groups[1].Value, out var av) || av.Arr is null) return null;
                double idx;
                if (vars.TryGetValue(ix.Groups[2].Value, out var iv) && iv.IsNum) idx = iv.Num;
                else if (!double.TryParse(ix.Groups[2].Value, out idx)) return null;
                if (idx < 0 || idx >= av.Arr.Count || idx % 1 != 0) return null;
                tv = new JsVal { Str = av.Arr[(int)idx] };
            }
            else if (Regex.IsMatch(term, @"^\w+$") && vars.TryGetValue(term, out var vv))
                tv = vv;
            else
                return null;

            if (tv.Str is not null) numeric = false;
            else if (!tv.IsNum) return null;   // arrays/DOM handles cannot concatenate

            if (first)
            {
                acc = tv.Str; numAcc = tv.Num; first = false;
                if (tv.Str is null && !tv.IsNum) return null;
            }
            else if (numeric)
                numAcc += tv.Num;
            else
                acc = (acc ?? numAcc.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    + (tv.Str ?? tv.Num.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (first) return null;
        return numeric ? new JsVal { IsNum = true, Num = numAcc } : new JsVal { Str = acc };
    }

    /// <summary>Split on a separator outside quotes and brackets.</summary>
    private static IEnumerable<string> SplitTopLevel(string s, char sep)
    {
        var depth = 0; var start = 0; var quote = '\0';
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (quote != '\0') { if (c == quote && s[i - 1] != '\\') quote = '\0'; continue; }
            if (c is '\'' or '"') quote = c;
            else if (c is '[' or '(' or '{') depth++;
            else if (c is ']' or ')' or '}') depth--;
            else if (c == sep && depth == 0) { yield return s[start..i]; start = i + 1; }
        }
        yield return s[start..];
    }
}

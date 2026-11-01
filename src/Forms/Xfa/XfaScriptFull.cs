namespace Aspose.Pdf.Forms.Xfa;

// The fuller syntax a RENDER's script run reads: functions and their calls, return, loops,
// increments, array literals and computed members. The field-set run keeps the older, narrower
// reading (it steps over these constructs), so everything here is behind the parser's full mode.

/// <summary>A function declaration or expression.</summary>
internal sealed class XFunc : IXStmt, IXNode { public string Name = ""; public List<string> Params = new(); public List<IXStmt> Body = new(); }
internal sealed class XReturn : IXStmt { public IXNode? Value; }
internal sealed class XContinue : IXStmt { }
/// <summary>for, while and do-while: the body runs first for a do-while.</summary>
internal sealed class XFor : IXStmt { public IXStmt? Init; public IXNode? Test, Update; public List<IXStmt> Body = new(); public bool BodyFirst; }
/// <summary>x++ / ++x / x-- / --x.</summary>
internal sealed class XIncDec : IXNode { public IXNode Target = default!; public int Delta; public bool Prefix; }
internal sealed class XIndex : IXNode { public IXNode Obj = default!, Index = default!; }
internal sealed class XArray : IXNode { public List<IXNode> Items = new(); }
/// <summary>Something the parser could not read (a regular expression, an object literal): its value is not known.</summary>
internal sealed class XUnknown : IXNode { }

internal sealed partial class XParser
{
    /// <summary>The statements only the full mode reads; null hands the statement back to the common reading.</summary>
    private IXStmt? ParseFullStmt()
    {
        if (IsKw("function") && _t[_p + 1].Type == "id") return ParseFunction();
        if (IsKw("return"))
        {
            _p++;
            var value = IsPunc(";") || IsPunc("}") || IsEof ? null : ParseExpr();
            Eat(";");
            return new XReturn { Value = value };
        }
        if (IsKw("continue")) { _p++; Eat(";"); return new XContinue(); }
        if (IsKw("throw")) { _p++; ParseExpr(); Eat(";"); return new XSkipped(); }
        if (IsKw("var") || IsKw("let") || IsKw("const")) return ParseVarList();
        if (IsKw("while"))
        {
            _p++; Eat("(");
            var test = ParseExpr();
            Eat(")");
            return new XFor { Test = test, Body = ParseStmtAsList() };
        }
        if (IsKw("do"))
        {
            _p++;
            var loop = new XFor { Body = ParseStmtAsList(), BodyFirst = true };
            if (IsKw("while")) { _p++; Eat("("); loop.Test = ParseExpr(); Eat(")"); Eat(";"); }
            return loop;
        }
        return IsKw("for") ? ParseFor() : null;
    }

    private IXStmt ParseFor()
    {
        _p++; Eat("(");
        // for (x in y): the members of an object are not something the interpreter enumerates.
        int look = IsKw("var") || IsKw("let") || IsKw("const") ? _p + 1 : _p;
        if (look + 1 < _t.Count && _t[look].Type == "id" && _t[look + 1].Type == "id" && _t[look + 1].Text == "in")
        {
            SkipConstruct();
            return new XSkipped();
        }
        var loop = new XFor();
        if (IsKw("var") || IsKw("let") || IsKw("const")) loop.Init = ParseVarList();
        else
        {
            if (!IsPunc(";")) loop.Init = new XExprStmt { Expr = ParseExpr() };
            Eat(";");
        }
        if (!IsPunc(";")) loop.Test = ParseExpr();
        Eat(";");
        if (!IsPunc(")")) loop.Update = ParseExpr();
        Eat(")");
        loop.Body = ParseStmtAsList();
        return loop;
    }

    private XFunc ParseFunction()
    {
        _p++;                                   // function
        var f = new XFunc();
        if (Cur.Type == "id") f.Name = Next().Text;
        Eat("(");
        while (!IsEof && !IsPunc(")"))
        {
            if (Cur.Type == "id") f.Params.Add(Cur.Text);
            _p++;
        }
        Eat(")");
        if (IsPunc("{")) f.Body = ParseBlock().Body;
        return f;
    }

    /// <summary>var a = 1, b, c = a: every declared name, in order.</summary>
    private IXStmt ParseVarList()
    {
        _p++;                                   // var
        var all = new XBlock();
        do
        {
            if (IsPunc(",")) _p++;
            var v = new XVar { Name = Cur.Type == "id" ? Next().Text : "_" };
            if (IsPunc("=")) { _p++; v.Init = ParseAssign(); }
            all.Body.Add(v);
        } while (IsPunc(","));
        Eat(";");
        return all.Body.Count == 1 ? all.Body[0] : all;
    }

    /// <summary>The primaries only the full mode reads: a function expression, an array literal, and an object literal (not read: unknown).</summary>
    private IXNode? ParseFullPrimary()
    {
        if (IsKw("function")) return ParseFunction();
        if (Cur.Type == "regex")
        {
            var token = Next();
            return new XLiteral { Value = new XRegex(token.Text, token.Value as string ?? "") };
        }
        if (IsPunc("["))
        {
            _p++;
            var array = new XArray();
            while (!IsEof && !IsPunc("]"))
            {
                array.Items.Add(ParseAssign());
                if (IsPunc(",")) _p++;
                else break;
            }
            Eat("]");
            return array;
        }
        if (IsPunc("{")) { SkipBalancedBraces(); return new XUnknown(); }
        return null;
    }
}

/// <summary>A regular expression literal: the pattern and its flags, compiled on first use
/// (null when the pattern is not one the engine reads).</summary>
internal sealed class XRegex
{
    public readonly string Pattern, Flags;
    private System.Text.RegularExpressions.Regex? _regex;
    private bool _built;
    public XRegex(string pattern, string flags) { Pattern = pattern; Flags = flags; }

    public bool Global => Flags.IndexOf('g') >= 0;

    public System.Text.RegularExpressions.Regex? Regex
    {
        get
        {
            if (_built) return _regex;
            _built = true;
            // JavaScript folds case the same in every locale: under a Turkish culture /ti/i still matches "TITLE".
            var options = System.Text.RegularExpressions.RegexOptions.ECMAScript
                | System.Text.RegularExpressions.RegexOptions.CultureInvariant;
            if (Flags.IndexOf('i') >= 0) options |= System.Text.RegularExpressions.RegexOptions.IgnoreCase;
            if (Flags.IndexOf('m') >= 0) options |= System.Text.RegularExpressions.RegexOptions.Multiline;
            try { _regex = new System.Text.RegularExpressions.Regex(Pattern, options, RegexLimits.Timeout); }
            catch (ArgumentException) { _regex = null; }
            return _regex;
        }
    }
}

internal sealed partial class XLexer
{
    /// <summary>A slash starts a regular expression where a value may start: at the beginning, after an
    /// operator or an opening bracket, after return - not after a value, where it divides.</summary>
    private static bool RegexMayStart(List<XToken> toks)
    {
        if (toks.Count == 0) return true;
        var last = toks[toks.Count - 1];
        if (last.Type == "punc") return last.Text is not (")" or "]" or "++" or "--");
        return last.Type == "id" && last.Text is "return" or "typeof" or "case";
    }

    /// <summary>Reads /pattern/flags from the current slash; null (nothing consumed) when the line ends first.</summary>
    private XToken? ReadRegex()
    {
        int i = _i + 1;
        bool inClass = false;
        for (; i < _s.Length && _s[i] != '\n'; i++)
        {
            char c = _s[i];
            if (c == '\\') { i++; continue; }
            if (c == '[') inClass = true;
            else if (c == ']') inClass = false;
            else if (c == '/' && !inClass) break;
        }
        if (i >= _s.Length || _s[i] != '/') return null;
        var pattern = _s.Substring(_i + 1, i - _i - 1);
        int f = i + 1;
        while (f < _s.Length && char.IsLetter(_s[f])) f++;
        var token = new XToken { Type = "regex", Text = pattern, Value = _s.Substring(i + 1, f - i - 1) };
        _i = f;
        return token;
    }
}

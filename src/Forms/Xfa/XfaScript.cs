using System.Globalization;
using System.Text;
using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

// A minimal, self-contained (zero-dependency) interpreter for the ECMAScript subset that XFA
// templates use in their initialize/calculate scripts, together with the XFA object model those
// scripts touch (this / xfa / resolveNode / .presence / .rawValue / .access / .parent). It exists
// only to resolve which fields render when dynamic XFA is flattened to a static AcroForm; it is
// deliberately tolerant (unknown identifiers, members and calls evaluate to null instead of
// throwing) so a script it does not fully understand degrades to a no-op rather than aborting.

// ---------------------------------------------------------------------------------------------
// AST
// ---------------------------------------------------------------------------------------------
internal abstract class XNode { }

internal sealed class XLiteral : XNode { public object? Value; }
internal sealed class XIdent : XNode { public string Name = ""; }
internal sealed class XThis : XNode { }
internal sealed class XMember : XNode { public XNode Obj = default!; public string Name = ""; }
internal sealed class XCall : XNode { public XNode Callee = default!; public List<XNode> Args = new(); }
internal sealed class XUnary : XNode { public string Op = ""; public XNode Operand = default!; }
internal sealed class XBinary : XNode { public string Op = ""; public XNode L = default!, R = default!; }
internal sealed class XLogical : XNode { public string Op = ""; public XNode L = default!, R = default!; }
internal sealed class XCond : XNode { public XNode Test = default!, Then = default!, Else = default!; }
internal sealed class XAssign : XNode { public XNode Target = default!; public XNode Value = default!; }

internal abstract class XStmt { }
internal sealed class XExprStmt : XStmt { public XNode Expr = default!; }
internal sealed class XVar : XStmt { public string Name = ""; public XNode? Init; }
internal sealed class XIf : XStmt { public XNode Test = default!; public List<XStmt> Then = new(); public List<XStmt> Else = new(); }
internal sealed class XBlock : XStmt { public List<XStmt> Body = new(); }
internal sealed class XSwitch : XStmt { public XNode Disc = default!; public List<(XNode? test, List<XStmt> body)> Cases = new(); }
internal sealed class XBreak : XStmt { }
internal sealed class XNop : XStmt { }

// ---------------------------------------------------------------------------------------------
// Lexer
// ---------------------------------------------------------------------------------------------
internal sealed class XToken
{
    public string Type = "";     // id / num / str / punc / eof
    public string Text = "";
    public object? Value;
}

internal sealed class XLexer
{
    private readonly string _s;
    private int _i;
    public XLexer(string s) { _s = s; }

    private static readonly string[] Puncs =
    {
        "===","!==","==","!=","<=",">=","&&","||","**","+=","-=","<<",">>",
        "=","<",">","+","-","*","/","%","!","(",")","{","}","[","]",".",",",";","?",":","&","|","~",
    };

    public List<XToken> Tokenize()
    {
        var toks = new List<XToken>();
        while (_i < _s.Length)
        {
            char c = _s[_i];
            if (char.IsWhiteSpace(c)) { _i++; continue; }
            // comments
            if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '/')
            { while (_i < _s.Length && _s[_i] != '\n') _i++; continue; }
            if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '*')
            { _i += 2; while (_i + 1 < _s.Length && !(_s[_i] == '*' && _s[_i + 1] == '/')) _i++; _i += 2; continue; }
            // string
            if (c == '"' || c == '\'') { toks.Add(ReadString(c)); continue; }
            // number
            if (char.IsDigit(c) || (c == '.' && _i + 1 < _s.Length && char.IsDigit(_s[_i + 1])))
            { toks.Add(ReadNumber()); continue; }
            // identifier (XFA SOM names allow letters, digits, _ and # e.g. #text)
            if (char.IsLetter(c) || c == '_' || c == '$' || c == '#')
            { toks.Add(ReadIdent()); continue; }
            // punctuator (longest match)
            string? p = null;
            foreach (var cand in Puncs)
                if (_i + cand.Length <= _s.Length && _s.Substring(_i, cand.Length) == cand) { p = cand; break; }
            if (p is null) { _i++; continue; }           // unknown char — skip (tolerant)
            _i += p.Length;
            toks.Add(new XToken { Type = "punc", Text = p });
        }
        toks.Add(new XToken { Type = "eof", Text = "" });
        return toks;
    }

    private XToken ReadString(char q)
    {
        _i++;
        var sb = new StringBuilder();
        while (_i < _s.Length && _s[_i] != q)
        {
            if (_s[_i] == '\\' && _i + 1 < _s.Length) { _i++; sb.Append(Unescape(_s[_i])); }
            else sb.Append(_s[_i]);
            _i++;
        }
        _i++;
        return new XToken { Type = "str", Text = sb.ToString(), Value = sb.ToString() };
    }

    private static char Unescape(char c) => c switch { 'n' => '\n', 't' => '\t', 'r' => '\r', _ => c };

    private XToken ReadNumber()
    {
        int start = _i;
        while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.')) _i++;
        var t = _s.Substring(start, _i - start);
        double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out var d);
        return new XToken { Type = "num", Text = t, Value = d };
    }

    private XToken ReadIdent()
    {
        int start = _i;
        while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_' || _s[_i] == '$' || _s[_i] == '#')) _i++;
        return new XToken { Type = "id", Text = _s.Substring(start, _i - start) };
    }
}

// ---------------------------------------------------------------------------------------------
// Parser (recursive descent + precedence climbing, with statement-level error recovery)
// ---------------------------------------------------------------------------------------------
internal sealed class XParser
{
    private readonly List<XToken> _t;
    private int _p;
    public XParser(List<XToken> toks) { _t = toks; }

    private XToken Cur => _t[_p];
    private bool IsEof => Cur.Type == "eof";
    private bool IsPunc(string s) => Cur.Type == "punc" && Cur.Text == s;
    private bool IsKw(string s) => Cur.Type == "id" && Cur.Text == s;
    private XToken Next() => _t[_p++];
    private void Eat(string punc) { if (IsPunc(punc)) _p++; }

    public List<XStmt> ParseProgram()
    {
        var body = new List<XStmt>();
        while (!IsEof)
        {
            int before = _p;
            XStmt? s = null;
            try { s = ParseStmt(); } catch { s = null; }
            if (s is not null) body.Add(s);
            if (_p == before) SkipToStmtBoundary();          // recovery: never loop forever
        }
        return body;
    }

    private void SkipToStmtBoundary()
    {
        // advance to the next ';' or '}' (or eof) and step past it
        while (!IsEof && !IsPunc(";") && !IsPunc("}")) _p++;
        if (IsPunc(";") || IsPunc("}")) _p++;
    }

    private XStmt ParseStmt()
    {
        if (IsPunc(";")) { _p++; return new XNop(); }
        if (IsPunc("{")) return ParseBlock();
        if (IsKw("var") || IsKw("let") || IsKw("const")) return ParseVar();
        if (IsKw("if")) return ParseIf();
        if (IsKw("switch")) return ParseSwitch();
        if (IsKw("break")) { _p++; Eat(";"); return new XBreak(); }
        if (IsKw("return")) { _p++; if (!IsPunc(";") && !IsPunc("}")) ParseExpr(); Eat(";"); return new XNop(); }
        // for / while / function / try: not needed by load-time scripts — skip their whole body
        if (IsKw("for") || IsKw("while") || IsKw("function") || IsKw("try") || IsKw("do"))
        { SkipConstruct(); return new XNop(); }
        var e = ParseExpr();
        Eat(";");
        return new XExprStmt { Expr = e };
    }

    private void SkipConstruct()
    {
        // consume tokens up to and including a balanced { } block (or to next ';')
        while (!IsEof && !IsPunc("{") && !IsPunc(";")) _p++;
        if (IsPunc("{")) SkipBalancedBraces();
        else Eat(";");
    }

    private void SkipBalancedBraces()
    {
        int depth = 0;
        do
        {
            if (IsPunc("{")) depth++;
            else if (IsPunc("}")) depth--;
            _p++;
        } while (!IsEof && depth > 0);
    }

    private XBlock ParseBlock()
    {
        Eat("{");
        var b = new XBlock();
        while (!IsEof && !IsPunc("}"))
        {
            int before = _p;
            XStmt? s = null;
            try { s = ParseStmt(); } catch { s = null; }
            if (s is not null) b.Body.Add(s);
            if (_p == before) { if (!IsPunc("}")) _p++; }
        }
        Eat("}");
        return b;
    }

    private XVar ParseVar()
    {
        _p++;                                   // var
        var name = Cur.Type == "id" ? Next().Text : "_";
        XNode? init = null;
        if (IsPunc("=")) { _p++; init = ParseAssign(); }
        // ignore comma-declarations' tail
        while (IsPunc(",")) { _p++; if (Cur.Type == "id") _p++; if (IsPunc("=")) { _p++; ParseAssign(); } }
        Eat(";");
        return new XVar { Name = name, Init = init };
    }

    private XIf ParseIf()
    {
        _p++; Eat("(");
        var test = ParseExpr();
        Eat(")");
        var node = new XIf { Test = test };
        node.Then = ParseStmtAsList();
        if (IsKw("else")) { _p++; node.Else = ParseStmtAsList(); }
        return node;
    }

    private List<XStmt> ParseStmtAsList()
    {
        if (IsPunc("{")) return ParseBlock().Body;
        var s = ParseStmt();
        return new List<XStmt> { s };
    }

    private XSwitch ParseSwitch()
    {
        _p++; Eat("(");
        var disc = ParseExpr();
        Eat(")"); Eat("{");
        var sw = new XSwitch { Disc = disc };
        while (!IsEof && !IsPunc("}"))
        {
            XNode? test = null;
            if (IsKw("case")) { _p++; test = ParseExpr(); Eat(":"); }
            else if (IsKw("default")) { _p++; Eat(":"); }
            else { _p++; continue; }
            var body = new List<XStmt>();
            while (!IsEof && !IsPunc("}") && !IsKw("case") && !IsKw("default"))
            {
                int before = _p;
                XStmt? s = null;
                try { s = ParseStmt(); } catch { s = null; }
                if (s is not null) body.Add(s);
                if (_p == before) _p++;
            }
            sw.Cases.Add((test, body));
        }
        Eat("}");
        return sw;
    }

    // ---- expressions ----
    private XNode ParseExpr()
    {
        var e = ParseAssign();
        while (IsPunc(",")) { _p++; e = ParseAssign(); }   // comma operator → last value
        return e;
    }

    private XNode ParseAssign()
    {
        var left = ParseCond();
        if (IsPunc("=") || IsPunc("+=") || IsPunc("-="))
        {
            var op = Next().Text;
            var right = ParseAssign();
            if (op == "+=") right = new XBinary { Op = "+", L = left, R = right };
            else if (op == "-=") right = new XBinary { Op = "-", L = left, R = right };
            return new XAssign { Target = left, Value = right };
        }
        return left;
    }

    private XNode ParseCond()
    {
        var c = ParseBinary(0);
        if (IsPunc("?"))
        {
            _p++;
            var t = ParseAssign();
            Eat(":");
            var e = ParseAssign();
            return new XCond { Test = c, Then = t, Else = e };
        }
        return c;
    }

    // precedence table
    private static int Prec(string op) => op switch
    {
        "||" => 1,
        "&&" => 2,
        "|" => 3,
        "&" => 4,
        "==" or "!=" or "===" or "!==" => 5,
        "<" or ">" or "<=" or ">=" => 6,
        "<<" or ">>" => 7,
        "+" or "-" => 8,
        "*" or "/" or "%" => 9,
        _ => -1,
    };

    private XNode ParseBinary(int minPrec)
    {
        var left = ParseUnary();
        while (Cur.Type == "punc")
        {
            var op = Cur.Text;
            int prec = Prec(op);
            if (prec < 0 || prec < minPrec) break;
            _p++;
            var right = ParseBinary(prec + 1);
            left = (op == "&&" || op == "||")
                ? new XLogical { Op = op, L = left, R = right }
                : new XBinary { Op = op, L = left, R = right };
        }
        return left;
    }

    private XNode ParseUnary()
    {
        if (IsPunc("!") || IsPunc("-") || IsPunc("+") || IsPunc("~"))
        {
            var op = Next().Text;
            return new XUnary { Op = op, Operand = ParseUnary() };
        }
        if (IsKw("new")) { _p++; return ParseUnary(); }    // ignore 'new'
        if (IsKw("typeof")) { _p++; return new XUnary { Op = "typeof", Operand = ParseUnary() }; }
        return ParsePostfix();
    }

    private XNode ParsePostfix()
    {
        var e = ParsePrimary();
        while (true)
        {
            if (IsPunc("."))
            {
                _p++;
                var name = Cur.Type == "id" ? Next().Text : (Cur.Type == "str" ? Next().Text : "");
                e = new XMember { Obj = e, Name = name };
            }
            else if (IsPunc("["))
            {
                _p++;
                var idx = ParseExpr();
                Eat("]");
                // treat obj[expr] as member with computed name when it is a literal; else best-effort
                e = idx is XLiteral lit ? new XMember { Obj = e, Name = ToStr(lit.Value) } : new XMember { Obj = e, Name = "" };
            }
            else if (IsPunc("("))
            {
                _p++;
                var call = new XCall { Callee = e };
                if (!IsPunc(")"))
                {
                    call.Args.Add(ParseAssign());
                    while (IsPunc(",")) { _p++; call.Args.Add(ParseAssign()); }
                }
                Eat(")");
                e = call;
            }
            else break;
        }
        return e;
    }

    private XNode ParsePrimary()
    {
        if (IsPunc("("))
        {
            _p++;
            var e = ParseExpr();
            Eat(")");
            return e;
        }
        if (Cur.Type == "num") { var v = Next().Value; return new XLiteral { Value = v }; }
        if (Cur.Type == "str") { var v = Next().Value; return new XLiteral { Value = v }; }
        if (IsKw("true")) { _p++; return new XLiteral { Value = true }; }
        if (IsKw("false")) { _p++; return new XLiteral { Value = false }; }
        if (IsKw("null") || IsKw("undefined")) { _p++; return new XLiteral { Value = null }; }
        if (IsKw("this")) { _p++; return new XThis(); }
        if (Cur.Type == "id") return new XIdent { Name = Next().Text };
        // unknown token — consume and yield null so parsing continues
        _p++;
        return new XLiteral { Value = null };
    }

    private static string ToStr(object? v) => v is double d ? d.ToString(CultureInfo.InvariantCulture) : v?.ToString() ?? "";
}

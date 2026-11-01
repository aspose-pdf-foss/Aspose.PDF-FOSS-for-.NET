using System.Globalization;
using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

// Functions, script objects and loops of a RENDER's script run. A form keeps its shared code in
// named script objects under <variables> (function declarations and module variables); an event
// script calls them as Name.f(args), and code inside one calls its neighbours by plain name.
// The strict rule of the render run holds here too: a call the interpreter cannot follow, or a
// value it cannot know, marks the running script and its later writes are dropped.
internal sealed partial class XfaScriptRunner
{
    /// <summary>How deep calls may nest before a call is given up as unknown.</summary>
    private const int MaxCallDepth = 32;
    /// <summary>How many loop turns one render's scripts may take in all; past it a loop is given up.</summary>
    private const int MaxLoopSteps = 100000;

#pragma warning disable S3871 // internal control-flow signals: caught inside the interpreter, never escape
    private sealed class ReturnException : BreakException { public object? Value; }
    private sealed class ContinueException : BreakException { }
#pragma warning restore S3871

    /// <summary>A function value: the declaration and the script object whose names it sees.</summary>
    private sealed class Callable
    {
        public readonly XFunc Function;
        public readonly ScriptObject? Module;
        public Callable(XFunc function, ScriptObject? module) { Function = function; Module = module; }
    }

    /// <summary>A named script object of the form: its module variables and functions, run on first use.</summary>
    private sealed class ScriptObject
    {
        public XmlElement Source = default!;
        public readonly Dictionary<string, object?> Vars = new();
        public bool Loaded;
    }

    /// <summary>The caption of a node as scripts reach it (node.caption.value.text): assigning its text sets the caption.</summary>
    private sealed class CaptionRef
    {
        public readonly XfaNode Node;
        public CaptionRef(XfaNode node) { Node = node; }
    }

    /// <summary>A named value under &lt;variables&gt; (a form variable): scripts read its .value.</summary>
    private sealed class FormVariable { public string Value = ""; }

    private Dictionary<string, object>? _named;
    private ScriptObject? _module;
    private int _callDepth, _loopSteps;
    /// <summary>The node whose script is making the call in progress: a script object first used there runs for it.</summary>
    private XfaNode? _self;
    /// <summary>The page a layout-ready script is being answered for; 0 outside such a run.</summary>
    private int _pageNum;
    /// <summary>The text that stands for the page count until the pages are counted.</summary>
    private string? _countPlaceholder;
    /// <summary>The page count once the pages are counted.</summary>
    private string? _pageCount;

    /// <summary>The nodes whose layout-ready scripts or calculates ask the layout for page numbers, by their
    /// element: the fields whose text differs from page to page.</summary>
    public Dictionary<XmlElement, XfaNode> PageScriptNodes()
    {
        var nodes = new Dictionary<XmlElement, XfaNode>();
        foreach (var n in _model.All)
            if (n.Kind == "field" && (EventScripts(n, "ready", "$layout", paged: true).Any() || PageCalculate(n) is not null)) nodes[n.Template] = n;
        return nodes;
    }

    /// <summary>The field's calculate script when it asks for the page the field lies on (the layout's page
    /// functions, or the page area's own instance index) or for the page count, else null.</summary>
    private static XmlElement? PageCalculate(XfaNode n)
    {
        foreach (XmlNode c in n.Template.ChildNodes)
        {
            if (c is not XmlElement { LocalName: "calculate" } calc) continue;
            foreach (XmlNode s in calc.ChildNodes)
                if (s is XmlElement { LocalName: "script" } script
                    && (script.InnerText.Contains(LayoutObject, StringComparison.Ordinal) || script.InnerText.Contains("$layout.", StringComparison.Ordinal)
                        || script.InnerText.Contains("parent.index", StringComparison.Ordinal)))
                    return script;
        }
        return null;
    }

    /// <summary>Gives the page-dependent fields their values on one page of a counted set, for good, runs the
    /// calculates that read them again and writes every box's presence back onto its element: what the masters
    /// show on that page. The count is known here, so the page functions and the count functions both answer; a
    /// calculate in the form-calculation language is answered by what it asks for (the count, else the page).
    /// Returns the fields whose value the page changed.</summary>
    public List<XfaNode> ResolvePage(IEnumerable<XfaNode> paged, int pageNum, string pageCount)
    {
        var before = new Dictionary<XfaNode, int>();
        foreach (var n in _model.All) before[n] = n.Writes;
        _pageNum = pageNum;
        _pageCount = pageCount;
        try
        {
            foreach (var node in paged)
            {
                foreach (var script in EventScripts(node, "ready", "$layout", paged: true))
                    Exec(GetAst(script), node);
                if (PageCalculate(node) is { } calc && !IsJavaScript(calc))
                {
                    var text = calc.InnerText;
                    bool count = text.Contains("pageCount", StringComparison.Ordinal) || text.Contains("pageSpan", StringComparison.Ordinal);
                    bool fromOne = text.Contains("+ 1", StringComparison.Ordinal) || text.Contains("+1", StringComparison.Ordinal) || text.Contains("page(", StringComparison.Ordinal);
                    node.RawValue = count ? pageCount : (fromOne ? pageNum : pageNum - 1).ToString(CultureInfo.InvariantCulture);
                    node.RawAssigned = true;
                }
            }
            RunCalculatesInternal(null);
        }
        catch { /* a script the runner cannot run leaves the presence as it was */ }
        finally { (_pageNum, _pageCount) = (0, null); }
        var changed = new List<XfaNode>();
        foreach (var n in _model.All)
        {
            if (n.Presence != n.Template.GetAttribute("presence") && (n.Presence != "visible" || n.Template.HasAttribute("presence")))
                n.Template.SetAttribute("presence", n.Presence);
            if (n.Kind == "field" && n.Writes != before[n]) changed.Add(n);
        }
        return changed;
    }

    /// <summary>The value a node's layout-ready scripts give it on one page: the page functions answer
    /// with that page's number, and the count functions with <paramref name="countPlaceholder"/>.
    /// Null when the scripts assign nothing they could resolve. The node itself is left as it was.</summary>
    public string? PageValue(XfaNode node, int pageNum, string countPlaceholder)
    {
        var (value, assigned) = (node.RawValue, node.RawAssigned);
        node.RawAssigned = false;
        _pageNum = pageNum;
        _countPlaceholder = countPlaceholder;
        try
        {
            foreach (var script in EventScripts(node, "ready", "$layout", paged: true))
                Exec(GetAst(script), node);
            return node.RawAssigned ? node.RawValue : null;
        }
        catch { return null; }
        finally
        {
            _pageNum = 0;
            node.RawValue = value;
            node.RawAssigned = assigned;
        }
    }

    /// <summary>What a layout function answers while a page is being resolved, or null when it has no answer.</summary>
    private object? LayoutAnswer(string name) => name switch
    {
        "page" => (double)_pageNum,
        "absPage" or "sheet" => (double)(_pageNum - 1),
        "pageCount" or "absPageCount" or "sheetCount" or "pageSpan" => _pageCount ?? _countPlaceholder,
        _ => null,
    };

    /// <summary>Declares the functions a block declares before any of its statements run.</summary>
    private static void Hoist(List<IXStmt> body, Dictionary<string, object?> scope, ScriptObject? module)
    {
        foreach (var s in body)
            if (s is XFunc { Name.Length: > 0 } f) scope[f.Name] = new Callable(f, module);
    }

    private object? Invoke(Callable callable, List<object?> args, XfaNode self)
    {
        if (_callDepth >= MaxCallDepth) return Unknown();
        var f = callable.Function;
        var locals = new Dictionary<string, object?>();
        for (int i = 0; i < f.Params.Count; i++) locals[f.Params[i]] = i < args.Count ? args[i] : null;
        Hoist(f.Body, locals, callable.Module);
        var outer = _module;
        _module = callable.Module;
        _callDepth++;
        try { ExecBlock(f.Body, self, locals); return null; }
        catch (ReturnException r) { return r.Value; }
        catch (BreakException) { return null; }
        finally { _module = outer; _callDepth--; }
    }

    private void ExecLoop(XFor loop, XfaNode self, Dictionary<string, object?> scope)
    {
        if (loop.Init is not null) ExecStmt(loop.Init, self, scope);
        for (bool first = true; ; first = false)
        {
            bool tested = !(first && loop.BodyFirst) && loop.Test is not null;
            if (tested && !Truthy(EvalDeciding(loop.Test!, self, scope))) break;
            // A loop whose test could not be resolved, or that runs away, is given up.
            if (_tainted || ++_loopSteps > MaxLoopSteps) { _tainted = true; break; }
            try { ExecBlock(loop.Body, self, scope); }
            catch (ContinueException) { /* on to the update and the next turn */ }
            catch (BreakException ex) when (ex is not ReturnException) { break; }
            if (loop.Update is not null) Eval(loop.Update, self, scope);
        }
    }

    /// <summary>A name the running function's script object declares (a module variable or a neighbour function).</summary>
    private bool ModuleMember(string name, out object? value)
    {
        value = null;
        return _module is not null && _module.Vars.TryGetValue(name, out value);
    }

    /// <summary>The script object or form variable of that name, or null. The form's named objects
    /// are indexed once, the first time a script names one.</summary>
    private object? NamedObject(string name)
    {
        if (_named is null)
        {
            _named = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var variables in _root.Template.GetElementsByTagName("variables").OfType<XmlElement>())
                foreach (var v in variables.ChildNodes.OfType<XmlElement>())
                {
                    var key = v.GetAttribute("name");
                    if (key.Length == 0 || _named.ContainsKey(key)) continue;
                    if (v.LocalName != "script") _named[key] = new FormVariable { Value = v.InnerText };
                    else if (IsJavaScript(v)) _named[key] = new ScriptObject { Source = v };
                }
        }
        return _named.TryGetValue(name, out var found) ? found : null;
    }

    /// <summary>Runs a script object's own statements once: its functions are declared and its module variables set.</summary>
    private void Load(ScriptObject module, XfaNode self)
    {
        if (module.Loaded) return;
        module.Loaded = true;
        var ast = GetAst(module.Source);
        Hoist(ast, module.Vars, module);
        var (tainted, unknown, outer) = (_tainted, _unknown, _module);
        _module = module;
        try
        {
            foreach (var s in ast)
            {
                if (s is XFunc) continue;
                try { ExecStmt(s, self, module.Vars); }
                catch { /* a statement of the object that fails leaves its name unset */ }
            }
        }
        finally { (_tainted, _unknown, _module) = (tainted, unknown, outer); }
    }

    private object? EvalIncDec(XIncDec inc, XfaNode self, Dictionary<string, object?> scope)
    {
        var before = Num(Eval(inc.Target, self, scope));
        var after = before + inc.Delta;
        AssignTo(inc.Target, after, self, scope);
        return inc.Prefix ? after : before;
    }

    private object? EvalIndex(XIndex ix, XfaNode self, Dictionary<string, object?> scope)
    {
        var o = Eval(ix.Obj, self, scope);
        var i = Eval(ix.Index, self, scope);
        if (o is List<object?> list && i is double d) return d >= 0 && d < list.Count ? list[(int)d] : null;
        if (o is string s && i is double c) return c >= 0 && c < s.Length ? s[(int)c].ToString() : null;
        if (o is XfaNode && i is string name) return MemberGet(o, name);
        return Unknown();
    }

    /// <summary>Number(x), String(x), parseInt(x), parseFloat(x), isNaN(x), Array(): the few global
    /// functions scripts lean on. False when the call is not one of them (or the name is the script's own).</summary>
    private bool BuiltInCall(XCall c, XfaNode self, Dictionary<string, object?> scope, out object? result)
    {
        result = null;
        if (c.Callee is not XIdent id || scope.ContainsKey(id.Name) || ModuleMember(id.Name, out _)) return false;
        if (id.Name is not ("Number" or "String" or "parseInt" or "parseFloat" or "isNaN" or "Array")) return false;
        var arg = c.Args.Count > 0 ? Eval(c.Args[0], self, scope) : null;
        result = id.Name switch
        {
            "Number" or "parseFloat" => Num(arg),
            "parseInt" => Math.Truncate(Num(arg)),
            "isNaN" => double.IsNaN(Num(arg)),
            "String" => ToStr(arg),
            _ => new List<object?>(),
        };
        return true;
    }

    /// <summary>A member of a plain value: a string's or a list's length and methods, a script object's
    /// name, a form variable's value. False when the value has no such member.</summary>
    private bool ValueMember(object? o, string name, XfaNode self, out object? value)
    {
        value = null;
        switch (o)
        {
            case ScriptObject module:
                Load(module, self);
                if (!module.Vars.TryGetValue(name, out value)) value = Unknown();
                return true;
            case CaptionRef when name is "value" or "text":
                value = o;
                return true;
            case FormVariable variable when name == "value":
                value = variable.Value;
                return true;
            case string s when name == "length":
                value = (double)s.Length;
                return true;
            case List<object?> list when name == "length":
                value = (double)list.Count;
                return true;
            case string when name is "indexOf" or "lastIndexOf" or "substring" or "substr" or "charAt" or "toUpperCase"
                or "toLowerCase" or "toString" or "replace" or "split" or "trim" or "search":
            case XRegex when name == "test":
            case double when name is "toString" or "toFixed":
            case List<object?> when name is "item" or "push" or "join" or "indexOf":
                value = new BoundMethod(o, name);
                return true;
        }
        return false;
    }

    /// <summary>Calls a method of a string, a number or a list (see <see cref="ValueMember"/>).</summary>
    private object? ValueMethod(BoundMethod bm, List<object?> args)
    {
        object? a0 = args.Count > 0 ? args[0] : null, a1 = args.Count > 1 ? args[1] : null;
        if (bm.Target is List<object?> list)
        {
            switch (bm.Name)
            {
                case "item": { var i = Num(a0); return i >= 0 && i < list.Count ? list[(int)i] : null; }
                case "push": list.Add(a0); return (double)list.Count;
                case "join": return string.Join(a0 is string sep ? sep : ",", list.Select(ToStr));
                default: return (double)list.FindIndex(x => StrictEq(x, a0));
            }
        }
        if (bm.Target is XRegex tested)
            return tested.Regex is { } rx ? SafeRegex.IsMatch(rx, ToStr(a0)) : Unknown();
        if (bm.Target is double number)
            return bm.Name == "toFixed" ? number.ToString("F" + (int)Num(a0), CultureInfo.InvariantCulture) : ToStr(number);
        var s = (string)bm.Target!;
        switch (bm.Name)
        {
            case "indexOf": return (double)s.IndexOf(ToStr(a0), StringComparison.Ordinal);
            case "lastIndexOf": return (double)s.LastIndexOf(ToStr(a0), StringComparison.Ordinal);
            case "charAt": { var i = Num(a0); return i >= 0 && i < s.Length ? s[(int)i].ToString() : ""; }
            case "toUpperCase": return s.ToUpperInvariant();
            case "toLowerCase": return s.ToLowerInvariant();
            case "trim": return s.Trim();
            case "toString": return s;
            case "substring":
            case "substr":
                {
                    int from = Clamp(Num(a0), s.Length);
                    int to = a1 is null ? s.Length : Clamp(bm.Name == "substr" ? from + Num(a1) : Num(a1), s.Length);
                    return to > from ? s.Substring(from, to - from) : "";
                }
            case "replace" when a0 is XRegex { Regex: { } pattern } expression && (a1 is string || a1 is null):
                return expression.Global ? SafeRegex.Replace(pattern, s, ToStr(a1)) : SafeRegex.Replace(pattern, s, ToStr(a1), 1);
            case "search" when a0 is XRegex { Regex: { } sought }:
                { var m = SafeRegex.Match(sought, s); return m.Success ? (double)m.Index : -1d; }
            case "replace" when a0 is string find && find.Length > 0:
                { var at = s.IndexOf(find, StringComparison.Ordinal); return at < 0 ? s : s.Substring(0, at) + ToStr(a1) + s.Substring(at + find.Length); }
            case "split" when a0 is string sep && sep.Length > 0:
                return s.Split(new[] { sep }, StringSplitOptions.None).Cast<object?>().ToList();
        }
        return Unknown();
    }

    private static int Clamp(double v, int max) => double.IsNaN(v) || v < 0 ? 0 : v > max ? max : (int)v;

    /// <summary>The operators a render reads differently from the field-set run: two strings compare
    /// as text, and the page-count placeholder takes part in nothing but concatenation. False for
    /// every other case, which the common evaluation decides.</summary>
    private bool RenderBinary(string op, object? l, object? r, out object? result)
    {
        result = null;
        if (_countPlaceholder is { } mark && op != "+"
            && ((l is string ls && ls.Contains(mark, StringComparison.Ordinal)) || (r is string rs && rs.Contains(mark, StringComparison.Ordinal))))
        {
            _unknown = true;
            return true;
        }
        if (l is not string a || r is not string b || op is not ("<" or ">" or "<=" or ">=")) return false;
        int order = string.CompareOrdinal(a, b);
        result = op switch { "<" => order < 0, ">" => order > 0, "<=" => order <= 0, _ => order >= 0 };
        return true;
    }

    /// <summary>resolveNodes: every node a reference reaches - "name[*]" is each child of that name,
    /// any other reference the one node it resolves to.</summary>
    private List<object?> ResolveNodes(XfaNode baseNode, string som)
    {
        const string all = "[*]";
        var found = new List<object?>();
        som = som.Trim();
        if (!som.EndsWith(all, StringComparison.Ordinal))
        {
            if (ResolveNodeForRender(baseNode, som) is XfaNode one) found.Add(one);
            return found;
        }
        var path = som.Substring(0, som.Length - all.Length);
        var dot = path.LastIndexOf('.');
        var parent = dot < 0 ? baseNode : ResolveSom(baseNode, path.Substring(0, dot));
        var name = dot < 0 ? path : path.Substring(dot + 1);
        if (parent is null) { _unknown = true; return found; }
        CollectNamed(parent, name, found);
        return found;
    }

    private static void CollectNamed(XfaNode parent, string name, List<object?> into)
    {
        foreach (var c in parent.Children)
        {
            if (c.Name == name) into.Add(c);
            else if (c.Name.Length == 0) CollectNamed(c, name, into);
        }
    }
}

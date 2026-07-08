using System.Globalization;
using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

/// <summary>
/// Runs the XFA load-time scripts (<c>initialize</c> events then <c>calculate</c> scripts) over an
/// <see cref="XfaFormModel"/>, mutating node presence/access/rawValue, so that after the run a
/// field's <see cref="XfaNode.EffectiveHidden"/> reflects what the XFA engine would render.
/// Interaction/host scripts (<c>ref="$host"</c>: docReady / click / prePrint …) are skipped —
/// they do not fire when a form is flattened server-side. Calculates are run to a fixpoint (a few
/// passes) so ordering dependencies between them resolve without an explicit dependency graph.
/// </summary>
internal sealed class XfaScriptRunner
{
    private readonly XfaFormModel _model;
    private readonly XfaNode _root;
    private readonly Dictionary<XmlElement, List<XStmt>> _astCache = new();

    private XfaScriptRunner(XfaFormModel model) { _model = model; _root = model.Root; }

    public static void Run(XfaFormModel model)
    {
        try { new XfaScriptRunner(model).RunInternal(); }
        catch { /* tolerant: a script we cannot handle must never break flatten */ }
    }

    /// <summary>Re-run only the calculate scripts (dependency-triggered until settled). Used by the
    /// pagination stage to re-evaluate page-context-dependent presence (e.g. a last-page-only footer
    /// block) after the page-count fields are set for a given physical page. Calculates owned by a
    /// node whose name is in <paramref name="pinnedOwners"/> are skipped, so caller-injected values
    /// (the page-count fields, whose real calculates need a full layout engine) are not overwritten.</summary>
    public static void RunCalculates(XfaFormModel model, HashSet<string>? pinnedOwners = null)
    {
        try { new XfaScriptRunner(model).RunCalculatesInternal(pinnedOwners); }
        catch { }
    }

    // Each calculate execution records the rawValues it READ; it is only re-run when one of those
    // inputs changes. This mirrors XFA's dependency-triggered recalculation: an unconditional
    // "block control" calculate that reads only its own constant value runs once, while a
    // page-control calculate that reads a value another calculate computes re-runs after that value
    // settles — so the dependent (later) write wins, matching the engine's calculation waves.
    private List<(XfaNode node, string? val)>? _reads;

    private void RunInternal()
    {
        // 1) initialize events, document order (run once).
        foreach (var n in _model.All)
            foreach (var ast in Scripts(n, initialize: true))
                Exec(ast, n);

        // 2) calculate scripts, dependency-triggered until settled.
        RunCalculatesInternal(null);
    }

    private void RunCalculatesInternal(HashSet<string>? pinnedOwners)
    {
        var calcs = new List<(XfaNode node, List<XStmt> ast)>();
        foreach (var n in _model.All)
        {
            if (pinnedOwners is not null && pinnedOwners.Contains(n.Name)) continue;
            foreach (var ast in Scripts(n, initialize: false))
                calcs.Add((n, ast));
        }
        var lastReads = new Dictionary<int, List<(XfaNode, string?)>>();

        for (int pass = 0; pass < 24; pass++)
        {
            bool anyRan = false;
            for (int i = 0; i < calcs.Count; i++)
            {
                if (lastReads.TryGetValue(i, out var prev) && !ReadsChanged(prev)) continue;
                _reads = new List<(XfaNode, string?)>();
                Exec(calcs[i].ast, calcs[i].node);
                lastReads[i] = _reads;
                _reads = null;
                anyRan = true;
            }
            if (!anyRan) break;
        }
    }

    private static bool ReadsChanged(List<(XfaNode node, string? val)> prev)
    {
        foreach (var (node, val) in prev)
            if (node.RawValue != val) return true;
        return false;
    }

    /// <summary>Parsed scripts of a node for the requested phase (cached).</summary>
    private IEnumerable<List<XStmt>> Scripts(XfaNode node, bool initialize)
    {
        foreach (XmlNode c in node.Template.ChildNodes)
        {
            if (c.NodeType != XmlNodeType.Element) continue;
            var local = c.LocalName;
            bool match;
            if (initialize)
                match = local == "event" && Attr(c, "activity") == "initialize" && Attr(c, "ref") != "$host";
            else
                match = local == "calculate"
                     || (local == "event" && Attr(c, "activity") == "calculate" && Attr(c, "ref") != "$host");
            if (!match) continue;
            foreach (XmlNode sc in c.ChildNodes)
            {
                if (sc.NodeType != XmlNodeType.Element || sc.LocalName != "script") continue;
                if (sc is XmlElement se) yield return GetAst(se);
            }
        }
    }

    private List<XStmt> GetAst(XmlElement scriptEl)
    {
        if (_astCache.TryGetValue(scriptEl, out var cached)) return cached;
        List<XStmt> ast;
        try { ast = new XParser(new XLexer(scriptEl.InnerText).Tokenize()).ParseProgram(); }
        catch { ast = new List<XStmt>(); }
        _astCache[scriptEl] = ast;
        return ast;
    }

    private static string Attr(XmlNode n, string name) => n.Attributes?[name]?.Value ?? "";

    // -----------------------------------------------------------------------------------------
    // Statement execution
    // -----------------------------------------------------------------------------------------
    private sealed class BreakSignal : Exception { }

    private void Exec(List<XStmt> body, XfaNode self)
    {
        var scope = new Dictionary<string, object?>();
        foreach (var s in body)
        {
            try { ExecStmt(s, self, scope); }
            catch (BreakSignal) { break; }
            catch { /* skip a failing statement, keep going */ }
        }
    }

    private void ExecBlock(List<XStmt> body, XfaNode self, Dictionary<string, object?> scope)
    {
        foreach (var s in body)
        {
            try { ExecStmt(s, self, scope); }
            catch (BreakSignal) { throw; }
            catch { }
        }
    }

    private void ExecStmt(XStmt s, XfaNode self, Dictionary<string, object?> scope)
    {
        switch (s)
        {
            case XNop: break;
            case XExprStmt es: Eval(es.Expr, self, scope); break;
            case XVar v: scope[v.Name] = v.Init is null ? null : Eval(v.Init, self, scope); break;
            case XBlock b: ExecBlock(b.Body, self, scope); break;
            case XBreak: throw new BreakSignal();
            case XIf i:
                if (Truthy(Eval(i.Test, self, scope))) ExecBlock(i.Then, self, scope);
                else ExecBlock(i.Else, self, scope);
                break;
            case XSwitch sw:
                {
                    var d = Eval(sw.Disc, self, scope);
                    bool matched = false;
                    try
                    {
                        foreach (var (test, cbody) in sw.Cases)
                        {
                            if (!matched && test is not null && LooseEq(d, Eval(test, self, scope))) matched = true;
                            if (!matched && test is null) matched = true;   // default
                            if (matched) ExecBlock(cbody, self, scope);
                        }
                    }
                    catch (BreakSignal) { }
                    break;
                }
        }
    }

    // -----------------------------------------------------------------------------------------
    // Expression evaluation (tolerant; unknown → null)
    // -----------------------------------------------------------------------------------------
    private static readonly object XfaGlobal = new();       // the `xfa` object
    private static readonly object XfaLayout = new();       // `xfa.layout`
    private sealed class InstanceMgr { public XfaNode Node = default!; }

    private object? Eval(XNode e, XfaNode self, Dictionary<string, object?> scope)
    {
        switch (e)
        {
            case XLiteral l: return l.Value;
            case XThis: return self;
            case XIdent id: return EvalIdent(id.Name, self, scope);
            case XMember m: return EvalMember(m, self, scope);
            case XCall c: return EvalCall(c, self, scope);
            case XUnary u: return EvalUnary(u, self, scope);
            case XCond c: return Truthy(Eval(c.Test, self, scope)) ? Eval(c.Then, self, scope) : Eval(c.Else, self, scope);
            case XLogical lo:
                {
                    var l = Eval(lo.L, self, scope);
                    if (lo.Op == "&&") return Truthy(l) ? Eval(lo.R, self, scope) : l;
                    return Truthy(l) ? l : Eval(lo.R, self, scope);
                }
            case XBinary b: return EvalBinary(b, self, scope);
            case XAssign a: return EvalAssign(a, self, scope);
        }
        return null;
    }

    private object? EvalIdent(string name, XfaNode self, Dictionary<string, object?> scope)
    {
        if (name == "xfa") return XfaGlobal;
        if (name == "$" || name == "$form" || name == "$record") return _root;
        if (scope.TryGetValue(name, out var v)) return v;               // script-local var wins
        if (name == _root.Name) return _root;
        // XFA unqualified reference: resolve the bare name as a SOM node by searching from `self`
        // up its ancestors (descendant search at each level). This is how e.g. a footer script
        // reads `MyPresentPageCount.rawValue` — a field declared higher in the form.
        return ResolveUnqualified(self, name);
    }

    /// <summary>Resolve an unqualified SOM name from a context node (walk up ancestors, descendant
    /// search at each level). Returns null if nothing matches (kept tolerant — unknown globals like
    /// script-object names simply stay null).</summary>
    private static XfaNode? ResolveUnqualified(XfaNode from, string name)
    {
        for (var ctx = from; ctx is not null; ctx = ctx.Parent)
        {
            var direct = ctx.Child(name);
            if (direct is not null) return direct;
            var d = FindDescendant(ctx, name);
            if (d is not null) return d;
        }
        return null;
    }

    private object? EvalMember(XMember m, XfaNode self, Dictionary<string, object?> scope)
    {
        var o = Eval(m.Obj, self, scope);
        return MemberGet(o, m.Name);
    }

    private object? MemberGet(object? o, string name)
    {
        if (o is XfaNode n)
        {
            switch (name)
            {
                case "parent": return n.Parent;
                case "presence": return n.Presence;
                case "access": return n.Access;
                case "rawValue":
                case "value": _reads?.Add((n, n.RawValue)); return n.RawValue;
                case "name":
                case "somExpression": return n.Name;
                case "instanceManager": return new InstanceMgr { Node = n };
                case "resolveNode": return new BoundMethod(n, "resolveNode");
                default: return n.Child(name);           // child navigation (null if none)
            }
        }
        if (ReferenceEquals(o, XfaGlobal))
        {
            switch (name)
            {
                case "form":
                case "record":
                case "data":
                case "datasets": return _root;
                case "layout": return XfaLayout;
                case "resolveNode": return new BoundMethod(XfaGlobal, "resolveNode");
                default: return null;
            }
        }
        if (o is InstanceMgr im)
            return name == "count" ? (double)Occurrences(im.Node) : null;
        if (ReferenceEquals(o, XfaLayout))
            return new BoundMethod(XfaLayout, name);          // page/pageCount/… → callable
        return null;                                          // null / unknown → tolerant
    }

    private sealed class BoundMethod { public object? Target; public string Name; public BoundMethod(object? t, string n) { Target = t; Name = n; } }

    private object? EvalCall(XCall c, XfaNode self, Dictionary<string, object?> scope)
    {
        // Evaluate callee (may be a BoundMethod) and args.
        var callee = Eval(c.Callee, self, scope);
        var args = new List<object?>(c.Args.Count);
        foreach (var a in c.Args) args.Add(Eval(a, self, scope));

        if (callee is BoundMethod bm)
        {
            if (bm.Name == "resolveNode")
            {
                var som = args.Count > 0 ? ToStr(args[0]) : "";
                var baseNode = bm.Target as XfaNode ?? _root;
                return ResolveSom(baseNode, som);
            }
            if (bm.Name is "page" or "pageCount" or "absPage" or "sheet" or "pageCountInBatch")
                return 1d;                                    // single-context layout stub
            // instanceManager mutators / unknown layout calls → no-op
            return null;
        }
        // bare / unknown function (script objects like GlobalVariableDefinition.CallCommonVariables) → no-op
        return null;
    }

    private object? EvalUnary(XUnary u, XfaNode self, Dictionary<string, object?> scope)
    {
        if (u.Op == "typeof") { var v = Eval(u.Operand, self, scope); return TypeOf(v); }
        var x = Eval(u.Operand, self, scope);
        return u.Op switch
        {
            "!" => !Truthy(x),
            "-" => -Num(x),
            "+" => Num(x),
            "~" => (double)(~(long)Num(x)),
            _ => null,
        };
    }

    private object? EvalBinary(XBinary b, XfaNode self, Dictionary<string, object?> scope)
    {
        var l = Eval(b.L, self, scope);
        var r = Eval(b.R, self, scope);
        switch (b.Op)
        {
            case "==": return LooseEq(l, r);
            case "!=": return !LooseEq(l, r);
            case "===": return StrictEq(l, r);
            case "!==": return !StrictEq(l, r);
            case "<": return Num(l) < Num(r);
            case ">": return Num(l) > Num(r);
            case "<=": return Num(l) <= Num(r);
            case ">=": return Num(l) >= Num(r);
            case "+":
                if (l is string || r is string) return ToStr(l) + ToStr(r);
                if (l is null && r is null) return ToStr(l) + ToStr(r);
                return Num(l) + Num(r);
            case "-": return Num(l) - Num(r);
            case "*": return Num(l) * Num(r);
            case "/": return Num(l) / Num(r);
            case "%": return Num(l) % Num(r);
            case "&": return (double)((long)Num(l) & (long)Num(r));
            case "|": return (double)((long)Num(l) | (long)Num(r));
        }
        return null;
    }

    private object? EvalAssign(XAssign a, XfaNode self, Dictionary<string, object?> scope)
    {
        var val = Eval(a.Value, self, scope);
        AssignTo(a.Target, val, self, scope);
        return val;
    }

    private void AssignTo(XNode target, object? val, XfaNode self, Dictionary<string, object?> scope)
    {
        if (target is XIdent id) { scope[id.Name] = val; return; }
        if (target is XMember m)
        {
            var o = Eval(m.Obj, self, scope);
            if (o is XfaNode n)
            {
                switch (m.Name)
                {
                    case "presence": n.Presence = ToStr(val); return;
                    case "access": n.Access = ToStr(val); return;
                    case "rawValue":
                    case "value": n.RawValue = Nullify(ToStr(val)); return;
                    default:
                        // `parent.child = value` shorthand → set the child field's rawValue
                        var child = n.Child(m.Name);
                        if (child is not null) child.RawValue = Nullify(ToStr(val));
                        return;
                }
            }
            // assigning to a non-node member (caption.value.#text, unresolved node …) → no-op
        }
    }

    // -----------------------------------------------------------------------------------------
    // SOM resolution for resolveNode("…")
    // -----------------------------------------------------------------------------------------
    private XfaNode? ResolveSom(XfaNode baseNode, string som)
    {
        if (string.IsNullOrEmpty(som)) return baseNode;
        som = som.Trim();
        // Strip leading engine roots; they all mean "the form root".
        foreach (var prefix in new[] { "xfa.form.", "xfa.record.", "xfa.datasets.", "xfa.data.", "$form.", "$record.", "$." })
            if (som.StartsWith(prefix, StringComparison.Ordinal)) { som = som.Substring(prefix.Length); baseNode = _root; break; }
        if (som is "xfa.form" or "xfa.record" or "$form" or "$record") return _root;

        var cur = baseNode;
        int i = 0;
        while (i < som.Length && cur is not null)
        {
            bool descendant = false;
            if (som[i] == '.') { descendant = true; i++; }        // '..' → descendant axis
            int start = i;
            while (i < som.Length && som[i] != '.' && som[i] != '[') i++;
            var seg = som.Substring(start, i - start);
            // skip an index [n]
            if (i < som.Length && som[i] == '[') { while (i < som.Length && som[i] != ']') i++; if (i < som.Length) i++; }
            if (i < som.Length && som[i] == '.') i++;
            if (seg.Length == 0) continue;
            if (seg == _root.Name && cur == baseNode && !descendant) { continue; }   // leading root name
            cur = descendant ? FindDescendant(cur, seg) : (cur.Child(seg) ?? FindDescendant(cur, seg));
            // Non-node SOM tails (value, #text, caption) resolve to null → tolerated by callers.
        }
        return cur;
    }

    private static XfaNode? FindDescendant(XfaNode from, string name)
    {
        foreach (var c in from.Children)
        {
            if (c.Name == name) return c;
            var d = FindDescendant(c, name);
            if (d is not null) return d;
        }
        return null;
    }

    /// <summary>Occurrence count for instanceManager.count — the number of same-named sibling
    /// instances currently in the model (P2: static structure ⇒ 1 for a present subform).</summary>
    private static int Occurrences(XfaNode node)
    {
        if (node.Parent is null) return 1;
        int n = 0;
        foreach (var s in node.Parent.Children) if (s.Name == node.Name) n++;
        return n;
    }

    // -----------------------------------------------------------------------------------------
    // JS value coercion helpers
    // -----------------------------------------------------------------------------------------
    private static string? Nullify(string? v) => string.IsNullOrEmpty(v) ? null : v;

    private static bool Truthy(object? v) => v switch
    {
        null => false,
        bool b => b,
        double d => d != 0 && !double.IsNaN(d),
        string s => s.Length > 0,
        _ => true,
    };

    private static double Num(object? v) => v switch
    {
        null => 0,
        double d => d,
        bool b => b ? 1 : 0,
        string s => double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : double.NaN,
        _ => double.NaN,
    };

    private static string ToStr(object? v) => v switch
    {
        null => "",
        double d => d == Math.Floor(d) && !double.IsInfinity(d) ? ((long)d).ToString(CultureInfo.InvariantCulture) : d.ToString(CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        string s => s,
        XfaNode n => n.RawValue ?? "",
        _ => "",
    };

    private static string TypeOf(object? v) => v switch
    {
        null => "undefined",
        bool => "boolean",
        double => "number",
        string => "string",
        _ => "object",
    };

    private static bool IsNullish(object? v) => v is null;

    private static bool LooseEq(object? a, object? b)
    {
        if (IsNullish(a) && IsNullish(b)) return true;
        if (IsNullish(a) || IsNullish(b)) return false;
        if (a is double || b is double || a is bool || b is bool) return Num(a) == Num(b);
        return ToStr(a) == ToStr(b);
    }

    private static bool StrictEq(object? a, object? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        if (a is string sa && b is string sb) return sa == sb;
        if (a is double da && b is double db) return da == db;
        if (a is bool ba && b is bool bb) return ba == bb;
        if (a is XfaNode na && b is XfaNode nb) return ReferenceEquals(na, nb);
        return false;
    }
}

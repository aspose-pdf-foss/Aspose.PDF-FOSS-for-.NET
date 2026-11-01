using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

// The script run of a RENDER. It fires more events than the field-set run (the form-ready,
// layout-ready and document-ready scripts decide what a viewer shows when it opens the form) and
// it is strict where that run is tolerant: a script that reads something the interpreter cannot
// resolve - an unknown name, the result of a function it does not have, a loop it does not run -
// no longer changes presence or values, because a guess would hide or show the wrong content.
internal sealed partial class XfaScriptRunner
{
    private const string JavaScriptType = "application/x-javascript";
    private const string ExecEventMethod = "execEvent";
    private const string LayoutObject = "xfa.layout";

    private bool _render;
    /// <summary>The running script read something unresolved: its later writes are dropped.</summary>
    private bool _tainted;
    /// <summary>The expression being evaluated met something unresolved.</summary>
    private bool _unknown;

    /// <summary>The events a conversion fires: initialize, calculate, form ready and layout ready (measured on
    /// a footer a layout-ready script hides and a section its own form-ready script hides). The document-ready,
    /// click and print events are a viewer's and are not fired; the layout-ready scripts that ask for page
    /// numbers are answered page by page as the fields are painted.</summary>
    private void RunRenderInternal()
    {
        foreach (var n in _model.All)
            foreach (var ast in Scripts(n, initialize: true))
                Exec(ast, n);
        RunCalculatesInternal(null);
        RunEvents("ready", "$form");
        RunEvents("ready", "$layout");
    }

    private void RunEvents(string activity, string reference)
    {
        foreach (var n in _model.All)
            foreach (var script in EventScripts(n, activity, reference))
                Exec(GetAst(script), n);
    }

    /// <summary>The scripts of a node's events of one activity; <paramref name="reference"/> null takes
    /// every event object. A layout-ready script that asks the layout for page numbers is left out:
    /// its answer differs from page to page, and the page-number fields have their own path.</summary>
    private static IEnumerable<XmlElement> EventScripts(XfaNode node, string activity, string? reference, bool paged = false)
    {
        foreach (XmlNode c in node.Template.ChildNodes)
        {
            if (c is not XmlElement ev || ev.LocalName != "event" || ev.GetAttribute("activity") != activity) continue;
            var target = ev.GetAttribute("ref");
            if (reference is not null && (target.Length == 0 ? "$" : target) != reference) continue;
            foreach (XmlNode sc in ev.ChildNodes)
            {
                if (sc is not XmlElement script || script.LocalName != "script" || !IsJavaScript(script)) continue;
                if ((target == "$layout" && script.InnerText.Contains(LayoutObject, StringComparison.Ordinal)) != paged) continue;
                yield return script;
            }
        }
    }

    /// <summary>The interpreter reads one language; a script in the form-calculation language is not for it.</summary>
    private static bool IsJavaScript(XmlElement script) => script.GetAttribute("contentType") == JavaScriptType;

    private void ExecTry(XTry t, XfaNode self, Dictionary<string, object?> scope)
    {
        // The field-set run keeps what it always did with a try statement (it stepped over the guarded
        // block and ran the handler); a render runs the guarded block, which is the script's intent.
        ExecBlock(_render ? t.Body : t.Catch, self, scope);
        ExecBlock(t.Finally, self, scope);
    }

    private object? EvalDeciding(IXNode e, XfaNode self, Dictionary<string, object?> scope)
    {
        _unknown = false;
        var v = Eval(e, self, scope);
        NoteUnknown();
        return v;
    }

    private void NoteUnknown()
    {
        if (_render && _unknown) _tainted = true;
    }

    private object? Unknown()
    {
        _unknown = true;
        return null;
    }

    /// <summary>A member of a node the plain cases do not name: a child, the instance manager of a
    /// child (_name), the locale in force, or the method that fires an event.</summary>
    private object? RenderMember(XfaNode n, string name)
    {
        if (name == ExecEventMethod) return new BoundMethod(n, ExecEventMethod);
        if (name == "locale") return LocaleOf(n) ?? Unknown();
        switch (name)
        {
            case "isNull": _reads?.Add((n, n.Writes)); return n.RawValue is null;
            case "formattedValue": _reads?.Add((n, n.Writes)); return n.RawValue ?? "";
            case "resolveNodes": return new BoundMethod(n, name);
            case "caption": return new CaptionRef(n);
            case "className": return n.Kind;
            case "index": return (double)SameNamedSiblings(n).IndexOf(n);
            case "all": return SameNamedSiblings(n);
        }
        return n.Child(name) ?? InstanceManagerOf(n, name, upward: false) ?? Unknown();
    }

    /// <summary>A node of the data the form is merged with, as xfa.data hands it to a script.</summary>
    private sealed class DataRef(XmlElement element)
    {
        public XmlElement Element { get; } = element;
    }

    /// <summary>A member of a data node: the value of a node that holds text, its name, its parent, or its first
    /// child of that name. Anything else - the value of a group, a child the data does not have - is unresolved,
    /// and the script that reads it decides nothing.</summary>
    private object? DataMember(DataRef node, string name)
    {
        var e = node.Element;
        switch (name)
        {
            case "value":
            case "rawValue":
                return e.ChildNodes.OfType<XmlElement>().Any() ? Unknown() : e.InnerText;
            case "isNull": return e.InnerText.Length == 0;
            case "name": return e.LocalName;
            case "parent": return e.ParentNode is XmlElement parent ? new DataRef(parent) : Unknown();
        }
        foreach (XmlNode c in e.ChildNodes)
            if (c is XmlElement child && child.LocalName == name) return new DataRef(child);
        return Unknown();
    }

    private static List<object?> SameNamedSiblings(XfaNode n)
    {
        var all = new List<object?>();
        if (n.Parent is null) all.Add(n);
        else CollectNamed(n.Parent, n.Name, all);
        return all;
    }

    private static object? InstanceManagerOf(XfaNode from, string name, bool upward)
    {
        if (name.Length < 2 || name[0] != '_') return null;
        var managed = upward ? ResolveUnqualified(from, name.Substring(1)) : from.Child(name.Substring(1));
        return managed is null ? null : new InstanceMgr { Node = managed };
    }

    private static string? LocaleOf(XfaNode n)
    {
        for (var a = n; a is not null; a = a.Parent)
            if (a.Template.GetAttribute("locale") is { Length: > 0 } locale) return locale;
        return null;
    }

    /// <summary>resolveNode as a render needs it: "$" is the node itself, and a bare name the node does
    /// not hold is looked for in the containers around it, the way a reference in a script is.</summary>
    private object? ResolveNodeForRender(XfaNode baseNode, string som)
    {
        som = som.Trim();
        if (som == "$") return baseNode;
        var found = ResolveSom(baseNode, som);
        if (found is null && som.IndexOfAny(new[] { '.', '[', '#' }) < 0) found = ResolveUnqualified(baseNode, som);
        return found ?? Unknown();
    }
}

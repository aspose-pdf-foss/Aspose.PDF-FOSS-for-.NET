using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

internal static partial class XfaRenderer
{
    // The parts of a bind reference the occurrence expansion reads: "$." starts at the data
    // node of the enclosing subform, as a bare path does; "$record." starts at the record;
    // any other "$name" or "!name" root is not a data path read here. "[*]" takes every
    // same-named node instead of one.
    private const string CurrentNodePrefix = "$.";
    private const string RecordPrefix = "$record.";
    private const string AllOccurrences = "[*]";
    private const char ObjectRootMark = '$';
    private const char DatasetsRootMark = '!';

    /// <summary>The path a reference follows from the data node of the enclosing subform
    /// ("$.a.b" or a bare "a.b"), or null for a reference rooted elsewhere.</summary>
    private static string? RelativeRefPath(string reference)
    {
        if (reference.StartsWith(CurrentNodePrefix, StringComparison.Ordinal))
            return reference.Substring(CurrentNodePrefix.Length);
        if (reference.Length == 0 || reference[0] == ObjectRootMark || reference[0] == DatasetsRootMark) return null;
        return reference;
    }

    // Without a saved form state a repeatable subform is instantiated per data group only when
    // the data really repeats; one group binds through the single template instance as before.
    private const int MinGroupsToRepeat = 2;

    /// <summary>The bind reference of a subform or field (<c>bind match="dataRef"</c>), or
    /// the empty string.</summary>
    private static string DataRefOf(XmlElement e)
    {
        var bind = e.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == "bind");
        return bind is not null && bind.GetAttribute("match") == "dataRef" ? bind.GetAttribute("ref") : string.Empty;
    }

    /// <summary>The unconsumed data groups a subform's own bind reference names, for a subform
    /// whose name the data does not repeat (a row "Row" bound to "$.MovieDetails[*]").</summary>
    private static List<XmlElement> ReferencedGroups(OccurrenceExpandState ox, XmlElement sub, XmlElement? scope)
    {
        var reference = DataRefOf(sub);
        XmlElement? start = null;
        var path = string.Empty;
        if (reference.StartsWith(RecordPrefix, StringComparison.Ordinal))
        {
            start = ox.dataRoot;
            path = reference.Substring(RecordPrefix.Length);
        }
        else if (RelativeRefPath(reference) is { } relative)
        {
            start = scope;
            path = relative;
        }
        if (start is null || path.Length == 0) return new List<XmlElement>();
        return ReferencedNodes(start, path).Where(c => OccIsGroup(c) && !ox.used.Contains(c)).ToList();
    }

    /// <summary>The data nodes a dotted reference path reaches from <paramref name="start"/>:
    /// each segment is a child name, alone or as <c>name[n]</c> or <c>name[*]</c>.</summary>
    private static List<XmlElement> ReferencedNodes(XmlElement start, string path)
    {
        var current = new List<XmlElement> { start };
        foreach (var segment in path.Split('.'))
        {
            var bracket = segment.IndexOf('[');
            var name = bracket < 0 ? segment : segment.Substring(0, bracket);
            var all = segment.EndsWith(AllOccurrences, StringComparison.Ordinal);
            var index = 0;
            if (bracket >= 0 && !all
                && !int.TryParse(segment.Substring(bracket + 1).TrimEnd(']'), out index))
                return new List<XmlElement>();
            var next = new List<XmlElement>();
            foreach (var node in current)
            {
                var named = node.ChildNodes.OfType<XmlElement>().Where(c => c.LocalName == name).ToList();
                if (all) next.AddRange(named);
                else if (index < named.Count) next.Add(named[index]);
            }
            if (next.Count == 0) return next;
            current = next;
        }
        return current;
    }

    /// <summary>The value a field's own relative bind reference reaches inside the data group
    /// of the instance that holds it, or null. The reference starts at that group when only
    /// containers that take no data lie between the field and the instance; a reference
    /// that crosses every group of the instance's own name ("$.Entry[*].Party.Name" inside
    /// an instance of "Entry") continues inside this instance's group. With no instance
    /// (<paramref name="instance"/> null) the group is the record and the containers lie
    /// between the field and the form's root subform, a master page among them.</summary>
    private static string? ReferencedValue(XmlElement field, XmlElement? instance, XmlElement group)
    {
        if (RelativeRefPath(DataRefOf(field)) is not { } path) return null;
        for (var a = field.ParentNode as XmlElement; a is not null && !ReferenceEquals(a, instance);
             a = a.ParentNode as XmlElement)
        {
            if (a.LocalName is "pageArea" or "pageSet") continue;
            // The root subform is the record's own: the walk to the record ends there.
            if (instance is null && a.ParentNode is not XmlElement { LocalName: "subform" or "pageSet" or "pageArea" or "area" or "exclGroup" }) break;
            if (a.GetAttribute("name").Length > 0 && !BindsNoData(a)) return null;
        }
        // An instance that takes no data was only paired with the group by its name: the
        // field's reference starts above the group and names it first, as one or as every.
        if (instance is not null && BindsNoData(instance))
        {
            var ownGroups = group.LocalName + AllOccurrences + ".";
            var ownGroup = group.LocalName + ".";
            if (path.StartsWith(ownGroups, StringComparison.Ordinal)) path = path.Substring(ownGroups.Length);
            else if (path.StartsWith(ownGroup, StringComparison.Ordinal)) path = path.Substring(ownGroup.Length);
        }
        if (path.Length == 0) return null;
        var nodes = ReferencedNodes(group, path);
        return nodes.Count == 1 && !nodes[0].ChildNodes.OfType<XmlElement>().Any() ? nodes[0].InnerText : null;
    }

    /// <summary>Moves the members of every ordered subform set among the container's children
    /// into the container, in the set's place. A set is not a box of its own: its members
    /// flow, bind and repeat as children of the container that holds the set - as a box it
    /// was neither a body of the form nor walked for occurrences, and what it held was lost.
    /// A set that offers a choice between its members stays as it is.</summary>
    private static void HoistSubformSets(XmlElement container)
    {
        static XmlElement? FirstOrderedSet(XmlElement c) => c.ChildNodes.OfType<XmlElement>()
            .FirstOrDefault(k => k.LocalName == "subformSet" && k.GetAttribute("relation") is "" or "ordered");
        for (var set = FirstOrderedSet(container); set is not null; set = FirstOrderedSet(container))
        {
            var members = set.ChildNodes.OfType<XmlElement>()
                .Where(k => k.LocalName is "subform" or "subformSet" or "instanceManager").ToList();
            // The page the set asks to start on is where its first member starts.
            var setBreak = set.ChildNodes.OfType<XmlElement>().FirstOrDefault(k => k.LocalName == "breakBefore");
            var first = members.FirstOrDefault(k => k.LocalName != "instanceManager");
            if (setBreak is not null && first is not null
                && !first.ChildNodes.OfType<XmlElement>().Any(k => k.LocalName == "breakBefore"))
                first.AppendChild(setBreak);
            foreach (var member in members) container.InsertBefore(member, set);
            container.RemoveChild(set);
        }
    }

    /// <summary>Hoists the subform sets of a container and of every subform under it.</summary>
    private static void HoistSubformSetsUnder(XmlElement container)
    {
        HoistSubformSets(container);
        foreach (var sub in container.ChildNodes.OfType<XmlElement>().Where(k => k.LocalName == "subform").ToList())
            HoistSubformSetsUnder(sub);
    }

    /// <summary>True for a subform that takes no data node of its own: it declares so, or
    /// it has neither a name nor a reference to bind by.</summary>
    private static bool TakesNoData(XmlElement sub) =>
        BindsNoData(sub) || (sub.GetAttribute("name").Length == 0 && DataRefOf(sub).Length == 0);

    /// <summary>The expansion of one subform of a form that carries no saved form state.
    /// Only a repeatable subform whose data holds several groups is instantiated per group
    /// and marked; any other subform keeps its one unmarked instance, bound as before, and
    /// only moves the data scope along for the subforms inside it.</summary>
    private static bool ExpandUnsavedSubform(OccurrenceExpandState ox, XmlElement e, XmlElement? scope, XmlElement sub)
    {
        bool repeats = ox.max < 0 || ox.max > 1;
        if (!repeats || ox.avail.Count < MinGroupsToRepeat)
        {
            var inner = scope;
            if (ox.avail.Count > 0)
            {
                inner = ox.avail[0];
                ox.used.Add(inner);
            }
            OccWalk(ox, sub, inner, null);
            return true;
        }
        ox.n = ox.max < 0 ? ox.avail.Count : Math.Min(ox.avail.Count, ox.max);
        ox.fInst = new List<XmlElement>();
        ox.packetAuthoritative = false;
        CloneSubformInstances(ox, e, sub);
        ox.boundDepth++;
        BindSubformInstances(ox, scope);
        ox.boundDepth--;
        return true;
    }
}

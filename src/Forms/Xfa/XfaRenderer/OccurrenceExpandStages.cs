using System.Globalization;
using System.Text;
using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

internal static partial class XfaRenderer
{
// The stages of the occurrence expansion: the group test and the recursive subform walk.
    private static void OccWalk(OccurrenceExpandState ox, XmlElement e, XmlElement? scope, XmlElement? fScope)
    {
        HoistSubformSets(e);
        if (fScope is not null) HoistSubformSets(fScope);
#pragma warning disable S3267 // a step loop: each call does the work and returns false to stop - as a LINQ predicate the side effects would hide in a query
        foreach (var sub in e.ChildNodes.OfType<XmlElement>().Where(c => c.LocalName == "subform").ToList())
#pragma warning restore S3267
        {
            if (!ExpandSubformOccurrence(ox, e, scope, fScope, sub)) break;
        }
    }
    private static bool OccIsGroup(XmlElement el) =>
        el.ChildNodes.OfType<XmlElement>().Any()
#pragma warning disable S1075 // an XML namespace name, an identifier and not a location
        || el.GetAttribute("dataNode", "http://www.xfa.org/schema/xfa-data/1.0/") == "dataGroup";
#pragma warning restore S1075

    /// <summary></summary>
    private static bool ExpandSubformOccurrence(OccurrenceExpandState ox, XmlElement e, XmlElement? scope, XmlElement? fScope, XmlElement sub)
    {
        ox.name = sub.GetAttribute("name");
        ox.occ = sub.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == "occur");
        ox.max = 1;
        ox.min = 1;
        if (ox.occ is not null)
        {
            var maxs = ox.occ.GetAttribute("max"); var mins = ox.occ.GetAttribute("min");
            if (maxs.Length > 0 && int.TryParse(maxs, NumberStyles.Integer, CultureInfo.InvariantCulture, out var m)) ox.max = m;
            if (mins.Length > 0 && int.TryParse(mins, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mi)) ox.min = mi;
            // occur with only a min (e.g. min="5"): the subform still repeats
            // at least min times — an absent max never caps below it.
            if (ox.max >= 0 && ox.max < ox.min) ox.max = ox.min;
        }
        ox.avail = ox.name.Length > 0 && scope is not null
            ? scope.ChildNodes.OfType<XmlElement>()
                .Where(c => c.LocalName == ox.name && OccIsGroup(c) && !ox.used.Contains(c)).ToList()
            : new List<XmlElement>();
        ox.deepMatched = false;
        // Without a saved form state the lenient searches stay off outside an expanded
        // instance: a group is the subform's by name in its own scope, or by its reference.
        bool unsaved = ox.strict && ox.boundDepth == 0;
        if (!unsaved) FindDeepDataMatches(ox, scope);
        if (ox.avail.Count == 0) ox.avail = ReferencedGroups(ox, sub, scope);
        if (unsaved) return ExpandUnsavedSubform(ox, e, scope, sub);
        ox.initial = 1;
        if (ox.occ?.GetAttribute("initial") is { Length: > 0 } inis
            && int.TryParse(inis, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ii))
            ox.initial = ii;
        if (ox.initial < ox.min) ox.initial = ox.min;
        // The data-less min=0 removal only applies when the document both
        // carries data to bind against AND a form packet recording the saved
        // instance set (the subform's absence from it means it was removed).
        // A first-time merge instead creates the occur INITIAL instances.
        // A data-less min>1 still renders its min instances (empty).
        // A subform that takes no data is not absent for want of data: where the packet
        // does not record its parent either, nothing says it was removed, and it keeps
        // its initial instances.
        if (ox.avail.Count == 0)
        {
            if (ox.occ is not null && ox.dataRoot is not null && ox.min == 0)
                ox.n = ox.packetRecords && !(fScope is null && TakesNoData(sub)) ? 0 : ox.initial;
            else ox.n = Math.Max(1, ox.occ is null ? 1 : ox.min);
        }
        else ox.n = Math.Min(ox.avail.Count, ox.max < 0 ? ox.avail.Count : Math.Max(ox.max, 1));
        // Data present but fewer groups than the occur minimum: the template
        // minimum still governs the rendered instance count (trailing
        // instances stay empty).
        if (ox.avail.Count > 0 && ox.occ is not null && ox.n < ox.min) ox.n = ox.min;
        ox.fInst = new List<XmlElement>();
        ox.packetAuthoritative = false;
        ReadFormPacketInstances(ox, e, fScope, sub);
        if (ox.n == 0) { e.RemoveChild(sub); return true; }
        CloneSubformInstances(ox, e, sub);
        BindSubformInstances(ox, scope);
        return true;
    }

    /// <summary>Makes the subform's instance list: the template subform and a copy after it for
    /// every further instance.</summary>
    private static void CloneSubformInstances(OccurrenceExpandState ox, XmlElement e, XmlElement sub)
    {
        ox.instances = new List<XmlElement> { sub };
        for (int k = 1; k < ox.n; k++)
        {
            var copy = (XmlElement)sub.CloneNode(true);
            e.InsertAfter(copy, ox.instances[k - 1]);
            ox.instances.Add(copy);
        }
    }

    /// <summary>Binds each instance of one subform to its data group and walks into it. The walk
    /// re-enters the expansion for the nested subforms and overwrites the state's per-subform
    /// fields, so this subform's own lists are held here for the whole loop.</summary>
    private static void BindSubformInstances(OccurrenceExpandState ox, XmlElement? scope)
    {
        var instances = ox.instances;
        var avail = ox.avail;
        var fInst = ox.fInst;
        var packetAuthoritative = ox.packetAuthoritative;
        var deepMatched = ox.deepMatched;
        for (int k = 0; k < instances.Count; k++)
        {
            var fk = k < fInst.Count ? fInst[k] : null;
            // Under an authoritative packet only RECORDED instances bind data;
            // the template-default filler stays explicitly unbound.
            if (packetAuthoritative && k >= fInst.Count)
            {
                instances[k].SetAttribute("data-idx", "-1");
                OccWalk(ox, instances[k], scope, null);
                continue;
            }
            if (k < avail.Count)
            {
                ox.used.Add(avail[k]);
                instances[k].SetAttribute("data-idx", ox.groups.Count.ToString());
                ox.groups.Add(avail[k]);
                OccWalk(ox, instances[k], avail[k], fk);
            }
            else
            {
                // A deep-matched repeat that ran out of groups is explicitly
                // UNBOUND: its fields must stay empty rather than scavenge
                // same-name leaves from another section's data.
                if (deepMatched) instances[k].SetAttribute("data-idx", "-1");
                OccWalk(ox, instances[k], scope, fk);
            }
        }
    }

    /// <summary>Reads the instances the saved form packet records for this subform. Template
    /// siblings of one name share the packet's same-named instances in sibling order: each starts
    /// after what the earlier ones took and leaves one for every later one, so five same-named
    /// rows recorded as five instances stay five rows.</summary>
    private static void ReadFormPacketInstances(OccurrenceExpandState ox, XmlElement e, XmlElement? fScope, XmlElement sub)
    {
        if (fScope is not null && ox.name.Length > 0)
        {
            var recorded = fScope.ChildNodes.OfType<XmlElement>()
                .Where(c => c.LocalName == "subform" && c.GetAttribute("name") == ox.name).ToList();
            int taken = 0, kept = 0;
            bool past = false;
            foreach (var s in e.ChildNodes.OfType<XmlElement>())
            {
                if (ReferenceEquals(s, sub)) past = true;
                else if (s.LocalName == "subform" && s.GetAttribute("name") == ox.name)
                {
                    if (past) kept++; else taken++;
                }
            }
            ox.fInst = taken == 0 ? recorded : recorded.Skip(taken).ToList();
            int share = recorded.Count - taken - kept;
            bool managed = fScope.ChildNodes.OfType<XmlElement>()
                .Any(c => c.LocalName == "instanceManager" && c.GetAttribute("name") == "_" + ox.name);
            bool containerRepeats = e.ChildNodes.OfType<XmlElement>().Any(c => c.LocalName == "occur");
            if (managed && ox.packetRecords && !containerRepeats && recorded.Count == 0 && ox.avail.Count == 0)
            {
                // A manager the packet records with ZERO instances and no data to
                // bind is a DELIBERATE removal: the one template default renders
                // UNBOUND. With data groups the data-driven count above stands -
                // a form saved before its rows were added still carries every
                // group, and one row per group shows them all rather than a single
                // empty row. A manager with recorded instances is NOT trusted to
                // cap the count either: generator-produced packets under-record,
                // and the data-driven merge stays the authority there.
                ox.packetAuthoritative = true;
                ox.n = 1;
            }
            else if (managed && ox.occ is null && !containerRepeats && share > ox.n)
                ox.n = share;
            // A repeating subform that binds no data has no count but the one the packet records for this very
            // parent instance: the instances a script made (a grid of cells) are there, up to its maximum.
            else if (managed && ox.occ is not null && BindsNoData(sub) && share > ox.n)
                ox.n = ox.max < 0 ? share : Math.Min(share, Math.Max(ox.max, 1));
            // An optional subform dropped for want of data is there after all when the
            // packet records instances of it under this very parent.
            else if (managed && ox.n == 0 && share > 0)
                ox.n = ox.max < 0 ? share : Math.Min(share, Math.Max(ox.max, 1));
        }
    }

    /// <summary></summary>
    private static void FindDeepDataMatches(OccurrenceExpandState ox, XmlElement? scope)
    {
        if (ox.avail.Count == 0 && ox.name.Length > 0 && scope is not null)
        {
            // No direct child of the scope matches: fall back to scope DESCENDANTS
            // (XFA's lenient matching for data nested deeper than the template).
            ox.avail = scope.SelectNodes(".//*")!.OfType<XmlElement>()
                .Where(c => c.LocalName == ox.name && OccIsGroup(c) && !ox.used.Contains(c)).ToList();
            // Same-name groups under DIFFERENT parents belong to different
            // template sections (Debtor1's aliases vs Debtor2's): each
            // repeating subform consumes only the first unconsumed parent's
            // run, and the next section's search starts after it.
            if (ox.avail.Count > 1)
            {
                var firstParent = ox.avail[0].ParentNode;
                ox.avail = ox.avail.Where(c => ReferenceEquals(c.ParentNode, firstParent)).ToList();
            }
            ox.deepMatched = ox.avail.Count > 0;
        }
        if (ox.avail.Count == 0 && ox.name.Length > 0 && scope is not null && ox.occ is not null)
        {
            // Still nothing: a repeating subform whose own scope group is empty
            // matches data one scope UP (a container bound to an empty marker
            // group — e.g. <SubsequentSF/> — with the real repeat groups
            // recorded as its siblings). Data-scope matching ascends.
            for (var up = scope.ParentNode as XmlElement; up is not null && ox.avail.Count == 0;
                 up = up.ParentNode as XmlElement)
                ox.avail = up.ChildNodes.OfType<XmlElement>()
                    .Where(c => c.LocalName == ox.name && OccIsGroup(c) && !ox.used.Contains(c)).ToList();
        }
    }
}

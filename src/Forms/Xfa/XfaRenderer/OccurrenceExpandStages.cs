using System.Globalization;
using System.Text;
using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

internal static partial class XfaRenderer
{
// The stages of the occurrence expansion: the group test and the recursive subform walk.
    private static void OccWalk(OccurrenceExpandState ox, XmlElement e, XmlElement? scope, XmlElement? fScope)
    {
        foreach (var sub in e.ChildNodes.OfType<XmlElement>().Where(c => c.LocalName == "subform").ToList())
        {
            if (!ExpandSubformOccurrence(ox, e, scope, fScope, sub)) break;
        }
    }
    private static bool OccIsGroup(XmlElement el) =>
        el.ChildNodes.OfType<XmlElement>().Any()
        || el.GetAttribute("dataNode", "http://www.xfa.org/schema/xfa-data/1.0/") == "dataGroup";

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
            if (maxs.Length > 0 && int.TryParse(maxs, out var m)) ox.max = m;
            if (mins.Length > 0 && int.TryParse(mins, out var mi)) ox.min = mi;
            // occur with only a min (e.g. min="5"): the subform still repeats
            // at least min times — an absent max never caps below it.
            if (ox.max >= 0 && ox.max < ox.min) ox.max = ox.min;
        }
        ox.avail = ox.name.Length > 0 && scope is not null
            ? scope.ChildNodes.OfType<XmlElement>()
                .Where(c => c.LocalName == ox.name && OccIsGroup(c) && !ox.used.Contains(c)).ToList()
            : new List<XmlElement>();
        ox.deepMatched = false;
        FindDeepDataMatches(ox, scope);
        ox.initial = 1;
        if (ox.occ?.GetAttribute("initial") is { Length: > 0 } inis && int.TryParse(inis, out var ii))
            ox.initial = ii;
        if (ox.initial < ox.min) ox.initial = ox.min;
        ox.n = ox.avail.Count == 0
            // The data-less min=0 removal only applies when the document both
            // carries data to bind against AND a form packet recording the saved
            // instance set (the subform's absence from it means it was removed).
            // A first-time merge instead creates the occur INITIAL instances.
            // A data-less min>1 still renders its min instances (empty).
            ? (ox.occ is not null && ox.dataRoot is not null && ox.min == 0
                ? (ox.packetRecords ? 0 : ox.initial)
                : Math.Max(1, ox.occ is null ? 1 : ox.min))
            : Math.Min(ox.avail.Count, ox.max < 0 ? ox.avail.Count : Math.Max(ox.max, 1));
        // Data present but fewer groups than the occur minimum: the template
        // minimum still governs the rendered instance count (trailing
        // instances stay empty).
        if (ox.avail.Count > 0 && ox.occ is not null && ox.n < ox.min) ox.n = ox.min;
        ox.fInst = new List<XmlElement>();
        ox.packetAuthoritative = false;
        ReadFormPacketInstances(ox, e, fScope);
        if (ox.n == 0) { e.RemoveChild(sub); return true; }
        ox.instances = new List<XmlElement> { sub };
        for (int k = 1; k < ox.n; k++)
        {
            var copy = (XmlElement)sub.CloneNode(true);
            e.InsertAfter(copy, ox.instances[k - 1]);
            ox.instances.Add(copy);
        }
        BindSubformInstances(ox, scope);
        return true;
    }

    /// <summary></summary>
    private static void BindSubformInstances(OccurrenceExpandState ox, XmlElement? scope)
    {
        for (int k = 0; k < ox.instances.Count; k++)
        {
            var fk = k < ox.fInst.Count ? ox.fInst[k] : null;
            // Under an authoritative packet only RECORDED instances bind data;
            // the template-default filler stays explicitly unbound.
            if (ox.packetAuthoritative && k >= ox.fInst.Count)
            {
                ox.instances[k].SetAttribute("data-idx", "-1");
                OccWalk(ox, ox.instances[k], scope, null);
                continue;
            }
            if (k < ox.avail.Count)
            {
                ox.used.Add(ox.avail[k]);
                ox.instances[k].SetAttribute("data-idx", ox.groups.Count.ToString());
                ox.groups.Add(ox.avail[k]);
                OccWalk(ox, ox.instances[k], ox.avail[k], fk);
            }
            else
            {
                // A deep-matched repeat that ran out of groups is explicitly
                // UNBOUND: its fields must stay empty rather than scavenge
                // same-name leaves from another section's data.
                if (ox.deepMatched) ox.instances[k].SetAttribute("data-idx", "-1");
                OccWalk(ox, ox.instances[k], scope, fk);
            }
        }
    }

    /// <summary></summary>
    private static void ReadFormPacketInstances(OccurrenceExpandState ox, XmlElement e, XmlElement? fScope)
    {
        if (fScope is not null && ox.name.Length > 0)
        {
            ox.fInst = fScope.ChildNodes.OfType<XmlElement>()
                .Where(c => c.LocalName == "subform" && c.GetAttribute("name") == ox.name).ToList();
            bool managed = fScope.ChildNodes.OfType<XmlElement>()
                .Any(c => c.LocalName == "instanceManager" && c.GetAttribute("name") == "_" + ox.name);
            bool containerRepeats = e.ChildNodes.OfType<XmlElement>().Any(c => c.LocalName == "occur");
            if (managed && ox.packetRecords && !containerRepeats && ox.fInst.Count == 0)
            {
                // A manager the packet records with ZERO instances is a
                // DELIBERATE removal: the one template default renders
                // UNBOUND (probed — five <detail> data rows under
                // such a packet render as a single empty row). A manager
                // with recorded instances is NOT trusted to cap the count:
                // generator-produced packets under-record,
                // and the data-driven merge stays the authority there.
                ox.packetAuthoritative = true;
                ox.n = 1;
            }
            else if (managed && ox.occ is null && !containerRepeats && ox.fInst.Count > ox.n)
                ox.n = ox.fInst.Count;
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

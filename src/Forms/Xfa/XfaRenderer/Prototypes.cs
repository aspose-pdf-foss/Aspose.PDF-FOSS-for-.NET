using System.Globalization;
using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

internal static partial class XfaRenderer
{
    // What a box says of itself and never takes from the prototype it names.
    private static readonly string[] OwnAttributes = { "id", "name", "use", "usehref" };

    // Elements that hold one content element of several kinds: one that holds its own
    // kind takes none of another kind from the prototype.
    private static readonly string[] OneOfHolders = { "value", "ui" };

    private const string SomOpen = "som(";

    /// <summary>Completes every element of the template that names a prototype (use, or a
    /// usehref into this document) with what the prototype declares and the element does
    /// not: attributes, and child elements - a child both declare is completed the same
    /// way. A form set from a style sheet of prototypes keeps the sizes, fonts, margins and
    /// paragraph settings of its boxes there. Done once, before anything reads the template.</summary>
    private static void ResolvePrototypes(XmlElement template)
    {
        var referrers = new List<XmlElement>();
        CollectReferrers(template, referrers);
        if (referrers.Count == 0) return;
        var byId = new Dictionary<string, XmlElement>(StringComparer.Ordinal);
        foreach (var el in template.SelectNodes(".//*[@id]")!.OfType<XmlElement>())
            if (!byId.ContainsKey(el.GetAttribute("id"))) byId[el.GetAttribute("id")] = el;
        var visited = new HashSet<XmlElement>();
        foreach (var referrer in referrers) InheritPrototype(referrer, template, byId, visited);
    }

    private static void CollectReferrers(XmlElement e, List<XmlElement> into)
    {
        if (e.HasAttribute("use") || e.HasAttribute("usehref")) into.Add(e);
        foreach (XmlNode child in e.ChildNodes)
            if (child is XmlElement element) CollectReferrers(element, into);
    }

    private static void InheritPrototype(XmlElement e, XmlElement template, Dictionary<string, XmlElement> byId, HashSet<XmlElement> visited)
    {
        // An element is completed once; one met again while it is being completed closes a
        // circle of references, which ends there.
        if (!visited.Add(e)) return;
        var prototype = PrototypeOf(e, template, byId);
        if (prototype is null || ReferenceEquals(prototype, e) || prototype.LocalName != e.LocalName) return;
        // The prototype is complete before it is read: it, and what it holds, may name prototypes too.
        var inner = new List<XmlElement>();
        CollectReferrers(prototype, inner);
        foreach (var referrer in inner) InheritPrototype(referrer, template, byId, visited);
        MergePrototype(e, prototype);
    }

    /// <summary>The element a use or usehref names: by "#id", or by a path from the
    /// template ("som(...)" in a usehref, or the bare expression of a use). A usehref into
    /// another document names nothing here.</summary>
    private static XmlElement? PrototypeOf(XmlElement e, XmlElement template, Dictionary<string, XmlElement> byId)
    {
        var reference = e.GetAttribute("use").Trim();
        if (reference.Length == 0)
        {
            var href = e.GetAttribute("usehref").Trim();
            var hash = href.IndexOf('#');
            if (hash < 0) return null;
            var document = href.Substring(0, hash);
            if (document.Length > 0 && document != ".") return null;
            reference = href.Substring(hash);
        }
        if (reference.StartsWith("#" + SomOpen, StringComparison.Ordinal) && reference.EndsWith(")", StringComparison.Ordinal))
            return PrototypeByPath(template, reference.Substring(SomOpen.Length + 1, reference.Length - SomOpen.Length - 2));
        if (reference.StartsWith("#", StringComparison.Ordinal))
            return byId.TryGetValue(reference.Substring(1), out var named) ? named : null;
        return PrototypeByPath(template, reference);
    }

    /// <summary>Follows a path of names from the template element: "$template" is the
    /// template itself, "#tag" a child by its element name, any other segment a child by
    /// its name attribute, each with an optional [index].</summary>
    private static XmlElement? PrototypeByPath(XmlElement template, string path)
    {
        XmlElement? current = template;
        foreach (var raw in path.Split('.'))
        {
            var segment = raw.Trim();
            if (segment.Length == 0 || segment is "$template" or "xfa" or "template") continue;
            int index = 0;
            var bracket = segment.IndexOf('[');
            if (bracket > 0)
            {
                int.TryParse(segment.Substring(bracket + 1).TrimEnd(']'), NumberStyles.Integer, CultureInfo.InvariantCulture, out index);
                segment = segment.Substring(0, bracket);
            }
            bool byTag = segment[0] == '#';
            var wanted = byTag ? segment.Substring(1) : segment;
            // What a container keeps in its proto element is reached as if it were its own.
            current = current.ChildNodes.OfType<XmlElement>()
                .SelectMany(c => c.LocalName == "proto" ? c.ChildNodes.OfType<XmlElement>() : new[] { c })
                .Where(c => byTag ? c.LocalName == wanted : c.GetAttribute("name") == wanted)
                .Skip(index).FirstOrDefault();
            if (current is null) return null;
        }
        return ReferenceEquals(current, template) ? null : current;
    }

    private static void MergePrototype(XmlElement target, XmlElement prototype)
    {
        foreach (XmlAttribute attribute in prototype.Attributes)
            if (!OwnAttributes.Contains(attribute.Name) && !target.HasAttribute(attribute.Name))
                target.SetAttribute(attribute.Name, attribute.Value);
        var inherited = prototype.ChildNodes.OfType<XmlElement>().ToList();
        if (inherited.Count == 0)
        {
            // A leaf of text: taken where the element says nothing of its own.
            if (!target.HasChildNodes && prototype.InnerText.Length > 0) target.InnerText = prototype.InnerText;
            return;
        }
        var own = target.ChildNodes.OfType<XmlElement>().ToList();
        bool holdsOwnKind = OneOfHolders.Contains(target.LocalName) && own.Count > 0;
        bool holdsOwnBoxes = own.Any(c => BoxTags.Contains(c.LocalName));
        foreach (var group in inherited.GroupBy(c => c.LocalName))
        {
            var mine = own.Where(c => c.LocalName == group.Key).ToList();
            var theirs = group.ToList();
            bool box = BoxTags.Contains(group.Key);
            if (mine.Count == 0)
            {
                // The boxes a container holds are its own set: it takes the prototype's only when it has none.
                if (holdsOwnKind || (box && holdsOwnBoxes)) continue;
                foreach (var child in theirs) target.AppendChild(child.CloneNode(true));
            }
            else if (mine.Count == 1 && theirs.Count == 1 && !box)
                MergePrototype(mine[0], theirs[0]);
        }
    }
}

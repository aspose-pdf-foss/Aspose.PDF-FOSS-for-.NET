using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

internal static partial class XfaRenderer
{
    /// <summary>The value of a field that no data-bound instance holds and whose bind reference is
    /// relative to a container that is itself bound by a reference: the references of the field and
    /// of the containers around it, read outward to the root subform, make one path from the record
    /// (a header block bound to "$.Head.Numbering.Diary" holding a field bound to "$.Number"). The
    /// path is taken only when it is exact - every container on the way either carries a reference
    /// or takes no data, and the path reaches one leaf - so nothing is bound by a guess. A field whose
    /// own reference starts at the record ("$record.Job.Imprint") already names its whole path. Null
    /// when the field is not bound this way.</summary>
    private static string? ChainedReferenceValue(Ctx ctx, XmlElement field)
    {
        if (ctx.DataRoot is null) return null;
        var path = RecordPath(DataRefOf(field), out var rooted);
        if (path is null) return null;
        var crossedReference = false;
        for (var a = field.ParentNode as XmlElement; !rooted && a is not null; a = a.ParentNode as XmlElement)
        {
            if (a.LocalName is "pageArea" or "pageSet") continue;
            // The root subform holds the record itself: the path is complete there.
            if (a.ParentNode is not XmlElement { LocalName: "subform" or "subformSet" or "pageSet" or "pageArea" or "area" or "exclGroup" }) break;
            // An instance bound to a data group has its own resolution.
            if (a.GetAttribute("data-idx").Length > 0) return null;
            var reference = DataRefOf(a);
            if (reference.Length > 0)
            {
                var outer = RecordPath(reference, out rooted);
                if (outer is null) return null;
                path = outer + "." + path;
                crossedReference = true;
            }
            else if (!TakesNoData(a)) return null;      // bound by its name: where it lands is not ours to say
        }
        if (!crossedReference && !rooted) return null;
        var nodes = ReferencedNodes(ctx.DataRoot, path);
        return nodes.Count == 1 && !nodes[0].ChildNodes.OfType<XmlElement>().Any() ? nodes[0].InnerText : null;
    }

    /// <summary>The path of a bind reference and whether it starts at the record (true) or at the
    /// data node of the enclosing container (false); null for no reference or one rooted elsewhere.</summary>
    private static string? RecordPath(string reference, out bool rooted)
    {
        rooted = reference.StartsWith(RecordPrefix, StringComparison.Ordinal);
        if (rooted) return reference.Substring(RecordPrefix.Length);
        return reference.Length == 0 ? null : RelativeRefPath(reference);
    }
}

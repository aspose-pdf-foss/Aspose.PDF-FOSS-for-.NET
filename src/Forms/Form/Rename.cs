using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

public sealed partial class Form
{
    /// <summary>The attributes a field inherits from its ancestors (PDF 32000 table 220/222).</summary>
    private static readonly string[] InheritableFieldKeys = ["FT", "Ff", "V", "DV", "DA", "Q"];

    private const int MaxFieldDepth = 64;

    /// <summary>Give the field named <paramref name="fullName"/> the full name
    /// <paramref name="newFullName"/>. A name that stays under the field's parent changes the
    /// field's own part; any other name takes the field out of its parent, since a field
    /// under a parent always carries the parent's name in front of its own.</summary>
    /// <returns>False when the form has no such field.</returns>
    internal bool RenameField(string fullName, string newFullName)
    {
        var field = FindFieldOrNull(fullName);
        if (field is null) return false;
        var reader = _reader ?? OwnerDocument?.Reader;
        var named = reader is null ? null : NamedNode(field.Dict, reader);
        var parent = named is null ? null : reader!.ResolveDict(named.Get("Parent"));
        if (parent is null || !string.Equals(field.FullName, fullName, StringComparison.Ordinal))
        {
            field.SetPartialName(newFullName);
            return true;
        }
        var parentPrefix = new Field(parent, reader!).FullName + ".";
        if (newFullName.StartsWith(parentPrefix, StringComparison.Ordinal))
        {
            field.SetPartialName(newFullName.Substring(parentPrefix.Length));
            return true;
        }
        MoveToRoot(named!, parent, reader!);
        named!.Set("T", new PdfString(Compat.Latin1.GetBytes(newFullName)));
        return true;
    }

    /// <summary>The node that carries the field's own name: the dictionary itself, or the
    /// field above a widget that has none.</summary>
    private static PdfDictionary? NamedNode(PdfDictionary dict, PdfReader reader)
    {
        PdfDictionary? node = dict;
        for (var depth = 0; node is not null && depth < MaxFieldDepth; depth++)
        {
            if (node.ContainsKey("T")) return node;
            node = reader.ResolveDict(node.Get("Parent"));
        }
        return null;
    }

    /// <summary>Take a field out of its parent and list it in the form itself, where its top
    /// ancestor stood, with what it inherited written on it; ancestors left without a kid go.</summary>
    private void MoveToRoot(PdfDictionary field, PdfDictionary parent, PdfReader reader)
    {
        if (_acroForm is null || reader.Resolve(_acroForm.Get("Fields")) is not PdfArray roots) return;
        var ancestors = new List<PdfDictionary>();
        for (var node = parent; node is not null && ancestors.Count < MaxFieldDepth;
             node = reader.ResolveDict(node.Get("Parent")))
            ancestors.Add(node);
        var top = ancestors[ancestors.Count - 1];
        var rootIndex = IndexOf(roots, top, reader);
        if (rootIndex < 0) return;
        var kids = reader.Resolve(parent.Get("Kids")) as PdfArray;
        var kidIndex = kids is null ? -1 : IndexOf(kids, field, reader);
        if (kidIndex < 0) return;

        foreach (var key in InheritableFieldKeys)
        {
            if (field.ContainsKey(key)) continue;
            var inherited = ancestors.Select(a => a.Get(key)).FirstOrDefault(v => v is not null);
            if (inherited is not null) field.Set(key, inherited);
        }
        var entry = kids![kidIndex];
        kids.RemoveAt(kidIndex);
        field.Remove("Parent");
        roots.Insert(rootIndex, entry);

        // An ancestor whose last kid left is no field any more.
        for (var i = 0; i < ancestors.Count; i++)
        {
            if (reader.Resolve(ancestors[i].Get("Kids")) is PdfArray { Count: > 0 }) break;
            var holder = i + 1 < ancestors.Count ? reader.Resolve(ancestors[i + 1].Get("Kids")) as PdfArray : roots;
            var at = holder is null ? -1 : IndexOf(holder, ancestors[i], reader);
            if (at >= 0) holder!.RemoveAt(at);
            var gone = ancestors[i];
            _fields.RemoveAll(f => ReferenceEquals(f.Dict, gone));
            _groupFieldDicts.Remove(gone);
        }
    }

    private static int IndexOf(PdfArray array, PdfDictionary dict, PdfReader reader)
    {
        for (var i = 0; i < array.Count; i++)
            if (ReferenceEquals(reader.ResolveDict(array[i]), dict)) return i;
        return -1;
    }
}

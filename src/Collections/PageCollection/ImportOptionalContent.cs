using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

public sealed partial class PageCollection
{
    /// <summary>Lists in this document's catalog the optional content groups the clone of an
    /// imported page reached, with the OFF state the source document gives them: a layer
    /// switched off in the source stays off in the merged file instead of being drawn.</summary>
    private void ImportOptionalContentGroups(PdfReader sourceReader, Dictionary<int, PdfObject> remap)
    {
        var sourceProperties = sourceReader.ResolveDict(sourceReader.Catalog.Get("OCProperties"));
        var sourceGroups = sourceProperties is null ? null : sourceReader.ResolveArray(sourceProperties.Get("OCGs"));
        if (sourceGroups is null || sourceGroups.Count == 0) return;
        var sourceDefault = sourceReader.ResolveDict(sourceProperties!.Get("D"));
        var sourceOff = sourceDefault is null ? null : sourceReader.ResolveArray(sourceDefault.Get("OFF"));

        foreach (var entry in sourceGroups)
        {
            if (entry is not PdfIndirectRef source || !remap.TryGetValue(source.ObjectNumber, out var imported)
                || imported is not PdfIndirectRef group)
                continue;
            var properties = GetOrCreateDict(_reader.Catalog, "OCProperties");
            AddGroupOnce(GetOrCreateArray(properties, "OCGs"), group);
            if (sourceOff is not null && Holds(sourceOff, source))
                AddGroupOnce(GetOrCreateArray(GetOrCreateDict(properties, "D"), "OFF"), group);
        }
    }

    private static void AddGroupOnce(PdfArray groups, PdfIndirectRef group)
    {
        if (!Holds(groups, group)) groups.Add(group);
    }

    private static bool Holds(PdfArray groups, PdfIndirectRef group)
    {
        foreach (var item in groups)
        {
            if (item is PdfIndirectRef held && held.ObjectNumber == group.ObjectNumber) return true;
        }
        return false;
    }

    private PdfDictionary GetOrCreateDict(PdfDictionary owner, string key)
    {
        if (_reader.ResolveDict(owner.Get(key)) is { } existing) return existing;
        var created = new PdfDictionary();
        owner.Set(key, created);
        return created;
    }

    private PdfArray GetOrCreateArray(PdfDictionary owner, string key)
    {
        if (_reader.ResolveArray(owner.Get(key)) is { } existing) return existing;
        var created = new PdfArray();
        owner.Set(key, created);
        return created;
    }
}

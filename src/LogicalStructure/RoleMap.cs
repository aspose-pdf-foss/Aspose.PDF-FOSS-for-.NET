using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>
/// Maps custom (non-standard) structure-type names to PDF standard
/// types, persisting entries to the structure tree's /RoleMap dictionary
/// so a tagged document round-trips its custom tags.
/// </summary>
internal sealed class RoleMap
{
    private readonly PdfDictionary _structTreeRoot;

    internal RoleMap(PdfDictionary structTreeRoot) => _structTreeRoot = structTreeRoot;

    private PdfDictionary GetOrCreateMap()
    {
        if (_structTreeRoot.Get("RoleMap") is PdfDictionary existing) return existing;
        var map = new PdfDictionary();
        _structTreeRoot.Set("RoleMap", map);
        return map;
    }

    internal bool TryGet(string customTag, out string standardType)
    {
        if (_structTreeRoot.Get("RoleMap") is PdfDictionary map && map.GetName(customTag) is { } v)
        {
            standardType = v;
            return true;
        }
        standardType = string.Empty;
        return false;
    }

    internal void Set(string customTag, string standardType)
        => GetOrCreateMap().Set(customTag, new PdfName(standardType));

    internal static bool IsStandardType(string tag) => StructureTypeStandard.FromTag(tag) is not null;
}

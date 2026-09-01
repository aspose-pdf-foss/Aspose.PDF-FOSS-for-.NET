using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>
/// Tracks the structure-element identifiers (/ID) in use across a document
/// so <see cref="StructureElement.SetId"/> can reject duplicates. Shared by
/// all elements created from one <see cref="Aspose.Pdf.Tagged.ITaggedContent"/>.
/// </summary>
internal sealed class IdRegistry
{
    private readonly Dictionary<string, StructureElement> _ids = new(StringComparer.Ordinal);

    internal bool IsUsedByOther(string id, StructureElement self)
        => _ids.TryGetValue(id, out var owner) && !ReferenceEquals(owner, self);

    internal void Register(string id, StructureElement element) => _ids[id] = element;

    internal void Unregister(string? id, StructureElement element)
    {
        if (!string.IsNullOrEmpty(id) && _ids.TryGetValue(id!, out var owner) && ReferenceEquals(owner, element))
            _ids.Remove(id!);
    }
}

// ── Typed structure-element subclasses ────────────────────────────────
//
// Each subclass just fixes the /S role for its node; the public API declares
// these as distinct nominal types so callers can pattern-match on the
// element kind. No subclass adds members beyond what the base provides
// (matches DeclaredOnly reflection, which reports zero
// declared members on most of these subclasses).

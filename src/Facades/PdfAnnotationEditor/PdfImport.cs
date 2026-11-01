using Aspose.Pdf.Core;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfAnnotationEditor
{
    /// <summary>The source object numbers of the pages both documents have, each mapped to the bound
    /// document's page of the same position: an imported annotation's /P and the destinations of its
    /// actions then lead to pages of the bound document instead of copies of the source's.</summary>
    private static Dictionary<int, int> PageCounterparts(Document source, Document target, int sharedPages)
    {
        var map = new Dictionary<int, int>();
        for (var i = 1; i <= sharedPages; i++)
        {
            var from = source.Pages.At(i).SourceObjectNumber;
            var to = target.Pages.At(i).SourceObjectNumber;
            if (from > 0 && to > 0) map[from] = to;
        }
        return map;
    }

    /// <summary>Copy the annotations of <paramref name="sourcePage"/> (form widgets aside) onto
    /// <paramref name="targetPage"/> as objects of the target document: appearance streams, popups and
    /// replies come along and keep pointing at each other. An annotation the target page already holds
    /// under the same unique name (/NM) is replaced where it stands, its popup with it.</summary>
    private static void ImportPageAnnotations(Document target, Page sourcePage, Page targetPage,
        Dictionary<int, int> cloneMap)
    {
        var sourceReader = sourcePage.Reader;
        if (sourceReader.Resolve(sourcePage.Dict.Get("Annots")) is not PdfArray sourceAnnots) return;
        var pageHasCounterpart = cloneMap.ContainsKey(sourcePage.SourceObjectNumber);
        var wanted = new PdfArray();
        foreach (var entry in sourceAnnots)
        {
            if (sourceReader.ResolveDict(entry) is not { } dict || dict.GetName("Subtype") == "Widget") continue;
            // Without a page object to stand for the source page, /P would drag a copy of that page along.
            if (!pageHasCounterpart) dict.Remove("P");
            wanted.Add(entry);
        }
        if (wanted.Count == 0) return;

        // Cloned as one graph, through their references, so what they share is copied once.
        var holder = new PdfDictionary();
        holder.Set("Annots", wanted);
        var imported = (PdfArray)target.ImportDict(holder, sourceReader, cloneMap).Get("Annots")!;

        var reader = target.Reader;
        var slots = new List<PdfObject?>();
        var slotByName = new Dictionary<string, int>(StringComparer.Ordinal);
        if (reader.Resolve(targetPage.Dict.Get("Annots")) is PdfArray existing)
        {
            foreach (var item in existing)
            {
                if (UniqueName(reader, reader.ResolveDict(item)) is { } name) slotByName[name] = slots.Count;
                slots.Add(item);
            }
        }
        var popupSlots = new Dictionary<PdfDictionary, int>(ReferenceEqualityComparer.Instance);
        foreach (var item in imported)
        {
            var dict = reader.ResolveDict(item);
            if (dict is null) continue;
            if (dict.GetName("Subtype") == "Popup" && reader.ResolveDict(dict.Get("Parent")) is { } parent
                && popupSlots.TryGetValue(parent, out var popupSlot))
            {
                slots[popupSlot] = item;
                popupSlots.Remove(parent);
            }
            else if (UniqueName(reader, dict) is { } name && slotByName.TryGetValue(name, out var slot))
            {
                var oldPopup = reader.ResolveDict(reader.ResolveDict(slots[slot])?.Get("Popup"));
                slots[slot] = item;
                var oldPopupSlot = oldPopup is null ? -1 : slots.FindIndex(s => ReferenceEquals(reader.ResolveDict(s), oldPopup));
                if (oldPopupSlot >= 0) popupSlots[dict] = oldPopupSlot;
            }
            else
            {
                slots.Add(item);
            }
        }
        // The popup of a replaced annotation whose replacement brought none has nothing to belong to.
        foreach (var orphan in popupSlots.Values) slots[orphan] = null;

        var annots = new PdfArray();
        foreach (var item in slots)
        {
            if (item is not null) annots.Add(item);
        }
        targetPage.Dict.Set("Annots", annots);
    }

    private static string? UniqueName(IO.PdfReader reader, PdfDictionary? annotation) =>
        reader.Resolve(annotation?.Get("NM")) is PdfString name && name.Value.Length > 0 ? name.ToText() : null;
}

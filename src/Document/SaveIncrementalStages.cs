using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>Every object read from the file that the session edited since: one whose dictionary
    /// or array, or one held directly in it, had an entry set or removed, or a stream whose data was
    /// replaced. An edit made through the object model - a page's rotation, an annotation added to
    /// its /Annots, the catalog's open action - is in the update without being registered anywhere.</summary>
    private void AddEditedObjects(List<(int objectNumber, PdfObject obj)> modified)
    {
        foreach (var loaded in _reader.LoadedObjects)
        {
            if (HasEdits(loaded.Value)) modified.Add((loaded.Key.objNum, loaded.Value));
        }
    }

    /// <summary>True when the object, or a container it holds directly, was edited after it was read.
    /// What it references is an object of its own, checked on its own.</summary>
    private static bool HasEdits(PdfObject obj)
    {
        switch (obj)
        {
            case PdfDictionary dict:
                if (dict.Edited) return true;
                foreach (var value in dict.Values)
                    if (HasEdits(value)) return true;
                return false;
            case PdfArray array:
                if (array.Edited) return true;
                foreach (var item in array)
                    if (HasEdits(item)) return true;
                return false;
            case PdfStream stream:
                return stream.DataReplaced || HasEdits(stream.Dict);
            default:
                return false;
        }
    }

    /// <summary>The objects copied in with another document's pages, as a whole save writes them.</summary>
    private void AddImportedObjects(List<(int objectNumber, PdfObject obj)> modified)
    {
        if (_pages is null) return;
        foreach (var (objNum, obj) in _pages.ImportedObjects)
        {
            if (_reachableObjects is not null && !_reachableObjects.Contains(objNum)) continue;
            modified.Add((objNum, obj));
        }
    }

    /// <summary>The page tree's root, rewritten to the pages as they now stand, when a page was added,
    /// imported, removed or moved; null otherwise. Every page gets the root as its /Parent, taking on
    /// what it inherited from a node the flat tree leaves out, as the whole save's rebuild does.</summary>
    private (int objectNumber, PdfObject obj)? UpdatePageTreeForIncrementalSave()
    {
        if (_pages is null || !_pages.IsModified) return null;
        SyncInMemoryPageTree();
        if (_reader.Catalog.Get("Pages") is not PdfIndirectRef rootRef || _reader.ResolveDict(rootRef) is not { } root)
            return null;
        foreach (var page in Pages)
        {
            if (page.Dict.Get("Parent") is PdfIndirectRef parent && parent.ObjectNumber == rootRef.ObjectNumber) continue;
            MaterialiseInheritedAttributes(page.Dict);
            page.Dict.Set("Parent", new PdfIndirectRef(rootRef.ObjectNumber, 0));
        }
        return (rootRef.ObjectNumber, root);
    }

    /// <summary>Each page's object in the update: a page read from the file stays the object it was
    /// (written only when edited), a page copied in takes its reserved slot and a page made in the
    /// session a new number - so /Kids, a widget's /P and a destination name the page, never a copy.</summary>
    private void NumberPagesForIncrementalSave(IncrementalWriter writer)
    {
        if (_pages is null) return;
        foreach (var page in _pages)
        {
            if (page.ImportSlotObjNum > 0) writer.WriteObject(page.ImportSlotObjNum, page.Dict);
            else if (page.SourceObjectNumber > 0) writer.WrittenAs(page.Dict, page.SourceObjectNumber);
            else writer.WriteObject(writer.AllocateObjectNumber(), page.Dict);
        }
    }
}

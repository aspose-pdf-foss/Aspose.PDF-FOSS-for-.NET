using System.Collections;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

public sealed partial class PageCollection
{
// The stages of the object remap: processing one source object into its remapped copy.
    private void RemapSourceObject(ObjectRemapState ro, PdfObject src, Action<PdfObject> setter)
    {
        switch (src)
        {
            case PdfIndirectRef iref:
            {
                RemapIndirectRef(ro, iref, setter);
                return;
            }

            case PdfDictionary dict:
            {
                if (!ro.visitedIdentity.Add(dict)) { setter(dict); return; }
                var clone = new PdfDictionary();
                setter(clone);
                foreach (var key in dict.Keys)
                {
                    if (key == "Parent") continue;
                    var val = dict.Get(key);
                    if (val is not null)
                    {
                        var k = key;
                        ro.stack.Push((val, v => clone.Set(k, v)));
                    }
                }
                return;
            }

            case PdfArray arr:
            {
                if (!ro.visitedIdentity.Add(arr)) { setter(arr); return; }
                var clone = new PdfArray();
                setter(clone);
                for (int i = arr.Count - 1; i >= 0; i--)
                    ro.stack.Push((arr[i], v => clone.Add(v)));
                return;
            }

            case PdfStream stream:
            {
                RemapStream(ro, stream, setter);
                return;
            }

            default:
                setter(src);
                return;
        }
    }

    /// <summary></summary>
    private void RemapStream(ObjectRemapState ro, PdfStream stream, Action<PdfObject> setter)
    {
        if (!ro.visitedIdentity.Add(stream)) { setter(stream); return; }
        var dictClone = new PdfDictionary();
        var dataCopy = new byte[stream.RawData.Length];
        Array.Copy(stream.RawData, dataCopy, stream.RawData.Length);
        var streamClone = new PdfStream(dictClone, dataCopy);
        setter(streamClone);
        foreach (var key in stream.Dict.Keys)
        {
            if (key == "Parent") continue;
            var val = stream.Dict.Get(key);
            if (val is not null)
            {
                var k = key;
                ro.stack.Push((val, v => dictClone.Set(k, v)));
            }
        }
    }

    /// <summary></summary>
    private void RemapIndirectRef(ObjectRemapState ro, PdfIndirectRef iref, Action<PdfObject> setter)
    {
        // Already remapped?
        if (ro.remap.TryGetValue(iref.ObjectNumber, out var cached))
        {
            setter(cached);
            return;
        }
        // Resolve from source, allocate new obj number, register for writing
        var resolved = ro.sourceReader.Resolve(iref);
        if (resolved is null) { setter(iref); return; }

        // A reference to another PAGE (a leaf /Page or a /Pages tree node) is a
        // navigation target — a GoTo/Link destination or a widget's /P — not
        // content to copy. Cloning it would drag the whole target page's object
        // graph (its images, fonts) into this import. Point it at a reserved
        // destination slot instead; the page, if copied, is written there.
        if (resolved is PdfDictionary pd && IsPageTreeNode(pd))
        {
            var slotMap = _importPageSlots.GetValue(ro.sourceReader, static _ => new Dictionary<int, int>());
            if (!slotMap.TryGetValue(iref.ObjectNumber, out var slot))
            {
                slot = ro.nextObjNum++;
                slotMap[iref.ObjectNumber] = slot;
                RegisterSlot(slot);
            }
            var slotRef = new PdfIndirectRef(slot, 0);
            ro.remap[iref.ObjectNumber] = slotRef;
            setter(slotRef);
            return;
        }

        var newObjNum = ro.nextObjNum++;
        var newRef = new PdfIndirectRef(newObjNum, 0);
        ro.remap[iref.ObjectNumber] = newRef; // map before recursing (cycle breaking)

        // Schedule remapping of the resolved object's contents
        ro.stack.Push((resolved, remapped =>
        {
            _importedObjects.Add((newObjNum, remapped));
            // Also expose the imported object to the destination reader so
            // it resolves in-memory (e.g. Page.Contents) before the next save —
            // otherwise the ref dangles until _importedObjects is written out.
            _reader.RegisterOverlayObject(newObjNum, remapped);
        }));
        setter(newRef);
    }
}

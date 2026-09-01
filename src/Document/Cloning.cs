using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>
    /// A clone map shared across all imports from <paramref name="sourceReader"/> into this
    /// document, so objects shared between several imported source pages (fonts, images,
    /// colour spaces) are imported only once. See <see cref="ImportDict"/>.
    /// </summary>
    internal Dictionary<int, int> GetSharedImportCloneMap(PdfReader sourceReader)
    {
        _importCloneMaps ??= new Dictionary<PdfReader, Dictionary<int, int>>();
        if (!_importCloneMaps.TryGetValue(sourceReader, out var map))
        {
            map = new Dictionary<int, int>();
            _importCloneMaps[sourceReader] = map;
        }
        return map;
    }

    /// <summary>
    /// Deep-clone a PdfDictionary, recursively cloning all referenced objects.
    /// Indirect references from the source document are resolved and inlined.
    /// </summary>
    private PdfDictionary DeepCloneDict(PdfDictionary source, PdfReader sourceReader,
        Dictionary<int, int> cloneMap)
    {
        var result = new PdfDictionary();
        foreach (var key in source.Keys)
        {
            var value = source.Get(key);
            result.Set(key, DeepCloneObject(value, sourceReader, cloneMap));
        }
        return result;
    }

    /// <summary>
    /// Deep-clone a PdfDictionary, excluding specified keys.
    /// </summary>
    private PdfDictionary DeepCloneDictExcluding(PdfDictionary source, PdfReader sourceReader,
        Dictionary<int, int> cloneMap, params string[] excludeKeys)
    {
        var excludeSet = new HashSet<string>(excludeKeys, StringComparer.Ordinal);
        var result = new PdfDictionary();
        foreach (var key in source.Keys)
        {
            if (excludeSet.Contains(key)) continue;
            var value = source.Get(key);
            result.Set(key, DeepCloneObject(value, sourceReader, cloneMap));
        }
        return result;
    }

    private PdfObject DeepCloneObject(PdfObject? obj, PdfReader sourceReader,
        Dictionary<int, int> cloneMap)
    {
        if (obj is null or PdfNull) return PdfNull.Instance;

        if (obj is PdfIndirectRef iref)
        {
            // Check if we already cloned this object
            if (cloneMap.TryGetValue(iref.ObjectNumber, out var mappedObjNum))
                return new PdfIndirectRef(mappedObjNum, 0);

            // Resolve in source and deep-clone
            var resolved = sourceReader.Resolve(iref);
            if (resolved is null) return PdfNull.Instance;

            // Allocate a new object number in this document and reserve it in both
            // the clone map (so shared/cyclic references reuse it) and the new-object
            // list (so AllocateObjectNumber accounts for it during the recursive clone
            // of this object's children — otherwise a nested reference would be handed
            // the very same number, producing a self-referential object).
            var newObjNum = AllocateObjectNumber();
            cloneMap[iref.ObjectNumber] = newObjNum;
            var slot = _newObjects.Count;
            _newObjects.Add((newObjNum, PdfNull.Instance));

            var cloned = DeepCloneObject(resolved, sourceReader, cloneMap);
            _newObjects[slot] = (newObjNum, cloned);
            // Make the cloned object resolvable immediately, not just at save time.
            // Imported resources (e.g. a PdfPageStamp's fonts) are referenced by these
            // new object numbers; without an overlay registration the reader can't resolve
            // them while rendering the in-memory target document, so stamped text vanishes
            // The overlay carries the same number written at save.
            _reader.RegisterOverlayObject(newObjNum, cloned);
            return new PdfIndirectRef(newObjNum, 0);
        }

        if (obj is PdfStream stream)
        {
            var clonedDict = DeepCloneDict(stream.Dict, sourceReader, cloneMap);
            // Copy raw stream data
            return new PdfStream(clonedDict, stream.RawData);
        }

        if (obj is PdfDictionary dict)
        {
            return DeepCloneDict(dict, sourceReader, cloneMap);
        }

        if (obj is PdfArray arr)
        {
            var result = new PdfArray();
            foreach (var item in arr)
                result.Add(DeepCloneObject(item, sourceReader, cloneMap));
            return result;
        }

        // Primitive types (PdfInteger, PdfReal, PdfString, PdfName, PdfBoolean) — no cloning needed
        return obj;
    }

    /// <summary>Deep-clone a structure element (and its element children) as a direct
    /// (inline) dictionary, copying primitive attributes (/S, /ActualText, ...) and
    /// dropping page / marked-content references. Emitting inline dicts avoids any
    /// object-number interaction with the page-copy step.</summary>
    private PdfDictionary CloneStructElem(PdfDictionary src, PdfReader srcReader)
    {
        var clone = new PdfDictionary();
        foreach (var key in src.Keys)
        {
            if (key is "Pg" or "P" or "K") continue;
            var v = src.Get(key);
            if (v is PdfName or PdfString or PdfInteger or PdfReal or PdfBoolean)
                clone.Set(key, v!);
        }
        var children = new PdfArray();
        foreach (var childDict in ResolveStructKids(src, srcReader))
            children.Add(CloneStructElem(childDict, srcReader));
        if (children.Count > 0) clone.Set("K", children);
        return clone;
    }
}

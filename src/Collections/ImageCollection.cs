using System.Collections;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

/// <summary>
/// Collection of image XObjects on a page.
/// </summary>
public class ImageCollection : IReadOnlyList<ImageXObject>
{
    private protected readonly List<ImageXObject> _images;
    private protected readonly PdfDictionary? _ownerDict;
    private protected readonly PdfReader _ownerReader;

    /// <summary>The page whose resources this collection was materialised from,
    /// when known. Lets a resource rename follow through into the page's
    /// content-stream references (<see cref="RewriteDoReferences"/>).</summary>
    internal Page? OwnerPage { get; set; }

    /// <summary>Point every content-stream <c>Do</c> operator that paints the
    /// renamed XObject at its new key. Enumerating materialises the collection,
    /// so the rewritten operators re-serialize on save.</summary>
    internal void RewriteDoReferences(string oldName, string newName)
    {
        var contents = OwnerPage?.Contents;
        if (contents is null) return;
        // Enumerating an unmaterialised collection yields transient parsed
        // instances; materialise first so the mutation lands on the stable
        // operators the collection re-serializes on save.
        contents.EnsureMaterialized();
        foreach (var op in contents)
            if (op is Aspose.Pdf.Operators.Do d && d.Name == oldName)
                d.Name = newName;
    }

    internal ImageCollection(PdfDictionary pageDict, PdfReader reader)
    {
        _ownerDict = pageDict;
        _ownerReader = reader;
        var list = new List<ImageXObject>();
        // /Resources is inheritable through the /Pages tree -- when a page
        // doesn't carry its own, the nearest ancestor /Pages node's entry
        // applies. Walk up via /Parent until we find one, then recurse into
        // Form XObjects (some producers wrap content
        // there). Cycle-guarded by stream identity so a self-referencing
        // form can't loop forever.
        var visited = new HashSet<PdfStream>();
        CollectImages(InheritedResources(pageDict, reader), reader, list, visited,
            new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance));
        _images = list;
        foreach (var img in list) img.Owner = this;
    }

    /// <summary>Register a new image XObject stream in the owning resources'
    /// /XObject dictionary and append it to this collection. Returns the
    /// assigned resource name (Im1, Im2, …).</summary>
    internal string AppendImageXObject(PdfStream imageStream)
    {
        var resources = _ownerReader.ResolveDict(_ownerDict?.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            _ownerDict?.Set("Resources", resources);
        }
        var xobjects = _ownerReader.ResolveDict(resources.Get("XObject"));
        if (xobjects is null)
        {
            xobjects = new PdfDictionary();
            resources.Set("XObject", xobjects);
        }
        var n = 1;
        while (xobjects.ContainsKey($"Im{n}")) n++;
        var name = $"Im{n}";
        xobjects.Set(name, imageStream);
        _images.Add(new XImage(name, imageStream, _ownerReader) { OwnerXObjects = xobjects, Owner = this });
        return name;
    }

    private protected static PdfDictionary? InheritedResources(PdfDictionary pageDict, PdfReader reader)
    {
        var current = pageDict;
        while (current is not null)
        {
            var res = reader.ResolveDict(current.Get("Resources"));
            if (res is not null) return res;
            current = reader.ResolveDict(current.Get("Parent"));
        }
        return null;
    }

    private static void CollectImages(PdfDictionary? resources, PdfReader reader,
        List<ImageXObject> sink, HashSet<PdfStream> visited, HashSet<PdfDictionary> visitedResources)
    {
        if (resources is null || !visitedResources.Add(resources)) return;

        // Direct XObjects -- only Subtype=Image entries count. Form XObjects are
        // NOT recursed into: Resources.Images reports the resource dictionary's own
        // image entries, so an image that lives inside a stamped page's Form XObject
        // stays that form's private resource (a stamped-then-saved document reports
        // 0 page images; PdfExtractor reaches nested images through its own
        // form/pattern collectors).
        var xobjectDict = reader.ResolveDict(resources.Get("XObject"));
        if (xobjectDict is not null)
        {
            foreach (var key in xobjectDict.Keys)
            {
                var obj = reader.ResolveStream(xobjectDict.Get(key));
                if (obj is null) continue;
                // Every named entry surfaces, even when two names share one stream
                // object (image dedupe leaves e.g. Im1 and Im42 pointing at the same
                // stream — the collection still reports both). Cycle safety comes from
                // the visited-resources guard above, not from stream identity.
                var subtype = obj.Dict.GetName("Subtype");
                if (subtype == "Image")
                    sink.Add(new XImage(key, obj, reader) { OwnerXObjects = xobjectDict });
            }
        }

        // Tiling-pattern resources -- a Type-1 (tiling) pattern is itself a
        // content stream with its own /Resources, and producers like
        // some producers emit raster images there as the pattern's only paint.
        // Recurse so page.Images surfaces them too.
        var patternDict = reader.ResolveDict(resources.Get("Pattern"));
        if (patternDict is not null)
        {
            foreach (var key in patternDict.Keys)
            {
                // Tiling patterns are streams (PatternType 1); shading
                // patterns (PatternType 2) are plain dictionaries with no
                // /Resources. ResolveStream returns null for the latter
                // and we just skip it.
                var pat = reader.ResolveStream(patternDict.Get(key));
                if (pat is null || !visited.Add(pat)) continue;
                CollectImages(reader.ResolveDict(pat.Dict.Get("Resources")), reader, sink, visited, visitedResources);
            }
        }
    }

    public int Count => _images.Count;

    /// <summary>Get an image by its 1-based index (matching
    /// <see cref="Replace(int, Stream)"/> and <see cref="XImageCollection.Delete(int)"/>).</summary>
    public ImageXObject this[int index] => _images[index - 1];

    // IReadOnlyList is a 0-based contract (foreach/LINQ ElementAt); keep that honest
    // while the public indexer above stays 1-based.
    ImageXObject IReadOnlyList<ImageXObject>.this[int index] => _images[index];

    /// <summary>
    /// Get an image by its resource name (e.g., "Im0", "JI1a").
    /// Returns null if no image with the given name exists.
    /// </summary>
    public ImageXObject? GetByName(string name)
    {
        foreach (var img in _images)
            if (img.Name == name)
                return img;
        return null;
    }

    /// <summary>
    /// Replace the image data at the given 1-based index with the provided stream.
    /// Reuses the existing image resource name; subsequent reads see the new pixels.
    /// </summary>
    public void Replace(int index, Stream stream)
    {
        if (index < 1 || index > _images.Count)
            throw new ArgumentException($"Index {index} is outside the collection (1..{_images.Count})", nameof(index));
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        _images[index - 1].ReplaceImageData(ReadAll(stream));
    }

    /// <summary>Replace the image with the given resource name.</summary>
    public void Replace(string name, Stream stream)
    {
        var img = GetByName(name) ?? throw new KeyNotFoundException($"No image named '{name}'");
        img.ReplaceImageData(ReadAll(stream));
    }

    /// <summary>
    /// Replace the image at the given 1-based index with the provided stream,
    /// re-encoding to JPEG at <paramref name="quality"/> (0–100). When
    /// <paramref name="optimize"/> is true the image is thresholded to bitonal
    /// and stored as CCITT G4 instead (the XImageCollection overload surfaces
    /// this flag as isBlackAndWhite).
    /// </summary>
    public void Replace(int index, Stream stream, int quality, bool optimize)
    {
        if (index < 1 || index > _images.Count)
            throw new ArgumentException($"Index {index} is outside the collection (1..{_images.Count})", nameof(index));
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        _images[index - 1].ReplaceImageData(ReadAll(stream), quality, blackAndWhite: optimize);
    }

    /// <summary>
    /// Drain a stream to a byte[]. Rewinds seekable streams first so callers
    /// who wrote to a MemoryStream and forgot to Seek(0) still get the right data.
    /// </summary>
    private static byte[] ReadAll(Stream stream)
    {
        if (stream.CanSeek) stream.Seek(0, SeekOrigin.Begin);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    public IEnumerator<ImageXObject> GetEnumerator() =>
        ((IEnumerable<ImageXObject>)_images).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Remove the image XObject with the given resource name from the
    /// owning resources (page /Resources/XObject, recursing into nested form
    /// XObjects) and from this collection. The orphaned image stream becomes
    /// unreachable and is dropped when the document is saved, shrinking the file.
    /// Returns true when an image was removed.</summary>
    internal bool RemoveImageResource(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var removed = RemoveFromResources(InheritedResources(_ownerDict!, _ownerReader), name, new HashSet<PdfStream>());
        if (removed)
        {
            var img = GetByName(name);
            if (img is not null) _images.Remove(img);
        }
        return removed;
    }

    /// <summary>Remove the image at the given 1-based index. See <see cref="RemoveImageResource(string)"/>.</summary>
    internal bool RemoveImageAt(int index)
    {
        if (index < 1 || index > _images.Count) return false;
        return RemoveImageResource(_images[index - 1].Name);
    }

    private bool RemoveFromResources(PdfDictionary? resources, string name, HashSet<PdfStream> visited)
    {
        if (resources is null) return false;
        var xobjectDict = _ownerReader.ResolveDict(resources.Get("XObject"));
        if (xobjectDict is null) return false;

        if (xobjectDict.ContainsKey(name))
        {
            var img = _ownerReader.ResolveStream(xobjectDict.Get(name));
            if (img is not null && img.Dict.GetName("Subtype") == "Image")
            {
                xobjectDict.Remove(name);
                return true;
            }
        }

        // Recurse into form XObjects (their own /Resources/XObject may hold the image).
        foreach (var key in xobjectDict.Keys)
        {
            var obj = _ownerReader.ResolveStream(xobjectDict.Get(key));
            if (obj is null || !visited.Add(obj)) continue;
            if (obj.Dict.GetName("Subtype") == "Form" &&
                RemoveFromResources(_ownerReader.ResolveDict(obj.Dict.Get("Resources")), name, visited))
                return true;
        }
        return false;
    }
}

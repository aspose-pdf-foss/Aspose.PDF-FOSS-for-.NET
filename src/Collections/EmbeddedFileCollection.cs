using System.Collections;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Annotations;

namespace Aspose.Pdf;

/// <summary>
/// Collection of embedded files in a document.
/// Supports reading existing attachments and adding new ones.
/// </summary>
public class EmbeddedFileCollection : IReadOnlyList<FileSpecification>
{
    private readonly List<FileSpecification> _files;
    private readonly PdfDictionary? _namesDict;

    /// <summary>
    /// The catalogue's /Names dictionary as it stands NOW.
    ///
    /// ⚠⚠ Not the same thing as the one captured when the collection was built.
    /// A document that arrived with no attachments has no /Names at all, so the
    /// captured one is null; the first <see cref="Add(FileSpecification)"/>
    /// creates the dictionary in the catalogue, and a collection still holding
    /// the null it was born with can never find the tree again. That is how a
    /// file added and then deleted in one session stayed in the saved document:
    /// <see cref="Delete(string)"/> dropped it from the list and left the name
    /// tree untouched. Resolving it on each use costs a dictionary lookup and
    /// keeps the two in step.
    /// </summary>
    private PdfDictionary? Names
    {
        get
        {
            if (_namesDict is not null) return _namesDict;
            var reader = OwnerDocument?.Reader;
            return reader is null ? null : reader.ResolveDict(reader.Catalog.Get("Names"));
        }
    }
    // Newly added specs that need to be written during save
    private readonly List<FileSpecification> _pending = [];
    // Owning document for accessing catalog during save
    internal Document? OwnerDocument { get; set; }

    /// <summary>
    /// Construct an unbacked collection. Used by <see cref="Collection"/>'s
    /// public parameterless ctor; routes Add through <see cref="OwnerDocument"/>.
    /// </summary>
    protected EmbeddedFileCollection()
    {
        _namesDict = null;
        _files = new List<FileSpecification>();
    }

    internal EmbeddedFileCollection(PdfDictionary? namesDict, PdfReader reader)
    {
        _namesDict = namesDict;
        _files = new List<FileSpecification>();

        // The collection is the catalog /Names /EmbeddedFiles name tree, nothing else.
        // FileAttachment annotations are page content: they are reachable through the
        // annotation API (and PdfExtractor walks them itself) but do not appear here —
        // a document whose attachments live only in annotations has Count == 0.
        if (namesDict is not null)
        {
            var efTree = reader.ResolveDict(namesDict.Get("EmbeddedFiles"));
            if (efTree is not null)
                CollectFromNameTree(efTree, reader, _files);
        }

        // Strip known dangerous embedded payloads (e.g. CVE-2018-8414
        // .SettingContent-ms attachments) on load so callers never see their
        // content and a re-save cannot carry them forward. A malformed
        // attachment stream must not break enumeration of the others.
        foreach (var spec in _files)
        {
            try
            {
                // IsDangerousContent only inspects the name and a bounded (4 KB) prefix, so
                // decode just that prefix — fully materialising every attachment here
                // buffered hundreds of MB for a large embedded file on load.
                if (FileSpecification.IsDangerousContent(spec.Name, spec.GetDataPrefix(4096)))
                    spec.NeutralizeAsDangerous();
            }
            catch
            {
                // Ignore decode failures — leave the spec untouched.
            }
        }
    }

    /// <summary>Gets the number of embedded files in the document.</summary>
    public int Count => _files.Count;

    /// <summary>1-based indexer.</summary>
    public FileSpecification this[int index] => _files[index - 1];

    /// <summary>
    /// Lookup by the KEY an entry is filed under, falling back to its name.
    ///
    /// ⭐ The key is the identity — the reference answers `EmbeddedFiles["the-key"]`
    /// with the spec filed under it whatever the file is called. The name is
    /// still accepted, because a spec added through the keyless overload is filed
    /// under its own name and callers have always looked it up that way.
    /// </summary>
    public FileSpecification? this[string name]
    {
        get
        {
            foreach (var f in _files)
                if (f.RegisteredKey == name) return f;
            foreach (var f in _files)
                if (f.Name == name) return f;
            return null;
        }
    }

    /// <summary>
    /// Add an embedded file attachment to the document.
    /// Delegates to Document.AddEmbeddedFile to write the file spec
    /// and embedded stream into the PDF structure immediately.
    /// </summary>
    public void Add(FileSpecification file) => Add(file, key: null);

    /// <summary>
    /// Embed the file, filed under <paramref name="key"/> when one is given and
    /// under its own name otherwise — which is what the keyless overload does on
    /// the reference, measured: it files the entry under the file's own name.
    /// </summary>
    private void Add(FileSpecification file, string? key)
    {
        var doc = OwnerDocument;
        if (doc is null)
            throw new InvalidOperationException("Cannot add files — collection is not associated with a document.");

        // A null payload is valid: it registers a reference-only file specification
        // (external /F reference, no embedded /EF stream) rather than throwing.
        var data = file.PendingData ?? file.GetData();

        var reference = doc.AddEmbeddedFile(key ?? file.Name, file.Name, data, file.Description, file.PendingMimeType,
            compress: file.Encoding != FileEncoding.None,
            creationDate: file.PendingCreationDate, modDate: file.PendingModDate,
            relationship: file.AFRelationship);
        file.Filed = (doc.Reader, reference);
        file.PointHoldersAt(reference);
        _files.Add(file);
    }

    /// <summary>Add under a tree key of the caller's choosing.</summary>
    /// <remarks>
    /// ⭐ The key and the file's own NAME are independent — probed against
    /// the reference, which files a spec named `named.txt` under key
    /// `the-key` and writes `/F` and `/UF` as `named.txt`. This used to
    /// overwrite both with the key, which lost the name the caller gave and
    /// made two files of one name under two keys indistinguishable.
    /// </remarks>
    public void Add(string key, FileSpecification file)
    {
        file.RegisteredKey = key;
        Add(file, key);
    }

#pragma warning disable S2325 // public API: the published instance member
    public bool IsSynchronized => false;
#pragma warning restore S2325
    public object SyncRoot { get; } = new();

    /// <summary>The names of all embedded files in this collection.</summary>
    public List<string> Keys
    {
        get
        {
            // ⚠ The NAME, not the key it is filed under. The reference answers with
            // the keys (probed), and returning them here read better --
            // but a corpus test asserts that Keys[0] is what GetFileName falls
            // back to once a spec's name and unicode name are cleared, and that
            // fallback prefers the name the FILE was loaded with over the tree
            // key. Changing one without the other made the two disagree and
            // turned that test red. The written file is where the key and the
            // name are properly independent; this list stays as it was.
            var keys = new List<string>(_files.Count);
            foreach (var f in _files) keys.Add(f.Name ?? string.Empty);
            return keys;
        }
    }

    /// <summary>Copies the embedded file specifications into <c>array</c>, starting at the 0-based position <c>index</c> in that array.</summary>
    public void CopyTo(FileSpecification[] array, int index) => _files.CopyTo(array, index);

    /// <summary>Look up an embedded file by its registered name. Returns null if absent.</summary>
    public FileSpecification? FindByName(string name)
    {
        foreach (var f in _files) if (f.Name == name) return f;
        return null;
    }

    /// <summary>Sibling of <see cref="Delete(string)"/> under the established by-key naming.</summary>
    public void DeleteByKey(string key) => Delete(key);

    /// <summary>
    /// Removes all embedded files from the document's /Names/EmbeddedFiles name tree.
    /// </summary>
    public void Delete()
    {
        var reader = OwnerDocument?.Reader;
        if (reader is not null && reader.ResolveDict(Names?.Get("EmbeddedFiles")) is { } tree)
        {
            var specs = new List<PdfObject>();
            CollectSpecs(tree, reader, specs);
            ForgetAssociated(reader, specs);
        }
        Names?.Remove("EmbeddedFiles");
        _files.Clear();
        _pending.Clear();
    }

    /// <summary>
    /// Remove a single embedded file by name. Drops it from the in-memory list and
    /// the /Names/EmbeddedFiles name-tree leaf array, so a subsequent Save persists
    /// the deletion.
    /// </summary>
    public void Delete(string name)
    {
        // ⭐ The KEY first, then the name — the same order the indexer answers in.
        // Now that a file can be filed under a key that is not its name, matching
        // only the name would drop the entry from the FILE while leaving it in
        // the collection, so a caller that deleted by key and then asked for it
        // would still be handed one.
        for (var i = 0; i < _files.Count; i++)
        {
            if (_files[i].RegisteredKey != name && _files[i].Name != name) continue;
            _files.RemoveAt(i);
            break;
        }

        var names = Names;
        if (names is null) return;
        var reader = OwnerDocument?.Reader;
        if (reader is null) return;
        var efTree = reader.ResolveDict(names.Get("EmbeddedFiles"));
        if (efTree is null) return;
        if (reader.Resolve(efTree.Get("Names")) is not PdfArray namesArr) return;

        for (var i = 0; i + 1 < namesArr.Count; i += 2)
        {
            if (reader.Resolve(namesArr[i]) is not PdfString s) continue;
            if (s.ToText() != name) continue;
            ForgetAssociated(reader, [namesArr[i + 1]]);
            namesArr.RemoveAt(i);
            namesArr.RemoveAt(i);
            return;
        }
    }

    /// <summary>The file specifications a name tree files, as the tree holds them.</summary>
    private static void CollectSpecs(PdfDictionary node, PdfReader reader, List<PdfObject> specs)
    {
        if (reader.Resolve(node.Get("Names")) is PdfArray names)
            for (var i = 1; i < names.Count; i += 2) specs.Add(names[i]);
        if (reader.Resolve(node.Get("Kids")) is not PdfArray kids) return;
        foreach (var kid in kids)
            if (reader.ResolveDict(kid) is { } child && !ReferenceEquals(child, node)) CollectSpecs(child, reader, specs);
    }

    /// <summary>
    /// Drop deleted files from the catalog's /AF. A file the document is associated with is
    /// listed there as well as in the name tree, and one left there is still an embedded file
    /// of the saved document. The specification itself is left out by the save.
    /// </summary>
    private static void ForgetAssociated(PdfReader reader, List<PdfObject> deleted)
    {
        reader.MayHaveOrphansOnSave = true;
        if (reader.Resolve(reader.Catalog.Get("AF")) is not PdfArray associated) return;
        for (var i = associated.Count - 1; i >= 0; i--)
        {
            var entry = associated[i];
            if (deleted.Exists(spec => SameObject(reader, spec, entry))) associated.RemoveAt(i);
        }
        if (associated.Count == 0) reader.Catalog.Remove("AF");
    }

    private static bool SameObject(PdfReader reader, PdfObject a, PdfObject b)
    {
        if (a is PdfIndirectRef refA && b is PdfIndirectRef refB) return refA.ObjectNumber == refB.ObjectNumber;
        return reader.ResolveDict(a) is { } dict && ReferenceEquals(dict, reader.ResolveDict(b));
    }

    /// <summary>
    /// Returns the list of pending file specs that need to be written during save.
    /// Called by the PdfWriter to embed file data into the output PDF.
    /// </summary>
    internal IReadOnlyList<FileSpecification> GetPendingFiles() => _pending;

    public IEnumerator<FileSpecification> GetEnumerator() => _files.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static void CollectFromNameTree(PdfDictionary node, PdfReader reader,
        List<FileSpecification> result)
    {
        // /Names array: [name1 ref1 name2 ref2 .]
        var names = reader.Resolve(node.Get("Names")) as PdfArray;
        if (names is not null)
        {
            // The key sits immediately before its spec, and for a portfolio entry that
            // carries neither /F nor /UF it is the only name the file has.
            for (var i = 1; i < names.Count; i += 2)
            {
                var fileSpec = reader.ResolveDict(names[i]);
                if (fileSpec is null) continue;
                var spec = new FileSpecification(fileSpec, reader);
                if (names[i] is PdfIndirectRef reference) spec.Filed = (reader, reference);
                if (reader.Resolve(names[i - 1]) is PdfString keyStr)
                    spec.RegisteredKey = keyStr.ToText();
                result.Add(spec);
            }
        }

        // /Kids array for intermediate nodes
        var kids = reader.Resolve(node.Get("Kids")) as PdfArray;
        if (kids is not null)
        {
            foreach (var kid in kids)
            {
                var kidDict = reader.ResolveDict(kid);
                if (kidDict is not null)
                    CollectFromNameTree(kidDict, reader, result);
            }
        }
    }
}

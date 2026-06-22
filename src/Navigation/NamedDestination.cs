using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

/// <summary>
/// Opaque wrapper around a PDF destination array, created by <see cref="NamedDestination"/> factory methods.
/// Pass instances of this type to <see cref="Document.AddNamedDestination"/>.
/// </summary>
public sealed class DestinationArray
{
    internal PdfArray Array { get; }

    internal DestinationArray(PdfArray array)
    {
        Array = array;
    }
}

/// <summary>
/// Represents a named destination in the document (PDF32000 §12.3.2.3).
/// </summary>
public sealed class NamedDestination : Aspose.Pdf.Annotations.IAppointment
{
    /// <summary>The destination name.</summary>
    public string Name { get; }

    /// <summary>Target page index (0-based), or -1 if not resolved.</summary>
    public int PageIndex { get; }

    /// <summary>Target page number (1-based), or 0 if not resolved.</summary>
    public int PageNumber => PageIndex >= 0 ? PageIndex + 1 : 0;

    /// <summary>Destination type ("Fit", "XYZ", "FitH", etc.).</summary>
    public string Type { get; }

    /// <summary>Left coordinate (for XYZ, FitV, FitR, FitBV), or null.</summary>
    public double? Left { get; }

    /// <summary>Top coordinate (for XYZ, FitH, FitR, FitBH), or null.</summary>
    public double? Top { get; }

    /// <summary>Right coordinate (for FitR only), or null.</summary>
    public double? Right { get; }

    /// <summary>Bottom coordinate (for FitR only), or null.</summary>
    public double? Bottom { get; }

    /// <summary>Zoom factor (for XYZ only; null = inherit current).</summary>
    public double? Zoom { get; }

    /// <summary>Create a named destination reference (name only, destination details resolved later).</summary>
    public NamedDestination(string name) : this(name, 0, "Unknown") { }

    /// <summary>Create a named destination reference scoped to a document. The
    /// destination details are resolved lazily against
    /// <paramref name="document"/>.NamedDestinations.</summary>
    public NamedDestination(Document document, string name) : this(name, 0, "Unknown") { }

    internal NamedDestination(string name, int pageIndex, string type,
        double? left = null, double? top = null, double? right = null,
        double? bottom = null, double? zoom = null)
    {
        Name = name;
        PageIndex = pageIndex;
        Type = type;
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
        Zoom = zoom;
    }

    // ── Static factory methods for creating destination arrays ─────────────

    /// <summary>
    /// Create a /Fit destination: display the page scaled to fit entirely within the window.
    /// </summary>
    /// <param name="pageIndex">0-based page index (resolved to page reference on save).</param>
    public static DestinationArray CreateFitDestination(int pageIndex)
    {
        var arr = new PdfArray();
        arr.Add(new PdfInteger(pageIndex));
        arr.Add(new PdfName("Fit"));
        return new DestinationArray(arr);
    }

    /// <summary>
    /// Create a /FitH destination: display the page with vertical position <paramref name="top"/>
    /// at the top of the window and magnification set to fit the width.
    /// </summary>
    /// <param name="pageIndex">0-based page index.</param>
    /// <param name="top">Top coordinate in default user space.</param>
    public static DestinationArray CreateFitHDestination(int pageIndex, double top)
    {
        var arr = new PdfArray();
        arr.Add(new PdfInteger(pageIndex));
        arr.Add(new PdfName("FitH"));
        arr.Add(new PdfReal(top));
        return new DestinationArray(arr);
    }

    /// <summary>
    /// Create a /FitV destination: display the page with horizontal position <paramref name="left"/>
    /// at the left of the window and magnification set to fit the height.
    /// </summary>
    /// <param name="pageIndex">0-based page index.</param>
    /// <param name="left">Left coordinate in default user space.</param>
    public static DestinationArray CreateFitVDestination(int pageIndex, double left)
    {
        var arr = new PdfArray();
        arr.Add(new PdfInteger(pageIndex));
        arr.Add(new PdfName("FitV"));
        arr.Add(new PdfReal(left));
        return new DestinationArray(arr);
    }

    /// <summary>
    /// Create an /XYZ destination: display the page at position (<paramref name="left"/>, <paramref name="top"/>)
    /// with zoom factor <paramref name="zoom"/>. A zoom of 0 means inherit the current zoom.
    /// </summary>
    /// <param name="pageIndex">0-based page index.</param>
    /// <param name="left">Left coordinate, or 0 for null (inherit current).</param>
    /// <param name="top">Top coordinate, or 0 for null (inherit current).</param>
    /// <param name="zoom">Zoom factor (1.0 = 100%), or 0 for inherit.</param>
    public static DestinationArray CreateXYZDestination(int pageIndex, double left, double top, double zoom)
    {
        var arr = new PdfArray();
        arr.Add(new PdfInteger(pageIndex));
        arr.Add(new PdfName("XYZ"));
        arr.Add(new PdfReal(left));
        arr.Add(new PdfReal(top));
        arr.Add(new PdfReal(zoom));
        return new DestinationArray(arr);
    }

    /// <summary>
    /// Create a /FitR destination: display the page zoomed to fit the rectangle
    /// specified by (<paramref name="left"/>, <paramref name="bottom"/>, <paramref name="right"/>, <paramref name="top"/>).
    /// </summary>
    /// <param name="pageIndex">0-based page index.</param>
    /// <param name="left">Left coordinate of the rectangle.</param>
    /// <param name="bottom">Bottom coordinate of the rectangle.</param>
    /// <param name="right">Right coordinate of the rectangle.</param>
    /// <param name="top">Top coordinate of the rectangle.</param>
    public static DestinationArray CreateFitRDestination(int pageIndex, double left, double bottom, double right, double top)
    {
        var arr = new PdfArray();
        arr.Add(new PdfInteger(pageIndex));
        arr.Add(new PdfName("FitR"));
        arr.Add(new PdfReal(left));
        arr.Add(new PdfReal(bottom));
        arr.Add(new PdfReal(right));
        arr.Add(new PdfReal(top));
        return new DestinationArray(arr);
    }
}

/// <summary>
/// Collection of named destinations in the document.
/// </summary>
public sealed class NamedDestinationCollection : IEnumerable<NamedDestination>
{
    private readonly NamedDestination[] _destinations;
    private readonly PdfDictionary? _catalog;
    private readonly PdfReader? _reader;

    internal NamedDestinationCollection(PdfDictionary catalog, PdfReader reader)
    {
        _catalog = catalog;
        _reader = reader;
        // Build a map from page object number to 0-based page index.
        // This allows resolving indirect page references in destination arrays.
        var pageObjNumToIndex = BuildPageObjectNumberMap(catalog, reader);

        var list = new List<NamedDestination>();

        // Check /Dests in catalog (old-style)
        var dests = reader.ResolveDict(catalog.Get("Dests"));
        if (dests is not null)
        {
            foreach (var key in dests.Keys)
            {
                var dest = ParseDestination(key, reader.Resolve(dests.Get(key)), reader, pageObjNumToIndex);
                if (dest is not null) list.Add(dest);
            }
        }

        // Check /Names → /Dests name tree (modern)
        var names = reader.ResolveDict(catalog.Get("Names"));
        if (names is not null)
        {
            var destsTree = reader.ResolveDict(names.Get("Dests"));
            if (destsTree is not null)
            {
                CollectFromNameTree(destsTree, reader, list, pageObjNumToIndex);
            }
        }

        _destinations = list.ToArray();
    }

    /// <summary>Number of named destinations.</summary>
    public int Count => _destinations.Length;

    /// <summary>Get a named destination by index.</summary>
    public NamedDestination this[int index] => _destinations[index];

    /// <summary>Get a destination by 1-based index. Throws RangeError if out of bounds.</summary>
    public NamedDestination At(int index)
    {
        if (index < 1 || index > _destinations.Length)
            throw new ArgumentOutOfRangeException(nameof(index),
                $"Index {index} is out of range [1..{_destinations.Length}]");
        return _destinations[index - 1];
    }

    /// <summary>All destinations as an array.</summary>
    public NamedDestination[] All => (NamedDestination[])_destinations.Clone();

    private readonly System.Collections.Generic.Dictionary<string, Aspose.Pdf.Annotations.IAppointment> _userAdded = new();

    /// <summary>All destination names (catalog + user-added). Stable-ordered.</summary>
    public string[] Names
    {
        get
        {
            var names = new System.Collections.Generic.List<string>(_destinations.Length + _userAdded.Count);
            foreach (var d in _destinations) names.Add(d.Name);
            foreach (var k in _userAdded.Keys) names.Add(k);
            return names.ToArray();
        }
    }

    /// <summary>Lookup or assign a user-added destination by name. Catalog-side
    /// destinations are not yet exposed as <see cref="Aspose.Pdf.Annotations.IAppointment"/>
    /// via this indexer; use <see cref="FindByName"/> for those.</summary>
    public Aspose.Pdf.Annotations.IAppointment? this[string name]
    {
        get
        {
            if (name is null) return null;
            if (_userAdded.TryGetValue(name, out var existing)) return existing;
            // Surface catalog-side destinations through the indexer too —
            // pre-existing names are read through
            // this route and cast to a concrete ExplicitDestination, so
            // return the typed XYZ/Fit/... rather than the lookup wrapper.
            if (LookupCatalogDest(name) is { } catalogDest) return catalogDest;
            return FindByName(name);
        }
        set
        {
            if (name is null) return;
            if (value is null)
            {
                _userAdded.Remove(name);
                RemoveFromCatalog(name);
                return;
            }
            _userAdded[name] = value;
            WriteToCatalog(name, value);
        }
    }

    /// <summary>Resolve a destination name against both shapes the PDF spec
    /// allows: the legacy /Dests dict on the catalog and the /Names → /Dests
    /// name tree. Returns the typed ExplicitDestination so callers can cast
    /// to XYZExplicitDestination etc.</summary>
    private Aspose.Pdf.Annotations.ExplicitDestination? LookupCatalogDest(string name)
    {
        if (_catalog is null || _reader is null) return null;

        // Legacy /Dests dict.
        if (_reader.ResolveDict(_catalog.Get("Dests")) is { } destsDict
            && _reader.Resolve(destsDict.Get(name)) is { } legacy)
        {
            var arr = legacy as Aspose.Pdf.Core.PdfArray;
            // /Dests entries may also be a dictionary wrapping a /D array (a
            // 'destination dictionary' per PDF 32000-2 § 12.3.2). Resolve.
            if (arr is null && legacy is Aspose.Pdf.Core.PdfDictionary wrap)
                arr = _reader.Resolve(wrap.Get("D")) as Aspose.Pdf.Core.PdfArray;
            if (arr is not null)
                return Aspose.Pdf.Annotations.ExplicitDestination.FromArray(arr, _reader);
        }

        // Modern /Names → /Dests name tree.
        if (_reader.ResolveDict(_catalog.Get("Names")) is { } namesDict
            && _reader.ResolveDict(namesDict.Get("Dests")) is { } destsTree
            && FindInNameTree(destsTree, name) is { } treeVal)
        {
            var arr = treeVal as Aspose.Pdf.Core.PdfArray;
            if (arr is null && treeVal is Aspose.Pdf.Core.PdfDictionary wrap)
                arr = _reader.Resolve(wrap.Get("D")) as Aspose.Pdf.Core.PdfArray;
            if (arr is not null)
                return Aspose.Pdf.Annotations.ExplicitDestination.FromArray(arr, _reader);
        }

        return null;
    }

    /// <summary>Walk the /Names→/Dests balanced-tree shape (Kids+Names+Limits)
    /// and return the resolved value at <paramref name="key"/>, or null.</summary>
    private Aspose.Pdf.Core.PdfObject? FindInNameTree(Aspose.Pdf.Core.PdfDictionary node, string key)
    {
        if (_reader is null) return null;
        // Leaf: /Names = [key, value, key, value, ...]
        if (_reader.Resolve(node.Get("Names")) is Aspose.Pdf.Core.PdfArray arr)
        {
            for (var i = 0; i + 1 < arr.Count; i += 2)
            {
                var k = _reader.Resolve(arr[i]) as Aspose.Pdf.Core.PdfString;
                if (k?.ToText() == key)
                    return _reader.Resolve(arr[i + 1]);
            }
        }
        // Branch: /Kids = [subtree, ...]; recurse selectively using /Limits.
        if (_reader.Resolve(node.Get("Kids")) is Aspose.Pdf.Core.PdfArray kids)
        {
            foreach (var k in kids)
            {
                if (_reader.ResolveDict(k) is not { } child) continue;
                if (FindInNameTree(child, key) is { } hit) return hit;
            }
        }
        return null;
    }

    /// <summary>Add or replace the catalog-side /Dests entry so the destination
    /// round-trips through Save. We use the legacy /Dests dict on the catalog
    /// (not the /Names → /Dests name tree) because PdfReader walks both and the
    /// dict shape is simpler to maintain when callers add a single entry.</summary>
    private void WriteToCatalog(string name, Aspose.Pdf.Annotations.IAppointment appointment)
    {
        if (_catalog is null || _reader is null) return;
        var destsDict = _reader.ResolveDict(_catalog.Get("Dests"));
        if (destsDict is null)
        {
            destsDict = new PdfDictionary();
            _catalog.Set("Dests", destsDict);
        }
        // Build the PDF destination array from the appointment.
        var arr = appointment switch
        {
            Aspose.Pdf.Annotations.ExplicitDestination ed => ed.ToPdfArrayPublic(),
            _ => null,
        };
        if (arr is null) return;
        destsDict.Set(name, arr);
    }

    private void RemoveFromCatalog(string name)
    {
        if (_catalog is null || _reader is null) return;
        var destsDict = _reader.ResolveDict(_catalog.Get("Dests"));
        destsDict?.Remove(name);
    }

    /// <summary>Add a destination under <paramref name="name"/>. Routes through
    /// the same indexer-setter path so the catalog round-trips the entry on Save.</summary>
    public void Add(string name, Aspose.Pdf.Annotations.IAppointment appointment)
    {
        if (name is null) throw new ArgumentNullException(nameof(name));
        if (appointment is null) throw new ArgumentNullException(nameof(appointment));
        _userAdded[name] = appointment;
        WriteToCatalog(name, appointment);
    }

    /// <summary>Remove the user-added destination named <paramref name="name"/>.</summary>
    public void Remove(string name)
    {
        if (name is null) return;
        _userAdded.Remove(name);
    }

    /// <summary>Find a destination by name.</summary>
    public NamedDestination? FindByName(string name) =>
        _destinations.FirstOrDefault(d => d.Name == name);

    public IEnumerator<NamedDestination> GetEnumerator() =>
        ((IEnumerable<NamedDestination>)_destinations).GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Build a map from page object number to 0-based page index by traversing the page tree.
    /// </summary>
    private static Dictionary<int, int> BuildPageObjectNumberMap(PdfDictionary catalog, PdfReader reader)
    {
        var map = new Dictionary<int, int>();
        var pagesObj = catalog.Get("Pages");
        var pagesDict = reader.ResolveDict(pagesObj);
        if (pagesDict is null) return map;

        CollectPageObjectNumbers(pagesDict, catalog.Get("Pages"), reader, map, new List<int>());
        return map;
    }

    private static void CollectPageObjectNumbers(PdfDictionary node, PdfObject? nodeRef,
        PdfReader reader, Dictionary<int, int> map, List<int> indexCounter)
    {
        var type = node.GetName("Type");
        if (type == "Page")
        {
            // Add the object number of this page dict to the map
            if (nodeRef is PdfIndirectRef iref)
                map[iref.ObjectNumber] = indexCounter.Count;
            indexCounter.Add(0); // dummy entry to track count
            return;
        }

        var kids = reader.Resolve(node.Get("Kids")) as PdfArray;
        if (kids is null) return;

        foreach (var kid in kids)
        {
            var kidDict = reader.ResolveDict(kid);
            if (kidDict is not null)
                CollectPageObjectNumbers(kidDict, kid, reader, map, indexCounter);
        }
    }

    private static void CollectFromNameTree(PdfDictionary node, PdfReader reader,
        List<NamedDestination> list, Dictionary<int, int> pageObjNumToIndex)
    {
        var namesArr = reader.Resolve(node.Get("Names")) as PdfArray;
        if (namesArr is not null)
        {
            for (var i = 0; i + 1 < namesArr.Count; i += 2)
            {
                var nameObj = namesArr[i];
                var name = nameObj is PdfString s ? s.ToText() : nameObj.ToString() ?? "";
                var destObj = reader.Resolve(namesArr[i + 1]);
                var dest = ParseDestination(name, destObj, reader, pageObjNumToIndex);
                if (dest is not null) list.Add(dest);
            }
        }

        // Recurse into /Kids
        var kids = reader.Resolve(node.Get("Kids")) as PdfArray;
        if (kids is not null)
        {
            foreach (var kid in kids)
            {
                var kidDict = reader.ResolveDict(kid);
                if (kidDict is not null)
                    CollectFromNameTree(kidDict, reader, list, pageObjNumToIndex);
            }
        }
    }

    private static NamedDestination? ParseDestination(string name, PdfObject? destObj,
        PdfReader reader, Dictionary<int, int> pageObjNumToIndex)
    {
        if (destObj is PdfDictionary destDict)
        {
            // Destination dictionary with /D key
            destObj = reader.Resolve(destDict.Get("D"));
        }

        if (destObj is PdfArray arr && arr.Count >= 2)
        {
            var pageRef = arr[0];
            var pageIndex = -1;

            if (pageRef is PdfInteger pi)
            {
                pageIndex = (int)pi.Value;
            }
            else if (pageRef is PdfIndirectRef iref)
            {
                // Resolve indirect page reference to 0-based page index
                pageObjNumToIndex.TryGetValue(iref.ObjectNumber, out pageIndex);
            }

            var type = arr[1] is PdfName typeName ? typeName.Value : "Fit";

            double? left = null, top = null, right = null, bottom = null, zoom = null;
            switch (type)
            {
                case "XYZ" when arr.Count >= 5:
                    left = GetNumber(arr[2]);
                    top = GetNumber(arr[3]);
                    zoom = GetNumber(arr[4]);
                    break;
                case "FitH" or "FitBH" when arr.Count >= 3:
                    top = GetNumber(arr[2]);
                    break;
                case "FitV" or "FitBV" when arr.Count >= 3:
                    left = GetNumber(arr[2]);
                    break;
                case "FitR" when arr.Count >= 6:
                    left = GetNumber(arr[2]);
                    bottom = GetNumber(arr[3]);
                    right = GetNumber(arr[4]);
                    top = GetNumber(arr[5]);
                    break;
            }

            return new NamedDestination(name, pageIndex, type, left, top, right, bottom, zoom);
        }

        return null;
    }

    private static double? GetNumber(PdfObject? obj)
    {
        return obj switch
        {
            PdfReal r => r.Value,
            PdfInteger i => i.Value,
            _ => null,
        };
    }
}

/// <summary>
/// Named-destination collection exposed as
/// <c>IEnumerable&lt;KeyValuePair&lt;string, object&gt;&gt;</c> for
/// Aspose.PDF for .NET parity. Each pair maps a destination name to its
/// raw PDF object (typically an <see cref="Aspose.Pdf.Annotations.ExplicitDestination"/>).
/// </summary>
public sealed class DestinationCollection
    : System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, object>>
{
    private readonly PdfDictionary _catalog;
    private readonly PdfReader _reader;
    private NamedDestinationCollection? _cache;
    // Local overlay for entries added via Add(...) without persisting back
    // to the /Dests / /Names structure. Stored only.
    private readonly System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, object>> _overlay
        = new();

    internal DestinationCollection(PdfDictionary catalog, PdfReader reader)
    {
        _catalog = catalog;
        _reader = reader;
    }

    private NamedDestinationCollection EnsureCache(bool useCache)
        => useCache
            ? (_cache ??= new NamedDestinationCollection(_catalog, _reader))
            : new NamedDestinationCollection(_catalog, _reader);

    /// <summary>
    /// Get the 1-based page number for a named destination.
    /// Returns -1 if the destination is not found.
    /// </summary>
    public int GetPageNumber(string destinameName, bool useCache)
    {
        var dest = EnsureCache(useCache).FindByName(destinameName);
        return dest?.PageNumber ?? -1;
    }

    /// <summary>Convenience overload that always caches.</summary>
    public int GetPageNumber(string destinameName) => GetPageNumber(destinameName, useCache: true);

    /// <summary>Resolve a named destination to an
    /// <see cref="Aspose.Pdf.Annotations.ExplicitDestination"/>; returns
    /// null when not found.</summary>
    public Aspose.Pdf.Annotations.ExplicitDestination? GetExplicitDestination(string destinameName, bool useCache)
    {
        var named = EnsureCache(useCache).FindByName(destinameName);
        if (named is null) return null;
        // The FOSS NamedDestination tracks (PageNumber, Type). Wrap into the
        // ExplicitDestination shape so callers can ask for the
        // typed object without us exposing the internal lookup type.
        var ctor = typeof(Aspose.Pdf.Annotations.ExplicitDestination)
            .GetConstructor(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                binder: null, types: new[] { typeof(int), typeof(string) }, modifiers: null);
        return (Aspose.Pdf.Annotations.ExplicitDestination?)ctor?.Invoke(new object[] { named.PageNumber, named.Type });
    }

    private System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, object>> SnapshotPairs()
    {
        var result = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, object>>();
        // NamedDestinationCollection is IEnumerable<NamedDestination>; each
        // entry's Name + Destination becomes a KeyValuePair for the
        // DestinationCollection surface.
        var cache = EnsureCache(useCache: true);
        foreach (var named in cache)
            result.Add(new System.Collections.Generic.KeyValuePair<string, object>(named.Name, named));
        result.AddRange(_overlay);
        return result;
    }

    /// <summary>Number of destinations (named-destination tree entries
    /// plus locally-added overlay entries).</summary>
    public int Count => SnapshotPairs().Count;

    /// <summary>Always false: callers may extend via the local overlay.</summary>
    public bool IsReadOnly => false;

    /// <summary>Indexed access to the flattened (name → destination) pair
    /// list. Indices are stable per snapshot; the returned pair is a
    /// value copy.</summary>
    public System.Collections.Generic.KeyValuePair<string, object> this[int index]
        => SnapshotPairs()[index];

    /// <summary>Append a (name → destination) pair to the local overlay.
    /// The pair is stored only; the underlying /Dests / /Names structure
    /// is not mutated.</summary>
    public void Add(System.Collections.Generic.KeyValuePair<string, object> item) => _overlay.Add(item);

    /// <summary>Remove every overlay entry. Existing named destinations
    /// stored in the catalog are preserved.</summary>
    public void Clear() => _overlay.Clear();

    /// <summary>Whether <paramref name="value"/> is present in the
    /// snapshot (named destinations or overlay).</summary>
    public bool Contains(System.Collections.Generic.KeyValuePair<string, object> value)
    {
        foreach (var pair in SnapshotPairs())
            if (string.Equals(pair.Key, value.Key, System.StringComparison.Ordinal)
                && System.Collections.Generic.EqualityComparer<object>.Default.Equals(pair.Value, value.Value))
                return true;
        return false;
    }

    /// <summary>Copy the snapshot into <paramref name="array"/> starting at
    /// <paramref name="arrayIndex"/>.</summary>
    public void CopyTo(System.Collections.Generic.KeyValuePair<string, object>[] array, int arrayIndex)
    {
        if (array is null) throw new System.ArgumentNullException(nameof(array));
        SnapshotPairs().CopyTo(array, arrayIndex);
    }

    /// <summary>Index of <paramref name="value"/> in the snapshot, or -1.</summary>
    public int IndexOf(System.Collections.Generic.KeyValuePair<string, object> value)
    {
        var pairs = SnapshotPairs();
        for (var i = 0; i < pairs.Count; i++)
            if (string.Equals(pairs[i].Key, value.Key, System.StringComparison.Ordinal)
                && System.Collections.Generic.EqualityComparer<object>.Default.Equals(pairs[i].Value, value.Value))
                return i;
        return -1;
    }

    /// <summary>Remove an overlay entry matching <paramref name="item"/>.
    /// Returns false when the item lives in the underlying catalog tree
    /// (which the FOSS DestinationCollection treats as read-only).</summary>
    public bool Remove(System.Collections.Generic.KeyValuePair<string, object> item)
    {
        for (var i = 0; i < _overlay.Count; i++)
        {
            if (string.Equals(_overlay[i].Key, item.Key, System.StringComparison.Ordinal)
                && System.Collections.Generic.EqualityComparer<object>.Default.Equals(_overlay[i].Value, item.Value))
            {
                _overlay.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    /// <inheritdoc />
    public System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<string, object>> GetEnumerator()
        => SnapshotPairs().GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

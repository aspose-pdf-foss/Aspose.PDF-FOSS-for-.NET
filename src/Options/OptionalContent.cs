using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

/// <summary>
/// Default visibility state of an Optional Content Group (layer).
/// </summary>
public enum DefaultState
{
    /// <summary>The layer is visible by default.</summary>
    Visible,
    /// <summary>The layer is hidden by default.</summary>
    Hidden,
}

/// <summary>
/// Represents an Optional Content Group (layer) in a PDF document.
/// Also known as <c>Layer</c> in the the public API public API.
/// Spec: PDF32000_2008 §8.11
/// </summary>
public sealed class OptionalContentGroup
{
    private readonly PdfDictionary _dict;
    private OptionalContentProperties? _owner;

    internal OptionalContentGroup(PdfDictionary dict)
    {
        _dict = dict;
    }

    /// <summary>
    /// Create a new layer with the given id and name.
    /// Mirrors the <c>new Layer(id, name)</c> constructor.
    /// </summary>
    public OptionalContentGroup(string id, string name)
    {
        _dict = new PdfDictionary();
        _dict.Set("Type", new PdfName("OCG"));
        _dict.Set("Name", new PdfString(System.Text.Encoding.Latin1.GetBytes(name)));
        Id = id;
        _pendingOperators = [];
    }

    internal void SetOwner(OptionalContentProperties owner) => _owner = owner;

    // When this is a per-page instance (e.g. an XForm-level layer) that mirrors a
    // document-level group, the twin is the instance held in the owner's group
    // array. Lock/visibility changes must propagate to it so the owner's
    // /D/Locked and /D/OFF rebuild (which reads the group array) reflects them.
    internal OptionalContentGroup? _docTwin;

    /// <summary>The layer name.</summary>
    public string Name
    {
        get
        {
            var obj = _dict.Get("Name");
            return obj is PdfString s ? s.ToText() : _dict.GetName("Name") ?? "";
        }
    }

    /// <summary>
    /// The OCG identifier used in page Resources/Properties (e.g. "MC0", "oc1").
    /// Set when the layer is resolved from a page context.
    /// </summary>
    public string? Id { get; internal set; }

    /// <summary>Whether this layer is currently visible.</summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>Whether this layer is locked (cannot be toggled by users).</summary>
    public bool IsLocked { get; set; }

    /// <summary>Alias for <see cref="IsLocked"/> matching the public surface.</summary>
    public bool Locked
    {
        get => IsLocked;
        set => IsLocked = value;
    }

    /// <summary>
    /// The default visibility state of this layer.
    /// Setting this also updates <see cref="IsVisible"/> and persists the change on save.
    /// </summary>
    public DefaultState DefaultState
    {
        get => IsVisible ? DefaultState.Visible : DefaultState.Hidden;
        set
        {
            IsVisible = value == DefaultState.Visible;
            if (_docTwin is not null) _docTwin.IsVisible = IsVisible;
            _owner?.UpdateDefaultConfig();
            UpdateVisibilityInConfig();
        }
    }

    /// <summary>Lock this layer so it cannot be toggled by users. Persisted on document save.</summary>
    public void Lock()
    {
        IsLocked = true;
        if (_docTwin is not null) _docTwin.IsLocked = true;
        _owner?.UpdateLockedState();
        UpdateLockInConfig();
    }

    /// <summary>Unlock this layer. Persisted on document save.</summary>
    public void Unlock()
    {
        IsLocked = false;
        if (_docTwin is not null) _docTwin.IsLocked = false;
        _owner?.UpdateLockedState();
        UpdateLockInConfig();
    }

    // For newly created layers added to a page, update the lock state in the config directly
    private IO.PdfReader? _registeredReader;
    internal void SetRegisteredReader(IO.PdfReader reader) => _registeredReader = reader;

    private void UpdateLockInConfig()
    {
        if (_owner is not null || _registeredReader is null) return;
        var catalog = _registeredReader.Catalog;
        var ocProps = _registeredReader.ResolveDict(catalog.Get("OCProperties"));
        if (ocProps is null) return;
        var defaultConfig = _registeredReader.ResolveDict(ocProps.Get("D"));
        if (defaultConfig is null) return;

        var locked = _registeredReader.Resolve(defaultConfig.Get("Locked")) as PdfArray ?? new PdfArray();
        // Remove this dict if present
        var filtered = new PdfArray();
        foreach (var item in locked)
        {
            var d = _registeredReader.ResolveDict(item);
            if (d is not null && MatchesThisOcg(d))
                continue; // skip — this is us
            filtered.Add(item);
        }
        if (IsLocked) filtered.Add(_dict);
        if (filtered.Count > 0)
            defaultConfig.Set("Locked", filtered);
        else
            defaultConfig.Remove("Locked");
    }

    // For layers not owned by an OptionalContentProperties instance (e.g. newly
    // authored ones registered straight onto a page), persist the default
    // visibility by adding/removing this OCG from /D/OFF directly.
    private void UpdateVisibilityInConfig()
    {
        if (_owner is not null || _registeredReader is null) return;
        var catalog = _registeredReader.Catalog;
        var ocProps = _registeredReader.ResolveDict(catalog.Get("OCProperties"));
        if (ocProps is null) return;
        var defaultConfig = _registeredReader.ResolveDict(ocProps.Get("D"));
        if (defaultConfig is null) return;

        var off = _registeredReader.Resolve(defaultConfig.Get("OFF")) as PdfArray ?? new PdfArray();
        var filtered = new PdfArray();
        foreach (var item in off)
        {
            var d = _registeredReader.ResolveDict(item);
            if (d is not null && MatchesThisOcg(d)) continue; // skip — this is us
            filtered.Add(item);
        }
        if (!IsVisible) filtered.Add(_dict);
        if (filtered.Count > 0)
            defaultConfig.Set("OFF", filtered);
        else
            defaultConfig.Remove("OFF");
    }

    /// <summary>
    /// The intent of this layer (e.g., "View", "Design").
    /// Multiple intents can be specified; returns the first or null.
    /// </summary>
    public string? Intent
    {
        get
        {
            var obj = _dict.Get("Intent");
            return obj switch
            {
                PdfName n => n.Value,
                PdfArray a when a.Count > 0 && a[0] is PdfName n2 => n2.Value,
                _ => null,
            };
        }
    }

    /// <summary>
    /// Gets the content operators (as raw bytes) belonging to this layer
    /// from the page content stream (BDC/EMC markers) or from XForm streams
    /// that reference this layer's OCG.
    /// </summary>
    public IReadOnlyList<byte[]> Contents
    {
        get
        {
            if (_page is null || Id is null)
                return Array.Empty<byte[]>();

            // First try BDC/EMC style
            var bdcContents = LayerHelper.ExtractLayerContents(_page, Id);
            if (bdcContents.Count > 0) return bdcContents;

            // Fall back to XForm /OC style
            return LayerHelper.ExtractXFormLayerContents(_page, Id, _dict);
        }
    }

    /// <summary>The layer's content as parsed operator objects (one per content
    /// stream operator across all of this layer's BDC/EMC or XForm blocks).</summary>
    internal IEnumerable<Operator> ContentOperators()
    {
        var blocks = Contents;
        if (blocks.Count == 0) yield break;
        using var ms = new MemoryStream();
        foreach (var b in blocks)
        {
            ms.Write(b, 0, b.Length);
            ms.WriteByte((byte)'\n');
        }
        foreach (var line in ContentStreamOperatorParser.ParseOperators(ms.ToArray()))
            yield return new RawOperator(line);
    }

    /// <summary>
    /// Flatten this layer — remove the BDC/EMC markers but keep the content.
    /// The layer's content becomes unconditional page content.
    /// Also removes the OCG from the document's OCProperties.
    /// </summary>
    /// <param name="cleanupContentStream">If true, also clean up OC references in XObjects.</param>
    public void Flatten(bool cleanupContentStream = false)
    {
        if (_page is null || Id is null) return;
        LayerHelper.FlattenLayer(_page, Id, _dict);
    }

    /// <summary>
    /// Delete this layer and all its content from the page.
    /// Removes the BDC/EMC blocks and their content from the page content stream,
    /// and removes the OCG from the document's OCProperties.
    /// </summary>
    public void Delete()
    {
        if (_page is null || Id is null) return;
        LayerHelper.DeleteLayer(_page, Id, _dict);
        // Remove from the owning LayerCollection so Count reflects the deletion
        _layerCollection?.Remove(this);
    }

    internal LayerCollection? _layerCollection;

    /// <summary>
    /// Save the content of this layer to a new single-page PDF document.
    /// Only the content belonging to this layer is included.
    /// </summary>
    public byte[] Save()
    {
        if (_page is null || Id is null)
            throw new InvalidOperationException("Layer is not associated with a page.");
        return LayerHelper.SaveLayerToPdf(_page, Id, _dict);
    }

    /// <summary>
    /// Save the content of this layer to a stream as a new single-page PDF.
    /// </summary>
    public void Save(Stream stream)
    {
        var bytes = Save();
        stream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Save the content of this layer to a file as a new single-page PDF.
    /// </summary>
    public void Save(string outputPath)
    {
        File.WriteAllBytes(outputPath, Save());
    }

    /// <summary>
    /// Writable operator list for new layers.
    /// Use <see cref="AddContent(Operator)"/> to add operators.
    /// </summary>
    private List<Operator>? _pendingOperators;

    /// <summary>Add a content operator to this layer (for newly created layers).</summary>
    public void AddContent(Operator op)
    {
        _pendingOperators ??= [];
        _pendingOperators.Add(op);
    }

    /// <summary>Get the pending operators (for newly created layers).</summary>
    internal IReadOnlyList<Operator>? PendingOperators => _pendingOperators;

    /// <summary>Whether this is a newly created layer (not yet written to a page).</summary>
    internal bool IsNew => _pendingOperators is not null;

    private bool MatchesThisOcg(PdfDictionary candidate)
    {
        if (ReferenceEquals(candidate, _dict)) return true;
        if (candidate.GetName("Type") == "OCG" && Name.Length > 0)
        {
            var nameObj = candidate.Get("Name");
            return nameObj is PdfString s && s.ToText() == Name;
        }
        return false;
    }

    internal PdfDictionary Dict => _dict;
    internal Page? _page;
}

/// <summary>
/// Represents the collection of layers on a specific page.
/// </summary>
public sealed class LayerCollection : IReadOnlyList<OptionalContentGroup>
{
    private readonly List<OptionalContentGroup> _layers;

    internal LayerCollection(List<OptionalContentGroup> layers)
    {
        _layers = layers;
        // Set back-reference so Delete() can update the collection
        foreach (var l in _layers)
            l._layerCollection = this;
    }

    internal void Remove(OptionalContentGroup layer) => _layers.Remove(layer);

    /// <summary>Number of layers on this page.</summary>
    public int Count => _layers.Count;

    /// <summary>Get a layer by 0-based index.</summary>
    public OptionalContentGroup this[int index] => _layers[index];

    /// <summary>
    /// Add a new layer to this page. The layer's pending operators are injected
    /// into the page content stream wrapped in BDC/EMC markers.
    /// </summary>
    public void Add(OptionalContentGroup layer)
    {
        if (_page is null)
            throw new InvalidOperationException("LayerCollection is not associated with a page.");
        LayerHelper.AddLayerToPage(_page, layer);
        _layers.Add(layer);
    }

    internal Page? _page;

    internal void SetPage(Page page) => _page = page;

    public IEnumerator<OptionalContentGroup> GetEnumerator() => _layers.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Collection of Optional Content Groups (layers) in a document.
/// </summary>
public sealed class OptionalContentProperties
{
    private readonly OptionalContentGroup[] _groups;
    private readonly PdfDictionary _propsDict;
    private readonly PdfReader _reader;

    internal OptionalContentProperties(PdfDictionary propsDict, PdfReader reader)
    {
        _propsDict = propsDict;
        _reader = reader;

        var groups = new List<OptionalContentGroup>();

        // Parse /OCGs array
        var ocgs = reader.Resolve(propsDict.Get("OCGs")) as PdfArray;
        if (ocgs is not null)
        {
            foreach (var item in ocgs)
            {
                var dict = reader.ResolveDict(item);
                if (dict is not null)
                {
                    var g = new OptionalContentGroup(dict);
                    groups.Add(g);
                }
            }
        }

        // Parse default configuration (/D)
        var defaultConfig = reader.ResolveDict(propsDict.Get("D"));
        if (defaultConfig is not null)
        {
            // OFF groups
            var offArray = reader.Resolve(defaultConfig.Get("OFF")) as PdfArray;
            var offSet = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
            if (offArray is not null)
            {
                foreach (var item in offArray)
                {
                    var dict = reader.ResolveDict(item);
                    if (dict is not null) offSet.Add(dict);
                }
            }

            // Locked groups
            var lockedArray = reader.Resolve(defaultConfig.Get("Locked")) as PdfArray;
            var lockedSet = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
            if (lockedArray is not null)
            {
                foreach (var item in lockedArray)
                {
                    var dict = reader.ResolveDict(item);
                    if (dict is not null) lockedSet.Add(dict);
                }
            }

            foreach (var group in groups)
            {
                group.IsVisible = !offSet.Contains(group.Dict);
                group.IsLocked = lockedSet.Contains(group.Dict);
            }
        }

        _groups = groups.ToArray();

        // Set back-reference so Lock()/Unlock() can persist changes
        foreach (var g in _groups)
            g.SetOwner(this);
    }

    /// <summary>Number of layers.</summary>
    public int Count => _groups.Length;

    /// <summary>Get a layer by index.</summary>
    public OptionalContentGroup this[int index] => _groups[index];

    /// <summary>Find a layer by name.</summary>
    public OptionalContentGroup? FindByName(string name)
    {
        foreach (var g in _groups)
        {
            if (string.Equals(g.Name, name, StringComparison.Ordinal))
                return g;
        }
        return null;
    }

    /// <summary>All layer names.</summary>
    public IReadOnlyList<string> Names =>
        _groups.Select(g => g.Name).ToArray();

    /// <summary>All groups as an enumerable.</summary>
    public IEnumerable<OptionalContentGroup> Groups => _groups;

    /// <summary>
    /// Gets the presentation order of layers as specified in /D/Order.
    /// Returns the layer names in UI display order, or null if no order is specified.
    /// </summary>
    public IReadOnlyList<string>? GetDisplayOrder()
    {
        var defaultConfig = _reader.ResolveDict(_propsDict.Get("D"));
        if (defaultConfig is null) return null;

        var orderArray = _reader.Resolve(defaultConfig.Get("Order")) as PdfArray;
        if (orderArray is null || orderArray.Count == 0) return null;

        var names = new List<string>();
        CollectOrderNames(orderArray, names);
        return names;
    }

    private void CollectOrderNames(PdfArray arr, List<string> names)
    {
        foreach (var item in arr)
        {
            var resolved = _reader.Resolve(item);
            switch (resolved)
            {
                case PdfDictionary dict:
                {
                    // This is an OCG reference — find its name
                    var nameObj = dict.Get("Name");
                    var name = nameObj is PdfString s ? s.ToText() : dict.GetName("Name");
                    if (name is not null) names.Add(name);
                    break;
                }
                case PdfArray nested:
                    CollectOrderNames(nested, names);
                    break;
            }
        }
    }

    /// <summary>
    /// Set the visibility of a layer by name.
    /// The change is persisted in the /D (default config) dictionary.
    /// </summary>
    public bool SetVisibility(string name, bool visible)
    {
        var group = FindByName(name);
        if (group is null) return false;

        group.IsVisible = visible;
        UpdateDefaultConfig();
        return true;
    }

    /// <summary>
    /// Remove an OCG from the document-level OCProperties (OCGs array, D/Order, D/OFF, D/Locked).
    /// </summary>
    internal void RemoveOcg(PdfDictionary ocgDict)
    {
        // Remove from /OCGs array
        var ocgsArray = _reader.Resolve(_propsDict.Get("OCGs")) as PdfArray;
        if (ocgsArray is not null)
        {
            var newOcgs = new PdfArray();
            foreach (var item in ocgsArray)
            {
                var dict = _reader.ResolveDict(item);
                if (dict is not null && ReferenceEquals(dict, ocgDict))
                    continue;
                newOcgs.Add(item);
            }
            _propsDict.Set("OCGs", newOcgs);
        }

        // Remove from /D/Order, /D/OFF, /D/Locked
        var defaultConfig = _reader.ResolveDict(_propsDict.Get("D"));
        if (defaultConfig is not null)
        {
            RemoveFromArray(defaultConfig, "Order", ocgDict);
            RemoveFromArray(defaultConfig, "ON", ocgDict);
            RemoveFromArray(defaultConfig, "OFF", ocgDict);
            RemoveFromArray(defaultConfig, "Locked", ocgDict);
        }
    }

    private void RemoveFromArray(PdfDictionary parent, string key, PdfDictionary ocgDict)
    {
        var arr = _reader.Resolve(parent.Get(key)) as PdfArray;
        if (arr is null) return;

        var newArr = new PdfArray();
        foreach (var item in arr)
        {
            var resolved = _reader.Resolve(item);
            if (resolved is PdfDictionary d && ReferenceEquals(d, ocgDict))
                continue;
            if (resolved is PdfArray nested)
            {
                var filtered = FilterArray(nested, ocgDict);
                if (filtered.Count > 0) newArr.Add(filtered);
            }
            else
            {
                newArr.Add(item);
            }
        }
        if (newArr.Count > 0)
            parent.Set(key, newArr);
        else
            parent.Remove(key);
    }

    private PdfArray FilterArray(PdfArray arr, PdfDictionary ocgDict)
    {
        var result = new PdfArray();
        foreach (var item in arr)
        {
            var resolved = _reader.Resolve(item);
            if (resolved is PdfDictionary d && ReferenceEquals(d, ocgDict))
                continue;
            if (resolved is PdfArray nested)
            {
                var filtered = FilterArray(nested, ocgDict);
                if (filtered.Count > 0) result.Add(filtered);
            }
            else
            {
                result.Add(item);
            }
        }
        return result;
    }

    private PdfDictionary GetOrCreateDefaultConfig()
    {
        var defaultConfig = _reader.ResolveDict(_propsDict.Get("D"));
        if (defaultConfig is null)
        {
            defaultConfig = new PdfDictionary();
            _propsDict.Set("D", defaultConfig);
        }
        return defaultConfig;
    }

    internal void UpdateDefaultConfig()
    {
        var defaultConfig = GetOrCreateDefaultConfig();

        // Rebuild /OFF array with hidden groups
        var offArray = new PdfArray();
        var ocgsArray = _reader.Resolve(_propsDict.Get("OCGs")) as PdfArray;
        if (ocgsArray is not null)
        {
            for (var i = 0; i < _groups.Length && i < ocgsArray.Count; i++)
            {
                if (!_groups[i].IsVisible)
                    offArray.Add(ocgsArray[i]);
            }
        }

        if (offArray.Count > 0)
            defaultConfig.Set("OFF", offArray);
        else
            defaultConfig.Remove("OFF");
    }

    internal void UpdateLockedState()
    {
        var defaultConfig = GetOrCreateDefaultConfig();
        var ocgsArray = _reader.Resolve(_propsDict.Get("OCGs")) as PdfArray;

        // Rebuild /Locked array with locked groups
        var lockedArray = new PdfArray();
        if (ocgsArray is not null)
        {
            for (var i = 0; i < _groups.Length && i < ocgsArray.Count; i++)
            {
                if (_groups[i].IsLocked)
                    lockedArray.Add(ocgsArray[i]);
            }
        }

        if (lockedArray.Count > 0)
            defaultConfig.Set("Locked", lockedArray);
        else
            defaultConfig.Remove("Locked");
    }

    internal PdfDictionary PropsDict => _propsDict;
    internal PdfReader Reader => _reader;
}

/// <summary>
/// Helper for layer content stream operations (delete, flatten, extract).
/// </summary>
internal static class LayerHelper
{
    /// <summary>
    /// Get the page content stream as a single byte array.
    /// </summary>
    internal static byte[] GetPageContentBytes(Page page)
    {
        var reader = page.Reader;
        var contents = reader.Resolve(page.Dict.Get("Contents"));

        if (contents is PdfStream stream)
            return reader.DecodeStream(stream);

        if (contents is PdfArray arr)
        {
            using var ms = new MemoryStream();
            foreach (var item in arr)
            {
                var s = reader.ResolveStream(item);
                if (s is not null)
                {
                    var data = reader.DecodeStream(s);
                    ms.Write(data, 0, data.Length);
                    ms.WriteByte((byte)'\n');
                }
            }
            return ms.ToArray();
        }

        return Array.Empty<byte>();
    }

    /// <summary>
    /// Extract content bytes from XForm objects that reference the given OCG dict.
    /// </summary>
    internal static IReadOnlyList<byte[]> ExtractXFormLayerContents(Page page, string layerId, PdfDictionary ocgDict)
    {
        var reader = page.Reader;
        var resources = reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null) return Array.Empty<byte[]>();

        var xobjects = reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is null) return Array.Empty<byte[]>();

        // Get OCG name for fallback comparison
        var ocgName = GetOcgName(ocgDict);

        var results = new List<byte[]>();
        foreach (var key in xobjects.Keys)
        {
            var xobj = reader.ResolveStream(xobjects.Get(key));
            if (xobj is null) continue;

            var ocRef = xobj.Dict.Get("OC");
            if (ocRef is null) continue;

            if (MatchesOcg(reader, ocRef, ocgDict, ocgName))
            {
                var data = reader.DecodeStream(xobj);
                if (data.Length > 0)
                {
                    // Split into individual operator lines to match .NET behavior
                    SplitContentIntoOperators(data, results);
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Split a content stream into individual operator lines.
    /// Each non-empty line (trimmed) becomes a separate byte[] entry.
    /// </summary>
    private static void SplitContentIntoOperators(byte[] data, List<byte[]> results)
    {
        var text = Encoding.Latin1.GetString(data);
        var lines = text.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim('\r', ' ', '\t');
            if (trimmed.Length > 0)
                results.Add(Encoding.Latin1.GetBytes(trimmed));
        }
    }

    /// <summary>
    /// Check if an /OC reference points to the given OCG dict (directly, via OCMD, or by name).
    /// </summary>
    private static bool MatchesOcg(PdfReader reader, PdfObject ocRef, PdfDictionary ocgDict, string? ocgName)
    {
        var ocDict = reader.ResolveDict(ocRef);
        if (ocDict is null) return false;

        // Direct match by reference
        if (ReferenceEquals(ocDict, ocgDict)) return true;

        // Match by OCG Name
        var resolvedName = GetOcgName(ocDict);
        if (ocgName is not null && resolvedName == ocgName) return true;

        // OCMD: check /OCGs
        var ocmdOcgs = reader.Resolve(ocDict.Get("OCGs"));
        if (ocmdOcgs is PdfDictionary singleOcg)
        {
            if (ReferenceEquals(singleOcg, ocgDict)) return true;
            if (ocgName is not null && GetOcgName(singleOcg) == ocgName) return true;
        }
        else if (ocmdOcgs is PdfArray arr)
        {
            foreach (var item in arr)
            {
                var d = reader.ResolveDict(item);
                if (d is null) continue;
                if (ReferenceEquals(d, ocgDict)) return true;
                if (ocgName is not null && GetOcgName(d) == ocgName) return true;
            }
        }

        return false;
    }

    private static string? GetOcgName(PdfDictionary dict)
    {
        var obj = dict.Get("Name");
        if (obj is PdfString s) return s.ToText();
        return dict.GetName("Name");
    }

    /// <summary>
    /// Extract content bytes that belong to a specific layer (between /OC /{id} BDC … EMC).
    /// </summary>
    internal static IReadOnlyList<byte[]> ExtractLayerContents(Page page, string layerId)
    {
        var contentBytes = GetPageContentBytes(page);
        var text = Encoding.Latin1.GetString(contentBytes);
        var results = new List<byte[]>();

        // Find all /OC /{layerId} BDC … EMC blocks
        var pattern = $@"/OC\s+/{Regex.Escape(layerId)}\s+BDC\b";
        var matches = Regex.Matches(text, pattern);

        foreach (Match m in matches)
        {
            var start = m.Index + m.Length;
            var depth = 1;
            var pos = start;

            while (pos < text.Length && depth > 0)
            {
                // Find next BDC or EMC
                var bdcIdx = FindOperator(text, "BDC", pos);
                var emcIdx = FindOperator(text, "EMC", pos);

                if (emcIdx < 0) break; // malformed

                if (bdcIdx >= 0 && bdcIdx < emcIdx)
                {
                    depth++;
                    pos = bdcIdx + 3;
                }
                else
                {
                    depth--;
                    if (depth == 0)
                    {
                        var block = text.Substring(start, emcIdx - start).Trim();
                        if (block.Length > 0)
                            results.Add(Encoding.Latin1.GetBytes(block));
                    }
                    pos = emcIdx + 3;
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Flatten a layer — remove BDC/EMC markers but keep the content.
    /// Also removes the OCG from document-level OCProperties.
    /// </summary>
    internal static void FlattenLayer(Page page, string layerId, PdfDictionary ocgDict)
    {
        var contentBytes = GetPageContentBytes(page);
        var text = Encoding.Latin1.GetString(contentBytes);

        // Remove /OC /{layerId} BDC markers and their corresponding EMC
        var result = RemoveLayerMarkers(text, layerId, keepContent: true);

        page.SetContentStream(Encoding.Latin1.GetBytes(result));

        // Remove the OCG property from page resources
        RemovePropertyFromResources(page, layerId);

        // Remove OCG from document-level OCProperties
        RemoveOcgFromDocument(page, ocgDict);

        // Clean up /OC refs from XObjects
        CleanupXObjectOcRefs(page);
    }

    /// <summary>
    /// Delete a layer — remove BDC/EMC blocks AND their content.
    /// Also removes the OCG from document-level OCProperties.
    /// </summary>
    internal static void DeleteLayer(Page page, string layerId, PdfDictionary ocgDict)
    {
        var contentBytes = GetPageContentBytes(page);
        var text = Encoding.Latin1.GetString(contentBytes);

        // Remove /OC /{layerId} BDC … EMC blocks entirely
        var result = RemoveLayerMarkers(text, layerId, keepContent: false);

        page.SetContentStream(Encoding.Latin1.GetBytes(result));

        // Remove the OCG property from page resources
        RemovePropertyFromResources(page, layerId);

        // XForm-style layer (/OC on a Form XObject): remove the form's Do
        // invocations and the XObject entries so neither the content nor the
        // OCG reference survives the save.
        DeleteXFormLayer(page, ocgDict);

        // Remove OCG from document-level OCProperties
        RemoveOcgFromDocument(page, ocgDict);
    }

    /// <summary>Delete an XForm-level layer: drop the Do invocations of every Form
    /// XObject whose /OC matches <paramref name="ocgDict"/> from the page content,
    /// then remove those XObject resource entries. No-op for BDC-style layers.</summary>
    private static void DeleteXFormLayer(Page page, PdfDictionary ocgDict)
    {
        var reader = page.Reader;
        var resources = reader.ResolveDict(page.Dict.Get("Resources"));
        var xobjects = resources is null ? null : reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is null) return;

        var ocgName = GetOcgName(ocgDict);
        var toRemove = new List<string>();
        foreach (var key in xobjects.Keys)
        {
            var xobj = reader.ResolveStream(xobjects.Get(key));
            var ocRef = xobj?.Dict.Get("OC");
            if (ocRef is null) continue;
            if (MatchesOcg(reader, ocRef, ocgDict, ocgName))
                toRemove.Add(key);
        }
        if (toRemove.Count == 0) return;

        var text = Encoding.Latin1.GetString(GetPageContentBytes(page));
        foreach (var name in toRemove)
            text = System.Text.RegularExpressions.Regex.Replace(
                text, "/" + System.Text.RegularExpressions.Regex.Escape(name) + @"\s+Do\b", " ");
        page.SetContentStream(Encoding.Latin1.GetBytes(text));

        foreach (var name in toRemove)
            xobjects.Remove(name);
    }

    /// <summary>
    /// Save the layer content as a new single-page PDF with only that layer's content.
    /// </summary>
    internal static byte[] SaveLayerToPdf(Page page, string layerId)
    {
        return SaveLayerToPdf(page, layerId, null);
    }

    internal static byte[] SaveLayerToPdf(Page page, string layerId, PdfDictionary? ocgDict)
    {
        // Try BDC/EMC style first, then XForm /OC style
        var contents = ExtractLayerContents(page, layerId);
        bool isXFormLayer = false;
        if (contents.Count == 0 && ocgDict is not null)
        {
            contents = ExtractXFormLayerContents(page, layerId, ocgDict);
            isXFormLayer = contents.Count > 0;
        }
        if (contents.Count == 0)
            return Document.Create().ToArray();

        // Create a new document with a page matching source dimensions
        var mediaBox = page.MediaBox;
        var doc = Document.Create();
        var newPage = doc.Pages.Add(mediaBox.Width, mediaBox.Height);

        // Combine all content blocks
        using var ms = new MemoryStream();
        foreach (var block in contents)
        {
            ms.Write(block, 0, block.Length);
            ms.WriteByte((byte)'\n');
        }

        // Copy relevant resources — from XForm's own Resources for XForm layers,
        // or from the page's Resources for BDC-style layers
        if (isXFormLayer && ocgDict is not null)
            CopyXFormLayerResources(page, newPage, ocgDict);
        else
            CopyLayerResources(page, newPage, ms.ToArray());

        newPage.SetContentStream(ms.ToArray());
        return doc.ToArray();
    }

    /// <summary>
    /// Merge all layers on a page into a single layer with the given name.
    /// </summary>
    internal static void MergeLayersOnPage(Page page, string newLayerName, PdfReader reader)
    {
        var layers = GetPageLayers(page, reader);
        if (layers.Count == 0) return;

        // Flatten all existing layers (remove BDC/EMC markers, keep content)
        var contentBytes = GetPageContentBytes(page);
        var text = Encoding.Latin1.GetString(contentBytes);

        foreach (var layer in layers)
        {
            if (layer.Id is not null)
            {
                text = RemoveLayerMarkers(text, layer.Id, keepContent: true);
                RemovePropertyFromResources(page, layer.Id);
                RemoveOcgFromDocument(page, layer.Dict);
            }
        }

        // Clean up /OC refs from XObjects
        CleanupXObjectOcRefs(page);

        // Create a new OCG for the merged layer
        var ocgDict = new PdfDictionary();
        ocgDict.Set("Type", new PdfName("OCG"));
        ocgDict.Set("Name", new PdfString(Encoding.Latin1.GetBytes(newLayerName)));

        // Register in page Resources/Properties
        var resources = reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            page.Dict.Set("Resources", resources);
        }
        var props = reader.ResolveDict(resources.Get("Properties"));
        if (props is null)
        {
            props = new PdfDictionary();
            resources.Set("Properties", props);
        }
        var propName = "MC0";
        props.Set(propName, ocgDict);

        // Wrap all content in new BDC/EMC
        var wrappedContent = $"/OC /{propName} BDC\n{text.Trim()}\nEMC\n";
        page.SetContentStream(Encoding.Latin1.GetBytes(wrappedContent));

        // Add OCG to document OCProperties
        var catalog = reader.Catalog;
        var ocPropsDict = reader.ResolveDict(catalog.Get("OCProperties"));
        if (ocPropsDict is null)
        {
            ocPropsDict = new PdfDictionary();
            catalog.Set("OCProperties", ocPropsDict);
        }
        var ocgsArr = reader.Resolve(ocPropsDict.Get("OCGs")) as PdfArray ?? new PdfArray();
        ocgsArr.Add(ocgDict);
        ocPropsDict.Set("OCGs", ocgsArr);

        var dConfig = reader.ResolveDict(ocPropsDict.Get("D"));
        if (dConfig is null)
        {
            dConfig = new PdfDictionary();
            ocPropsDict.Set("D", dConfig);
        }
        var orderArr = reader.Resolve(dConfig.Get("Order")) as PdfArray ?? new PdfArray();
        orderArr.Add(ocgDict);
        dConfig.Set("Order", orderArr);
    }

    /// <summary>
    /// Get layers on a specific page by inspecting Resources/Properties for OCG references,
    /// and also XForm /OC references in the page's XObject resources.
    /// </summary>
    internal static List<OptionalContentGroup> GetPageLayers(Page page, PdfReader reader)
    {
        var result = new List<OptionalContentGroup>();

        // Get document-level OCG properties so layers can persist state changes.
        // Build a lookup from OCG dict → existing group instance so that changes
        // to a page layer's DefaultState propagate to the document-level group.
        var ocPropsDict = reader.ResolveDict(reader.Catalog.Get("OCProperties"));
        var ocProps = ocPropsDict is not null ? new OptionalContentProperties(ocPropsDict, reader) : null;
        var ocgLookup = new Dictionary<PdfDictionary, OptionalContentGroup>(ReferenceEqualityComparer.Instance);
        if (ocProps is not null)
        {
            for (int i = 0; i < ocProps.Count; i++)
                ocgLookup[ocProps[i].Dict] = ocProps[i];
        }

        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var resources = reader.ResolveDict(page.Dict.Get("Resources"));

        // 1. Check Resources/Properties for OCG references (BDC-style layers)
        if (resources is not null)
        {
            var props = reader.ResolveDict(resources.Get("Properties"));
            if (props is not null)
            {
                foreach (var key in props.Keys)
                {
                    var propDict = reader.ResolveDict(props.Get(key));
                    if (propDict is null) continue;

                    var type = propDict.GetName("Type");
                    if (type != "OCG") continue;
                    if (!seen.Add(propDict)) continue;

                    // Reuse the document-level group instance so state changes propagate
                    OptionalContentGroup ocg;
                    if (ocgLookup.TryGetValue(propDict, out var existing))
                    {
                        ocg = existing;
                        ocg.Id = key;
                        ocg._page = page;
                    }
                    else
                    {
                        ocg = new OptionalContentGroup(propDict) { Id = key, _page = page };
                        ocg.SetRegisteredReader(reader);
                        ApplyDocLevelState(ocg, propDict, reader);
                    }
                    result.Add(ocg);
                }
            }

            // 2. Check XObject resources for /OC references (XForm-level layers)
            var xobjects = reader.ResolveDict(resources.Get("XObject"));
            if (xobjects is not null)
            {
                foreach (var key in xobjects.Keys)
                {
                    var xobj = reader.ResolveStream(xobjects.Get(key));
                    if (xobj is null) continue;

                    var ocRef = xobj.Dict.Get("OC");
                    if (ocRef is null) continue;

                    var ocDict = reader.ResolveDict(ocRef);
                    if (ocDict is null) continue;

                    // /OC can point directly to an OCG dict or to an OCMD
                    var ocType = ocDict.GetName("Type");
                    PdfDictionary? actualOcgDict = null;
                    string? propId = null;

                    if (ocType == "OCG")
                    {
                        actualOcgDict = ocDict;
                    }
                    else if (ocType == "OCMD")
                    {
                        // OCMD — resolve the first OCG in its /OCGs
                        var ocmdOcgs = reader.Resolve(ocDict.Get("OCGs"));
                        if (ocmdOcgs is PdfArray arr && arr.Count > 0)
                            actualOcgDict = reader.ResolveDict(arr[0]);
                        else if (ocmdOcgs is PdfDictionary d)
                            actualOcgDict = d;
                    }
                    else
                    {
                        // No /Type — assume it's an OCG
                        actualOcgDict = ocDict;
                    }

                    if (actualOcgDict is null) continue;

                    // Don't dedup XObject-level layers — each XObject with /OC
                    // is a separate layer entry (matches the public behavior).
                    propId = key;
                    // XObject layers: always create new instances since multiple
                    // XObjects may share the same OCG but need separate Id/page refs.
                    // Copy visibility state from the document-level group if available.
                    var ocg = new OptionalContentGroup(actualOcgDict) { Id = propId, _page = page };
                    ocg.SetRegisteredReader(reader);
                    if (ocgLookup.TryGetValue(actualOcgDict, out var docGroup))
                    {
                        ocg.IsVisible = docGroup.IsVisible;
                        ocg.IsLocked = docGroup.IsLocked;
                        ocg.SetOwner(ocProps!);
                        ocg._docTwin = docGroup;
                    }
                    else
                    {
                        ApplyDocLevelState(ocg, actualOcgDict, reader);
                    }
                    result.Add(ocg);
                }

            }
        }

        return result;
    }

    private static void ApplyDocLevelState(OptionalContentGroup ocg, PdfDictionary ocgDict, PdfReader reader)
    {
        var ocPropsDict = reader.ResolveDict(reader.Catalog.Get("OCProperties"));
        if (ocPropsDict is null) return;

        var dConfig = reader.ResolveDict(ocPropsDict.Get("D"));
        if (dConfig is null) return;

        var ocgName = ocg.Name;

        var offArray = reader.Resolve(dConfig.Get("OFF")) as PdfArray;
        if (offArray is not null)
        {
            foreach (var item in offArray)
            {
                var d = reader.ResolveDict(item);
                if (d is not null && MatchesOcg(d, ocgDict, ocgName))
                    ocg.IsVisible = false;
            }
        }

        var lockedArray = reader.Resolve(dConfig.Get("Locked")) as PdfArray;
        if (lockedArray is not null)
        {
            foreach (var item in lockedArray)
            {
                var d = reader.ResolveDict(item);
                if (d is not null && MatchesOcg(d, ocgDict, ocgName))
                    ocg.IsLocked = true;
            }
        }
    }

    private static bool MatchesOcg(PdfDictionary candidate, PdfDictionary ocgDict, string ocgName)
    {
        if (ReferenceEquals(candidate, ocgDict)) return true;
        // Fallback: compare by /Name for OCG dicts that were inlined during save
        if (candidate.GetName("Type") == "OCG" && ocgName.Length > 0)
        {
            var nameObj = candidate.Get("Name");
            var candidateName = nameObj is PdfString s ? s.ToText() : "";
            return candidateName == ocgName;
        }
        return false;
    }

    private static string RemoveLayerMarkers(string text, string layerId, bool keepContent)
    {
        var sb = new StringBuilder(text.Length);
        var pattern = $@"/OC\s+/{Regex.Escape(layerId)}\s+BDC\b";
        int lastEnd = 0;

        var matches = Regex.Matches(text, pattern);
        foreach (Match m in matches)
        {
            // Add text before this BDC
            sb.Append(text, lastEnd, m.Index - lastEnd);

            // Find matching EMC
            var start = m.Index + m.Length;
            var depth = 1;
            var pos = start;
            var emcEnd = -1;

            while (pos < text.Length && depth > 0)
            {
                var bdcIdx = FindOperator(text, "BDC", pos);
                var emcIdx = FindOperator(text, "EMC", pos);

                if (emcIdx < 0) { emcEnd = text.Length; break; }

                if (bdcIdx >= 0 && bdcIdx < emcIdx)
                {
                    depth++;
                    pos = bdcIdx + 3;
                }
                else
                {
                    depth--;
                    if (depth == 0)
                    {
                        emcEnd = emcIdx + 3;
                        if (keepContent)
                        {
                            var content = text.Substring(start, emcIdx - start).Trim();
                            if (content.Length > 0)
                            {
                                sb.Append(content);
                                sb.Append('\n');
                            }
                        }
                    }
                    pos = emcIdx + 3;
                }
            }

            lastEnd = emcEnd >= 0 ? emcEnd : text.Length;
        }

        // Add remaining text
        if (lastEnd < text.Length)
            sb.Append(text, lastEnd, text.Length - lastEnd);

        return sb.ToString();
    }

    private static int FindOperator(string text, string op, int startPos)
    {
        var idx = startPos;
        while (idx < text.Length)
        {
            idx = text.IndexOf(op, idx, StringComparison.Ordinal);
            if (idx < 0) return -1;

            // Verify it's a standalone operator (not part of another word)
            bool validBefore = idx == 0 || !char.IsLetterOrDigit(text[idx - 1]);
            bool validAfter = idx + op.Length >= text.Length ||
                              !char.IsLetterOrDigit(text[idx + op.Length]);

            if (validBefore && validAfter) return idx;
            idx += op.Length;
        }
        return -1;
    }

    private static void RemovePropertyFromResources(Page page, string layerId)
    {
        var resources = page.Reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null) return;

        var props = page.Reader.ResolveDict(resources.Get("Properties"));
        if (props is null) return;

        props.Remove(layerId);

        // If Properties is now empty, remove it
        if (!props.Keys.Any())
            resources.Remove("Properties");
    }

    private static void RemoveOcgFromDocument(Page page, PdfDictionary ocgDict)
    {
        var reader = page.Reader;
        var catalog = reader.Catalog;
        var ocPropsDict = reader.ResolveDict(catalog.Get("OCProperties"));
        if (ocPropsDict is null) return;

        var ocProps = new OptionalContentProperties(ocPropsDict, reader);
        ocProps.RemoveOcg(ocgDict);
    }

    private static void CleanupXObjectOcRefs(Page page)
    {
        var reader = page.Reader;
        var resources = reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null) return;

        var xobjects = reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is null) return;

        foreach (var key in xobjects.Keys)
        {
            var xobj = reader.ResolveStream(xobjects.Get(key));
            if (xobj is not null)
                xobj.Dict.Remove("OC");
        }
    }

    /// <summary>
    /// Copy resources from an XForm's own Resources dict to the target page.
    /// Used when saving an XForm-level layer to a standalone PDF.
    /// </summary>
    private static void CopyXFormLayerResources(Page source, Page target, PdfDictionary ocgDict)
    {
        var reader = source.Reader;
        var pageResources = reader.ResolveDict(source.Dict.Get("Resources"));
        if (pageResources is null) return;

        var xobjects = reader.ResolveDict(pageResources.Get("XObject"));
        if (xobjects is null) return;

        // Find the first XForm that matches the OCG and copy its Resources
        var ocgName = GetOcgName(ocgDict);
        foreach (var key in xobjects.Keys)
        {
            var xobj = reader.ResolveStream(xobjects.Get(key));
            if (xobj is null) continue;
            var ocRef = xobj.Dict.Get("OC");
            if (ocRef is null) continue;
            if (!MatchesOcg(reader, ocRef, ocgDict, ocgName)) continue;

            var xformRes = reader.ResolveDict(xobj.Dict.Get("Resources"));
            if (xformRes is not null)
            {
                target.Dict.Set("Resources", xformRes);
                return;
            }
        }

        // Fallback: copy page resources
        CopyLayerResources(source, target, Array.Empty<byte>());
    }

    private static void CopyLayerResources(Page source, Page target, byte[] contentBytes)
    {
        var reader = source.Reader;
        var srcResources = reader.ResolveDict(source.Dict.Get("Resources"));
        if (srcResources is null) return;

        // Copy Font and XObject resources
        var targetResources = new PdfDictionary();

        var srcFonts = reader.ResolveDict(srcResources.Get("Font"));
        if (srcFonts is not null)
            targetResources.Set("Font", srcFonts);

        var srcXObjects = reader.ResolveDict(srcResources.Get("XObject"));
        if (srcXObjects is not null)
        {
            // Clone XObjects without /OC refs
            var newXObjects = new PdfDictionary();
            foreach (var key in srcXObjects.Keys)
            {
                newXObjects.Set(key, srcXObjects.Get(key)!);
            }
            targetResources.Set("XObject", newXObjects);
        }

        var srcExtGState = reader.ResolveDict(srcResources.Get("ExtGState"));
        if (srcExtGState is not null)
            targetResources.Set("ExtGState", srcExtGState);

        var srcColorSpace = reader.ResolveDict(srcResources.Get("ColorSpace"));
        if (srcColorSpace is not null)
            targetResources.Set("ColorSpace", srcColorSpace);

        target.Dict.Set("Resources", targetResources);
    }

    /// <summary>
    /// Add a new layer to a page: register the OCG, inject BDC/EMC content.
    /// </summary>
    internal static void AddLayerToPage(Page page, OptionalContentGroup layer)
    {
        var reader = page.Reader;
        var pageDict = page.Dict;

        // 1. Ensure Resources/Properties exists
        var resources = reader.ResolveDict(pageDict.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            pageDict.Set("Resources", resources);
        }
        var properties = reader.ResolveDict(resources.Get("Properties"));
        if (properties is null)
        {
            properties = new PdfDictionary();
            resources.Set("Properties", properties);
        }

        // 2. Assign a property name (MC0, MC1, .) for the OCG on this page
        var propName = layer.Id ?? "MC0";
        int counter = 0;
        while (properties.ContainsKey(propName))
            propName = $"MC{++counter}";
        layer.Id = propName;
        properties.Set(propName, layer.Dict);

        // 3. Register OCG in document OCProperties
        RegisterOcgInDocument(page, layer);

        // 4. Build content bytes from pending operators
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"/OC /{propName} BDC");
        if (layer.PendingOperators is not null)
        {
            foreach (var op in layer.PendingOperators)
                sb.AppendLine(op.ToPdf());
        }
        sb.AppendLine("EMC");
        var layerBytes = System.Text.Encoding.ASCII.GetBytes(sb.ToString());

        // 5. Append to page content stream
        var existing = reader.Resolve(pageDict.Get("Contents"));
        byte[] existingData;
        if (existing is PdfStream es)
            existingData = reader.DecodeStream(es);
        else if (existing is PdfArray arr)
        {
            using var ms = new System.IO.MemoryStream();
            foreach (var item in arr)
            {
                var s = reader.ResolveStream(item);
                if (s is not null)
                {
                    var d = reader.DecodeStream(s);
                    ms.Write(d, 0, d.Length);
                    ms.WriteByte((byte)'\n');
                }
            }
            existingData = ms.ToArray();
        }
        else
            existingData = [];

        var combined = new byte[existingData.Length + 1 + layerBytes.Length];
        existingData.CopyTo(combined, 0);
        if (existingData.Length > 0) combined[existingData.Length] = (byte)'\n';
        layerBytes.CopyTo(combined, existingData.Length + (existingData.Length > 0 ? 1 : 0));
        pageDict.Set("Contents", new PdfStream(new PdfDictionary(), combined));

        layer._page = page;
    }

    private static void RegisterOcgInDocument(Page page, OptionalContentGroup layer)
    {
        var reader = page.Reader;
        var catalog = reader.Catalog;

        // Get or create OCProperties
        var ocProps = reader.ResolveDict(catalog.Get("OCProperties"));
        if (ocProps is null)
        {
            ocProps = new PdfDictionary();
            catalog.Set("OCProperties", ocProps);
        }

        layer.SetRegisteredReader(reader);

        // Add to /OCGs array
        var ocgs = reader.Resolve(ocProps.Get("OCGs")) as PdfArray;
        if (ocgs is null)
        {
            ocgs = new PdfArray();
            ocProps.Set("OCGs", ocgs);
        }
        ocgs.Add(layer.Dict);

        // Ensure /D (default config) exists with /Order
        var defaultConfig = reader.ResolveDict(ocProps.Get("D"));
        if (defaultConfig is null)
        {
            defaultConfig = new PdfDictionary();
            defaultConfig.Set("Name", new PdfString(System.Text.Encoding.Latin1.GetBytes("Default")));
            ocProps.Set("D", defaultConfig);
        }

        // Add to /Order array (controls display in viewers)
        var order = reader.Resolve(defaultConfig.Get("Order")) as PdfArray;
        if (order is null)
        {
            order = new PdfArray();
            defaultConfig.Set("Order", order);
        }
        order.Add(layer.Dict);

        // Persist lock state if locked
        if (layer.IsLocked)
        {
            var locked = reader.Resolve(defaultConfig.Get("Locked")) as PdfArray;
            if (locked is null)
            {
                locked = new PdfArray();
                defaultConfig.Set("Locked", locked);
            }
            locked.Add(layer.Dict);
        }

        // Persist visibility state
        if (!layer.IsVisible)
        {
            var off = reader.Resolve(defaultConfig.Get("OFF")) as PdfArray;
            if (off is null)
            {
                off = new PdfArray();
                defaultConfig.Set("OFF", off);
            }
            off.Add(layer.Dict);
        }
    }
}

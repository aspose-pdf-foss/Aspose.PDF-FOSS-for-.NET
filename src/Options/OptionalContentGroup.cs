using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

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

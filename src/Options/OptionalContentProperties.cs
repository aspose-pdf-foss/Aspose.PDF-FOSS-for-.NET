using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

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

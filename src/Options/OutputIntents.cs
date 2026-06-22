using System.Collections;
using System.Collections.Generic;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

/// <summary>Collection of <see cref="OutputIntent"/> entries on the
/// document catalog's /OutputIntents array. Reads happen on demand;
/// mutations write back to the catalog so they survive a subsequent
/// <see cref="Document.ToArray()"/>.</summary>
public sealed class OutputIntents : IEnumerable<OutputIntent>
{
    private readonly Document _document;

    internal OutputIntents(Document document)
    {
        _document = document;
    }

    public int Count
    {
        get
        {
            var arr = GetArray(create: false);
            return arr?.Count ?? 0;
        }
    }

    public bool IsReadOnly => false;

    public OutputIntent this[int index]
    {
        get
        {
            var arr = GetArray(create: false);
            if (arr is null || index < 0 || index >= arr.Count)
                throw new System.ArgumentOutOfRangeException(nameof(index));
            var dict = _document.Reader.ResolveDict(arr[index]);
            return DictToOutputIntent(dict);
        }
    }

    public void Add(OutputIntent item)
    {
        if (item is null) return;
        var arr = GetArray(create: true)!;
        arr.Add(OutputIntentToDict(item));
    }

    public void Clear()
    {
        _document.Reader.Catalog.Remove("OutputIntents");
    }

    public bool Contains(OutputIntent item)
    {
        if (item is null) return false;
        foreach (var existing in this)
            if (Equals(existing, item)) return true;
        return false;
    }

    public void CopyTo(OutputIntent[] array, int arrayIndex)
    {
        if (array is null) throw new System.ArgumentNullException(nameof(array));
        var i = arrayIndex;
        foreach (var item in this)
            array[i++] = item;
    }

    public bool Remove(OutputIntent item)
    {
        if (item is null) return false;
        var arr = GetArray(create: false);
        if (arr is null) return false;
        for (var i = 0; i < arr.Count; i++)
        {
            var existing = DictToOutputIntent(_document.Reader.ResolveDict(arr[i]));
            if (!Equals(existing, item)) continue;
            arr.RemoveAt(i);
            return true;
        }
        return false;
    }

    public IEnumerator<OutputIntent> GetEnumerator()
    {
        var arr = GetArray(create: false);
        if (arr is null) yield break;
        for (var i = 0; i < arr.Count; i++)
        {
            yield return DictToOutputIntent(_document.Reader.ResolveDict(arr[i]));
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private PdfArray? GetArray(bool create)
    {
        var catalog = _document.Reader.Catalog;
        if (_document.Reader.Resolve(catalog.Get("OutputIntents")) is PdfArray existing)
            return existing;
        if (!create) return null;
        var fresh = new PdfArray();
        catalog.Set("OutputIntents", fresh);
        return fresh;
    }

    private static OutputIntent DictToOutputIntent(PdfDictionary? dict)
    {
        if (dict is null) return new OutputIntent(string.Empty, string.Empty, null, null, null, null);
        var subtype = dict.GetName("S") ?? string.Empty;
        var oci = ReadString(dict, "OutputConditionIdentifier") ?? string.Empty;
        var oc = ReadString(dict, "OutputCondition");
        var reg = ReadString(dict, "RegistryName");
        var inf = ReadString(dict, "Info");
        byte[]? profile = null;
        if (dict.Get("DestOutputProfile") is PdfStream s) profile = s.RawData;
        return new OutputIntent(subtype, oci, oc, reg, inf, profile);
    }

    private static string? ReadString(PdfDictionary dict, string key) => dict.Get(key) switch
    {
        PdfString s => s.ToText(),
        PdfName n => n.Value,
        _ => null,
    };

    private static PdfDictionary OutputIntentToDict(OutputIntent item)
    {
        var d = new PdfDictionary();
        d.Set("Type", new PdfName("OutputIntent"));
        if (!string.IsNullOrEmpty(item.Subtype)) d.Set("S", new PdfName(item.Subtype));
        if (!string.IsNullOrEmpty(item.OutputConditionIdentifier))
            d.Set("OutputConditionIdentifier", new PdfString(System.Text.Encoding.UTF8.GetBytes(item.OutputConditionIdentifier)));
        if (!string.IsNullOrEmpty(item.OutputCondition))
            d.Set("OutputCondition", new PdfString(System.Text.Encoding.UTF8.GetBytes(item.OutputCondition)));
        if (!string.IsNullOrEmpty(item.RegistryName))
            d.Set("RegistryName", new PdfString(System.Text.Encoding.UTF8.GetBytes(item.RegistryName)));
        if (!string.IsNullOrEmpty(item.Info))
            d.Set("Info", new PdfString(System.Text.Encoding.UTF8.GetBytes(item.Info)));
        if (item.DestOutputProfile is { Length: > 0 } profile)
            d.Set("DestOutputProfile", new PdfStream(new PdfDictionary(), profile));
        return d;
    }

    private static bool Equals(OutputIntent a, OutputIntent b)
    {
        if (a is null || b is null) return a is null && b is null;
        return string.Equals(a.OutputConditionIdentifier, b.OutputConditionIdentifier, System.StringComparison.Ordinal)
            && string.Equals(a.Subtype, b.Subtype, System.StringComparison.Ordinal);
    }
}
